using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using EngineIO.Client;

using Microsoft.Extensions.Logging;

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
    ///     Whether the underlying Engine.io connection has been established, tracked
    ///     here because <see cref="Engine.Connected" /> has no transport to answer for
    ///     until it has.
    /// </summary>
    private bool _connected;

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

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Connect to a namespace, opening the Engine.io connection if it is the first.
    /// </summary>
    /// <param name="namespace">Namespace to join, or the default one</param>
    /// <param name="cancellationToken"></param>
    public async Task ConnectAsync(string? @namespace = default, CancellationToken cancellationToken = default)
    {
        if (!_connected)
        {
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _connected = true;
        }

        // TODO: the server answers with `0{"sid":"..."}` for the namespace, which is
        // what _namespaces is waiting for. Recording it needs the inbound parser.
        await SendPacketAsync(new Packet(PacketType.Connect, @namespace), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Leave a namespace.
    /// </summary>
    /// <param name="namespace">Namespace to leave, or the default one</param>
    /// <param name="cancellationToken"></param>
    public async Task DisconnectAsync(string? @namespace = default, CancellationToken cancellationToken = default)
    {
        var packet = new Packet(PacketType.Disconnect, @namespace);
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
        var packet = new Packet(PacketType.Event, @namespace, @event);
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
        var packet = new Packet(PacketType.Event, @namespace, @event);
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
        var packet = new Packet(PacketType.BinaryEvent, @namespace, @event);
        packet.AddItem(data);
        return SendPacketAsync(packet, cancellationToken);
    }

    private async Task SendPacketAsync(Packet packet, CancellationToken cancellationToken)
    {
        // The header travels as a plain-text Engine.io message; each attachment then
        // follows as its own binary message, in the order its placeholder named it.
        await _client.SendAsync(EnginePacket.CreateMessagePacket(packet.Serialize()), cancellationToken)
            .ConfigureAwait(false);

        foreach (var attachment in packet.Attachments)
        {
            await _client.SendAsync(EnginePacket.CreateBinaryPacket(attachment), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}