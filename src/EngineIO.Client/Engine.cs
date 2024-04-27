using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private readonly ILogger<Engine> _logger;
    private readonly Channel<Packet> _packetsChannel = Channel.CreateUnbounded<Packet>();
    private readonly CancellationTokenSource _pollingCancellationTokenSource = new();

    private ITransport _transport;
    private HttpPollingTransport _httpTransport;
    private WebSocketTransport? _wsTransport;

#pragma warning disable CS8618
    public Engine(Action<ClientOptions> configure, ILoggerFactory? loggerFactory = null)
#pragma warning restore CS8618
    {
        configure(_clientOptions);
        if (loggerFactory is not null)
        {
            this._logger = loggerFactory.CreateLogger<Engine>();
        }
    }

    public bool Connected { get; private set; }

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

#pragma warning disable CS4014
        Task.Run(PollAsync, _pollingCancellationTokenSource.Token);
#pragma warning restore CS4014
        Connected = true;
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
                _transport.Close();
                HandleException(e);
                return;
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
#pragma warning disable CS4014
                    _transport.SendAsync(Packet.PongPacket.ToPlaintextPacket(), PacketFormat.PlainText,
                        _pollingCancellationTokenSource.Token);
#pragma warning restore CS4014
                    continue;
                }

                if (packet.Type == PacketType.Close)
                {
                    writer.Complete();
                    _transport.Close();
                    break;
                }

                if (packet.Type == PacketType.Message)
                {
#pragma warning disable CS4014
                    writer.WriteAsync(packet, _pollingCancellationTokenSource.Token);
#pragma warning restore CS4014
                }
            }
        }
    }

    private void HandleException(Exception exception)
    {
        _logger.LogError(exception, exception.Message);
        // TODO: clean up
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
        if (!Connected)
        {
            yield break;
        }
        
        var reader = _packetsChannel.Reader;
        while (await reader.WaitToReadAsync(cancellationToken))
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