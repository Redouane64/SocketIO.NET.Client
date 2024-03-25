using System.Text;
using EngineIO.Client.Transport;
using EngineIO.Client.UnitTests.Extensions;
using Moq;

namespace EngineIO.Client.UnitTests.Transports;

public class HttpPollingTransportTests
{
    [Fact]
    async Task Should_Parse_Concatenated_Packets_Correctly()
    {
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.MockGetByteArrayAsync(new [] {
            (byte)'4',
            (byte)'H',
            (byte)'e',
            (byte)'l',
            (byte)'l',
            (byte)'o',
            (byte)0x1e,
            (byte)'2',
            (byte)0x1e,
            (byte)'4',
            (byte)'W',
            (byte)'o',
            (byte)'r',
            (byte)'l',
            (byte)'d',
        });
        var transport = new HttpPollingTransport(
            new HttpClient(mockHttpMessageHandler.Object) { BaseAddress = new Uri("http://foo.bar") });

        var packets = await transport.GetAsync(CancellationToken.None);

        Assert.Equal(3, packets.Count);
        Assert.Equal('4', (char)packets[0].Span[0]);
        Assert.Equal("Hello", Encoding.UTF8.GetString(packets[0][1..].Span));

        Assert.Equal('2', (char)packets[1].Span[0]);
        Assert.Equal('4', (char)packets[2].Span[0]);
        Assert.Equal("World", Encoding.UTF8.GetString(packets[2][1..].Span));

    }
}
