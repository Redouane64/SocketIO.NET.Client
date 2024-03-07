using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client.Transport;

public class WebSocketTransport : ITransport, IDisposable
{
    private readonly ClientWebSocket _client;
    private readonly ILogger<WebSocketTransport> _logger;

    private readonly int _protocol = 4;
    private readonly SemaphoreSlim _receiveSemaphore = new(1, 1);
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

    private readonly CancellationTokenSource _pollingCancellationToken = new();
    private readonly HandshakePacket? _handshakePacket;
    private readonly Uri _uri;
    private bool _handshake;

    public WebSocketTransport(string baseAddress, HandshakePacket handshakePacket, ILogger<WebSocketTransport> logger)
    {
        if (handshakePacket.Sid is null)
        {
            throw new ArgumentException("Sid is missing");
        }

        if (baseAddress.StartsWith(Uri.UriSchemeHttp))
        {
            baseAddress = baseAddress.Replace("http://", "ws://");
        }

        if (baseAddress.StartsWith(Uri.UriSchemeHttps))
        {
            baseAddress = baseAddress.Replace("https://", "wss://");
        }

        var uri = $"{baseAddress}/engine.io?EIO={_protocol}&transport={Name}&sid={handshakePacket.Sid}";

        _handshakePacket = handshakePacket;
        _logger = logger;
        
        _uri = new Uri(uri);
        _client = new ClientWebSocket();
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    public string Name => "websocket";

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
        await SendAsync(pingProbePacket, cancellationToken);
        _logger.LogDebug("Probe sent");

        // pong probe
        var pongProbePacket = await GetAsync(cancellationToken);

        if (pongProbePacket[0] != (byte)PacketType.Pong)
        {
            throw new Exception("Unexpected response from server");
        }

        _logger.LogDebug("Probe completed");

        // upgrade
        var upgradePacket = new byte[1] { (byte)PacketType.Upgrade };
        await SendAsync(upgradePacket, cancellationToken);

        _logger.LogDebug("Upgrade completed");
        _handshake = true;
    }

    public async Task Disconnect()
    {
        await _pollingCancellationToken.CancelAsync();
        _client.Abort();
    }

    public async Task<byte[]> GetAsync(CancellationToken cancellationToken = default)
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
                    _logger.LogDebug("Connection closed by the remote server");
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

        return buffer.AsSpan(0, receivedCount).ToArray();
    }

    public async Task SendAsync(byte[] packet, CancellationToken cancellationToken = default)
    {
        if (_client.State == WebSocketState.Closed)
        {
            throw new Exception("Connection closed unexpectedly");
        }

        try
        {
            await _sendSemaphore.WaitAsync(CancellationToken.None);
            await _client.SendAsync(packet, WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, cancellationToken);
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }

    public async IAsyncEnumerable<byte[]> PollAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var cts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _pollingCancellationToken.Token);

        while (!cts.IsCancellationRequested)
        {
            var packet = await GetAsync(cancellationToken);

            if (packet[0] == (byte)PacketType.Ping)
            {
                _logger.LogDebug("Heartbeat received");
#pragma warning disable CS4014 
                SendHeartbeat(cancellationToken);
#pragma warning restore CS4014
                continue;
            }

            yield return packet;
        }
    }

    private void SendHeartbeat(CancellationToken cancellationToken)
    {
#pragma warning disable CS4014
        SendAsync(new[] { (byte)PacketType.Pong }, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS4014
    }

}
