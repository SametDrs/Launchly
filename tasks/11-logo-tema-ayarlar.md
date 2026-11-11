# 11 – Logo, yarı saydam tema ve ayar penceresi tasarımı

## Bağımlılık
`08`, `09` ve `10` bitmiş olmalı: Ayarlar sayfalarına taşınacak bölümler o görevlerde ekleniyor.

**Ön koşul:** yöneticinin verdiği, kullanıcının seçtiği logo PNG'sinin yolu.
- Şartlar: kare, en az 1024×1024, koyu lacivert zemin, açık maviye geçişli soyut "L" / yukarı ok.
- Yönetici bu görevi başlatırken yolu notta verir.
- Yol yoksa görev başlamaz. Durum `backlog.md`'ye yazılır.

## Amaç
1. **Logo:** aynı logo exe, tray, ayar penceresi ve kurulum dosyasında kullanılır.
2. **Tema:** Windows 11'de gerçekten görünen Mica/Acrylic yarı saydamlığı. Windows 10'da ve sistem saydamlığı kapalıyken koyu temaya düşülür. Tema ayarlardan seçilir.
3. **Ayar penceresi:** Flow Launcher benzeri sol menülü düzen. Sayfalar Genel, Görünüm, Arama kaynakları, Eklentiler ve Hakkında. Ana pencereyle aynı tema kullanılır.

## Yapılacaklar

### 1. Logo ve `.ico`
- Seçilen PNG `src/Launchly.App/Assets/Launchly.png` olarak kopyalanır.
- **`.ico` üretimi:** yeni `tools/make-ico.ps1` betiği PNG'den `src/Launchly.App/Assets/Launchly.ico` üretir.
  - Boyutlar: 16, 20, 24, 32, 40, 48, 64 ve 256 px.
  - Her giriş PNG kodlu olur, `System.Drawing` HighQualityBicubic ile küçültülür.
  - Betik tekrar çalıştırılabilir. Üretilen `.ico` repoya eklenir.
- **Bağlama:**
  - `Launchly.App.csproj`: `<ApplicationIcon>Assets\Launchly.ico</ApplicationIcon>`. `Assets\Launchly.ico` ve `Assets\Launchly.png` için `<Resource Include=...>`.
  - `Tray/TrayIcon.cs`: `SystemIcons.Application` yerine `Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Launchly.ico")).Stream` ile `new Icon(stream, SystemInformation.SmallIconSize)` kullanılır. `Icon` nesnesi `TrayIcon` dispose edilirken dispose edilir.
  - `SettingsWindow.xaml`: `Icon="pack://application:,,,/Assets/Launchly.ico"`. `MainWindow` görev çubuğunda görünmüyorsa (?) yalnızca Alt+Tab için aynı ikon verilir.
  - `installer/Launchly.iss`: `[Setup]`'a `SetupIconFile=..\src\Launchly.App\Assets\Launchly.ico` eklenir. `UninstallDisplayIcon={app}\{#AppExe}` aynı kalır; exe artık ikonu taşır.
- **16 px okunurluğu:** 16 ve 20 px boyutlarında logo seçilemez hale gelirse görev durur ve yöneticiye bildirilir. Sadeleştirilmiş küçük boyut görseli yöneticiden istenir; görsel bu görevde çizilmez.

### 2. Yarı saydam tema
- **Önce teşhis.** Bugünkü durum: log `Arka plan: istenen "Mica", uygulanan "Mica"` diyor, ama kullanıcı saydamlık görmüyor.
  - Teşhis **hem Mica hem Acrylic için ayrı ayrı** yapılır. Yeni varsayılan Acrylic olduğundan, Acrylic'in gözle görülür biçimde bulanık çizildiği kanıtlanmadan görev bitmez.
  - Aşağıdakiler her iki tür için tek tek denenir. Sonuçlar tür başına `backlog-log.md`'ye not olarak yazılır:
  - (a) Mica tasarım gereği arkadaki pencereleri değil, yalnızca duvar kağıdı tonunu gösterir. Bulanık görünüm Acrylic (`DWMWA_SYSTEMBACKDROP_TYPE = 3`) gerektirir.
  - (b) `Interop/WindowBackdrop.cs`'teki kaplama alfası etkiyi bastırıyor olabilir: Mica `0x40`, Acrylic `0x99`, renk `#1F2024`.
  - (c) `Themes/Dark.xaml`'daki opak fırçalar yüzeyi kapatıyor olabilir: `FooterBrush`, `FieldBrush`, liste arka planı.
  - (d) Sistem ayarı "Saydamlık efektleri" kapalı olabilir (`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency = 0`), ya da enerji tasarrufu modu açık olabilir.
  - (e) `WindowStyle=None` pencerede DWM arka planının çizilip çizilmediği (?).
