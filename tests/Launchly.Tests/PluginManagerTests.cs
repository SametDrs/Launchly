using System.Diagnostics;
using Launchly.Core;
using Launchly.Plugin;
using static Launchly.Tests.FakePlugin;
using static Launchly.Tests.TestHelpers;

namespace Launchly.Tests;

public class PluginManagerTests
{
    private static FakePlugin Global(string id, params Result[] results) =>
        new(id, ["*"], (_, _) => Results(results));

    private static FakePlugin Keyworded(string id, string keyword, params Result[] results) =>
        new(id, [keyword], (_, _) => Results(results));

    // --- Yönlendirme ---

    [Fact]
    public async Task NoKeyword_RunsOnlyGlobalPlugins()
    {
        var global = Global("g", R("global"));
        var yt = Keyworded("yt", "yt", R("youtube"));
        using var manager = CreateManager([global, yt]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        await manager.QueryAsync("beatles", log.Add);

        Assert.Equal(new[] { "global" }, log.Last);
        Assert.Equal(1, global.QueryCount);
        Assert.Equal(0, yt.QueryCount);
    }

    [Fact]
    public async Task Keyword_RunsOnlyThatPlugin_WithSearchStripped()
    {
        var global = Global("g", R("global"));
        var yt = Keyworded("yt", "yt", R("youtube"));
        using var manager = CreateManager([global, yt]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        await manager.QueryAsync("YT the beatles", log.Add);

        Assert.Equal(new[] { "youtube" }, log.Last);
        Assert.Equal(0, global.QueryCount);
        Assert.Equal("YT", yt.Queries.Single().ActionKeyword);
        Assert.Equal("the beatles", yt.Queries.Single().Search);
    }

    [Fact]
    public async Task UnknownKeyword_FallsBackToGlobalPlugins_WithWholeText()
    {
        var global = Global("g", R("global"));
        var yt = Keyworded("yt", "yt", R("youtube"));
        using var manager = CreateManager([global, yt]);
        await manager.InitializeAsync();

        await manager.QueryAsync("zz beatles", _ => { });

        Assert.Equal(0, yt.QueryCount);
        Assert.Equal(string.Empty, global.Queries.Single().ActionKeyword);
        Assert.Equal("zz beatles", global.Queries.Single().Search);
    }

    [Fact]
    public async Task PluginWithSeveralKeywords_IsReachableByEach()
    {
        var plugin = new FakePlugin("web", ["g", "yt"], (_, _) => Results(R("hit")));
        using var manager = CreateManager([plugin]);
        await manager.InitializeAsync();

        await manager.QueryAsync("g x", _ => { });
        await manager.QueryAsync("yt x", _ => { });

        Assert.Equal(2, plugin.QueryCount);
        Assert.Contains("g", manager.ActionKeywords);
        Assert.Contains("yt", manager.ActionKeywords);
        Assert.DoesNotContain("*", manager.ActionKeywords);
    }

    [Fact]
    public async Task DisabledPlugin_IsSkipped_AndItsKeywordIsNotActive()
    {
        var yt = Keyworded("yt", "yt", R("youtube"));
        var global = Global("g", R("global"));
        var settings = new Settings { DisabledPlugins = ["yt"] };
        using var manager = CreateManager([yt, global], settings);
        await manager.InitializeAsync();

        await manager.QueryAsync("yt beatles", _ => { });

        Assert.Equal(0, yt.QueryCount);
        Assert.DoesNotContain("yt", manager.ActionKeywords);
        Assert.Equal("yt beatles", global.Queries.Single().Search);
    }

    [Fact]
    public async Task Init_ReceivesDataDirectory_AndFailingPluginIsDisabled()
    {
        var ok = Global("ok", R("ok"));
        var bad = new FakePlugin("bad", ["bad"], (_, _) => Results(R("never")),
            init: () => throw new InvalidOperationException("boom"));
        using var manager = CreateManager([ok, bad]);

        await manager.InitializeAsync();
        await manager.QueryAsync("bad x", _ => { });

        Assert.NotNull(ok.Context);
        Assert.True(Directory.Exists(ok.Context!.DataDirectory));
        Assert.Equal(0, bad.QueryCount);
        Assert.DoesNotContain("bad", manager.ActionKeywords);
    }

    // --- Paralellik ---

    [Fact]
    public async Task Plugins_RunInParallel()
    {
        // İki eklenti de, öteki başlayana kadar bitmez. Sıralı çalışsalardı ilki asla bitmez (zaman aşımı).
        var aStarted = new TaskCompletionSource();
        var bStarted = new TaskCompletionSource();
        var a = new FakePlugin("a", ["*"], async (_, _) =>
        {
            aStarted.SetResult();
            await bStarted.Task;
            return [R("a", 1)];
        });
        var b = new FakePlugin("b", ["*"], async (_, _) =>
        {
            bStarted.SetResult();
            await aStarted.Task;
            return [R("b", 2)];
        });
        using var manager = CreateManager([a, b]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        await manager.QueryAsync("x", log.Add).WaitAsync(TestTimeout);

        Assert.Equal(new[] { "b", "a" }, log.Last);
    }

    [Fact]
    public async Task BlockingSynchronousPlugin_DoesNotStallOthers()
    {
        var release = new ManualResetEventSlim();
        var blocking = new FakePlugin("block", ["*"], (_, _) =>
        {
            release.Wait(TestTimeout);
            return Results(R("late", 1));
        });
        var fast = new FakePlugin("fast", ["*"], (_, _) => Results(R("fast", 2)));
        using var manager = CreateManager([blocking, fast]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        var query = manager.QueryAsync("x", log.Add);
        await WaitUntilAsync(() => log.Publishes.Count > 0, "hızlı eklentinin yayını");
        Assert.Equal(new[] { "fast" }, log.Last);

        release.Set();
        await query.WaitAsync(TestTimeout);
        Assert.Equal(new[] { "fast", "late" }, log.Last);
    }

    // --- İptal ---

    [Fact]
    public async Task NewQuery_CancelsPreviousQuery()
    {
        var started = new TaskCompletionSource();
        var observedCancel = new TaskCompletionSource();
        var plugin = new FakePlugin("p", ["*"], async (q, ct) =>
        {
            if (q.Search == "first")
            {
                started.SetResult();
                try
                {
                    await Task.Delay(System.Threading.Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    observedCancel.SetResult();
                    throw;
                }
            }

            return [R("second-result")];
        });
        using var manager = CreateManager([plugin]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        var first = manager.QueryAsync("first", log.Add);
        await started.Task.WaitAsync(TestTimeout);

        await manager.QueryAsync("second", log.Add).WaitAsync(TestTimeout);

        await observedCancel.Task.WaitAsync(TestTimeout);   // eklentinin token'ı iptal edildi
        await first.WaitAsync(TestTimeout);                  // iptal edilen sorgu hatasız döner
        Assert.Equal(new[] { "second-result" }, log.Last);
        Assert.Equal(new[] { "second-result" }, log.AllTitles.Distinct());
    }

    [Fact]
    public async Task CancelledQuery_NeverPublishes_EvenIfPluginIgnoresTheToken()
    {
        var releaseOld = new TaskCompletionSource();
        var oldStarted = new TaskCompletionSource();
        var plugin = new FakePlugin("p", ["*"], async (q, _) =>
        {
            if (q.Search == "a")
            {
                oldStarted.SetResult();
                await releaseOld.Task;      // token'ı yok sayar
                return [R("old", 100)];
            }

            return [R("new", 1)];
        });
        using var manager = CreateManager([plugin]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        var first = manager.QueryAsync("a", log.Add);
        await oldStarted.Task.WaitAsync(TestTimeout);
        await manager.QueryAsync("ab", log.Add).WaitAsync(TestTimeout);

        releaseOld.SetResult();                          // eski eklenti şimdi, geç, sonuç döner
        await first.WaitAsync(TestTimeout);
        await Task.Delay(100);                           // geç yayın olsaydı birikirdi

        Assert.DoesNotContain("old", log.AllTitles);
        Assert.Equal(new[] { "new" }, log.Last);
    }

    [Fact]
    public async Task RapidTyping_OnlyLastQueryResultsAreEverPublished()
    {
        // Her sorgu, uzunluğuyla ters orantılı gecikmeyle döner: eskiler daha geç biter.
        var plugin = new FakePlugin("p", ["*"], async (q, _) =>
        {
            await Task.Delay((10 - q.Search.Length) * 15);
            return [R(q.Search)];
        });
        using var manager = CreateManager([plugin]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        var tasks = new List<Task>();
        foreach (var text in new[] { "a", "ab", "abc", "abcd" })
        {
            tasks.Add(manager.QueryAsync(text, log.Add));
            await Task.Delay(5);
        }

        await Task.WhenAll(tasks).WaitAsync(TestTimeout);
        await Task.Delay(200);

        Assert.Equal(new[] { "abcd" }, log.AllTitles.Distinct());
    }

    [Fact]
    public async Task CancelCurrent_StopsPublishing()
    {
        var started = new TaskCompletionSource();
        var plugin = new FakePlugin("p", ["*"], async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(100);              // token'ı yok sayar
            return [R("late")];
        });
        using var manager = CreateManager([plugin]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        var query = manager.QueryAsync("x", log.Add);
        await started.Task.WaitAsync(TestTimeout);
        manager.CancelCurrent();

        await query.WaitAsync(TestTimeout);
        await Task.Delay(150);
        Assert.Empty(log.Publishes);
    }

    // --- Gecikme ---

    [Fact]
    public async Task SearchDelay_PostponesPluginStart()
    {
        var sw = Stopwatch.StartNew();
        long startedAt = -1;
        var plugin = new FakePlugin("slow", ["*"], (_, _) =>
        {
            startedAt = sw.ElapsedMilliseconds;
            return Results(R("r"));
        }, searchDelayMs: 200);
        using var manager = CreateManager([plugin]);
        await manager.InitializeAsync();

        await manager.QueryAsync("x", _ => { }).WaitAsync(TestTimeout);

        Assert.InRange(startedAt, 170, long.MaxValue);   // zamanlayıcı toleransı
    }

    [Fact]
    public async Task SearchDelay_IsPerPlugin_FastPluginIsNotHeldBack()
    {
        var slow = new FakePlugin("slow", ["*"], (_, _) => Results(R("slow", 1)), searchDelayMs: 300);
        var fast = new FakePlugin("fast", ["*"], (_, _) => Results(R("fast", 2)));
        using var manager = CreateManager([slow, fast]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        var query = manager.QueryAsync("x", log.Add);
        await WaitUntilAsync(() => log.Publishes.Count > 0, "ilk yayın");
        Assert.Equal(new[] { "fast" }, log.Last);
        Assert.Equal(0, slow.QueryCount);

        await query.WaitAsync(TestTimeout);
        Assert.Equal(new[] { "fast", "slow" }, log.Last);
    }

    [Fact]
    public async Task NewQueryDuringDelay_MeansPluginIsNeverCalledForTheOldQuery()
    {
        var plugin = new FakePlugin("slow", ["*"], (q, _) => Results(R(q.Search)), searchDelayMs: 150);
        using var manager = CreateManager([plugin]);
        await manager.InitializeAsync();

        var first = manager.QueryAsync("a", _ => { });
        await Task.Delay(30);
        await manager.QueryAsync("ab", _ => { }).WaitAsync(TestTimeout);
        await first.WaitAsync(TestTimeout);

        Assert.Equal(new[] { "ab" }, plugin.Queries.Select(q => q.Search));
    }

    // --- Birleştirme, sıralama, sınır ---

    [Fact]
    public async Task ResultsArrivingWithinMergeWindow_ArePublishedOnce_SortedByScore()
    {
        var a = new FakePlugin("a", ["*"], (_, _) => Results(R("low", 1), R("mid", 5)));
        var b = new FakePlugin("b", ["*"], async (_, _) =>
        {
            await Task.Delay(30);
            return [R("high", 9)];
        });
        using var manager = CreateManager([a, b], mergeWindow: TimeSpan.FromMilliseconds(500));
        await manager.InitializeAsync();
        var log = new PublishLog();

        await manager.QueryAsync("x", log.Add).WaitAsync(TestTimeout);

        Assert.Single(log.Publishes);
        Assert.Equal(new[] { "high", "mid", "low" }, log.Last);
    }

    [Fact]
    public async Task LateResults_AreMergedIntoASecondSortedPublish()
    {
        var a = new FakePlugin("a", ["*"], (_, _) => Results(R("first", 1)));
        var b = new FakePlugin("b", ["*"], async (_, _) =>
        {
            await Task.Delay(300);
            return [R("later", 9)];
        });
        using var manager = CreateManager([a, b], mergeWindow: TimeSpan.FromMilliseconds(20));
        await manager.InitializeAsync();
        var log = new PublishLog();

        await manager.QueryAsync("x", log.Add).WaitAsync(TestTimeout);

        Assert.Equal(2, log.Publishes.Count);
        Assert.Equal(new[] { "first" }, log.Publishes[0]);
        Assert.Equal(new[] { "later", "first" }, log.Publishes[1]);
    }

    [Fact]
    public async Task Results_AreLimitedToMaxResults()
    {
        var plugin = new FakePlugin("p", ["*"], (_, _) =>
            Results(Enumerable.Range(1, 20).Select(i => R($"r{i}", i)).ToArray()));
        using var manager = CreateManager([plugin], new Settings { MaxResults = 3 });
        await manager.InitializeAsync();
        var log = new PublishLog();

        await manager.QueryAsync("x", log.Add);

        Assert.Equal(new[] { "r20", "r19", "r18" }, log.Last);
    }

    [Fact]
    public async Task EmptyQuery_IsLimitedToSix_TypedQueryToMaxResults()
    {
        var plugin = new FakePlugin("p", ["*"], (_, _) =>
            Results(Enumerable.Range(1, 20).Select(i => R($"r{i}", i)).ToArray()), handlesEmptyQuery: true);
        using var manager = CreateManager([plugin], new Settings { MaxResults = 8 });
        await manager.InitializeAsync();
        var empty = new PublishLog();
        var typed = new PublishLog();

        await manager.QueryAsync(string.Empty, empty.Add);
        await manager.QueryAsync("x", typed.Add);

        Assert.Equal(Settings.EmptyQueryMaxResults, empty.Last.Length);
        Assert.All(empty.Publishes, p => Assert.True(p.Length <= 6));
        Assert.Equal(new[] { "r20", "r19", "r18", "r17", "r16", "r15" }, empty.Last);   // en yüksek puanlılar
        Assert.Equal(8, typed.Last.Length);
        Assert.All(typed.Publishes, p => Assert.True(p.Length <= 8));
    }

    [Fact]
    public async Task EmptyQuery_MaxResultsBelowSix_UsesMaxResults()
    {
        var plugin = new FakePlugin("p", ["*"], (_, _) =>
            Results(Enumerable.Range(1, 20).Select(i => R($"r{i}", i)).ToArray()), handlesEmptyQuery: true);
        using var manager = CreateManager([plugin], new Settings { MaxResults = 4 });
        await manager.InitializeAsync();
        var log = new PublishLog();

        await manager.QueryAsync("   ", log.Add);

        Assert.Equal(4, log.Last.Length);
    }

    [Fact]
    public async Task EqualScores_KeepArrivalOrder()
    {
        var plugin = new FakePlugin("p", ["*"], (_, _) => Results(R("one"), R("two"), R("three")));
        using var manager = CreateManager([plugin]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        await manager.QueryAsync("x", log.Add);

        Assert.Equal(new[] { "one", "two", "three" }, log.Last);
    }

    [Fact]
    public async Task Results_AreStampedWithPluginId()
    {
        IReadOnlyList<Result>? got = null;
        using var manager = CreateManager([Global("my.plugin", R("a"))]);
        await manager.InitializeAsync();

        await manager.QueryAsync("x", r => got = r);

        Assert.Equal("my.plugin", got!.Single().PluginId);
    }

    // --- Dayanıklılık ---

    [Fact]
    public async Task ThrowingPlugin_DoesNotAffectOthers()
    {
        var bad = new FakePlugin("bad", ["*"], (_, _) => throw new InvalidOperationException("boom"));
        var good = Global("good", R("ok"));
        using var manager = CreateManager([bad, good]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        await manager.QueryAsync("x", log.Add);

        Assert.Equal(new[] { "ok" }, log.Last);
    }

    [Fact]
    public async Task NoResults_PublishesEmptyListOnce_SoOldResultsAreCleared()
    {
        using var manager = CreateManager([new FakePlugin("p", ["*"], (_, _) => Results())]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        await manager.QueryAsync("x", log.Add);

        Assert.Single(log.Publishes);
        Assert.Empty(log.Last);
    }

    [Fact]
    public async Task BlankQuery_PublishesEmpty_WithoutCallingPlugins()
    {
        var plugin = Global("g", R("a"));
        using var manager = CreateManager([plugin]);
        await manager.InitializeAsync();
        var log = new PublishLog();

        await manager.QueryAsync("   ", log.Add);

        Assert.Single(log.Publishes);
        Assert.Empty(log.Last);
        Assert.Equal(0, plugin.QueryCount);
    }

    // --- Son yayın (isFinal) ---

    [Fact]
    public async Task IsFinal_TrueExactlyOnce_OnLastPublish_AfterSlowPlugin()
    {
        var releaseSlow = new TaskCompletionSource();
        var fast = Global("fast", R("fast", 1));
        var slow = new FakePlugin("slow", ["*"], async (_, _) =>
        {
            await releaseSlow.Task;
            return [R("slow", 9)];
        });
        using var manager = CreateManager([fast, slow]);
        await manager.InitializeAsync();
        var log = new List<(string[] Titles, bool IsFinal)>();

        var query = manager.QueryAsync("x", (results, isFinal) =>
        {
            lock (log)
            {
                log.Add((results.Select(r => r.Title).ToArray(), isFinal));
            }
        });
        await WaitUntilAsync(() => { lock (log) { return log.Count > 0; } }, "hızlı eklentinin kısmi yayını");
        lock (log)
        {
            Assert.False(log[0].IsFinal);   // yavaş eklenti sürerken son yayın değil
        }

        releaseSlow.SetResult();
        await query.WaitAsync(TestTimeout);

        lock (log)
        {
            Assert.Single(log, e => e.IsFinal);
            Assert.True(log[^1].IsFinal);
            Assert.Equal(new[] { "slow", "fast" }, log[^1].Titles);
        }
    }

    [Theory]
    [InlineData("x")]       // eklenti sonuç vermiyor
    [InlineData("   ")]     // boş kutu, eklenti yok
    public async Task IsFinal_PublishedOnce_WhenNoResults(string text)
    {
        using var manager = CreateManager([new FakePlugin("p", ["*"], (_, _) => Results())]);
        await manager.InitializeAsync();
        var log = new List<bool>();

        await manager.QueryAsync(text, (results, isFinal) =>
        {
            Assert.Empty(results);
            log.Add(isFinal);
        });

        Assert.Equal(new[] { true }, log);
    }

    [Fact]
    public async Task IsFinal_NeverPublished_ForCancelledQuery()
    {
        var started = new TaskCompletionSource();
        var plugin = new FakePlugin("p", ["*"], async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return [R("never")];
        });
        using var manager = CreateManager([plugin]);
        await manager.InitializeAsync();
        var finals = 0;

        var query = manager.QueryAsync("x", (_, isFinal) => { if (isFinal) { Interlocked.Increment(ref finals); } });
        await started.Task.WaitAsync(TestTimeout);
        manager.CancelCurrent();
        await query.WaitAsync(TestTimeout);

        Assert.Equal(0, finals);
    }
}
