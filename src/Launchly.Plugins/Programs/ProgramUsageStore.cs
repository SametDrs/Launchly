using System.Collections.Immutable;
using Launchly.Core;
using Launchly.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.Programs;

/// <summary>Tek uygulamanın kullanım kaydı. Değişmez: güncelleme yeni nesne üretir (okuyucular kilitsiz okur).</summary>
public sealed class UsageEntry
{
    public int Count { get; init; }

    public DateTime LastUsedUtc { get; init; }
}

/// <summary>Gizlenen uygulama. Ad, uygulama kaldırılsa da ayarlarda gösterilebilsin diye saklanır.</summary>
public sealed class HiddenProgram
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}

/// <summary><c>usage.json</c> içeriği. <see cref="Hidden"/> ve <see cref="Pinned"/> eski dosyalarda yoktur: boş liste olur.</summary>
public sealed class UsageData
{
    public int Version { get; set; } = 1;

    /// <summary>Anahtar: <see cref="ProgramEntry.Id"/> (<see cref="StringComparer.OrdinalIgnoreCase"/>).</summary>
    public Dictionary<string, UsageEntry> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gizlenen uygulamalar (gizlenme sırasıyla); boş listede ve aramada görünmez.</summary>
    public List<HiddenProgram> Hidden { get; set; } = [];

    /// <summary>Sabitlenen uygulamaların kimlikleri, sabitlenme sırasıyla; boş listede en üstte.</summary>
    public List<string> Pinned { get; set; } = [];
}

/// <summary>
/// Uygulama açılışlarını sayar ve "frecency" (sıklık x yakınlık) puanı verir. <c>%AppData%\Launchly\usage.json</c>.
/// <para>
/// Eşzamanlılık: okuma (<see cref="Frecency"/>, <see cref="TopIds"/>; eklenti iş parçacığı) kilitsizdir ve değişmez bir anlık
/// görüntüyü okur. <see cref="RecordLaunch"/> (UI iş parçacığı) yalnızca belleği günceller; disk yazımı ~1 sn debounce ile
/// iş parçacığı havuzunda tek yazıma birleştirilir. İki yazım aynı anda dosyaya erişmez (ayrı yazım kilidi).
/// </para>
/// <para>
/// Gizleme ve sabitleme (<see cref="Hide"/>, <see cref="Pin"/>; menüden, UI iş parçacığında) belleği anında günceller,
/// diske aynı debounce yolundan yazılır. Okuyucular (<see cref="HiddenIds"/>, <see cref="PinnedIds"/>) kilitsiz okur.
/// Kullanım sayacı gizlemeyle silinmez: gizleme geri alınınca sıralama eski haline döner.
/// </para>
/// </summary>
public sealed class ProgramUsageStore : IDisposable
{
    /// <summary>Kayıttan sonra diske yazmadan önce beklenen süre (art arda açılışlar tek yazımda birleşir).</summary>
    public static readonly TimeSpan DefaultWriteDelay = TimeSpan.FromSeconds(1);

    /// <summary><see cref="Flush"/>, sürmekte olan bir yazımı en çok bu kadar bekler (kapanış takılmasın).</summary>
    private const int FlushTimeoutMs = 2000;

    private readonly JsonStorage<UsageData> _storage;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TimeSpan _writeDelay;
    private readonly object _gate = new();        // bellek durumu: _items, _dirty, _writeScheduled, _disposed
    private readonly object _writeGate = new();   // disk: aynı anda tek yazım
    private volatile ImmutableDictionary<string, UsageEntry> _items;
    private volatile Prefs _prefs;                 // gizli + sabit; değişmez, tek nesne olarak değiştirilir
    private bool _dirty;
    private bool _writeScheduled;
    private bool _disposed;

    /// <summary>DI için: <c>%AppData%\Launchly\usage.json</c>, sistem saati.</summary>
    public ProgramUsageStore(ILogger<ProgramUsageStore> logger)
        : this(Path.Combine(DataLocation.RoamingDirectory, "usage.json"), TimeProvider.System, logger)
    {
    }

