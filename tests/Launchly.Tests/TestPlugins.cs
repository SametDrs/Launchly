using Launchly.Core;
using Launchly.Core.Plugins;
using Launchly.Plugin;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Tests;

/// <summary>Testlerde kullanılan sahte eklenti; davranışı verilen temsilciyle belirlenir.</summary>
internal sealed class FakePlugin : IPlugin
{
    private readonly Func<Query, CancellationToken, Task<IReadOnlyList<Result>>> _handler;
    private int _queryCount;

    public FakePlugin(
        string id,
        string[] keywords,
        Func<Query, CancellationToken, Task<IReadOnlyList<Result>>> handler,
        int searchDelayMs = 0,
        Func<Task>? init = null)
    {
        _handler = handler;
        InitHandler = init;
        Metadata = new PluginMetadata { Id = id, Name = id, ActionKeywords = keywords, SearchDelayMs = searchDelayMs };
    }

    public PluginMetadata Metadata { get; }

    public Func<Task>? InitHandler { get; }

    public int QueryCount => Volatile.Read(ref _queryCount);

    public List<Query> Queries { get; } = [];

    public PluginContext? Context { get; private set; }

    public Task InitAsync(PluginContext context)
    {
        Context = context;
        return InitHandler?.Invoke() ?? Task.CompletedTask;
    }

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _queryCount);
        lock (Queries)
        {
            Queries.Add(query);
        }

        return _handler(query, cancellationToken);
    }

    public static Task<IReadOnlyList<Result>> Results(params Result[] results) =>
        Task.FromResult<IReadOnlyList<Result>>(results);

    public static Result R(string title, int score = 0, Func<Task<bool>>? action = null, string autoComplete = "") =>
        new() { Title = title, Score = score, Action = action, AutoComplete = autoComplete };
}

/// <summary>PluginManager.QueryAsync yayınlarını sırayla kaydeder (iş parçacığı güvenli).</summary>
internal sealed class PublishLog
{
    private readonly object _gate = new();
    private readonly List<string[]> _publishes = [];

    public void Add(IReadOnlyList<Result> results)
    {
        lock (_gate)
        {
            _publishes.Add(results.Select(r => r.Title).ToArray());
        }
    }

    public IReadOnlyList<string[]> Publishes
    {
        get
        {
            lock (_gate)
            {
                return _publishes.ToArray();
            }
        }
    }

    public string[] Last
    {
        get
        {
            lock (_gate)
            {
                return _publishes.Count == 0 ? [] : _publishes[^1];
            }
        }
    }

    public IEnumerable<string> AllTitles => Publishes.SelectMany(p => p);
}

internal static class TestHelpers
{
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    public static PluginManager CreateManager(
        IEnumerable<IPlugin> plugins,
        Settings? settings = null,
        TimeSpan? mergeWindow = null) =>
        new(plugins, settings ?? new Settings(), NullLogger<PluginManager>.Instance,
            Path.Combine(Path.GetTempPath(), "LaunchlyTests", Guid.NewGuid().ToString("N")))
        {
            MergeWindow = mergeWindow ?? TimeSpan.FromMilliseconds(20),
        };

    public static async Task WaitUntilAsync(Func<bool> condition, string because = "condition")
    {
        var deadline = DateTime.UtcNow + TestTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Zaman aşımı: {because}");
            }

            await Task.Delay(5);
        }
    }
}
