using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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

        while (!_pollingCancellationTokenSource.IsCancellationRequested)
        {
            ReadOnlyCollection<ReadOnlyMemory<byte>> packets;
            try
            {
                packets = await _transport.GetAsync(_pollingCancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                writer.Complete();
                await _transport.Disconnect();
                HandleException(e);
                break;
            }

            foreach (var data in packets)
            {
                if (!Packet.TryParse(data, out var packet))
                {
                    continue;
                }

                // Handle heartbeat packet and yield the other packet types to the caller
                if (packet.Type == PacketType.Ping)
                {

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    _transport.SendAsync(Packet.PongPacket.ToWirePacket(), PacketFormat.PlainText,
                        _pollingCancellationTokenSource.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                    continue;
                }

                if (packet.Type == PacketType.Close)
                {
                    writer.Complete();
                    await _transport.Disconnect();
                    break;
                }

                if (packet.Type == PacketType.Message)
                {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    writer.WriteAsync(packet, _pollingCancellationTokenSource.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                }
            }
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
        var body = Encoding.UTF8.GetBytes(text);
        var packet = Packet.CreatePacket(PacketFormat.PlainText, body).ToWirePacket();
        await _transport.SendAsync(packet, PacketFormat.PlainText, cancellationToken);
    }

    /// <summary>
    ///     Send binary message.
    /// </summary>
    /// <param name="binary">Binary data</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(ReadOnlyMemory<byte> binary, CancellationToken cancellationToken = default)
    {
        var packet = Packet.CreatePacket(PacketFormat.Binary, binary).ToWirePacket(_base64Encoder);
        await _transport.SendAsync(packet, PacketFormat.Binary, cancellationToken);
    }
}