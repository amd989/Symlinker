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
- Code that needs a live window, UAC or native calls (`MainWindow`, `App`, `Program`, `MetroDialogService`, `InputError`, `ElevatedLinker.StartAsync`, `Elevation.GetShieldIcon`) is marked `[ExcludeFromCodeCoverage]`. Keep logic out of those and put it in a tested class instead
- The elevated helper's pipe protocol is tested in-process: the test plays the window's end of the pipe and runs `ElevatedLinker.RunHelper` on a background thread, so no UAC prompt is involved

### Publishing

- `./release.ps1` builds and publishes the ClickOnce installer to the `gh-pages` branch (`-OnlyBuild` skips deployment). It reads the Git tag for the version (`v1.2.3` becomes `1.2.3.0`).
- `./build-portable.ps1` builds the portable ZIP used for Scoop.
- `./build-msix.ps1` builds the Microsoft Store package (see below).

### Publishing (Microsoft Store / MSIX)

```powershell
# Builds Symlinker/bin/msix/Symlinker-<version>.msixbundle (x64 + arm64, self-contained, unsigned).
# Version defaults to the latest Git tag; the Store needs a higher version on every upload.
./build-msix.ps1

# Also install this PC's build for a test run (Developer Mode)
./build-msix.ps1 -Install

# Also run the Windows App Certification Kit (administrator shell)
./build-msix.ps1 -Wack
```

- Upload the `.msixbundle` in Partner Center > Symlinker > the submission > Packages. Store ID `9P8H93Z0TGK4`.
- Manifest template and tile assets live in `packaging/msix/`. Identity values there must match Partner Center.
- `Properties/DisplayName` must be a name reserved in Partner Center (Manage app names). "Symlinker" alone is taken, so it is "Symlinker - Symlink Creator". The Start menu name (`VisualElements DisplayName`) stays "Symlinker".
- `-p:MsixBuild=true` swaps in `Properties/app.msix.manifest` (`requireAdministrator`), since the packaged app cannot restart itself with `runas`. So the Store build always runs as administrator and never shows the restart prompt. The script fails if the built exe doesn't ask for admin.
- The manifest disables file/registry write virtualization so links created under AppData land in the real location.

## Architecture

The window is view glue. Logic lives in plain classes that the tests drive with fake dialogs and a fake mklink.

### Key Components

1. **Program.cs**: Entry point. Parses command-line options (`LaunchOptions`) and opens the main window. It does not elevate. With `--link-helper` it runs the hidden elevated helper instead (see "Administrator rights").
2. **App.xaml / App.xaml.cs**: Loads MahApps.Metro styles and syncs the theme (light/dark and accent color) with Windows.
3. **MainWindow.xaml**: The whole UI. Two cards (link location, target), pill toggles for folder/file mode and link type, and a footer with the About button, an inline error line and the Create link button. Styles live in `Window.Resources`.
4. **MainWindow.xaml.cs**: Mode switching, link name auto-fill, file pickers, drag and drop. Hands link creation to `LinkCreator`.
5. **MainWindow.Inputs.cs**: Inline errors (`InputError.HasError` attached property plus the footer message), theme-dependent brushes, restoring launch options, and the up-front admin notice.
6. **Elevation.cs**: `LaunchOptions` (command-line parsing) and `Elevation` (admin check, symbolic link privilege probe, the UAC shield icon).
7. **LinkCreator.cs**: The link workflow: replace-existing prompts, and moving an existing file or folder to the target (rename on the same drive, copy with progress across drives, rollback on failure).
8. **LinkRunner.cs**: `ILinkRunner`, which `LinkCreator` uses to create links. `LinkRunner` runs mklink unelevated, except symbolic links that need admin, which go through the elevated helper. `PrepareAsync()` starts the helper (one UAC prompt) before anything on disk changes.
9. **LinkCommand.cs**: `LinkKind`, `LinkRequest`/`LinkResult` and `LinkCommand.RunAsync()`, which runs `cmd.exe /c mklink`. Used unelevated and by the elevated helper.
10. **ElevatedLinker.cs**: Starts `Symlinker.exe --link-helper` as administrator and sends it link requests over a named pipe. `RunHelper()` is the helper's side.
11. **LinkMover.cs**: File system helpers for moving: copy that keeps attributes and recreates nested links, delete that never follows links.
12. **InputValidator.cs**: Field checks behind the inline errors, and `ColorContrast` for text on the accent color.
13. **Dialogs.cs**: `IDialogService` / `IProgressDialog`, and `MetroDialogService`, which shows them in the window.
14. **UiHelpers.cs**: Path name extraction, picker start folders, byte formatting, app version, `LinkNameAutoFill`.

### Link Creation Flow

