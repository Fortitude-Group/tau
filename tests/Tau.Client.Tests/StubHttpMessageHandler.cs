using System.Text;

namespace Tau.Client.Tests;

/// <summary>
/// An <see cref="HttpMessageHandler"/> test double: answers every request from a caller-supplied
/// responder, without touching the network, and records every request (and its body) it saw.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, int, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string?> RequestBodies { get; } = [];

    public StubHttpMessageHandler(Func<HttpRequestMessage, string?, int, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    /// <summary>A handler that always returns the same response.</summary>
    public static StubHttpMessageHandler Always(HttpResponseMessage response) =>
        new((_, _, _) => response);

    /// <summary>A handler that always returns a fresh response built by <paramref name="factory"/>.</summary>
    public static StubHttpMessageHandler Always(Func<HttpResponseMessage> factory) =>
        new((_, _, _) => factory());

    /// <summary>A handler that returns each response in <paramref name="responses"/> in turn, repeating the last one.</summary>
    public static StubHttpMessageHandler Sequence(params HttpResponseMessage[] responses) =>
        new((_, _, callIndex) => responses[Math.Min(callIndex, responses.Length - 1)]);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A real handler observes the token; a stub that ignores it would make every
        // cancellation test a false negative.
        cancellationToken.ThrowIfCancellationRequested();

        var callIndex = Requests.Count;
        Requests.Add(request);

        string? body = null;
        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            body = Encoding.UTF8.GetString(bytes);
        }

        RequestBodies.Add(body);
        return _responder(request, body, callIndex);
    }
}
