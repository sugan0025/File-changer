<!-- ========================================================================= -->
<!-- HOOT: FAST LOCAL FILE CONVERTER FOR WINDOWS, MACOS & LINUX                -->
<!-- ========================================================================= -->

<div align="center">

# 🦉 HOOT — Fast Local File Converter

*Convert, optimize, and transform your files in a hoot.*

*Proudly watched over by our Blobatar Owl mascot.*

<br>

<a href="https://git.io/typing-svg">
  <img src="https://readme-typing-svg.demolab.com?font=Fira+Code&weight=600&size=19&duration=3000&pause=1000&color=60A5FA&center=true&vCenter=true&repeat=true&width=750&height=45&lines=Liquid+Glass+Aesthetic+%E2%80%A2+Ambient+Backdrop+Blur+%26+Specular+Borders;Instant+Drag+%26+Drop+Donut+Wheel+%E2%80%A2+Subtle+Synthesized+Audio+Chimes;Clipboard+Auto-Detector+%E2%80%A2+Quick+Conversion+Notification+HUD;100%25+Offline+Local+Engine+%E2%80%A2+Zero+Telemetry+%E2%80%A2+Windows+%26+macOS" alt="HOOT Typist" />
</a>

<br><br>

<!-- Badges: Platform, Runtime, Theme & License -->
<a href="https://dotnet.microsoft.com/">
  <img src="https://img.shields.io/badge/.NET_10-Avalonia_UI-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10 Avalonia UI" />
</a>
&nbsp;
<a href="https://github.com/sugan0025/File-changer/releases">
  <img src="https://img.shields.io/badge/Platform-Windows_%7C_macOS_%7C_Linux-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Windows & macOS" />
</a>
&nbsp;
<a href="https://github.com/sugan0025/File-changer/tree/release-binaries">
  <img src="https://img.shields.io/badge/Theme-Liquid_Glass_Design-38BDF8?style=for-the-badge&logo=css3&logoColor=white" alt="Liquid Glass Design" />
</a>
&nbsp;
<img src="https://img.shields.io/badge/Privacy-100%25_Offline_Local-16A34A?style=for-the-badge&logo=shield&logoColor=white" alt="100% Offline Local" />
&nbsp;
<a href="./LICENSE">
  <img src="https://img.shields.io/badge/License-MIT-F59E0B?style=for-the-badge&logo=open-source-initiative&logoColor=white" alt="MIT License" />
</a>

<br><br>

<p align="center">
  <b>A modern, high-performance offline desktop file converter engineered with a custom frosted Liquid Glass interface, synthesized audio chimes, clipboard auto-detection, and native multiplatform support for Windows, macOS, and Linux. Proudly watched over by our Blobatar Owl mascot.</b>
</p>