    internal ProgramUsageStore(string filePath, TimeProvider? time = null, ILogger? logger = null, TimeSpan? writeDelay = null)
    {
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _writeDelay = writeDelay ?? DefaultWriteDelay;
        _storage = new JsonStorage<UsageData>(filePath, _logger);
        (_items, _prefs) = Load();
    }

    /// <summary>
    /// Gizleme ya da sabitleme değişti (çağıranın iş parçacığında, kilit dışında). Ayar penceresi gibi abonelerin
    /// UI iş parçacığına kendisi taşıması gerekir.
    /// </summary>
    public event Action? Changed;

    internal string FilePath => _storage.FilePath;

    /// <summary>Gizlenen kimlikler (anlık görüntü, <see cref="StringComparer.OrdinalIgnoreCase"/>).</summary>
    public IReadOnlySet<string> HiddenIds => _prefs.HiddenIds;

    /// <summary>Gizlenen uygulamalar, gizlenme sırasıyla (anlık görüntü).</summary>
    public IReadOnlyList<HiddenProgram> Hidden => _prefs.Hidden;

    /// <summary>Sabitlenen kimlikler, sabitlenme sırasıyla (anlık görüntü).</summary>
    public IReadOnlyList<string> PinnedIds => _prefs.Pinned;

    public bool IsHidden(string id) => !string.IsNullOrEmpty(id) && _prefs.HiddenIds.Contains(id);

    public bool IsPinned(string id) => !string.IsNullOrEmpty(id) && _prefs.PinnedSet.Contains(id);

    /// <summary>Girdiyi gizler (zaten gizliyse ad güncellenmez). Kullanım sayacı ve sabitleme korunur. Değiştiyse true.</summary>
    public bool Hide(string id, string name) => Update(id, p =>
        p.HiddenIds.Contains(id) ? null : p with
        {
            Hidden = p.Hidden.Add(new HiddenProgram { Id = id, Name = name ?? string.Empty }),
            HiddenIds = p.HiddenIds.Add(id),
        });

    /// <summary>Gizlemeyi geri alır. Değiştiyse true.</summary>
    public bool Unhide(string id) => Update(id, p =>
        !p.HiddenIds.Contains(id) ? null : p with
        {
            Hidden = p.Hidden.RemoveAll(h => string.Equals(h.Id, id, StringComparison.OrdinalIgnoreCase)),
            HiddenIds = p.HiddenIds.Remove(id),
        });

    /// <summary>Girdiyi sabitler listesinin sonuna ekler. Değiştiyse true.</summary>
    public bool Pin(string id) => Update(id, p =>
        p.PinnedSet.Contains(id) ? null : p with { Pinned = p.Pinned.Add(id), PinnedSet = p.PinnedSet.Add(id) });

