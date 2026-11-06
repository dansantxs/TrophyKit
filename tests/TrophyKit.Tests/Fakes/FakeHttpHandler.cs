using System.Net;
using System.Text;

namespace TrophyKit.Tests.Fakes;

/// <summary>Records requests and answers them with queued or routed responses.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _queue = new();
    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    public FakeHttpHandler Enqueue(HttpStatusCode status, string? body = null, Action<HttpResponseMessage>? configure = null)
    {
        _queue.Enqueue(_ =>
        {
            var response = Json(status, body);
            configure?.Invoke(response);
            return Task.FromResult(response);
        });
        return this;
    }

    public FakeHttpHandler EnqueueException(Exception exception)
    {
        _queue.Enqueue(_ => Task.FromException<HttpResponseMessage>(exception));
        return this;
    }

    public FakeHttpHandler EnqueueHang()
    {
        _queue.Enqueue(async _ =>
        {
            await Task.Delay(Timeout.Infinite, CancellationToken.None).WaitAsync(_hangToken);
            throw new InvalidOperationException();
        });
        return this;
    }

    private readonly object _sync = new();
    private CancellationToken _hangToken;

    /// <summary>Answers every request whose URL contains <paramref name="urlPart"/>.</summary>
    public FakeHttpHandler Route(string urlPart, HttpStatusCode status, string body, HttpMethod? method = null)
        => Route(r => r.RequestUri!.ToString().Contains(urlPart, StringComparison.Ordinal) && (method is null || r.Method == method),
            _ => Json(status, body));

    public FakeHttpHandler Route(Func<HttpRequestMessage, bool> match, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((match, respond));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
            body);

        Func<HttpRequestMessage, Task<HttpResponseMessage>>? queued = null;
        lock (_sync)
        {
            Requests.Add(recorded);
            _hangToken = cancellationToken;
            if (_queue.Count > 0)
            {
                queued = _queue.Dequeue();
            }
        }

        HttpResponseMessage response;
        if (queued is not null)
        {
            response = await queued(request);
        }
        else
        {
            var route = _routes.FirstOrDefault(r => r.Match(request));
            if (route.Respond is null)
            {
                throw new InvalidOperationException($"No fake response for {request.Method} {request.RequestUri}");
            }

            response = route.Respond(request);
        }

        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string? body) => new(status)
    {
        Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json"),
    };
}

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
}
