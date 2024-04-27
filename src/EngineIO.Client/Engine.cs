using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
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
    private readonly IEncoder _base64Encoder = new Base64Encoder();

    private readonly string _baseAddress;
    private readonly ClientOptions _clientOptions = new();
    private readonly HttpClient _httpClient;
    private readonly ILogger<Engine> _logger;
    
    private readonly Channel<Packet> _packetsChannel = Channel.CreateUnbounded<Packet>();
    private readonly ClientWebSocket _webSocketClient;

    private CancellationTokenSource _pollingCancellationTokenSource = new();

    private HttpPollingTransport _httpTransport;
    private ITransport _transport;
    private WebSocketTransport? _wsTransport;

#pragma warning disable CS8618
    public Engine(Action<ClientOptions> configure, ILoggerFactory? loggerFactory = null)
#pragma warning restore CS8618
    {
        configure(_clientOptions);
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(_clientOptions.Uri);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.ConnectionClose = false;

        _webSocketClient = new ClientWebSocket();
        _baseAddress = _clientOptions.Uri;

        if (loggerFactory is not null)
        {
            this._logger = loggerFactory.CreateLogger<Engine>();
        }
    }

    public void Dispose()
    {
        _pollingCancellationTokenSource.Dispose();
        _httpTransport.Dispose();
        _wsTransport?.Dispose();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var (connected, exception) = await TryConnect(cancellationToken);
        if (connected)
        {
            return;
        }
        
        if (!_clientOptions.AutoReconnect)
        {
            if (!connected)
            {
                throw exception!;
            }
        }
        
        int retryCount = 0;
        while (retryCount < _clientOptions.MaxConnectionRetry)
        {
            (connected, exception) = await TryConnect();
            if (connected)
            {
                break;
            }
            
            this._logger.LogError(exception!.Message);
            Interlocked.Increment(ref retryCount);
            
            await Task.Delay(retryCount * 1000, cancellationToken);
        }
        
        if (!connected)
        {
            this._logger.LogError("Unable to establish connection to the remote server: {Message}", exception!.Message);
        }
    }

    private async Task<(bool, Exception?)> TryConnect(CancellationToken cancellationToken = default)
    {
        _transport = _httpTransport = new HttpPollingTransport(_httpClient);
        try
        {
            await _httpTransport.ConnectAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            return (false, exception);
        }

        if (_clientOptions.AutoUpgrade && _httpTransport.Upgrades!.Contains("websocket"))
        {
            try
            {
                _pollingCancellationTokenSource.Cancel();
                _pollingCancellationTokenSource.Dispose();
                _transport = _wsTransport = new WebSocketTransport(_baseAddress, _httpTransport.Sid!, _webSocketClient);
                await _wsTransport.ConnectAsync(cancellationToken);

                _pollingCancellationTokenSource = new CancellationTokenSource();
            }
            catch(Exception exception)
            {
                return (false, exception);
            }
        }

#pragma warning disable CS4014
        Task.Run(PollAsync, _pollingCancellationTokenSource.Token);
#pragma warning restore CS4014
        return (true, null);
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
        if (exception is TransportException transportException)
        {
            // TODO: handle this
        }

        if (exception is WebSocketException webSocketException)
        {
            
        }

        if (exception is HttpRequestException requestException)
        {
            
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