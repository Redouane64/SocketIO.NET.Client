namespace EngineIO.Client;

public interface ITransport
{
    string Transport { get; }

    Task<IReadOnlyCollection<Packet>> GetAsync(
        CancellationToken cancellationToken = default);

    Task SendAsync(Packet packet,
        CancellationToken cancellationToken = default);
}