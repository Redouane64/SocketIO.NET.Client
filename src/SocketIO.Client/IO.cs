using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using EngineIO.Client;

using Microsoft.Extensions.Logging;

using SocketIO.Client.Exceptions;
using SocketIO.Client.Packets;

using EnginePacket = EngineIO.Client.Packets.Packet;

namespace SocketIO.Client;

/// <summary>
///     Socket.IO client, multiplexing namespaces over a single Engine.io connection.
/// </summary>
public sealed class IO : IAsyncDisposable
{
    /// <summary>
    ///     Path a Socket.IO server serves Engine.io from.
    /// </summary>
    public const string DefaultPath = "/socket.io";

    private readonly Engine _client;

    /// <summary>
    ///     Map namespace with its corresponding sid.
    /// </summary>
    private readonly Dictionary<string, string> _namespaces = new();

    /// <summary>
    ///     Serializes connection attempts, so that two callers joining a namespace at
    ///     once open one Engine.io connection between them rather than one each.
    /// </summary>
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    /// <summary>
    ///     Holds a packet and its attachments together on the wire.
    /// </summary>
    /// <remarks>
    ///     A binary packet is several Engine.io packets that a decoder reads as one
    ///     run: the header, then exactly as many binary packets as it announced.
    ///     Another packet landing in the middle of that run is a protocol error, and
    ///     the transports only serialize sends one at a time — not in groups.
    /// </remarks>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>
    ///     One queue per namespace being listened to, created the first time somebody
    ///     asks for it.
    /// </summary>
    /// <remarks>
    ///     A single loop drains the Engine.io stream and fans out from it. Letting each
    ///     listener drain that stream itself would not work: it is one channel, so two
    ///     listeners would compete for packets and each would see roughly half of them.
    ///     Creating the queue on demand also means a listener that subscribed before
    ///     connecting misses nothing.
    /// </remarks>
    private readonly ConcurrentDictionary<string, Channel<Packet>> _listeners = new();

    /// <summary>
    ///     Ends the receive loop when the client is disposed.
    /// </summary>
    private readonly CancellationTokenSource _receiveCancellation = new();

    private readonly ILogger<IO>? _logger;

    /// <summary>
    ///     The loop that turns Engine.io messages into packets, started on connect.
    /// </summary>
    private Task? _receiveTask;

    /// <summary>
    ///     Set once the receive loop has ended, before any queue is completed, so that
    ///     a listener arriving afterwards can see that it has missed the stream.
    /// </summary>
    private volatile bool _receiveEnded;

    /// <summary>
    ///     Why the receive loop ended, if it ended badly. Written before
    ///     <see cref="_receiveEnded" />, which publishes it.
    /// </summary>
    private Exception? _receiveError;

    public IO(string baseAddress, string path = DefaultPath, ILoggerFactory? loggerFactory = null)
    {
        Path = path;
        _logger = loggerFactory?.CreateLogger<IO>();

        _client = new Engine(Configure(baseAddress, path), loggerFactory);
    }

    /// <summary>
    ///     Drives the connection from a supplied <see cref="HttpClient" />, so the
    ///     protocol behaviour can be exercised against a stubbed server.
    /// </summary>
    internal IO(HttpClient httpClient, string baseAddress, string path = DefaultPath,
        ILoggerFactory? loggerFactory = null)
    {
        Path = path;
        _logger = loggerFactory?.CreateLogger<IO>();

        _client = new Engine(Configure(baseAddress, path), httpClient, loggerFactory: loggerFactory);
    }

    private static Action<ClientOptions> Configure(string baseAddress, string path)
    {
        return options =>
        {
            options.BaseAddress = baseAddress;
            options.Path = path;
            options.AutoUpgrade = true;
            // TODO: allow passing custom headers and queries
        };
    }

    /// <summary>
    ///     Path the server is served from.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Whether the underlying Engine.io connection is established.
    /// </summary>
    public bool Connected => _client.Connected;

