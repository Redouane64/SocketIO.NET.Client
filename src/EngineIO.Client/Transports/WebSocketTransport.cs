using System.Collections.ObjectModel;
using System.Net.WebSockets;
using EngineIO.Client.Packets;

namespace EngineIO.Client.Transport;

public sealed class WebSocketTransport : ITransport, IDisposable
{
    private readonly ClientWebSocket _client;

    private readonly int _protocol = 4;
    private readonly SemaphoreSlim _receiveSemaphore = new(1, 1);
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

    private readonly CancellationTokenSource _pollingCancellationToken = new();
    private readonly Uri _uri;
    private readonly string _sid;
    
    private bool _handshake;

    public WebSocketTransport(string baseAddress, string sid)
    {
        _sid = sid ?? throw new ArgumentException("Sid is missing");

        if (baseAddress.StartsWith(Uri.UriSchemeHttp))
        {
            baseAddress = baseAddress.Replace("http://", "ws://");
        }

        if (baseAddress.StartsWith(Uri.UriSchemeHttps))
        {
            baseAddress = baseAddress.Replace("https://", "wss://");
        }

        var uri = $"{baseAddress}/engine.io?EIO={_protocol}&transport={Name}&sid={sid}";

        _uri = new Uri(uri);
        _client = new ClientWebSocket();
    }
    
    public string Name => "websocket";

    public void Dispose()
    {
        _client.Dispose();
        _receiveSemaphore.Dispose();
        _sendSemaphore.Dispose();
        _pollingCancellationToken.Dispose();
    }

    public async Task Handshake(CancellationToken cancellationToken = default)
    {
        if (_handshake)
        {
            return;
        }

        await _client.ConnectAsync(_uri, cancellationToken);

        // ping probe
        await SendAsync(Packet.PingProbePacket, cancellationToken);

        // pong probe
        var pongProbePacket = await GetAsync(cancellationToken);

        if (pongProbePacket[0].Type != PacketType.Pong)
        {
            throw new Exception("Unexpected response from server");
        }

        // upgrade
        var upgradePacket = new byte[1] { (byte)PacketType.Upgrade };
        await SendAsync(Packet.UpgradePacket, cancellationToken);

        _handshake = true;
    }

    public async Task Disconnect()
    {
        await _pollingCancellationToken.CancelAsync();
        _client.Abort();
    }

    public async Task<ReadOnlyCollection<Packet>> GetAsync(CancellationToken cancellationToken = default)
    {
        var buffer = new byte[16];
        var receivedCount = 0;

        try
        {
            await _receiveSemaphore.WaitAsync(CancellationToken.None);

            WebSocketReceiveResult receiveResult;
            do
            {
                receiveResult = await _client.ReceiveAsync(buffer, cancellationToken);

                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }

                receivedCount += receiveResult.Count;
                if (receiveResult.Count > receivedCount - buffer.Length)
                {
                    Array.Resize(ref buffer, buffer.Length + receivedCount);
                }

            } while (!receiveResult.EndOfMessage);
        }
        finally
        {
            _receiveSemaphore.Release();
        }

        var packet = new ReadOnlyMemory<byte>(buffer);
        var format = packet.Span[0] == 98 ? PacketFormat.Binary : PacketFormat.PlainText;
        var type = format == PacketFormat.PlainText ? (PacketType)packet.Span[0] : PacketType.Message;
        var content = packet[1..];

        return new Collection<Packet> { new Packet(format, type, content) }.AsReadOnly();
    }

    public async Task SendAsync(Packet packet,
        CancellationToken cancellationToken = default)
    {
        if (_client.State == WebSocketState.Closed)
        {
            throw new Exception("Connection closed unexpectedly");
        }

        try
        {
            await _sendSemaphore.WaitAsync(CancellationToken.None);
            // TODO: if packet length exceeds MaxPayload, it should be sent with multiple Send calls.
            await _client.SendAsync(packet.ToPayload(), 
                WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, cancellationToken);
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }
}
