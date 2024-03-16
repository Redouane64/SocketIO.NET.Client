﻿using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngineIO.Client.Packets;

namespace EngineIO.Client.Transport;

public sealed class HttpPollingTransport : ITransport, IDisposable
{
    private readonly HttpClient _httpClient;

    private readonly int _protocol = 4;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly CancellationTokenSource _pollingCancellationToken = new();
    private readonly byte _separator = 0x1E;

    private bool _connected;
    private string _path;

    public HttpPollingTransport(string baseAddress)
    {
        _httpClient = new HttpClient();

        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.ConnectionClose = false;

        _httpClient.BaseAddress = new Uri(baseAddress);
        _path = $"/engine.io?EIO={_protocol}&transport={Name}";
    }

    public string Name => "polling";

    public string? Sid { get; private set; }

    public string[]? Upgrades { get; private set; }

    public int PingInterval { get; private set; }

    public int PingTimeout { get; private set; }

    public int MaxPayload { get; private set; }

    public void Dispose()
    {
        _httpClient.Dispose();
        _semaphore.Dispose();
        _pollingCancellationToken.Dispose();
    }

    public async Task Disconnect()
    {
        await SendAsync(Packet.ClosePacket.ToPlaintextPacket(), PacketFormat.PlainText);
        await _pollingCancellationToken.CancelAsync();
    }

    public async Task<ReadOnlyCollection<ReadOnlyMemory<byte>>> GetAsync(CancellationToken cancellationToken = default)
    {
        var data = Array.Empty<byte>();
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            data = await _httpClient.GetByteArrayAsync(_path, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }

        var packets = new Collection<ReadOnlyMemory<byte>>();

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

        return packets.AsReadOnly();
    }

    public async Task SendAsync(ReadOnlyMemory<byte> packets, PacketFormat format, CancellationToken cancellationToken = default)
    {
        if (!_connected)
        {
            throw new Exception("Transport is not connected");
        }

        try
        {
            using var content = new ReadOnlyMemoryContent(packets);
            content.Headers.ContentType = format == PacketFormat.Binary
                ? new MediaTypeHeaderValue("application/octet-stream")
                : new MediaTypeHeaderValue("text/plain", "utf-8");

            await _semaphore.WaitAsync(cancellationToken);
            using var response = await _httpClient.PostAsync(_path, content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connected)
        {
            return;
        }

        var data = await GetAsync(cancellationToken);
        var packet = Packet.Parse(data[0]);

        if (packet.Type != PacketType.Open)
        {
            throw new Exception("Unexpected packet type");
        }

        var handshake = JsonSerializer
            .Deserialize<HandshakePacket>(packet.Body.Span)!;

        Sid = handshake.Sid;
        MaxPayload = handshake.MaxPayload;
        PingInterval = handshake.PingInterval;
        PingTimeout = handshake.PingTimeout;
        Upgrades = handshake.Upgrades;

        _path += $"&sid={Sid}";
        _connected = true;
    }

    private class HandshakePacket
    {
        [JsonPropertyName("sid")]
        public required string Sid { get; set; }

        [JsonPropertyName("upgrades")]
        public required string[] Upgrades { get; set; }

        [JsonPropertyName("pingInterval")]
        public required int PingInterval { get; set; }

        [JsonPropertyName("pingTimeout")]
        public required int PingTimeout { get; set; }

        [JsonPropertyName("maxPayload")]
        public required int MaxPayload { get; set; }
    }
}
