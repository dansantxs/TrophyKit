using System.Net;
using System.Net.Http.Headers;
using TrophyKit.Http;
using TrophyKit.Tests.Fakes;

namespace TrophyKit.Tests.Core;

public class HttpPipelineTests
{
    private static TrophyKitHttpOptions FastOptions(int retries = 3) => new()
    {
        MaxRetries = retries,
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        RetryMaxDelay = TimeSpan.FromMilliseconds(5),
        AttemptTimeout = TimeSpan.FromMilliseconds(200),
    };

    [Fact]
    public async Task Retries_transient_status_then_succeeds()
    {
        var fake = new FakeHttpHandler()
            .Enqueue(HttpStatusCode.ServiceUnavailable)
            .Enqueue(HttpStatusCode.TooManyRequests)
            .Enqueue(HttpStatusCode.OK, "{}");
        using var client = TrophyKitHttp.CreateClient(FastOptions(), null, "Test", fake);

        using var response = await client.GetAsync(new Uri("https://example.test/a"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, fake.Requests.Count);
    }

    [Fact]
    public async Task Stops_after_max_retries()
    {
        var fake = new FakeHttpHandler();
        for (var i = 0; i < 5; i++)
        {
            fake.Enqueue(HttpStatusCode.BadGateway);
        }

        using var client = TrophyKitHttp.CreateClient(FastOptions(retries: 2), null, "Test", fake);
        using var response = await client.GetAsync(new Uri("https://example.test/a"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(3, fake.Requests.Count);
    }

    [Fact]
    public async Task Does_not_retry_client_errors()
    {
        var fake = new FakeHttpHandler().Enqueue(HttpStatusCode.BadRequest);
        using var client = TrophyKitHttp.CreateClient(FastOptions(), null, "Test", fake);

        using var response = await client.GetAsync(new Uri("https://example.test/a"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Gives_up_when_retry_after_exceeds_limit()
    {
        var fake = new FakeHttpHandler().Enqueue(
            HttpStatusCode.TooManyRequests,
            configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10)));
        using var client = TrophyKitHttp.CreateClient(FastOptions(), null, "Test", fake);

        using var response = await client.GetAsync(new Uri("https://example.test/a"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Single(fake.Requests);
        var ex = HttpErrors.Create(response, "Test", default, null);
        var rate = Assert.IsType<TrophyKitRateLimitException>(ex);
        Assert.Equal(TimeSpan.FromMinutes(10), rate.RetryAfter);
    }

    [Fact]
    public async Task Retries_network_errors_and_timeouts()
    {
        var fake = new FakeHttpHandler()
            .EnqueueException(new HttpRequestException("boom"))
            .EnqueueHang()
            .Enqueue(HttpStatusCode.OK, "{}");
        using var client = TrophyKitHttp.CreateClient(FastOptions(), null, "Test", fake);

        using var response = await client.GetAsync(new Uri("https://example.test/a"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, fake.Requests.Count);
    }

    [Fact]
    public async Task Timeout_surfaces_as_TimeoutException_when_retries_exhausted()
    {
        var fake = new FakeHttpHandler().EnqueueHang();
        using var client = TrophyKitHttp.CreateClient(FastOptions(retries: 0), null, "Test", fake);

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.GetAsync(new Uri("https://example.test/a"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Local_rate_limiter_rejects_when_queue_is_full()
    {
        var options = FastOptions();
        options.RateLimitPermits = 1;
        options.RateLimitWindow = TimeSpan.FromHours(1);
        options.RateLimitQueueLimit = 0;
        using var limiter = RateLimitHandler.CreateLimiter(options);
        var fake = new FakeHttpHandler().Enqueue(HttpStatusCode.OK, "{}");
        using var client = TrophyKitHttp.CreateClient(options, limiter, "Test", fake);

        using var first = await client.GetAsync(new Uri("https://example.test/a"), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TrophyKitRateLimitException>(
            () => client.GetAsync(new Uri("https://example.test/a"), TestContext.Current.CancellationToken));
        Assert.Single(fake.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(TrophyKitAuthenticationException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(TrophyKitAccessDeniedException))]
    [InlineData(HttpStatusCode.NotFound, typeof(TrophyKitNotFoundException))]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(TrophyKitRateLimitException))]
    [InlineData(HttpStatusCode.BadRequest, typeof(TrophyKitException))]
    public async Task Maps_status_codes_to_exceptions(HttpStatusCode status, Type expected)
    {
        using var response = FakeHttpHandler.Json(status, """{"code":"X1","message":"nope"}""");
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.test/path");

        var ex = await Assert.ThrowsAnyAsync<TrophyKitException>(() => HttpErrors.EnsureSuccessAsync(
            response, "Test", body => new PlatformError("X1", "nope"), TestContext.Current.CancellationToken));

        Assert.IsType(expected, ex);
        Assert.Equal("Test", ex.Platform);
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal("X1", ex.ErrorCode);
        Assert.Contains("/path", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_wraps_invalid_json()
    {
        using var response = FakeHttpHandler.Json(HttpStatusCode.OK, "<html>");

        var ex = await Assert.ThrowsAsync<TrophyKitException>(
            () => HttpJson.ReadAsync<Dictionary<string, string>>(response, "Test", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("unexpected format", ex.Message, StringComparison.Ordinal);
    }
}
