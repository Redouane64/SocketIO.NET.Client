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
    /// Fetch packets from the server.
    /// </summary>
    /// <remarks>
    /// Decoding the wire format belongs to the transport: long-polling concatenates
    /// packets and base64-encodes binary ones, while a WebSocket frame carries a
    /// single packet whose binary form has no packet type prefix at all.
    /// </remarks>
    /// <returns>The packets carried by one read.</returns>
    Task<ReadOnlyCollection<Packet>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a packet, encoded for this transport's wire format.
    /// </summary>
    /// <param name="packet"></param>
    /// <param name="cancellationToken"></param>
    Task SendAsync(Packet packet, CancellationToken cancellationToken = default);
}