# Dosyaktar - Yerel Ağ Dosya ve Oyun Aktarım Aracı

Dosyaktar, aynı ağ üzerinde (Wi-Fi veya Ethernet) bulunan cihazlar arasında yüksek hızda, güvenli ve internet kotası harcamadan dosya ile oyun transferi yapmanızı sağlayan yerel ağ (LAN) tabanlı bir masaüstü uygulamasıdır. 

Modern ve kullanıcı dostu arayüzü sayesinde ShareIt, AirDrop veya Bluetooth tarzı bir eşleşme deneyimi sunar.

---

## 🚀 Temel Özellikler

- **PIN Korumalı Eşleşme:** İki cihazın birbirine bağlanırken otomatik oluşturulan 6 haneli kodlarla güvenli bir şekilde eşleşmesini sağlar.
- **Aktif Oturum (Session) Mimarisi:** Cihazlar eşleştikten sonra aktif bir "Oturum" başlatılır. Bu oturum boyunca dosyaları hedefe tek tek seçmeden, yalnızca sürükleyip bırakarak anında gönderebilirsiniz.
- **Oyun Aktarım Modülü (Game Transfer):** Sisteminizde kurulu olan oyunların kayıt defteri (Registry) veya dosya konumlarını tarayarak diğer cihazlara bütün olarak gönderilmesini sağlar.
- **Otomatik Cihaz Keşfi (Discovery):** Uygulama açıldığı an aynı ağdaki diğer Dosyaktar yüklü cihazları (UDP Broadcast kullanarak) 2 saniye içinde tespit eder ve listeler.
- **Yüksek Hızlı Aktarım:** TCP protokolü üzerinden, cihazların ağ kartı kapasitesinin (Örn. Gigabit LAN) desteklediği maksimum hızda veri aktarımı sağlar.

---

## 🎨 Arayüz (UI) Detayları

Dosyaktar arayüzü, üç ana sütundan (Kolondan) oluşacak şekilde tasarlanmıştır:

### 1. Sol Panel (Navigasyon & Menü)
Uygulamanın ana menülerini barındırır:
- **Keşif (Discovery):** Ağdaki diğer cihazları arayıp bulduğunuz ekrandır.
- **Aktif Oturum (Active):** Eşleşme sağlandığında açılan ve anlık dosya aktarımı yapılan canlı gösterge panelidir.
- **Geçmiş (History):** Gönderilen veya alınan dosyaların kaydını tutar.
- **Oyun Aktar (Games):** Bilgisayardaki oyunları tarayıp göndermenizi sağlayan özel modüldür.
- **Ayarlar (Settings):** Cihaz adını değiştirme, karanlık/aydınlık tema ayarı, varsayılan indirme konumu ve ağ portu gibi tercihleri barındırır.

### 2. Orta Panel (Ana Çalışma Alanı)
Seçilen menüye göre değişen ana içerik alanıdır. 
- **Keşif Modu:** Ağdaki cihazlar kartlar halinde listelenir. "Listeyi Yenile" butonu bulunur. Herhangi bir cihaza tıklandığında ona eşleşme isteği (PIN ile) gönderilir.
- **Aktif Oturum Modu:** Karşılıklı eşleşme sağlandığında ortada bir "Sürükle - Bırak" alanı oluşur. Bu alana PDF, Video veya Klasör attığınız an dosya doğrudan karşı hedefe aktarılır. Sol tarafta kendi cihazınız, sağ tarafta karşı cihazınız görsel olarak eşleşmiş şekilde görünür.
- **Geçmiş Modu:** Başarılı/Başarısız transferler tarih ve saatleriyle listelenir.

### 3. Sağ Panel (Aktarım Kuyruğu)
Transfer işlemlerinin canlı durumunu gösterir.
- **TransferCard:** Bir dosya gönderilmeye veya alınmaya başladığında bu bölümde dosyanın ismi, kaç dosya olduğu ve aktarım hedefleri yazar. 
- Transferin iptal edilmesi (✕) veya canlı durumu buradan izlenir.

---

## ⚙️ Arka Plan Mimarisi (Teknik Detaylar)

Uygulamanın ağ iletişimi 3 farklı port ve teknoloji üzerinden yönetilir:

1. **UDP 5002 (Discovery - Keşif):** Ağdaki cihazların birbirini bulması için her 2 saniyede bir `NetworkDiscovery.cs` üzerinden sinyal (Broadcast) gönderilir.
2. **TCP 5003 (Pairing - Eşleşme):** "ShareIt" tarzı eşleşmeler için kullanılır. Bir cihaz diğerine bağlanmak istediğinde, cihazlar bu port üzerinden haberleşir ve 6 haneli kod doğrulamasını (`PairingService.cs`) gerçekleştirir.
3. **TCP 5001 (Data Transfer):** Dosyalar ve veriler eşleşme tamamlandıktan sonra asıl yüksek boyutlu aktarım için bu porttan gönderilir/alınır (`FileTransferService.cs`).

### WPF ve Tasarım Dili
Uygulamanın görsel tarafı C# WPF (Windows Presentation Foundation) kullanılarak geliştirilmiştir. Modern `Border`, `CornerRadius` yuvarlatmaları, `DynamicResource` tabanlı Tema sistemi ve özel animasyonlar kullanılmıştır. Uygulama, `.NET 8.0` altyapısı üzerinde çalışır ve tamamen `Single File` (.exe) olarak çalışabilecek şekilde derlenir.

---

## 🛠 Kullanım Senaryosu (Nasıl Kullanılır?)

1. **Uygulamayı Açın:** Her iki bilgisayarda uygulamayı başlatın.
2. **Cihazı Seçin:** Orta ekrandaki (Keşif) listede diğer cihazınızı göreceksiniz. Cihaz ismine tıklayın.
3. **Ekranda Çıkan PIN'i Onaylayın:** Gönderen cihazda bir eşleşme isteği oluşur. Alıcı cihazda "*Cihaz sana bağlanmak istiyor, Kod: 123456*" uyarısı çıkar. "Onayla" butonuna basın.
4. **Sürükle ve Bırak:** Onay sonrası cihazlar **Aktif Oturum** (Session) sekmesine yönlendirilir. Artık göndermek istediğiniz dosyaları ortadaki gri kutuya sürükleyip bırakmanız yeterlidir. Dosyalar anında karşı tarafa ulaşır.
5. **Geçmişi İnceleyin:** Sol panelden "Geçmiş" sekmesine tıklayarak ne gönderip ne aldığınızı görebilirsiniz.
