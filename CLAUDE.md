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

### Publishing

- `./release.ps1` builds and publishes the ClickOnce installer to the `gh-pages` branch (`-OnlyBuild` skips deployment). It reads the Git tag for the version (`v1.2.3` becomes `1.2.3.0`).
- `./build-portable.ps1` builds the portable ZIP used for Scoop.

## Architecture

### Key Components

1. **Program.cs**: Entry point. Parses command-line options (`LaunchOptions`) and opens the main window. It does not elevate. With `--link-helper` it runs the hidden elevated helper instead (see "Administrator rights").
2. **App.xaml / App.xaml.cs**: Loads MahApps.Metro styles and syncs the theme (light/dark and accent color) with Windows.
3. **MainWindow.xaml**: The whole UI. Two cards (link location, target), pill toggles for folder/file mode and link type, and a footer with the About button, an inline error line and the Create link button. Styles live in `Window.Resources`.
4. **MainWindow.xaml.cs**: Mode switching, link name auto-fill, file pickers, drag and drop, and link creation (`CreateLink()`, `SendCommand()`, the move-and-link flow, and `RunMklinkAsync()`).
5. **MainWindow.Inputs.cs**: Field validation with inline errors (`InputError.HasError` attached property plus the footer message), theme-dependent brushes, restoring launch options, the up-front admin notice, and starting the elevated helper on demand (`ElevateIfNeededAsync()`).
6. **Elevation.cs**: `LaunchOptions` (command-line parsing) and `Elevation` (admin check, symbolic link privilege probe, the UAC shield icon).
7. **LinkCommand.cs**: `LinkRequest`/`LinkResult` and `LinkCommand.RunAsync()`, which runs `cmd.exe /c mklink`. Used by the window and by the elevated helper.
8. **ElevatedLinker.cs**: Starts `Symlinker.exe --link-helper` as administrator and sends it link requests over a named pipe.
9. **LinkMover.cs**: File system helpers for moving an existing file or folder to the target before linking.

### Link Creation Flow

1. User picks folder or file mode (pills) and a link type. Junction is folder-only, hard link is file-only.
2. User fills in the link location, link name and target (typing, browsing, or drag and drop).
3. Create link (or Enter, it is the default button) runs `ValidateInputs()`, which marks bad fields inline.
4. `RefreshSymlinkAccessNow()` probes again whether a symbolic link needs administrator rights.
5. `CreateLink()` handles an existing item at the link path (delete it, or move it to the target), then `SendCommand()` runs mklink and shows the result. `ElevateIfNeededAsync()` is called before the first change on disk.

### Administrator rights

- Junctions and hard links never need admin. Symbolic links need admin unless Windows Developer Mode is on.
- `Elevation.CanCreateSymbolicLinks()` probes by creating a throwaway symlink in the temp folder.
- The probe runs when the window loads (again on activation while admin is needed, and on Create link). If symbolic links need admin, Create link shows the UAC shield and the footer says Windows will ask for permission, so the user knows before filling in the form. Field errors take the same footer spot and hide the notice.
- The window never restarts or elevates itself, so drag and drop keeps working. When a symbolic link needs admin, `ElevateIfNeededAsync()` starts `Symlinker.exe --link-helper <pipe> <pid>` with `runas` (one UAC prompt). The helper has no window: it connects back over the named pipe, creates each link it is sent with mklink, and exits when the window closes the pipe after Create link finishes.
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

## Project Structure

```
Symlinker/
├── Symlinker/
│   ├── App.xaml(.cs)            # Application, theme sync
│   ├── MainWindow.xaml          # UI and styles
│   ├── MainWindow.xaml.cs       # Core window logic and link creation
│   ├── MainWindow.Inputs.cs     # Validation, inline errors, admin notice
│   ├── Elevation.cs             # LaunchOptions, symlink probe, UAC shield
│   ├── LinkCommand.cs           # Runs mklink
│   ├── ElevatedLinker.cs        # Elevated link helper over a named pipe
│   ├── LinkMover.cs             # Move-and-link file system helpers
│   ├── Program.cs               # Entry point
│   ├── Properties/
│   │   ├── Resources.resx           # English strings
│   │   ├── Resources.<lang>.resx    # de, es, fr, hi, it, ja, pt, zh
│   │   ├── Resources.Designer.cs    # Typed string accessors
│   │   └── app.manifest             # asInvoker, PerMonitorV2 DPI
│   ├── icon.ico
│   └── Symlinker.csproj
├── Symlinker.sln
├── Directory.Packages.props
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

- **Drag and drop**: Works because the window always runs unelevated. It does not work if the user starts Symlinker as administrator themselves, because Windows blocks drops from a lower-privilege Explorer.
- **Platform**: Windows 10/11 only.

## Localization

- Every UI string lives in `Properties/Resources.resx` and must also be added to all 8 translations (de, es, fr, hi, it, ja, pt, zh).
- `Resources.Designer.cs` is not regenerated by `dotnet build`. When adding a string, add its typed property by hand in the same generated style.
- The window is a fixed 560x280, so check long translations (German, Italian, French) for overflow in pills, buttons and dialogs.