    public async ValueTask DisposeAsync()
    {
        // Stop receiving before the engine goes away, so the loop unwinds on its own
        // token rather than on whatever the teardown happens to throw at it.
        await _receiveCancellation.CancelAsync().ConfigureAwait(false);

        if (_receiveTask is not null)
        {
            await _receiveTask.ConfigureAwait(false);
        }

        await _client.DisposeAsync().ConfigureAwait(false);

        _receiveCancellation.Dispose();
        _connectLock.Dispose();
        _sendLock.Dispose();
    }

    /// <summary>
    ///     Connect to a namespace, opening the Engine.io connection if it is the first.
    /// </summary>
    /// <param name="namespace">Namespace to join, or the default one</param>
    /// <param name="cancellationToken"></param>
    public async Task ConnectAsync(string? @namespace = default, CancellationToken cancellationToken = default)
    {
        // Built before the connection is touched, so a namespace the protocol refuses
        // does not leave a connection open behind it.
        var packet = new PacketBuilder(PacketType.Connect, @namespace);

        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_client.Connected)
            {
                await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);

                // Engine.io reports a failed handshake through its packet stream
                // rather than by throwing, so a caller that only ever sends would
                // otherwise go on writing to a transport that never came up.
                if (!_client.Connected)
                {
                    throw new IOConnectionException(
                        "The Engine.io connection could not be established.", _client.ConnectionError);
                }
            }

            // Restarted after a reconnection: the previous loop ended with the
            // connection that fed it.
            if (_receiveTask is null or { IsCompleted: true })
            {
                _receiveTask = Task.Run(ReceiveAsync, CancellationToken.None);
            }
        }
        finally
        {
            _connectLock.Release();
        }

        // TODO: the server answers with `0{"sid":"..."}` for the namespace, which is
        // what _namespaces is waiting for. Recording it needs the inbound parser.
        await SendPacketAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Leave a namespace.
    /// </summary>
    /// <param name="namespace">Namespace to leave, or the default one</param>
    /// <param name="cancellationToken"></param>
    public async Task DisconnectAsync(string? @namespace = default, CancellationToken cancellationToken = default)
    {
        var packet = new PacketBuilder(PacketType.Disconnect, @namespace);
        await SendPacketAsync(packet, cancellationToken).ConfigureAwait(false);
        _namespaces.Remove(packet.Namespace);
    }

    /// <summary>
    ///     Listen for incoming packets on a namespace.
    /// </summary>
    /// <param name="namespace">Namespace to listen on, or the default one</param>
    /// <param name="cancellationToken">IAsyncEnumerable cancellation token</param>
    /// <returns>Packets</returns>
    public IAsyncEnumerable<Packet> ListenAsync(
        string? @namespace = default, CancellationToken cancellationToken = default)
    {
        var queue = _listeners.GetOrAdd(
            PacketBuilder.NormalizeNamespace(@namespace), _ => Channel.CreateUnbounded<Packet>());

        // The stream may already have ended — the client disposed, the connection
        // gone. A queue created after that is one nothing will ever complete, so its
        // listener would wait for a packet that cannot arrive.
        if (_receiveEnded)
        {
            queue.Writer.TryComplete(_receiveError);
        }

        return queue.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    ///     Drain the Engine.io message stream, decode it, and hand each packet to the
    ///     namespace that is listening for it.
    /// </summary>
    private async Task ReceiveAsync()
    {
        var decoder = new Decoder();

        try
        {
            await foreach (var message in _client.ListenAsync(_receiveCancellation.Token).ConfigureAwait(false))
            {
                var packet = decoder.Add(message);

                if (packet is not null)
                {
                    Route(packet);
                }
            }

            EndListeners(null);
        }
        catch (OperationCanceledException)
        {
            // Shutting down through DisposeAsync is not a failure.
            EndListeners(null);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "The receive loop ended.");
            EndListeners(exception);
        }
    }

    /// <summary>
    ///     Split protocol concerns from consumer concerns, the way
    ///     <c>Engine.PollAsync</c> does for the heartbeat.
    /// </summary>
    private void Route(Packet packet)
    {
        // TODO: Connect carries the namespace's own sid, which is what _namespaces is
        // waiting for; Disconnect ends it, and ConnectError has to reach whoever asked
        // to join rather than being dropped here.
        if (packet.Type is PacketType.Connect or PacketType.Disconnect or PacketType.ConnectError)
        {
            _logger?.LogDebug("Namespace {Namespace} sent {Type}, which is not routed yet.",
                packet.Namespace, packet.Type);
            return;
        }

        if (_listeners.TryGetValue(packet.Namespace, out var queue))
        {
            // The channel is unbounded, so this never fails or blocks.
            queue.Writer.TryWrite(packet);
            return;
        }

        // Buffering for a namespace nobody listens to would grow without limit.
        _logger?.LogDebug("Dropped a {Type} packet for {Namespace}, which has no listener.",
            packet.Type, packet.Namespace);
    }

    /// <summary>
    ///     End every listener's enumeration, with the reason if there was one.
    /// </summary>
    private void EndListeners(Exception? exception)
    {
        // Published before anything is completed, so a listener subscribing alongside
        // this either is completed by the loop below or completes itself. Both may
        // happen; completing a queue twice is harmless.
        _receiveError = exception;
        _receiveEnded = true;

        foreach (var queue in _listeners.Values)
        {
            queue.Writer.TryComplete(exception);
        }
    }

    /// <summary>
    ///     Send plain text data.
    /// </summary>
    /// <param name="text">Plain text data</param>
    /// <param name="event">Event name, or the default one</param>
    /// <param name="namespace">Namespace to send on, or the default one</param>
    /// <param name="cancellationToken"></param>
    public Task SendAsync(string text, string? @event = default, string? @namespace = default,
        CancellationToken cancellationToken = default)
    {
        var packet = new PacketBuilder(PacketType.Event, @namespace, @event);
        packet.AddItem(text);
        return SendPacketAsync(packet, cancellationToken);
    }

    /// <summary>
    ///     Send a Json serializable POCO.
    /// </summary>
    /// <param name="data">Data instance</param>
    /// <param name="event">Event name, or the default one</param>
    /// <param name="namespace">Namespace to send on, or the default one</param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="T">Data type</typeparam>
    public Task SendAsync<T>(T data, string? @event = default, string? @namespace = default,
        CancellationToken cancellationToken = default) where T : class
    {
        var packet = new PacketBuilder(PacketType.Event, @namespace, @event);
        packet.AddItem(data);
        return SendPacketAsync(packet, cancellationToken);
    }

    /// <summary>
    ///     Send binary data.
    /// </summary>
    /// <remarks>
    ///     Present so that a byte array reaches the binary overload rather than
    ///     <see cref="SendAsync{T}" />, which would quietly encode it as a base64 string.
    /// </remarks>
    /// <param name="data">Binary data</param>
    /// <param name="event">Event name, or the default one</param>
    /// <param name="namespace">Namespace to send on, or the default one</param>
    /// <param name="cancellationToken"></param>
    public Task SendAsync(byte[] data, string? @event = default, string? @namespace = default,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(new ReadOnlyMemory<byte>(data), @event, @namespace, cancellationToken);
    }

    /// <summary>
    ///     Send binary data.
    /// </summary>
    /// <param name="data">Binary data</param>
    /// <param name="event">Event name, or the default one</param>
    /// <param name="namespace">Namespace to send on, or the default one</param>
    /// <param name="cancellationToken"></param>
    public Task SendAsync(ReadOnlyMemory<byte> data, string? @event = default, string? @namespace = default,
        CancellationToken cancellationToken = default)
    {
        var packet = new PacketBuilder(PacketType.BinaryEvent, @namespace, @event);
        packet.AddItem(data);
        return SendPacketAsync(packet, cancellationToken);
    }

    private async Task SendPacketAsync(PacketBuilder packet, CancellationToken cancellationToken)
    {
        if (!_client.Connected)
        {
            throw new IOConnectionException(
                $"Not connected. Call {nameof(ConnectAsync)} before sending.", _client.ConnectionError);
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // The header travels as a plain-text Engine.io message; each attachment
            // then follows as its own binary message, in the order its placeholder
            // named it. Nothing may come between them.
            await _client.SendAsync(EnginePacket.CreateMessagePacket(packet.Serialize()), cancellationToken)
                .ConfigureAwait(false);

            foreach (var attachment in packet.Attachments)
            {
                await _client.SendAsync(EnginePacket.CreateBinaryPacket(attachment), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
}