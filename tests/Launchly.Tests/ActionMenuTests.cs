using Launchly.App.ViewModels;
using Launchly.Core.Plugins;
using Launchly.Plugin;
using Microsoft.Extensions.Logging.Abstractions;
using static Launchly.Tests.FakePlugin;
using static Launchly.Tests.TestHelpers;

namespace Launchly.Tests;

/// <summary>Eylem menüsü (Ctrl+K / Actions) ve Ctrl+Shift+Enter (menü açmadan yönetici olarak çalıştır).</summary>
public class ActionMenuTests
{
    /// <summary>İş parçacığı güvenli çalıştırma kaydı.</summary>
    private sealed class RunLog
    {
        private readonly List<string> _items = [];

        public void Add(string item)
        {
            lock (_items)
            {
                _items.Add(item);
            }
        }

        public string[] Items
        {
            get
            {
                lock (_items)
                {
                    return [.. _items];
                }
            }
        }
    }

    private static async Task<(MainViewModel Vm, PluginManager Manager)> CreateAsync(params FakePlugin[] plugins)
    {
        var manager = CreateManager(plugins);
        await manager.InitializeAsync();
        return (new MainViewModel(manager, NullLogger<MainViewModel>.Instance), manager);
    }

    private static ResultAction Act(
        string title, RunLog log, bool returns = false, string? id = null, bool requery = false, Func<Task>? wait = null) => new()
        {
            Id = id,
            Title = title,
            RequeryAfter = requery,
            Hint = id == ResultAction.RunAsAdminId ? "Ctrl+Shift+Enter" : null,
            Execute = async () =>
            {
                if (wait is not null)
                {
                    await wait();
                }

                log.Add(title);
                return returns;
            },
        };

    private static Func<Task<bool>> Open(string title, RunLog log, bool returns = true) => () =>
    {
        log.Add(title);
        return Task.FromResult(returns);
    };

    private static Result Item(string title, RunLog log, int score = 1, bool withOpen = true, params ResultAction[] actions) => new()
    {
        Title = title,
        Score = score,
        Action = withOpen ? Open("open:" + title, log) : null,
        ContextActions = actions,
    };

    private static FakePlugin Plugin(Func<Query, Result[]> results) =>
        new("p", ["*"], (q, _) => Task.FromResult<IReadOnlyList<Result>>(results(q)));

    private static async Task<(MainViewModel Vm, PluginManager Manager)> WithResultsAsync(Func<Query, Result[]> results, int expected)
    {
        var (vm, manager) = await CreateAsync(Plugin(results));
        vm.QueryText = "q";
        await WaitUntilAsync(() => vm.Results.Count == expected, "sonuçlar");
        return (vm, manager);
    }

    private static string[] Titles(MainViewModel vm) => vm.Results.Select(r => r.Title).ToArray();

    private static string[] MenuTitles(MainViewModel vm) => vm.ActionItems.Select(a => a.Title).ToArray();

    // ---- SDK ----

    [Fact]
    public void Sdk_ContextActions_DefaultsToEmpty()
    {
        Assert.Empty(new Result { Title = "x" }.ContextActions);
    }

    [Fact]
    public void BuildMenu_OpenFirst_ThenContextActions_SkipsNullEntries()
    {
        var log = new RunLog();
        var result = new Result
        {
            Title = "x",
            Action = Open("open", log),
            ContextActions = [Act("a", log), null!, Act("b", log)],
        };

        Assert.Equal(["Aç", "a", "b"], MainViewModel.BuildMenu(result).Select(a => a.Title));
        Assert.Empty(MainViewModel.BuildMenu(new Result { Title = "y" }));
        Assert.Empty(MainViewModel.BuildMenu(new Result { Title = "z", ContextActions = null! }));   // eski/bozuk eklenti
    }

    // ---- Açma / kapama ----

    [Fact]
    public async Task Toggle_WithoutSelection_DoesNotOpen()
    {
        var (vm, manager) = await CreateAsync(Plugin(_ => []));
        using var m = manager;

        Assert.False(vm.ToggleActionMenu());
        Assert.False(vm.IsActionMenuOpen);
        Assert.Empty(vm.ActionItems);
    }

    [Fact]
    public async Task Toggle_ResultWithoutAnyAction_DoesNotOpen()
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(_ => [Item("a", log, withOpen: false)], 1);
        using var m = manager;

