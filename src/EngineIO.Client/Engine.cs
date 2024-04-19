using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EngineIO.Client.Packets;
using EngineIO.Client.Transports;
using EngineIO.Client.Transports.Exceptions;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly IEncoder _base64Encoder = new Base64Encoder();

    private readonly string _baseAddress;
    private readonly ClientOptions _clientOptions = new();
    private readonly HttpClient _httpClient;
    
    private readonly Channel<Packet> _packetsChannel = Channel.CreateUnbounded<Packet>();
    private readonly ClientWebSocket _webSocketClient;
    
    private CancellationTokenSource _pollingCancellationTokenSource = new();
    
    private HttpPollingTransport _httpTransport;
    private ITransport _transport;
    private WebSocketTransport _wsTransport;

    private int _retryCount = 0, _maxRetryCount = 3;
    private bool _errored = false;

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
        _pollingCancellationTokenSource.Dispose();
        _httpTransport.Dispose();
        _wsTransport.Dispose();
    }

    public async Task ConnectAsync()
    {
        _transport = _httpTransport = new HttpPollingTransport(_httpClient);

        while (_retryCount <= _maxRetryCount)
        {
            try
            { 
                await _httpTransport.ConnectAsync(_pollingCancellationTokenSource.Token);
                break;
            }
            catch
            {
                Debug.WriteLine("Connection error, retrying...");
                Interlocked.Increment(ref _retryCount);
                _errored = true;
            }

            await Task.Delay(_retryCount * 1000);
        }

        if (_errored)
        {
            throw new Exception("Unable to connecto the remote server");
        }

        if (_clientOptions.AutoUpgrade && _httpTransport.Upgrades!.Contains("websocket"))
        {
            _pollingCancellationTokenSource.Cancel();
            _pollingCancellationTokenSource.Dispose();
            _transport = _wsTransport = new WebSocketTransport(_baseAddress, _httpTransport.Sid!, _webSocketClient);
            await _wsTransport.ConnectAsync();

            _pollingCancellationTokenSource = new CancellationTokenSource();
        }

#pragma warning disable CS4014
        Task.Run(PollAsync, _pollingCancellationTokenSource.Token);
#pragma warning restore CS4014
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
                writer.Complete(e);
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
        if (exception is TransportException transportException)
        {
            // TODO: handle this
        }

        if (exception is WebSocketException webSocketException)
        {
            // TODO: handle this
        }

        if (exception is HttpRequestException requestException)
        {
            // TODO: handle this
        }
    }

    public async Task DisconnectAsync()
    {
        _pollingCancellationTokenSource.Cancel();
        await _transport.Disconnect();
    }

    /// <summary>
    ///     Listen for messages stream.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>Packets</returns>
    public IAsyncEnumerable<Packet> ListenAsync(CancellationToken cancellationToken = default)
    {
        var reader = _packetsChannel.Reader;
        return reader.ReadAllAsync(cancellationToken);
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
