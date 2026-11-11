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

/// <summary><c>usage.json</c> içeriği.</summary>
public sealed class UsageData
{
    public int Version { get; set; } = 1;

    /// <summary>Anahtar: <see cref="ProgramEntry.Id"/> (<see cref="StringComparer.OrdinalIgnoreCase"/>).</summary>
    public Dictionary<string, UsageEntry> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Uygulama açılışlarını sayar ve "frecency" (sıklık x yakınlık) puanı verir. <c>%AppData%\Launchly\usage.json</c>.
/// <para>
/// Eşzamanlılık: okuma (<see cref="Frecency"/>, <see cref="TopIds"/>; eklenti iş parçacığı) kilitsizdir ve değişmez bir anlık
/// görüntüyü okur. <see cref="RecordLaunch"/> (UI iş parçacığı) yalnızca belleği günceller; disk yazımı ~1 sn debounce ile
/// iş parçacığı havuzunda tek yazıma birleştirilir. İki yazım aynı anda dosyaya erişmez (ayrı yazım kilidi).
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
        _items = LoadItems();
    }

    internal string FilePath => _storage.FilePath;

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
            _dirty = true;
            schedule = !_writeScheduled;
            _writeScheduled = true;
        }

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
            lock (_gate)
            {
                if (!_dirty)
                {
                    return;
                }

                _dirty = false;
                snapshot = _items;
            }

            try
            {
                _storage.Save(new UsageData { Items = new Dictionary<string, UsageEntry>(snapshot, StringComparer.OrdinalIgnoreCase) });
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

    private ImmutableDictionary<string, UsageEntry> LoadItems()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, UsageEntry>(StringComparer.OrdinalIgnoreCase);
        UsageData? data;
        try
        {
            data = _storage.Load();
        }
        catch (Exception ex)
        {
            SafeLog(l => l.LogWarning(ex, "Kullanım kaydı yüklenemedi; boş kayıtla başlanıyor"));
            return builder.ToImmutable();
        }

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
}
