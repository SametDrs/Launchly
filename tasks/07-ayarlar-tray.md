# 07 – Ayarlar ve tray

## Amaç
Kullanıcının hotkey'i, web arama kaynaklarını ve başlangıç davranışını arayüzden yönetebilmesi.

## Bağımlılık
`02-pencere.md`, `05-websearch.md`

## Yapılacaklar
- **Ayar penceresi** (alt çubuktaki `Settings` ile açılır), koyu temaya uyumlu:
  - Hotkey: tuş yakalama kutusu, çakışma kontrolü, uygula/geri al.
  - Web kaynakları: liste, ekle/düzenle/sil, `Enabled` anahtarı, `{q}` içermeyen şablonu reddet.
  - Backdrop seçimi (`None`/`Mica`/`Acrylic`).
  - Pencere gizlenme davranışı (odak kaybında gizle açık/kapalı).
- **Otomatik başlatma:** Windows açılışında başlat (`HKCU\...\Run` kaydı), ayardan aç/kapat.
- **Tray:** `Settings`, `Çıkış` menüsü; tek örnek (ikinci başlatma mevcut pencereyi açar).
- Ayar değişiklikleri `settings.json`'a yazılır ve yeniden başlatmadan uygulanır.

## Kapsam dışı
Dil seçimi, güncelleme mekanizması, tema editörü, dış eklenti yönetimi.

## İlgili yollar
`src/Launchly.App/Settings/`, `src/Launchly.Core/Settings`

## Bitti ölçütü
- Hotkey değişince eski tuş serbest kalır, yenisi çalışır; çakışmada hata görünür.
- Eklenen web kaynağı (örn. `maps`) hemen çalışır.
- Otomatik başlatma açıkken oturum açınca uygulama tray'de başlar; ikinci örnek açılmaz.
- `dotnet build` temiz.
