namespace Symlinker
{
    using System;
    using System.Diagnostics;
    using System.Globalization;

    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
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

                var gotOutput = false;
                var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };
                process.ErrorDataReceived += Process_ErrorDataReceived;
                process.OutputDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                    {
                        gotOutput = true;
                        Dispatcher.InvokeAsync(() => this.ShowMessageAsync(Res.MessageBoxSuccessTitle, ev.Data, settings: FastDialog));
                    }
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && !gotOutput)
                    await this.ShowMessageAsync(Res.MessageBoxSuccessTitle, Res.LinkSuccessfullyCreated, settings: FastDialog);

                process.Dispose();
            }
            catch (Exception)
            {
                await this.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.CmdNotFound, settings: FastDialog);
            }
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
                Dispatcher.InvokeAsync(() => this.ShowMessageAsync(Res.MessageBoxErrorTitle, e.Data, settings: FastDialog));
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
