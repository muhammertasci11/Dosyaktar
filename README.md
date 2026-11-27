<div align="center">
  <img src="Dosyaktar/Assets/icon.png" width="96" height="96" alt="Dosyaktar Icon"/>
  <h1>Dosyaktar</h1>
  <p><strong>Aynı ağda ışık hızında dosya, klasör ve oyun transferi</strong></p>
  <br/>

  <p>
    <a href="https://github.com/muhammertasci11/Dosyaktar/releases/latest">
      <img src="https://img.shields.io/github/v/release/muhammertasci11/Dosyaktar?include_prereleases&style=for-the-badge&logo=github" alt="Latest Release"/>
    </a>
    <a href="https://github.com/muhammertasci11/Dosyaktar/releases/download/v2.2.1/Dosyaktar.exe">
      <img src="https://img.shields.io/badge/Download-Dosyaktar.exe-blue?style=for-the-badge&logo=windows" alt="Download v2.2.1"/>
    </a>
    <img src="https://img.shields.io/badge/.NET-8.0-5C2D91?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8.0">
    <img src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white" alt="C#">
    <img src="https://img.shields.io/badge/WPF-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="WPF">
    <img src="https://img.shields.io/badge/Windows-10%2F11-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Windows">
  </p>
</div>

<br/>

<div align="center">
  <img src="landing-full-desktop.png" width="720" alt="Dosyaktar masaüstü arayüzü"/>
  <p><i>Dosyaktar ana penceresinde oyun kütüphanesi ve mobil akış</i></p>
</div>

<br/>

<details open>
  <summary><b>📸 Ekran görüntüleri</b></summary>
  <br/>
  <div align="center">
    <table>
      <tr>
        <td><b>Ana ekran</b></td>
        <td><b>Oyun kitaplığı</b></td>
      </tr>
      <tr>
        <td>
          <img src="landing-anchor-desktop.png" width="340" alt="Ana ekran"/>
        </td>
        <td>
          <img src="landing-oyunlar-desktop.png" width="340" alt="Oyun kitaplığı"/>
        </td>
      </tr>
      <tr>
        <td>
          <img src="landing-oyunlar-mobile.png" width="220" alt="Mobil oyun kitaplığı"/>
        </td>
        <td>
          <img src="landing-anchor-mobile.png" width="220" alt="Mobil ana ekran"/>
        </td>
      </tr>
    </table>
  </div>
</details>

<br/>

## 🚀 Hakkında

**Dosyaktar**, aynı yerel ağdaki (Wi-Fi veya Ethernet) Windows bilgisayarlar arasında dosya, klasör ve **kurulu oyunları** internet kotası harcamadan taşıyan, kurulum gerektirmeyen masaüstü uygulamasıdır. Bulut yok, hesap yok, reklam yok.

Sürükle-bırak kadar basit bir akış: cihazı seç, PIN'i onayla, dosyaları bırak. Arka planda 8 MB'lik parçalar hâlinde, **32 paralel TCP kanalıyla** aktarım yapılır.

### Neden Dosyaktar?

| Özellik | Avantaj |
|---------|---------|
| **İnternet gerektirmeyen** | Yerel ağda doğrudan cihazdan cihaza aktarım |
| **Oyun desteği** | Steam, Epic, GOG, Battle.net, Ubisoft, EA, Riot kitaplıklarını tarar ve aktarır |
| **Tek dosya** | `win-x64` self-contained tek `.exe` — kurulum yok |
| **Mobil köprü** | Telefon tarayıcısı üzerinden dosya gönderme/alma (kurulum gerektmez) |
| **Flash Mode** | 8 MB parçalar, 32 paralel kanal |
| **8 tema** | WCAG AA kontrastlı tema paleti |

<br/>

## 📦 Kurulum

### İndirin