- **Düzeltme:**
  - Kaplama rengi ve alfası saf bir fonksiyona çıkarılır: yeni `Interop/BackdropPalette.cs`, `For(BackdropType requested, bool supported, bool transparencyEnabled)`.
  - Fonksiyon uygulanan türü ve `WindowBackgroundBrush`, `FooterBrush`, `FieldBrush` renklerini döner. `WindowBackdrop.Apply` bu sonucu pencerenin `Resources`'ına yazar.
  - Saydam modda `FooterBrush` ve `FieldBrush` yarı saydam olur. Alfa değerleri teşhise göre seçilir.
- **Yedek:**
  - `WindowBackdrop.IsSupported` false ise (build < 22621) ya da `EnableTransparency = 0` ise uygulanan tür `None` olur. Bu durumda düz `#1F2024` kullanılır ve log yazılır.
  - Sistem saydamlık ayarı çalışırken değişirse `SystemEvents.UserPreferenceChanged` ile yeniden uygulanır.
- **Varsayılan tema Acrylic olur (kesin karar).** `src/Launchly.Core/Settings.cs:18` `Backdrop` varsayılanı `BackdropType.Mica` yerine `BackdropType.Acrylic` olur.
- **Mevcut kullanıcılar için tek seferlik geçiş:**
  - Sorun: v1 dosyasında kullanıcının açıkça Mica seçtiği ile varsayılanı hiç değiştirmediği ayırt edilemez. `JsonStorage` tüm alanları yazar; kullanıcının makinesindeki dosyada da `"Backdrop": "Mica"` vardır.
  - Kural: `Settings.Version` 1'den 2'ye çıkar (`Settings.cs:12` varsayılanı `2`). `Settings.Normalize` (`Settings.cs:42`) içinde, dosyadan `Version < 2` okunduysa:
    - `Backdrop == Mica` → `Acrylic`. Eski varsayılan olduğu için kullanıcı seçimi sayılmaz.
    - `None` ve `Acrylic` olduğu gibi kalır; bunlar ancak açık seçimle yazılmış olabilir.
    - Ardından `Version = 2` atanır ve geçiş `Information` düzeyinde loglanır.
  - Geçiş yalnızca bir kez yapılır. `Version = 2` dosyada `Mica` görülürse bu kullanıcının açık seçimidir ve korunur.
  - Geçiş sonucu diske bir sonraki kayıtta yazılır (`OnExit` ya da Uygula). Bu adım için ayrıca kayıt yapılmaz.
  - `SettingsService.CreateDraft` `Version`'u taslağa kopyalar (?). Kopyalamıyorsa Uygula sonrası `Version` 1'e düşmemelidir; bu kontrol edilir ve gerekiyorsa `SettingsService.cs:62-73` düzeltilir.
- **Tema seçimi:** mevcut `Settings.Backdrop` (`BackdropType`: `None`/`Mica`/`Acrylic`) tema ayarı olarak kalır; yeni alan eklenmez. Görünüm sayfasındaki etiketler:
  - "Koyu (düz)"
  - "Mica"
  - "Acrylic (bulanık)"
