using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using EngineIO.Client.Packets;

namespace EngineIO.Client.Transports;

public sealed class WebSocketTransport : ITransport, IDisposable
{
    private readonly ClientWebSocket _client;

    private readonly int _protocol = 4;
    private readonly SemaphoreSlim _receiveSemaphore = new(1, 1);
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    private readonly string _sid;

    private readonly Uri _uri;

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

    public void Dispose()
    {
        _client.Dispose();
        _receiveSemaphore.Dispose();
        _sendSemaphore.Dispose();
    }

    public string Name => "websocket";

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
        if (!Packet.TryParse(data[0], out var packet))
        {
            throw new Exception("Unexpected response from server");
        }
        
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
        var packets = new Collection<ReadOnlyMemory<byte>>();
        var stream = new MemoryStream();
        var buffer = new byte[16];

        try
        {
            await _receiveSemaphore.WaitAsync(CancellationToken.None);
            WebSocketReceiveResult result;
            do
            {
                result = await _client.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, String.Empty, CancellationToken.None);
                    packets.Add(new [] { (byte)PacketType.Close });
                    break;
                }

                await stream.WriteAsync(buffer, 0, result.Count, cancellationToken);
            } while (!result.EndOfMessage);
        }
        finally
        {
            _receiveSemaphore.Release();
        }

        stream.Seek(0, SeekOrigin.Begin);
        
        packets.Add(stream.ToArray());
        return new ReadOnlyCollection<ReadOnlyMemory<byte>>(packets);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> packets, PacketFormat format,
        CancellationToken cancellationToken = default)
    {
        if (_client.State == WebSocketState.Closed)
        {
            throw new Exception("Connection closed unexpectedly");
        }

        try
        {
            await _sendSemaphore.WaitAsync(CancellationToken.None);
            await _client.SendAsync(packets, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }
}
