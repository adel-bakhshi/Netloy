<div align="center">

<img src="assets/netloy.1024x1024.png" alt="Netloy Logo" width="200"/>

# 🚀 Netloy

**Cross-Platform .NET Application Packaging Tool**

[![License](https://img.shields.io/badge/License-AGPL--3.0-blue.svg)](https://github.com/adel-bakhshi/Netloy/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4.svg)](https://dotnet.microsoft.com/)
[![NuGet Version](https://img.shields.io/nuget/v/Netloy.svg)](https://www.nuget.org/packages/Netloy/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Netloy.svg)](https://www.nuget.org/packages/Netloy/)
[![GitHub Release](https://img.shields.io/github/v/release/adel-bakhshi/Netloy)](https://github.com/adel-bakhshi/Netloy/releases)

_Build once, deploy everywhere_

[Features](#-features) • [Installation](#-installation) • [Quick Start](#-quick-start) • [Documentation](https://github.com/adel-bakhshi/Netloy/wiki) • [Contributing](#-contributing)

</div>

---

## 📖 What is Netloy?

**Netloy** is a powerful command-line tool that automates the packaging and deployment of .NET applications across multiple platforms and formats. With a single configuration file, create professional installers and packages for Windows, Linux, and macOS without platform-specific expertise.

Say goodbye to complex build scripts and manual packaging. Netloy handles icon generation, desktop integration, AppStream metadata, and platform-specific requirements automatically.

---

## ✨ Features

### 🎯 Multi-Platform Support

- **Windows**: EXE (Inno Setup), MSI (WiX v3)
- **Linux**: DEB, RPM, AppImage, Flatpak, Pacman
- **macOS**: APP Bundle, DMG
- **Portable**: Cross-platform ZIP/TAR.GZ archives

### ⚙️ Powerful Configuration

- Single `.netloy` configuration file for all platforms
- Macro system for dynamic templating (`${APP_VERSION}`, `${PUBLISH_OUTPUT_DIRECTORY}`, etc.)
- Post-publish scripts (Bash, PowerShell, Batch)
- Configuration upgrade tool for version compatibility

### 🎨 Automated Asset Management

- **Icon generation**: Provide one PNG (1024×1024), get all required sizes automatically
- Multi-format icon support: PNG, SVG, ICO, ICNS
- AppStream metadata generation for Linux software centers

### 🖥️ Desktop Integration

- Desktop shortcuts and Start Menu entries
- File associations and context menu integration
- Terminal command support (`StartCommand`)
- Platform-specific category mapping

### 🔧 Build Flexibility

- Supports both .NET Core and .NET Framework
- Custom `dotnet publish` arguments
- Direct binary packaging (skip build, use existing binaries)
- Clean build artifacts automatically

---

## 📦 Installation

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or later

### Install via .NET Tool

dotnet tool install --global Netloy

### Verify Installation

netloy --version

---

## 🚀 Quick Start

### 1. Create a Configuration File

Navigate to your .NET project directory and create a new configuration:

netloy --new conf

This generates a `.netloy` file with default settings. Edit it to customize your application metadata, packaging options, and platform-specific settings.

### 2. Build Your Package

#### Windows Installer (MSI)

netloy -t msi -r win-x64 --config-path MyApp.netloy

#### Linux DEB Package

netloy -t deb -r linux-x64 --config-path MyApp.netloy

#### macOS DMG

netloy -t dmg -r osx-arm64 --config-path MyApp.netloy

### 3. Find Your Package

Built packages are saved to the output directory specified in your configuration or via `-o` flag.

---

## 📋 Supported Package Types

| Platform | Type      | Format       | Command Flag  |
| -------- | --------- | ------------ | ------------- |
| Windows  | Installer | EXE          | `-t exe`      |
| Windows  | Installer | MSI          | `-t msi`      |
| Windows  | Portable  | ZIP          | `-t portable` |
| Linux    | Package   | DEB          | `-t deb`      |
| Linux    | Package   | RPM          | `-t rpm`      |
| Linux    | Package   | Pacman       | `-t pacman`   |
| Linux    | AppImage  | AppImage     | `-t appimage` |
| Linux    | Flatpak   | Flatpak      | `-t flatpak`  |
| Linux    | Portable  | TAR.GZ       | `-t portable` |
| macOS    | Bundle    | APP (zipped) | `-t app`      |
| macOS    | Installer | DMG          | `-t dmg`      |
| macOS    | Portable  | TAR.GZ       | `-t portable` |

---

## 🛠️ Configuration Example

Here's a minimal `.netloy` configuration:

### App Preamble

```
AppBaseName = MyApp
AppFriendlyName = My Awesome App
AppId = com.example.myapp
AppVersionRelease = 1.0.0
AppShortSummary = A simple example application
AppDescription = """
This is a longer description of my application.
It supports multiple paragraphs.
"""
AppLicenseId = MIT
```

### Publisher

```
PublisherName = Example Inc.
PublisherCopyright = Copyright © 2025 Example Inc.
PublisherLinkUrl = https://example.com
PublisherEmail = contact@example.com
```

### Desktop Integration

```
DesktopTerminal = false
PrimeCategory = Utility
IconFiles = """
Deploy/icons/app-icon.1024x1024.png
Deploy/icons/app-icon.svg
Deploy/icons/app-icon.ico
Deploy/icons/app-icon.icns
"""
AutoGenerateIcons = true
```

### .NET Publish

```
DotnetProjectPath = ./MyApp.csproj
DotnetPublishArgs = -p:Version=<APPVERSION> --self-contained true
```

### Output

```
PackageName = myapp
OutputDirectory = ./output
```

📚 For complete configuration reference, see the [Wiki](https://github.com/adel-bakhshi/Netloy/wiki).

---

## 🧪 Examples

### Build with Custom Version

netloy -t msi -r win-x64 --app-version 2.1.0 --config-path MyApp.netloy

### Build for ARM64 Architecture

netloy -t deb -r linux-arm64 --config-path MyApp.netloy --verbose

### Skip Build (Use Existing Binaries)

netloy -t appimage -r linux-x64 --binary-path ./bin/Release/net8.0/linux-x64/publish

### Create New Desktop File Template

netloy --new desktop

---

## 📚 Documentation

- **[Wiki Home](https://github.com/adel-bakhshi/Netloy/wiki)**: Comprehensive documentation
- **[Getting Started Guide](https://github.com/adel-bakhshi/Netloy/Netloy/wiki/Getting-Started)**: Step-by-step tutorial
- **[Configuration Reference](https://github.com/adel-bakhshi/Netloy/wiki/Configuration-Reference)**: All config options explained
- **[Macro System](https://github.com/adel-bakhshi/Netloy/wiki/Macro-System)**: Template variables and usage
- **[Command Line Reference](https://github.com/adel-bakhshi/Netloy/wiki/Command-Line-Reference)**: CLI arguments

---

## 🤝 Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) for details on our code of conduct and the process for submitting pull requests.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the **AGPL-3.0 License** - see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- Inspired by [PupNet Deploy](https://github.com/kuiperzone/PupNet-Deploy), though **Netloy is a complete rewrite from scratch** with expanded features and cross-platform support
- [Inno Setup](https://jrsoftware.org/isinfo.php) for Windows EXE installer generation
- [WiX Toolset](https://wixtoolset.org/) for Windows MSI package creation
- [AppImage](https://appimage.org/) for portable Linux application format
- [Flatpak](https://flatpak.org/) for sandboxed Linux application distribution

---

<div align="center">

**Made with ❤️ by [Adel Bakhshi](https://github.com/adel-bakhshi)**

⭐ If Netloy helps your project, consider giving it a star!

</div>
