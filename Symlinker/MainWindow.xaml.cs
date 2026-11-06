namespace Symlinker
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;

    using System.IO;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Interop;

    using MahApps.Metro.Controls;
    using MahApps.Metro.IconPacks;

    using Res = Symlinker.Properties.Resources;

    /// <summary>Main application window. Only view glue lives here; the logic is in tested classes.</summary>
    [ExcludeFromCodeCoverage(Justification = "WPF view glue; needs a live window and user input.")]
    public partial class MainWindow : MetroWindow
    {
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

        private const string PlaceholderBrushKey = "MahApps.Brushes.Gray2";
        private const string PrimaryTextBrushKey = "MahApps.Brushes.ThemeForeground";

        private readonly LinkNameAutoFill linkNameAutoFill = new();
        private readonly IDialogService dialogs;
        private readonly LinkRunner linkRunner;
        private readonly LinkCreator linkCreator;

        private bool isFolder = true;

        internal MainWindow(LaunchOptions options = null)
        {
            launchOptions = options;
            InitializeComponent();
            dialogs = new MetroDialogService(this);
            linkRunner = new LinkRunner(
                dialogs,
                () => symlinksNeedAdmin,
                async () => await ElevatedLinker.StartAsync(),
                LinkCommand.RunAsync,
                UpdateAdminNotice);
            linkCreator = new LinkCreator(dialogs, linkRunner);
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
            if (!InputValidator.TargetFitsMode(destinationLocationTextBox.Text, isFolder))
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

        private LinkKind SelectedKind =>
            hardLinkRadio.IsChecked == true ? LinkKind.Hard
            : junctionRadio.IsChecked == true ? LinkKind.Junction
            : LinkKind.Symbolic;

        private void DestinationPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            var path = destinationLocationTextBox.Text;
            if (string.IsNullOrEmpty(path))
            {
                ShowTargetPlaceholder();
                AutoFillLinkName(string.Empty);
                return;
            }

            var name = UiHelpers.GetName(path);
            destinationFolderName.Text = name;
            destinationFolderName.FontStyle = FontStyles.Normal;
            destinationFolderName.SetResourceReference(ForegroundProperty, PrimaryTextBrushKey);
            AutoFillLinkName(name);
        }

        private void AutoFillLinkName(string targetName)
        {
            var name = linkNameAutoFill.Update(linkNameTextBox.Text, targetName);
            if (name != null)
                linkNameTextBox.Text = name;
        }

        /// <summary>Keeps the window open while files are being moved, so a move is never cut off halfway.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (linkCreator.IsMoving)
                e.Cancel = true;

            base.OnClosing(e);
        }

        private void ExploreButton1_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();

            // Start in the folder already entered so consecutive links don't need re-navigating
            var current = UiHelpers.GetExistingDirectory(linkLocationTextBox.Text, useParent: false);
            if (current != null)
                dialog.InitialDirectory = current;

            if (dialog.ShowDialog() == true)
                linkLocationTextBox.Text = dialog.FolderName;
        }

        private void ExploreButton2_Click(object sender, RoutedEventArgs e)
        {
            // Start next to the current target so sibling folders/files are one click away
            var current = UiHelpers.GetExistingDirectory(destinationLocationTextBox.Text, useParent: true);

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

        private async void CreateLink_Click(object sender, RoutedEventArgs e)
        {
            createLinkButton.IsEnabled = false;
            try
            {
                if (ValidateInputs())
                {
                    RefreshSymlinkAccessNow();
                    await linkCreator.CreateAsync(new LinkInput(
                        linkLocationTextBox.Text,
                        linkNameTextBox.Text,
                        destinationLocationTextBox.Text,
                        isFolder,
                        SelectedKind));
                }
            }
            finally
            {
                createLinkButton.IsEnabled = true;
            }
        }

        private async void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            await dialogs.ShowMessageAsync(
                Res.MessageBoxAboutTitle,
                string.Format(CultureInfo.CurrentUICulture, Res.AboutDescription, UiHelpers.GetAppVersion(), DateTime.Now.Year));
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
