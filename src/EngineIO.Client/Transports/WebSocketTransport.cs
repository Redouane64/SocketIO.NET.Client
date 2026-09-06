using System;
using System.Buffers;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using EngineIO.Client.Packets;
using EngineIO.Client.Transports.Exceptions;

namespace EngineIO.Client.Transports;

public sealed class WebSocketTransport : ITransport, IDisposable
{
    /// <summary>
    ///     Size of a single read from the socket. Messages larger than this are
    ///     read across several iterations and reassembled.
    /// </summary>
    private const int ReceiveChunkSize = 4096;

    private readonly ClientWebSocket _client;

    private readonly int _protocol = 4;
    private readonly SemaphoreSlim _receiveSemaphore = new(1, 1);
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    private readonly Uri _uri;

    private bool _connected;

    public WebSocketTransport(string baseAddress, string sid)
    {
        if (string.IsNullOrEmpty(baseAddress))
        {
            throw new ArgumentException("base address cannot be null or empty.", nameof(baseAddress));
        }

        if (string.IsNullOrEmpty(sid))
        {
            throw new ArgumentException("Sid cannot be null or empty.", nameof(sid));
        }

        _client = new ClientWebSocket();

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

    public bool Connected => _connected;

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
            throw new TransportException(ErrorReason.InvalidPacket);
        }

        if (packet.Type != PacketType.Pong)
        {
            throw new TransportException(ErrorReason.InvalidPacket);
        }

        // upgrade
        await SendAsync(Packet.UpgradePacket.ToPlaintextPacket(), PacketFormat.PlainText, cancellationToken);

        _connected = true;
    }

    public Task Disconnect()
    {
        if (_connected)
        {
            _client.Abort();
        }

        _connected = false;
        return Task.CompletedTask;
    }

    public async Task<ReadOnlyCollection<ReadOnlyMemory<byte>>> GetAsync(CancellationToken cancellationToken = default)
    {
        var packets = new Collection<ReadOnlyMemory<byte>>();
        using var rent = MemoryPool<byte>.Shared.Rent(ReceiveChunkSize);
        Memory<byte> buffer = rent.Memory;

        try
        {
            await _receiveSemaphore.WaitAsync(CancellationToken.None);
            var message = new ArrayBufferWriter<byte>(ReceiveChunkSize);
            ValueWebSocketReceiveResult result;
            do
            {
                result = await _client.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    packets.Add(new[] { (byte)PacketType.Close });
                    return new ReadOnlyCollection<ReadOnlyMemory<byte>>(packets);
                }

                message.Write(buffer.Span[..result.Count]);
            } while (!result.EndOfMessage);

            // Copy out: the writer's buffer is not owned by the caller, and the
            // rented buffer goes back to the pool when this method returns.
            packets.Add(message.WrittenSpan.ToArray());
        }
        finally
        {
            _receiveSemaphore.Release();
        }

        return new ReadOnlyCollection<ReadOnlyMemory<byte>>(packets);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> packets, PacketFormat format,
        CancellationToken cancellationToken = default)
    {
        if (_client.State is WebSocketState.Closed or WebSocketState.Aborted)
        {
            throw new TransportException(ErrorReason.ConnectionClosed);
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