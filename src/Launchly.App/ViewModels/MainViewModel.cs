using CommunityToolkit.Mvvm.ComponentModel;
using Launchly.Core.Localization;
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
    // Eylem menüsü durumu (IsActionMenuOpen, hedef, öğeler, seçili öğe) de yalnızca _gate altında yazılır.
    private readonly object _gate = new();
    private int _generation;                                  // her yeni sorgu/boş ekran bir nesil
    private GenerationState _state = GenerationState.Final;   // güncel neslin durumu
    private int _appliedSequence;                             // güncel nesilde uygulanan son yayının sırası
    private IReadOnlyList<Result>? _appliedSource;            // son uygulanan anlık görüntü (aynı nesne yeniden uygulanmaz)
    private PendingEnter? _pendingEnter;                      // sonucu bekleyen Enter/Ctrl+Shift+Enter (en çok bir tane)
    private bool _running;                                    // bir Enter'ın ya da menü eyleminin eylemi çalışıyor
    private Result? _menuTarget;                              // menünün hedefi: açıldığı andaki Result nesnesi (indeks değil)
    private IReadOnlyList<ResultAction> _menuActions = [];    // menü öğeleri ("Aç" + ContextActions), açılışta sabitlenir
    private int _selectedActionIndex = -1;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private int _selectedIndex = -1;

    /// <summary>Sonuç listesi tek seferde (yeni liste nesnesiyle) değiştirilir; arayüz bir kez yenilenir.</summary>
    [ObservableProperty]
    private IReadOnlyList<ResultItemViewModel> _results = [];

    /// <summary>Eylem menüsü açık mı (Ctrl+K / Actions). Açıkken sonuç listesi yerine <see cref="ActionItems"/> görünür.</summary>
    [ObservableProperty]
    private bool _isActionMenuOpen;

    /// <summary>Menü öğeleri; menü kapalıyken boş.</summary>
    [ObservableProperty]
    private IReadOnlyList<ActionItemViewModel> _actionItems = [];

    /// <summary>Menünün hedef sonucunun başlığı.</summary>
    [ObservableProperty]
    private string _actionMenuTitle = string.Empty;

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

    /// <summary>Menüde seçili eylemin sırası (menü kapalıyken -1). Arayüz bağlaması da _gate altında yazar.</summary>
    public int SelectedActionIndex
    {
        get
        {
            lock (_gate)
            {
                return _selectedActionIndex;
            }
        }

        set
        {
            lock (_gate)
            {
                SetProperty(ref _selectedActionIndex, value);
            }
        }
    }

    /// <summary>
    /// Seçili sonuç menü açmadan yönetici olarak çalıştırılabilir mi (<see cref="ResultAction.RunAsAdminId"/> eylemi var mı).
    /// Alt çubuktaki "Ctrl+Shift+Enter Yönetici" ipucu buna bağlıdır. Menü açıkken kısayol etkisiz olduğundan false.
    /// </summary>
    public bool CanRunSelectedAsAdmin => !IsActionMenuOpen && Selected?.Result is { } r && FindRunAsAdmin(r) is not null;

    /// <summary>Seçimi <paramref name="delta"/> kadar kaydırır; uçlarda başa/sona sarar. Menü açıkken menüde gezer.</summary>
    public void MoveSelection(int delta)
    {
        lock (_gate)
        {
            if (IsActionMenuOpen)
            {
                var count = ActionItems.Count;
                if (count > 0)
                {
                    var nextAction = (_selectedActionIndex + delta) % count;
                    SelectedActionIndex = nextAction < 0 ? nextAction + count : nextAction;
                }

                return;
            }
        }

        if (Results.Count == 0)
        {
            return;
        }

        var next = (SelectedIndex + delta) % Results.Count;
        SelectedIndex = next < 0 ? next + Results.Count : next;
    }

    /// <summary>Alt+1..9 hızlı seçim: 1 tabanlı sıradaki sonucu seçer (çalıştırmaz). Menü açıkken etkisiz.</summary>
    public bool SelectByNumber(int number)
    {
        var index = number - 1;
        if (IsActionMenuOpen || index < 0 || index >= Results.Count)
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
    /// Enter'dan sonra basılan Enter ise hemen kabul edilir. Menü açıkken menüde seçili eylemi çalıştırır.
    /// Dönüş: pencere gizlenmeli mi (eylem true döndürdüyse). Seçim/eylem yoksa ya da eylem hata verirse false.
    /// </summary>
    public Task<bool> ExecuteSelectedAsync()
    {
        lock (_gate)
        {
            if (IsActionMenuOpen)
            {
                return ExecuteActionAsync();
            }
        }

        return ExecuteSelectedCoreAsync(static r => r.Action);
    }

    /// <summary>
    /// Ctrl+Shift+Enter: menü açmadan seçili sonucun <see cref="ResultAction.RunAsAdminId"/> eylemini çalıştırır.
    /// Enter ile aynı bekleme/koruma yolunu kullanır. Sonuçta bu eylem yoksa hiçbir şey çalışmaz (normal açılışa
    /// düşülmez) ve false döner. Menü açıkken etkisizdir.
    /// </summary>
    public Task<bool> ExecuteSelectedAsAdminAsync()
    {
        lock (_gate)
        {
            if (IsActionMenuOpen)
            {
                return Task.FromResult(false);
            }
        }

        return ExecuteSelectedCoreAsync(static r => FindRunAsAdmin(r)?.Execute);
    }

    /// <summary>
    /// Ctrl+K / Actions: menüyü açar ya da kapatır. Seçili sonuç yoksa ya da sonucun hiç eylemi yoksa açılmaz.
    /// Açılınca bekleyen Enter iptal edilir; hedef o anki <see cref="Result"/> nesnesidir. Dönüş: menü açık mı.
    /// </summary>
    public bool ToggleActionMenu()
    {
        lock (_gate)
        {
            if (IsActionMenuOpen)
            {
                CloseActionMenuLocked();
                return false;
            }

            if (Selected?.Result is not { } target)
            {
                return false;
            }

            var actions = BuildMenu(target);
            if (actions.Count == 0)
            {
                return false;
            }

            CancelPendingEnterLocked();
            _menuTarget = target;
            _menuActions = actions;
            ActionItems = actions.Select(a => new ActionItemViewModel(a)).ToList();
            ActionMenuTitle = target.Title;
            SelectedActionIndex = 0;
            IsActionMenuOpen = true;
            return true;
        }
    }

    /// <summary>Menüyü kapatır (açık değilse etkisiz). Çalışmakta olan eylem durdurulmaz.</summary>
    public void CloseActionMenu()
    {
        lock (_gate)
        {
            CloseActionMenuLocked();
        }
    }

    /// <summary>Esc: menü açıksa yalnızca menüyü kapatır (false: pencere açık kalır); değilse bekleyen Enter'ı iptal eder (true: gizle).</summary>
    public bool HandleEscape()
    {
        lock (_gate)
        {
            if (IsActionMenuOpen)
            {
                CloseActionMenuLocked();
                return false;
            }

            CancelPendingEnterLocked();
            return true;
        }
    }

    /// <summary>
    /// Menüde seçili eylemi çalıştırır. Başlarken menü kapanır. Eylem true dönerse pencere gizlenmeli (dönüş true);
    /// false döner ve eylem <see cref="ResultAction.RequeryAfter"/> ise güncel sorgu yeni nesil olarak yeniden çalışır.
    /// Başka bir eylem/Enter çalışıyorsa ya da menü kapalıysa yok sayılır (çift tetikleme: Enter + tık).
    /// </summary>
    public async Task<bool> ExecuteActionAsync()
    {
        ResultAction action;
        string title;
        lock (_gate)
        {
            if (!IsActionMenuOpen || _running || _pendingEnter is not null)
            {
                return false;
            }

            var index = _selectedActionIndex;
            if (index < 0 || index >= _menuActions.Count)
            {
                return false;
            }

            action = _menuActions[index];
            title = _menuTarget?.Title ?? string.Empty;
            CloseActionMenuLocked();
            _running = true;
        }

        var hide = false;
        try
        {
            hide = await RunActionAsync(action.Execute, title);
            return hide;
        }
        finally
        {
            int generation = 0;
            string text = string.Empty;
            lock (_gate)
            {
                _running = false;
                if (!hide && action.RequeryAfter)
                {
                    generation = BeginGenerationLocked();
                    text = QueryText;
                }
            }

            if (generation != 0)
            {
                LaunchQuery(text, generation);   // kilit dışında: PluginManager _gate altında çağrılmaz
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

    /// <summary>Tab: seçili sonucun <see cref="Result.AutoComplete"/> metnini arama kutusuna yazar. Yazıldıysa true. Menü açıkken etkisiz.</summary>
    public bool AutoCompleteSelected()
    {
        if (IsActionMenuOpen)
        {
            return false;
        }

        var text = Selected?.Result.AutoComplete;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        QueryText = text;
        return true;
    }

    /// <summary>Pencere her açıldığında çağrılır: kutuyu ve listeyi temizler; bekleyen Enter iptal olur, menü kapanır.</summary>
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

    /// <summary>Menü öğeleri: <see cref="Result.Action"/> doluysa ilk öğe "Aç", ardından <see cref="Result.ContextActions"/>.</summary>
    public static IReadOnlyList<ResultAction> BuildMenu(Result result)
    {
        var list = new List<ResultAction>();
        if (result.Action is { } open)
        {
            list.Add(new ResultAction { Title = Loc.T("action.open"), Glyph = "", Execute = open });   // OpenInNewWindow
        }

        foreach (var action in result.ContextActions ?? [])
        {
            if (action is not null)
            {
                list.Add(action);
            }
        }

        return list;
    }

    private static ResultAction? FindRunAsAdmin(Result result) =>
        (result.ContextActions ?? []).FirstOrDefault(a => a is not null && a.Id == ResultAction.RunAsAdminId);

    private ResultItemViewModel? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    partial void OnQueryTextChanged(string value) => StartQuery(value);

    partial void OnSelectedIndexChanged(int value) => OnPropertyChanged(nameof(CanRunSelectedAsAdmin));

    partial void OnResultsChanged(IReadOnlyList<ResultItemViewModel> value) => OnPropertyChanged(nameof(CanRunSelectedAsAdmin));

    partial void OnIsActionMenuOpenChanged(bool value) => OnPropertyChanged(nameof(CanRunSelectedAsAdmin));

    /// <summary>
    /// Enter ve Ctrl+Shift+Enter'ın ortak yolu. <paramref name="pick"/> çalışacak eylemi o anki seçili sonuçtan seçer
    /// (karar anında, _gate altında). Bekleme kaydı aynı <see cref="PendingEnter"/> alanıdır; biri bekliyor ya da
    /// çalışıyorsa diğeri yok sayılır.
    /// </summary>
    private async Task<bool> ExecuteSelectedCoreAsync(Func<Result, Func<Task<bool>>?> pick)
    {
        PendingEnter? pending = null;
        Result? selected = null;
        lock (_gate)
        {
            if (_running || _pendingEnter is not null || _state == GenerationState.Unfinished || IsActionMenuOpen)
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
                pending = _pendingEnter = new PendingEnter(_generation, pick);
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
                if (_generation != pending.Generation || _state != GenerationState.Final || IsActionMenuOpen)
                {
                    return false;
                }

                _running = true;
                selected = Selected?.Result;
            }
        }

        try
        {
            return selected is null ? false : await RunActionAsync((pending?.Pick ?? pick)(selected), selected.Title);
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
            }
        }
    }

    // _gate altında: menüyü kapatır; hedef bırakılır. Açık değilse etkisiz.
    private void CloseActionMenuLocked()
    {
        if (!IsActionMenuOpen)
        {
            return;
        }

        IsActionMenuOpen = false;
        _menuTarget = null;
        _menuActions = [];
        ActionItems = [];
        ActionMenuTitle = string.Empty;
        SelectedActionIndex = -1;
    }

    // _gate altında: bekleyen Enter'ı bırakır ve uyandırır; uyanan Enter sahipliği kaybettiğini görüp hiçbir şey çalıştırmaz.
    // Wake devamları eşzamansız çalışır (RunContinuationsAsynchronously): kilit altında tamamlamak güvenli.
    private void CancelPendingEnterLocked()
    {
        var pending = _pendingEnter;
        _pendingEnter = null;
        pending?.Wake.TrySetResult();
    }

    private async Task<bool> RunActionAsync(Func<Task<bool>>? action, string title)
    {
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
            _logger.LogError(ex, "Sonuç eylemi başarısız: {Title}", title);
            return false;
        }
    }

    // Yeni nesil başlatır: önceki neslin bekleyen Enter'ı iptal edilir (sonraki nesle geçmez), eski mesajlar artık atılır.
    private void StartQuery(string text)
    {
        int generation;
        lock (_gate)
        {
            generation = BeginGenerationLocked();
        }

        LaunchQuery(text, generation);
    }

    // _gate altında: yeni nesil. Bekleyen Enter iptal olur, menü kapanır (yazmak normal aramaya döner).
    private int BeginGenerationLocked()
    {
        var generation = ++_generation;
        _state = GenerationState.Running;
        _appliedSequence = 0;
        CancelPendingEnterLocked();
        CloseActionMenuLocked();
        return generation;
    }

    // Kilit dışında çağrılır.
    private void LaunchQuery(string text, int generation)
    {
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
    // Post sırası karışsa da liste son yayında kalır. Son yayın bekleyen Enter'ı uyandırır. Menü açıksa kapanmaz:
    // hedef nesne başvurusu olduğundan liste değişse de menü aynı sonucu hedefler.
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
    private sealed class PendingEnter(int generation, Func<Result, Func<Task<bool>>?> pick)
    {
        public int Generation { get; } = generation;

        /// <summary>Uyanınca seçili sonuçtan çalışacak eylemi seçer (Enter: Action; Ctrl+Shift+Enter: yönetici eylemi).</summary>
        public Func<Result, Func<Task<bool>>?> Pick { get; } = pick;

        public TaskCompletionSource Wake { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