        Assert.False(vm.ToggleActionMenu());
        Assert.False(vm.IsActionMenuOpen);
    }

    [Fact]
    public async Task Menu_OpenIsFirst_AndCallsResultAction()
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(_ => [Item("a", log, actions: Act("ctx", log))], 1);
        using var m = manager;

        Assert.True(vm.ToggleActionMenu());

        Assert.True(vm.IsActionMenuOpen);
        Assert.Equal(["Aç", "ctx"], MenuTitles(vm));
        Assert.Equal("a", vm.ActionMenuTitle);
        Assert.Equal(0, vm.SelectedActionIndex);

        Assert.True(await vm.ExecuteSelectedAsync());   // "Aç" -> Result.Action (true: pencere gizlenir)
        Assert.Equal(["open:a"], log.Items);
        Assert.False(vm.IsActionMenuOpen);              // eylem başlarken menü kapandı
    }

    [Fact]
    public async Task Menu_WithoutResultAction_HasNoOpenItem()
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(_ => [Item("a", log, withOpen: false, actions: Act("ctx", log))], 1);
        using var m = manager;

        Assert.True(vm.ToggleActionMenu());
        Assert.Equal(["ctx"], MenuTitles(vm));
    }

    [Fact]
    public async Task Menu_Enter_RunsSelectedAction_NotResultAction()
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(_ => [Item("a", log, actions: [Act("x", log), Act("y", log)])], 1);
        using var m = manager;
        vm.ToggleActionMenu();

        vm.MoveSelection(1);
        vm.MoveSelection(1);
        Assert.Equal(2, vm.SelectedActionIndex);
        vm.MoveSelection(1);                             // sarar
        Assert.Equal(0, vm.SelectedActionIndex);
        vm.MoveSelection(-1);
        Assert.Equal(2, vm.SelectedActionIndex);
        Assert.Equal(0, vm.SelectedIndex);               // sonuç seçimi değişmedi

        Assert.False(await vm.ExecuteSelectedAsync());   // "y" false döner: pencere açık kalır
        Assert.Equal(["y"], log.Items);
    }

    [Fact]
    public async Task Escape_ClosesOnlyTheMenu_ThenHidesWindow()
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(_ => [Item("a", log)], 1);
        using var m = manager;
        vm.ToggleActionMenu();

        Assert.False(vm.HandleEscape());                 // menü kapandı, pencere açık kalır
        Assert.False(vm.IsActionMenuOpen);
        Assert.Empty(vm.ActionItems);
        Assert.Equal(-1, vm.SelectedActionIndex);

        Assert.True(vm.HandleEscape());                  // menü yokken: gizle
        Assert.Empty(log.Items);
    }

    [Fact]
    public async Task CtrlK_Twice_ClosesTheMenu()
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(_ => [Item("a", log)], 1);
        using var m = manager;

        Assert.True(vm.ToggleActionMenu());
        Assert.False(vm.ToggleActionMenu());
        Assert.False(vm.IsActionMenuOpen);
    }

    [Fact]
    public async Task Menu_AltDigitAndTab_AreIgnored()
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(
            _ => [new Result { Title = "a", Score = 2, Action = Open("a", log), AutoComplete = "auto" }, Item("b", log)], 2);
        using var m = manager;
        vm.ToggleActionMenu();

        Assert.False(vm.SelectByNumber(2));
        Assert.False(vm.AutoCompleteSelected());
        Assert.Equal(0, vm.SelectedIndex);
        Assert.Equal("q", vm.QueryText);
        Assert.True(vm.IsActionMenuOpen);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("reset")]
    [InlineData("close")]
    public async Task NewGeneration_OrHide_ClosesTheMenu(string how)
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(_ => [Item("a", log)], 1);
        using var m = manager;
        vm.ToggleActionMenu();

        switch (how)
        {
            case "query":
                vm.QueryText = "qq";                     // kutuya yazmak normal aramaya döner
                break;
            case "reset":
                vm.Reset();
                break;
            case "close":
                vm.CloseActionMenu();                    // HideLauncher
                break;
        }

        Assert.False(vm.IsActionMenuOpen);
        Assert.Empty(vm.ActionItems);
        Assert.Equal(string.Empty, vm.ActionMenuTitle);
        Assert.False(await vm.ExecuteActionAsync());     // kapalı menüde eylem çalışmaz
        Assert.Empty(log.Items);
    }

    [Fact]
    public async Task LatePublish_SameGeneration_KeepsMenuOpen_AndTargetUnchanged()
    {
        var log = new RunLog();
        var releaseSlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fast = new FakePlugin("fast", ["*"], (_, _) => Results(Item("fast-low", log, 1)));
        var slow = new FakePlugin("slow", ["*"], async (_, _) =>
        {
            await releaseSlow.Task;
            return [Item("slow-high", log, 9)];
        });
        var (vm, manager) = await CreateAsync(fast, slow);
        using var m = manager;
        vm.QueryText = "q";
        await WaitUntilAsync(() => Titles(vm).SequenceEqual(["fast-low"]), "kısmi yayın");

        Assert.True(vm.ToggleActionMenu());
        releaseSlow.SetResult();
        await WaitUntilAsync(() => Titles(vm).SequenceEqual(["slow-high", "fast-low"]), "geç yayın");

        Assert.True(vm.IsActionMenuOpen);
        Assert.Equal("fast-low", vm.ActionMenuTitle);
        Assert.True(await vm.ExecuteSelectedAsync());    // "Aç" hedefi: menünün açıldığı sonuç, şimdiki seçili değil
        Assert.Equal(["open:fast-low"], log.Items);
    }

    [Fact]
    public async Task PendingEnter_ThenMenuOpens_EnterRunsNothing()
    {
        var log = new RunLog();
        var releaseSlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fast = new FakePlugin("fast", ["*"], (_, _) => Results(Item("fast", log, 1)));
        var slow = new FakePlugin("slow", ["*"], async (_, _) =>
        {
            await releaseSlow.Task;
            return [Item("slow", log, 9)];
        });
        var (vm, manager) = await CreateAsync(fast, slow);
        using var m = manager;
        vm.QueryText = "q";
        await WaitUntilAsync(() => vm.Results.Count == 1, "kısmi yayın");

        var enter = vm.ExecuteSelectedAsync();           // son yayını bekliyor
        Assert.False(enter.IsCompleted);
        Assert.True(vm.ToggleActionMenu());              // bekleyen Enter iptal

        Assert.False(await enter.WaitAsync(TestTimeout));
        vm.CloseActionMenu();                            // kapanınca yeni bekleyen Enter oluşmaz
        releaseSlow.SetResult();
        await WaitUntilAsync(() => vm.Results.Count == 2, "son yayın");
        await Task.Delay(50);
        Assert.Empty(log.Items);
    }

    [Fact]
    public async Task ActionRunning_SecondActionEnterOrAdmin_AreIgnored()
    {
        var log = new RunLog();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (vm, manager) = await WithResultsAsync(
            _ => [Item("a", log, actions: [Act("slow", log, returns: true, wait: () => gate.Task), Act("admin", log, id: ResultAction.RunAsAdminId)])], 1);
        using var m = manager;
        vm.ToggleActionMenu();
        vm.MoveSelection(1);

        var first = vm.ExecuteSelectedAsync();           // menüde "slow" çalışıyor
        Assert.False(first.IsCompleted);
        Assert.False(vm.IsActionMenuOpen);

        Assert.False(await vm.ExecuteSelectedAsync());   // Enter yok sayıldı
        Assert.False(await vm.ExecuteSelectedAsAdminAsync());
        Assert.True(vm.ToggleActionMenu());              // menü açılabilir ama eylemi çalışmaz
        Assert.False(await vm.ExecuteActionAsync());
        vm.CloseActionMenu();                            // menüyü kapatmak çalışan eylemi durdurmaz

        gate.SetResult();
        Assert.True(await first.WaitAsync(TestTimeout));
        Assert.Equal(["slow"], log.Items);
    }

    [Fact]
    public async Task DoubleTrigger_EnterAndClick_RunsActionOnce()
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(_ => [Item("a", log, actions: Act("x", log))], 1);
        using var m = manager;
        vm.ToggleActionMenu();
        vm.SelectedActionIndex = 1;

        var enter = vm.ExecuteSelectedAsync();
        var click = vm.ExecuteActionAsync();

        Assert.False(await enter);
        Assert.False(await click);
        Assert.Equal(["x"], log.Items);
    }

    [Fact]
    public async Task RequeryAfter_FalseResult_StartsNewGeneration()
    {
        var log = new RunLog();
        var hidden = false;
        var plugin = Plugin(_ => Volatile.Read(ref hidden)
            ? [Item("b", log)]
            : [Item("a", log, actions: new ResultAction
            {
                Title = "Gizle",
                RequeryAfter = true,
                Execute = () => { Volatile.Write(ref hidden, true); return Task.FromResult(false); },
            }), Item("b", log, score: 0)]);
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;
        vm.QueryText = "q";
        await WaitUntilAsync(() => vm.Results.Count == 2, "sonuçlar");
        var queries = plugin.QueryCount;
        vm.ToggleActionMenu();
        vm.SelectedActionIndex = 1;

        Assert.False(await vm.ExecuteSelectedAsync());   // false: pencere açık kalır

        await WaitUntilAsync(() => Titles(vm).SequenceEqual(["b"]), "yenilenen liste");
        Assert.True(plugin.QueryCount > queries);
        Assert.Equal("q", plugin.Queries[^1].Search);    // güncel sorgu yeniden çalıştı
        Assert.False(vm.IsActionMenuOpen);
    }

    [Fact]
    public async Task RequeryAfter_TrueResult_DoesNotRequery()
    {
        var log = new RunLog();
        var plugin = Plugin(_ => [Item("a", log, actions: Act("x", log, returns: true, requery: true))]);
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;
        vm.QueryText = "q";
        await WaitUntilAsync(() => vm.Results.Count == 1, "sonuç");
        await Task.Delay(50);
        var queries = plugin.QueryCount;
        vm.ToggleActionMenu();
        vm.SelectedActionIndex = 1;

        Assert.True(await vm.ExecuteSelectedAsync());
        await Task.Delay(100);
        Assert.Equal(queries, plugin.QueryCount);
    }

    [Fact]
    public async Task RequeryAfter_OldGenerationPublish_NeverOverwritesNewList()
    {
        // Yeniden sorgu yeni nesildir: önceki neslin geç gelen yayını listeye yazılmaz.
        var log = new RunLog();
        var releaseOld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var fast = new FakePlugin("fast", ["*"], (_, _) => Results(Item("a", log, 1, actions: Act("Gizle", log, requery: true))));
        var slow = new FakePlugin("slow", ["*"], async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await releaseOld.Task;                    // ilk neslin yavaş sonucu (token'ı yok sayar)
                return [Item("stale", log, 9)];
            }

            return [Item("fresh", log, 9)];
        });
        var (vm, manager) = await CreateAsync(fast, slow);
        using var m = manager;
        vm.QueryText = "q";
        await WaitUntilAsync(() => Titles(vm).SequenceEqual(["a"]), "kısmi yayın");
        vm.ToggleActionMenu();
        vm.SelectedActionIndex = 1;

        Assert.False(await vm.ExecuteSelectedAsync());
        await WaitUntilAsync(() => Titles(vm).SequenceEqual(["fresh", "a"]), "yeni nesil");
        releaseOld.SetResult();
        await Task.Delay(150);

        Assert.Equal(["fresh", "a"], Titles(vm));
    }

    // ---- Ctrl+Shift+Enter ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Admin_RunsOnlyRunAsAdminAction_AndReturnsItsFlag(bool returns)
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(
            _ => [Item("a", log, actions: [Act("other", log), Act("admin", log, returns, ResultAction.RunAsAdminId)])], 1);
        using var m = manager;

        Assert.Equal(returns, await vm.ExecuteSelectedAsAdminAsync());
        Assert.Equal(["admin"], log.Items);              // Result.Action çağrılmadı
    }

    [Fact]
    public async Task Admin_ResultWithoutRunAsAdmin_RunsNothing()
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(_ => [Item("web", log, actions: Act("copy", log, returns: true))], 1);
        using var m = manager;

        Assert.False(await vm.ExecuteSelectedAsAdminAsync());
        Assert.Empty(log.Items);                         // normal Enter'a düşülmez
        Assert.False(vm.CanRunSelectedAsAdmin);
    }

    private static FakePlugin SlowAdminPlugin(RunLog log, int delayMs = 200) => new("p", ["*"], async (q, _) =>
    {
        await Task.Delay(q.Search.StartsWith("slow") ? delayMs : 0);
        var title = "res-" + q.Search;
        return [Item(title, log, actions: Act("admin:" + title, log, returns: true, id: ResultAction.RunAsAdminId))];
    });

    [Fact]
    public async Task Admin_WhileQueryPending_WaitsAndRunsOnNewSelection()
    {
        var log = new RunLog();
        var (vm, manager) = await CreateAsync(SlowAdminPlugin(log));
        using var m = manager;
        vm.QueryText = "slow";

        var admin = vm.ExecuteSelectedAsAdminAsync();
        Assert.False(admin.IsCompleted);

        Assert.True(await admin.WaitAsync(TestTimeout));
        Assert.Equal(["admin:res-slow"], log.Items);
    }

    [Theory]
    [InlineData("esc")]
    [InlineData("query")]
    [InlineData("reset")]
    public async Task Admin_Pending_CancelledByEscOrNewQuery_RunsNothing(string cancel)
    {
        var log = new RunLog();
        var (vm, manager) = await CreateAsync(SlowAdminPlugin(log));
        using var m = manager;
        vm.QueryText = "slow1";

        var admin = vm.ExecuteSelectedAsAdminAsync();
        switch (cancel)
        {
            case "esc":
                Assert.True(vm.HandleEscape());
                break;
            case "query":
                vm.QueryText = "slow2";
                break;
            case "reset":
                vm.Reset();
                break;
        }

        Assert.False(await admin.WaitAsync(TestTimeout));
        await Task.Delay(300);
        Assert.Empty(log.Items);
    }

    [Theory]
    [InlineData("enter-then-admin")]
    [InlineData("admin-then-enter")]
    [InlineData("admin-twice")]
    public async Task EnterAndAdmin_WhilePending_OnlyFirstRunsOnce(string order)
    {
        var log = new RunLog();
        var (vm, manager) = await CreateAsync(SlowAdminPlugin(log));
        using var m = manager;
        vm.QueryText = "slow";

        Task<bool> first, second;
        switch (order)
        {
            case "enter-then-admin":
                first = vm.ExecuteSelectedAsync();
                second = vm.ExecuteSelectedAsAdminAsync();
                break;
            case "admin-then-enter":
                first = vm.ExecuteSelectedAsAdminAsync();
                second = vm.ExecuteSelectedAsync();
                break;
            default:
                first = vm.ExecuteSelectedAsAdminAsync();
                second = vm.ExecuteSelectedAsAdminAsync();
                break;
        }

        Assert.False(await second);
        Assert.True(await first.WaitAsync(TestTimeout));
        Assert.Equal([order == "enter-then-admin" ? "open:res-slow" : "admin:res-slow"], log.Items);
    }

    [Fact]
    public async Task Admin_WhileEnterRunning_IsIgnored_AndViceVersa()
    {
        var log = new RunLog();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (vm, manager) = await WithResultsAsync(_ =>
        [
            new Result
            {
                Title = "a",
                Action = async () => { await gate.Task; log.Add("open"); return true; },
                ContextActions = [Act("admin", log, returns: true, id: ResultAction.RunAsAdminId, wait: () => gate.Task)],
            },
        ], 1);
        using var m = manager;

        var enter = vm.ExecuteSelectedAsync();
        Assert.False(await vm.ExecuteSelectedAsAdminAsync());
        gate.SetResult();
        Assert.True(await enter.WaitAsync(TestTimeout));
        Assert.Equal(["open"], log.Items);
    }

    [Fact]
    public async Task Admin_WhileMenuOpen_IsIgnored()
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(_ => [Item("a", log, actions: Act("admin", log, true, ResultAction.RunAsAdminId))], 1);
        using var m = manager;
        vm.ToggleActionMenu();

        Assert.False(await vm.ExecuteSelectedAsAdminAsync());
        Assert.True(vm.IsActionMenuOpen);
        Assert.Empty(log.Items);
        Assert.Equal("Ctrl+Shift+Enter", vm.ActionItems[1].Hint);   // menüde ipucu görünür
        Assert.True(vm.ActionItems[1].HasHint);
        Assert.False(vm.ActionItems[0].HasHint);
    }

    [Fact]
    public async Task CanRunSelectedAsAdmin_FollowsSelectionAndMenu()
    {
        var log = new RunLog();
        var (vm, manager) = await WithResultsAsync(_ =>
        [
            Item("lnk", log, 2, actions: Act("admin", log, id: ResultAction.RunAsAdminId)),
            Item("packaged", log, 1, actions: Act("copy", log)),
        ], 2);
        using var m = manager;
        var changes = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CanRunSelectedAsAdmin))
            {
                Interlocked.Increment(ref changes);
            }
        };

        Assert.True(vm.CanRunSelectedAsAdmin);
        vm.MoveSelection(1);
        Assert.False(vm.CanRunSelectedAsAdmin);
        vm.MoveSelection(1);
        Assert.True(vm.CanRunSelectedAsAdmin);
        vm.ToggleActionMenu();
        Assert.False(vm.CanRunSelectedAsAdmin);          // menü açıkken kısayol etkisiz
        vm.CloseActionMenu();
        Assert.True(vm.CanRunSelectedAsAdmin);
        Assert.True(changes >= 4);
    }
}
