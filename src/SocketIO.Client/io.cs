using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace SocketIO.Client;

public enum MessageType : short
{
    Open = 48,
    Close = 49,
    Ping = 50,
    Pong = 3,
    Message = 52,
    Upgrade = 5,
    Noop = 6
}

public record OpenPacketPayload
{
    [JsonPropertyName("sid")] public string Sid { get; set; }

    [JsonPropertyName("upgrades")] public string[] Upgrades { get; set; }

    [JsonPropertyName("pingInterval")] public int PingInterval { get; set; }

    [JsonPropertyName("pingTimeout")] public int PingTimeout { get; set; }

    [JsonPropertyName("maxPayload")] public int MaxPayload { get; set; }
}

public class Io
{
    private const int Protocol = 4;
    private const string Transport = "polling";
    private const string Path = "/socket.io/";
    
    private readonly HttpClient _client;
    private readonly ILogger<Io> _logger;
    
    private int _maxPayload;
    private int _pingInterval;
    private int _pingTimeout;
    private string _sid;
    private string[] _upgrades;

    private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private Timer _pingTimer;

    public Io(string baseUri, ILogger<Io> logger)
    {
        _logger = logger;
        _client = new HttpClient(new SocketsHttpHandler()
        {
            ConnectTimeout = Timeout.InfiniteTimeSpan
        });
        _client.BaseAddress = new Uri(baseUri);
    }

    private string HandshakeUriPath => $"{Path}?EIO={Protocol}&transport={Transport}";
    private string SessionUriPath => $"{HandshakeUriPath}&sid={_sid}";

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // Handshake
        await Handshake(cancellationToken);

        _pingTimer = new Timer((state) =>
        {
            DoPing();
        }, null, TimeSpan.FromMilliseconds(_pingInterval), TimeSpan.FromMilliseconds(_pingInterval));
    }

    private void DoPing()
    {
        GetPacket(SessionUriPath).ContinueWith((ping, _) =>
        {
            var (type, payload) = ping.Result;
            _logger.LogDebug("Heartbeat: {type}", type);
        }, null, _cancellationTokenSource.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Current);
    }

    public async Task<string> GetData(CancellationToken cancellationToken = default)
    {
        // Get some more packet
        (MessageType type, byte[] payload) = await GetPacket(SessionUriPath, cancellationToken);

        switch (type)
        {
            case MessageType.Ping:
            case MessageType.Pong:
            case MessageType.Upgrade:
            case MessageType.Noop:
            case MessageType.Close:
                throw new NotImplementedException();

            case MessageType.Message:
                string content = Encoding.UTF8.GetString(payload);
                _logger.LogDebug("Server: {message}", content);

                return content;

            default:
                throw new Exception("Invalid response type");
        }
    }

    private async Task Handshake(CancellationToken cancellationToken = default)
    {
        (MessageType type, byte[] payload) = await GetPacket(HandshakeUriPath, cancellationToken);

        if (type != MessageType.Open)
        {
            throw new Exception("Unexpected message type");
        }

        OpenPacketPayload packet = JsonSerializer.Deserialize<OpenPacketPayload>(payload)!;
        _sid = packet.Sid;
        _pingInterval = packet.PingInterval;
        _pingTimeout = packet.PingTimeout;
        _upgrades = packet.Upgrades;
        _maxPayload = packet.MaxPayload;

        _logger.LogDebug(Encoding.UTF8.GetString(payload));
    }

    private async Task<(MessageType, Byte[])> GetPacket(string path, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response =
            await _client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        MessageType type = (MessageType)Convert.ToInt16(data[0]);
        byte[] contents = data[1..].ToArray();

        return (type, contents);
    }
}