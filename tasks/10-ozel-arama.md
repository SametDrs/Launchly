# 10 – Özel arama kaynakları

## Bağımlılık
`05-websearch.md`, `07-ayarlar-tray.md` (bitti), `09-actions.md` (menü ve "Aç" kuralı). `11`'den önce yapılır: 11 bu listeyi "Arama kaynakları" sayfasına taşır.

## Amaç
Kullanıcı "anahtar kelime = URL" kaynağı tanımlar. Kural, şablonda `{q}` olup olmamasına göre değişir:
- **`{q}` yoksa:** anahtar kelime siteyi doğrudan açar. Örnek `film = https://example.com/`.
- **`{q}` varsa:** mevcut arama davranışı geçerlidir.

Ek olarak `UrlPlugin`, `notes.md` gibi dosya adlarını artık site sanmaz.

## WebSearch ile birleşme
- Yeni bir model ya da eklenti yazılmaz. Özel kaynak, `Settings.SearchSources` listesindeki sıradan bir `SearchSource`'tur.
  - Varsayılan 6 kaynak (`SearchSource.CreateDefaults`) ve kullanıcı kaynakları aynı listede durur.
  - Aynı doğrulamadan geçer ve aynı `WebSearchPlugin` tarafından çalıştırılır.
- Ayrım, şablonda `{q}` olup olmamasıdır. `SearchSource`'a hesaplanan `bool IsSearchable => UrlTemplate.Contains(Placeholder)` özelliği eklenir. Bu özellik JSON'a yazılmaz (`[JsonIgnore]`).

## Yapılacaklar

### 1. `src/Launchly.Core/SearchSource.cs`
`TryValidate`'te `{q}` zorunluluğu kalkar (şu an `SearchSource.cs:45-48`). Kalan kurallar:
- Boş olmayan başlık.
- Boşluksuz anahtar kelime, `*` olamaz.
- Mutlak `http`/`https` URL.

`{q}` yoksa şablon olduğu gibi `Uri.TryCreate(..., Absolute)` ile doğrulanır. Hata metinleri "`{q}` içeren" ifadesinden arındırılır.

### 2. `src/Launchly.Plugins/WebSearch/WebSearchPlugin.cs`
- **Anahtar kelimeli sorgu (`film ` / `film xyz`), `IsSearchable == false` olan kaynak:**
  - Tek sonuç üretilir: `Title = "<Başlık> sitesini aç"`, `SubTitle = UrlTemplate`, `Score = 1000`, `TypeLabel = "Alt+1"`.
  - `Action` `SearchUrl.IsSafe` denetiminden sonra `IUrlOpener.Open` çağırır.
  - Kelimeden sonra yazılan metin yok sayılır (kesin karar).
  - Şu anki "Aramak için yazın" sonucu (`WebSearchPlugin.cs:66-77`) yalnızca `IsSearchable` kaynaklarda kalır.
- **Yalın anahtar kelime (sonda boşluk yok):** `QueryBuilder`, kelimeyi yalnızca ardından boşluk gelince anahtar kelime sayar. Bu yüzden "film" yazıp Enter'a basmak bugün global sorgudur.
  - Çözüm: `Metadata.ActionKeywords`'e `PluginMetadata.GlobalKeyword` de eklenir. Karma kayıt (hem `*` hem kelime) `PluginManager.RebuildIndex` (`PluginManager.cs:325-350`) tarafından zaten destekleniyor.
  - Global sorguda `query.Search.Trim()`, etkin ve `IsSearchable == false` bir kaynağın anahtar kelimesine eşitse (büyük/küçük harf duyarsız), yukarıdaki "sitesini aç" sonucu `Score = 950` ile döner.
  - Diğer tüm global sorgularda eklenti hemen boş liste döner: ağ ve disk erişimi yok, sözlük araması dışında iş yok.
  - `IsSearchable` kaynaklar için global davranış değişmez (bilinçli; 07 review kararı).
- `Metadata` yine her okunuşta ayardan üretilir. Global anahtar sabittir.

