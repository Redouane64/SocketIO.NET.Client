using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public class WebSocketTransport : ITransport, IDisposable
{
    private readonly ILogger<WebSocketTransport> _logger;
    private readonly ClientWebSocket _client;

    private readonly int _protocol = 4;
    private readonly string _transport = "websocket";
    
    private Uri _uri;
    private bool _handshake;

    public WebSocketTransport(string baseAddress, string? sid, ILogger<WebSocketTransport> logger)
    {
        if (baseAddress.StartsWith(Uri.UriSchemeHttp))
        {
            baseAddress = baseAddress.Replace("http://", "ws://");
        }
        
        if (baseAddress.StartsWith(Uri.UriSchemeHttps))
        {
            baseAddress = baseAddress.Replace("https://", "wss://");
        }

        var uri = $"{baseAddress}/engine.io?EIO={_protocol}&transport={_transport}";
        
        if (sid is not null)
        {
            uri += $"&sid={sid}";
        }
        
        _uri = new Uri(uri);
        _logger = logger;
        _client = new ClientWebSocket();
    }

    public string Transport => _transport;
    
    public async Task Handshake(CancellationToken cancellationToken = default)
    {
        if (_handshake)
        {
            return;
        }

        await _client.ConnectAsync(_uri, cancellationToken);
      
        // ping probe
        var pingProbePacket = new[]
            { (byte)PacketType.Ping, (byte)'p', (byte)'r', (byte)'o', (byte)'b', (byte)'e' };
        await _client.SendAsync(
            new ArraySegment<byte>(pingProbePacket),
            WebSocketMessageType.Text,
            false,
            cancellationToken);
        _logger.LogDebug("Probe sent");
        
        // pong probe
        var pongProbePacket = new byte[6];
        await _client.ReceiveAsync(new Memory<byte>(pongProbePacket), cancellationToken);

        if (pongProbePacket[0] != (byte)PacketType.Pong)
        {
            throw new Exception("Unexpected response from server");
        }

        _logger.LogDebug("Probe completed");

        // upgrade
        var upgradePacket = new byte[1] { (byte)PacketType.Upgrade };
        await _client.SendAsync(
            new ArraySegment<byte>(upgradePacket),
            WebSocketMessageType.Text,
            false,
            cancellationToken);

        _logger.LogDebug("Upgrade completed");
        _handshake = true;
    }

    public Task Heartbeat(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<byte[]> GetAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task SendAsync(byte[] packet, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<byte[]> PollAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
