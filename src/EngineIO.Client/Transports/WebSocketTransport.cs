using System;
using System.Buffers;
using System.Collections.Generic;
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

    /// <summary>
    ///     How long to wait for the server to answer the close handshake before
    ///     dropping the socket.
    /// </summary>
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

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
        await SendAsync(Packet.PingProbePacket, cancellationToken);

        // pong probe: the reply must echo the "probe" payload we just sent, otherwise
        // it is an unrelated pong and the upgrade has not been acknowledged.
        var packets = await GetAsync(cancellationToken);
        if (packets.Count == 0
            || packets[0].Type != PacketType.Pong
            || !packets[0].Body.Span.SequenceEqual(Packet.PingProbePacket.Body.Span))
        {
            throw new TransportException(ErrorReason.InvalidPacket);
        }

        // upgrade
        await SendAsync(Packet.UpgradePacket, cancellationToken);

        _connected = true;
    }

    public async Task Disconnect()
    {
        if (!_connected)
        {
            return;
        }

        _connected = false;

        try
        {
            // Tell the server we are going away, then run the WebSocket close
            // handshake instead of aborting the socket underneath it.
            await SendAsync(Packet.ClosePacket);

            if (_client.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(CloseTimeout);
                await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, timeout.Token);
            }
        }
        catch (Exception exception) when (
            exception is WebSocketException or TransportException or OperationCanceledException)
        {
            // The peer is already gone or will not answer the close handshake.
            _client.Abort();
        }
    }

    public async Task<IReadOnlyList<Packet>> GetAsync(CancellationToken cancellationToken = default)
    {
        await _receiveSemaphore.WaitAsync(cancellationToken);

        try
        {
            using var rent = MemoryPool<byte>.Shared.Rent(ReceiveChunkSize);
            Memory<byte> buffer = rent.Memory;

            // Only allocated if the message turns out to span more than one read.
            ArrayBufferWriter<byte>? message = null;
            byte[]? payload = null;
            ValueWebSocketReceiveResult result;

            do
            {
                result = await _client.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    _connected = false;
                    return new[] { Packet.ClosePacket };
                }

                if (result.EndOfMessage && message is null)
                {
                    // Common case: the whole packet arrived in one read, so there is
                    // nothing to reassemble.
                    payload = buffer.Span[..result.Count].ToArray();
                    break;
                }

                message ??= new ArrayBufferWriter<byte>(ReceiveChunkSize * 2);
                message.Write(buffer.Span[..result.Count]);
            } while (!result.EndOfMessage);

            // Each frame carries exactly one packet. Copy out: the writer's buffer is
            // not owned by the caller, and the rented buffer returns to the pool here.
            payload ??= message!.WrittenSpan.ToArray();

            // A binary frame is the message payload itself, sent as-is. There is no
            // packet type byte to parse off the front.
            if (result.MessageType == WebSocketMessageType.Binary)
            {
                return new[] { Packet.CreateBinaryPacket(payload) };
            }

            return Packet.TryParse(payload, out var packet)
                ? new[] { packet }
                : Array.Empty<Packet>();
        }
        finally
        {
            _receiveSemaphore.Release();
        }
    }

    public async Task SendAsync(Packet packet, CancellationToken cancellationToken = default)
    {
        if (_client.State is WebSocketState.Closed or WebSocketState.Aborted)
        {
            throw new TransportException(ErrorReason.ConnectionClosed);
        }

        // Binary is sent as-is in a binary frame. The base64 + 'b' prefix encoding
        // belongs to long-polling, which can only carry text.
        var binary = packet.Format == PacketFormat.Binary;
        var payload = binary ? packet.Body : packet.ToPlaintextPacket();
        var messageType = binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text;

        await _sendSemaphore.WaitAsync(cancellationToken);

        try
        {
            await _client.SendAsync(payload, messageType, true, cancellationToken);
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }
}