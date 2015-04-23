// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MainWindow.cs" company="ShiftMe, Inc.">
//   2010-2013
// </copyright>
// <summary>
//   This class manages the window
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Symlink_Creator
{
    using System;
    using System.Deployment.Application;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Windows.Forms;

    using Symlink_Creator.Properties;

    /// <summary>
    ///     This class manages the window
    /// </summary>
    public partial class MainWindow : Form
    {
        #region Fields

        /// <summary>The tool tip.</summary>
        private readonly ToolTip toolTip = new ToolTip();

        /// <summary>The folder.</summary>
        private bool folder;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// The constructor
        /// </summary>
        public MainWindow()
        {
            this.InitializeComponent();
            this.toolTip.IsBalloon = true;
            this.linkTypeComboBox.SelectedIndex = 0;
            this.TypeSelector.SelectedIndex = 0;
        }

        #endregion

        #region Methods

        /// <summary>The combo box 1 mouse hover.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        private void ComboBox1MouseHover(object sender, EventArgs e)
        {
            this.toolTip.ToolTipIcon = ToolTipIcon.Info;
            this.toolTip.UseAnimation = true;
            this.toolTip.UseFading = true;
            this.toolTip.AutoPopDelay = 5000;
            this.toolTip.ToolTipTitle = "Symbolic Link types";
            this.toolTip.SetToolTip(
                this.linkTypeComboBox,
                "This option allows you to select the style of your symbolic link, either\nyou choose to use symbolic links, hard links or directory junctions.\n use symbolic links as a default");
        }

        /// <summary>
        ///     This Method lets you select the type of link you want to create
        /// </summary>
        /// <returns>String with the type of link to create</returns>
        private string ComboBoxSelection()
        {
            switch (this.linkTypeComboBox.SelectedIndex)
            {
                case 1:
                    return "/H ";
                case 2:
                    return "/J ";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        ///     Creates the link if the conditions are met
        /// </summary>
        private void CreateLink()
        {
            try
            {
                if (this.linkLocationTextBox.Text != string.Empty && this.linkNameComboBox.Text != string.Empty && this.destinationLocationTextBox.Text != string.Empty)
                {
                    // Everything needs to be filled...
                    if (this.folder && Directory.Exists(this.linkLocationTextBox.Text) && Directory.Exists(this.destinationLocationTextBox.Text))
                    {
                        // Ask if the folders exist
                        var link = string.Format(
                            "\"{0}\\{1}\" ", this.linkLocationTextBox.Text, this.linkNameComboBox.Text);
                        
                        // concatenates the link name with the folder name and then it adds a pair of ", to allow using directories with spaces..
                        var directories = Directory.GetDirectories(this.linkLocationTextBox.Text);

                        // gets the folders in the selected directory
                        if (directories.Any(e => e.Split('\\').Last().Equals(this.linkNameComboBox.Text)))
                        {
                            // looks for folders with the same name of the link name
                            // if found the program ask the user if he wants to delete the folder that is already there
                            var answer = MessageBox.Show(
                                Resources.DialogFolderExists,
                                Resources.DialogFolderExistsDialog,
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);
                            if (answer == DialogResult.Yes)
                            {
                                // if the answer is yes, the folder is deleted in order to create a new one
                                var dir2Delete = directories.First(e => e.Split('\\').Last().Equals(this.linkNameComboBox.Text));
                                Directory.Delete(dir2Delete);
                                this.SendCommand(link);
                                return;
                            }

                            MessageBox.Show(
                                Resources.LinkCreationAborted,
                                Resources.LinkCreationAbortedWarning,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Stop);
                        }
                        else
                        {
                            this.SendCommand(link);
                        }
                    }
                    else if (Directory.Exists(this.linkLocationTextBox.Text) && File.Exists(this.destinationLocationTextBox.Text))
                    {
                        // same thing as above... it just deletes files instead of folders
                        var link = string.Format(
                            "\"{0}\\{1}\" ", this.linkLocationTextBox.Text, this.linkNameComboBox.Text);
                        
                        var files = Directory.GetFiles(this.linkLocationTextBox.Text);
                        if (files.Any(e => e.Split('\\').Last().Equals(this.linkNameComboBox.Text)))
                        {
                            var answer = MessageBox.Show(
                                Resources.DialogDeleteFile,
                                Resources.DialogDeleteFileWarning,
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);
                            if (answer == DialogResult.Yes)
                            {
                                var file2Delete = files.First(e => e.Split('\\').Last().Equals(this.linkNameComboBox.Text));
                                File.Delete(file2Delete);
                                this.SendCommand(link);
                                return;
                            }
                            MessageBox.Show(
                                Resources.LinkCreationAborted,
                                Resources.LinkCreationAbortedWarning,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Stop);
                        }
                        else
                        {
                            this.SendCommand(link);
                        }
                    }
                    else
                    {
                        MessageBox.Show(Resources.FilesOrFolderNotExists, Resources.MessageBoxErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    MessageBox.Show(Resources.FillBlanks, Resources.MessageBoxErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch(Exception exception)
            {
                MessageBox.Show(Resources.MessageBoxExceptionOcurred + exception.Message, Resources.MessageBoxErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>This method build a string using the paramethers provided by the user, after that, it start a new
        ///     cmd.exe process with the string just built.</summary>
        /// <param name="link">Path to the place you want your symlink</param>
        private void SendCommand(string link)
        {
            try
            {
                var target = string.Format(CultureInfo.InvariantCulture,"\"{0}\"", this.destinationLocationTextBox.Text);
                // concatenates a pair of "", this is to make folders with spaces to work
                var typeLink = this.ComboBoxSelection();
                var directory = this.folder ? "/D " : string.Empty;
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
                process.ErrorDataReceived += this.process_ErrorDataReceived;
                process.OutputDataReceived += this.process_OutputDataReceived;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                process.Close();
                process.Dispose();
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.CmdNotFound, Resources.MessageBoxErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                MessageBox.Show(e.Data, Resources.MessageBoxSuccessTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                MessageBox.Show(e.Data, Resources.MessageBoxErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///     This method allows to switch modes between file or folder symlinks
        /// </summary>
        private void Switcher()
        {
            if (this.TypeSelector.SelectedIndex == 0)
            {
                this.groupBox1.Text = Resources.MainWindow_Switcher_Link_Folder;
                this.groupBox2.Text = Resources.MainWindow_Switcher_Destination_Folder;
                this.label2.Text = Resources.MainWindow_Switcher_Now_give_a_name_to_the_link_;
                this.label3.Text = Resources.MainWindow_Switcher_Please_select_the_path_to_the_real_folder_you_want_to_link_;
                this.folder = true;
            }
            else
            {
                this.groupBox1.Text = Resources.MainWindow_Switcher_Link_File;
                this.groupBox2.Text = Resources.MainWindow_Switcher_Destination_File;
                this.label2.Text = Resources.MainWindow_Switcher_Now_give_a_name_to_your_file_;
                this.label3.Text = Resources.MainWindow_Switcher_Please_select_the_path_to_the_real_file_you_want_to_link_;
                this.folder = false;
            }
        }

        /// <summary>The type selector_ mouse hover.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        private void TypeSelectorMouseHover(object sender, EventArgs e)
        {
            this.toolTip.ToolTipIcon = ToolTipIcon.Info;
            this.toolTip.UseAnimation = true;
            this.toolTip.UseFading = true;
            this.toolTip.AutoPopDelay = 10000;
            this.toolTip.ToolTipTitle = Resources.TooltipTypeSelectorTitle;
            this.toolTip.SetToolTip(this.TypeSelector, Resources.TooltipTypeSelectorDescription);
        }

        /// <summary>The type selector_ selected index changed.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        private void TypeSelectorSelectedIndexChanged(object sender, EventArgs e)
        {
            this.Switcher();
        }

        private void ExploreButton1Click(object sender, EventArgs e)
        {
            this.folderBrowser.ShowDialog();
            this.linkLocationTextBox.Text = this.folderBrowser.SelectedPath;
        }

        private void Explorebutton2Click(object sender, EventArgs e)
        {
            // if folder is true the folder browser will be shown
            if (this.folder)
            {
                this.folderBrowser.ShowDialog();
                this.destinationLocationTextBox.Text = this.folderBrowser.SelectedPath;
            }
            else
            {
                this.filesBrowser.ShowDialog();
                this.destinationLocationTextBox.Text = this.filesBrowser.FileName;
            }
        }

        private void CreateLinkClick(object sender, EventArgs e)
        {
            this.CreateLink();
        }

        private void HelpImageClick(object sender, EventArgs e)
        {
            string version;
            try
            {
                version = ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
            }
            catch
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                version = fvi.FileVersion;
            }

            MessageBox.Show(
                string.Format(CultureInfo.CurrentCulture, Resources.AboutDescription, version),
                Resources.MessageBoxAboutTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        #endregion

        private void TextBox_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void TextBox_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, false))
            {
                e.Effect = DragDropEffects.All;
            } 
        }
        
        private void TextBox_DragEnter(object sender, DragEventArgs e)
        {
            var textBox = (TextBox)sender;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length != 0)
            {
                textBox.Text = files[0];
            }
        }
    }
}