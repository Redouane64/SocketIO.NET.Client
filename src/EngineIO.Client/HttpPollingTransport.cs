using System.Text.Json;
using System.Text.Json.Serialization;
using EngineIO.Client.Serializers;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public sealed class HttpPollingTransport : ITransport
{
    private const int Protocol = 4;
    private readonly HttpClient _client;

    private readonly ILogger<HttpPollingTransport> _logger;
    private readonly IPacketSerializer<Packet> _packetSerializer = new DefaulPacketSerializer();

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private bool _handshake;

    public int MaxPayload { get; private set; }
    public int PingInterval { get; private set; }
    public int PingTimeout { get; private set; }
    public string? Sid { get; private set; }
    public string[]? Upgrades { get; private set; }

    public HttpPollingTransport(HttpClient client, ILogger<HttpPollingTransport> logger)
    {
        _client = client;
        _logger = logger;
    }

    private string Path => $"/engine.io?EIO={Protocol}&transport={Transport}";

    public IPacketSerializer<Packet> Serializer => _packetSerializer;
    public string Transport => "polling";

    public async Task<byte[]> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!_handshake) throw new Exception("Transport is not connected");

        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            var path = string.Join('&', Path, $"sid={Sid}");
            return await _client.GetByteArrayAsync(path, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_handshake) throw new Exception("Transport is not connected");

            await _semaphore.WaitAsync(cancellationToken);
            var response = await _client.PostAsync(
                string.Join('&', Path, $"sid={Sid}"), new ByteArrayContent(data), cancellationToken);

            if (!response.IsSuccessStatusCode) throw new Exception("Unexpected response from remote server");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task Handshake(CancellationToken cancellationToken = default)
    {
        if (_handshake) return;

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            var packet = await _client.GetByteArrayAsync(Path, cancellationToken);

            if (_packetSerializer is null) throw new Exception("Packet serializer is not set");

            var serializedPacket = _packetSerializer.Deserialize(packet.AsSpan());

            if (serializedPacket.Type != "0") throw new Exception("Unexpected packet type");

            var handshake = JsonSerializer
                .Deserialize<HandshakePayload>(serializedPacket.Data!);

            if (handshake is null) throw new Exception("Invalid handshake packet");

            Sid = handshake.Sid;
            MaxPayload = handshake.MaxPayload;
            Upgrades = handshake.Upgrades;
            PingTimeout = handshake.PingTimeout;
            PingInterval = handshake.PingInterval;

            _handshake = true;
            _logger.LogDebug("Handshake completed successfully");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private class HandshakePayload
    {
        [JsonPropertyName("sid")] public required string Sid { get; set; }

        [JsonPropertyName("upgrades")] public required string[] Upgrades { get; set; }

        [JsonPropertyName("pingInterval")] public required int PingInterval { get; set; }

        [JsonPropertyName("pingTimeout")] public required int PingTimeout { get; set; }

        [JsonPropertyName("maxPayload")] public required int MaxPayload { get; set; }
    }
}