1. User picks folder or file mode (pills) and a link type. Junction is folder-only, hard link is file-only.
2. User fills in the link location, link name and target (typing, browsing, or drag and drop).
3. Create link (or Enter, it is the default button) runs `ValidateInputs()`, which marks the fields `InputValidator` flags.
4. `RefreshSymlinkAccessNow()` probes again whether a symbolic link needs administrator rights.
5. `LinkCreator.CreateAsync()` handles an existing item at the link path (replace it, or move it to the target), then runs mklink and shows the result. `ILinkRunner.PrepareAsync()` is called before the first change on disk.

### Administrator rights

- Junctions and hard links never need admin. Symbolic links need admin unless Windows Developer Mode is on.
- `Elevation.CanCreateSymbolicLinks()` probes by creating a throwaway symlink in the temp folder.
- The probe runs when the window loads (again on activation while admin is needed, and on Create link). If symbolic links need admin, Create link shows the UAC shield and the footer says Windows will ask for permission, so the user knows before filling in the form. Field errors take the same footer spot and hide the notice.
- The window never restarts or elevates itself, so drag and drop keeps working. When a symbolic link needs admin, `LinkRunner.PrepareAsync()` starts `Symlinker.exe --link-helper <pipe> <pid>` with `runas` (one UAC prompt). The helper has no window: it connects back over the named pipe and creates each link it is sent with mklink. It stays up until the window closes (or it is ended, then the next symbolic link starts a new one), so the user approves UAC once per session. While it runs, the shield and the footer notice are hidden. Only symbolic links that need admin go through it; junctions and hard links always run unelevated.
- Elevation happens before anything on disk changes (deleting an existing item, or the move-and-link flow), so declining the UAC prompt (Win32 error 1223) quietly stops and leaves everything as it was.
- Both ends check the other's process ID on the pipe (`GetNamedPipeClientProcessId` / `GetNamedPipeServerProcessId`), and `LinkCommand` rejects paths with quotes or line breaks, so the helper only ever runs a single mklink.
- Command-line options (`--mode`, `--type`, `--location`, `--name`, `--target`) still prefill the form. The old `--engage` flag is still accepted and ignored.

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
│   ├── MainWindow.Inputs.cs     # Inline errors, admin notice
│   ├── Elevation.cs             # LaunchOptions, symlink probe, UAC shield
│   ├── LinkCreator.cs           # Link workflow
│   ├── LinkRunner.cs            # Unelevated or elevated mklink
│   ├── LinkCommand.cs           # Runs mklink
│   ├── ElevatedLinker.cs        # Elevated link helper over a named pipe
│   ├── LinkMover.cs             # Move/copy/delete helpers
│   ├── InputValidator.cs        # Field checks, accent contrast
│   ├── Dialogs.cs               # Dialog abstraction + MahApps implementation
│   ├── UiHelpers.cs             # Small UI helpers
│   ├── Program.cs               # Entry point
│   ├── Properties/
│   │   ├── Resources.resx           # English strings
│   │   ├── Resources.<lang>.resx    # de, es, fr, hi, it, ja, pt, zh
│   │   ├── Resources.Designer.cs    # Typed string accessors
│   │   ├── app.manifest             # asInvoker, PerMonitorV2 DPI
│   │   └── app.msix.manifest        # Store build: requireAdministrator
│   ├── icon.ico
│   └── Symlinker.csproj
├── Symlinker.Tests/            # xUnit tests, 100% coverage gate
├── Symlinker.sln
├── Directory.Packages.props
├── global.json                 # Opts dotnet test into Microsoft.Testing.Platform
├── release.ps1                 # ClickOnce build/publish
├── build-portable.ps1          # Portable ZIP for Scoop
├── build-msix.ps1              # Microsoft Store MSIX bundle
├── packaging/msix/             # Store manifest template and tile assets
└── .github/workflows/          # ci.yml, release.yml
```

## Release Process

Releases are automated via GitHub Actions:

1. Push a Git tag: `git tag v1.2.3 && git push origin v1.2.3`
2. [.github/workflows/release.yml](.github/workflows/release.yml) runs `release.ps1` on a Windows runner
3. The app is published to `gh-pages` and ClickOnce auto-updates existing installs

## Known Limitations

- **Drag and drop**: Works because the window always runs unelevated. It does not work if the user starts Symlinker as administrator themselves, because Windows blocks drops from a lower-privilege Explorer.
- **Platform**: Windows 10/11 only.

## Localization

- Every UI string lives in `Properties/Resources.resx` and must also be added to all 8 translations (de, es, fr, hi, it, ja, pt, zh).
- `Resources.Designer.cs` is not regenerated by `dotnet build`. When adding a string, add its typed property by hand in the same generated style.
- The window is a fixed 560x280, so check long translations (German, Italian, French) for overflow in pills, buttons and dialogs.
