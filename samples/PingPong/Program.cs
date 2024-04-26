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
        CancellationTokenSource cts = new();

        using var engine = new Engine((options) =>
        {
            options.Uri = "http://127.0.0.1:9854";
            options.AutoUpgrade = false;
        }, loggerFactory);

        Console.CancelKeyPress += async (sender, eventArgs) =>
        {
            await cts.CancelAsync();
        };

        await engine.ConnectAsync();

        /*
        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            while (await timer.WaitForNextTickAsync())
            {
                await engine.SendAsync("Hello from client!!");
            }
        });
        */

        await foreach (var packet in engine.ListenAsync(cts.Token))
        {
            var message = Encoding.UTF8.GetString(packet.Body.Span);
            logger.LogInformation("Server: {message}", message);
        }

        await engine.DisconnectAsync();
        Console.ReadKey();
    }

}