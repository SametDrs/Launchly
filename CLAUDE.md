# Proje Talimatları

## Backlog kuralları
- İş yarıda kalırsa (oturum bitiyor, bağlam doluyor, engel çıktı, kullanıcı durdurdu) `backlog.md` içine kayıt ekle: durum, sıradaki adım, ilgili dosyalar, tarih.
- Hiçbir iş yarım bırakılmaz; ya bitirilir ya `backlog.md`'ye yazılır.
- Oturum başında `backlog.md`'yi oku, açık iş varsa kullanıcıya bildir.
- İş bitince `backlog.md`'den ilgili kaydı sil ve `backlog-log.md`'nin en üstüne tek satır kayıt ekle.
- Agent notlarını (karar, engel, öğrenilen) `backlog-log.md`'ye düş.
- Kullanıcı açıkça söylemeden kodlamaya başlama; fikirler `notes.md`'de toplanır.

## Sabitler
- Projenin değişmez gerçekleri için bkz. @const.md. Yeni bir sabit gerçek öğrenilirse oraya ekle.

## Klasör iskeleti
```
Launchly/
├─ src/
│  ├─ Launchly.Plugin/    Eklenti SDK'sı: IPlugin, Query, Result (bağımlılık yok)
│  ├─ Launchly.Core/      QueryBuilder, PluginManager, StringMatcher, Settings, JsonStorage
│  ├─ Launchly.App/       WPF: MainWindow, ViewModel'ler, tema, hotkey, tray
│  └─ Launchly.Plugins/   Dahili eklentiler (klasör başına bir eklenti)
│     ├─ Programs/  WebSearch/  Calculator/  Files/  System/  Url/
├─ tests/Launchly.Tests/
├─ CLAUDE.md  AGENTS.md  const.md  notes.md  backlog.md  backlog-log.md
```
- Yığın: C# .NET 9 + WPF. v1 = çekirdek + yardımcılar. Tasarım: koyu, geniş, alt çubuklu (Settings / Actions).
- Boş klasörlerde `.gitkeep` var; kod başlayınca silinir.
- Repo: https://github.com/SametDrs/Launchly (public). Mimari plan: `C:\Users\samet\.claude\plans\https-github-com-flow-launcher-flow-laun-smooth-wozniak.md`

## Görevler
- Aşama aşama görev dosyaları `tasks/` altında: `01-iskelet`, `02-pencere`, `03-sorgu-cekirdegi`, `04-programs`, `05-websearch`, `06-yardimcilar`, `07-ayarlar-tray`.
- Her `/goal` tek görev dosyasını okuyup bitirir; bağımlılık sırası dosyanın başında yazar.
- Görev bitince: `backlog-log.md` en üste tek satır kayıt, yarım kalırsa `backlog.md`'ye kayıt.
