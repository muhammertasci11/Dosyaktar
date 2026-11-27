# Dosyaktar — ajan talimatları

Bu depoda çalışan her ajan (insan veya model) aşağıdaki iki dosyayı **işe başlamadan önce** okur.
İkisi bu projenin kalıcı çalışma sözleşmesidir.

| Dosya | Ne için |
|---|---|
| `C:\Users\muham\Desktop\ChatGPT-Tasarim-Skili.md` | Tasarım kararları: hiyerarşi, boşluk, tema, erişilebilirlik, durum tasarımı, çıktı türüne göre yaklaşım. Her görsel/UX kararının kaynağı budur. |
| `C:\Users\muham\Desktop\ChatGPT-Bilgisayar-Kontrol-Skili.md` | Bilgisayar/browser doğrulama disiplini: **gözlemle → tek anlamlı işlem → yeni durumu doğrula**. |

## Doğrulama döngüsü (pazarlık konusu değil)

1. Uygulamayı gerçekten çalıştır, pencerenin ekran görüntüsünü al, ne göründüğünü yaz.
2. Tek bir anlamlı işlem yap (tıkla, yaz, kaydır, tema değiştir).
3. Yeni ekran görüntüsüyle sonucu doğrula; beklenen ile görünen farklıysa düzelt ve tekrar doğrula.
4. Yapılmayan tıklama, denenmemiş akış veya ölçülmemiş kontrast "tamamlandı" diye sunulmaz.
   Ölçüm aracı yoksa ölçüm uydurulmaz; neyin doğrulanamadığı açıkça yazılır.

