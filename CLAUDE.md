# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Symlinker is a WPF application (.NET 10, MahApps.Metro) that provides a GUI for creating symbolic links, hard links, and directory junctions on Windows. It wraps the Windows `mklink` command with an easy-to-use interface.

**Target Platform:** Windows 10+ only

## Build & Development Commands

### Building the Application

```powershell
# Build in Debug configuration
dotnet build "Symlinker/Symlinker.csproj" --configuration Debug

# Build in Release configuration
dotnet build "Symlinker/Symlinker.csproj" --configuration Release
```

### Running the Application

```powershell
dotnet run --project "Symlinker/Symlinker.csproj"
```

The app starts unelevated. It only asks for administrator rights when a symbolic link needs them (see "Administrator rights" below).

### Running the Tests

```powershell
dotnet test --project Symlinker.Tests
```

- xUnit v3 on Microsoft.Testing.Platform (opted in through [global.json](global.json)), with coverage from `coverlet.MTP`
- Every run collects coverage for the `Symlinker` assembly and **fails below 100% line, branch or method coverage**. The Cobertura report lands in `TestResults/`
- Tests use the real file system under `%TEMP%`. Symbolic link tests need Developer Mode or an elevated prompt (GitHub's Windows runners are elevated)
- Code that needs a live window, UAC or native icons (`MainWindow`, `App`, `Program`, `MetroDialogService`, `InputError`, `Elevation.RestartElevated`, `Elevation.GetShieldIcon`) is marked `[ExcludeFromCodeCoverage]`. Keep logic out of those and put it in a tested class instead

### Publishing

- `./release.ps1` builds and publishes the ClickOnce installer to the `gh-pages` branch (`-OnlyBuild` skips deployment). It reads the Git tag for the version (`v1.2.3` becomes `1.2.3.0`).
- `./build-portable.ps1` builds the portable ZIP used for Scoop.

## Architecture

The window is view glue. Logic lives in plain classes that the tests drive with fake dialogs and a fake mklink.

### Key Components

1. **Program.cs**: Entry point. Parses command-line options (`LaunchOptions`) and opens the main window. It does not elevate.
2. **App.xaml / App.xaml.cs**: Loads MahApps.Metro styles and syncs the theme (light/dark and accent color) with Windows.
3. **MainWindow.xaml**: The whole UI. Two cards (link location, target), pill toggles for folder/file mode and link type, and a footer with the About button, an inline error line and the Create link button. Styles live in `Window.Resources`.
4. **MainWindow.xaml.cs**: Mode switching, link name auto-fill, file pickers, drag and drop. Hands link creation to `LinkCreator`.
5. **MainWindow.Inputs.cs**: Inline errors (`InputError.HasError` attached property plus the footer message), theme-dependent brushes, restoring launch options, the up-front admin notice, and the administrator restart prompt.
6. **Elevation.cs**: `LaunchOptions` (command-line parsing and correctly quoted argument building) and `Elevation` (admin check, symbolic link privilege probe, elevated restart with `runas`, the UAC shield icon).
7. **LinkCreator.cs**: The link workflow: replace-existing prompts, and moving an existing file or folder to the target (rename on the same drive, copy with progress across drives, rollback on failure).
8. **LinkMover.cs**: File system helpers for moving: copy that keeps attributes and recreates nested links, delete that never follows links.
9. **Mklink.cs**: `LinkKind`, mklink argument building, and `CmdMklinkRunner`, which runs `cmd.exe /c mklink`.
10. **InputValidator.cs**: Field checks behind the inline errors, and `ColorContrast` for text on the accent color.
11. **Dialogs.cs**: `IDialogService` / `IProgressDialog`, and `MetroDialogService`, which shows them in the window.
12. **UiHelpers.cs**: Path name extraction, picker start folders, byte formatting, app version, `LinkNameAutoFill`.

### Link Creation Flow

1. User picks folder or file mode (pills) and a link type. Junction is folder-only, hard link is file-only.
2. User fills in the link location, link name and target (typing, browsing, or drag and drop).
3. Create link (or Enter, it is the default button) runs `ValidateInputs()`, which marks the fields `InputValidator` flags.
4. `EnsureCanCreateLinkAsync()` checks whether a symbolic link can be created. If not, it offers to restart as administrator.
5. `LinkCreator.CreateAsync()` handles an existing item at the link path (replace it, or move it to the target), then runs mklink and shows the result.

### Administrator rights

- Junctions and hard links never need admin. Symbolic links need admin unless Windows Developer Mode is on.
- `Elevation.CanCreateSymbolicLinks()` probes by creating a throwaway symlink in the temp folder.
- The probe runs when the window loads (and again on activation while admin is needed). If symbolic links need admin, Create link shows the UAC shield and the footer shows a notice with a "Restart as administrator" link, so the user knows before filling in the form. Field errors take the same footer spot and hide the notice.
- When elevation is needed, the app restarts itself with `runas` and passes the form values as quoted arguments (`--mode`, `--type`, `--location`, `--name`, `--target`). The old `--engage` flag is still accepted and ignored.

### Important Patterns

- **Command building**: mklink flags are `/D` (directory symlink), `/H` (hard link), `/J` (junction); paths are wrapped in quotes.
- **Theme colors**: Use `DynamicResource` with MahApps brushes (`MahApps.Brushes.Gray1..10`, `Accent`, `ThemeForeground`). The gray scale is not monotonic in the dark theme, so check contrast in both themes. `Symlinker.Brushes.Error` and `Symlinker.Brushes.OnAccent` are set at runtime in `MainWindow.Inputs.cs`.

### Dependencies

Managed via Central Package Management ([Directory.Packages.props](Directory.Packages.props)):

- **MahApps.Metro**: Window chrome, dialogs, theming
- **MahApps.Metro.IconPacks.Material**: Icons
- **xunit.v3**, **coverlet.MTP**: Tests and coverage

## Project Structure

```
Symlinker/
├── Symlinker/
│   ├── App.xaml(.cs)            # Application, theme sync
│   ├── MainWindow.xaml          # UI and styles
│   ├── MainWindow.xaml.cs       # Window glue
│   ├── MainWindow.Inputs.cs     # Inline errors, admin restart
│   ├── Elevation.cs             # LaunchOptions and elevation helpers
│   ├── LinkCreator.cs           # Link workflow
│   ├── LinkMover.cs             # Move/copy/delete helpers
│   ├── Mklink.cs                # mklink arguments and runner
│   ├── InputValidator.cs        # Field checks, accent contrast
│   ├── Dialogs.cs               # Dialog abstraction + MahApps implementation
│   ├── UiHelpers.cs             # Small UI helpers
│   ├── Program.cs               # Entry point
│   ├── Properties/
│   │   ├── Resources.resx           # English strings
│   │   ├── Resources.<lang>.resx    # de, es, fr, hi, it, ja, pt, zh
│   │   ├── Resources.Designer.cs    # Typed string accessors
│   │   └── app.manifest             # asInvoker, PerMonitorV2 DPI
│   ├── icon.ico
│   └── Symlinker.csproj
├── Symlinker.Tests/            # xUnit tests, 100% coverage gate
├── Symlinker.sln
├── Directory.Packages.props
├── global.json                 # Opts dotnet test into Microsoft.Testing.Platform
├── release.ps1                 # ClickOnce build/publish
├── build-portable.ps1          # Portable ZIP for Scoop
└── .github/workflows/          # ci.yml, release.yml
```

## Release Process

Releases are automated via GitHub Actions:

1. Push a Git tag: `git tag v1.2.3 && git push origin v1.2.3`
2. [.github/workflows/release.yml](.github/workflows/release.yml) runs `release.ps1` on a Windows runner
3. The app is published to `gh-pages` and ClickOnce auto-updates existing installs

## Known Limitations

- **Drag and drop**: Works in the normal (unelevated) app. It does not work after restarting as administrator, because Windows blocks drops from a lower-privilege Explorer.
- **Platform**: Windows 10/11 only.

## Localization

- Every UI string lives in `Properties/Resources.resx` and must also be added to all 8 translations (de, es, fr, hi, it, ja, pt, zh).
- `Resources.Designer.cs` is not regenerated by `dotnet build`. When adding a string, add its typed property by hand in the same generated style.
- The window is a fixed 560x280, so check long translations (German, Italian, French) for overflow in pills, buttons and dialogs.
