# 09 – Actions menüsü

## Bağımlılık
`08-program-kesfi-kullanim.md`. 09 şunları kullanır: `ProgramEntry.Id`, `Kind`, `LaunchPath` ve `ProgramUsageStore`. `10` ve `11`'den önce yapılır.

## Amaç
Seçili sonuç için bir eylem menüsü sunmak. Menü `Ctrl+K` ile ya da alt çubuktaki `Actions` düğmesiyle açılır. Eylemler:
- Aç
- Yönetici olarak çalıştır
- Dosya konumunu aç
- Yolu kopyala
- Gizle
- Sabitle

Eklentiler eylemlerini SDK üzerinden tanımlar. `Ctrl+Shift+Enter`, menüyü açmadan seçili sonucu yönetici olarak çalıştırır.

## Yapılacaklar

### 1. SDK: `src/Launchly.Plugin/`
- Yeni `ResultAction.cs` sınıfı:
  - `required string Title`
  - `string Glyph` (Segoe Fluent Icons karakteri, boş olabilir)
  - `string? Hint` (yalnızca gösterim, ör. `"Ctrl+Shift+Enter"`)
  - `bool RequeryAfter` (eylemden sonra güncel sorgu yeniden çalışır)
  - `required Func<Task<bool>> Execute`: true dönerse pencere gizlenir. Kural `Result.Action` ile aynıdır.
- `Result`'a `IReadOnlyList<ResultAction> ContextActions { get; init; } = [];` eklenir. Mevcut eklentiler değişmeden derlenir.
- **"Aç" eylemi otomatik eklenir.** Menü şu şekilde kurulur: `Result.Action` null değilse ilk öğe "Aç" olur ve `Result.Action`'ı çağırır; ardından `ContextActions` gelir. Eklentiler "Aç"ı kendi listelerine koymaz.

### 2. Arayüz: `MainViewModel` ve `MainWindow`
- `MainViewModel`'e eklenecekler:
  - `[ObservableProperty] bool IsActionMenuOpen`
  - `IReadOnlyList<ActionItemViewModel> ActionItems`
  - `int SelectedActionIndex`
  - `string ActionMenuTitle` (hedef sonucun başlığı)
  - Yeni `ViewModels/ActionItemViewModel.cs`: `Title`, `Glyph`, `Hint`
- `ToggleActionMenu()`:
  - Seçili sonuç yoksa ya da sonucun hiç eylemi yoksa menü açılmaz.
  - Açılınca bekleyen Enter iptal edilir (`CancelPendingEnterLocked`). Menü o anki `Result` nesnesinin anlık görüntüsünü hedef alır.
