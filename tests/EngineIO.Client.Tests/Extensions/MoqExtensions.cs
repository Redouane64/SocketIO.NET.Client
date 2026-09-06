using System.Net;

using Moq;
using Moq.Protected;

namespace EngineIO.Client.Tests.Extensions;

/// <summary>
///     A request the mocked server received.
/// </summary>
public sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? ContentType, byte[] Body);

public static class MoqExtensions
{
    public static void MockGetByteArrayAsync(this Mock<HttpMessageHandler> handler, byte[] response)
    {
        handler.Protected().Setup<Task<HttpResponseMessage>>(
            "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>()).ReturnsAsync(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(response) });
    }

    /// <summary>
    ///     Stand in for an Engine.IO server over HTTP long-polling: answer each GET with
    ///     the next scripted payload, answer every POST with "ok" the way the protocol
    ///     requires, and record every request for inspection.
    /// </summary>
    /// <param name="handler">Handler to set up</param>
    /// <param name="pollResponses">Payloads to return from successive GETs. The last one
    ///     is repeated once the script runs out, so a poll loop never starves.</param>
    /// <returns>The requests the transport issued, in order.</returns>
    public static List<CapturedRequest> MockPollingServer(
        this Mock<HttpMessageHandler> handler, params byte[][] pollResponses)
    {
        var requests = new List<CapturedRequest>();
        var polls = 0;

        handler.Protected().Setup<Task<HttpResponseMessage>>(
            "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, _) =>
            {
                var body = request.Content is null
                    ? Array.Empty<byte>()
                    : await request.Content.ReadAsByteArrayAsync();

                lock (requests)
                {
                    requests.Add(new CapturedRequest(request.Method, request.RequestUri!,
                        request.Content?.Headers.ContentType?.ToString(), body));
                }

                if (request.Method != HttpMethod.Get)
                {
                    return Ok("ok"u8.ToArray());
                }

                if (pollResponses.Length == 0)
                {
                    return Ok(Array.Empty<byte>());
                }

                var index = Math.Min(Interlocked.Increment(ref polls) - 1, pollResponses.Length - 1);
                return Ok(pollResponses[index]);
            });

        return requests;
    }

    private static HttpResponseMessage Ok(byte[] body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    }
}