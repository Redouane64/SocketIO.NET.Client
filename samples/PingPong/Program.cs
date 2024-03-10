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
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(o =>
            {
                o.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                o.SingleLine = true;
                o.ColorBehavior = LoggerColorBehavior.Enabled;
            }).SetMinimumLevel(LogLevel.Debug);
        });

        var logger = loggerFactory.CreateLogger<Program>();

        using var engine = new Engine((options) =>
        {
            options.Uri = "http://127.0.0.1:9854";
            options.AutoUpgrade = false;
        }, loggerFactory);
        await engine.ConnectAsync();

        var cts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            try
            {
                await foreach (var message in engine.Stream(TimeSpan.FromSeconds(1), cts.Token))
                {
                    logger.LogInformation("Server: {0}", message);
                }
            }
            catch (Exception exception)
            {
                // TODO:
                logger.LogError(exception, "Error");
            }
            Console.WriteLine("Streaming completed");
        });
        
        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                await engine.SendAsync("Hello from client!!");
            }
        });

        Console.ReadKey();

        await cts.CancelAsync();
        await engine.DisconnectAsync();
        
    }
}
