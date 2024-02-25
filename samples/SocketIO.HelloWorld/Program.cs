using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace SocketIO.Client.HelloWorld;

internal class Program
{
    private static async Task Main(string[] args)
    {
        ILogger<Io> logger = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        }).CreateLogger<Io>();

        Io client = new("http://127.0.0.1:9854", logger);
        await client.ConnectAsync();

        var data = client.GetData();

        Console.ReadKey();
    }
}