namespace EngineIO.Client;

public class ClientOptions
{
    /// <summary>
    /// Engine.io server Uri.
    /// </summary>
    public string Uri { get; set; }
    
    /// <summary>
    /// Flag indicating whether client should automatically update from HTTP polling to websocket transport.
    /// </summary>
    public bool AutoUpgrade { get; set; }

    /// <summary>
    /// HTTP polling interval in milliseconds.
    /// </summary>
    public int PollingInterval { get; set; }
}
