namespace EngineIO.Client;

public class ClientOptions
{
    /// <summary>
    ///     Engine.io server Uri.
    /// </summary>
    public string BaseAddress { get; set; } = null!;

    /// <summary>
    ///     Flag indicating whether client should automatically update from HTTP polling to websocket transport.
    /// </summary>
    public bool AutoUpgrade { get; set; }

    /// <summary>
    ///     Enable or disable packet buffering.
    /// </summary>
    public bool Buffering { get; set; }
}