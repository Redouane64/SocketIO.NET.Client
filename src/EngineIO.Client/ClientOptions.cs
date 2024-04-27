namespace EngineIO.Client;

public class ClientOptions
{
    /// <summary>
    ///     Engine.io server Uri.
    /// </summary>
#nullable disable
    public string Uri { get; set; }

    /// <summary>
    ///     Flag indicating whether client should automatically update from HTTP polling to websocket transport.
    /// </summary>
    public bool AutoUpgrade { get; set; }

    /// <summary>
    ///     Enable or disable packet buffering.
    /// </summary>
    public bool Buffering { get; set; }

    /// <summary>
    ///     Indicate whether the client should reconnect automatically when connection is lost.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    ///     Maximum retry count before connection retry stops. Default value is 3.
    /// </summary>
    public int MaxConnectionRetry { get; set; } = 3;
}