[**Dosyaktar.exe v2.2.1 indir**](https://github.com/muhammertasci11/Dosyaktar/releases/download/v2.2.1/Dosyaktar.exe) (77,25 MB)

### Nasıl kullanılır

1. **Başlat:** Dosyaktar'ı aynı ağdaki iki bilgisayarda da açın.
2. **Keşfet:** Karşı cihaz otomatik olarak belirir.
3. **Eşleştir:** Hedef cihaza tıklayın ve 6 haneli PIN'i onaylayın.
4. **Aktar:** Dosya/klasör bırakın ya da Oyunlar sayfasından bir oyun seçip taşıyın.
5. **Telefon bağlan:** Mobil sayfasındaki QR kodu okutun veya adresi açın, PIN'i girin.

> **Not:** Her iki bilgisayarda da aynı sürümün çalışması gerekir. İlk çalıştırmada Windows Firewall kuralı otomatik olarak eklenir.

<br/>

## ✨ Özellikler

### Hızlı transfer (Flash Mode)
Dosyalar 8 MB'lik parçalara bölünür ve 32 paralel TCP kanalı üzerinden eşzamanlı taşınır. Büyük dosyalarda G/Ç darboğazı oluşmaz.

### Oyun transferi
7 sağlayıcıyı destekler: **Steam, Epic Games, GOG, Battle.net, Ubisoft Connect, EA App, Riot Client.** Kayıt defteri ve manifest dosyaları üzerinden kurulu oyunlar bulunur; hedef makinede oyun kitaplığınıza eklenir.

### Mobil köprü
Masaüstü uygulama yerel ağda bir HTTP köprü açar. Telefon tarayıcınızdan adresi açın, 6 haneli PIN'i girin ve dosya gönderin/ alın. Hiçbir mobil uygulama kurulumu gerektirmez.

### Temalar ve görünüm
8 WCAG AA kontrastlı tema: Prism, Midnight, Nordic, Aurora, Sunset, Cyber, Forest, Graphite.

<br/>

## 🏗️ Mimari

| Katman | Sorumluluk |
|--------|------------|
| `Services/NetworkDiscovery.cs` | UDP 5002 — aynı ağdaki cihazları keşfet |
| `Services/PairingService.cs` | TCP 5003 — PIN eşleştirme |
| `Services/FileTransferService.cs` | TCP 5001 — 32 kanal transfer hattı |
| `Services/GameLibraryService.cs` | Sağlayıcı bazlı oyun taraması |
| `Services/GameTransferService.cs` | Oyun aktarımı ve metadata yeniden yazma |
| `Services/MobileServer.cs` | Gömülü HTTP sunucusu (5050) + mobil arayüz |
| `Services/HistoryService.cs` | Transfer geçmişi |
| `Services/UpdateService.cs` | GitHub Releases güncelleme kontrolü |
| `ThemeManager.cs` | 8 tema paleti |

Arayüz tek yüzey: `MainWindow.xaml` — Keşif, Oturum, Oyunlar, Mobil, Geçmiş, Ayarlar.

<br/>

## 🛠️ Teknoloji

- **Framework:** .NET 8.0
- **Arayüz:** WPF (özel çerçevesiz pencere)
- **Ağ:** Async TCP/UDP soketleri, 32 paralel kanal
- **Mobil:** Gömülü HTTP sunucusu + tek-sayfa mobil arayüz
- **İşletim sistemi:** Windows 10 / 11 (x64)
- **Dağıtım:** Self-contained tek dosya

<br/>

## 📂 Dosya yapısı

```
Dosyaktar/
├── Dosyaktar/                 # WPF proje kaynağı
│   ├── Views/                 # XAML arayüzleri
│   ├── Services/              # Ağ, oyun, tema, geçiş servisleri
│   ├── Controls/              # Özel kontroller
│   ├── Themes/                # Tema paletleri
│   └── Assets/                # İkonlar ve varlıklar
├── Assets/mobile/             # Mobil web arayüzü
├── docs/                      # GitHub Pages dokümanları
└── README.md
```

<br/>

## 📄 Lisans ve geliştirici

Geliştirici: **[Muhammer Taşçı](https://github.com/muhammertasci11)**  
*Mekatronik Mühendisliği Öğrencisi | Yazılım ve Otomasyon Geliştirici*

> "Yerel ağ transferini zarafet ve hızla yeniden tanımlıyor."

<br/>

<div align="center">
  <a href="https://github.com/muhammertasci11/Dosyaktar/releases/tag/v2.2.1">
    <img src="https://img.shields.io/badge/Changelog-v2.2.1-success?style=for-the-badge&logo=github" alt="Changelog"/>
  </a>
</div>
