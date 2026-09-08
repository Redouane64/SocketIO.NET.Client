using System;

namespace SocketIO.Client.Exceptions;

/// <summary>
///     Thrown when an operation needs a connection that is not there.
/// </summary>
/// <remarks>
///     Engine.io reports a failed handshake by completing its packet stream with the
///     reason rather than by throwing, so the reason is carried here as the inner
///     exception instead of being lost to a caller that only sends.
/// </remarks>
public class IOConnectionException : Exception
{
    public IOConnectionException(string message)
        : base(message)
    {
    }

    public IOConnectionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}