using System.Collections.ObjectModel;
using EngineIO.Client.Packets;

namespace EngineIO.Client.Transport;

public interface ITransport
{
    /// <summary>
    ///     Transport name.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Perform transport handshake.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task Handshake(CancellationToken cancellationToken = default);

    Task Disconnect();

    /// <summary>
    ///     Fetch raw packets from server.
    /// </summary>
    /// <returns>Packets as an array of Bytes.</returns>
    Task<ReadOnlyCollection<Packet>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Send raw packets to server.
    /// </summary>
    /// <param name="packet">Packets encoded as Bytes</param>
    /// <param name="cancellationToken"></param>
    Task SendAsync(Packet packet,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Start polling server periodically and stream packets back to the caller.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>Packets stream</returns>
    IAsyncEnumerable<Packet> PollAsync(CancellationToken cancellationToken = default);
}
