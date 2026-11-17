namespace Symlinker
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
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

        // Window height for a single link; a batch adds room for the list
        private const double SingleLinkHeight = 280;
        private const double ListRowHeight = 30;
        private const int MaxVisibleRows = 5;
        private const double ListMargin = 8;

        private readonly LinkBatch batch = new();
        private readonly IDialogService dialogs;
        private readonly LinkRunner linkRunner;
        private readonly LinkCreator linkCreator;

        private bool isFolder = true;

        // Set while the cards are filled from a row, so that doesn't count as the user editing it
        private bool loadingRow;

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

            // The cards edit the selected row
            linkList.ItemsSource = batch.Rows;
            linkNameTextBox.TextChanged += (s, e) =>
            {
                if (!loadingRow)
                    batch.Selected.LinkName = linkNameTextBox.Text;
            };
            linkLocationTextBox.TextChanged += (s, e) =>
            {
                if (!loadingRow)
                    batch.Selected.LinkLocation = linkLocationTextBox.Text;
            };

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

            // Keep the target when it still fits the new mode (an existing folder or file). A batch
            // keeps every target; the ones that don't fit are marked when the links are created.
            if (!batch.IsBatch && !InputValidator.TargetFitsMode(destinationLocationTextBox.Text, isFolder))
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
            }
            else
            {
                destinationFolderName.Text = UiHelpers.GetName(path);
                destinationFolderName.FontStyle = FontStyles.Normal;
                destinationFolderName.SetResourceReference(ForegroundProperty, PrimaryTextBrushKey);
            }

            if (loadingRow)
                return;

            // The row fills in its link name from the target until the user types their own
            var row = batch.Selected;
            row.SetTarget(path);
            if (linkNameTextBox.Text != row.LinkName)
                linkNameTextBox.Text = row.LinkName;
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
                SetLinkLocation(dialog.FolderName);
        }

        private void ExploreButton2_Click(object sender, RoutedEventArgs e)
        {
            // Start next to the current target (or the previous row's) so siblings are one click away
            var current = UiHelpers.GetExistingDirectory(batch.TargetHint(batch.Selected) ?? string.Empty, useParent: true);

            // Picking several adds a row for each
            if (isFolder)
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
                if (current != null)
                    dialog.InitialDirectory = current;
                if (dialog.ShowDialog() == true)
                    SetTargets(dialog.FolderNames);
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
                if (current != null)
                    dialog.InitialDirectory = current;
                if (dialog.ShowDialog() == true)
                    SetTargets(dialog.FileNames);
            }
        }

        /// <summary>Uses the picked or dropped link location for this row, and for the rows that shared its old one.</summary>
        private void SetLinkLocation(string folder)
        {
            batch.SetLocation(folder);
            linkLocationTextBox.Text = folder;
        }

        /// <summary>The first target goes in the selected row; each other one gets a row of its own.</summary>
        private void SetTargets(IReadOnlyList<string> targets)
        {
            if (targets.Count == 1)
            {
                destinationLocationTextBox.Text = targets[0];
                return;
            }

            batch.AddTargets(targets);
            UpdateBatchView();
            LoadRow(batch.Selected);
        }

        private async void CreateLink_Click(object sender, RoutedEventArgs e)
        {
            createLinkButton.IsEnabled = false;
            try
            {
                if (!ValidateInputs())
                    return;

                RefreshSymlinkAccessNow();
                if (!batch.IsBatch)
                {
                    await linkCreator.CreateAsync(batch.Selected.ToInput(isFolder, SelectedKind));
                    return;
                }

                var rows = batch.Rows.ToList();
                var outcomes = await linkCreator.CreateBatchAsync(rows.Select(row => row.ToInput(isFolder, SelectedKind)).ToList());
                if (outcomes == null)
                    return;

                // Keep only the links that still need attention, so trying again doesn't redo the others
                batch.RemoveCreated(rows, outcomes);
                UpdateBatchView();
                LoadRow(batch.Selected);
            }
            finally
            {
                createLinkButton.IsEnabled = true;
            }
        }

        private void AddAnother_Click(object sender, RoutedEventArgs e)
        {
            batch.Add();
            UpdateBatchView();
            LoadRow(batch.Selected);
            targetBrowseButton.Focus();
        }

        private void RemoveRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is LinkRow row)
                RemoveRow(row);
        }

        private void LinkList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && linkList.SelectedItem is LinkRow row)
            {
                RemoveRow(row);
                (linkList.ItemContainerGenerator.ContainerFromItem(batch.Selected) as UIElement)?.Focus();
                e.Handled = true;
            }
        }

        private void RemoveRow(LinkRow row)
        {
            batch.Remove(row);
            UpdateBatchView();
            LoadRow(batch.Selected);
        }

        private void LinkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (linkList.SelectedItem is LinkRow row && row != batch.Selected)
            {
                batch.Selected = row;
                LoadRow(row);
            }
        }

        /// <summary>Fills the cards from a row and shows its problems, if any.</summary>
        private void LoadRow(LinkRow row)
        {
            loadingRow = true;
            try
            {
                linkLocationTextBox.Text = row.LinkLocation;
                destinationLocationTextBox.Text = row.Target;
                linkNameTextBox.Text = row.LinkName;
            }
            finally
            {
                loadingRow = false;
            }

            ShowInputErrors();
        }

        /// <summary>Shows the list for a batch, sized for up to five rows, or the single-link window.</summary>
        private void UpdateBatchView()
        {
            var count = batch.Rows.Count;
            var listHeight = Math.Min(count, MaxVisibleRows) * ListRowHeight;
            linkList.Height = listHeight;
            linkList.Visibility = batch.IsBatch ? Visibility.Visible : Visibility.Collapsed;
            Height = SingleLinkHeight + (batch.IsBatch ? listHeight + ListMargin : 0);

            linkList.SelectedItem = batch.Selected;
            linkList.ScrollIntoView(batch.Selected);

            createLinkText.Text = batch.IsBatch
                ? string.Format(CultureInfo.CurrentUICulture, Res.ButtonCreateLinks, count)
                : Res.ButtonCreateLink;
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
                    // The first item picks the mode; several items add a row each
                    var droppedIsFolder = Directory.Exists(files[0]);
                    if (droppedIsFolder != isFolder)
                    {
                        if (droppedIsFolder)
                            folderTypeRadio.IsChecked = true;
                        else
                            fileTypeRadio.IsChecked = true;
                    }

                    SetTargets(files);
                }
            }
        }

        private void LinkCard_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length != 0 && Directory.Exists(files[0]))
                    SetLinkLocation(files[0]);
            }
        }
    }
}