- Desteklenmeyen sistemde Mica ve Acrylic seçenekleri devre dışıdır ve açıklama gösterilir: "Windows 11 22H2 ve sonrası gerekir" ya da "Windows saydamlık efektleri kapalı".
- **Ayar penceresi de aynı temayı kullanır.**
  - `SettingsWindow.xaml` `Background="{StaticResource WindowBackgroundBrush}"` → `DynamicResource` olur. Diğer fırçalar da pencere düzeyinde ezilebilsin diye `DynamicResource` olur.
  - `App.xaml.cs`'te `settingsService.Applied` olayı `WindowBackdrop.Apply`'ı hem `MainWindow` hem `SettingsWindow` için çağırır. `SettingsWindow` ilk gösterilişte (`SourceInitialized`) de uygular.
  - `SettingsWindow.xaml.cs`'teki koyu başlık çubuğu (DWM 20) çağrısı `WindowBackdrop` içine taşınır; tek yer kalır.

### 3. Ayar penceresi: sol menülü düzen
- **`Settings/SettingsWindow.xaml` yeniden düzenlenir:**
  - Solda ~200 px `ListBox NavList`. Öğeler glyph ve metinle gösterilir: Genel, Görünüm, Arama kaynakları, Eklentiler, Hakkında.
  - Sağda `ContentControl` seçili sayfayı gösterir.
  - Alt çubuk (Mesaj, Geri al, Uygula, Kapat) tüm sayfalarda ortaktır.
  - Pencere boyutu 900×600 px, en küçük 760×500 px.
- Yeni `UserControl`'ler `src/Launchly.App/Settings/Pages/` altında, hepsi aynı `SettingsViewModel`'e bağlı:
  - **`GeneralPage.xaml`:** Kısayol (`HotkeyBox`; tuş yakalama `OnHotkeyKeyDown` ile çalışmaya devam eder) ve Davranış (`HideOnFocusLost`, `StartOnSystemStartup`).
  - **`AppearancePage.xaml`:** tema seçimi (yukarıdaki üç seçenek ve açıklama).
  - **`SearchSourcesPage.xaml`:** 10'daki "Arama kaynakları" listesi olduğu gibi taşınır.
  - **`PluginsPage.xaml`:**
    - Her eklenti için ad, Id, anahtar kelimeler ve `Etkin` anahtarı gösterilir. Bu, `Settings.DisabledPlugins` ile çalışır.
    - Programs bölümünün altında 09'daki "Gizlenen uygulamalar" listesi yer alır (taşınır).
    - Eklenti listesi `PluginManager`'dan okunur. `SettingsViewModel`'e salt okunur `IReadOnlyList<PluginMetadata>` verilir.
  - **`AboutPage.xaml`:**
    - Logo (`Assets/Launchly.png`).
    - Sürüm (`AssemblyInformationalVersionAttribute`).
    - Repo bağlantısı (`https://github.com/SametDrs/Launchly`). Bağlantı mevcut `IUrlOpener` ile açılır.
- **`SettingsViewModel`:**
  - `SettingsPage SelectedPage` (yeni enum).
  - `ObservableCollection<PluginToggleItem> Plugins` (`Id`, `Name`, `Keywords`, `IsEnabled`).
  - `Revert` eklenti anahtarlarını da geri alır.
  - `IsBackdropSupported` ve `BackdropUnavailableReason` özellikleri eklenir. Destek bilgisi `Func<(bool Supported, string? Reason)>` olarak enjekte edilir.
- **`SettingsService.Apply`** (`SettingsService.cs:149-153`) `DisabledPlugins`'i de `_live`'a kopyalar.
  - Bugün kopyalamıyor, bu yüzden Eklentiler sayfası bu değişiklik olmadan çalışmaz.
  - Liste yeni bir nesne olarak atanır, yerinde değiştirilmez.
  - `CreateDraft` bu alanı zaten kopyalıyor (`SettingsService.cs:68`).

## Kapsam dışı
- Açık tema ve tema editörü.
- Vurgu rengi seçimi.
- Saydamlık kaydırıcısı.
- `MaxResults` ayarı.
- Dil seçimi ve güncelleme denetimi.
- Dış eklenti yükleme.
- Eklenti başına ayrıntılı ayar sayfası.
- Logo görselini üretmek (yönetici yapar).
- Ana pencerenin düzenini değiştirmek.

