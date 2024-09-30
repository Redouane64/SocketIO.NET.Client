using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using EngineIO.Client;

using Microsoft.Extensions.Logging;
#if DEBUG
using Microsoft.Extensions.Logging.Console;
#else
using Microsoft.Extensions.Logging.Abstractions;
#endif

namespace PingPong;

internal class Program : IDisposable
{
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<Program> logger;
    private static readonly CancellationTokenSource cts = new();

    Program()
    {
#if DEBUG
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(o =>
            {
                o.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                o.SingleLine = true;
                o.ColorBehavior = LoggerColorBehavior.Enabled;
            }).SetMinimumLevel(LogLevel.Debug);
        });
        
        logger = loggerFactory.CreateLogger<Program>();
#else
        this.loggerFactory = NullLoggerFactory.Instance;
        this.logger = loggerFactory.CreateLogger<Program>();
#endif
        Engine = new Engine((options) =>
        {
            options.BaseAddress = "http://127.0.0.1:9854";
            options.AutoUpgrade = true;
        }, loggerFactory);
    }

    public Engine Engine { get; }

    private static async Task Main(string[] args)
    {
        Console.CancelKeyPress += async (sender, eventArgs) =>
        {
            await cts.CancelAsync();
        };

        var program = new Program();
        program.logger.LogDebug("Client Starting...");

        await program.Engine.ConnectAsync(cts.Token);
        await Task.WhenAll(Task.Run(program.EmitAsync, cts.Token), Task.Run(program.ListenAsync, cts.Token));

        program.logger.LogDebug("Disconnecting...");
        await program.Engine.DisconnectAsync();
    }

    private async Task ListenAsync()
    {
        await foreach (var packet in Engine.ListenAsync(cts.Token))
        {
            var message = Encoding.UTF8.GetString(packet.Body.Span);
            logger.LogInformation("Server: {message}", message);
        }
    }

    private async Task EmitAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(cts.Token))
        {
            await Engine.SendAsync("Ping", cts.Token);
        }
    }

    public void Dispose()
    {
        Engine.Dispose();
    }
}