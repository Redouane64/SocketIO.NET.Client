using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using EngineIO.Client.Packets;
using EngineIO.Client.Transports;
using EngineIO.Client.Transports.Exceptions;

using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    /// <summary>
    ///     How long <see cref="Dispose" /> waits for the receive loop to unwind.
    /// </summary>
    private static readonly TimeSpan PollingShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly ClientOptions _clientOptions = new();
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

    public bool Connected => _transport.Connected;

    public void Dispose()
    {
        _pollingCancellationTokenSource.Cancel();

        // The receive loop holds the transports and their semaphores. Disposing those
        // while it is still running makes it fail on a disposed object.
        try
        {
            _pollingTask?.Wait(PollingShutdownTimeout);
        }
        catch (AggregateException)
        {
            // PollAsync reports its own failures through the logger.
        }

        _pollingCancellationTokenSource.Dispose();
        _heartbeatCts?.Dispose();
        _httpTransport?.Dispose();
        _wsTransport?.Dispose();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _transport = _httpTransport = new HttpPollingTransport(_clientOptions.BaseAddress);
        try
        {
            await _httpTransport.ConnectAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            HandleException(exception);
            return;
        }

        if (_clientOptions.AutoUpgrade && _httpTransport.Upgrades!.Contains("websocket"))
        {
            try
            {
                _transport = _wsTransport = new WebSocketTransport(_clientOptions.BaseAddress, _httpTransport.Sid!);
                await _wsTransport.ConnectAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                HandleException(exception);
                return;
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

        _pollingTask = Task.Run(PollAsync, _pollingCancellationTokenSource.Token);
    }

    private async Task PollAsync()
    {
        var writer = _packetsChannel.Writer;

        try
        {
            while (!_pollingCancellationTokenSource.IsCancellationRequested)
            {
                var packets = await _transport.GetAsync(_receiveToken);

                foreach (var packet in packets)
                {
                    // Handle heartbeat packet and yield the other packet types to the caller
                    if (packet.Type == PacketType.Ping)
                    {
                        ResetHeartbeat();
                        await _transport.SendAsync(Packet.PongPacket, _pollingCancellationTokenSource.Token);
                        continue;
                    }

                    // The server is done. Leaving the loop entirely matters: `break`
                    // would only leave the foreach, and the next iteration would poll
                    // a transport that has just been disconnected.
                    if (packet.Type == PacketType.Close)
                    {
                        await _transport.Disconnect();
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
            await _transport.Disconnect();
            HandleException(new TransportException(ErrorReason.ConnectionClosed,
                $"No ping received within {_heartbeatTimeoutMs}ms; the connection is considered closed."));
        }
        catch (OperationCanceledException)
        {
            // Shutting down through DisconnectAsync is not a failure.
        }
        catch (Exception e)
        {
            await _transport.Disconnect();
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
        // TODO: clean up
        _pollingCancellationTokenSource.Cancel();
    }

    public async Task DisconnectAsync()
    {
        try
        {
            // Close first, cancel second. Cancelling aborts the in-flight long poll,
            // which the server treats as the transport going away: it discards the
            // session, and the close packet then arrives on a session that is gone.
            await _transport.Disconnect();
        }
        finally
        {
            _pollingCancellationTokenSource.Cancel();
        }

        // Let the loop unwind before returning, so the caller can dispose safely.
        if (_pollingTask is not null)
        {
            await _pollingTask;
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
        var listenerCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(this._pollingCancellationTokenSource.Token,
            cancellationToken);
        while (await reader.WaitToReadAsync(listenerCancellationToken.Token))
        {
            while (reader.TryRead(out var packet))
            {
                yield return packet;
            }
        }
    }

    /// <summary>
    ///     Send plain text message.
    /// </summary>
    /// <param name="text">Plain text message</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        await _transport.SendAsync(Packet.CreateMessagePacket(text), cancellationToken);
    }

    /// <summary>
    ///     Send binary message.
    /// </summary>
    /// <param name="binary">Binary data</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(ReadOnlyMemory<byte> binary, CancellationToken cancellationToken = default)
    {
        await _transport.SendAsync(Packet.CreateBinaryPacket(binary), cancellationToken);
    }
}