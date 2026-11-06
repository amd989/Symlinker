namespace Symlinker
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;

    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Interop;

    using MahApps.Metro.Controls;
    using MahApps.Metro.Controls.Dialogs;
    using MahApps.Metro.IconPacks;

    using Res = Symlinker.Properties.Resources;

    /// <summary>Main application window.</summary>
    public partial class MainWindow : MetroWindow
    {
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

        private const string PlaceholderBrushKey = "MahApps.Brushes.Gray2";
        private const string PrimaryTextBrushKey = "MahApps.Brushes.ThemeForeground";

        private bool isFolder = true;

        // Last link name filled in from the target, so user-typed names are never overwritten
        private string autoFilledLinkName = string.Empty;

        // True while files are being moved, so the window can't be closed halfway through
        private bool isMoving;

        private static readonly MetroDialogSettings FastDialog = new()
        {
            AnimateShow = false,
            AnimateHide = false, 
        };

        internal MainWindow(LaunchOptions options = null)
        {
            launchOptions = options;
            InitializeComponent();
            InitializeInputs();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

            UpdateMode();
            ApplyLaunchOptions();
        }

        private void TypeRadio_Checked(object sender, RoutedEventArgs e)
        {
            UpdateMode();
        }

        private void UpdateMode()
        {
            if (folderTypeRadio == null || junctionRadio == null || symLinkRadio == null || hardLinkRadio == null || sourceIcon == null) return;

            isFolder = folderTypeRadio.IsChecked == true;

            junctionRadio.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;
            hardLinkRadio.Visibility = isFolder ? Visibility.Collapsed : Visibility.Visible;

            if (!isFolder && junctionRadio.IsChecked == true)
                symLinkRadio.IsChecked = true;

            if (isFolder && hardLinkRadio.IsChecked == true)
                symLinkRadio.IsChecked = true;

            sourceIcon.Kind = isFolder
                ? PackIconMaterialKind.FolderOutline
                : PackIconMaterialKind.FileOutline;

            var linkTypeTooltip = isFolder ? Res.TooltipLinkTypeFolderDescription : Res.TooltipLinkTypeFileDescription;
            symLinkRadio.ToolTip = linkTypeTooltip;
            hardLinkRadio.ToolTip = linkTypeTooltip;
            junctionRadio.ToolTip = linkTypeTooltip;

            UpdateTargetBrowseLabel();
            ClearInputErrors();

            // Keep the target when it still fits the new mode (an existing folder or file)
            if (!TargetFitsMode(destinationLocationTextBox.Text, isFolder))
                destinationLocationTextBox.Text = string.Empty;

            if (destinationLocationTextBox.Text.Length == 0)
                ShowTargetPlaceholder();
        }

        private void ShowTargetPlaceholder()
        {
            destinationFolderName.Text = isFolder ? Res.PlaceholderTargetFolder : Res.PlaceholderTargetFile;
            destinationFolderName.FontStyle = FontStyles.Italic;
            destinationFolderName.SetResourceReference(ForegroundProperty, PlaceholderBrushKey);
        }

        private string GetLinkTypeFlag()
        {
            if (hardLinkRadio.IsChecked == true) return "/H ";
            if (junctionRadio.IsChecked == true) return "/J ";
            return string.Empty;
        }

        private void DestinationPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            var path = destinationLocationTextBox.Text;
            if (string.IsNullOrEmpty(path))
            {
                ShowTargetPlaceholder();
                AutoFillLinkName(string.Empty);
                return;
            }

            var trimmed = path.TrimEnd('\\', '/');
            var lastSep = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            var name = lastSep >= 0 ? trimmed[(lastSep + 1)..] : trimmed;
            destinationFolderName.Text = name;
            destinationFolderName.FontStyle = FontStyles.Normal;
            destinationFolderName.SetResourceReference(ForegroundProperty, PrimaryTextBrushKey);
            AutoFillLinkName(name);
        }

        /// <summary>
        /// Keeps the link name in sync with the target's name until the user types their own.
        /// </summary>
        private void AutoFillLinkName(string targetName)
        {
            var current = linkNameTextBox.Text;
            var userEdited = current.Length != 0 && current != autoFilledLinkName;
            if (userEdited)
                return;

            // Skip names that can't be used as a link name, like a drive root ("D:")
            if (targetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                targetName = string.Empty;

            autoFilledLinkName = targetName;
            linkNameTextBox.Text = targetName;
        }

        private async Task CreateLink()
        {
            try
            {
                var linkLocation = linkLocationTextBox.Text.Trim();
                var linkName = linkNameTextBox.Text.Trim();
                var destination = destinationLocationTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(linkLocation) || string.IsNullOrWhiteSpace(linkName) || string.IsNullOrWhiteSpace(destination))
                {
                    await this.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.FillBlanks, settings: FastDialog);
                    return;
                }

                if (linkName.Contains('"') || linkLocation.Contains('"') || destination.Contains('"'))
                {
                    await this.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.InvalidQuoteInPath, settings: FastDialog);
                    return;
                }

                var link = $"\"{linkLocation}\\{linkName}\"";

                // A real file or folder already sits where the link goes and the target is free:
                // offer to move it to the target and leave a link behind (#33)
                if (Directory.Exists(linkLocation) && Path.IsPathFullyQualified(destination))
                {
                    var linkPath = Path.Combine(linkLocation, linkName);
                    FileSystemInfo existing = isFolder ? new DirectoryInfo(linkPath) : new FileInfo(linkPath);
                    if (existing.Exists && !LinkMover.IsLink(existing) && IsFreeMoveTarget(destination))
                    {
                        await OfferMoveAsync(existing, link, Path.GetFullPath(destination));
                        return;
                    }
                }

                if (isFolder && Directory.Exists(linkLocation) && Directory.Exists(destination))
                {
                    var directories = Directory.GetDirectories(linkLocation);

                    if (directories.Any(e => Path.GetFileName(e).Equals(linkName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var answer = await this.ShowMessageAsync(
                            Res.DialogFolderExistsDialog,
                            Res.DialogFolderExists,
                            MessageDialogStyle.AffirmativeAndNegative,
                            FastDialog);
                        if (answer == MessageDialogResult.Affirmative)
                        {
                            var dir2Delete = directories.First(e => Path.GetFileName(e).Equals(linkName, StringComparison.OrdinalIgnoreCase));
                            Directory.Delete(dir2Delete, true);
                            await SendCommand(link, destination);
                            return;
                        }

                        await this.ShowMessageAsync(Res.LinkCreationAbortedWarning, Res.LinkCreationAborted, settings: FastDialog);
                    }
                    else
                    {
                        await SendCommand(link, destination);
                    }
                }
                else if (!isFolder && Directory.Exists(linkLocation) && File.Exists(destination))
                {
                    var files = Directory.GetFiles(linkLocation);
                    if (files.Any(e => Path.GetFileName(e).Equals(linkName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var answer = await this.ShowMessageAsync(
                            Res.DialogDeleteFileWarning,
                            Res.DialogDeleteFile,
                            MessageDialogStyle.AffirmativeAndNegative,
                            FastDialog);
                        if (answer == MessageDialogResult.Affirmative)
                        {
                            var file2Delete = files.First(e => Path.GetFileName(e).Equals(linkName, StringComparison.OrdinalIgnoreCase));
                            File.Delete(file2Delete);
                            await SendCommand(link, destination);
                            return;
                        }
                        await this.ShowMessageAsync(Res.LinkCreationAbortedWarning, Res.LinkCreationAborted, settings: FastDialog);
                    }
                    else
                    {
                        await SendCommand(link, destination);
                    }
                }
                else
                {
                    await this.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.FilesOrFolderNotExists, settings: FastDialog);
                }
            }
            catch (Exception exception)
            {
                await this.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.MessageBoxExceptionOcurred + "\n" + exception.Message, settings: FastDialog);
            }
        }

        private async Task SendCommand(string link, string destination)
        {
            var (success, message) = await RunMklinkAsync(link, destination);
            await this.ShowMessageAsync(
                success ? Res.MessageBoxSuccessTitle : Res.MessageBoxErrorTitle,
                message,
                settings: FastDialog);
        }

        /// <summary>Runs mklink and returns whether it worked, with the message to show the user.</summary>
        private async Task<(bool Success, string Message)> RunMklinkAsync(string link, string destination)
        {
            try
            {
                var typeLink = GetLinkTypeFlag();
                var directory = isFolder ? "/D " : string.Empty;
                var stringCommand = $"/c mklink {directory}{typeLink}{link} \"{destination}\"";
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = stringCommand,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var process = new Process { StartInfo = processStartInfo };
                process.Start();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                var outputText = (await output).Trim();
                var errorText = (await error).Trim();

                if (process.ExitCode == 0)
                    return (true, outputText.Length > 0 ? outputText : Res.LinkSuccessfullyCreated);

                return (false, errorText.Length > 0 ? errorText : outputText);
            }
            catch (Exception)
            {
                return (false, Res.CmdNotFound);
            }
        }

        /// <summary>
        /// Whether the target path is free to receive a moved item: it doesn't exist yet,
        /// or (in folder mode) it is an empty folder.
        /// </summary>
        private bool IsFreeMoveTarget(string destination)
        {
            if (File.Exists(destination))
                return false;

            if (!Directory.Exists(destination))
                return true;

            return isFolder && !Directory.EnumerateFileSystemEntries(destination).Any();
        }

        private async Task OfferMoveAsync(FileSystemInfo existing, string link, string target)
        {
            var settings = new MetroDialogSettings
            {
                AnimateShow = false,
                AnimateHide = false,
                AffirmativeButtonText = Res.ButtonMoveAndLink,
                NegativeButtonText = Res.ButtonCancel,
            };

            var message = string.Format(
                CultureInfo.CurrentUICulture,
                isFolder ? Res.DialogMoveFolder : Res.DialogMoveFile,
                existing.Name,
                target);

            var answer = await this.ShowMessageAsync(Res.DialogMoveTitle, message, MessageDialogStyle.AffirmativeAndNegative, settings);
            if (answer != MessageDialogResult.Affirmative)
            {
                await this.ShowMessageAsync(Res.LinkCreationAbortedWarning, Res.LinkCreationAborted, settings: FastDialog);
                return;
            }

            isMoving = true;
            try
            {
                await MoveAndLinkAsync(existing, link, target);
            }
            finally
            {
                isMoving = false;
            }
        }

        /// <summary>
        /// Moves <paramref name="source"/> to <paramref name="target"/> and creates the link where it was.
        /// Any failure before the link exists puts everything back the way it was.
        /// </summary>
        private async Task MoveAndLinkAsync(FileSystemInfo source, string link, string target)
        {
            var originalPath = source.FullName;

            if (LinkMover.IsSameOrInside(target, originalPath) || LinkMover.IsSameOrInside(originalPath, target))
            {
                await this.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.MoveTargetInsideSource, settings: FastDialog);
                return;
            }

            var targetWasEmptyFolder = Directory.Exists(target);

            // Where the original currently lives; it only leaves originalPath once we start moving
            var currentPath = originalPath;
            var copiedToTarget = false;

            try
            {
                var targetParent = Path.GetDirectoryName(target);
                if (targetParent != null)
                    Directory.CreateDirectory(targetParent);

                // Only ever an empty folder here (checked before offering the move)
                if (targetWasEmptyFolder)
                    Directory.Delete(target);

                if (LinkMover.IsSameVolume(originalPath, target))
                {
                    // Same drive: a rename, instant and atomic
                    LinkMover.Rename(originalPath, target);
                    currentPath = target;
                }
                else
                {
                    // Different drive: set the original aside first. This fails right away if
                    // something has its files open, before any time is spent copying.
                    var staging = LinkMover.GetUnusedPath(originalPath + ".symlinker-move");
                    LinkMover.Rename(originalPath, staging);
                    currentPath = staging;

                    copiedToTarget = true;
                    FileSystemInfo staged = isFolder ? new DirectoryInfo(staging) : new FileInfo(staging);
                    var copyError = await CopyWithProgressAsync(staged, target);
                    if (copyError != null)
                    {
                        var title = copyError == Res.MoveCanceled ? Res.LinkCreationAbortedWarning : Res.MessageBoxErrorTitle;
                        await RollBackMoveAsync(originalPath, currentPath, target, copiedToTarget, targetWasEmptyFolder, copyError, title);
                        return;
                    }
                }

                var (linked, linkMessage) = await RunMklinkAsync(link, target);
                if (!linked)
                {
                    await RollBackMoveAsync(originalPath, currentPath, target, copiedToTarget, targetWasEmptyFolder, string.Format(CultureInfo.CurrentUICulture, Res.MoveFailed, linkMessage));
                    return;
                }

                if (currentPath != target)
                {
                    try
                    {
                        LinkMover.Delete(currentPath);
                    }
                    catch (Exception)
                    {
                        await this.ShowMessageAsync(
                            Res.MessageBoxSuccessTitle,
                            string.Format(CultureInfo.CurrentUICulture, Res.MoveCleanupFailed, currentPath),
                            settings: FastDialog);
                        return;
                    }
                }

                await this.ShowMessageAsync(
                    Res.MessageBoxSuccessTitle,
                    string.Format(CultureInfo.CurrentUICulture, Res.MoveSucceeded, source.Name, target),
                    settings: FastDialog);
            }
            catch (Exception exception)
            {
                await RollBackMoveAsync(originalPath, currentPath, target, copiedToTarget, targetWasEmptyFolder, string.Format(CultureInfo.CurrentUICulture, Res.MoveFailed, exception.Message));
            }
        }

        /// <summary>Puts the original back where it was and removes any partial copy, then explains what happened.</summary>
        private async Task RollBackMoveAsync(string originalPath, string currentPath, string target, bool copiedToTarget, bool targetWasEmptyFolder, string message, string title = null)
        {
            title ??= Res.MessageBoxErrorTitle;

            try
            {
                // Only remove the target when it is our own copy, never the only copy of the files
                if (copiedToTarget && currentPath != target)
                    LinkMover.Delete(target);

                if (currentPath != originalPath)
                    LinkMover.Rename(currentPath, originalPath);

                if (targetWasEmptyFolder)
                    Directory.CreateDirectory(target);
            }
            catch (Exception)
            {
                if (currentPath != originalPath)
                {
                    message = string.Format(CultureInfo.CurrentUICulture, Res.MoveRollbackFailed, currentPath);
                    title = Res.MessageBoxErrorTitle;
                }
            }

            await this.ShowMessageAsync(title, message, settings: FastDialog);
        }

        /// <summary>
        /// Copies with a cancelable progress dialog. Returns null when the copy finished,
        /// otherwise the message to show the user.
        /// </summary>
        private async Task<string> CopyWithProgressAsync(FileSystemInfo source, string target)
        {
            var settings = new MetroDialogSettings
            {
                AnimateShow = false,
                AnimateHide = false,
                NegativeButtonText = Res.ButtonCancel,
            };

            var controller = await this.ShowProgressAsync(Res.ProgressMoveTitle, Res.ProgressMovePreparing, isCancelable: true, settings: settings);
            controller.SetIndeterminate();

            using var cancellation = new CancellationTokenSource();
            controller.Canceled += (s, e) => cancellation.Cancel();

            try
            {
                var total = await Task.Run(() => LinkMover.GetTotalBytes(source), cancellation.Token);

                void Show(long done)
                {
                    controller.SetProgress(total == 0 ? 1 : Math.Min(1, (double)done / total));
                    controller.SetMessage(string.Format(CultureInfo.CurrentUICulture, Res.ProgressMoveMessage, FormatBytes(done), FormatBytes(total)));
                }

                Show(0);
                IProgress<long> progress = new Progress<long>(Show);
                var stopwatch = Stopwatch.StartNew();
                long copied = 0;
                long lastReport = 0;

                await Task.Run(
                    () => LinkMover.Copy(
                        source,
                        target,
                        bytes =>
                        {
                            copied += bytes;
                            if (stopwatch.ElapsedMilliseconds - lastReport >= 100)
                            {
                                lastReport = stopwatch.ElapsedMilliseconds;
                                progress.Report(copied);
                            }
                        },
                        cancellation.Token),
                    cancellation.Token);

                return null;
            }
            catch (OperationCanceledException)
            {
                return Res.MoveCanceled;
            }
            catch (Exception exception)
            {
                return string.Format(CultureInfo.CurrentUICulture, Res.MoveFailed, exception.Message);
            }
            finally
            {
                await controller.CloseAsync();
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return string.Format(CultureInfo.CurrentUICulture, unit == 0 ? "{0:0} {1}" : "{0:0.0} {1}", size, units[unit]);
        }

        /// <summary>Keeps the window open while files are being moved, so a move is never cut off halfway.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (isMoving)
                e.Cancel = true;

            base.OnClosing(e);
        }

        private void ExploreButton1_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();

            // Start in the folder already entered so consecutive links don't need re-navigating
            var current = GetExistingDirectory(linkLocationTextBox.Text, useParent: false);
            if (current != null)
                dialog.InitialDirectory = current;

            if (dialog.ShowDialog() == true)
                linkLocationTextBox.Text = dialog.FolderName;
        }

        private void ExploreButton2_Click(object sender, RoutedEventArgs e)
        {
            // Start next to the current target so sibling folders/files are one click away
            var current = GetExistingDirectory(destinationLocationTextBox.Text, useParent: true);

            if (isFolder)
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog();
                if (current != null)
                    dialog.InitialDirectory = current;
                if (dialog.ShowDialog() == true)
                    destinationLocationTextBox.Text = dialog.FolderName;
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog();
                if (current != null)
                    dialog.InitialDirectory = current;
                if (dialog.ShowDialog() == true)
                    destinationLocationTextBox.Text = dialog.FileName;
            }
        }

        /// <summary>
        /// Returns an existing directory to open a picker in, based on a path typed by the user.
        /// With <paramref name="useParent"/>, returns the folder that contains the path instead.
        /// Returns null when nothing usable exists.
        /// </summary>
        private static string GetExistingDirectory(string path, bool useParent)
        {
            try
            {
                path = path.Trim();
                if (string.IsNullOrEmpty(path))
                    return null;

                var full = Path.GetFullPath(path);
                var candidate = useParent ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(full)) : full;

                // Fall back to the path itself when it has no parent (e.g. a drive root)
                candidate ??= full;
                return Directory.Exists(candidate) ? candidate : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async void CreateLink_Click(object sender, RoutedEventArgs e)
        {
            createLinkButton.IsEnabled = false;
            try
            {
                if (ValidateInputs() && await EnsureCanCreateLinkAsync())
                    await CreateLink();
            }
            finally
            {
                createLinkButton.IsEnabled = true;
            }
        }

        private async void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var version = Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion");
            if (string.IsNullOrEmpty(version))
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                version = fvi.FileVersion;
            }

            await this.ShowMessageAsync(
                Res.MessageBoxAboutTitle,
                string.Format(CultureInfo.CurrentUICulture, Res.AboutDescription, version, DateTime.Now.Year),
                settings: FastDialog);
        }

        private void TextBox_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void TextBox_PreviewDragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void SourceCard_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length != 0)
                {
                    var path = files[0];
                    var droppedIsFolder = Directory.Exists(path);
                    if (droppedIsFolder != isFolder)
                    {
                        if (droppedIsFolder)
                            folderTypeRadio.IsChecked = true;
                        else
                            fileTypeRadio.IsChecked = true;
                    }
                    destinationLocationTextBox.Text = path;
                }
            }
        }

        private void LinkCard_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length != 0 && Directory.Exists(files[0]))
                    linkLocationTextBox.Text = files[0];
            }
        }
    }
}
