using System;

namespace EngineIO.Client.Transports.Exceptions;

public enum ErrorReason
{
    InvalidPacket,
    ConnectionClosed,
}

public class TransportException : Exception
{
    public TransportException(ErrorReason errorReason)
        : base()
    {
        ErrorReason = errorReason;
    }

    public TransportException(ErrorReason errorReason, string message)
        : base(message)
    {
        ErrorReason = errorReason;
    }

    public ErrorReason ErrorReason { get; private set; }
}
