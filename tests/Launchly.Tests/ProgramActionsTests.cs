using System.ComponentModel;
using System.Text.Json;
using Launchly.Core;
using Launchly.Plugin;
using Launchly.Plugins.Programs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Tests;

/// <summary>Programs eylemleri: yönetici, dosya konumu, yolu kopyala, gizle, sabitle (gerçek kabuk çağrısı yok).</summary>
public sealed class ProgramActionsTests : IDisposable
{
    private const string CalcAumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "launchly-actions-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = [];

    public ProgramActionsTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            d.Dispose();
        }

        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string UsagePath => Path.Combine(_dir, "usage.json");

    private string StartRoot => Path.Combine(_dir, "start");

    private ProgramUsageStore Store()
    {
        var store = new ProgramUsageStore(UsagePath, new FakeTimeProvider(), NullLogger.Instance, TimeSpan.FromHours(1));
        _disposables.Add(store);
        return store;
    }

    private async Task<ProgramsPlugin> ReadyAsync(
        string[] lnkNames,
        ProgramUsageStore usage,
        FakeProgramLauncher launcher,
        RecordingClipboard? clipboard = null,
        IAppsFolderSource? apps = null,
        Dictionary<string, string>? targets = null)
    {
        Directory.CreateDirectory(StartRoot);
        foreach (var n in lnkNames)
        {
            File.WriteAllText(Path.Combine(StartRoot, n + ".lnk"), string.Empty);
        }

        var scanner = new ProgramScanner(
            [StartRoot],
            lnk => targets is not null && targets.TryGetValue(Path.GetFileNameWithoutExtension(lnk), out var t)
                ? t
                : @"C:\Apps\" + Path.GetFileNameWithoutExtension(lnk) + ".exe",
            displayName: lnk => Path.GetFileNameWithoutExtension(lnk));
        var plugin = new ProgramsPlugin(
            NullLogger.Instance,
            Path.Combine(_dir, "cache", "programs.json"),
            scanner,
            launcher,
            new Settings(),
            apps ?? new FakeAppsFolder(),
            usage,
            () => null,
            clipboard);
        _disposables.Add(plugin);
        await plugin.InitAsync(new PluginContext(_dir));
        await plugin.RefreshTask;
        return plugin;
    }

    private static Task<IReadOnlyList<Result>> Query(ProgramsPlugin plugin, string text) =>
        plugin.QueryAsync(new Query(text, string.Empty, text), CancellationToken.None);

    private static async Task<Result> Single(ProgramsPlugin plugin, string search, string title) =>
        (await Query(plugin, search)).Single(r => r.Title == title);

    private static ResultAction Action(Result r, string title) => r.ContextActions.Single(a => a.Title == title);

    private static string[] Titles(IEnumerable<Result> results) => results.Select(r => r.Title).ToArray();

    // ---------------------------------------------------------------- Eylem listesi

    [Fact]
    public async Task Shortcut_HasAllActions_InOrder_AdminCarriesIdAndHint()
    {
        var plugin = await ReadyAsync(["Alpha"], Store(), new FakeProgramLauncher(), new RecordingClipboard());

        var r = await Single(plugin, "alpha", "Alpha");

        Assert.Equal(["Yönetici olarak çalıştır", "Dosya konumunu aç", "Yolu kopyala", "Gizle", "Sabitle"], r.ContextActions.Select(a => a.Title));
        var admin = r.ContextActions[0];
        Assert.Equal(ResultAction.RunAsAdminId, admin.Id);
        Assert.Equal("Ctrl+Shift+Enter", admin.Hint);
        Assert.All(r.ContextActions, a => Assert.False(string.IsNullOrEmpty(a.Glyph)));
        Assert.True(Action(r, "Gizle").RequeryAfter);
        Assert.True(Action(r, "Sabitle").RequeryAfter);
        Assert.False(admin.RequeryAfter);
    }

    [Fact]
    public async Task AppsFolder_HasNoAdminOrReveal_CopiesShellPath()
    {
        var clip = new RecordingClipboard();
        var launcher = new FakeProgramLauncher();
        var plugin = await ReadyAsync([], Store(), launcher, clip, new FakeAppsFolder((CalcAumid, "Hesap Makinesi")));

        var r = await Single(plugin, "hesap", "Hesap Makinesi");

        Assert.Equal(["Yolu kopyala", "Gizle", "Sabitle"], r.ContextActions.Select(a => a.Title));
        Assert.DoesNotContain(r.ContextActions, a => a.Id == ResultAction.RunAsAdminId);
        Assert.True(await Action(r, "Yolu kopyala").Execute());
        Assert.Equal([@"shell:AppsFolder\" + CalcAumid], clip.Texts);
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public async Task ProtocolCommand_HasNoAdminOrReveal_CopiesCommand()
    {
        var clip = new RecordingClipboard();
        var plugin = await ReadyAsync(["Alpha"], Store(), new FakeProgramLauncher(), clip);

        var settings = await Single(plugin, string.Empty, "Ayarlar");   // ms-settings: yedeği

        Assert.Equal(["Yolu kopyala", "Gizle", "Sabitle"], settings.ContextActions.Select(a => a.Title));
        Assert.True(await Action(settings, "Yolu kopyala").Execute());
        Assert.Equal(["ms-settings:"], clip.Texts);
    }

    [Fact]
    public async Task FileCommand_AdminAndReveal_UseResolvedFile()
    {
        var launcher = new FakeProgramLauncher();
        var plugin = await ReadyAsync(["Alpha"], Store(), launcher);

        var cmd = await Single(plugin, string.Empty, "Komut İstemi");   // cmd.exe yedeği
        var resolved = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        Assert.Equal(["Yönetici olarak çalıştır", "Dosya konumunu aç", "Gizle", "Sabitle"], cmd.ContextActions.Select(a => a.Title));   // pano yok: kopyala yok
        Assert.True(await Action(cmd, "Dosya konumunu aç").Execute());
        Assert.Equal([resolved], launcher.Revealed, StringComparer.OrdinalIgnoreCase);
        Assert.True(await Action(cmd, "Yönetici olarak çalıştır").Execute());
        Assert.Equal(["cmd.exe"], launcher.AdminLaunched);
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public async Task Reveal_ExistingTarget_SelectsTarget_MissingTarget_SelectsShortcut()
    {
        var exe = Path.Combine(_dir, "real.exe");
        File.WriteAllText(exe, string.Empty);
        var launcher = new FakeProgramLauncher();
        var plugin = await ReadyAsync(["Real", "Ghost"], Store(), launcher, targets: new() { ["Real"] = exe });

        Assert.True(await Action(await Single(plugin, "real", "Real"), "Dosya konumunu aç").Execute());
        Assert.True(await Action(await Single(plugin, "ghost", "Ghost"), "Dosya konumunu aç").Execute());

        Assert.Equal([exe, Path.Combine(StartRoot, "Ghost.lnk")], launcher.Revealed);
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public async Task CopyPath_ShortcutCopiesTarget_OrShortcutWhenTargetEmpty()
    {
        var clip = new RecordingClipboard();
        var plugin = await ReadyAsync(["Alpha", "NoTarget"], Store(), new FakeProgramLauncher(), clip, targets: new() { ["NoTarget"] = string.Empty });

        Assert.True(await Action(await Single(plugin, "alpha", "Alpha"), "Yolu kopyala").Execute());
        Assert.True(await Action(await Single(plugin, "notarget", "NoTarget"), "Yolu kopyala").Execute());

        Assert.Equal([@"C:\Apps\Alpha.exe", Path.Combine(StartRoot, "NoTarget.lnk")], clip.Texts);
    }

    [Fact]
    public async Task Copy_ClipboardFailure_ReturnsFalse()
    {
        var plugin = await ReadyAsync(["Alpha"], Store(), new FakeProgramLauncher());
        var entry = plugin.Entries.Single();
        var broken = new ProgramsPlugin(
            NullLogger.Instance, Path.Combine(_dir, "c2", "p.json"), new ProgramScanner([]), new FakeProgramLauncher(),
            usage: Store(), clipboard: new ThrowingClipboard());
        _disposables.Add(broken);

        Assert.False(await broken.BuildActions(entry).Single(a => a.Title == "Yolu kopyala").Execute());
    }

    private sealed class ThrowingClipboard : Launchly.Plugins.Calculator.IClipboardService
    {
        public void SetText(string text) => throw new InvalidOperationException("pano kilitli");
    }

    // ---------------------------------------------------------------- Yönetici olarak çalıştır

    [Fact]
    public async Task Admin_Success_LaunchesElevated_AndRecordsUsage()
    {
        var usage = Store();
        var launcher = new FakeProgramLauncher();
        var plugin = await ReadyAsync(["Alpha"], usage, launcher);
        var r = await Single(plugin, "alpha", "Alpha");

        Assert.True(await r.ContextActions.Single(a => a.Id == ResultAction.RunAsAdminId).Execute());

        Assert.Equal([Path.Combine(StartRoot, "Alpha.lnk")], launcher.AdminLaunched);
        Assert.Empty(launcher.Launched);
        Assert.Equal([plugin.Entries.Single().Id], usage.TopIds(5));
    }

    [Fact]
    public async Task Admin_UacCancelled_ReturnsFalse_DoesNotRecord()
    {
        var usage = Store();
        var launcher = new FakeProgramLauncher { AdminError = new Win32Exception(ShellProgramLauncher.ErrorCancelled) };
        var plugin = await ReadyAsync(["Alpha"], usage, launcher);
        var r = await Single(plugin, "alpha", "Alpha");

        Assert.False(await r.ContextActions.Single(a => a.Id == ResultAction.RunAsAdminId).Execute());

        Assert.Empty(usage.TopIds(5));
        Assert.Empty(launcher.Launched);   // normal açılışa düşülmez
    }

    [Fact]
    public async Task Admin_OtherError_ReturnsFalse_DoesNotRecord()
    {
        var usage = Store();
        var launcher = new FakeProgramLauncher { AdminError = new Win32Exception(2) };
        var plugin = await ReadyAsync(["Alpha"], usage, launcher);

        Assert.False(await (await Single(plugin, "alpha", "Alpha")).ContextActions[0].Execute());
        Assert.Empty(usage.TopIds(5));
    }

    [Fact]
    public async Task Admin_CancelledCodeOnNormalLaunch_IsStillAnError()
    {
        // 1223 yalnızca yönetici isteminde "iptal" sayılır; normal açılış hatası da false döner, kayıt yok.
        var usage = Store();
        var plugin = await ReadyAsync(["Alpha"], usage, new FakeProgramLauncher(_ => throw new Win32Exception(ShellProgramLauncher.ErrorCancelled)));

        Assert.False(await (await Single(plugin, "alpha", "Alpha")).Action!());
        Assert.Empty(usage.TopIds(5));
    }

    // ---------------------------------------------------------------- Gizle

    [Fact]
    public async Task Hide_RemovesFromSearchAndEmptyList_ReturnsFalse_KeepsUsage()
    {
        var usage = Store();
        var plugin = await ReadyAsync(["Alpha", "Alphabet"], usage, new FakeProgramLauncher());
        var alpha = plugin.Entries.Single(e => e.Name == "Alpha");
        usage.RecordLaunch(alpha.Id);
        Assert.Equal("Alpha", (await Query(plugin, string.Empty))[0].Title);

        Assert.False(await Action(await Single(plugin, "alpha", "Alpha"), "Gizle").Execute());   // pencere açık kalır

        Assert.Equal(["Alphabet"], Titles(await Query(plugin, "alpha")));
        Assert.DoesNotContain("Alpha", Titles(await Query(plugin, string.Empty)));
        Assert.True(usage.IsHidden(alpha.Id));
        Assert.Equal("Alpha", Assert.Single(usage.Hidden).Name);
        Assert.Equal([alpha.Id], usage.TopIds(5));          // sayaç silinmez
    }

    [Fact]
    public async Task Hide_PinnedEntry_IsNotShownInEmptyList()
    {
        var usage = Store();
        var plugin = await ReadyAsync(["Alpha"], usage, new FakeProgramLauncher());
        var id = plugin.Entries.Single().Id;
        usage.Pin(id);
        usage.Hide(id, "Alpha");

        Assert.DoesNotContain("Alpha", Titles(await Query(plugin, string.Empty)));
        Assert.Empty(await Query(plugin, "alpha"));
    }

    [Fact]
    public async Task Hide_StarterCommand_IsSkipped_NextStarterFillsIn()
    {
        var usage = Store();
        var plugin = await ReadyAsync(["Alpha"], usage, new FakeProgramLauncher());

        Assert.False(await Action(await Single(plugin, string.Empty, "Dosya Gezgini"), "Gizle").Execute());

        Assert.Equal(["Ayarlar", "Komut İstemi", "Not Defteri", "Hesap Makinesi"], Titles(await Query(plugin, string.Empty)));
        Assert.Contains("cmd:explorer.exe", usage.HiddenIds);
    }

    [Fact]
    public async Task Hide_Persists_AcrossStoreInstances_AndUnhideRestoresFrecencyOrder()
    {
        string alphaId;
        {
            using var first = new ProgramUsageStore(UsagePath, new FakeTimeProvider(), NullLogger.Instance, TimeSpan.FromHours(1));
            var plugin = await ReadyAsync(["Alpha", "Beta"], first, new FakeProgramLauncher());
            alphaId = plugin.Entries.Single(e => e.Name == "Alpha").Id;
            var betaId = plugin.Entries.Single(e => e.Name == "Beta").Id;
            for (var i = 0; i < 3; i++)
            {
                first.RecordLaunch(alphaId);
            }

            first.RecordLaunch(betaId);
            await Action(await Single(plugin, "alpha", "Alpha"), "Gizle").Execute();
            plugin.Dispose();
        }   // Dispose diske yazar

        var second = Store();
        Assert.True(second.IsHidden(alphaId));
        Assert.Equal("Alpha", Assert.Single(second.Hidden).Name);
        var again = await ReadyAsync([], second, new FakeProgramLauncher());
        Assert.Equal("Beta", (await Query(again, string.Empty))[0].Title);

        Assert.True(second.Unhide(alphaId));

        Assert.Equal(["Alpha", "Beta"], Titles(await Query(again, string.Empty)).Take(2));   // frecency sırası geri gelir
        Assert.False(second.Unhide(alphaId));
    }

    // ---------------------------------------------------------------- Sabitle

    [Fact]
    public async Task Pin_PutsEntriesFirst_InPinOrder_ThenUsage_ThenStarters()
    {
        var usage = Store();
        var plugin = await ReadyAsync(["Alpha", "Beta", "Gamma"], usage, new FakeProgramLauncher());
        var gamma = plugin.Entries.Single(e => e.Name == "Gamma").Id;
        usage.RecordLaunch(gamma);

        Assert.False(await Action(await Single(plugin, "beta", "Beta"), "Sabitle").Execute());
        Assert.False(await Action(await Single(plugin, "alpha", "Alpha"), "Sabitle").Execute());

        Assert.Equal(["Beta", "Alpha", "Gamma", "Dosya Gezgini", "Ayarlar", "Komut İstemi"], Titles(await Query(plugin, string.Empty)));
        Assert.Equal("Sabitlemeyi kaldır", (await Single(plugin, "beta", "Beta")).ContextActions[^1].Title);
    }

    [Fact]
    public async Task Unpin_RestoresOrder_KeepsOtherPins()
    {
        var usage = Store();
        var plugin = await ReadyAsync(["Alpha", "Beta"], usage, new FakeProgramLauncher());
        var alpha = plugin.Entries.Single(e => e.Name == "Alpha").Id;
        var beta = plugin.Entries.Single(e => e.Name == "Beta").Id;
        usage.Pin(beta);
        usage.Pin(alpha);

        Assert.False(await Action(await Single(plugin, "beta", "Beta"), "Sabitlemeyi kaldır").Execute());

        Assert.Equal([alpha], usage.PinnedIds);
        Assert.Equal(["Alpha", "Dosya Gezgini"], Titles(await Query(plugin, string.Empty)).Take(2));
        Assert.Equal("Sabitle", (await Single(plugin, "beta", "Beta")).ContextActions[^1].Title);
    }

    [Fact]
    public async Task Pin_StaleTitle_DoesWhatTitleSays()
    {
        // Menü başlığı sorgu anındaki durumdur: "Sabitle" sonradan basılırsa yine sabitler (ters çevirmez).
        var usage = Store();
        var plugin = await ReadyAsync(["Alpha"], usage, new FakeProgramLauncher());
        var id = plugin.Entries.Single().Id;
        var pin = Action(await Single(plugin, "alpha", "Alpha"), "Sabitle");
        usage.Pin(id);

        await pin.Execute();

        Assert.Equal([id], usage.PinnedIds);
    }

    [Fact]
    public async Task PinnedButDeletedApp_IsSkipped_ButKeptInStore()
    {
        var usage = Store();
        usage.Pin(@"lnk:C:\Start\Silindi.lnk");
        usage.Pin("app:Gone_x!App");
        var plugin = await ReadyAsync(["Alpha"], usage, new FakeProgramLauncher());

        Assert.Equal(["Dosya Gezgini", "Ayarlar", "Komut İstemi", "Not Defteri", "Hesap Makinesi"], Titles(await Query(plugin, string.Empty)));
        Assert.Equal(2, usage.PinnedIds.Count);
    }

    [Fact]
    public async Task Pin_ManyPinned_CappedAtSix()
    {
        var usage = Store();
        var names = Enumerable.Range(1, 8).Select(i => "App" + i).ToArray();
        var plugin = await ReadyAsync(names, usage, new FakeProgramLauncher());
        foreach (var e in plugin.Entries.OrderByDescending(e => e.Name))
        {
            usage.Pin(e.Id);
        }

        Assert.Equal(["App8", "App7", "App6", "App5", "App4", "App3"], Titles(await Query(plugin, string.Empty)));
    }

    [Fact]
    public async Task PrefsActions_AfterDispose_DoNothing()
    {
        var usage = Store();
        var plugin = await ReadyAsync(["Alpha"], usage, new FakeProgramLauncher());
        var r = await Single(plugin, "alpha", "Alpha");
        plugin.Dispose();

        Assert.False(await Action(r, "Gizle").Execute());
        Assert.False(await Action(r, "Sabitle").Execute());
        Assert.Empty(usage.HiddenIds);
        Assert.Empty(usage.PinnedIds);
    }

    // ---------------------------------------------------------------- usage.json uyumluluğu

    [Fact]
    public void OldUsageJson_WithoutHiddenPinned_Loads()
    {
        File.WriteAllText(UsagePath, """
            {"Version":1,"Items":{"lnk:C:\\Start\\A.lnk":{"Count":2,"LastUsedUtc":"2026-01-01T00:00:00Z"}}}
            """);

        var store = Store();

        Assert.Empty(store.HiddenIds);
        Assert.Empty(store.PinnedIds);
        Assert.Equal([@"lnk:C:\Start\A.lnk"], store.TopIds(5));
    }

    [Fact]
    public void UsageJson_NullAndDuplicatePrefs_AreNormalized()
    {
        File.WriteAllText(UsagePath, """
            {"Version":1,"Items":null,"Hidden":[null,{"Id":"x","Name":null},{"Id":"X","Name":"dup"},{"Id":"","Name":"e"}],"Pinned":[null,"a","A","","b"]}
            """);

        var store = Store();

        Assert.Equal(["x"], store.Hidden.Select(h => h.Id));
        Assert.Equal(["a", "b"], store.PinnedIds);
        Assert.True(store.IsHidden("X"));
        Assert.True(store.IsPinned("B"));
    }

    [Fact]
    public void Prefs_Saved_WithVersion1_AndChangedEventFires()
    {
        var events = 0;
        {
            using var store = new ProgramUsageStore(UsagePath, new FakeTimeProvider(), NullLogger.Instance, TimeSpan.FromHours(1));
            store.Changed += () => events++;
            Assert.True(store.Hide("lnk:a", "A"));
            Assert.False(store.Hide("lnk:a", "A"));        // değişiklik yok: olay da yok
            Assert.True(store.Pin("lnk:b"));
            Assert.False(store.Unpin("lnk:yok"));
        }

        Assert.Equal(2, events);
        using var doc = JsonDocument.Parse(File.ReadAllText(UsagePath));
        Assert.Equal(1, doc.RootElement.GetProperty("Version").GetInt32());
        Assert.Equal("A", doc.RootElement.GetProperty("Hidden")[0].GetProperty("Name").GetString());
        Assert.Equal("lnk:b", doc.RootElement.GetProperty("Pinned")[0].GetString());
    }
}
