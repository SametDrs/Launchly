# 02 – Pencere

## Amaç
`Alt+Space` ile açılıp kapanan, koyu, geniş, alt çubuklu arama penceresini yapmak. Sonuçlar henüz sahte (sabit) veriyle gösterilir.

## Bağımlılık
`01-iskelet.md`

## Yapılacaklar
- **MainWindow:** borderless, topmost, `ShowInTaskbar=false`, ekranın üst-orta bölgesinde, yuvarlak köşe.
- **Arka plan:** Win11 Mica/Acrylic (`DwmSetWindowAttribute`); desteklenmiyorsa düz koyu renk. Tek `Backdrop` ayarı (`None`/`Mica`/`Acrylic`).
- **Düzen:**
  - Üstte arama kutusu: sol arama ikonu, placeholder `Type to search...`, altında mavi çizgi.
  - Ortada `ListView`: ikon, başlık, sağda tür etiketi (`Application`), seçili satırda sol mavi şerit.
  - Altta çubuk: solda `Settings`, sağda `Actions`.
- **Tema:** koyu varsayılan `ResourceDictionary`.
- **Hotkey:** Win32 `RegisterHotKey` (varsayılan `Alt+Space`), `HwndSource` ile mesaj yakalama. Çakışmada kullanıcıya hata göster. Tuş `Settings`'ten okunur.
- **Davranış:** `Deactivated` olunca gizle; her açılışta kutuyu temizle ve odakla; Esc gizler.
- **Klavye:** ↑/↓ seçim, Enter (şimdilik boş), Alt+1..9 hızlı seçim iskeleti.
- **ViewModel:** CommunityToolkit.Mvvm ile `MainViewModel` (`QueryText`, `Results`, `SelectedIndex`).
- **Tray ikonu:** çıkış menüsü.

## Kapsam dışı
Gerçek arama, eklentiler, ayar penceresi, otomatik başlatma.

## İlgili yollar
`src/Launchly.App/` (MainWindow, ViewModels, Themes, Hotkey, Tray)

## Bitti ölçütü
- `Alt+Space` pencereyi açar/kapatır; odak kaybında ve Esc ile gizlenir.
- Sahte listede ok tuşlarıyla seçim çalışır; tasarım 1. görseldeki düzenle uyumlu.
- Hotkey başka uygulamada kayıtlıysa anlaşılır hata görünür.
- `dotnet build` temiz.