[📥 Downloads](#-pre-built-downloads) &nbsp;•&nbsp; [✨ Features](#-features) &nbsp;•&nbsp; [🎯 How It Works](#-how-it-works) &nbsp;•&nbsp; [🚀 Quick Start](#-quick-start) &nbsp;•&nbsp; [📁 Architecture](#-project-architecture) &nbsp;•&nbsp; [🛠 Tech Stack](#-tech-stack) &nbsp;•&nbsp; [👤 Author & Credits](#-author--credits)

</div>

---

## 📋 Overview

**HOOT** brings effortless, private, and offline conversion directly to your mouse cursor. Hold <kbd>Ctrl</kbd> on Windows or <kbd>Option</kbd> on macOS, drag any file, and release it over an interactive donut menu wheel to convert documents, images, video, and audio instantly. In a hoot!

```yaml
Application Status   : Production Ready (Windows x64 · macOS Apple Silicon · Linux x64)
Target Framework     : .NET 10 (C# 13 · Avalonia UI Cross-Platform)
Visual Design System : Frosted Liquid Glass (Ambient Vignette · Translucent Cards · Gear 5 Art)
Audio Feedback       : Synthesized Audio Ticks (Unmanaged Win32 Pinned Buffers · macOS afplay)
Clipboard Pipeline   : Zero-Delay Sequence Watcher · HUD Toast Notification · 1-Click Convert
Privacy & Security   : 100% Offline Local Processing · Zero Network Telemetry · No Cloud Uploads
```

---

## 📥 Pre-built Downloads

Download standalone portable archives for your platform (no .NET SDK or installation required — extract and run immediately):

| Platform | Download Link | Target Systems | Package Size |
| :--- | :--- | :--- | :--- |
| **Windows** | [**Download for Windows (x64)**](https://github.com/sugan0025/File-changer/raw/release-binaries/binaries/File-changer-win-x64.zip) | Windows 10 & 11 (64-bit) | ~80 MB |
| **macOS (Apple Silicon)** | [**Download for macOS (Apple Silicon)**](https://github.com/sugan0025/File-changer/raw/release-binaries/binaries/File-changer-osx-arm64.zip) | M1, M2, M3, M4 Macs | ~78 MB |
| **Linux** | [**Download for Linux (x64)**](https://github.com/sugan0025/File-changer/raw/release-binaries/binaries/File-changer-linux-x64.tar.gz) | Ubuntu, Debian, Fedora, Arch (64-bit) | ~82 MB |

> You can also browse all packages on the dedicated [**`release-binaries`**](https://github.com/sugan0025/File-changer/tree/release-binaries/binaries) branch or explore release tags under [**Releases**](https://github.com/sugan0025/File-changer/releases).

---

## ✨ Features

- **Liquid Glass Interface**: Translucent acrylic panels, specular borders, frosted card gradients, and Luffy Gear 5 / Sun God Nika artwork backdrop.
- **Audio & Haptic Chimes**:
  - Subtle hover tick sound when gliding across wheel formats.
  - Smooth whoosh sound when turning or flipping the donut slice face.
  - Pleasant chime notification upon conversion completion.
  - Seamlessly powered on Windows via pinned unmanaged audio memory and on macOS via native system sound players.
- **Clipboard Auto-Detector**:
  - Automatically identifies copied images and files on the system clipboard.
  - Non-intrusive bottom-right HUD popup to convert clipboard contents with 1 click.
- **Radial Donut Menu Wheel**:
  - Front-face shortcuts for **BG Remove** (transparent PNG generator) and **Extract Audio** (instant MP3/M4A extraction).
  - Multi-page donut slices: tap <kbd>Tab</kbd> while hovering to flip between front and back tool profiles.
- **All-in-One Format Support**:
  - **Images**: PNG, JPG, WEBP, ICO (multi-resolution), AVIF, BMP, TIFF, SVG vectorizer, and background removal.
  - **Documents**: PDF, DOCX, XLSX, ODT, HWPX, Markdown, EPUB, TXT, HTML.
  - **Video & Audio**: MP4, MKV, WebM, GIF, MP3, WAV, M4A, subtitle extractions.
- **Complete Privacy**: Everything processes locally on your machine with zero server calls.

---

## 🎯 How It Works

1. **Grab any file** in File Explorer or macOS Finder.
2. **Hold <kbd>Ctrl</kbd>** (Windows) or **<kbd>Option</kbd>** (macOS) and start dragging the file.
3. The **Liquid Glass Donut Wheel** opens under your cursor.
4. Drop the file on your target format slice (e.g. `PNG`, `BG Remove`, `PDF`, `MP4`, or `ICO`).
5. Your converted file appears right next to the original in milliseconds!

---

## 🚀 Quick Start

### ⚡ 1-Click Install & Update for Windows (Recommended)

Share this single PowerShell command line with your friends to install **HOOT** or instantly upgrade an older installation to the latest version. It sets modern TLS security so it works on all versions of Windows PowerShell:

```powershell
[Net.ServicePointManager]::SecurityProtocol = 3072; irm https://raw.githubusercontent.com/sugan0025/File-changer/main/install.ps1 | iex
```

> **Alternative (using built-in `curl.exe` in CMD / PowerShell):**
> ```powershell
> curl.exe --ssl-no-revoke -fsSL https://raw.githubusercontent.com/sugan0025/File-changer/main/install.ps1 | iex
> ```

#### What this command line does:
- 🔄 **Replaces Old Versions**: If you or your friends have an older installation where the update button says *"You're up to date"*, this command gracefully terminates the old process and upgrades everything to the latest release (`v2.4.6`).
- 🦉 **Applies HOOT Branding**: Installs the custom HOOT icon, frosted Liquid Glass themes, and Blobatar Owl mascot assets.
- 🚀 **Enables In-App Updates**: Automatically writes the local `.installed` marker so future automatic background updates check and pull directly from `sugan0025/File-changer`.

---

### 🛠️ Building from Source

If you prefer building from source, you only need the [.NET 10 SDK](https://dotnet.microsoft.com/download):

```bash
# 1. Clone the repository
git clone https://github.com/sugan0025/File-changer.git
cd File-changer

# 2. Run the application
dotnet run --project src/Filee.App

# 3. (Optional) Publish a standalone portable build
dotnet publish src/Filee.App -c Release -r win-x64 --self-contained -o publish
```

---

## 📁 Project Architecture

```
File-changer/
├── src/
│   ├── Filee.App/                # Avalonia desktop shell (views, controls, liquid glass theme)
│   │   ├── Assets/               # Icons, Luffy Gear 5 wallpapers, vector marks
│   │   ├── Controls/             # DonutMenu radial wheel rendering and slice geometry
│   │   ├── Services/             # Audio engine, clipboard watcher, window coordinator
│   │   ├── Styles/               # Soft.axaml (Liquid Glass brushes, card & button styles)
│   │   └── Views/                # Modern settings window, notice popups, HUD
│   ├── Filee.Core/               # Core domain models, profiles, settings schemas, job queues
│   ├── Filee.Engines/            # Media engines (Magick, FFmpeg, PDF, LibreOffice integrations)
│   ├── Filee.Platform.Windows/   # Win32 platform services, cursor hooks, clipboard interop
│   ├── Filee.Platform.MacOS/     # macOS Cocoa platform hooks, NSPasteboard, Finder integration
│   └── Filee.Platform.Linux/     # Linux Wayland/X11 platform services
├── tests/                        # Comprehensive unit and integration test suites
└── Releases/                     # Release configuration and portable distributions
```

---

## 🛠 Tech Stack

| Layer | Technology |
|---|---|
| **Runtime & Language** | .NET 10 · C# 13 (Latest language features) |
| **Desktop UI Framework** | Avalonia UI 11.2 (Cross-platform XAML) |
| **Architecture Pattern** | MVVM with CommunityToolkit.Mvvm |
| **Design System** | Custom Liquid Glass Theme (Translucent Brushes, Specular Edge Gradients) |
| **Image & Graphics Engine** | Magick.NET, SkiaSharp |
| **Document & Conversion** | Pandoc, Tesseract OCR, PDFtoImage |
| **Platform Interop** | Win32 API (`winmm.dll`, `user32.dll`) · macOS AppKit / ObjC Runtime |

---

## 🔒 Privacy & Local Processing

- **No Remote Telemetry**: File-changer performs **zero** analytics pings and transmits **no** data over the internet.
- **Isolated Execution**: All conversion processes run locally on your hardware.
- **Safe Clipboard Access**: The clipboard watcher reads only metadata sequences and clears immediately after user interaction.

---

<!-- sponsors: src/Filee.App/Assets/Sponsors/sponsors.json -->

## 👤 Author & Credits

Engineered and customized by **Sugan S** ([@sugan0025](https://github.com/sugan0025)).

Based on the original [Filee](https://github.com/KnifeLemon/Filee) project by **Myeonggu Kim** ([KnifeLemon](https://github.com/KnifeLemon)) and contributors, licensed under the MIT License.

<br>

<div align="center">

[![GitHub Profile](https://img.shields.io/badge/GitHub-sugan0025-181717?style=for-the-badge&logo=github&logoColor=white)](https://github.com/sugan0025)
&nbsp;
[![LinkedIn Profile](https://img.shields.io/badge/LinkedIn-Connect-0A66C2?style=for-the-badge&logo=linkedin&logoColor=white)](https://www.linkedin.com/in/sugan0025)

<br><br>

<b>MIT License © 2026 Sugan S & Filee contributors.</b>

</div>
