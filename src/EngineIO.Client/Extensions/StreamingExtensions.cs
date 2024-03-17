using System.Runtime.CompilerServices;
using EngineIO.Client.Packets;

namespace EngineIO.Client.Extensions;

public static class StreamingExtensions
{
    /// <summary>
    /// Listen for messages stream.
    /// </summary>
    /// <param name="engine">EngineIO client instance</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Packets</returns>
    public static async IAsyncEnumerable<Packet> ListenAsync(this Engine engine, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var streamingCancellationToken =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, engine._pollingCancellationTokenSource.Token);
        while (!streamingCancellationToken.IsCancellationRequested)
        {
            if (engine.Messages.TryDequeue(out var packet))
            {
                yield return packet;
            }

            await Task.CompletedTask;
        }
    }
}
