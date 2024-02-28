using System;
using System.Threading.Tasks;
using EngineIO.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace SocketIO.Client.HelloWorld;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => {
                builder.AddSimpleConsole(o => {
                        o.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                        o.SingleLine = true;
                        o.ColorBehavior = LoggerColorBehavior.Enabled;
                }).SetMinimumLevel(LogLevel.Debug);
            });

        using var engine = new Engine("http://127.0.0.1:9854", logger);
        await engine.ConnectAsync();

        Console.ReadKey();
        await engine.DisconnectAsync();
    }
}
