<div align="center">
  <img src="Dosyaktar/Assets/icon.png" width="128" height="128" alt="Dosyaktar Icon"/>
  <h1>Dosyaktar</h1>
  <p><strong>Aynı ağda ışık hızında dosya, klasör ve oyun transferi</strong></p>

  <p>
    <img src="https://img.shields.io/badge/.NET-8.0-5C2D91?style=for-the-badge&logo=.net&logoColor=white" alt=".NET 8.0">
    <img src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white" alt="C#">
    <img src="https://img.shields.io/badge/WPF-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="WPF">
    <img src="https://img.shields.io/badge/version-2.1.0-6C3BFF?style=for-the-badge" alt="v2.1.0">
  </p>
</div>

---

## Hakkında

**Dosyaktar**, aynı yerel ağdaki (Wi-Fi veya Ethernet) Windows bilgisayarlar arasında dosya, klasör ve **kurulu oyunları** internet kotası harcamadan taşıyan bir masaüstü uygulamasıdır. Bulut yok, hesap yok, reklam yok.

Sürükle-bırak kadar basit bir akış: cihazı seç, PIN'i onayla, dosyaları bırak. Arka planda 8 MB'lik parçalar hâlinde, **32 paralel TCP kanalıyla** aktarım yapılır.

## Öne çıkanlar

### Hızlı transfer
- **Flash Mode:** Dosyalar 8 MB'lik parçalara bölünür, 32 paralel TCP kanalı üzerinden eşzamanlı taşınır.
- **Kilitlenmesiz yazma:** Alıcı taraf `RandomAccess` ile doğrudan diske yazar; büyük dosyalarda G/Ç darboğazı oluşmaz.
- **Sıfır kurulum:** Tek dosya çalıştırılabilir (`win-x64`, self-contained).

### Oyun kitaplığı ve oyun transferi — *yeni*
- **7 sağlayıcı:** Steam, Epic Games, GOG, Battle.net, Ubisoft Connect, EA App, Riot Client.
- **Gerçek kitaplık taraması:** Kayıt defteri anahtarları ve sağlayıcı manifest dosyalarından kurulu oyunlar bulunur; elle klasör seçmek gerekmez.
- **Oyunu taşı, oynanabilir kalsın:** Steam `.acf` ve Epic manifest dosyaları hedef makinede yeniden yazılır; oyun hedefte kitaplıkta görünür ve açılır.
- **Tek tıkla aktarım:** Oyun, mevcut transfer hattı üzerinden taşınır — ayrı bir araç yok.

### Telefon köprüsü — *kurulum gerekmez*
- Masaüstü uygulama yerel ağda bir köprü açar; karşı ekranda **QR kod** ve adres görünür.
- Telefon tarayıcısından adres açılır, **6 haneli PIN** girilir. Uygulama kurmak gerekmez.
- **Gönder:** telefondan bilgisayara dosya/fotoğraf yükle. **Al:** bilgisayarın paylaştığı dosyaları indir. **Durum:** canlı aktarım bilgisi.
- PIN **tek kullanımlıktır** ve her başarılı girişten sonra yenilenir.
- Telefon arayüzü ayrı bir uygulama kurmadan çalışır; tarayıcı menüsünden **ana ekrana kısayol** olarak eklenebilir.
- Hiçbir dosya buluta gitmez; trafik doğrudan bilgisayara akar.

### 8 tema
Prism (parlak cam), Midnight (derin mavi), Nordic (buz mavisi), Aurora (kutup yeşili), Sunset (sıcak kum), Cyber (neon gece), Forest (orman), Graphite (saf gri).

Tüm temalar WCAG AA metin kontrastı hedefiyle üretilir; metin ve ikon renkleri `*InkBrush` / `*FillBrush` rolleri üzerinden gelir.

---

## Mimari

| Katman | Sorumluluk |
|---|---|
| `Services/NetworkDiscovery.cs` | UDP 5002 üzerinden 2 saniyede bir yayın; aynı ağdaki cihazları bulur. |
| `Services/PairingService.cs` | TCP 5003 el sıkışması ve 6 haneli PIN doğrulaması. |
| `Services/FileTransferService.cs` | TCP 5001 veri hattı; Flash Mode'da 32 işçi kanalı yönetir. |
| `Services/GameLibraryService.cs` | Sağlayıcı bazlı kitaplık taraması (Steam, Epic, GOG, Battle.net, Ubisoft, EA, Riot). |
| `Services/GameTransferService.cs` | Oyun aktarımı ve hedefte sağlayıcı metadata'sının yeniden yazılması. |
| `Services/MobileServer.cs` | Gömülü HTTP sunucusu (varsayılan 5050) + `Assets/mobile/` telefon arayüzü. |
| `Services/HistoryService.cs` | Transfer geçmişi ve kalıcılığı. |
| `Services/UpdateService.cs` | GitHub Releases üzerinden güncelleme kontrolü. |
| `ThemeManager.cs` | 8 palet üretir, `MergedDictionaries[0]` üzerinden uygular. |

Arayüz tek yüzeydir: `MainWindow.xaml` içindeki Keşif, Oturum, Oyunlar, Mobil, Geçmiş ve Ayarlar sayfaları.

---

## Kullanım

1. **Başlat:** Dosyaktar'ı aynı ağdaki iki bilgisayarda da aç.
2. **Keşfet:** Karşı cihaz kartı otomatik olarak belirir.
3. **Eşleştir:** Hedef cihaza tıkla, 6 haneli PIN'i karşı tarafta onayla.
4. **Aktar:** Oturum sayfasına dosya/klasör bırak ya da **Oyunlar** sayfasından bir oyun seçip taşı.
5. **Telefondan bağlan:** **Mobil** sayfasındaki QR kodu okut veya adresi aç, PIN'i gir.

---

## Teknoloji ve gereksinimler

- **Framework:** .NET 8.0
- **Arayüz:** WPF (özel çerçevesiz pencere, veri odaklı temalar)
- **Ağ:** Async TCP/UDP soketleri, 32 paralel kanal
- **Mobil:** Gömülü HTTP sunucusu + telefon tarayıcısında çalışan tek sayfalık arayüz (kurulum gerekmez)
- **İşletim sistemi:** Windows 10 / 11 (x64)
- **Dağıtım:** Self-contained tek dosya

### Derleme

```powershell
cd Dosyaktar
dotnet build -c Debug --nologo
```

Çıktı: `Dosyaktar/bin/Debug/net8.0-windows/win-x64/Dosyaktar.exe`

### Yayınlama

```powershell
cd Dosyaktar
dotnet publish -c Release --nologo
```

---

## Ayarlar ve veriler

- Ayarlar: `%APPDATA%\Dosyaktar\settings.json`
- Geçmiş: aynı klasörde, uygulama içindeki **Geçmiş** sayfasından yönetilir.
- Mobil köprü varsayılan olarak **5050** portunu kullanır.

---

## Lisans ve geliştirici

Geliştirici: **[Muhammer Taşçı](https://github.com/muhammertasci11)**  
*Mekatronik Mühendisliği Öğrencisi | Yazılım ve Otomasyon Geliştirici*

<div align="center">
  <i>"Yerel ağ transferini zarafet ve hızla yeniden tanımlıyor."</i>
</div>
