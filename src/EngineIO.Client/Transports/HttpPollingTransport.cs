using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using EngineIO.Client.Packets;
using EngineIO.Client.Transports.Exceptions;

namespace EngineIO.Client.Transports;

public sealed class HttpPollingTransport : ITransport, IDisposable
{
    private readonly IEncoder _encoder = new Base64Encoder();
    private readonly HttpClient _httpClient;

    private readonly int _protocol = 4;

    /// <summary>
    ///     Guards the long-polling GET. The protocol allows at most one in flight.
    /// </summary>
    private readonly SemaphoreSlim _getSemaphore = new(1, 1);

    /// <summary>
    ///     Guards POSTs. Kept separate from the GET so that sending a packet does
    ///     not have to wait for the in-flight long poll to return.
    /// </summary>
    private readonly SemaphoreSlim _postSemaphore = new(1, 1);

    private readonly byte _separator = 0x1E;

    private bool _connected;

    public HttpPollingTransport(string baseAddress)
    {
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(baseAddress);
        Path = $"/engine.io?EIO={_protocol}&transport={Name}";
    }

    internal HttpPollingTransport(HttpClient httpClient)
    {
        _httpClient = httpClient;
        Path = $"/engine.io?EIO={_protocol}&transport={Name}";
    }

    public string Path { get; private set; }

    public string? Sid { get; private set; }
    public string[]? Upgrades { get; private set; }
    public int PingInterval { get; private set; }
    public int PingTimeout { get; private set; }
    public int MaxPayload { get; private set; }

    public void Dispose()
    {
        _httpClient.Dispose();
        _getSemaphore.Dispose();
        _postSemaphore.Dispose();
    }

    public string Name => "polling";

    public bool Connected => _connected;

    public async Task Disconnect()
    {
        if (!_connected)
        {
            return;
        }

        // Clear the flag before awaiting, so a concurrent caller — the poll loop
        // reacting to the same shutdown — does not post a second close packet.
        _connected = false;
        await SendAsync(Packet.ClosePacket);
    }

    public async Task<ReadOnlyCollection<Packet>> GetAsync(CancellationToken cancellationToken = default)
    {
        byte[] data;
        await _getSemaphore.WaitAsync(cancellationToken);

        try
        {
            using var response = await _httpClient.GetAsync(Path, cancellationToken);
            response.EnsureSuccessStatusCode();
            data = await response.Content.ReadAsByteArrayAsync();
        }
        finally
        {
            _getSemaphore.Release();
        }

        var packets = new List<Packet>();

        var start = 0;
        for (var index = start; index < data.Length; index++)
        {
            if (data[index] == _separator)
            {
                Decode(new ReadOnlyMemory<byte>(data, start, index - start), packets);
                start = index + 1;
            }
        }

        if (start < data.Length)
        {
            Decode(new ReadOnlyMemory<byte>(data, start, data.Length - start), packets);
        }

        return new ReadOnlyCollection<Packet>(packets);
    }

    public async Task SendAsync(Packet packet, CancellationToken cancellationToken = default)
    {
        // Long-polling carries text only, so a binary packet travels base64-encoded
        // behind a 'b' prefix.
        var payload = packet.Format == PacketFormat.Binary
            ? packet.ToBinaryPacket(_encoder)
            : packet.ToPlaintextPacket();

        // The handshake advertises how much the server will accept in one request.
        if (MaxPayload > 0 && payload.Length > MaxPayload)
        {
            throw new TransportException(ErrorReason.PayloadTooLarge,
                $"Packet is {payload.Length} bytes, which exceeds the server's maxPayload of {MaxPayload} bytes.");
        }

        await _postSemaphore.WaitAsync(cancellationToken);

        try
        {
            using var content = new ReadOnlyMemoryContent(payload);

            // A polling payload is always text: binary packets travel base64-encoded
            // behind a 'b' prefix, so there is nothing to label octet-stream.
            content.Headers.ContentType =
                new MediaTypeHeaderValue("text/plain") { CharSet = Encoding.UTF8.WebName };

            using var response = await _httpClient.PostAsync(Path, content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            _postSemaphore.Release();
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connected)
        {
            return;
        }

        ReadOnlyCollection<Packet> response = await GetAsync(cancellationToken);

        if (response.Count == 0 || response[0].Type != PacketType.Open)
        {
            throw new TransportException(ErrorReason.InvalidPacket);
        }

        var handshake = JsonSerializer
            .Deserialize<HandshakePacket>(response[0].Body.Span)!;

        Sid = handshake.Sid;
        MaxPayload = handshake.MaxPayload;
        PingInterval = handshake.PingInterval;
        PingTimeout = handshake.PingTimeout;
        Upgrades = handshake.Upgrades;

        Path += $"&sid={Sid}";
        _connected = true;
    }

    private void Decode(ReadOnlyMemory<byte> payload, ICollection<Packet> packets)
    {
        if (payload.Length == 0)
        {
            return;
        }

        // A 'b' prefix marks a base64-encoded binary message packet.
        if (payload.Span[0] == (byte)'b')
        {
            packets.Add(Packet.CreateBinaryPacket(_encoder.Decode(payload[1..], Encoding.UTF8)));
            return;
        }

        if (Packet.TryParse(payload, out var packet))
        {
            packets.Add(packet);
        }
    }

    private class HandshakePacket
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("upgrades")]
        public string[]? Upgrades { get; set; }

        [JsonPropertyName("pingInterval")]
        public int PingInterval { get; set; }

        [JsonPropertyName("pingTimeout")]
        public int PingTimeout { get; set; }

        [JsonPropertyName("maxPayload")]
        public int MaxPayload { get; set; }
    }
}
