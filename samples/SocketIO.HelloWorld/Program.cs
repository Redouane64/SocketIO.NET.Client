using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EngineIO.Client;
using Microsoft.Extensions.Logging;

namespace SocketIO.Client.HelloWorld;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => { builder.AddConsole().SetMinimumLevel(LogLevel.Debug); })
            .CreateLogger<HttpPollingTransport>();

        var http = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = Timeout.InfiniteTimeSpan
        });
        http.BaseAddress = new Uri("http://127.0.0.1:9854");

        var transport = new HttpPollingTransport(http, logger);
        await transport.Handshake();

        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(transport.PingInterval));

        while (await timer.WaitForNextTickAsync())
        {
            var binary = await transport.GetAsync();
            var packet = transport.Serializer.Deserialize(binary);

            switch (packet.Type)
            {
                case "1":
                    Console.WriteLine("Connection closed by remote server");
                    
                    break;
                
                case "2":
                    Console.WriteLine("Heartbeat <");
                    var pong = transport.Serializer.Serialize("3", null);
                    await transport.SendAsync(pong);
                    Console.WriteLine("Heartbeat >");
                    
                    break;
                
                default:
                    Console.WriteLine($"Unhandled packet type: {packet.Type}");
                    if (packet.Data is not null)
                    {
                        Console.WriteLine($"Data: {packet.Data}");
                    }

                    break;
            }
        }


        Console.ReadKey();
    }
}