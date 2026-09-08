using System.Net;
using System.Text;

namespace SocketIO.Client.Tests;

/// <summary>
///     A request the fake server received.
/// </summary>
public sealed record CapturedRequest(HttpMethod Method, Uri Uri, byte[] Body)
{
    public string Text => Encoding.UTF8.GetString(Body);
}

/// <summary>
///     Stands in for an Engine.io server over HTTP long-polling: answers each GET with
///     the next scripted payload, answers every POST the way the protocol requires, and
///     records every request in the order it arrived.
/// </summary>
/// <remarks>
///     Hand-rolled rather than mocked because the ordering tests need the requests as a
///     sequence, and because this project takes no test-double dependency.
/// </remarks>
public sealed class FakePollingServer : HttpMessageHandler
{
    private readonly List<CapturedRequest> _requests = new();
    private readonly byte[][] _pollResponses;
    private int _polls;

    public FakePollingServer(params byte[][] pollResponses)
    {
        _pollResponses = pollResponses;
    }

    /// <summary>
    ///     How long a GET takes to answer, the way a real long poll does. Keeps the
    ///     receive loop from spinning while a test is doing something else.
    /// </summary>
    public TimeSpan PollDelay { get; init; } = TimeSpan.FromMilliseconds(20);

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>
    ///     The bodies the client posted, in order.
    /// </summary>
    public IReadOnlyList<string> Posts
    {
        get { return Requests.Where(request => request.Method == HttpMethod.Post).Select(r => r.Text).ToArray(); }
    }

    public static byte[] Handshake(string upgrades = "")
    {
        return Encoding.UTF8.GetBytes(
            $$"""0{"sid":"1NkM2QzZGMjEyMTIxCg","maxPayload":1000000,"pingTimeout":20000,"pingInterval":25000,"upgrades":[{{upgrades}}]}""");
    }

    public static byte[] Payload(string payload)
    {
        return Encoding.UTF8.GetBytes(payload);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        lock (_requests)
        {
            _requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));
        }

        if (request.Method != HttpMethod.Get)
        {
            return Ok("ok"u8.ToArray());
        }

        if (PollDelay > TimeSpan.Zero)
        {
            await Task.Delay(PollDelay, cancellationToken);
        }

        if (_pollResponses.Length == 0)
        {
            return Ok(Array.Empty<byte>());
        }

        // The last scripted response is repeated once the script runs out, so a poll
        // loop never starves.
        var index = Math.Min(Interlocked.Increment(ref _polls) - 1, _pollResponses.Length - 1);
        return Ok(_pollResponses[index]);
    }

    private static HttpResponseMessage Ok(byte[] body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    }
}