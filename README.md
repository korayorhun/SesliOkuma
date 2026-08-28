# Sesli Okuma

<p align="center"><img src="docs/panel.png" width="380" alt="Sesli Okuma"> <img src="docs/panel-en.png" width="380" alt="Sesli Okuma (English)"></p>

**Türkçe** · [English](#english)

Windows 10/11 için küçük bir sistem tepsisi aracı: **herhangi bir uygulamada seçtiğiniz metni bir kısayolla sesli okur** (varsayılan `Ctrl + Alt + S`), tekrar basınca susar. İkona tıklayınca açılan minimal panelden kısayolu, ana dili ve sesleri, okuma hızını ve arayüz dilini seçersiniz.

- **8 arayüz dili:** Türkçe, English, 中文, हिन्दी, Español, Français, العربية, Português — panelden anında değiştirilir; kurulum da aynı dillerdedir.
- **Kısayol sizin:** alana tıklayıp yeni tuş kombinasyonuna basın; çakışma varsa söyler, eski kısayol korunur.
- **Dile göre ses:** metnin yazısı/dili algılanır; ana dil metinleri ana dil sesiyle, diğerleri ikinci sesle okunur. Çok dilli neural sesler her iki listede de görünür.
- **Doğal sesler tek tıkla:** panel, açık kaynak [NaturalVoiceSAPIAdapter](https://github.com/gexgd0419/NaturalVoiceSAPIAdapter)'ı kurmayı önerir (Emel, Ahmet ve 15+ İngilizce neural ses; internet gerekir). Windows'un kendi ses paketleri de panelden eklenir.
- **Otomatik güncelleme:** günde bir kez GitHub Releases'a bakar; yeni sürümü panelde gösterir, SHA-256 doğrulamalı indirir, sessizce kurar ve yeniden açılır (kapatılabilir).
- **Hakkında & destek:** dil menüsünün altındaki *Hakkında* kartında kaynak kod, sürüm notları, sorun bildirme ve isteğe bağlı **Destek ol ♥** (GitHub Sponsors, tek seferlik bağış) bulunur.
- Kurulum yönetici yetkisi istemez; **veri toplamaz** — metin yalnızca seçtiğiniz Windows ses sağlayıcısına (SAPI) gider. Ayarlar ve günlük: `%LOCALAPPDATA%\SesliOkuma\`.

## Kurulum

[Releases](https://github.com/korayorhun/SesliOkuma/releases) sayfasından en son `SesliOkuma-Setup-x.y.z.exe` dosyasını indirip çalıştırın. İmzasız bir program olduğu için Windows SmartScreen "tanınmayan uygulama" uyarısı verebilir: *Ek bilgi → Yine de çalıştır*.

## Kullanım

| Eylem | Nasıl |
|---|---|
| Seçili metni oku | metni seç → `Ctrl + Alt + S` (veya seçtiğiniz kısayol) |
| Sustur | okurken tekrar aynı kısayol (veya tepsi ikonu → Sustur) |
| Ayar paneli | tepsi ikonuna tıkla (veya exe'yi tekrar çalıştır) |
| Arayüz dili | paneldeki küre simgesi |
| Çıkış | tepsi ikonu → sağ tık → Çıkış |

Metin ilk olarak UI Automation ile okunur; bunu desteklemeyen uygulamalarda otomatik olarak kopyalama (Ctrl+C) yoluna düşer ve panonuzu eski hâline geri koyar.

## Kaynaktan derleme

Ek araç gerekmez; .NET Framework 4.8 ile gelen C# derleyicisi yeterlidir:

```powershell
.\build.ps1                 # src\*.cs -> SesliOkuma.exe (ve başlatır)
.\release.ps1 -Version 1.2.0 # sürüm + derleme + kurulum paketi + GitHub Release
```

Kurulum paketi için [Inno Setup 6](https://jrsoftware.org/isinfo.php) gerekir (`installer\SesliOkuma.iss`; Hintçe için resmi olmayan `Hindi.islu` dil dosyası Inno'nun `Languages` klasörüne kopyalanmalıdır).

## Lisans

MIT — bkz. [LICENSE.txt](LICENSE.txt).

---

## English

**Sesli Okuma** ("Read Aloud" in Turkish) is a small Windows 10/11 tray tool: **select text in any application and press a shortcut** (default `Ctrl + Alt + S`) to hear it read aloud; press again to stop. Click the tray icon for a minimal panel with the shortcut, voices per language, reading speed and interface language.

- **8 interface languages** — Türkçe, English, 中文, हिन्दी, Español, Français, العربية, Português; switch instantly from the panel. The installer speaks the same languages.
- **Your shortcut** — click the field and press a new combination; conflicts are reported and the old shortcut kept.
- **Voice per language** — the text's script/language is detected; primary-language text uses your primary voice, everything else the second voice. Multilingual neural voices appear in both lists.
- **Natural voices in one click** — the panel offers to install the open-source [NaturalVoiceSAPIAdapter](https://github.com/gexgd0419/NaturalVoiceSAPIAdapter), which adds Microsoft's online neural voices for many languages (internet required). Windows voice packs can be added from the panel too.
- **Auto-update** — checks GitHub Releases once a day, shows the new version in the panel, downloads with SHA-256 verification, installs silently and restarts (can be turned off).
- **About & support** — the *About* card (under the language menu) links to source, release notes, issues and an optional **Support ♥** button (GitHub Sponsors, one-time).
- Per-user install, no admin rights; **no data collection** — text goes only to the Windows speech provider (SAPI) you choose.

Install: download the latest `SesliOkuma-Setup-x.y.z.exe` from [Releases](https://github.com/korayorhun/SesliOkuma/releases). The package is unsigned, so SmartScreen may warn: *More info → Run anyway*. Build from source with `build.ps1` (only the C# compiler shipped with .NET Framework 4.8 is needed). MIT licensed.