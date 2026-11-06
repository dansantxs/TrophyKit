namespace TrophyKit.Tests.Fakes;

public static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> CollectAsync<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        var list = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            list.Add(item);
        }

        return list;
    }
}
