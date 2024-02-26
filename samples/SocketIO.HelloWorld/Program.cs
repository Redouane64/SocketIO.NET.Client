using System;
using System.Linq;
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

        var http = new HttpClient();
        http.BaseAddress = new Uri("http://127.0.0.1:9854");

        var transport = new HttpPollingTransport(http, logger);
        await transport.Handshake();

        await ListenToPackets(transport);

        Console.ReadKey();
    }

    private static async Task ListenToPackets(HttpPollingTransport transport)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Min(transport.PingTimeout, transport.PingInterval)));

        while (await timer.WaitForNextTickAsync())
        {
            var packets = await transport.GetAsync();
            
            foreach (var packet in packets)
            {
                switch (packet.Type)
                {
                    case PacketType.Close:
                        Console.WriteLine($"[{DateTime.Now}] Connection closed by remote server");
                        
                        break;
                    
                    case PacketType.Ping:
                        Console.WriteLine($"[{DateTime.Now}] Heartbeat <");
                        await transport.SendAsync(Packet.Pong());
                        Console.WriteLine($"[{DateTime.Now}] Heartbeat >");
                        
                        break;
                    
                    case PacketType.Message:
                        Console.WriteLine($"[{DateTime.Now}] Server: {packet}");
                        
                        break;
                    
                    default:
                        Console.WriteLine($"[{DateTime.Now}] Unhandled packet type: {packet.Type}");
                        if (packet.Payload.Any())
                        {
                            Console.WriteLine($"Data: {packet}");
                        }

                        break;
                }
            }

        }
    }
}