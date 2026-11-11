using System.Text.Json;
using Launchly.Core;
using Launchly.Core.Querying;
using Launchly.Core.Storage;
using Launchly.Plugin;
using Launchly.Plugins.Programs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Tests;

/// <summary>Elle ilerletilen saat (yeni NuGet paketi yok). Zamanlayıcılar sistem saatini kullanır.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private long _ticks = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero).UtcTicks;

    public DateTimeOffset Now
    {
        get => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _ticks, value.UtcTicks);
    }

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Sahte AppsFolder kaynağı: liste, istisna ya da bloklayan temsilci.</summary>
internal sealed class FakeAppsFolder : IAppsFolderSource
{
    private readonly Func<CancellationToken, IReadOnlyList<(string ParsingName, string DisplayName)>> _enumerate;

    public FakeAppsFolder(params (string ParsingName, string DisplayName)[] items) => _enumerate = _ => items;

    public FakeAppsFolder(Func<CancellationToken, IReadOnlyList<(string ParsingName, string DisplayName)>> enumerate) => _enumerate = enumerate;

    public IReadOnlyList<(string ParsingName, string DisplayName)> Enumerate(CancellationToken cancellationToken) => _enumerate(cancellationToken);
}

public sealed class ProgramDiscoveryTests : IDisposable
{
    private const string CalcAumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
    private const string NotepadAumid = "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App";
    private const string TerminalAumid = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
    private const string SettingsAumid = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "launchly-discovery-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = [];

    public ProgramDiscoveryTests()
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

    private string CachePath => Path.Combine(_dir, "cache", "programs.json");

    private string UsagePath => Path.Combine(_dir, "usage.json");

