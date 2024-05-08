using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

using EngineIO.Client.Packets;

namespace EngineIO.Client.Transports;

public interface ITransport
{
    /// <summary>
    /// Transport name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Flag indicate whether the transport is connected or not.
    /// </summary>
    bool Connected { get; }

    /// <summary>
    /// Perform transport handshake.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect transport by sending close packet to remote server.
    /// </summary>
    Task Disconnect();

    /// <summary>
    /// Fetch raw packets from server.
    /// </summary>
    /// <returns>Packets as an array of Bytes.</returns>
    Task<ReadOnlyCollection<ReadOnlyMemory<byte>>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send raw packets to server.
    /// </summary>
    /// <param name="packets"></param>
    /// <param name="format"></param>
    /// <param name="cancellationToken"></param>
    Task SendAsync(ReadOnlyMemory<byte> packets, PacketFormat format, CancellationToken cancellationToken = default);
}