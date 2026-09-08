using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using EngineIO.Client.Packets;
using EngineIO.Client.Transports;
using EngineIO.Client.Transports.Exceptions;

using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public sealed class Engine : IDisposable, IAsyncDisposable
{
    /// <summary>
    ///     How long the synchronous <see cref="Dispose" /> blocks waiting for the
    ///     receive loop. <see cref="DisposeAsync" /> waits without blocking.
    /// </summary>
    private static readonly TimeSpan PollingShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly ClientOptions _clientOptions = new();
    private readonly HttpClient? _httpClient;
    private readonly IWebSocket? _webSocket;
    private readonly ILogger<Engine>? _logger;
    private readonly Channel<Packet> _packetsChannel = Channel.CreateUnbounded<Packet>();
    private readonly CancellationTokenSource _pollingCancellationTokenSource = new();

    /// <summary>
    ///     How long the connection may go without a server ping before it is
    ///     considered closed. Zero disables the check.
    /// </summary>
    private int _heartbeatTimeoutMs;

    /// <summary>
    ///     Cancels the receive when the heartbeat budget runs out. Created once per
    ///     connection and pushed forward by <see cref="ResetHeartbeat" />, so the
    ///     receive loop allocates no cancellation state per iteration.
    /// </summary>
    private CancellationTokenSource? _heartbeatCts;

    /// <summary>
    ///     The token every receive runs under: the heartbeat source when there is a
    ///     budget to enforce, otherwise the polling source itself.
    /// </summary>
    private CancellationToken _receiveToken;

    /// <summary>
    ///     The receive loop, kept so that shutdown can wait for it to unwind before
    ///     anything it uses is torn down.
    /// </summary>
    private Task? _pollingTask;

#nullable disable
    private ITransport _transport;
    private HttpPollingTransport _httpTransport;
    private WebSocketTransport _wsTransport;
#nullable enable

    public Engine(Action<ClientOptions> configure, ILoggerFactory? loggerFactory = null)
    {
        configure(_clientOptions);
        if (loggerFactory is not null)
        {
            this._logger = loggerFactory.CreateLogger<Engine>();
        }
    }

    /// <summary>
    ///     Drives the polling transport from a supplied <see cref="HttpClient" />, so the
    ///     protocol behaviour can be exercised against a stubbed server.
    /// </summary>
    internal Engine(Action<ClientOptions> configure, HttpClient httpClient,
        IWebSocket? webSocket = null, ILoggerFactory? loggerFactory = null)
        : this(configure, loggerFactory)
    {
        _httpClient = httpClient;
        _webSocket = webSocket;
    }

    /// <summary>
    ///     Whether a transport is currently connected. False before
    ///     <see cref="ConnectAsync" /> has succeeded, and false again once the
    ///     connection has gone away.
    /// </summary>
    public bool Connected => _transport?.Connected ?? false;

    /// <summary>
    ///     Why the last connection attempt failed, or <c>null</c> if none has.
    /// </summary>
    /// <remarks>
    ///     <see cref="ConnectAsync" /> reports failure through the packet stream rather
    ///     than by throwing, so a caller that does not listen — a protocol layered on
    ///     top, deciding whether it may send — has no other way to see the reason.
    /// </remarks>
    public Exception? ConnectionError { get; private set; }

    /// <summary>
    ///     Name of the transport currently in use, so tests can tell whether the
    ///     connection upgraded.
    /// </summary>
    internal string TransportName => _transport.Name;

    /// <summary>
    ///     Preferred over <see cref="Dispose" />: waits for the receive loop without
    ///     blocking the calling thread.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _pollingCancellationTokenSource.Cancel();

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancelled before it ever started; there is nothing to wait for.
            }
        }

        DisposeCore();
    }

    public void Dispose()
    {
        _pollingCancellationTokenSource.Cancel();

        // The receive loop holds the transports and their semaphores. Disposing those
        // while it is still running makes it fail on a disposed object. Blocking here
        // is why DisposeAsync is preferred.
        try
        {
            _pollingTask?.Wait(PollingShutdownTimeout);
        }
        catch (AggregateException)
        {
            // PollAsync reports its own failures through the logger.
        }

        DisposeCore();
    }

    private void DisposeCore()
    {
        _pollingCancellationTokenSource.Dispose();
        _heartbeatCts?.Dispose();
        _httpTransport?.Dispose();
        _wsTransport?.Dispose();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _transport = _httpTransport = _httpClient is null
            ? new HttpPollingTransport(_clientOptions.BaseAddress, _clientOptions.Path)
            : new HttpPollingTransport(_httpClient, _clientOptions.Path);
        try
        {
            await _httpTransport.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            HandleException(exception);
            return;
        }

        if (_clientOptions.AutoUpgrade && _httpTransport.Upgrades!.Contains("websocket"))
        {
            // The upgrade is an optimisation, not a requirement: a probe the server
            // never answers leaves the polling transport connected and in charge, so
            // it is repointed only once the WebSocket has taken over.
            WebSocketTransport? wsTransport = null;
            try
            {
                wsTransport = _webSocket is null
                    ? new WebSocketTransport(_clientOptions.BaseAddress, _httpTransport.Sid!, _clientOptions.Path)
                    : new WebSocketTransport(_webSocket, _clientOptions.BaseAddress, _httpTransport.Sid!,
                        _clientOptions.Path);
                await wsTransport.ConnectAsync(cancellationToken).ConfigureAwait(false);

                _transport = _wsTransport = wsTransport;
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "Upgrade to websocket failed; staying on HTTP long-polling.");
                wsTransport?.Dispose();
            }
        }

        // The server pings every pingInterval and allows pingTimeout for the reply,
        // so silence for longer than their sum means the connection is gone.
        _heartbeatTimeoutMs = _httpTransport.PingInterval + _httpTransport.PingTimeout;

        if (_heartbeatTimeoutMs > 0)
        {
            _heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(_pollingCancellationTokenSource.Token);
            _receiveToken = _heartbeatCts.Token;
            ResetHeartbeat();
        }
        else
        {
            _receiveToken = _pollingCancellationTokenSource.Token;
        }

        // No token here on purpose: Task.Run would cancel the task before the loop
        // ever ran, and the finally that completes the packet channel would be
        // skipped. PollAsync observes the token itself and exits on the first check.
        _pollingTask = Task.Run(PollAsync);
    }

    private async Task PollAsync()
    {
        var writer = _packetsChannel.Writer;

        try
        {
            while (!_pollingCancellationTokenSource.IsCancellationRequested)
            {
                var packets = await _transport.GetAsync(_receiveToken).ConfigureAwait(false);

                foreach (var packet in packets)
                {
                    // Handle heartbeat packet and yield the other packet types to the caller
                    if (packet.Type == PacketType.Ping)
                    {
                        ResetHeartbeat();
                        await _transport.SendAsync(Packet.PongPacket, _pollingCancellationTokenSource.Token).ConfigureAwait(false);
                        continue;
                    }

                    // The server is done. Leaving the loop entirely matters: `break`
                    // would only leave the foreach, and the next iteration would poll
                    // a transport that has just been disconnected.
                    if (packet.Type == PacketType.Close)
                    {
                        await _transport.Disconnect().ConfigureAwait(false);
                        return;
                    }

                    if (packet.Type == PacketType.Message)
                    {
                        // The channel is unbounded, so this never fails or blocks.
                        writer.TryWrite(packet);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!_pollingCancellationTokenSource.IsCancellationRequested)
        {
            // Only the heartbeat deadline can cancel while the engine is still running.
            await _transport.Disconnect().ConfigureAwait(false);
            HandleException(new TransportException(ErrorReason.ConnectionClosed,
                $"No ping received within {_heartbeatTimeoutMs}ms; the connection is considered closed."));
        }
        catch (OperationCanceledException)
        {
            // Shutting down through DisconnectAsync is not a failure.
        }
        catch (Exception e)
        {
            await _transport.Disconnect().ConfigureAwait(false);
            HandleException(e);
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private void ResetHeartbeat()
    {
        // Reschedules the existing timer rather than building new cancellation state.
        _heartbeatCts?.CancelAfter(_heartbeatTimeoutMs);
    }

    private void HandleException(Exception exception)
    {
        _logger?.LogError(exception, exception.Message);
        ConnectionError = exception;

        // End the stream with the reason it ended. A listener can then tell a
        // connection that died from one the server closed by agreement, which
        // completes the channel without an error.
        _packetsChannel.Writer.TryComplete(exception);
        _pollingCancellationTokenSource.Cancel();
    }

    public async Task DisconnectAsync()
    {
        try
        {
            // Close first, cancel second. Cancelling aborts the in-flight long poll,
            // which the server treats as the transport going away: it discards the
            // session, and the close packet then arrives on a session that is gone.
            await _transport.Disconnect().ConfigureAwait(false);
        }
        finally
        {
            _pollingCancellationTokenSource.Cancel();
        }

        // Let the loop unwind before returning, so the caller can dispose safely.
        if (_pollingTask is not null)
        {
            await _pollingTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Listen for incoming packets.
    /// </summary>
    /// <param name="cancellationToken">IAsyncEnumerable cancellation token</param>
    /// <returns>Packets</returns>
    public async IAsyncEnumerable<Packet> ListenAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reader = _packetsChannel.Reader;

        // Only the caller's token cancels the enumeration. The engine signals the end
        // of the stream by completing the channel, so linking the polling token here
        // would race that completion and surface a cancellation instead of the reason.
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var packet))
            {
                yield return packet;
            }
        }
    }

    /// <summary>
    ///     Send a packet as it stands, leaving its framing to the current transport.
    /// </summary>
    /// <remarks>
    ///     A protocol layered on top of Engine.io does its own encoding and has to say
    ///     which Engine.io packet carries the result — a decision the text and binary
    ///     overloads make on the caller's behalf.
    /// </remarks>
    /// <param name="packet">Packet to send</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(Packet packet, CancellationToken cancellationToken = default)
    {
        await _transport.SendAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Send plain text message.
    /// </summary>
    /// <param name="text">Plain text message</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        await _transport.SendAsync(Packet.CreateMessagePacket(text), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Send binary message.
    /// </summary>
    /// <param name="binary">Binary data</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(ReadOnlyMemory<byte> binary, CancellationToken cancellationToken = default)
    {
        await _transport.SendAsync(Packet.CreateBinaryPacket(binary), cancellationToken).ConfigureAwait(false);
    }
}