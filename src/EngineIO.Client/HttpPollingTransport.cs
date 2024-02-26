using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public sealed class HttpPollingTransport : ITransport
{
    private const int Protocol = 4;

    private static readonly string _transport = "polling";

    private readonly HttpClient _client;
    private readonly ILogger _logger;
    private readonly IPacketParser _packetParser = new DefaultPacketParser();

    private readonly string _path =
        $"/engine.io?EIO={Protocol}&transport={_transport}";

    private readonly SemaphoreSlim _semaphore = new(2, 2);

    private bool _handshake;

    public HttpPollingTransport(HttpClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
    }

    public int MaxPayload { get; private set; }
    public int PingInterval { get; private set; }
    public int PingTimeout { get; private set; }
    public string? Sid { get; private set; }
    public string[]? Upgrades { get; private set; }
    public string Transport => _transport;

    public async Task<IReadOnlyCollection<Packet>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_handshake) throw new Exception("Transport is not connected");
        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            var path = string.Join('&', _path, $"sid={Sid}");
            var data = await _client.GetByteArrayAsync(path, cancellationToken);
            return _packetParser.Parse(data);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SendAsync(Packet packet,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_handshake) throw new Exception("Transport is not connected");
            await _semaphore.WaitAsync(cancellationToken);

            var content = new ByteArrayContent(packet.Data);
            var response = await _client.PostAsync(
                string.Join('&', _path, $"sid={Sid}"), content,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new Exception("Unexpected response from remote server");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task Handshake(CancellationToken cancellationToken = default)
    {
        if (_handshake) return;
        var data = await _client.GetByteArrayAsync(_path, cancellationToken);
        if (_packetParser is null)
            throw new Exception("Packet parser is not set");
        var packets = _packetParser.Parse(data);
        var handshakePacket = packets.First();

        if (handshakePacket.Type != PacketType.Open)
            throw new Exception("Unexpected packet type");
        var handshake = JsonSerializer
            .Deserialize<HandshakePayload>(handshakePacket.Payload);
        if (handshake is null) throw new Exception("Invalid handshake packet");
        Sid = handshake.Sid;
        MaxPayload = handshake.MaxPayload;
        Upgrades = handshake.Upgrades;
        PingTimeout = handshake.PingTimeout;
        PingInterval = handshake.PingInterval;

        _handshake = true;
        _logger.LogDebug("Handshake completed successfully");
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