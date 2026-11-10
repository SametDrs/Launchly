using Launchly.Core;
using Launchly.Core.Plugins;
using Launchly.Plugin;
using Launchly.Plugins.Programs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Tests;

public sealed class ProgramsPluginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "launchly-programs-" + Guid.NewGuid().ToString("N"));

    public ProgramsPluginTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CachePath => Path.Combine(_dir, "cache", "programs.json");

    private string MakeLnk(string root, string name)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name + ".lnk");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    // Sahte çözümleyici: hedef, "<ad>.exe" ya da tablodan.
    private static Func<string, string> Resolver(Dictionary<string, string>? map = null) =>
        lnk => map is not null && map.TryGetValue(Path.GetFileNameWithoutExtension(lnk), out var t)
            ? t
            : @"C:\Apps\" + Path.GetFileNameWithoutExtension(lnk) + ".exe";

    private ProgramsPlugin CreatePlugin(ProgramScanner scanner, Action<string>? launch = null) =>
        new(NullLogger.Instance, CachePath, scanner, launch ?? (_ => { }));

    private static async Task<IReadOnlyList<Result>> Query(ProgramsPlugin plugin, string text) =>
        await plugin.QueryAsync(new Query(text, string.Empty, text), CancellationToken.None);

    private async Task<ProgramsPlugin> InitializedAsync(string[] names, Action<string>? launch = null)
    {
        var root = Path.Combine(_dir, "start");
        foreach (var n in names)
        {
            MakeLnk(root, n);
        }

        var plugin = CreatePlugin(new ProgramScanner([root], Resolver()), launch);
        await plugin.InitAsync(new PluginContext(_dir));
        await plugin.RefreshTask;
        return plugin;
    }

    [Fact]
    public void Scanner_FindsLnk_InSubfolders_AndDedupesAcrossRoots()
    {
        var user = Path.Combine(_dir, "user");
        var common = Path.Combine(_dir, "common");
        MakeLnk(user, "Visual Studio Code");
        MakeLnk(common, "Visual Studio Code");          // aynı ad + aynı hedef: yinelenen
        MakeLnk(Path.Combine(common, "Sub"), "Notepad");

        var list = new ProgramScanner([user, common], Resolver()).Scan();

        Assert.Equal(["Notepad", "Visual Studio Code"], list.Select(e => e.Name));
        Assert.Equal(Path.Combine(user, "Visual Studio Code.lnk"), list[1].ShortcutPath);   // kullanıcı klasörü önce
    }

    [Fact]
    public void Scanner_SkipsNonExecutableTargets_AndUninstallers()
    {
        var root = Path.Combine(_dir, "s");
        MakeLnk(root, "App");
        MakeLnk(root, "Docs");
        MakeLnk(root, "Uninstall App");
        MakeLnk(root, "Store thing");

        var map = new Dictionary<string, string> { ["Docs"] = @"C:\Apps\readme.html", ["Store thing"] = string.Empty };
        var list = new ProgramScanner([root], Resolver(map)).Scan();

        Assert.Equal(["App", "Store thing"], list.Select(e => e.Name));
    }

    [Fact]
    public void Scanner_SameNameDifferentTargets_AreBothKept()
    {
        var a = Path.Combine(_dir, "a");
        var b = Path.Combine(_dir, "b");
        MakeLnk(a, "Tool");
        MakeLnk(b, "Tool");
        var calls = 0;
        var scanner = new ProgramScanner([a, b], _ => @"C:\t" + (++calls) + ".exe");

        Assert.Equal(2, scanner.Scan().Count);
    }

    [Fact]
    public void Scanner_MissingRoot_IsIgnored()
    {
        var list = new ProgramScanner([Path.Combine(_dir, "yok")], Resolver()).Scan();

        Assert.Empty(list);
    }

    [Fact]
    public async Task Query_Prefix_RanksVisualStudioCodeFirst_AndHasTypeLabel()
    {
        var plugin = await InitializedAsync(["Visual Studio Code", "Notepad", "Calculator", "Voice Recorder"]);

        var results = await Query(plugin, "visual");

        Assert.Equal("Visual Studio Code", results[0].Title);
        Assert.Equal("Application", results[0].TypeLabel);
        Assert.Equal(@"C:\Apps\Visual Studio Code.exe", results[0].SubTitle);
    }

    [Fact]
    public async Task Query_Acronym_Vsc_FindsVisualStudioCode()
    {
        var plugin = await InitializedAsync(["Visual Studio Code", "Notepad", "Voice Source Control"]);

        var results = await Query(plugin, "vsc");

        Assert.Contains(results, r => r.Title == "Visual Studio Code");
        Assert.DoesNotContain(results, r => r.Title == "Notepad");
    }

    [Fact]
    public async Task Query_Blank_ListsAllAlphabetically()
    {
        var plugin = await InitializedAsync(["Zebra", "alpha", "Mango"]);

        var results = await Query(plugin, string.Empty);

        Assert.Equal(["alpha", "Mango", "Zebra"], results.Select(r => r.Title));
    }

    [Fact]
    public async Task Metadata_IsGlobal_AndHandlesEmptyQuery()
    {
        var plugin = await InitializedAsync([]);

        Assert.Equal([PluginMetadata.GlobalKeyword], plugin.Metadata.ActionKeywords);
        Assert.True(plugin.Metadata.HandlesEmptyQuery);
    }

    [Fact]
    public async Task PluginManager_BlankQuery_ShowsProgramsAlphabetical_ButOtherPluginsStayQuiet()
    {
        var plugin = await InitializedAsync(["Beta", "Alpha"]);
        var other = new FakePlugin("other", ["*"], (_, _) => Task.FromResult<IReadOnlyList<Result>>([new Result { Title = "x" }]));
        using var manager = new PluginManager(
            [plugin, other], new Settings(), NullLogger<PluginManager>.Instance, Path.Combine(_dir, "plugins"));
        IReadOnlyList<Result>? last = null;

        await manager.QueryAsync(string.Empty, r => last = r);

        Assert.True(manager.HasEmptyQueryPlugins);
        Assert.Equal(["Alpha", "Beta"], last!.Select(r => r.Title));
        Assert.Equal(0, other.QueryCount);
    }

    [Fact]
    public async Task Action_LaunchesShortcut_AndReturnsTrue_WindowHides()
    {
        string? launched = null;
        var plugin = await InitializedAsync(["Notepad"], p => launched = p);

        var r = (await Query(plugin, "notepad"))[0];

        Assert.True(await r.Action!());
        Assert.EndsWith("Notepad.lnk", launched);
    }

    [Fact]
    public async Task Action_WhenLaunchFails_ReturnsFalse_AndDoesNotThrow()
    {
        var plugin = await InitializedAsync(["Notepad"], _ => throw new InvalidOperationException("açılamadı"));

        var r = (await Query(plugin, "notepad"))[0];

        Assert.False(await r.Action!());
    }

    [Fact]
    public async Task Cache_IsWrittenAfterScan_AndServesQueriesWhileNextScanIsStillRunning()
    {
        await InitializedAsync(["Alpha", "Beta"]);
        Assert.True(File.Exists(CachePath));

        // İkinci açılış: tarama bloklu; sonuçlar yalnızca önbellekten gelebilir.
        using var release = new ManualResetEventSlim();
        var root = Path.Combine(_dir, "start");
        var second = CreatePlugin(new ProgramScanner([root], lnk =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return Resolver()(lnk);
        }));

        await second.InitAsync(new PluginContext(_dir));

        Assert.False(second.RefreshTask.IsCompleted);
        Assert.Equal(["Alpha", "Beta"], (await Query(second, string.Empty)).Select(r => r.Title));

        release.Set();
        await second.RefreshTask;
    }

    [Fact]
    public async Task Cache_RoundTrip_ReturnsSameEntriesWithoutScanning()
    {
        await InitializedAsync(["Alpha", "Beta"]);
        var storage = new Launchly.Core.Storage.JsonStorage<ProgramCache>(CachePath);

        var cache = storage.Load();

        Assert.Equal(["Alpha", "Beta"], cache.Programs.Select(p => p.Name));
        Assert.Equal(@"C:\Apps\Alpha.exe", cache.Programs[0].TargetPath);
    }

    [Fact]
    public async Task Init_WithCorruptCache_DoesNotThrow_AndRescans()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        File.WriteAllText(CachePath, "{ bozuk json");

        var plugin = await InitializedAsync(["Alpha"]);

        Assert.Equal(1, plugin.Count);
    }

    [Fact]
    public void RealResolver_ReadsTargetOfRealShortcut()
    {
        var exe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var lnk = Path.Combine(_dir, "Gerçek.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(lnk);
        link.TargetPath = exe;
        link.Save();

        var entries = new ProgramScanner([_dir]).Scan();

        var entry = Assert.Single(entries);
        Assert.Equal("Gerçek", entry.Name);
        Assert.Equal(exe, entry.TargetPath, ignoreCase: true);
    }

    [Fact]
    public void IconLoader_ReturnsFrozenImage_AndCachesIt()
    {
        var exe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var loader = new IconLoader();

        var first = loader.Get(exe);
        var second = loader.Get(exe);

        Assert.NotNull(first);
        Assert.True(first!.IsFrozen);
        Assert.Same(first, second);
    }
}
