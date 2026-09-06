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
        if (_connected)
        {
            await SendAsync(Packet.ClosePacket.ToPlaintextPacket(), PacketFormat.PlainText);
        }

        _connected = false;
    }

    public async Task<ReadOnlyCollection<ReadOnlyMemory<byte>>> GetAsync(CancellationToken cancellationToken = default)
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

        var packets = new List<ReadOnlyMemory<byte>>();

        var start = 0;
        for (var index = start; index < data.Length; index++)
        {
            if (data[index] == _separator)
            {
                var payload = new ReadOnlyMemory<byte>(data, start, index - start);
                packets.Add(payload);
                start = index + 1;
            }
        }

        if (start < data.Length)
        {
            var payload = new ReadOnlyMemory<byte>(data, start, data.Length - start);
            packets.Add(payload);
        }

        return new ReadOnlyCollection<ReadOnlyMemory<byte>>(packets);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> packets, PacketFormat format,
        CancellationToken cancellationToken = default)
    {
        await _postSemaphore.WaitAsync(cancellationToken);

        try
        {
            using var content = new ReadOnlyMemoryContent(packets);

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

        ReadOnlyCollection<ReadOnlyMemory<byte>> response = await GetAsync(cancellationToken);

        if (!Packet.TryParse(response[0], out var packet))
        {
            throw new TransportException(ErrorReason.InvalidPacket);
        }

        if (packet.Type != PacketType.Open)
        {
            throw new TransportException(ErrorReason.InvalidPacket);
        }

        var handshake = JsonSerializer
            .Deserialize<HandshakePacket>(packet.Body.Span)!;

        Sid = handshake.Sid;
        MaxPayload = handshake.MaxPayload;
        PingInterval = handshake.PingInterval;
        PingTimeout = handshake.PingTimeout;
        Upgrades = handshake.Upgrades;

        Path += $"&sid={Sid}";
        _connected = true;
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