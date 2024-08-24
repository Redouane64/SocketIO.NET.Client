using System;
using System.Collections.Generic;

using EngineIO.Client;

namespace SocketIO.Client;

public class IO
{
    private readonly Engine _client;
    
    // Map namespace with its corresponding sid
    private readonly Dictionary<string, string> _namespaces = new();

    public IO()
    {
        
    }
}