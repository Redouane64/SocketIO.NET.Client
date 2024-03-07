using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EngineIO.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace PingPong;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(o =>
            {
                o.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                o.SingleLine = true;
                o.ColorBehavior = LoggerColorBehavior.Enabled;
            }).SetMinimumLevel(LogLevel.Debug);
        });

        using var engine = new Engine("http://127.0.0.1:9854", logger);
        await engine.ConnectAsync();

        var cts = new CancellationTokenSource();

        var streaming = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in engine.Stream(TimeSpan.FromSeconds(1), cts.Token))
                {
                    Console.WriteLine("Server: {0}", Encoding.UTF8.GetString(message));
                }
            }
            catch (Exception exception)
            {
                // TODO:
            }
            Console.WriteLine("Streaming completed");
        }, cts.Token);

        // await engine.Upgrade();

        Console.ReadKey();
        
        await cts.CancelAsync();
        Task.WaitAll(streaming);
        
        await engine.DisconnectAsync();
    }
}
