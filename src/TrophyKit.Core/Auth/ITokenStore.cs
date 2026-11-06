using System.Collections.Concurrent;
using System.Text;

namespace TrophyKit.Auth;

/// <summary>
/// Persists authentication tokens between runs so the user does not have to log in again.
/// Values are opaque strings (JSON) produced by the platform clients.
/// </summary>
/// <remarks>
/// Tokens grant access to the user's account. Implementations used in production should encrypt
/// them at rest (e.g. with ASP.NET Core Data Protection or a secrets vault).
/// </remarks>
public interface ITokenStore
{
    /// <summary>Returns the value stored under <paramref name="key"/>, or <see langword="null"/>.</summary>
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>, replacing any previous value.</summary>
    ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Removes the value stored under <paramref name="key"/>, if any.</summary>
    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps tokens in memory only. Tokens are lost when the process exits. This is the default store.
/// </summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_values.TryGetValue(key, out var value) ? value : null);

    /// <inheritdoc />
    public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _values[key] = value;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _values.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Stores each token as a plain-text file in a directory. Handy for console apps and local development.
/// </summary>
/// <remarks>Tokens are <b>not</b> encrypted. Do not use it on shared machines or servers.</remarks>
public sealed class FileTokenStore : ITokenStore, IDisposable
{
    private readonly string _directory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Creates a store that writes files to <paramref name="directory"/> (created if missing).</summary>
    public FileTokenStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
                : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(key);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, value, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            File.Delete(path);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    private string PathFor(string key)
    {
        var safe = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            safe.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        return Path.Combine(_directory, safe + ".json");
    }
}
