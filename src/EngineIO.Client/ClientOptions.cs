using EngineIO.Client.Transports;

namespace EngineIO.Client;

public class ClientOptions
{
    /// <summary>
    ///     Engine.io server Uri.
    /// </summary>
    public string BaseAddress { get; set; } = null!;

    /// <summary>
    ///     Path the Engine.io endpoint is served from.
    /// </summary>
    /// <remarks>
    ///     A Socket.IO server carries Engine.io under its own path — "/socket.io" by
    ///     default — so the client has to be told where to look.
    /// </remarks>
    public string Path { get; set; } = TransportPath.Default;

    /// <summary>
    ///     Flag indicating whether client should automatically update from HTTP polling to websocket transport.
    /// </summary>
    public bool AutoUpgrade { get; set; }

    /// <summary>
    ///     Enable or disable packet buffering.
    /// </summary>
    public bool Buffering { get; set; }
}