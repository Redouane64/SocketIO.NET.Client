using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EngineIO.Client.Packets;
using EngineIO.Client.Transports;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly IEncoder _base64Encoder = new Base64Encoder();

    private readonly string _baseAddress;
    private readonly ClientOptions _clientOptions = new();
    private readonly HttpClient _httpClient;

    private readonly Channel<Packet> _packetsChannel = Channel.CreateUnbounded<Packet>();
    private readonly ClientWebSocket _webSocketClient;

    private bool _connected;
    private HttpPollingTransport _httpTransport;

    private CancellationTokenSource _pollingCancellationTokenSource = new();
    private ITransport _transport;
    private WebSocketTransport _wsTransport;

#pragma warning disable CS8618
    public Engine(Action<ClientOptions> configure)
#pragma warning restore CS8618
    {
        configure(_clientOptions);
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(_clientOptions.Uri);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.ConnectionClose = false;

        _webSocketClient = new ClientWebSocket();
        _baseAddress = _clientOptions.Uri;
    }

    public void Dispose()
    {
        _pollingCancellationTokenSource?.Dispose();
        _httpTransport?.Dispose();
        _wsTransport?.Dispose();
    }

    public async Task ConnectAsync()
    {
        _transport = _httpTransport = new HttpPollingTransport(_httpClient);
        await _httpTransport.ConnectAsync(_pollingCancellationTokenSource.Token);

        if (_clientOptions.AutoUpgrade && _httpTransport.Upgrades!.Contains("websocket"))
        {
            _pollingCancellationTokenSource.Cancel();
            _pollingCancellationTokenSource.Dispose();
            _transport = _wsTransport = new WebSocketTransport(_baseAddress, _httpTransport.Sid!, _webSocketClient);
            await _wsTransport.ConnectAsync();

            _pollingCancellationTokenSource = new CancellationTokenSource();
        }

        _connected = true;
#pragma warning disable CS4014
        Task.Run(StartPolling, _pollingCancellationTokenSource.Token).ConfigureAwait(false);
#pragma warning restore CS4014
    }

    private void StartPolling()
    {
        PollAsync().ConfigureAwait(false);
    }

    private async Task PollAsync()
    {
        var writer = _packetsChannel.Writer;

        while (!_pollingCancellationTokenSource.IsCancellationRequested)
        {
            var packets = await _transport.GetAsync(_pollingCancellationTokenSource.Token);

            foreach (var data in packets)
            {
                if (!Packet.TryParse(data, out var packet))
                {
                    Debug.WriteLine("polling encountered an invalid packet");
                    continue;
                }

                // Handle heartbeat packet and yield the other packet types to the caller
                if (packet.Type == PacketType.Ping)
                {
#pragma warning disable CS4014
                    _transport.SendAsync(Packet.PongPacket.ToPlaintextPacket(), PacketFormat.PlainText,
                        _pollingCancellationTokenSource.Token).ConfigureAwait(false);
#pragma warning restore CS4014
                    continue;
                }

                if (packet.Type == PacketType.Close)
                {
                    writer.Complete();
                    await DisconnectAsync();
                    break;
                }

                if (packet.Type == PacketType.Message)
                {
#pragma warning disable CS4014
                    writer.WriteAsync(packet, _pollingCancellationTokenSource.Token).ConfigureAwait(false);
#pragma warning restore CS4014
                }
            }
        }
    }

    public async Task DisconnectAsync()
    {
        if (!_connected)
        {
            return;
        }

        _pollingCancellationTokenSource.Cancel();
        await _transport.Disconnect();
        _connected = false;
    }

    /// <summary>
    ///     Listen for messages stream.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>Packets</returns>
    public async IAsyncEnumerable<Packet> ListenAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reader = _packetsChannel.Reader;
        while (!cancellationToken.IsCancellationRequested)
        {
            Packet packet;
            try
            {
                packet = await reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                yield break;
            }

            yield return packet;
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