### 3. Ayar penceresi: mevcut `Settings/SettingsWindow.xaml` ve `SettingsViewModel`
- Bölüm başlığı "Web kaynakları" yerine "Arama kaynakları" olur.
- Mevcut ekle (`AddSource`), sil (`RemoveSource`) ve satır içi düzenleme korunur.
- Satırın altına ipucu eklenir: "Adreste `{q}` varsa arama yapar, yoksa siteyi açar."
- Yeni boş satır varsayılanları: `Enabled = true`, şablon `https://`.
- Doğrulama hatası hangi satırda olduğunu (başlık ya da sıra) mesajda söyler.

### 4. Anahtar kelime çakışması: `src/Launchly.Core/SettingsService.cs`
- `TryValidate` mevcut "etkin kaynaklarda yinelenen anahtar kelime" kontrolüne ek olarak başka eklentilerin anahtar kelimeleriyle çakışmayı da reddeder. Örnek: Files `f`.
- Liste, `PluginManager` üzerinden WebSearch dışındaki eklentilerin `Metadata.ActionKeywords`'ünden (global hariç) alınır. Bunun için `PluginManager`'a `IReadOnlyCollection<string> ReservedKeywords(string exceptPluginId)` eklenir (?). SettingsService şu an `RefreshKeywords` için zaten `PluginManager`'a erişiyor.

### 5. `src/Launchly.Plugins/Url/UrlPlugin.cs`: dosya adı ayrımı
- **Kesin karar (kullanıcı onayladı):** dosya uzantısı listesi yaklaşımı uygulanır. TLD beyaz listesi kullanılmaz.
  - `ornek.md` gibi şemasız ve `www.`'suz bir girdi site açmaz.
  - `.md`, `.py`, `.sh`, `.zip` ve `.mov` gerçek TLD'ler olsa da şemasız yazılınca site sayılmaz. Bu bilinen ve kabul edilen sınırlamadır.
- Şemasız girdide (`http(s)://` yok) son etiket (TLD) aşağıdaki dosya uzantısı listesindeyse sonuç yoktur:
  - Bu kural yalnızca girdi `www.` ile başlamıyorsa ve port ya da `/`, `?`, `#` içermiyorsa uygulanır.
  - Liste `UrlDetector` içinde `static readonly HashSet<string>` (`OrdinalIgnoreCase`) olarak tutulur:
    `md, txt, log, ini, cfg, conf, json, xml, yml, yaml, csv, tsv, cs, csproj, sln, py, js, ts, tsx, jsx, java, cpp, h, sh, ps1, bat, cmd, exe, dll, msi, iso, zip, rar, 7z, gz, tar, png, jpg, jpeg, gif, bmp, svg, webp, pdf, doc, docx, xls, xlsx, ppt, pptx, mp3, wav, flac, mp4, mov, avi, mkv, webm`
- Uygulama yeri `UrlDetector.TryNormalize` (`UrlPlugin.cs:16-47`), `BareDomain` eşleşmesinden sonradır.
- Açık şemalı (`https://notes.md`) ve `www.` ile başlayan girdi URL olarak kalır.

## Kapsam dışı
- Kaynak başına ikon ya da favicon indirme.
- Kaynak sıralamasını sürükle-bırakla değiştirme.
- İçe ve dışa aktarma.
- `{q}` dışında yer tutucular.
- `{q}`'suz kaynakta sonradan yazılan metni URL'ye eklemek.
- Yalın kelimeyle `IsSearchable` kaynağın önizlemesi.
- Sayfa tasarımı: sol menü 11'de gelir.

## Geriye uyumluluk
- `settings.json` şeması değişmez. Yeni alan yoktur; `IsSearchable` hesaplanır ve yazılmaz.
- Mevcut kaynakların tümünde `{q}` var, davranışları aynı kalır.
- `Settings.Normalize` doğrulama yapmaz. `{q}`'suz kaynak içeren dosya eski sürümde yüklenir ve o sürümde yalnızca ayar penceresinde reddedilir.

## Eşzamanlılık ve iptal riskleri
- **[EŞZAMANLILIK]** `SettingsService.Apply` `SearchSources` listesini değiştirirken eklenti başka bir iş parçacığında sorgu çalıştırıyor olabilir.
  - `WebSearchPlugin` her sorguda `_settings.SearchSources` başvurusunu bir kez yerel değişkene alır ve yalnızca onu kullanır.
  - `Apply` listeyi yerinde değiştirmez, yeni liste atar. Bu varsayım doğrulanmalı ve gerekiyorsa `SettingsService.cs:149-153` düzeltilmeli (?).