- Menü açıkken tuşlar ve olaylar:
  - Yukarı/Aşağı tuşları menüde gezer (`MoveSelection` menü moduna göre dallanır).
  - `Enter` seçili eylemi çalıştırır (`ExecuteSelectedAsync` menü modunda `ExecuteActionAsync`'e gider).
  - `Esc` yalnızca menüyü kapatır; pencere açık kalır.
  - `Ctrl+K` menüyü kapatır.
  - Alt+rakam ve Tab etkisizdir.
  - Yeni nesil (`StartQuery`, `Reset`) menüyü kapatır. Kutuya yazmak menüyü kapatıp normal aramaya döner.
  - Aynı nesilde gelen geç yayın (`HandleResults`) menüyü kapatmaz.
- `ExecuteActionAsync`:
  - Başlarken menü kapanır.
  - Eylem true dönerse pencere gizlenir.
  - `RequeryAfter` ise ve eylem false döndüyse `StartQuery(QueryText)` ile liste yenilenir.
- `MainWindow.xaml`:
  - Menü açıkken `ResultList` gizlenir. Aynı alanda `ActionList` (`ListView`, `ResultItemStyle` ile uyumlu) ve üstünde `ActionMenuTitle` görünür.
  - WPF `Popup` kullanılmaz: odak ve topmost sorunları çıkarır.
- `MainWindow.xaml.cs`:
  - `OnPreviewKeyDown`'a `Ctrl+K` eklenir.
  - `ActionsButton` için `Click` bağlanır ve ardından arama kutusu odağı geri alır.
  - Esc'nin önceliği şöyledir: menü açıksa menüyü kapatır, değilse mevcut davranış (gizle) çalışır.
  - `HideLauncher` menüyü kapatır.

### 3. Programs eylemleri
- Eylem listesi `ProgramEntry.Kind`'a göre kurulur.
- **Kesin karar:** paketli (Store/MSIX, `Kind = AppsFolder`) uygulamalarda "Yönetici olarak çalıştır" sunulmaz. Bu eylem menüde yer almaz ve `Ctrl+Shift+Enter` bu uygulamalarda çalışmaz.

  | Eylem | Shortcut / Command | AppsFolder |
  |---|---|---|
  | Yönetici olarak çalıştır | var | yok (paketli uygulamada `runas` uygulanamaz) |
  | Dosya konumunu aç | var: `TargetPath` doluysa ve dosya varsa o, yoksa `ShortcutPath` | yok |
  | Yolu kopyala | var: `TargetPath` doluysa o, yoksa `ShortcutPath` | var: `shell:AppsFolder\<AUMID>` |
  | Gizle | var (`RequeryAfter`) | var (`RequeryAfter`) |
  | Sabitle / Sabitlemeyi kaldır | var (`RequeryAfter`) | var (`RequeryAfter`) |

- Sistem çağrıları yeni `IProgramLauncher` arayüzünün arkasına alınır. Testler sahtesini kullanır.
  - `Launch(string launchPath)`: mevcut `LaunchShortcut`.
  - `LaunchAsAdmin(string path)`: `ProcessStartInfo { UseShellExecute = true, Verb = "runas" }`.
  - `Reveal(string path)`: `explorer.exe /select,"<yol>"`.
  - Mevcut `Action<string> launch` test ctor'u bu arayüze çevrilir.
- UAC iptali `Win32Exception` ile `NativeErrorCode == 1223` olarak gelir. Bu durumda eylem false döner, log `Information` düzeyindedir (hata değildir).
- Yönetici olarak açılış da kullanım kaydına girer (`RecordLaunch`).
- Pano için mevcut `IClipboardService` (`src/Launchly.Plugins/Calculator/CalculatorPlugin.cs:8`) kullanılır. `ProgramsPlugin` ctor'una eklenir.
- Yönetici eylemi `ResultAction.Id = ResultAction.RunAsAdminId` ile işaretlenir ve `Hint = "Ctrl+Shift+Enter"` taşır. Kısayolun kuralları aşağıdaki 3a bölümündedir.

### 3a. `Ctrl+Shift+Enter`: menü açmadan yönetici olarak çalıştır
- SDK: `ResultAction`'a `string? Id` eklenir. Ayrıca `public const string RunAsAdminId = "launchly.runas"` eklenir. Eklentiler yönetici eylemini bu Id ile işaretler. Bugün bunu yalnızca Programs yapar (Shortcut ve Command girdileri).
- `MainViewModel.ExecuteSelectedAsAdminAsync()` eklenir. Bu yöntem `ExecuteSelectedAsync` ile aynı yolu kullanır: ortak bir `ExecuteSelectedCoreAsync(Func<Result, Func<Task<bool>>?> pick)` olur.
  - Aynı `_running`/`_pendingEnter` koruması geçerlidir. Sorgu sürüyorsa son yayın beklenir. Karar uyanınca `_gate` altında verilir.
  - Seçim, Enter'daki gibi o anki seçili sonuçtur.
  - Kısayolla Enter ya da kısayolun ikinci basışı birbirini tetiklemez: biri bekliyorsa ya da çalışıyorsa diğeri yok sayılır.
- **Desteklemeyen sonuç:** seçili sonucun `ContextActions` listesinde `RunAsAdminId` yoksa hiçbir şey çalışmaz. Dönüş false olur, pencere açık kalır. Normal Enter gibi davranılmaz.
  - Gerekçe: kullanıcı yetkili açılış istedi. Bunun yerine sessizce yetkisiz açılış yapmak tutarsız olur.
  - Paketli uygulamalar, web, hesap makinesi ve URL sonuçları bu kapsama girer.
- Menü açıkken `Ctrl+Shift+Enter` yok sayılır. Menüde Enter seçili eylemi zaten çalıştırır.
- `MainWindow.xaml.cs` `OnPreviewKeyDown`: `Ctrl+Shift+Enter`, düz Enter kontrolünden önce yakalanır ve olay işlenmiş sayılır (`Handled`).
- **Gösterim:**
  - Eylem menüsünde yönetici satırının sağında `Hint` görünür.
  - Alt çubukta `Actions` düğmesinin solunda ipucu metni görünür: "Ctrl+Shift+Enter Yönetici". Metin yalnızca seçili sonuç bu eylemi destekliyorsa görünür. Bağlantı `MainViewModel.CanRunSelectedAsAdmin` (bool, seçim ya da liste değişince güncellenir) ile yapılır.

### 4. Gizleme ve sabitleme: 08'deki `ProgramUsageStore`'a eklenir
- Gerekçe: veri ayar penceresinin taslak/Apply akışından değil, menüden anında yazılır. Bu yüzden `Settings` değil, aynı store kullanılır. Kullanım sayaçlarıyla aynı Id anahtarını (`ProgramEntry.Id`) paylaşır.
- `UsageData`'ya eklenecekler:
  - `List<HiddenProgram> Hidden` (`Id`, `Name`; ad, uygulama kaldırılsa da ayarlarda gösterilsin diye saklanır)
  - `List<string> Pinned` (sıralı Id listesi)
- Store API'si:
  - `Hide(id, name)`
  - `Unhide(id)`
  - `Pin(id)`
  - `Unpin(id)`
  - `IsHidden`, `IsPinned`
  - `event Action? Changed`
- Gizli girdiler hem boş listeden hem aramadan çıkarılır. Kullanım sayacı silinmez; gizleme geri alınınca sıralama eski haline döner.
- Boş liste sırası:
  1. Sabitlenenler, sabitleme sırasıyla.
  2. 08'deki frecency listesi.
  3. Başlangıç listesi.
- Toplam `Settings.EmptyQueryMaxResults` (6) ile kesilir. Sabit sayısı sınırlanmaz; 6'yı aşan sabitler boş listede görünmez.
- Gizlenmiş başlangıç öğesi boş listede gösterilmez.
- Gizlenenler geri alınabilir. Mevcut `Settings/SettingsWindow.xaml` sayfasına "Gizlenen uygulamalar" bölümü eklenir: ad ve "Göster" düğmesi.
  - Bu bölüm taslak/Apply akışına girmez; "Göster" `Unhide`'ı hemen çağırır. "Geri al" düğmesi bunu geri almaz; bu durum arayüzde not olarak yazılır.
  - 11'de bölüm "Eklentiler" sayfasına taşınacak.
  - `SettingsViewModel` store'u DI'dan alır: `App.xaml.cs`'te `new SettingsViewModel(settingsService, usageStore)`.

### 5. Files eylemleri
`FilesPlugin` sonuçlarına "Dosya konumunu aç" ve "Yolu kopyala" eklenir (06'daki "ileride" notu). Açma işlemi mevcut `IFileOpener` arayüzüne `Reveal(string path)` eklenerek yapılır.

## Kapsam dışı
- Diğer eklentilere (WebSearch, Calculator, Url, System) özel eylem. Bunlarda menüde yalnızca "Aç" görünür.
- Eylemleri arama kutusundan filtrelemek.
- Fareyle sağ tık menüsü.
- Sabitleri sürükle-bırakla sıralamak.
- Paketli uygulamanın kurulum klasörünü açmak.

## Geriye uyumluluk
- SDK: `ContextActions` varsayılan olarak boştur. `Result` başlatıcısı kullanan mevcut kod ve testler değişmeden derlenir.
- `usage.json`: `Hidden` ve `Pinned` alanları eksik olduğunda boş liste olur (`JsonStorage` varsayılanı). `null` değerleri store yükleyince normalize eder. `Version` 1 kalır.
- `settings.json` değişmez.

## Eşzamanlılık ve iptal riskleri
- **[EŞZAMANLILIK]** Menüden çalışan eylem ile Enter aynı `_running` korumasını paylaşır ve karar `_gate` altında verilir.
  - Eylem sürerken gelen Enter ya da ikinci menü eylemi yok sayılır.
  - Menü kapatılırken çalışan eylem durdurulmaz.
- **[EŞZAMANLILIK]** Menü, `Result` nesne başvurusunu tutar, indeksi tutmaz. Geç yayın listeyi değiştirse de hedef değişmez.
  - Menü durumu (`IsActionMenuOpen`, hedef, öğeler) yalnızca `_gate` altında yazılır.
  - `_gate` tutulurken `PluginManager` çağrılmaz. `RequeryAfter` yeniden sorgusu kilit dışından başlatılır.
- **[EŞZAMANLILIK]** `Ctrl+Shift+Enter` ayrı bir bekleme durumu açmaz. Enter ile aynı `PendingEnter` alanını kullanır: hangi eylemin seçileceği bilgisi bu kayıtta tutulur. Esc, gizleme, yeni sorgu ve `Reset` onu da iptal eder.
- **[İPTAL]** Menü açılınca bekleyen Enter iptal edilir. Menü kapanınca yeni bir bekleyen Enter oluşmaz.
- **[EŞZAMANLILIK]** Gizleme ve sabitleme UI iş parçacığında belleği anında günceller; disk yazımı 08'deki debounce yolundan geçer.
  - `QueryAsync` gizli ve sabit kümelerini kilitsiz bir anlık görüntüden okur.
  - `Changed` olayı ayar penceresinde UI iş parçacığına `Dispatcher` ile taşınır.
- **[İPTAL]** `RequeryAfter` sonrası başlayan sorgu yeni bir nesildir. Bu nesil eski nesle ait Enter'ı devralmaz (mevcut kural).

## Test beklentileri
- SDK: `new Result { Title = "x" }.ContextActions` boştur.
- `MainViewModel` (sahte eklentiyle, mevcut `MainViewModelTests` deseni):
  - Seçim yokken menü açılmaz.
  - "Aç" ilk sırada gelir ve `Result.Action`'ı çağırır. `Action` null ise "Aç" yoktur.
  - Menü açıkken Enter `Result.Action`'ı değil seçili eylemi çalıştırır.
  - Esc menüyü kapatır; dönüş değeri pencerenin gizlenmeyeceğini gösterir.
  - Yeni sorgu ve `Reset` menüyü kapatır. Aynı nesildeki geç yayın menüyü kapatmaz ve hedef değişmez.
  - Bekleyen Enter varken menü açılırsa Enter hiçbir şey çalıştırmaz.
  - Eylem sürerken ikinci eylem ya da Enter yok sayılır.
  - `RequeryAfter` ile false dönen eylem yeni nesil başlatır. true dönen eylem yeniden sorgu başlatmaz.
- `ExecuteSelectedAsAdminAsync`:
  - `RunAsAdminId` eylemi olan sonuçta yalnızca o eylem çalışır, `Result.Action` çağrılmaz. Eylemin dönüşü pencerenin gizlenip gizlenmeyeceğini belirler.
  - Bu eylemi olmayan sonuçta (paketli uygulama ya da web sonucu) hiçbir şey çalışmaz ve dönüş false olur.
  - Sorgu sürerken basılırsa son yayını bekler ve o anki seçili sonuçta çalışır. Beklerken Esc ya da yeni sorgu gelirse hiçbir şey çalışmaz.
  - Bekleyen ya da çalışan bir Enter varken basılırsa yok sayılır. Tersi de geçerlidir. Çift basışta eylem bir kez çalışır.
  - Menü açıkken etkisizdir.
  - `CanRunSelectedAsAdmin` seçim değişince doğru değeri verir.
- `ProgramsPlugin` (sahte `IProgramLauncher`, `IClipboardService`):
  - `Kind`'a göre eylem listesi doğrudur: `AppsFolder` girdisinde yönetici ve konum eylemi yoktur.
  - `LaunchAsAdmin` 1223 fırlatırsa false döner. Başarılı yönetici açılışı kullanım kaydına girer.
  - Yolu kopyala eylemi doğru metni panoya yazar.
  - Gizlenen girdi boş listeden ve aramadan çıkar, yeni store örneğinde de gizli kalır. `Unhide` sonrası girdi frecency sırasıyla geri gelir.
  - Sabitlenen girdi boş listede ilk sıradadır ve sıra korunur. `Unpin` sonrası eski yerine döner.
  - `Hidden` ve `Pinned` alanları olmayan eski `usage.json` yüklenir.
- `FilesPlugin`: iki yeni eylem doğru yolla sahte açıcıyı ve sahte panoyu çağırır.

## Elle doğrulama
- [ ] Bir uygulama seçiliyken `Ctrl+K` menüyü açar. Ok tuşları menüde gezer, Enter eylemi çalıştırır, Esc menüyü kapatır (pencere açık kalır).
- [ ] `Actions` düğmesine tıklamak aynı menüyü açar. Kutuya yazınca menü kapanıp arama sürer.
- [ ] "Yönetici olarak çalıştır" UAC isteminden sonra uygulamayı açar. UAC iptalinde pencere açık kalır ve hata kutusu çıkmaz.
- [ ] "Dosya konumunu aç" Gezgin'de dosyayı seçili gösterir. "Yolu kopyala" panoya doğru yolu yazar.
- [ ] Claude (paketli) için menüde yönetici ve konum eylemi görünmez.
- [ ] Bir `.lnk` uygulaması seçiliyken `Ctrl+Shift+Enter` UAC istemini açar ve alt çubukta "Ctrl+Shift+Enter Yönetici" ipucu görünür.
- [ ] Claude ya da bir web sonucu seçiliyken `Ctrl+Shift+Enter` hiçbir şey yapmaz ve ipucu görünmez.
- [ ] Gizlenen uygulama aramada çıkmaz. Ayarlardaki "Gizlenen uygulamalar" bölümünden "Göster" ile geri gelir.
- [ ] Sabitlenen uygulama boş açılışta en üsttedir ve yeniden başlatmadan sonra da öyle kalır.
- [ ] Bir dosya sonucunda (`f ...`) konum aç ve yolu kopyala eylemleri çalışır.

## İlgili yollar
`src/Launchly.Plugin/Result.cs`, `src/Launchly.Plugin/ResultAction.cs`, `src/Launchly.App/ViewModels/`, `src/Launchly.App/MainWindow.xaml(.cs)`, `src/Launchly.App/Settings/`, `src/Launchly.Plugins/Programs/`, `src/Launchly.Plugins/Files/FilesPlugin.cs`, `tests/Launchly.Tests/MainViewModelTests.cs`, `tests/Launchly.Tests/ProgramsPluginTests.cs`, `tests/Launchly.Tests/HelperPluginsTests.cs`

## Bitti sayılır
- `dotnet build Launchly.sln --no-incremental` 0 uyarı, 0 hata ile geçer.
- `dotnet test` tümüyle geçer. Mevcut `MainViewModelTests` Enter testleri değişmeden geçer.
- `Actions` düğmesi ve `Ctrl+K` gerçek uygulamada çalışır. Elle doğrulama listesinin tamamı görüldü. UAC adımı kullanıcıyla birlikte yapılır.
- Gerçek `runas`, `explorer` ve pano çağrıları yalnızca arayüz arkasındadır. Testler bunları çağırmaz.
