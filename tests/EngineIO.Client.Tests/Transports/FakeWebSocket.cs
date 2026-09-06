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
    private readonly SemaphoreSlim _available = new(0);
    private readonly object _gate = new();
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
        lock (_gate)
        {
            _inbound.Enqueue(new Frame(type, payload, endOfMessage));
        }

        _available.Release();
    }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        Record(nameof(ConnectAsync));
        return Task.CompletedTask;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage,
        CancellationToken cancellationToken)
    {
        var frame = new Frame(messageType, buffer.ToArray(), endOfMessage);
        lock (_gate)
        {
            Calls.Add(nameof(SendAsync));
            Sent.Add(frame);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        Record(nameof(ReceiveAsync));

        // Wait for a frame the way a real socket does, rather than failing because
        // the test has not queued the next one yet. The timeout keeps an
        // under-scripted test failing instead of hanging.
        if (!await _available.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
        {
            throw new InvalidOperationException("The test scripted no further frames to receive.");
        }

        Frame frame;
        lock (_gate)
        {
            frame = _inbound.Dequeue();
        }

        frame.Payload.CopyTo(buffer.Span);
        return new ValueWebSocketReceiveResult(frame.Payload.Length, frame.Type, frame.EndOfMessage);
    }

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
        CancellationToken cancellationToken)
    {
        Record(nameof(CloseAsync));
        State = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public void Abort()
    {
        Record(nameof(Abort));
        State = WebSocketState.Aborted;
    }

    private void Record(string call)
    {
        lock (_gate)
        {
            Calls.Add(call);
        }
    }

    public void Dispose()
    {
    }

    internal sealed record Frame(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage);
}