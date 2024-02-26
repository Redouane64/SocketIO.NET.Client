using System;
using System.Threading.Tasks;
using EngineIO.Client;
using Microsoft.Extensions.Logging;

namespace SocketIO.Client.HelloWorld;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => { builder.AddConsole().SetMinimumLevel(LogLevel.Debug); })
            .CreateLogger<Engine>();

        using (var engine = new Engine("http://127.0.0.1:9854", logger))
        {
            await engine.ConnectAsync();

            Console.ReadKey();
            await engine.DisconnectAsync();
        }
    }
}