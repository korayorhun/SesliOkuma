# Sesli Okuma

Windows 11 için küçük bir araç: **herhangi bir uygulamada seçtiğiniz metni `Ctrl + Alt + S` ile sesli okur**, tekrar basınca susar. Sistem tepsisinde bir konuşma balonu ikonuyla durur; ikona tıklayınca açılan minimal panelden Türkçe ve diğer diller için ayrı ses seçebilir, okuma hızını ayarlayabilir ve Windows ile başlatmayı açıp kapatabilirsiniz.

- Kurulum yönetici yetkisi istemez (kullanıcı profilinize kurulur).
- Veri toplamaz; okunan metin sadece seçtiğiniz Windows ses sağlayıcısına (SAPI) gider.
- Ayarlar ve günlük: `%LOCALAPPDATA%\SesliOkuma\`

<p align="center"><img src="docs/panel.png" width="412" alt="Sesli Okuma ayar paneli"></p>

## Kurulum

[Releases](https://github.com/korayorhun/SesliOkuma/releases) sayfasından en son `SesliOkuma-Setup-x.y.z.exe` dosyasını indirip çalıştırın. İmzasız bir program olduğu için Windows SmartScreen "tanınmayan uygulama" uyarısı verebilir: *Ek bilgi → Yine de çalıştır*.

## Kullanım

| Eylem | Nasıl |
|---|---|
| Seçili metni oku | metni seç → `Ctrl + Alt + S` |
| Sustur | okurken tekrar `Ctrl + Alt + S` (veya tepsi ikonu → Sustur) |
| Ayar paneli | tepsi ikonuna tıkla (veya exe'yi tekrar çalıştır) |
| Çıkış | tepsi ikonu → sağ tık → Çıkış |

Metin ilk olarak UI Automation ile okunur; bunu desteklemeyen uygulamalarda otomatik olarak kopyalama (Ctrl+C) yoluna düşer ve panonuzu eski hâline geri koyar.

## Doğal sesler (isteğe bağlı)

Windows'un yerleşik SAPI sesleri (Tolga, David, Zira…) robotiktir. İnsan gibi konuşan sesler için açık kaynak **[NaturalVoiceSAPIAdapter](https://github.com/gexgd0419/NaturalVoiceSAPIAdapter)** projesini kurabilirsiniz; ardından panelde *Emel, Ahmet, Andrew, Aria…* gibi sesler görünür. Bu sesler Microsoft'un çevrimiçi servisiyle çalışır (internet gerekir). Adapter bu projeden bağımsızdır ve bu pakete dahil değildir; kendi lisans ve kullanım koşullarına tabidir.

## Kaynaktan derleme

Ek araç gerekmez; .NET Framework 4.8 ile gelen C# derleyicisi yeterlidir:

```powershell
.\build.ps1            # src\*.cs -> SesliOkuma.exe (ve başlatır)
```

Kurulum paketi için [Inno Setup 6](https://jrsoftware.org/isinfo.php) gerekir:

```powershell
iscc installer\SesliOkuma.iss   # -> dist\SesliOkuma-Setup-x.y.z.exe
```

## Lisans

MIT — bkz. [LICENSE.txt](LICENSE.txt).