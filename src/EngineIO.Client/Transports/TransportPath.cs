using System;

namespace EngineIO.Client.Transports;

/// <summary>
///     The path an Engine.IO endpoint is served from.
/// </summary>
internal static class TransportPath
{
    /// <summary>
    ///     Where a bare Engine.IO server listens. A Socket.IO server speaks the same
    ///     protocol but serves it from "/socket.io" instead, which is why the path is
    ///     configurable at all.
    /// </summary>
    public const string Default = "/engine.io";

    /// <summary>
    ///     Bring a caller-supplied path to the form a server actually matches on: a
    ///     leading and a trailing slash, with the query string appended straight after.
    /// </summary>
    /// <remarks>
    ///     The trailing slash is not cosmetic. Both servers normalize their configured
    ///     path the same way and compare it against the start of the request, so a
    ///     Socket.IO server answers "/socket.io/?EIO=4" and 404s "/socket.io?EIO=4".
    /// </remarks>
    public static string Normalize(string? path)
    {
        var trimmed = (path ?? Default).Trim().Trim('/');
        return trimmed.Length == 0 ? "/" : $"/{trimmed}/";
    }
}