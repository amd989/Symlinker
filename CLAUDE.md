# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Symlinker is a Windows Forms application (.NET 8) that provides a GUI for creating symbolic links, hard links, and directory junctions on Windows. It wraps the Windows `mklink` command with an easy-to-use interface.

**Target Platform:** Windows 10+ only (as of 2025 modernization)

## Build & Development Commands

### Building the Application

```powershell
# Build in Debug configuration
dotnet build "Symlinker/Symlinker.csproj" --configuration Debug

# Build in Release configuration
dotnet build "Symlinker/Symlinker.csproj" --configuration Release
```

### Publishing (ClickOnce)

The application uses ClickOnce deployment and is published to GitHub Pages:

```powershell
# Full build and publish to gh-pages branch
./release.ps1

# Build only (skip deployment)
./release.ps1 -OnlyBuild
```

The `release.ps1` script:
- Reads the Git tag to determine version (e.g., `v1.2.3` becomes `1.2.3.0`)
- Uses MSBuild to publish with ClickOnce profile
- Deploys to `gh-pages` branch for auto-update support

### Running the Application

```powershell
dotnet run --project "Symlinker/Symlinker.csproj"
```

## Architecture

### Key Components

1. **Program.cs**: Entry point with UAC elevation logic
   - Automatically re-launches itself with administrator privileges using the `--engage` flag
   - Uses Windows API (`BCM_SETSHIELD`) to display UAC shield icon

2. **MainWindow.cs**: Main form and business logic
   - Handles UI state for file vs. folder symlink modes
   - Manages three link types: Symbolic Link, Hard Link, Directory Junction
   - Executes `cmd.exe /c mklink` with appropriate parameters
   - Supports drag-and-drop for file/folder paths

3. **MainWindow.Designer.cs**: Auto-generated Windows Forms designer code
   - Contains all UI control initialization

### Link Creation Flow

1. User selects link type (file/folder) via `typeSelectorComboBox`
2. `Switcher()` method updates UI labels and available link types
3. User inputs link location, link name, and destination
4. `CreateLink()` validates paths and checks for conflicts
5. `SendCommand()` builds and executes mklink command with proper escaping
6. Process output/error handlers display results via MessageBox

### Important Patterns

- **Command Building**: Uses `string.Format()` with `CultureInfo.InvariantCulture` for mklink command construction
- **Path Handling**: Wraps paths in quotes to support spaces
- **Link Type Mapping**:
  - Symbolic Link: `/D` (directory) or empty (file)
  - Hard Link: `/H`
  - Directory Junction: `/J` (folders only)

### Dependencies

Managed via Central Package Management ([Directory.Packages.props](Directory.Packages.props)):

- **Microsoft-WindowsAPICodePack-Core** (1.1.5): Common file dialogs
- **Microsoft-WindowsAPICodePack-Shell** (1.1.5): Shell integration

## Project Structure

```
Symlinker/
├── Symlinker/          # Main project directory
│   ├── MainWindow.cs         # Core form logic
│   ├── MainWindow.Designer.cs # UI designer code
│   ├── MainWindow.resx       # Form resources
│   ├── Program.cs            # Entry point with UAC elevation
│   ├── Properties/
│   │   ├── Resources.Designer.cs  # Localized strings
│   │   └── Settings.Designer.cs   # App settings
│   ├── icon.ico              # Application icon
│   └── Symlinker.csproj
├── Symlinker.sln             # Solution file
├── Directory.Packages.props  # Central package versions
├── release.ps1               # ClickOnce build/publish script
└── .github/workflows/
    └── release.yml           # GitHub Actions for tagged releases
```

## Release Process

Releases are automated via GitHub Actions:

1. Push a Git tag: `git tag v1.2.3 && git push origin v1.2.3`
2. GitHub Actions workflow ([.github/workflows/release.yml](.github/workflows/release.yml)) triggers
3. `release.ps1` executes on Windows runner
4. Application published to `gh-pages` branch
5. ClickOnce installer auto-updates existing installations

## Known Limitations

- **Drag & Drop**: Does not work when running as administrator (Windows security limitation)
- **UAC Required**: Creating symlinks requires administrator privileges on Windows
- **Platform**: Windows 10/11 only (uses .NET 8 Windows-specific APIs)

## Localization

UI strings are managed through resource files:
- [Symlinker/Properties/Resources.Designer.cs](Symlinker/Properties/Resources.Designer.cs)
- [Symlinker/MainWindow.resx](Symlinker/MainWindow.resx)

When modifying UI text, update the resource files rather than hardcoding strings.
