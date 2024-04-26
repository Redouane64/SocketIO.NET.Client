using System.Net;

using Moq;
using Moq.Protected;

namespace EngineIO.Client.Tests.Extensions;

public static class MoqExtensions
{
    public static void MockGetByteArrayAsync(this Mock<HttpMessageHandler> handler, byte[] response)
    {
        handler.Protected().Setup<Task<HttpResponseMessage>>(
            "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>()).ReturnsAsync(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(response) });
    }
}