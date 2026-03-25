namespace Symlinker
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Interop;

    using MahApps.Metro.Controls;

    using Res = Symlinker.Properties.Resources;

    /// <summary>Main application window.</summary>
    public partial class MainWindow : MetroWindow
    {
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

        private bool isFolder;

        public MainWindow()
        {
            InitializeComponent();

            linkTypeComboBox.SelectedIndex = 0;
            typeSelectorComboBox.SelectedIndex = 0;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Request Windows 11 rounded corners for this window
            var hwnd = new WindowInteropHelper(this).Handle;
            var preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        private void TypeSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Switcher();
        }

        private void Switcher()
        {
            if (groupBox1Header == null || groupBox2Header == null)
                return;

            if (typeSelectorComboBox.SelectedIndex == 0)
            {
                groupBox1Header.Text = Res.MainWindow_Switcher_Link_Folder;
                groupBox2Header.Text = Res.MainWindow_Switcher_Destination_Folder;
                label2.Text = Res.MainWindow_Switcher_Now_give_a_name_to_the_link_;
                label3.Text = Res.MainWindow_Switcher_Please_select_the_path_to_the_real_folder_you_want_to_link_;
                isFolder = true;

                // Add Directory Junction if not present
                bool hasJunction = false;
                foreach (ComboBoxItem item in linkTypeComboBox.Items)
                {
                    if (item.Content as string == "Directory Junction")
                    {
                        hasJunction = true;
                        break;
                    }
                }
                if (!hasJunction)
                    linkTypeComboBox.Items.Add(new ComboBoxItem { Content = "Directory Junction" });

                linkTypeComboBox.ToolTip = Res.TooltipLinkTypeFolderDescription;
            }
            else
            {
                groupBox1Header.Text = Res.MainWindow_Switcher_Link_File;
                groupBox2Header.Text = Res.MainWindow_Switcher_Destination_File;
                label2.Text = Res.MainWindow_Switcher_Now_give_a_name_to_your_file_;
                label3.Text = Res.MainWindow_Switcher_Please_select_the_path_to_the_real_file_you_want_to_link_;
                isFolder = false;

                // Remove Directory Junction for file mode
                ComboBoxItem junctionItem = null;
                foreach (ComboBoxItem item in linkTypeComboBox.Items)
                {
                    if (item.Content as string == "Directory Junction")
                    {
                        junctionItem = item;
                        break;
                    }
                }
                if (junctionItem != null)
                {
                    if (linkTypeComboBox.SelectedIndex == 2)
                        linkTypeComboBox.SelectedIndex = 0;
                    linkTypeComboBox.Items.Remove(junctionItem);
                }

                linkTypeComboBox.ToolTip = Res.TooltipLinkTypeFileDescription;
            }

            typeSelectorComboBox.ToolTip = Res.TooltipTypeSelectorDescription;
        }

        private string ComboBoxSelection()
        {
            switch (linkTypeComboBox.SelectedIndex)
            {
                case 1:
                    return "/H ";
                case 2:
                    return "/J ";
                default:
                    return string.Empty;
            }
        }

        private void CreateLink()
        {
            try
            {
                if (linkLocationTextBox.Text != string.Empty && linkNameTextBox.Text != string.Empty && destinationLocationTextBox.Text != string.Empty)
                {
                    if (isFolder && Directory.Exists(linkLocationTextBox.Text) && Directory.Exists(destinationLocationTextBox.Text))
                    {
                        var link = string.Format(
                            "\"{0}\\{1}\" ", linkLocationTextBox.Text, linkNameTextBox.Text);

                        var directories = Directory.GetDirectories(linkLocationTextBox.Text);

                        if (directories.Any(e => e.Split('\\').Last().Equals(linkNameTextBox.Text)))
                        {
                            var answer = MessageBox.Show(
                                Res.DialogFolderExists,
                                Res.DialogFolderExistsDialog,
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);
                            if (answer == MessageBoxResult.Yes)
                            {
                                var dir2Delete = directories.First(e => e.Split('\\').Last().Equals(linkNameTextBox.Text));
                                Directory.Delete(dir2Delete);
                                SendCommand(link);
                                return;
                            }

                            MessageBox.Show(
                                Res.LinkCreationAborted,
                                Res.LinkCreationAbortedWarning,
                                MessageBoxButton.OK,
                                MessageBoxImage.Stop);
                        }
                        else
                        {
                            SendCommand(link);
                        }
                    }
                    else if (Directory.Exists(linkLocationTextBox.Text) && File.Exists(destinationLocationTextBox.Text))
                    {
                        var link = string.Format(
                            "\"{0}\\{1}\" ", linkLocationTextBox.Text, linkNameTextBox.Text);

                        var files = Directory.GetFiles(linkLocationTextBox.Text);
                        if (files.Any(e => e.Split('\\').Last().Equals(linkNameTextBox.Text)))
                        {
                            var answer = MessageBox.Show(
                                Res.DialogDeleteFile,
                                Res.DialogDeleteFileWarning,
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);
                            if (answer == MessageBoxResult.Yes)
                            {
                                var file2Delete = files.First(e => e.Split('\\').Last().Equals(linkNameTextBox.Text));
                                File.Delete(file2Delete);
                                SendCommand(link);
                                return;
                            }
                            MessageBox.Show(
                                Res.LinkCreationAborted,
                                Res.LinkCreationAbortedWarning,
                                MessageBoxButton.OK,
                                MessageBoxImage.Stop);
                        }
                        else
                        {
                            SendCommand(link);
                        }
                    }
                    else
                    {
                        MessageBox.Show(Res.FilesOrFolderNotExists, Res.MessageBoxErrorTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show(Res.FillBlanks, Res.MessageBoxErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(Res.MessageBoxExceptionOcurred + exception.Message, Res.MessageBoxErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SendCommand(string link)
        {
            try
            {
                var target = string.Format(CultureInfo.InvariantCulture, "\"{0}\"", destinationLocationTextBox.Text);
                var typeLink = ComboBoxSelection();
                var directory = isFolder ? "/D " : string.Empty;
                var stringCommand = string.Format(CultureInfo.InvariantCulture, "/c mklink {0}{1}{2}{3}", directory, typeLink, link, target);
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = stringCommand,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };
                process.ErrorDataReceived += Process_ErrorDataReceived;
                process.OutputDataReceived += Process_OutputDataReceived;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                process.Close();
                process.Dispose();
            }
            catch (Exception)
            {
                MessageBox.Show(Res.CmdNotFound, Res.MessageBoxErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                MessageBox.Show(e.Data, Res.MessageBoxSuccessTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                MessageBox.Show(e.Data, Res.MessageBoxErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExploreButton1_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true)
                linkLocationTextBox.Text = dialog.FolderName;
        }

        private void ExploreButton2_Click(object sender, RoutedEventArgs e)
        {
            if (isFolder)
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog();
                if (dialog.ShowDialog() == true)
                    destinationLocationTextBox.Text = dialog.FolderName;
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog();
                if (dialog.ShowDialog() == true)
                    destinationLocationTextBox.Text = dialog.FileName;
            }
        }

        private void CreateLink_Click(object sender, RoutedEventArgs e)
        {
            CreateLink();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            string version;
            try
            {
                version = Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion");
            }
            catch
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                version = fvi.FileVersion;
            }

            MessageBox.Show(
                string.Format(CultureInfo.CurrentCulture, Res.AboutDescription, version),
                Res.MessageBoxAboutTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

        private void TextBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length != 0)
                {
                    var textBox = (TextBox)sender;
                    textBox.Text = files[0];
                }
            }
        }
    }
}
