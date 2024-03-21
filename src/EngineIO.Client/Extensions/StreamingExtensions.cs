using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EngineIO.Client.Packets;

namespace EngineIO.Client.Extensions;

public static class StreamingExtensions
{
    /// <summary>
    ///     Listen for messages stream.
    /// </summary>
    /// <param name="engine">EngineIO client instance</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Packets</returns>
    public static async IAsyncEnumerable<Packet> ListenAsync(this Engine engine,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var streamingCancellationToken =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                engine.PollingCancellationTokenSource.Token);
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
