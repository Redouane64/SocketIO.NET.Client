using System.Net.WebSockets;

namespace EngineIO.Client;

public class WebSocketTransport : ITransport
{
    private readonly string _path;
    
    private ClientWebSocket _client;
    private SemaphoreSlim _semaphore = new(1, 1);

    private bool _handshake;

    public WebSocketTransport(string path)
    {
        _path = path;
        _client = new ClientWebSocket();
    }

    public string Transport => "WebSocket";
    
    public async Task Handshake(CancellationToken cancellationToken = default)
    {
        if (_handshake)
        {
            return;
        }
        
        await _client.ConnectAsync(new Uri(_path), cancellationToken);

        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            
            // ping probe
            var pingProbePacket = new[]
                { (byte)PacketType.Ping, (byte)'p', (byte)'r', (byte)'o', (byte)'b', (byte)'e' };
            await _client.SendAsync(
                new ArraySegment<byte>(pingProbePacket),
                WebSocketMessageType.Text,
                false,
                cancellationToken);

            // pong probe
            var pongProbePacket = new byte[6];
            await _client.ReceiveAsync(new Memory<byte>(pongProbePacket), cancellationToken);

            if (pongProbePacket[0] != (byte)PacketType.Pong)
            {
                throw new Exception("Unexpected response from server");
            }

            // upgrade
            var upgradePacket = new byte[1] { (byte)PacketType.Upgrade };
            await _client.SendAsync(
                new ArraySegment<byte>(upgradePacket),
                WebSocketMessageType.Text,
                false,
                cancellationToken);

            _handshake = true;
        }
        finally
        {
            _semaphore.Release();
        }
        
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
}
