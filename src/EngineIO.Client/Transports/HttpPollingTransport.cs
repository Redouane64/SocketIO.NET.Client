﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using EngineIO.Client.Packets;
using EngineIO.Client.Transports.Exceptions;

namespace EngineIO.Client.Transports;

public sealed class HttpPollingTransport : ITransport, IDisposable
{
    private readonly HttpClient _httpClient;

    private readonly int _protocol = 4;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly byte _separator = 0x1E;

    public HttpPollingTransport(HttpClient httpClient)
    {
        _httpClient = httpClient;
        Path = $"/engine.io?EIO={_protocol}&transport={Name}";
    }

    public bool Connected { get; private set; }

    public string Path { get; private set; }

    public string? Sid { get; private set; }

    public string[]? Upgrades { get; private set; }

    public int PingInterval { get; private set; }

    public int PingTimeout { get; private set; }

    public int MaxPayload { get; private set; }

    public void Dispose()
    {
        _httpClient.Dispose();
        _semaphore.Dispose();
    }

    public string Name => "polling";

    public async Task Disconnect()
    {
        await SendAsync(Packet.ClosePacket.ToPlaintextPacket(), PacketFormat.PlainText);
    }

    public async Task<ReadOnlyCollection<ReadOnlyMemory<byte>>> GetAsync(CancellationToken cancellationToken = default)
    {
        byte[] data;
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            using var response = await _httpClient.GetAsync(Path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var message = await response.Content.ReadAsStreamAsync();
                    var error = await JsonSerializer.DeserializeAsync<BadRequestError>(message, cancellationToken: cancellationToken);
                    throw new BadRequestException(error!.Code, error.Message!);
                }

                // throw on any other response error
                response.EnsureSuccessStatusCode();
            }

            data = await response.Content.ReadAsByteArrayAsync();
        }
        finally
        {
            _semaphore.Release();
        }

        var packets = new List<ReadOnlyMemory<byte>>();

        var start = 0;
        for (var index = start; index < data.Length; index++)
        {
            if (data[index] == _separator)
            {
                var payload = new ReadOnlyMemory<byte>(data, start, index - start);
                packets.Add(payload);
                start = index + 1;
            }
        }

        if (start < data.Length)
        {
            var payload = new ReadOnlyMemory<byte>(data, start, data.Length - start);
            packets.Add(payload);
        }

        return new ReadOnlyCollection<ReadOnlyMemory<byte>>(packets);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> packets, PacketFormat format,
        CancellationToken cancellationToken = default)
    {
        if (!Connected)
        {
            throw new Exception("Transport is not connected");
        }

        try
        {
            using var content = new ReadOnlyMemoryContent(packets);
            content.Headers.ContentType = format == PacketFormat.Binary
                ? new MediaTypeHeaderValue("application/octet-stream")
                : new MediaTypeHeaderValue("text/plain") { CharSet = Encoding.UTF8.WebName };

            await _semaphore.WaitAsync(cancellationToken);
            using var response = await _httpClient.PostAsync(Path, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var error = await JsonSerializer.DeserializeAsync<BadRequestError>(
                        await response.Content.ReadAsStreamAsync(), cancellationToken: cancellationToken);

                    throw new BadRequestException(error!.Code, error.Message!);
                }

                // Throw any other an unexpected exception
                response.EnsureSuccessStatusCode();
            }

        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (Connected)
        {
            return;
        }

        ReadOnlyCollection<ReadOnlyMemory<byte>> response;
        try
        {
            response = await GetAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            throw new Exception("Unable to connect", exception);
        }

        if (!Packet.TryParse(response[0], out var packet))
        {
            throw new Exception("Unexpected packet type");
        }

        if (packet.Type != PacketType.Open)
        {
            throw new Exception("Unexpected packet type");
        }

        var handshake = JsonSerializer
            .Deserialize<HandshakePacket>(packet.Body.Span);

#pragma warning disable CS8602
        Sid = handshake.Sid;
#pragma warning restore CS8602
        MaxPayload = handshake.MaxPayload;
        PingInterval = handshake.PingInterval;
        PingTimeout = handshake.PingTimeout;
        Upgrades = handshake.Upgrades;

        Path += $"&sid={Sid}";
        Connected = true;
    }

    private class HandshakePacket
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("upgrades")]
        public string[]? Upgrades { get; set; }

        [JsonPropertyName("pingInterval")]
        public int PingInterval { get; set; }

        [JsonPropertyName("pingTimeout")]
        public int PingTimeout { get; set; }

        [JsonPropertyName("maxPayload")]
        public int MaxPayload { get; set; }
    }
}
