using System;

namespace SocketIO.Client.Exceptions;

/// <summary>
///     Thrown when the bytes that arrived are not a packet the protocol allows.
/// </summary>
/// <remarks>
///     Fatal to the connection rather than to the packet: a server answers a packet it
///     cannot decode by closing the transport, so a client that carried on after one
///     would be talking to nobody.
/// </remarks>
public class PacketFormatException : Exception
{
    public PacketFormatException(string message)
        : base(message)
    {
    }
}