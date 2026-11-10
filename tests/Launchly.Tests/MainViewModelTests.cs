using Launchly.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using static Launchly.Tests.FakePlugin;
using static Launchly.Tests.TestHelpers;

namespace Launchly.Tests;

public class MainViewModelTests
{
    private static async Task<(MainViewModel Vm, Launchly.Core.Plugins.PluginManager Manager)> CreateAsync(params FakePlugin[] plugins)
    {
        var manager = CreateManager(plugins);
        await manager.InitializeAsync();
        return (new MainViewModel(manager, NullLogger<MainViewModel>.Instance), manager);
    }

    private static string[] Titles(MainViewModel vm) => vm.Results.Select(r => r.Title).ToArray();

    [Fact]
    public async Task Typing_ListsPluginResults_SortedByScore_AndSelectsFirst()
    {
        var plugin = new FakePlugin("p", ["*"], (q, _) =>
            Results(R(q.Search + "-low", 1), R(q.Search + "-high", 9)));
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;

        vm.QueryText = "ab";

        await WaitUntilAsync(() => vm.Results.Count == 2, "sonuçların listelenmesi");
        Assert.Equal(new[] { "ab-high", "ab-low" }, Titles(vm));
        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public async Task RapidTyping_StaleResultsNeverReachTheList()
    {
        var plugin = new FakePlugin("p", ["*"], async (q, _) =>
        {
            await Task.Delay(q.Search == "a" ? 200 : 10);   // eski sorgu daha geç döner, token'ı yok sayar
            return [R("res-" + q.Search)];
        });
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;
        var seen = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Results))
            {
                lock (seen)
                {
                    seen.AddRange(Titles(vm));
                }
            }
        };

        vm.QueryText = "a";
        await Task.Delay(20);
        vm.QueryText = "ab";

        await WaitUntilAsync(() => Titles(vm).SequenceEqual(["res-ab"]), "son sorgunun sonucu");
        await Task.Delay(350);   // eski sorgunun geç sonucu gelseydi şimdiye düşerdi

        Assert.Equal(new[] { "res-ab" }, Titles(vm));
        lock (seen)
        {
            Assert.DoesNotContain("res-a", seen);
        }
    }

    [Fact]
    public async Task ClearingTheBox_ClearsResults_AndDropsPendingQuery()
    {
        var plugin = new FakePlugin("p", ["*"], async (q, _) =>
        {
            await Task.Delay(q.Search == "slow" ? 150 : 0);
            return [R("res-" + q.Search)];
        });
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;

        vm.QueryText = "fast";
        await WaitUntilAsync(() => vm.Results.Count == 1, "ilk sonuç");

        vm.QueryText = "slow";
        vm.QueryText = string.Empty;

        Assert.Empty(vm.Results);
        Assert.Equal(-1, vm.SelectedIndex);
        await Task.Delay(300);
        Assert.Empty(vm.Results);   // beklemedeki "slow" sonucu düşmedi
    }

    [Fact]
    public async Task Reset_ClearsBoxAndResults()
    {
        var plugin = new FakePlugin("p", ["*"], (_, _) => Results(R("x")));
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;
        vm.QueryText = "q";
        await WaitUntilAsync(() => vm.Results.Count == 1, "sonuç");

        vm.Reset();

        Assert.Equal(string.Empty, vm.QueryText);
        Assert.Empty(vm.Results);
        Assert.Equal(-1, vm.SelectedIndex);
    }

    [Fact]
    public async Task ExecuteSelected_RunsAction_AndReturnsItsHideFlag()
    {
        var ran = 0;
        var plugin = new FakePlugin("p", ["*"], (_, _) => Results(
            R("first", 9, () => { ran++; return Task.FromResult(true); }),
            R("second", 1, () => { ran += 100; return Task.FromResult(false); })));
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;
        vm.QueryText = "q";
        await WaitUntilAsync(() => vm.Results.Count == 2, "sonuçlar");

        Assert.True(await vm.ExecuteSelectedAsync());      // seçili: first -> true (gizle)
        Assert.Equal(1, ran);

        vm.MoveSelection(1);
        Assert.False(await vm.ExecuteSelectedAsync());     // second -> false (açık kal)
        Assert.Equal(101, ran);
    }

    [Fact]
    public async Task ExecuteSelected_WithoutSelectionOrAction_DoesNothing()
    {
        var plugin = new FakePlugin("p", ["*"], (_, _) => Results(R("no-action")));
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;

        Assert.False(await vm.ExecuteSelectedAsync());     // sonuç yok

        vm.QueryText = "q";
        await WaitUntilAsync(() => vm.Results.Count == 1, "sonuç");
        Assert.False(await vm.ExecuteSelectedAsync());     // eylem yok
    }

    [Fact]
    public async Task ExecuteSelected_ActionThrows_ReturnsFalse()
    {
        var plugin = new FakePlugin("p", ["*"], (_, _) => Results(
            R("bad", 1, () => throw new InvalidOperationException("boom"))));
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;
        vm.QueryText = "q";
        await WaitUntilAsync(() => vm.Results.Count == 1, "sonuç");

        Assert.False(await vm.ExecuteSelectedAsync());
    }

    [Fact]
    public async Task Tab_WritesAutoCompleteOfSelectedResult_AndRequeries()
    {
        var plugin = new FakePlugin("p", ["*"], (q, _) => Results(
            R("Visual Studio Code", 9, autoComplete: "vscode"),
            R("Other", 1)));
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;
        vm.QueryText = "vsc";
        await WaitUntilAsync(() => vm.Results.Count == 2, "sonuçlar");

        Assert.True(vm.AutoCompleteSelected());
        Assert.Equal("vscode", vm.QueryText);
        await WaitUntilAsync(() => plugin.Queries.Count(q => q.Search == "vscode") == 1, "yeni sorgu");

        vm.MoveSelection(1);                               // AutoComplete'i olmayan sonuç
        Assert.False(vm.AutoCompleteSelected());
        Assert.Equal("vscode", vm.QueryText);
    }

    [Fact]
    public async Task MoveSelection_WrapsAround_AndIgnoresEmptyList()
    {
        var plugin = new FakePlugin("p", ["*"], (_, _) => Results(R("a", 3), R("b", 2), R("c", 1)));
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;

        vm.MoveSelection(1);                               // boş liste: bir şey olmaz
        Assert.Equal(-1, vm.SelectedIndex);

        vm.QueryText = "q";
        await WaitUntilAsync(() => vm.Results.Count == 3, "sonuçlar");

        vm.MoveSelection(-1);
        Assert.Equal(2, vm.SelectedIndex);
        vm.MoveSelection(1);
        Assert.Equal(0, vm.SelectedIndex);
        Assert.True(vm.SelectByNumber(2));
        Assert.Equal(1, vm.SelectedIndex);
        Assert.False(vm.SelectByNumber(9));
    }

    [Fact]
    public async Task ExecuteSelected_WhileQueryPending_WaitsForNewResults_NotStaleList()
    {
        var ran = new List<string>();
        var plugin = new FakePlugin("p", ["*"], async (q, _) =>
        {
            await Task.Delay(q.Search == "slow" ? 150 : 0);
            var title = "res-" + q.Search;
            return [R(title, 1, () => { ran.Add(title); return Task.FromResult(true); })];
        });
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;
        vm.QueryText = "fast";
        await WaitUntilAsync(() => Titles(vm).SequenceEqual(["res-fast"]), "ilk sonuç");

        vm.QueryText = "slow";              // sonuçlar henüz gelmedi; eski liste hâlâ "res-fast"
        Assert.Equal(["res-fast"], Titles(vm));
        var hide = await vm.ExecuteSelectedAsync();

        Assert.True(hide);
        Assert.Equal(["res-slow"], ran);
    }

    [Fact]
    public async Task ExecuteSelected_WhilePendingQueryYieldsNothing_DoesNotRunStaleItem()
    {
        var ran = 0;
        var plugin = new FakePlugin("p", ["*"], async (q, _) =>
        {
            await Task.Delay(q.Search == "none" ? 100 : 0);
            return q.Search == "none" ? Array.Empty<Launchly.Plugin.Result>() : new[] { R("old", 1, () => { ran++; return Task.FromResult(true); }) };
        });
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;
        vm.QueryText = "x";
        await WaitUntilAsync(() => vm.Results.Count == 1, "ilk sonuç");

        vm.QueryText = "none";
        Assert.False(await vm.ExecuteSelectedAsync());

        Assert.Equal(0, ran);
    }

    private static FakePlugin SlowPlugin(List<string> ran, int delayMs = 200) => new("p", ["*"], async (q, _) =>
    {
        await Task.Delay(q.Search.StartsWith("slow") ? delayMs : 0);
        var title = "res-" + q.Search;
        return [R(title, 1, () => { ran.Add(title); return Task.FromResult(true); })];
    });

    [Fact]
    public async Task PendingEnter_CancelledByCancelPendingExecute_RunsNothing()
    {
        var ran = new List<string>();
        var (vm, manager) = await CreateAsync(SlowPlugin(ran));
        using var m = manager;
        vm.QueryText = "slow";

        var enter = vm.ExecuteSelectedAsync();
        vm.CancelPendingExecute();                      // Esc / pencere gizleme

        Assert.False(await enter);
        await WaitUntilAsync(() => vm.Results.Count == 1, "sonuç");
        await Task.Delay(50);
        Assert.Empty(ran);
    }

    [Fact]
    public async Task PendingEnter_NewQuery_CancelsIt_AndDoesNotRunLaterGeneration()
    {
        var ran = new List<string>();
        var (vm, manager) = await CreateAsync(SlowPlugin(ran));
        using var m = manager;
        vm.QueryText = "slow1";

        var enter = vm.ExecuteSelectedAsync();
        vm.QueryText = "slow2";                         // yeni nesil

        Assert.False(await enter);
        await WaitUntilAsync(() => Titles(vm).SequenceEqual(["res-slow2"]), "yeni sonuç");
        Assert.Empty(ran);
    }

    [Fact]
    public async Task PendingEnter_Reset_CancelsIt()
    {
        var ran = new List<string>();
        var (vm, manager) = await CreateAsync(SlowPlugin(ran));
        using var m = manager;
        vm.QueryText = "slow";

        var enter = vm.ExecuteSelectedAsync();
        vm.Reset();

        Assert.False(await enter);
        Assert.Empty(ran);
    }

    [Fact]
    public async Task DoubleEnter_WhileQueryPending_RunsActionOnce()
    {
        var ran = new List<string>();
        var (vm, manager) = await CreateAsync(SlowPlugin(ran));
        using var m = manager;
        vm.QueryText = "slow";

        var first = vm.ExecuteSelectedAsync();
        var second = vm.ExecuteSelectedAsync();

        Assert.False(await second);                     // devam eden yürütme varken yok sayıldı
        Assert.True(await first);
        Assert.Equal(["res-slow"], ran);
    }

    [Fact]
    public async Task ExecuteSelected_WhileActionRunning_IgnoresSecondEnter()
    {
        var gate = new TaskCompletionSource();
        var ran = 0;
        var plugin = new FakePlugin("p", ["*"], (_, _) => Results(
            R("a", 1, async () => { ran++; await gate.Task; return true; })));
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;
        vm.QueryText = "q";
        await WaitUntilAsync(() => vm.Results.Count == 1, "sonuç");

        var first = vm.ExecuteSelectedAsync();
        Assert.False(await vm.ExecuteSelectedAsync());
        gate.SetResult();

        Assert.True(await first);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void ResultItem_WithIconImage_HasNoInitial()
    {
        var withImage = new ResultItemViewModel(new Launchly.Plugin.Result { Title = "Notepad", IconImage = new System.Windows.Media.DrawingImage() });
        var without = new ResultItemViewModel(new Launchly.Plugin.Result { Title = "Notepad" });

        Assert.NotNull(withImage.Image);
        Assert.Equal(string.Empty, withImage.Initial);
        Assert.Equal("N", without.Initial);
    }

    [Fact]
    public async Task SubTitleAndIcon_AreExposedForTheRow()
    {
        var plugin = new FakePlugin("p", ["*"], (_, _) => Results(
            new Launchly.Plugin.Result { Title = "Notepad", SubTitle = "C:\\Windows\\notepad.exe", Icon = "\uE8A5", TypeLabel = "Application" },
            R("Zed")));
        var (vm, manager) = await CreateAsync(plugin);
        using var m = manager;
        vm.QueryText = "q";
        await WaitUntilAsync(() => vm.Results.Count == 2, "sonuçlar");

        var withIcon = vm.Results[0];
        Assert.True(withIcon.HasSubTitle);
        Assert.Equal("\uE8A5", withIcon.Glyph);
        Assert.Equal(string.Empty, withIcon.Initial);
        Assert.Equal("Application", withIcon.TypeLabel);

        var noIcon = vm.Results[1];
        Assert.False(noIcon.HasSubTitle);
        Assert.Equal(string.Empty, noIcon.Glyph);
        Assert.Equal("Z", noIcon.Initial);
    }
}
