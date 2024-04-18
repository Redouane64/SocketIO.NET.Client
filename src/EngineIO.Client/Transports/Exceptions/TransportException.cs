using System;

namespace EngineIO.Client.Transports.Exceptions;

public class TransportException : Exception {
    public TransportException(string message) : base(message) { }
}
