<div align="center">
  <img src="https://raw.githubusercontent.com/muhammertasci11/Dosyaktar/main/Dosyaktar/Assets/dosyaktar_icon.ico" width="128" height="128" alt="Dosyaktar Icon" onerror="this.src='https://img.icons8.com/fluent/128/000000/data-transfer.png'"/>
  
  <h1>🚀 Dosyaktar</h1>
  <p><strong>Modern, High-Speed Local Network File & Game Transfer Tool</strong></p>

  <p>
    <img src="https://img.shields.io/badge/.NET-8.0-5C2D91?style=for-the-badge&logo=.net&logoColor=white" alt=".NET 8.0">
    <img src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white" alt="C#">
    <img src="https://img.shields.io/badge/WPF-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="WPF">
    <img src="https://img.shields.io/github/v/release/muhammertasci11/Dosyaktar?style=for-the-badge&color=2DD4BF" alt="Release">
  </p>
</div>

---

## 📖 About The Project

**Dosyaktar** is a modern, high-performance desktop application designed to securely transfer files, folders, and installed games between devices on the same local network (Wi-Fi or Ethernet) without using any internet quota. 

Featuring an elegant, custom borderless UI inspired by modern design principles, Dosyaktar provides a seamless "AirDrop" or "ShareIt" style experience for Windows PCs.

### ✨ Key Features

*   🔍 **Auto-Discovery:** Instantly detects other devices running Dosyaktar on your network within 2 seconds using UDP Broadcasts.
*   🔒 **PIN-Protected Pairing:** Securely connect devices using an automatically generated 6-digit PIN code.
*   ⚡ **Ultra-Fast Transfers:** Utilizes TCP protocol to transfer data at the maximum speed supported by your network hardware (e.g., Gigabit LAN).
*   🎮 **Game Transfer Module:** Automatically scans installed games (via Registry & Folders) and transfers them entirely to the target device.
*   🔄 **Active Session Architecture:** Once paired, simply drag & drop files/folders into the active session window for instant transmission.
*   🎨 **Dynamic UI/UX:** Built with WPF, featuring fluid animations, dark/light themes, and custom rounded-corner panels.
*   📡 **Built-in Auto Updater:** Seamlessly checks for new GitHub releases and automatically applies updates in the background.

---

## 🛠️ Technical Architecture

The application handles network communication asynchronously across three dedicated ports to ensure maximum stability and speed:

1.  **UDP 5002 (Discovery):** Broadcasts signals every 2 seconds to locate peers on the local network (`NetworkDiscovery.cs`).
2.  **TCP 5003 (Pairing):** Handles the handshake and 6-digit secure PIN verification between two devices (`PairingService.cs`).
3.  **TCP 5001 (Data Transfer):** Dedicated high-bandwidth pipeline for transferring heavy payloads, files, and game directories (`FileTransferService.cs`).

---

## 🚀 How to Use

1.  **Launch:** Open Dosyaktar on both the sender and receiver PCs connected to the same network.
2.  **Discover:** Navigate to the **Keşif (Discovery)** tab. The other device will appear as a card automatically.
3.  **Pair:** Click on the target device. A 6-digit PIN will appear on the sender's screen. The receiver must click **"Accept" (Onayla)**.
4.  **Transfer:** Once the active session starts, drag and drop any file, video, or folder into the central designated drop zone.
5.  **Track:** Monitor real-time transfer progress, speed, and status in the right-side Queue Panel.

---

## 💻 Tech Stack & Requirements

*   **Framework:** .NET 8.0
*   **UI Technology:** WPF (Windows Presentation Foundation)
*   **Architecture:** Async TCP/UDP Sockets, MVVM (partial)
*   **OS Compatibility:** Windows 10 / Windows 11 (x64)
*   **Deployment:** Compiled as a Self-Contained single executable.

---

## 📜 License & Author

Developed with ❤️ by **[Muhammer Taşçı](https://github.com/muhammertasci11)**.  
*Mechatronics Engineering Student | Software & Automation Developer*

<div align="center">
  <i>"Redefining local area network transfers with elegance and speed."</i>
</div>