- **[EŞZAMANLILIK]** `Metadata` okuması ile `RefreshKeywords` arasında yarış olmamalıdır. Global anahtar sabit olduğundan dizin yeniden kurulurken eklenti global listeden düşmez.
- **[İPTAL]** Global yol her sorguda çalışır ve senkron, kısa bir sözlük araması yapar. `cancellationToken` girişte bir kez kontrol edilir. `SearchDelayMs` eklenmez.

## Test beklentileri
- `SearchSource.TryValidate`:
  - Kabul: `{q}`'suz `https://site/`.
  - Red: `ftp://`, göreli adres, boş şablon, boşluklu kelime, `*`.
- `SearchSource.IsSearchable` iki durumda doğru değer verir ve JSON'a yazılmaz.
- `WebSearchPlugin`:
  - `film ` ve `film xyz` tek bir "sitesini aç" sonucu verir. Eylem sahte açıcıya tam URL'yi gönderir.
  - Global `film` ve `FILM` aynı sonucu 950 puanla verir.
  - Global `filmler` ve `youtube` (`{q}`'lu kaynak) sonuç vermez.
  - Devre dışı kaynak sonuç vermez.
  - `IsSafe` reddettiği URL'yi açmaz.
- `PluginManager`: hem `*` hem kelime kayıtlı eklenti, global sorguda ve kelimeli sorguda bir kez çalışır (çift sonuç yok).
- `SettingsService`:
  - `f` kelimeli kaynak (Files ile çakışma) reddedilir; canlı ayar değişmez.
  - `{q}`'suz kaynak Apply sonrası hemen sorgulanabilir (`RefreshKeywords`).
- `SettingsViewModel`: ekle → düzenle → Apply → Revert gidiş-dönüşü `{q}`'suz kaynakla çalışır.
- `UrlDetector` yeni durumlar:
  - Red: `notes.md`, `ornek.md`, `readme.txt`, `app.py`, `archive.zip`, `Notes.MD`.
  - Kabul: `https://notes.md`, `www.notes.md`, `notes.md/yol`, `example.com`, `github.io`.
  - `HelperPluginsTests.Url_Detects_AndAddsScheme` ve `Url_RejectsNonHttpAndNonUrls` değişmeden geçer.

## Elle doğrulama
- [ ] Ayarlar → Arama kaynakları → Ekle: `Film`, `film`, `https://example.com/` → Uygula.
- [ ] Kutuya `film` yazıp Enter'a basınca site varsayılan tarayıcıda açılır. `film ` ve `film abc` aynı siteyi açar.
- [ ] Şablona `{q}` eklenince `film abc` arama URL'sini açar.
- [ ] Kelime olarak `f` girilince Uygula hata gösterir ve kaynak kaydedilmez.
- [ ] Kaynak silinip Uygula'ya basılınca `film` artık siteyi açmaz.
- [ ] `notes.md` yazınca "Open URL" sonucu çıkmaz. `https://notes.md` yazınca çıkar.
- [ ] `ornek.md`, `rapor.pdf` ve `foto.jpg` site açmaz. `www.ornek.md` ve `https://ornek.md` "Open URL" sonucu verir. `github.com` önceki gibi çalışır.
- [ ] Mevcut kaynaklar (`g `, `youtube ` vb.) önceki gibi çalışır.

## İlgili yollar
`src/Launchly.Core/SearchSource.cs`, `src/Launchly.Core/SettingsService.cs`, `src/Launchly.Core/Plugins/PluginManager.cs`, `src/Launchly.Plugins/WebSearch/`, `src/Launchly.Plugins/Url/UrlPlugin.cs`, `src/Launchly.App/Settings/`, `tests/Launchly.Tests/WebSearchPluginTests.cs`, `tests/Launchly.Tests/SettingsServiceTests.cs`, `tests/Launchly.Tests/HelperPluginsTests.cs`

## Bitti sayılır
- `dotnet build Launchly.sln --no-incremental` 0 uyarı, 0 hata ile geçer.
- `dotnet test` tümüyle geçer.
- `film` örneği gerçek uygulamada eklenir ve çalışır. Tarayıcı adımını kullanıcı doğrular.
- `notes.md` URL sonucu üretmez.
- Mevcut `settings.json` değişiklik yapılmadan yüklenir.