    /// <summary>Sabitlemeyi kaldırır; diğer sabitlerin sırası korunur. Değiştiyse true.</summary>
    public bool Unpin(string id) => Update(id, p =>
        !p.PinnedSet.Contains(id) ? null : p with
        {
            Pinned = p.Pinned.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)),
            PinnedSet = p.PinnedSet.Remove(id),
        });

    /// <summary>Başarılı bir açılışı kaydeder: sayaç +1, son kullanım şimdi. Dispose sonrası yok sayılır. Diske beklemez.</summary>
    public void RecordLaunch(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        bool schedule;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var count = _items.TryGetValue(id, out var old) ? old.Count : 0;
            _items = _items.SetItem(id, new UsageEntry { Count = count == int.MaxValue ? count : count + 1, LastUsedUtc = now });
            schedule = MarkDirtyLocked();
        }

        ScheduleWrite(schedule);
    }

    // Gizli/sabit kümesini değiştirir. change null dönerse değişiklik yok. Dispose sonrası yok sayılır.
    private bool Update(string id, Func<Prefs, Prefs?> change)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        bool schedule;
        lock (_gate)
        {
            if (_disposed || change(_prefs) is not { } next)
            {
                return false;
            }

            _prefs = next;
            schedule = MarkDirtyLocked();
        }

        ScheduleWrite(schedule);
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            // Abone hatası menü eylemini bozmaz; bellek ve disk güncellemesi yapıldı.
            SafeLog(l => l.LogWarning(ex, "Kullanım kaydı Changed abonesi hata verdi"));
        }

        return true;
    }

    // _gate altında: kirli işaretler; yeni yazım planlanması gerekiyorsa true.
    private bool MarkDirtyLocked()
    {
        _dirty = true;
        var schedule = !_writeScheduled;
        _writeScheduled = true;
        return schedule;
    }

    private void ScheduleWrite(bool schedule)
    {
        if (schedule)
        {
            // Task.Run: gecikme 0 olsa bile yazım çağıranın (UI) iş parçacığında yapılmaz.
            _ = Task.Run(WriteLaterAsync);
        }
    }

    /// <summary>Kullanım puanı: <c>Count x ağırlık(son kullanım)</c>; kayıt yoksa 0.</summary>
    public double Frecency(string id)
    {
        if (string.IsNullOrEmpty(id) || !_items.TryGetValue(id, out var entry))
        {
            return 0;
        }

        return Score(entry, _time.GetUtcNow().UtcDateTime);
    }

    /// <summary>Kayıtlı kimlikler, puana göre azalan (eşitlikte son kullanılan önce) en çok <paramref name="count"/> tane.</summary>
    public IReadOnlyList<string> TopIds(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var items = _items;
        var now = _time.GetUtcNow().UtcDateTime;
        return items
            .Select(kv => (Id: kv.Key, Score: Score(kv.Value, now), kv.Value.LastUsedUtc))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.LastUsedUtc)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .Select(x => x.Id)
            .ToList();
    }

    /// <summary>
    /// Bekleyen değişikliği hemen (çağıran iş parçacığında) diske yazar. Sürmekte olan bir yazım en çok ~2 sn beklenir;
    /// süre dolarsa bu çağrı yazmadan döner (sürmekte olan yazım kendi anlık görüntüsünü yazar).
    /// </summary>
    public void Flush() => WriteIfDirty(FlushTimeoutMs);

    /// <summary>Bekleyen yazımı bir kez bitirir; sonra gelen <see cref="RecordLaunch"/> yok sayılır. İdempotent.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Flush();
    }

    /// <summary>Yakınlık ağırlığı: 1 gün 1.0, 7 gün 0.7, 30 gün 0.5, 90 gün 0.3, daha eski 0.1 (sınır değer dahil).</summary>
    internal static double Weight(TimeSpan age)
    {
        if (age <= TimeSpan.FromDays(1))
        {
            return 1.0;   // gelecekteki tarih (saat geri alındı) de "yeni" sayılır
        }

        if (age <= TimeSpan.FromDays(7))
        {
            return 0.7;
        }

        if (age <= TimeSpan.FromDays(30))
        {
            return 0.5;
        }

        return age <= TimeSpan.FromDays(90) ? 0.3 : 0.1;
    }

    private static double Score(UsageEntry entry, DateTime nowUtc) => entry.Count * Weight(nowUtc - entry.LastUsedUtc);

    private async Task WriteLaterAsync()
    {
        try
        {
            if (_writeDelay > TimeSpan.Zero)
            {
                await Task.Delay(_writeDelay, _time).ConfigureAwait(false);
            }
        }
        finally
        {
            // Bayrak yazımdan önce iner: yazım sürerken gelen kayıt yeni bir yazım planlar (kaybolmaz).
            lock (_gate)
            {
                _writeScheduled = false;
            }
        }

        WriteIfDirty(Timeout.Infinite);
    }

    private void WriteIfDirty(int timeoutMs)
    {
        if (!Monitor.TryEnter(_writeGate, timeoutMs))
        {
            SafeLog(l => l.LogWarning("Kullanım kaydı yazımı zaman aşımı: sürmekte olan yazım beklenmedi"));
            return;
        }

        try
        {
            ImmutableDictionary<string, UsageEntry> snapshot;
            Prefs prefs;
            lock (_gate)
            {
                if (!_dirty)
                {
                    return;
                }

                _dirty = false;
                snapshot = _items;
                prefs = _prefs;
            }

            try
            {
                _storage.Save(new UsageData
                {
                    Items = new Dictionary<string, UsageEntry>(snapshot, StringComparer.OrdinalIgnoreCase),
                    Hidden = [.. prefs.Hidden],
                    Pinned = [.. prefs.Pinned],
                });
            }
            catch (Exception ex)
            {
                // Sonraki kayıtta ya da Flush'ta yeniden denenir.
                lock (_gate)
                {
                    _dirty = true;
                }

                SafeLog(l => l.LogWarning(ex, "Kullanım kaydı yazılamadı: {File}", _storage.FilePath));
            }
        }
        finally
        {
            Monitor.Exit(_writeGate);
        }
    }

    private (ImmutableDictionary<string, UsageEntry> Items, Prefs Prefs) Load()
    {
        UsageData? data;
        try
        {
            data = _storage.Load();
        }
        catch (Exception ex)
        {
            SafeLog(l => l.LogWarning(ex, "Kullanım kaydı yüklenemedi; boş kayıtla başlanıyor"));
            return (ImmutableDictionary.Create<string, UsageEntry>(StringComparer.OrdinalIgnoreCase), Prefs.Empty);
        }

        return (LoadItems(data), LoadPrefs(data));
    }

    // Eski dosyada alan yoksa boş liste; null, boş kimlik ve büyük/küçük harf ikizleri (ilk görülen kalır) atılır.
    private static Prefs LoadPrefs(UsageData? data)
    {
        var prefs = Prefs.Empty;
        foreach (var h in data?.Hidden ?? [])
        {
            if (h is null || string.IsNullOrWhiteSpace(h.Id) || prefs.HiddenIds.Contains(h.Id))
            {
                continue;
            }

            prefs = prefs with
            {
                Hidden = prefs.Hidden.Add(new HiddenProgram { Id = h.Id, Name = h.Name ?? string.Empty }),
                HiddenIds = prefs.HiddenIds.Add(h.Id),
            };
        }

        foreach (var id in data?.Pinned ?? [])
        {
            if (!string.IsNullOrWhiteSpace(id) && !prefs.PinnedSet.Contains(id))
            {
                prefs = prefs with { Pinned = prefs.Pinned.Add(id), PinnedSet = prefs.PinnedSet.Add(id) };
            }
        }

        return prefs;
    }

    private ImmutableDictionary<string, UsageEntry> LoadItems(UsageData? data)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, UsageEntry>(StringComparer.OrdinalIgnoreCase);

        // Elle düzenlenmiş/bozuk içerik: null ve geçersiz öğeler atılır; yalnızca büyük/küçük harfi farklı anahtarlar birleşir.
        foreach (var (key, value) in data?.Items ?? [])
        {
            if (string.IsNullOrWhiteSpace(key) || value is null || value.Count <= 0)
            {
                continue;
            }

            var lastUsed = value.LastUsedUtc.Kind switch
            {
                DateTimeKind.Utc => value.LastUsedUtc,
                DateTimeKind.Local => value.LastUsedUtc.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value.LastUsedUtc, DateTimeKind.Utc),
            };

            if (builder.TryGetKey(key, out var firstKey) && builder.TryGetValue(firstKey, out var existing))
            {
                // İlk görülen yazım korunur (indeksleyici anahtarı da değiştirirdi).
                var sum = (long)existing.Count + value.Count;
                builder.Remove(firstKey);
                builder[firstKey] = new UsageEntry
                {
                    Count = (int)Math.Min(int.MaxValue, sum),
                    LastUsedUtc = existing.LastUsedUtc > lastUsed ? existing.LastUsedUtc : lastUsed,
                };
            }
            else
            {
                builder[key] = new UsageEntry { Count = value.Count, LastUsedUtc = lastUsed };
            }
        }

        return builder.ToImmutable();
    }

    private void SafeLog(Action<ILogger> log)
    {
        try
        {
            log(_logger);
        }
        catch (Exception)
        {
            // Kapanışta log sağlayıcısı kapanmış olabilir; loglama hatası kaydı etkilemez.
        }
    }

    /// <summary>Gizli ve sabit kümeleri; değişmez, okuyucular tek nesneyi kilitsiz okur.</summary>
    private sealed record Prefs(
        ImmutableList<HiddenProgram> Hidden,
        ImmutableHashSet<string> HiddenIds,
        ImmutableList<string> Pinned,
        ImmutableHashSet<string> PinnedSet)
    {
        public static readonly Prefs Empty = new(
            [],
            ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
            [],
            ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase));
    }
}