Ekran görüntüsü/otomasyon araçları: `C:\Users\muham\AppData\Local\Temp\opencode\` (`shot.ps1`, `crop.ps1`, `tokencheck.ps1`).

## Üretim işi: Nemotron Ultra alt ajanı

Tasarım veya kod **yazdırılırken** iş, alt ajana verilir; model kimliği:

```
nvidia/nvidia/nemotron-3-ultra-550b-a55b
```

- Alt ajana dosya kapsamı, kabul ölçütü ve doğrulama komutu birlikte verilir; belirsiz "güzelleştir" isteği verilmez.
- Birbirine değmeyen dosya kümeleri paralel alt ajanlara dağıtılır (ör. `docs/` ile `Assets/mobile/` ayrı).
- Alt ajanın çıktısı burada yeniden derlenir/çalıştırılır ve ekran görüntüsüyle doğrulanır; "ajan yaptı" doğrulama yerine geçmez.

## Proje gerçekleri

- Gerçek kaynak: `Dosyaktar\` (WPF, `net8.0-windows`, win-x64, self-contained single-file). `Desktop\Dosyaktar.lnk` eski bir yayına bakar; kullanılmaz.
- Derleme: `cd Dosyaktar; dotnet build -c Debug --nologo -v q` → `0 Hata` beklenir.
- Derlemeden önce uygulamayı kapat: `Get-Process Dosyaktar -EA SilentlyContinue | Stop-Process -Force`.
- Çıktı: `Dosyaktar\bin\Debug\net8.0-windows\win-x64\Dosyaktar.exe`.
- Mobil destek: WPF içindeki gömülü HTTP sunucusu (`Services/MobileServer.cs`, varsayılan 5050) + `Assets/mobile/` telefon arayüzü. MAUI yok. Telefonun "Al" sekmesi dosyayı tarayıcı indirmesine devretmez: `fetch` akışıyla sayfa içinde alır, hız/kalan/iptal gösterir, bitince blob "Dosyayı kaydet" bağlantısı verir (256 MB üstü veya akış desteklenmiyorsa tarayıcıya devreder).
- Tema: `ThemeManager.cs` 8 palet üretir ve `MergedDictionaries[0]`'a yazar. Metin/ikon renkleri için `*InkBrush` / `*FillBrush` rolleri kullanılır; ham `AccentBrush` yalnızca dekoratif dolgularda kalır. Kontrast `tokencheck.ps1` ile ölçülür (hedef ≥ 4.5).
- Ayarlar: `%APPDATA%\Dosyaktar\settings.json`, tek yüzey `MainWindow.xaml` içindeki Ayarlar sayfası.
- Landing: `docs\` (GitHub Pages, harici bağımlılık yok). Mobil arayüz: `Dosyaktar\Assets\mobile\`.
- Başlangıç sayfası: yalnızca `RestoreLastPage()` belirler; `RbSend` varsayılanı XAML'dedir, kodda tekrar atanmaz.
- GitHub API: PowerShell 5.1 `Invoke-WebRequest` 403 döndürebilir; doğrulamayı `curl.exe` ile yap.
- Oyun kapakları: `Services/GameCoverService.cs` sağlayıcıdan bağımsız çalışır; Steam mağaza araması + herkese açık CDN, API anahtarı yok, önbellek `%LOCALAPPDATA%\Dosyaktar\covers`. Yeni AppID'lerde `header.jpg` 404 döner; aramadaki hash'li `tiny_image` kapsülü yedektir. `HttpClient` User-Agent değeri ASCII olmalı, aksi halde indirme sessizce düşer.
- Kenar çubuğu simgeleri: `Controls/NavTabButton.cs` + `Themes/NavIcons.xaml` vektör geometrileri. Hover/aktif animasyonları `SystemParameters.ClientAreaAnimation` kapalıyken çalışmaz; yeni sekme eklerken `RadioButton` yerine `NavTabButton` kullanılır.
- Yerelleştirme: `Localization/Loc.cs` (`S`, `F`, `SetLanguage`, `LanguageChanged`) + `Localization/TExtension.cs`. **Anahtar, Türkçe kaynak metnin kendisidir** — ayrı bir anahtar uzayı yok. XAML'de `{loc:T 'Dosya gönder'}`, kodda `Loc.S("...")` / `Loc.F("... {0}", x)`. Eksik metin anahtara değil Türkçeye düşer. Metin eklerken önce `Resources/strings.json`'a altı dilde girin (EN/DE/FR/ES/JA zorunlu), sonra kullanın.
- CLI (yalnızca ekran görüntüsü/QA): `--lang=xx`, `--page=games|send|settings`, `--offscreen`. `App.Ephemeral` açıkken `SettingsService.Save` hiç çalışmaz, yani `%APPDATA%` kirlenmez. Bu derlemeler `-p:ApplicationManifest=app.asinvoker.manifest` ile yapılır; yayın manifesti `app.manifest` (`requireAdministrator`) kalır.
- Tanıtım sayfası `docs/index.html` **tek dosyadır**: harici CSS/JS yok, altı dil `I18N` sözlüğünde gömülü, dil `?lang=` → `localStorage` → `navigator.language` → EN sırasıyla seçilir ve ekran görüntüleri de dille değişir. Ekran görüntüleri `docs/shots/{tr,en,de,fr,es,ja}-{games,send,settings}.jpg` (1458×950 kaynaktan 1280 genişlik, JPEG q90). README seti aynı dosyaları kullanır: `README.md` (EN) + `.tr/.de/.fr/.es/.ja.md`.

## Değişmezler

- Türkçe arayüz; metinler kısa, eylem odaklı ve kullanıcının dilinde.
- Aynı anda tek iş: bir ayar değiştiğinde ekranda seçilen değer ile çalışan işlem aynı parametreyi kullanır (§ "Her eylemden sonra ne olduğunu görünür kıl").
- Klavye odağı her etkileşimli öğede görünür olur; `IsKeyboardFocused` tetikleyicisi olmayan yeni kontrol eklenmez.
- Hareket azaltma tercihine (`SystemParameters.ClientAreaAnimation`) saygı gösterilir.
- Ölü kod ve kullanılmayan pencere bırakılmaz; tek bir ayar yüzeyi vardır.
