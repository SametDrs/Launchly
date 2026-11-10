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

    // Sahiplik modeli: aşağıdaki alanların hepsi YALNIZCA _gate altında okunur/yazılır. Arka plan (PluginManager)
    // bunlara doğrudan dokunmaz; yalnızca nesil + sıra damgalı mesaj gönderir (HandleResults, MarkUnfinished) ve
    // mesaj _gate altında değerlendirilir. Post sırasına (FIFO) güvenilmez: eski nesil ve sırası geçmiş yayın atılır.
    // Sorgu mesajlarının Results/SelectedIndex yazımı da _gate altındadır. _gate tutulurken PluginManager çağrılmaz
    // (PluginManager kendi kilidi altında yayın yapar; ters sıralı kilit oluşmasın).
    private readonly object _gate = new();
    private int _generation;                                  // her yeni sorgu/boş ekran bir nesil
    private GenerationState _state = GenerationState.Final;   // güncel neslin durumu
    private int _appliedSequence;                             // güncel nesilde uygulanan son yayının sırası
    private IReadOnlyList<Result>? _appliedSource;            // son uygulanan anlık görüntü (aynı nesne yeniden uygulanmaz)
    private PendingEnter? _pendingEnter;                      // sonucu bekleyen Enter (en çok bir tane)
    private bool _running;                                    // bir Enter'ın eylemi çalışıyor

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

    private enum GenerationState
    {
        Running,    // sorgu sürüyor; Enter son yayını bekler
        Final,      // son yayın uygulandı; Enter hemen çalışır
        Unfinished, // sorgu son yayın olmadan bitti (iptal/hata); Enter hiçbir şey çalıştırmaz
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

    /// <summary>Alt+1..9 hızlı seçim: 1 tabanlı sıradaki sonucu seçer (çalıştırmaz).</summary>
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
    /// Seçili sonucun eylemini çalıştırır. Güncel neslin sorgusu sürüyorsa önce son yayının (tüm eklentiler döndü)
    /// uygulanmasını bekler, sonra o anki seçili öğeyi çalıştırır. Beklerken Esc/gizleme
    /// (<see cref="CancelPendingExecute"/>), yeni sorgu ya da <see cref="Reset"/> gelirse hiçbir şey çalıştırmaz.
    /// Devam eden (bekleyen ya da çalışan) bir yürütme varken gelen Enter yok sayılır; iptal edilmiş bekleyen
    /// Enter'dan sonra basılan Enter ise hemen kabul edilir.
    /// Dönüş: pencere gizlenmeli mi (eylem true döndürdüyse). Seçim/eylem yoksa ya da eylem hata verirse false.
    /// </summary>
    public async Task<bool> ExecuteSelectedAsync()
    {
        PendingEnter? pending = null;
        Result? selected = null;
        lock (_gate)
        {
            if (_running || _pendingEnter is not null || _state == GenerationState.Unfinished)
            {
                return false;
            }

            if (_state == GenerationState.Final)
            {
                _running = true;
                selected = Selected?.Result;
            }
            else
            {
                pending = _pendingEnter = new PendingEnter(_generation);
            }
        }

        if (pending is not null)
        {
            await pending.Wake.Task;
            lock (_gate)
            {
                // Uyanma nedeni ne olursa olsun karar tek yerde: bu Enter hâlâ bekleyen mi, nesil aynı mı, son yayın
                // uygulandı mı. Uyanmayla buraya gelmek arasında gelen iptal de böylece kazanır. İptal edilen Enter
                // ortak duruma dokunmaz (o arada basılmış yeni bir Enter'ın kaydı silinmez).
                if (!ReferenceEquals(_pendingEnter, pending))
                {
                    return false;
                }

                _pendingEnter = null;
                if (_generation != pending.Generation || _state != GenerationState.Final)
                {
                    return false;
                }

                _running = true;
                selected = Selected?.Result;
            }
        }

        try
        {
            return await RunActionAsync(selected);
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
            }
        }
    }

    /// <summary>Bekleyen Enter'ı iptal eder (Esc, pencere gizleme). Çalışmaya başlamış eylem durdurulmaz.</summary>
    public void CancelPendingExecute()
    {
        lock (_gate)
        {
            CancelPendingEnterLocked();
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

    /// <summary>Pencere her açıldığında çağrılır: kutuyu ve listeyi temizler; bekleyen Enter iptal olur.</summary>
    public void Reset()
    {
        if (QueryText.Length == 0)
        {
            StartQuery(string.Empty);   // metin değişmeyince OnQueryTextChanged tetiklenmez
        }
        else
        {
            QueryText = string.Empty;   // OnQueryTextChanged -> StartQuery
        }
    }

    private ResultItemViewModel? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    partial void OnQueryTextChanged(string value) => StartQuery(value);

    // _gate altında: bekleyen Enter'ı bırakır ve uyandırır; uyanan Enter sahipliği kaybettiğini görüp hiçbir şey çalıştırmaz.
    // Wake devamları eşzamansız çalışır (RunContinuationsAsynchronously): kilit altında tamamlamak güvenli.
    private void CancelPendingEnterLocked()
    {
        var pending = _pendingEnter;
        _pendingEnter = null;
        pending?.Wake.TrySetResult();
    }

    private async Task<bool> RunActionAsync(Result? result)
    {
        if (result?.Action is not { } action)
        {
            return false;
        }

        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sonuç eylemi başarısız: {Title}", result.Title);
            return false;
        }
    }

    // Yeni nesil başlatır: önceki neslin bekleyen Enter'ı iptal edilir (sonraki nesle geçmez), eski mesajlar artık atılır.
    private void StartQuery(string text)
    {
        int generation;
        lock (_gate)
        {
            generation = ++_generation;
            _state = GenerationState.Running;
            _appliedSequence = 0;
            CancelPendingEnterLocked();
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            _ = RunQueryAsync(text, generation);
        }
        else if (_pluginManager.HasEmptyQueryPlugins)
        {
            // Kutu boş: boş sorguyu destekleyen eklenti (Programs) listesi. Eski liste yenisi gelene kadar tutulur (titreme yok).
            _ = RunQueryAsync(string.Empty, generation);
        }
        else
        {
            // Çalışan sorgu iptal edilir (yayını zaten bu nesilde atılırdı); liste hemen temizlenir.
            _pluginManager.CancelCurrent();
            HandleResults(generation, sequence: 1, [], isFinal: true);
        }
    }

    private async Task RunQueryAsync(string text, int generation)
    {
        // PluginManager bir sorgunun yayınlarını sırayla yapar (aynı anda tek): bu yerel değişkenlere sırayla yazılır;
        // await sonrası okuma, görevin tamamlanmasıyla son yayından sonraya sıralanır.
        var sequence = 0;
        var finalPublished = false;
        try
        {
            await _pluginManager.QueryAsync(text, (results, isFinal) =>
            {
                var seq = ++sequence;
                finalPublished |= isFinal;
                Post(() => HandleResults(generation, seq, results, isFinal));
            }).ConfigureAwait(false);   // devamı yalnızca _gate altındaki duruma dokunur; UI'a dönmesi gerekmez
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sorgu çalıştırılamadı: {Query}", text);
        }

        if (!finalPublished)
        {
            MarkUnfinished(generation);
        }
    }

    // Sorgu son yayın olmadan bitti (iptal/hata): bu nesil artık tamamlanmayacak, bekleyen Enter hiçbir şey çalıştırmaz.
    // Arka plandan doğrudan çağrılır; yalnızca _gate altındaki duruma dokunur. Nesil değiştiyse etkisizdir.
    private void MarkUnfinished(int generation)
    {
        lock (_gate)
        {
            if (generation == _generation && _state == GenerationState.Running)
            {
                _state = GenerationState.Unfinished;
                CancelPendingEnterLocked();
            }
        }
    }

    // Bir yayını uygular (UI bağlamında). Eski nesil ya da daha yeni bir yayından sonra gelen yayın atılır; böylece
    // Post sırası karışsa da liste son yayında kalır. Son yayın bekleyen Enter'ı uyandırır.
    private void HandleResults(int generation, int sequence, IReadOnlyList<Result> results, bool isFinal)
    {
        lock (_gate)
        {
            if (generation != _generation || sequence <= _appliedSequence)
            {
                return;
            }

            _appliedSequence = sequence;
            if (!ReferenceEquals(results, _appliedSource))
            {
                // Değişmemiş son yayın (aynı nesne) yeniden uygulanmaz: kullanıcının seçimi sıfırlanmaz.
                _appliedSource = results;
                Results = results.Select(r => new ResultItemViewModel(r)).ToList();
                SelectedIndex = results.Count > 0 ? 0 : -1;
            }

            if (isFinal)
            {
                _state = GenerationState.Final;
                _pendingEnter?.Wake.TrySetResult();
            }
        }
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

    /// <summary>Bekleyen Enter. <see cref="Wake"/> yalnızca uyandırır; çalıştırma kararı uyanınca _gate altında verilir.</summary>
    private sealed class PendingEnter(int generation)
    {
        public int Generation { get; } = generation;

        public TaskCompletionSource Wake { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