## Geriye uyumluluk
- `settings.json` şemasına yeni alan eklenmez.
  - `Backdrop` aynı alan ve aynı değerlerle tema ayarıdır.
  - `DisabledPlugins` mevcut bir alandır.
  - Yalnızca `Version` değeri 1'den 2'ye çıkar.
- Varsayılan tema `Acrylic` olur. v1 dosyasında `"Backdrop": "Mica"` varsa bir kez `Acrylic`'e geçilir. `None` ve `Acrylic` korunur. v2 dosyasındaki her değer kullanıcı seçimidir ve korunur. Ayrıntı yukarıda, "Mevcut kullanıcılar için tek seferlik geçiş".
- `Version` alanı olmayan bir dosya yeni varsayılan (2) ile okunur ve geçişe girmez. Bu sınır durum bilinçli olarak kabul edilir.
- Windows 10'da `Mica` ya da `Acrylic` yazılı dosya hata vermez. Uygulanan tür `None` olur; ayar dosyası bu yüzden yeniden yazılmaz. Geçiş sonucu `Acrylic` olsa bile Windows 10'da `None` uygulanır.
- `DisabledPlugins`'te bilinmeyen Id varsa korunur ve yok sayılır.

## Eşzamanlılık ve iptal riskleri
- **[EŞZAMANLILIK]** `PluginManager.IsEnabled` (`PluginManager.cs:316-317`) `DisabledPlugins`'i sorgu iş parçacığında okur.
  - `Apply` listeyi yerinde değiştirmez, yeni liste atar.
  - Süren sorgu eski listeyle biter; sonraki sorgu yeni listeyi görür.
  - Kapatılan eklenti için `RefreshKeywords` çağrısı yeterli mi, kontrol edilir (?).
- **[EŞZAMANLILIK]** `WindowBackdrop.Apply` ve `UserPreferenceChanged` işleyicisi UI iş parçacığında çalışır. `SystemEvents` olayı başka bir iş parçacığından gelirse `Dispatcher.BeginInvoke` ile taşınır. Olay aboneliği `OnExit`'te kaldırılır.
- **[İPTAL]** Kapanışta `TrayIcon.Dispose` ikon nesnesini ve `NotifyIcon`'u bırakır. Tray ikonunun görev çubuğunda "hayalet" kalmaması elle doğrulanır.
- **[EŞZAMANLILIK]** Sayfa değiştirmek taslağı sıfırlamaz. Tek `SettingsViewModel` örneği tüm sayfaların taslağını tutar. Uygula tüm sayfaları birlikte uygular.

## Test beklentileri
- `BackdropPalette.For` için tüm kombinasyonlar:
  - desteklenmiyor → `None`
  - saydamlık kapalı → `None`
  - Mica ve Acrylic için beklenen alfa değerleri
  - `None` → opak renk
- Tema geçişi (`Settings.Normalize`):
  - `new Settings().Backdrop == Acrylic` ve `Version == 2`.
  - `{"Version":1,"Backdrop":"Mica"}` → `Acrylic`, `Version == 2`.
  - `{"Version":1,"Backdrop":"None"}` → `None`. `{"Version":1,"Backdrop":"Acrylic"}` → `Acrylic`.
  - `{"Version":2,"Backdrop":"Mica"}` → `Mica` (açık seçim korunur).
  - Geçiş sonrası kaydedip yeniden yükleme ikinci bir geçiş yapmaz.
  - `CreateDraft` → `Apply` sonrası `Version` 2 kalır.
  - Eski varsayılana bağlı mevcut testler yeni varsayılana göre güncellenir:
    - `tests/Launchly.Tests/HotkeyParserTests.cs:59` `Backdrop_DefaultsToMica_AndSerializesAsString` → Acrylic varsayılanı. Test yeniden adlandırılır, kayıt/okuma denetimi farklı bir değerle (`Mica`) yapılır.
    - `tests/Launchly.Tests/SettingsServiceTests.cs:196`: "hiçbir şey uygulanmadı" denetimi yeni varsayılanla yapılır.
