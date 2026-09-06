using System.Net.WebSockets;
using System.Text;

using EngineIO.Client.Transports;

namespace EngineIO.Client.Tests.Transports;

/// <summary>
///     A scripted stand-in for the socket: frames are queued for the transport to
///     read, and everything it writes is recorded.
/// </summary>
internal sealed class FakeWebSocket : IWebSocket
{
    private readonly Queue<Frame> _inbound = new();

    public WebSocketState State { get; set; } = WebSocketState.Open;

    public List<Frame> Sent { get; } = new();

    /// <summary>Names of the calls the transport made, in order.</summary>
    public List<string> Calls { get; } = new();

    public void QueueText(string payload)
    {
        Queue(WebSocketMessageType.Text, Encoding.UTF8.GetBytes(payload), true);
    }

    public void QueueBinary(byte[] payload)
    {
        Queue(WebSocketMessageType.Binary, payload, true);
    }

    public void QueueClose()
    {
        Queue(WebSocketMessageType.Close, Array.Empty<byte>(), true);
    }

    public void Queue(WebSocketMessageType type, byte[] payload, bool endOfMessage)
    {
        _inbound.Enqueue(new Frame(type, payload, endOfMessage));
    }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(ConnectAsync));
        return Task.CompletedTask;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage,
        CancellationToken cancellationToken)
    {
        Calls.Add(nameof(SendAsync));
        Sent.Add(new Frame(messageType, buffer.ToArray(), endOfMessage));
        return ValueTask.CompletedTask;
    }

    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        Calls.Add(nameof(ReceiveAsync));

        if (_inbound.Count == 0)
        {
            throw new InvalidOperationException("The test scripted no further frames to receive.");
        }

        var frame = _inbound.Dequeue();
        frame.Payload.CopyTo(buffer.Span);
        return ValueTask.FromResult(
            new ValueWebSocketReceiveResult(frame.Payload.Length, frame.Type, frame.EndOfMessage));
    }

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
        CancellationToken cancellationToken)
    {
        Calls.Add(nameof(CloseAsync));
        State = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public void Abort()
    {
        Calls.Add(nameof(Abort));
        State = WebSocketState.Aborted;
    }

    public void Dispose()
    {
    }

    internal sealed record Frame(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage);
}