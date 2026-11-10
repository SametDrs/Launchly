using Launchly.Core;
using Launchly.Core.Storage;

namespace Launchly.Tests;

public sealed class JsonStorageTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public JsonStorageTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "launchly-tests", Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_dir, "sub", "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaultsAndCreatesFile()
    {
        var storage = new JsonStorage<Settings>(_path);

        var settings = storage.Load();

        Assert.Equal(new Settings().Hotkey, settings.Hotkey);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var storage = new JsonStorage<Settings>(_path);
        storage.Save(new Settings { Hotkey = "Ctrl+Space", MaxResults = 12, StartOnSystemStartup = true });

        var loaded = new JsonStorage<Settings>(_path).Load();

        Assert.Equal("Ctrl+Space", loaded.Hotkey);
        Assert.Equal(12, loaded.MaxResults);
        Assert.True(loaded.StartOnSystemStartup);
    }

    [Fact]
    public void Save_KeepsPreviousVersionAsBackup()
    {
        var storage = new JsonStorage<Settings>(_path);
        storage.Save(new Settings { Hotkey = "First" });
        storage.Save(new Settings { Hotkey = "Second" });

        Assert.True(File.Exists(storage.BackupPath));
        Assert.Contains("First", File.ReadAllText(storage.BackupPath));
        Assert.Contains("Second", File.ReadAllText(_path));
    }

    [Fact]
    public void Load_WhenFileCorrupt_LoadsFromBackup()
    {
        var storage = new JsonStorage<Settings>(_path);
        storage.Save(new Settings { Hotkey = "Good" });
        storage.Save(new Settings { Hotkey = "Newer" }); // "Good" artık .bak içinde
        File.WriteAllText(_path, "{ bozuk json");

        var loaded = new JsonStorage<Settings>(_path).Load();

        Assert.Equal("Good", loaded.Hotkey);
        Assert.True(File.Exists(storage.CorruptPath));
        Assert.Contains("Good", File.ReadAllText(storage.BackupPath)); // yedek ezilmedi
        Assert.Contains("Good", File.ReadAllText(_path));              // asıl dosya onarıldı
    }

    [Fact]
    public void Load_WhenFileAndBackupCorrupt_ReturnsDefaults()
    {
        var storage = new JsonStorage<Settings>(_path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "not json");
        File.WriteAllText(storage.BackupPath, "null");

        var loaded = storage.Load();

        Assert.Equal(new Settings().Hotkey, loaded.Hotkey);
        Assert.True(File.Exists(_path));
    }
}
