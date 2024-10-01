using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using EngineIO.Client;
using EngineIO.Client.Transports;
using PacketFormat = EngineIO.Client.Packets.PacketFormat;

using SocketIO.Client.Packets;


namespace SocketIO.Client;

public class IO
{
    private static readonly string DefaultPath = "socket.io";
    
    private readonly Engine _client;
    private readonly string _baseUrl;

    public string Path { get; set; } = DefaultPath;
    
    // Map namespace with its corresponding sid
    private readonly Dictionary<string, string> _namespaces = new();

    public IO(string baseUrl)
    {
        this._baseUrl = baseUrl;

        this._client = new Engine((config) =>
        {
            config.BaseAddress = baseUrl;
            config.AutoUpgrade = true;
            config.Path = Path;
            // TODO: allow passing custom headers and queries
        });
    }

    public Task Connect(string? @namespace = default)
    {
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ListenAsync(
        string? @namespace = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // TODO:
        yield return Array.Empty<byte>();
    }

    private Task _Send(Packet packet)
    {
        var buffer = packet.Serialize().ToArray();
        var eioPacker = EngineIO.Client.Packets.Packet.CreatePacket(PacketFormat.PlainText, buffer);

        return this._client.SendAsync(eioPacker.ToWirePacket());
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, string? @namespace = null)
    {
        var packet = new Packet(PacketType.BinaryEvent, @namespace);
        packet.AddItem(data);
        
        // TODO: 
        return this._Send(packet);
    }

    public Task SendAsync(string text, string? @namespace = null)
    {
        var packet = new Packet(PacketType.Event, @namespace);
        packet.AddItem(text);
        return this._Send(packet);
    }
    
    public Task SendAsync<T>(T data, string? @namespace = null) where T : class
    {
        var packet = new Packet(PacketType.Event, @namespace);
        packet.AddItem(data);
        return this._Send(packet);
    }
}