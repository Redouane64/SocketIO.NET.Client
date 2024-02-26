namespace EngineIO.Client;

public interface ITransport
{
    string Transport { get; }

    Task Handshake(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Packet>> GetAsync(
        CancellationToken cancellationToken = default);

    Task SendAsync(Packet packet,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<Packet> Poll(CancellationToken cancellationToken = default);
}