    private string MakeLnk(string root, string name)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name + ".lnk");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private static Func<string, string> Resolver(Dictionary<string, string>? map = null) =>
        lnk => map is not null && map.TryGetValue(Path.GetFileNameWithoutExtension(lnk), out var t)
            ? t
            : @"C:\Apps\" + Path.GetFileNameWithoutExtension(lnk) + ".exe";

    // Görünen ad = dosya adı: gerçek kabuk çağrısı testleri yavaşlatmaz, makine diline bağlı olmaz.
    private static string FileNameOnly(string lnk) => Path.GetFileNameWithoutExtension(lnk);

    private ProgramUsageStore Store(FakeTimeProvider? time = null, TimeSpan? delay = null)
    {
        var store = new ProgramUsageStore(UsagePath, time, NullLogger.Instance, delay ?? TimeSpan.FromHours(1));
        _disposables.Add(store);
        return store;
    }

    private ProgramsPlugin Plugin(
        ProgramScanner scanner,
        ProgramUsageStore usage,
        IAppsFolderSource? apps = null,
        Func<string?>? browser = null,
        Action<string>? launch = null,
        Settings? settings = null)
    {
        var plugin = new ProgramsPlugin(NullLogger.Instance, CachePath, scanner, new FakeProgramLauncher(launch), settings, apps, usage, browser);
        _disposables.Add(plugin);
        return plugin;
    }

    private async Task<ProgramsPlugin> ReadyAsync(
        string[] lnkNames,
        ProgramUsageStore usage,
        IAppsFolderSource? apps = null,
        Func<string?>? browser = null,
        Dictionary<string, string>? targets = null,
        Action<string>? launch = null,
        Settings? settings = null)
    {
        var root = Path.Combine(_dir, "start");
        Directory.CreateDirectory(root);
        foreach (var n in lnkNames)
        {
            MakeLnk(root, n);
        }

        var plugin = Plugin(new ProgramScanner([root], Resolver(targets), displayName: FileNameOnly), usage, apps, browser, launch, settings);
        await plugin.InitAsync(new PluginContext(_dir));
        await plugin.RefreshTask;
        return plugin;
    }

    private static async Task<IReadOnlyList<Result>> Query(ProgramsPlugin plugin, string text) =>
        await plugin.QueryAsync(new Query(text, string.Empty, text), CancellationToken.None);

    private static ProgramEntry Lnk(string name, string target = "") =>
        new() { Name = name, ShortcutPath = @"C:\Start\" + name + ".lnk", TargetPath = target };

    // ---------------------------------------------------------------- ProgramScanner: görünen ad

    [Fact]
    public void Scanner_UsesInjectedDisplayName_AndFallsBackToFileName()
    {
        var root = Path.Combine(_dir, "s");
        MakeLnk(root, "Command Prompt");
        MakeLnk(root, "Empty");
        MakeLnk(root, "Throws");
        MakeLnk(root, "Suffix");

        var list = new ProgramScanner([root], Resolver(), displayName: lnk => Path.GetFileNameWithoutExtension(lnk) switch
        {
            "Command Prompt" => "Komut İstemi",
            "Empty" => "   ",
            "Throws" => throw new InvalidOperationException("kabuk"),
            _ => "Son Ek.lnk",   // uzantı gizli değilse kabuk ".lnk" ile döner
        }).Scan();

        Assert.Equal(["Empty", "Komut İstemi", "Son Ek", "Throws"], list.Select(e => e.Name));
        Assert.EndsWith("Command Prompt.lnk", list.Single(e => e.Name == "Komut İstemi").ShortcutPath);
    }

    [Fact]
    public void Scanner_LocalizedUninstallerName_IsFiltered()
    {
        var root = Path.Combine(_dir, "s");
        MakeLnk(root, "Remove Tool");
        MakeLnk(root, "Tool");

        var list = new ProgramScanner([root], Resolver(), displayName: lnk =>
            Path.GetFileNameWithoutExtension(lnk) == "Remove Tool" ? "Aracı Kaldır" : "Araç").Scan();

        Assert.Equal(["Araç"], list.Select(e => e.Name));
    }

    // ---------------------------------------------------------------- ProgramCatalog.Merge

    [Fact]
    public void Merge_PackagedItem_IsAddedAsAppsFolder()
    {
        var merged = ProgramCatalog.Merge([], [("Claude_pzs8sxrjxfjjc!Claude", "Claude")]);

        var e = Assert.Single(merged);
        Assert.Equal(ProgramKind.AppsFolder, e.Kind);
        Assert.Equal("Claude_pzs8sxrjxfjjc!Claude", e.AppUserModelId);
        Assert.Equal("Claude", e.Name);
        Assert.Equal(string.Empty, e.TargetPath);
    }

    [Fact]
    public void Merge_SameNameUnpackagedItem_JoinsLnk_AndCarriesAumid()
    {
        var lnk = Lnk("Dosya Gezgini", @"C:\Windows\explorer.exe");

        var merged = ProgramCatalog.Merge([lnk], [("Microsoft.Windows.Explorer", "DOSYA GEZGİNİ ")]);

        var e = Assert.Single(merged);
        Assert.Equal(ProgramKind.Shortcut, e.Kind);
        Assert.Equal("Microsoft.Windows.Explorer", e.AppUserModelId);
        Assert.Equal(@"C:\Windows\explorer.exe", e.TargetPath);
        Assert.Equal(string.Empty, lnk.AppUserModelId);   // girdi listesi değiştirilmez
    }

    [Fact]
    public void Merge_UniqueUnpackagedItems_AreAdded()
    {
        var merged = ProgramCatalog.Merge(
            [Lnk("Notepad++", @"C:\Apps\notepad++.exe")],
            [
                ("Microsoft.Windows.ControlPanel", "Denetim Masası"),
                ("Microsoft.AutoGenerated.{0A1B2C3D-0000-0000-0000-000000000000}", "Eski Araç"),
                ("steam://rungameid/570", "Dota 2"),
                (@"{6D809377-6AF0-444B-8957-A3773F02200E}\Tools\tool.exe", "Tool"),
            ]);

        Assert.Equal(5, merged.Count);
        Assert.All(merged.Where(e => e.Name != "Notepad++"), e => Assert.Equal(ProgramKind.AppsFolder, e.Kind));
        Assert.Contains(merged, e => e.AppUserModelId == "steam://rungameid/570");
    }

    [Fact]
    public void Merge_FiltersUninstallers_WebLinks_AndDocuments()
    {
        var merged = ProgramCatalog.Merge(
            [],
            [
                ("Contoso.App_abc!App", "Uninstall Contoso"),
                ("Contoso.Remover", "Contoso'yu Kaldır"),
                ("https://contoso.example/help", "Contoso Yardım"),
                (@"{6D809377-6AF0-444B-8957-A3773F02200E}\Contoso\readme.html", "Benioku"),
                ("", "Adsız"),
                ("Contoso.Empty", "  "),
                ("Contoso.App_abc!Main", "Contoso"),
                ("contoso.app_ABC!main", "Contoso yinelenen"),
            ]);

        Assert.Equal(["Contoso"], merged.Select(e => e.Name));
    }

    [Fact]
    public void Merge_Result_IsSortedByName()
    {
        var merged = ProgramCatalog.Merge(
            [Lnk("mango"), Lnk("Zebra")],
            [("A.B_x!App", "Çay"), ("A.C_x!App", "alpha"), ("Bravo.App", "Bravo")]);

        Assert.Equal(["alpha", "Bravo", "Çay", "mango", "Zebra"], merged.Select(e => e.Name));
    }

    [Fact]
    public void Sanitize_DropsInvalidEntries_AndNormalizesNulls()
    {
        var json = """
            [
              null,
              { "Name": "Ok", "ShortcutPath": "C:\\s\\Ok.lnk", "TargetPath": null, "AppUserModelId": null },
              { "Name": "App", "Kind": 1, "AppUserModelId": "X!App" },
              { "Name": "NoAumid", "Kind": 1 },
              { "Name": "Cmd", "Kind": 2, "ShortcutPath": "calc.exe" },
              { "Name": "Future", "Kind": 9, "ShortcutPath": "C:\\s\\F.lnk" },
              { "Name": "  ", "ShortcutPath": "C:\\s\\Blank.lnk" },
              { "Name": "NoPath" }
            ]
            """;
        var entries = JsonSerializer.Deserialize<List<ProgramEntry?>>(json);

        var clean = ProgramCatalog.Sanitize(entries);

        Assert.Equal(["Ok", "App"], clean.Select(e => e.Name));
        Assert.Equal(string.Empty, clean[0].TargetPath);
        Assert.Equal(string.Empty, clean[0].AppUserModelId);
        Assert.Empty(ProgramCatalog.Sanitize(null));
    }

    // ---------------------------------------------------------------- ProgramEntry: Id / LaunchPath

    [Theory]
    [InlineData(ProgramKind.Shortcut, @"C:\s\App.lnk", "", @"lnk:C:\s\App.lnk", @"C:\s\App.lnk")]
    [InlineData(ProgramKind.AppsFolder, "", "X!App", "app:X!App", @"shell:AppsFolder\X!App")]
    [InlineData(ProgramKind.Command, "ms-settings:", "", "cmd:ms-settings:", "ms-settings:")]
    public void Entry_IdAndLaunchPath_PerKind(ProgramKind kind, string shortcut, string aumid, string id, string launch)
    {
        var e = new ProgramEntry { Name = "n", Kind = kind, ShortcutPath = shortcut, AppUserModelId = aumid };

        Assert.Equal(id, e.Id);
        Assert.Equal(launch, e.LaunchPath);
    }

    [Fact]
    public void Entry_ComputedProperties_AreNotSerialized()
    {
        var json = JsonSerializer.Serialize(new ProgramEntry { Name = "n", Kind = ProgramKind.AppsFolder, AppUserModelId = "X!App" });

        Assert.DoesNotContain("\"Id\"", json);
        Assert.DoesNotContain("LaunchPath", json);
    }

    [Fact]
    public async Task Launch_AppsFolderEntry_SendsShellAppsFolderPath_AndRecordsAppId()
    {
        var usage = Store();
        string? launched = null;
        var plugin = await ReadyAsync([], usage, new FakeAppsFolder(("X!App", "Xapp")), launch: p => launched = p);

        var r = Assert.Single(await Query(plugin, "xapp"));

        Assert.True(await r.Action!());
        Assert.Equal(@"shell:AppsFolder\X!App", launched);
        Assert.Equal(1.0, usage.Frecency("app:X!App"));
    }

    [Fact]
    public async Task Launch_Failure_IsNotRecorded()
    {
        var usage = Store();
        var plugin = await ReadyAsync(["Alpha"], usage, launch: _ => throw new InvalidOperationException("yok"));

        var r = Assert.Single(await Query(plugin, "alpha"));

        Assert.False(await r.Action!());
        Assert.Empty(usage.TopIds(10));
    }

    [Fact]
    public async Task Launch_AfterDispose_IsNotRecorded()
    {
        var usage = Store();
        var launches = 0;
        var plugin = await ReadyAsync(["Alpha"], usage, launch: _ => launches++);
        var r = Assert.Single(await Query(plugin, "alpha"));

        plugin.Dispose();

        Assert.True(await r.Action!());   // açılış çalışır (pencere gizlenir), kayıt yapılmaz
        Assert.Equal(1, launches);
        Assert.Empty(usage.TopIds(10));
    }

    // ---------------------------------------------------------------- Önbellek

    [Fact]
    public async Task Cache_V1Json_LoadsAsShortcut_AndScanRewritesV2()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        File.WriteAllText(CachePath, """
            { "Version": 1, "ScannedAtUtc": "2026-01-01T00:00:00Z",
              "Programs": [ { "Name": "Alpha", "ShortcutPath": "C:\\Start\\Alpha.lnk", "TargetPath": "C:\\Apps\\Alpha.exe" } ] }
            """);
        var root = Path.Combine(_dir, "start");
        MakeLnk(root, "Alpha");
        using var release = new ManualResetEventSlim();
        var plugin = Plugin(new ProgramScanner([root], lnk =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return Resolver()(lnk);
        }, displayName: FileNameOnly), Store());

        await plugin.InitAsync(new PluginContext(_dir));

        var cached = Assert.Single(plugin.Entries);
        Assert.Equal(ProgramKind.Shortcut, cached.Kind);
        Assert.Equal(string.Empty, cached.AppUserModelId);
        Assert.Equal(@"lnk:C:\Start\Alpha.lnk", cached.Id);
        Assert.Equal("Alpha", Assert.Single(await Query(plugin, "alpha")).Title);

        release.Set();
        await plugin.RefreshTask;

        Assert.Equal(ProgramCache.CurrentVersion, new JsonStorage<ProgramCache>(CachePath).Load().Version);
        Assert.Equal(2, ProgramCache.CurrentVersion);
    }

    [Fact]
    public void Cache_V2_RoundTrip_KeepsAllFields()
    {
        var storage = new JsonStorage<ProgramCache>(CachePath);
        var programs = new List<ProgramEntry>
        {
            new() { Name = "Gezgin", ShortcutPath = @"C:\s\G.lnk", TargetPath = @"C:\Windows\explorer.exe", AppUserModelId = "Microsoft.Windows.Explorer" },
            new() { Name = "Hesap", Kind = ProgramKind.AppsFolder, AppUserModelId = CalcAumid },
        };

        storage.Save(new ProgramCache { ScannedAtUtc = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), Programs = programs });
        var loaded = storage.Load();

        Assert.Equal(2, loaded.Version);
        Assert.Equal(
            programs.Select(p => (p.Name, p.ShortcutPath, p.TargetPath, p.Kind, p.AppUserModelId, p.Id)),
            loaded.Programs.Select(p => (p.Name, p.ShortcutPath, p.TargetPath, p.Kind, p.AppUserModelId, p.Id)));
    }

    [Fact]
    public async Task AppsFolderFailure_LnkResultsAreStillWritten()
    {
        var plugin = await ReadyAsync(["Alpha", "Beta"], Store(), new FakeAppsFolder(_ => throw new InvalidCastException("COM")));

        Assert.True(plugin.RefreshTask.IsCompletedSuccessfully);
        Assert.Equal(2, plugin.Count);
        Assert.Equal(["Alpha", "Beta"], new JsonStorage<ProgramCache>(CachePath).Load().Programs.Select(p => p.Name));
    }

    [Fact]
    public async Task AppsFolderItems_AreMerged_Cached_AndSearchable()
    {
        var plugin = await ReadyAsync(["Alpha"], Store(), new FakeAppsFolder(("Claude_pzs8sxrjxfjjc!Claude", "Claude")));

        Assert.Equal("Claude", Assert.Single(await Query(plugin, "claude")).Title);
        var cached = new JsonStorage<ProgramCache>(CachePath).Load().Programs;
        Assert.Contains(cached, p => p.Kind == ProgramKind.AppsFolder && p.AppUserModelId == "Claude_pzs8sxrjxfjjc!Claude");
    }

    [Fact]
    public async Task Dispose_DuringAppsFolderScan_WritesNeitherListNorCache()
    {
        var root = Path.Combine(_dir, "start");
        MakeLnk(root, "Alpha");
        using var entered = new ManualResetEventSlim();
        var apps = new FakeAppsFolder(token =>
        {
            entered.Set();
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            token.ThrowIfCancellationRequested();
            return [("Late_x!App", "Late")];
        });
        var plugin = Plugin(new ProgramScanner([root], Resolver(), displayName: FileNameOnly), Store(), apps);
        await plugin.InitAsync(new PluginContext(_dir));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        plugin.Dispose();
        await plugin.RefreshTask;

        Assert.Equal(0, plugin.Count);
        Assert.Empty(new JsonStorage<ProgramCache>(CachePath).Load().Programs);   // önbellek yalnızca boş varsayılan
    }

    [Fact]
    public async Task AppsFolderSourceIgnoringCancel_LateResultIsDropped()
    {
        var root = Path.Combine(_dir, "start");
        MakeLnk(root, "Alpha");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var apps = new FakeAppsFolder(_ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return [("Late_x!App", "Late")];   // iptali görmeyen kaynak: sonuç yine de döner
        });
        var plugin = Plugin(new ProgramScanner([root], Resolver(), displayName: FileNameOnly), Store(), apps);
        await plugin.InitAsync(new PluginContext(_dir));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        plugin.Dispose();
        release.Set();
        await plugin.RefreshTask;

        Assert.Equal(0, plugin.Count);
        Assert.Empty(new JsonStorage<ProgramCache>(CachePath).Load().Programs);
    }

    // ---------------------------------------------------------------- ProgramUsageStore

    [Fact]
    public void Usage_RecordLaunch_IncrementsCount_AndUsesClock()
    {
        var time = new FakeTimeProvider();
        var store = Store(time);

        store.RecordLaunch("lnk:a");
        time.Advance(TimeSpan.FromHours(2));
        store.RecordLaunch("LNK:A");   // anahtar büyük/küçük harf duyarsız
        store.Flush();

        var item = Assert.Single(new JsonStorage<UsageData>(UsagePath).Load().Items);
        Assert.Equal(2, item.Value.Count);
        Assert.Equal(time.Now.UtcDateTime, item.Value.LastUsedUtc);
        Assert.Equal(2.0, store.Frecency("lnk:a"));

        time.Advance(TimeSpan.FromDays(3));
        Assert.Equal(1.4, store.Frecency("lnk:a"), 10);
        Assert.Equal(0, store.Frecency("lnk:yok"));
        Assert.Equal(0, store.Frecency(string.Empty));
    }

    [Theory]
    [InlineData(-1.0, 1.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.0001, 0.7)]
    [InlineData(7.0, 0.7)]
    [InlineData(7.0001, 0.5)]
    [InlineData(30.0, 0.5)]
    [InlineData(30.0001, 0.3)]
    [InlineData(90.0, 0.3)]
    [InlineData(90.0001, 0.1)]
    [InlineData(4000.0, 0.1)]
    public void Usage_WeightBands_Boundaries(double days, double weight)
    {
        Assert.Equal(weight, ProgramUsageStore.Weight(TimeSpan.FromDays(days)));
    }

    [Fact]
    public void Usage_WeightBands_ExactTickBoundary()
    {
        Assert.Equal(1.0, ProgramUsageStore.Weight(TimeSpan.FromDays(1)));
        Assert.Equal(0.7, ProgramUsageStore.Weight(TimeSpan.FromDays(1) + TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void Usage_TopIds_OrdersByFrecency_ThenRecency()
    {
        var time = new FakeTimeProvider();
        var store = Store(time);
        for (var i = 0; i < 3; i++)
        {
            store.RecordLaunch("old");          // 3 x 0.3 = 0.9 (40 gün sonra)
        }

        time.Advance(TimeSpan.FromDays(40));
        store.RecordLaunch("tie-older");        // 1.0
        time.Advance(TimeSpan.FromMinutes(1));
        store.RecordLaunch("tie-newer");        // 1.0, daha yeni

        Assert.Equal(["tie-newer", "tie-older", "old"], store.TopIds(10));
        Assert.Equal(["tie-newer"], store.TopIds(1));
        Assert.Empty(store.TopIds(0));
    }

    [Fact]
    public void Usage_Persists_NewInstanceReadsSameData()
    {
        var time = new FakeTimeProvider();
        var first = new ProgramUsageStore(UsagePath, time, NullLogger.Instance, TimeSpan.FromHours(1));
        first.RecordLaunch("app:X!App");
        first.RecordLaunch("app:X!App");
        first.RecordLaunch("lnk:b");
        first.Dispose();

        var second = Store(time);

        Assert.Equal(2.0, second.Frecency("app:x!app"));
        Assert.Equal(["app:X!App", "lnk:b"], second.TopIds(5));
    }

    [Fact]
    public void Usage_MissingFile_StartsEmpty()
    {
        var store = Store();

        Assert.Empty(store.TopIds(5));
        store.Flush();   // yazılacak bir şey yok
        Assert.Empty(new JsonStorage<UsageData>(UsagePath).Load().Items);
    }

    [Fact]
    public void Usage_CorruptFile_StartsEmpty_AndStillRecords()
    {
        File.WriteAllText(UsagePath, "{ bozuk json");
        var store = Store();

        Assert.Empty(store.TopIds(5));
        store.RecordLaunch("lnk:a");
        store.Flush();

        Assert.Equal(1, new JsonStorage<UsageData>(UsagePath).Load().Items["lnk:a"].Count);
    }

    [Fact]
    public void Usage_HandEditedFile_InvalidItemsDropped_CaseTwinsMerged()
    {
        File.WriteAllText(UsagePath, """
            { "Version": 1, "Items": {
                "lnk:a": { "Count": 2, "LastUsedUtc": "2026-11-11T10:00:00Z" },
                "LNK:A": { "Count": 3, "LastUsedUtc": "2026-11-11T11:00:00Z" },
                "lnk:zero": { "Count": 0, "LastUsedUtc": "2026-11-11T10:00:00Z" },
                "lnk:neg": { "Count": -4, "LastUsedUtc": "2026-11-11T10:00:00Z" },
                "lnk:null": null,
                "  ": { "Count": 5, "LastUsedUtc": "2026-11-11T10:00:00Z" },
                "lnk:local": { "Count": 1, "LastUsedUtc": "2026-11-11T10:00:00" }
            } }
            """);
        var store = Store(new FakeTimeProvider());

        Assert.Equal(5.0, store.Frecency("lnk:a"));
        Assert.Equal(["lnk:a", "lnk:local"], store.TopIds(10));
    }

    [Fact]
    public void Usage_NullItems_StartsEmpty()
    {
        File.WriteAllText(UsagePath, """{ "Version": 1, "Items": null }""");

        Assert.Empty(Store().TopIds(5));
    }

    [Fact]
    public async Task Usage_RecordLaunch_DoesNotWriteOnCaller_DebouncedWriteFollows()
    {
        var store = Store(delay: TimeSpan.FromMilliseconds(300));

        for (var i = 0; i < 20; i++)
        {
            store.RecordLaunch("lnk:a");
        }

        Assert.Equal(0, ReadCount(UsagePath, "lnk:a"));   // disk yazımı çağıranda değil
        await TestHelpers.WaitUntilAsync(() => ReadCount(UsagePath, "lnk:a") == 20, "tek yazımda 20");
    }

    [Fact]
    public async Task Usage_ParallelRecordAndRead_FinishWithoutException()
    {
        var store = Store(delay: TimeSpan.Zero);   // her kayıt arka planda gerçek yazım planlar
        const int perWorker = 400;
        var ids = new[] { "lnk:a", "lnk:b", "app:c", "cmd:d" };

        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < perWorker; i++)
            {
                store.RecordLaunch(ids[(w + i) % ids.Length]);
            }
        }));
        var readers = Enumerable.Range(0, 4).Select(r => Task.Run(() =>
        {
            for (var i = 0; i < perWorker; i++)
            {
                _ = store.Frecency(ids[i % ids.Length]);
                _ = store.TopIds(3);
            }
        }));
        var flusher = Task.Run(() =>
        {
            for (var i = 0; i < 20; i++)
            {
                store.Flush();
            }
        });

        await Task.WhenAll(writers.Concat(readers).Append(flusher)).WaitAsync(TestHelpers.TestTimeout);
        store.Dispose();

        Assert.Equal(4.0 * perWorker, ids.Sum(store.Frecency));
        Assert.Equal(4 * perWorker, ids.Sum(id => ReadCount(UsagePath, id)));
    }

    [Fact]
    public async Task Usage_AfterDispose_RecordIsIgnored_AndNotWritten()
    {
        var store = Store(delay: TimeSpan.FromMilliseconds(50));
        store.RecordLaunch("lnk:a");
        store.Dispose();
        Assert.Equal(1, ReadCount(UsagePath, "lnk:a"));
        var written = File.GetLastWriteTimeUtc(UsagePath);

        store.RecordLaunch("lnk:b");
        store.Flush();
        store.Dispose();   // idempotent
        await Task.Delay(200);

        Assert.Equal(0, store.Frecency("lnk:b"));
        Assert.Equal(0, ReadCount(UsagePath, "lnk:b"));
        Assert.Equal(written, File.GetLastWriteTimeUtc(UsagePath));
    }

    [Fact]
    public async Task Usage_AfterFlush_PendingDebounceDoesNotWriteAgain()
    {
        var store = Store(delay: TimeSpan.FromMilliseconds(100));
        store.RecordLaunch("lnk:a");
        store.Flush();
        File.Delete(UsagePath);

        await Task.Delay(400);   // planlanmış yazım uyanır, kirli değil: dosyaya dokunmaz

        Assert.False(File.Exists(UsagePath));
    }

    // Ham okuma: JsonStorage.Load yazım sırasında okursa dosyayı bozuk sanıp kenara alabilir (ve eksikse yazar).
    private static int ReadCount(string path, string id)
    {
        try
        {
            var data = JsonSerializer.Deserialize<UsageData>(
                File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return data?.Items is { } items && items.TryGetValue(id, out var e) ? e.Count : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return -1;
        }
    }

    // ---------------------------------------------------------------- Başlangıç listesi

    [Fact]
    public void Starters_ResolveByAumid_ByTargetFile_AndCommandFallback_BrowserNullSkipped()
    {
        var entries = new List<ProgramEntry>
        {
            new() { Name = "Hesap Makinesi", Kind = ProgramKind.AppsFolder, AppUserModelId = CalcAumid },
            Lnk("Notepad", @"C:\Windows\System32\NOTEPAD.EXE"),
        };

        var list = StarterApps.Resolve(entries, () => null);

        Assert.Equal(["Dosya Gezgini", "Ayarlar", "Komut İstemi", "Notepad", "Hesap Makinesi"], list.Select(e => e.Name));
        Assert.Equal(
            [ProgramKind.Command, ProgramKind.Command, ProgramKind.Command, ProgramKind.Shortcut, ProgramKind.AppsFolder],
            list.Select(e => e.Kind));
        Assert.Equal(["cmd:explorer.exe", "cmd:ms-settings:", "cmd:cmd.exe"], list.Take(3).Select(e => e.Id));
    }

    [Fact]
    public void Starters_BrowserByTargetFile_QuotedPath_AndThrowingResolverSkipped()
    {
        var chrome = Lnk("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");

        var found = StarterApps.Resolve([chrome], () => "\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\"");
        var thrown = StarterApps.Resolve([chrome], () => throw new InvalidOperationException("assoc"));
        var noLnk = StarterApps.Resolve([], () => @"C:\Browsers\other.exe");

        Assert.Equal("Google Chrome", found[1].Name);
        Assert.Equal(6, found.Count);
        Assert.DoesNotContain(thrown, e => e.Name == "Google Chrome");
        Assert.Equal(5, noLnk.Count);   // tarayıcının yedeği yok
    }

    [Fact]
    public void Starters_MergedExplorerLnk_ResolvesByCarriedAumid_TerminalPrefersAumidOverCmdLnk()
    {
        var merged = ProgramCatalog.Merge(
            [Lnk("Gezgin", @"C:\Other\shell.exe"), Lnk("Komut İstemi", @"C:\Windows\System32\cmd.exe")],
            [("Microsoft.Windows.Explorer", "Gezgin"), (TerminalAumid, "Terminal")]);

        var list = StarterApps.Resolve(merged, null);

        Assert.Equal("Gezgin", list[0].Name);              // hedef explorer.exe değil; taşınan AUMID ile bulundu
        Assert.Equal(ProgramKind.Shortcut, list[0].Kind);
        Assert.Equal("Terminal", list[2].Name);            // AUMID, cmd.exe kısayolundan önce gelir
    }

    [Fact]
    public async Task EmptyQuery_NoUsage_ShowsStarterListInOrder_AtMostSix()
    {
        var apps = new FakeAppsFolder(
            (CalcAumid, "Hesap Makinesi"),
            (NotepadAumid, "Not Defteri"),
            (TerminalAumid, "Terminal"),
            (SettingsAumid, "Ayarlar"),
            ("Microsoft.Windows.Explorer", "Dosya Gezgini"),
            ("Claude_pzs8sxrjxfjjc!Claude", "Claude"));
        var targets = new Dictionary<string, string>
        {
            ["Dosya Gezgini"] = @"C:\Windows\explorer.exe",
            ["Firefox"] = @"C:\Program Files\Mozilla Firefox\firefox.exe",
        };
        var plugin = await ReadyAsync(
            ["Dosya Gezgini", "Firefox", "Alpha"], Store(), apps, () => @"C:\Program Files\Mozilla Firefox\firefox.exe", targets);

        var results = await Query(plugin, string.Empty);

        Assert.Equal(["Dosya Gezgini", "Firefox", "Ayarlar", "Terminal", "Not Defteri", "Hesap Makinesi"], results.Select(r => r.Title));
        Assert.Equal(1, plugin.Entries.Count(e => e.Name == "Dosya Gezgini"));   // .lnk ile AppsFolder eşi birleşti
    }

    [Fact]
    public async Task EmptyQuery_WithUsage_FrecencyFirst_ThenStarters_NoDuplicates()
    {
        var time = new FakeTimeProvider();
        var usage = Store(time);
        var apps = new FakeAppsFolder((CalcAumid, "Hesap Makinesi"), ("Claude_pzs8sxrjxfjjc!Claude", "Claude"));
        var plugin = await ReadyAsync(["Zeta", "Alpha"], usage, apps);
        var zeta = plugin.Entries.Single(e => e.Name == "Zeta");

        for (var i = 0; i < 3; i++)
        {
            usage.RecordLaunch(zeta.Id);
        }

        usage.RecordLaunch("app:" + CalcAumid);
        usage.RecordLaunch("cmd:notepad.exe");   // Command yedeği de kayıttan bulunur

        var results = await Query(plugin, string.Empty);

        Assert.Equal(["Zeta", "Hesap Makinesi", "Not Defteri", "Dosya Gezgini", "Ayarlar", "Komut İstemi"], results.Select(r => r.Title));
        Assert.Equal(results.Count, results.Select(r => r.Title).Distinct().Count());
    }

    [Fact]
    public async Task EmptyQuery_DeletedIdInUsage_IsSkipped()
    {
        var usage = Store();
        for (var i = 0; i < 5; i++)
        {
            usage.RecordLaunch(@"lnk:C:\Start\Silindi.lnk");
        }

        usage.RecordLaunch("app:Gone_x!App");
        var plugin = await ReadyAsync(["Alpha"], usage);

        var results = await Query(plugin, string.Empty);

        Assert.Equal(["Dosya Gezgini", "Ayarlar", "Komut İstemi", "Not Defteri", "Hesap Makinesi"], results.Select(r => r.Title));
    }

    [Fact]
    public async Task EmptyQuery_ManyUsed_CappedAtSix_EvenWithLargerMaxResults()
    {
        var usage = Store();
        var names = Enumerable.Range(1, 9).Select(i => "App" + i).ToArray();
        var plugin = await ReadyAsync(names, usage, settings: new Settings { MaxResults = 20 });
        foreach (var e in plugin.Entries)
        {
            usage.RecordLaunch(e.Id);
        }

        Assert.Equal(6, (await Query(plugin, string.Empty)).Count);
        Assert.Equal(9, (await Query(plugin, "app")).Count);
    }

    [Fact]
    public async Task EmptyQuery_MaxResultsBelowSix_UsesMaxResults()
    {
        var plugin = await ReadyAsync(["Alpha"], Store(), settings: new Settings { MaxResults = 3 });

        Assert.Equal(["Dosya Gezgini", "Ayarlar", "Komut İstemi"], (await Query(plugin, "  ")).Select(r => r.Title));
    }

    [Fact]
    public async Task EmptyQuery_CommandStarter_LaunchesCommand_AndRecordsCmdId()
    {
        var usage = Store();
        string? launched = null;
        var plugin = await ReadyAsync(["Alpha"], usage, launch: p => launched = p);

        var notepad = (await Query(plugin, string.Empty)).Single(r => r.Title == "Not Defteri");

        Assert.True(await notepad.Action!());
        Assert.Equal("notepad.exe", launched);
        Assert.Equal(["cmd:notepad.exe"], usage.TopIds(5));
        Assert.Equal("Not Defteri", (await Query(plugin, string.Empty))[0].Title);
    }

    // ---------------------------------------------------------------- Arama sıralaması

    [Fact]
    public async Task Search_EqualMatchScore_HigherFrecencyWins()
    {
        var a = Path.Combine(_dir, "a");
        var b = Path.Combine(_dir, "b");
        MakeLnk(a, "Tool");
        MakeLnk(b, "Tool");
        var calls = 0;
        var usage = Store();
        var plugin = Plugin(
            new ProgramScanner([a, b], _ => @"C:\t" + Interlocked.Increment(ref calls) + ".exe", displayName: FileNameOnly), usage);
        await plugin.InitAsync(new PluginContext(_dir));
        await plugin.RefreshTask;

        var before = await Query(plugin, "tool");
        Assert.Equal(before[0].Score, before[1].Score);
        var second = plugin.Entries.Single(e => e.TargetPath == before[1].SubTitle);
        usage.RecordLaunch(second.Id);

        var after = await Query(plugin, "tool");

        Assert.Equal(before[1].SubTitle, after[0].SubTitle);
        Assert.Equal(after[1].Score + 10, after[0].Score);
    }

    [Fact]
    public async Task Search_UsageBonus_IsCappedAtFifty()
    {
        var usage = Store();
        var plugin = await ReadyAsync(["Notepad"], usage);
        var id = plugin.Entries.Single().Id;
        for (var i = 0; i < 100; i++)
        {
            usage.RecordLaunch(id);
        }

        var r = Assert.Single(await Query(plugin, "note"));

        Assert.Equal(50, plugin.UsageBonus(id));
        Assert.Equal(StringMatcher.Match("note", "Notepad").Score + 50, r.Score);
        Assert.Equal(0, plugin.UsageBonus("lnk:yok"));
    }

    [Fact]
    public async Task Search_Bonus_RoundsFrecencyTimesTen()
    {
        var time = new FakeTimeProvider();
        var usage = Store(time);
        var plugin = await ReadyAsync(["Notepad"], usage);
        var id = plugin.Entries.Single().Id;
        usage.RecordLaunch(id);
        time.Advance(TimeSpan.FromDays(10));   // 1 x 0.5

        Assert.Equal(5, plugin.UsageBonus(id));
    }

    // ---------------------------------------------------------------- Gerçek makine (Windows kabuğu)

    [Fact]
    public async Task Real_AppsFolder_EnumeratesItems_OnMtaAndSta()
    {
        // Tarama doğrudan MTA'da (yedeğe düşmeden) çalışmalı; STA yedeği de aynı sonucu vermeli.
        var mta = await Task.Run(() =>
        {
            Assert.Equal(ApartmentState.MTA, Thread.CurrentThread.GetApartmentState());
            return AppsFolderScanner.EnumerateCore(CancellationToken.None);
        });
        var sta = await StaRunner.Run(() => AppsFolderScanner.EnumerateCore(CancellationToken.None));

        Assert.NotEmpty(mta);
        Assert.Equal(mta.Count, sta.Count);
        Assert.All(mta, i => Assert.False(string.IsNullOrWhiteSpace(i.ParsingName)));
    }

    [Fact]
    public void Real_AppsFolder_HonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new AppsFolderScanner().Enumerate(cts.Token));
    }

    [Fact]
    public void Real_PackagedIcon_LoadsFrozen_AndCaches()
    {
        var packaged = new AppsFolderScanner().Enumerate(CancellationToken.None)
            .Select(i => i.ParsingName)
            .FirstOrDefault(ProgramCatalog.IsPackaged);
        if (packaged is null)
        {
            return;   // paketli uygulama yok (ör. Server Core): atlanır
        }

        var loader = new IconLoader();
        var first = loader.GetForParsingName(packaged);
        var second = loader.GetForParsingName(packaged);

        var bitmap = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(first);
        Assert.True(bitmap.IsFrozen);
        Assert.Same(first, second);
        Assert.Equal(32, bitmap.PixelWidth);
        Assert.Null(loader.GetForParsingName("Yok.Boyle_bir!Uygulama"));
    }

    [Fact]
    public void Real_DefaultBrowser_IsNullOrExistingExe()
    {
        var path = DefaultBrowser.GetExecutablePath();

        if (path is not null)
        {
            Assert.True(File.Exists(path), path);
            Assert.EndsWith(".exe", path, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Real_ShellDisplayName_AppliesDesktopIniLocalization()
    {
        var folder = Path.Combine(_dir, "loc");
        Directory.CreateDirectory(folder);
        var lnk = MakeLnk(folder, "Original");
        File.WriteAllText(Path.Combine(folder, "desktop.ini"), "[LocalizedFileNames]\r\nOriginal.lnk=Çevrilmiş Ad\r\n", System.Text.Encoding.Unicode);
        File.SetAttributes(Path.Combine(folder, "desktop.ini"), FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(folder, FileAttributes.ReadOnly);   // kabuk desktop.ini'yi yalnızca ReadOnly/System klasörde okur
        try
        {
            Assert.Equal("Çevrilmiş Ad", ShellDisplayName.Get(lnk));
        }
        finally
        {
            File.SetAttributes(folder, FileAttributes.Normal);
            File.SetAttributes(Path.Combine(folder, "desktop.ini"), FileAttributes.Normal);
        }
    }
}
