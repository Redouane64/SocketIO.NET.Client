using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using EngineIO.Client;

namespace SocketIO.Client;

public class IO
{
    private readonly Engine _client;
    private readonly string _baseUrl;
    
    // Map namespace with its corresponding sid
    private readonly Dictionary<string, string> _namespaces = new();

    public IO(string baseUrl)
    {
        this._baseUrl = baseUrl;

        this._client = new Engine((config) =>
        {
            config.BaseAddress = baseUrl;
            config.AutoUpgrade = true;
            
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
        yield return Array.Empty<byte>();
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, string? @namespace = null)
    {
        return Task.CompletedTask;
    }
    
    public Task SendAsync<T>(T data, string? @namespace = null)
    {
        return Task.CompletedTask;
    }
    
    public Task SendAsync<T>(T[] data, string? @namespace = null)
    {
        return Task.CompletedTask;
    }
}