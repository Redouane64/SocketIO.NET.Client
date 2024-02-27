namespace EngineIO.Client;

public interface ITransport
{
    string Transport { get; }

    Task Handshake(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<byte[]>> GetAsync(
        CancellationToken cancellationToken = default);

    Task SendAsync(byte[] packet,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<byte[]> Poll(CancellationToken cancellationToken = default);
}
