using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
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

    public IO(string baseAddress, string path = DefaultPath, ILoggerFactory? loggerFactory = null)
    {
        Path = path;

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
        await _client.DisposeAsync().ConfigureAwait(false);
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
        // TODO: decoding a Socket.IO packet from the Engine.io message stream, holding
        // a binary header back until its attachments have arrived, and routing the
        // result to the listener of the namespace it names.
        throw new NotImplementedException(
            "Receiving requires the Socket.IO packet parser, which is not implemented yet.");
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