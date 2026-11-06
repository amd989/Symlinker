# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Symlinker is a WPF application (.NET 10) that provides a GUI for creating symbolic links, hard links, and directory junctions on Windows. It wraps the Windows `mklink` command with an easy-to-use interface.

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

### Running the Tests

```powershell
dotnet test --project Symlinker.Tests
```

- xUnit v3 on Microsoft.Testing.Platform (opted in through [global.json](global.json)), with coverage from `coverlet.MTP`
- Every run collects coverage for the `Symlinker` assembly and **fails below 100% line, branch or method coverage**. The Cobertura report lands in `TestResults/`
- Tests use the real file system under `%TEMP%`. Symbolic link tests need Developer Mode or an elevated prompt (GitHub's Windows runners are elevated)
- Code that needs a live window or UAC (`MainWindow`, `App`, `Program`, `MetroDialogService`) is marked `[ExcludeFromCodeCoverage]`. Keep logic out of those and put it in a tested class instead

## Architecture

WPF app (.NET 10) using MahApps.Metro. The window is thin view glue; the logic lives in plain classes that the tests drive with fake dialogs and a fake mklink.

### Key Components

1. **Program.cs**: Entry point. Relaunches itself elevated (`runas`) with the `--engage` flag
2. **App.xaml(.cs)**: Loads MahApps styles and syncs the theme with Windows
3. **MainWindow.xaml(.cs)**: View glue: file/folder mode, pickers, drag and drop, link name auto-fill
4. **LinkCreator.cs**: The link workflow: validation, replace-existing prompts, and moving an existing file or folder to the target (rename on the same drive, copy with progress across drives, rollback on failure)
5. **LinkMover.cs**: File system helpers for moving: copy that keeps attributes and recreates nested links, delete that never follows links
6. **Mklink.cs**: `LinkKind`, mklink argument building, and `CmdMklinkRunner` which runs `cmd /c mklink`
7. **Dialogs.cs**: `IDialogService` / `IProgressDialog`, and `MetroDialogService` which shows them in the window
8. **UiHelpers.cs**: Path name extraction, picker start folders, byte formatting, app version, `LinkNameAutoFill`

### Link Type Mapping

- Symbolic Link: `/D` (directory) or empty (file)
- Hard Link: `/H` (files only)
- Directory Junction: `/D /J` (folders only)

### Dependencies

Managed via Central Package Management ([Directory.Packages.props](Directory.Packages.props)):

- **MahApps.Metro**: Window chrome, dialogs, theming
- **MahApps.Metro.IconPacks.Material**: Icons
- **xunit.v3**, **coverlet.MTP**: Tests and coverage

## Project Structure

```
Symlinker/
├── Symlinker/                # App
│   ├── LinkCreator.cs        # Link workflow (tested)
│   ├── LinkMover.cs          # Move/copy/delete helpers (tested)
│   ├── Mklink.cs             # mklink arguments and runner (tested)
│   ├── Dialogs.cs            # Dialog abstraction + MahApps implementation
│   ├── UiHelpers.cs          # Small UI helpers (tested)
│   ├── MainWindow.xaml(.cs)  # Window (view glue)
│   ├── App.xaml(.cs)         # Application, theme sync
│   ├── Program.cs            # Entry point with UAC elevation
│   └── Properties/           # Localized resources and settings
├── Symlinker.Tests/          # xUnit tests, 100% coverage gate
├── Symlinker.sln
├── Directory.Packages.props  # Central package versions
├── global.json               # Opts dotnet test into Microsoft.Testing.Platform
├── release.ps1               # ClickOnce build/publish script
└── .github/workflows/
    ├── ci.yml                # Build and test on pull requests
    └── release.yml           # Tagged releases
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
