using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using EngineIO.Client.Packets;
using EngineIO.Client.Transports;

using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly IEncoder _base64Encoder = new Base64Encoder();
    private readonly ClientOptions _clientOptions = new();
    private readonly ILogger<Engine>? _logger;
    private readonly Channel<Packet> _packetsChannel = Channel.CreateUnbounded<Packet>();
    private readonly CancellationTokenSource _pollingCancellationTokenSource = new();

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
        _pollingCancellationTokenSource.Dispose();
        _httpTransport.Dispose();
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

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        Task.Run(PollAsync, _pollingCancellationTokenSource.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }

    private async Task PollAsync()
    {
        var writer = _packetsChannel.Writer;

        try
        {
            while (!_pollingCancellationTokenSource.IsCancellationRequested)
            {
                var packets = await _transport.GetAsync(_pollingCancellationTokenSource.Token);

                foreach (var data in packets)
                {
                    if (!Packet.TryParse(data, out var packet))
                    {
                        continue;
                    }

                    // Handle heartbeat packet and yield the other packet types to the caller
                    if (packet.Type == PacketType.Ping)
                    {
                        await _transport.SendAsync(Packet.PongPacket.ToPlaintextPacket(), PacketFormat.PlainText,
                            _pollingCancellationTokenSource.Token);
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

    private void HandleException(Exception exception)
    {
        _logger?.LogError(exception, exception.Message);
        // TODO: clean up
        _pollingCancellationTokenSource.Cancel();
    }

    public async Task DisconnectAsync()
    {
        _pollingCancellationTokenSource.Cancel();
        await _transport.Disconnect();
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
        var packet = Packet.CreateMessagePacket(text).ToPlaintextPacket();
        await _transport.SendAsync(packet, PacketFormat.PlainText, cancellationToken);
    }

    /// <summary>
    ///     Send binary message.
    /// </summary>
    /// <param name="binary">Binary data</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(ReadOnlyMemory<byte> binary, CancellationToken cancellationToken = default)
    {
        var packet = Packet.CreateBinaryPacket(binary).ToBinaryPacket(_base64Encoder);
        await _transport.SendAsync(packet, PacketFormat.Binary, cancellationToken);
    }
}