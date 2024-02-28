namespace EngineIO.Client;

public interface ITransport
{
    /// <summary>
    /// Transport name.
    /// </summary>
    string Transport { get; }

    Task Handshake(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch raw packets from server.
    /// </summary>
    /// <returns>Packets as an array of Bytes.</returns>
    Task<byte[]> GetAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send raw packets to server.
    /// </summary>
    /// <param name="packet">Packets encoded as Bytes</param>
    Task SendAsync(byte[] packet,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Start polling server periodically and stream packets back to the caller.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    IAsyncEnumerable<byte[]> PollAsync(CancellationToken cancellationToken = default);
}
