using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace EngineIO.Client.Transports;

/// <summary>
///     The part of <see cref="ClientWebSocket" /> the transport uses, so that frame
///     handling can be exercised without opening a socket.
/// </summary>
internal interface IWebSocket : IDisposable
{
    WebSocketState State { get; }

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage,
        CancellationToken cancellationToken);

    ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
        CancellationToken cancellationToken);

    void Abort();
}

internal sealed class ClientWebSocketAdapter : IWebSocket
{
    private readonly ClientWebSocket _client = new();

    public WebSocketState State => _client.State;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        return _client.ConnectAsync(uri, cancellationToken);
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage,
        CancellationToken cancellationToken)
    {
        return _client.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
    }

    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        return _client.ReceiveAsync(buffer, cancellationToken);
    }

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
        CancellationToken cancellationToken)
    {
        return _client.CloseAsync(closeStatus, statusDescription, cancellationToken);
    }

    public void Abort()
    {
        _client.Abort();
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}