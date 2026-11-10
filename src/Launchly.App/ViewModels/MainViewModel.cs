using CommunityToolkit.Mvvm.ComponentModel;
using Launchly.Core.Plugins;
using Launchly.Plugin;
using Microsoft.Extensions.Logging;

namespace Launchly.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly PluginManager _pluginManager;
    private readonly ILogger<MainViewModel> _logger;
    private readonly SynchronizationContext? _uiContext;
    private readonly object _generationGate = new();
    private int _generation;

    // Güncel neslin sonucu listeye uygulanınca (ya da nesil boşa çıkınca) tamamlanır; Enter bunu bekler.
    private TaskCompletionSource _applied = CompletedSignal();

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private int _selectedIndex = -1;

    /// <summary>Sonuç listesi tek seferde (yeni liste nesnesiyle) değiştirilir; arayüz bir kez yenilenir.</summary>
    [ObservableProperty]
    private IReadOnlyList<ResultItemViewModel> _results = [];

    /// <summary>Oluşturan iş parçacığının (UI) bağlamı yakalanır; sonuçlar oraya taşınır. Bağlam yoksa doğrudan uygulanır.</summary>
    public MainViewModel(PluginManager pluginManager, ILogger<MainViewModel> logger)
    {
        _pluginManager = pluginManager;
        _logger = logger;
        _uiContext = SynchronizationContext.Current;
    }

    /// <summary>Seçimi <paramref name="delta"/> kadar kaydırır; uçlarda başa/sona sarar.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var next = (SelectedIndex + delta) % Results.Count;
        SelectedIndex = next < 0 ? next + Results.Count : next;
    }

    /// <summary>Alt+1..9 hızlı seçim iskeleti: 1 tabanlı sıradaki sonucu seçer. Çalıştırma sonraki görevde.</summary>
    public bool SelectByNumber(int number)
    {
        var index = number - 1;
        if (index < 0 || index >= Results.Count)
        {
            return false;
        }

        SelectedIndex = index;
        return true;
    }

    /// <summary>
    /// Seçili sonucun eylemini çalıştırır. Dönüş: pencere gizlenmeli mi (eylem true döndürdüyse).
    /// Seçim yoksa, eylem tanımsızsa ya da eylem hata verirse false.
    /// </summary>
    public async Task<bool> ExecuteSelectedAsync()
    {
        // Son sorgunun sonuçları henüz gelmediyse eski listedeki öğe çalıştırılmaz; sonuç gelene kadar beklenir.
        // Bekleme sırasında yeni sorgu başlarsa (sinyal tamamlanır) güncel sinyal yeniden beklenir.
        while (true)
        {
            Task pending;
            lock (_generationGate)
            {
                pending = _applied.Task;
            }

            if (pending.IsCompleted)
            {
                break;
            }

            await pending;
        }

        var action = Selected?.Result.Action;
        if (action is null)
        {
            return false;
        }

        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sonuç eylemi başarısız: {Title}", Selected?.Title);
            return false;
        }
    }

    /// <summary>Tab: seçili sonucun <see cref="Result.AutoComplete"/> metnini arama kutusuna yazar. Yazıldıysa true.</summary>
    public bool AutoCompleteSelected()
    {
        var text = Selected?.Result.AutoComplete;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        QueryText = text;
        return true;
    }

    /// <summary>Pencere her açıldığında çağrılır: kutuyu ve listeyi temizler.</summary>
    public void Reset()
    {
        QueryText = string.Empty;
        ClearResults();
    }

    private ResultItemViewModel? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    partial void OnQueryTextChanged(string value) => StartQuery(value);

    private static TaskCompletionSource CompletedSignal()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }

    // Yeni nesil başlatır; önceki nesli bekleyenler (Enter) uyandırılır ve yeni nesli beklemeye geçer.
    private int BeginGeneration()
    {
        TaskCompletionSource old;
        int generation;
        lock (_generationGate)
        {
            old = _applied;
            _applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            generation = Interlocked.Increment(ref _generation);
        }

        old.TrySetResult();
        return generation;
    }

    private void CompleteGeneration(int generation)
    {
        lock (_generationGate)
        {
            if (generation == _generation)
            {
                _applied.TrySetResult();
            }
        }
    }

    private void StartQuery(string text)
    {
        var generation = BeginGeneration();

        if (string.IsNullOrWhiteSpace(text))
        {
            ShowHome(generation);
            return;
        }

        _ = RunQueryAsync(text, generation);
    }

    private void ClearResults() => ShowHome(BeginGeneration());

    // Kutu boş: boş sorguyu destekleyen eklenti (Programs) varsa onun listesi gösterilir, yoksa liste temizlenir.
    // Eski liste, yeni liste gelene kadar tutulur (titreme yok).
    private void ShowHome(int generation)
    {
        _pluginManager.CancelCurrent();
        if (_pluginManager.HasEmptyQueryPlugins)
        {
            _ = RunQueryAsync(string.Empty, generation);
        }
        else
        {
            ApplyResults([]);
            CompleteGeneration(generation);
        }
    }

    private async Task RunQueryAsync(string text, int generation)
    {
        try
        {
            await _pluginManager.QueryAsync(text, results => PostResults(generation, results));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sorgu çalıştırılamadı: {Query}", text);
        }
        finally
        {
            // Yayın yapılmadan bitmiş (iptal/hata) olsa da Enter'ı bekletme; Post sırası yayından sonradır.
            Post(() => CompleteGeneration(generation));
        }
    }

    // Arka plan iş parçacığından çağrılır. Bir sonraki sorgu başlamışsa (nesil değişmişse) sonuç atılır:
    // Post ile UI kuyruğuna girmiş eski bir yayın bile ekrana düşmez.
    private void PostResults(int generation, IReadOnlyList<Result> results)
    {
        Post(() =>
        {
            if (generation == Volatile.Read(ref _generation))
            {
                ApplyResults(results);
                CompleteGeneration(generation);
            }
        });
    }

    private void Post(Action action)
    {
        if (_uiContext is null)
        {
            action();
        }
        else
        {
            _uiContext.Post(_ => action(), null);
        }
    }

    private void ApplyResults(IReadOnlyList<Result> results)
    {
        Results = results.Select(r => new ResultItemViewModel(r)).ToList();
        SelectedIndex = results.Count > 0 ? 0 : -1;
    }
}
