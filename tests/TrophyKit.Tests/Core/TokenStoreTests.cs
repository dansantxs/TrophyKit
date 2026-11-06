using TrophyKit.Auth;

namespace TrophyKit.Tests.Core;

public class TokenStoreTests
{
    [Fact]
    public async Task InMemory_roundtrip()
    {
        var store = new InMemoryTokenStore();
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await store.GetAsync("k", ct));
        await store.SetAsync("k", "v", ct);
        Assert.Equal("v", await store.GetAsync("k", ct));
        await store.RemoveAsync("k", ct);
        Assert.Null(await store.GetAsync("k", ct));
    }

    [Fact]
    public async Task File_roundtrip_with_unsafe_key()
    {
        var dir = Path.Combine(Path.GetTempPath(), "trophykit-tests", Guid.NewGuid().ToString("N"));
        using var store = new FileTokenStore(dir);
        var ct = TestContext.Current.CancellationToken;

        await store.SetAsync("psn:tokens/../x", "{\"a\":1}", ct);
        Assert.Equal("{\"a\":1}", await store.GetAsync("psn:tokens/../x", ct));
        Assert.Single(Directory.GetFiles(dir));
        await store.RemoveAsync("psn:tokens/../x", ct);
        Assert.Null(await store.GetAsync("psn:tokens/../x", ct));

        Directory.Delete(dir, recursive: true);
    }
}
