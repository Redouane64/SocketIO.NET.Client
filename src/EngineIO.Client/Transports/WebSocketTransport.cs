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

    private readonly Uri _uri;
    private readonly string _sid;

    private bool _connected;

    public WebSocketTransport(string baseAddress, string sid, ClientWebSocket client)
    {
        _client = client;
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
    }

    public string Name => "websocket";

    public void Dispose()
    {
        _client.Dispose();
        _receiveSemaphore.Dispose();
        _sendSemaphore.Dispose();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connected)
        {
            return;
        }

        await _client.ConnectAsync(_uri, cancellationToken);

        // ping probe
        await SendAsync(Packet.PingProbePacket.ToPlaintextPacket(), PacketFormat.PlainText, cancellationToken);

        // pong probe
        var data = await GetAsync(cancellationToken);
        var packet = Packet.Parse(data[0]);

        if (packet.Type != PacketType.Pong)
        {
            throw new Exception("Unexpected response from server");
        }

        // upgrade
        await SendAsync(Packet.UpgradePacket.ToPlaintextPacket(), PacketFormat.PlainText, cancellationToken);

        _connected = true;
    }

    public Task Disconnect()
    {
        _client.Abort();
        return Task.CompletedTask;
    }

    public async Task<ReadOnlyCollection<ReadOnlyMemory<byte>>> GetAsync(CancellationToken cancellationToken = default)
    {
        var buffer = new byte[16];
        var receivedCount = 0;

        try
        {
            await _receiveSemaphore.WaitAsync(CancellationToken.None);

            WebSocketReceiveResult receiveResult;
            do
            {
                try
                {
                    receiveResult = await _client.ReceiveAsync(buffer, cancellationToken);
                }
                catch
                {
                    break;
                }

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

        return new ReadOnlyCollection<ReadOnlyMemory<byte>>(new[] { new ReadOnlyMemory<byte>(buffer) });
    }

    public async Task SendAsync(ReadOnlyMemory<byte> packets, PacketFormat format, CancellationToken cancellationToken = default)
    {
        if (_client.State == WebSocketState.Closed)
        {
            throw new Exception("Connection closed unexpectedly");
        }

        try
        {
            await _sendSemaphore.WaitAsync(CancellationToken.None);
            // TODO: what does WebSocketMessageType value mean?
            await _client.SendAsync(packets, WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, cancellationToken);
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }
}