- `.ico` dosyası: test repo kökünü `Launchly.sln`'e kadar yukarı çıkarak bulur, `Assets/Launchly.ico` başlığını okur. 8 giriş vardır ve boyutlar beklenen kümedir.
- `SettingsService`:
  - `DisabledPlugins` Apply ile canlı ayara geçer ve kaydedilir.
  - Doğrulama hatasında canlı ayar değişmez.
  - Apply sonrası `PluginManager` devre dışı eklentiyi çalıştırmaz.
- `SettingsViewModel`:
  - Eklenti anahtarını kapat → Apply → `DisabledPlugins` Id'yi içerir. Revert anahtarı geri alır.
  - Destek yokken `IsBackdropSupported == false` olur ve açıklama metni döner.
  - Sayfa değiştirmek taslağı korur.
- Mevcut `SettingsServiceTests.ViewModel_ApplyAndRevert` değişmeden ya da yalnızca yeni ctor parametreleriyle geçer.

## Elle doğrulama
- [ ] Gezgin'de `Launchly.exe`, görev çubuğu ve Alt+Tab yeni logoyu gösterir. Tray'de 16 px logo seçilebilir durumdadır.
- [ ] `installer\build.ps1` sonrası `Launchly-Setup.exe` ikonu ve "Uygulamalar ve özellikler" girdisi logoyu gösterir.
- [ ] Kullanıcının mevcut `settings.json` dosyasıyla (`Version` 1, `"Backdrop": "Mica"`) açılışta tema Acrylic'tir. Çıkıştan sonra dosyada `"Version": 2` ve `"Backdrop": "Acrylic"` yazar. Ayarlardan Mica seçilip yeniden başlatılınca Mica kalır.
- [ ] Windows 11'de "Acrylic (bulanık)" seçilince ana pencerenin arkası bulanık görünür. "Mica" seçilince duvar kağıdı tonu görünür. "Koyu (düz)" opaktır.
- [ ] Windows Ayarlar → Kişiselleştirme → Renkler → "Saydamlık efektleri" kapatılınca pencere düz koyuya döner. Açılınca geri gelir. Yeniden başlatma gerekmez.
- [ ] Ayar penceresi ana pencereyle aynı temayı kullanır. Sol menüdeki beş sayfa açılır. Uygula ve Geri al tüm sayfalarda çalışır.
- [ ] Eklentiler sayfasında Files kapatılıp Uygula'ya basılınca `f deneme` sonuç vermez. Açılınca geri gelir.
- [ ] Hakkında sayfasında sürüm doğrudur. Repo bağlantısı tarayıcıda açılır.
- [ ] Kısayol değiştirme, otomatik başlatma ve arama kaynakları yeni sayfalarda önceki gibi çalışır.

## İlgili yollar
`src/Launchly.App/Assets/`, `tools/make-ico.ps1`, `src/Launchly.App/Launchly.App.csproj`, `src/Launchly.App/Tray/TrayIcon.cs`, `src/Launchly.App/Interop/WindowBackdrop.cs`, `src/Launchly.App/Themes/Dark.xaml`, `src/Launchly.App/Settings/`, `src/Launchly.App/App.xaml.cs`, `src/Launchly.Core/SettingsService.cs`, `installer/Launchly.iss`, `tests/Launchly.Tests/SettingsServiceTests.cs`

## Bitti sayılır
- `dotnet build Launchly.sln --no-incremental` 0 uyarı, 0 hata ile geçer.
- `dotnet test` tümüyle geçer.
- Logo exe, tray, ayar penceresi ve setup dosyasında görünür.
- Windows 11'de seçilen tema gözle görülür biçimde uygulanır. Varsayılan Acrylic arkadaki pencereleri bulanık gösterir. Saydamlık kapalıyken düz koyu temaya düşülür.
- Mica → Acrylic geçişi yalnızca v1 dosyasında ve yalnızca bir kez çalışır.
- Ayar penceresi beş sayfalı sol menü düzenindedir ve tüm eski ayarlar kayıpsız çalışır.
- Teşhis sonuçları `backlog-log.md`'ye yazıldı.
