namespace Symlink_Creator
{
    partial class MainWindow
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainWindow));
            this.label1 = new System.Windows.Forms.Label();
            this.linkLocationTextBox = new System.Windows.Forms.TextBox();
            this.folderBrowser = new Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog();
            this.exploreButton1 = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.linkNameComboBox = new System.Windows.Forms.TextBox();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.label5 = new System.Windows.Forms.Label();
            this.linkTypeComboBox = new System.Windows.Forms.ComboBox();
            this.exploreButton2 = new System.Windows.Forms.Button();
            this.destinationLocationTextBox = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.createLinkButton = new System.Windows.Forms.Button();
            this.aboutButton = new System.Windows.Forms.PictureBox();
            this.filesBrowser = new System.Windows.Forms.OpenFileDialog();
            this.TypeSelector = new System.Windows.Forms.ComboBox();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.errorProvider = new System.Windows.Forms.ErrorProvider(this.components);
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.aboutButton)).BeginInit();
            this.groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(9, 21);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(240, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Please select the place where you want your link:";
            // 
            // linkLocationTextBox
            // 
            this.linkLocationTextBox.AllowDrop = true;
            this.linkLocationTextBox.Location = new System.Drawing.Point(12, 39);
            this.linkLocationTextBox.Name = "linkLocationTextBox";
            this.linkLocationTextBox.Size = new System.Drawing.Size(312, 20);
            this.linkLocationTextBox.TabIndex = 1;
            this.linkLocationTextBox.WordWrap = false;
            this.linkLocationTextBox.DragDrop += new System.Windows.Forms.DragEventHandler(this.TextBox_DragDrop);
            this.linkLocationTextBox.DragEnter += new System.Windows.Forms.DragEventHandler(this.TextBox_DragEnter);
            this.linkLocationTextBox.DragOver += new System.Windows.Forms.DragEventHandler(this.TextBox_DragOver);
            // 
            // folderBrowser
            // 
            this.folderBrowser.IsFolderPicker = true;
            // 
            // exploreButton1
            // 
            this.exploreButton1.Location = new System.Drawing.Point(330, 37);
            this.exploreButton1.Name = "exploreButton1";
            this.exploreButton1.Size = new System.Drawing.Size(79, 23);
            this.exploreButton1.TabIndex = 2;
            this.exploreButton1.Text = "Explore...";
            this.exploreButton1.UseVisualStyleBackColor = true;
            this.exploreButton1.Click += new System.EventHandler(this.ExploreButton1Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(11, 66);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(142, 13);
            this.label2.TabIndex = 3;
            this.label2.Text = "Now give a name to the link:";
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.linkNameComboBox);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Controls.Add(this.exploreButton1);
            this.groupBox1.Controls.Add(this.linkLocationTextBox);
            this.groupBox1.Location = new System.Drawing.Point(12, 71);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(421, 92);
            this.groupBox1.TabIndex = 4;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Link Folder";
            // 
            // linkNameComboBox
            // 
            this.linkNameComboBox.Location = new System.Drawing.Point(176, 63);
            this.linkNameComboBox.Name = "linkNameComboBox";
            this.linkNameComboBox.Size = new System.Drawing.Size(148, 20);
            this.linkNameComboBox.TabIndex = 3;
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.label5);
            this.groupBox2.Controls.Add(this.linkTypeComboBox);
            this.groupBox2.Controls.Add(this.exploreButton2);
            this.groupBox2.Controls.Add(this.destinationLocationTextBox);
            this.groupBox2.Controls.Add(this.label3);
            this.groupBox2.Location = new System.Drawing.Point(13, 170);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(420, 102);
            this.groupBox2.TabIndex = 5;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Destination Folder";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(10, 67);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(112, 13);
            this.label5.TabIndex = 4;
            this.label5.Text = "Select the type of link:";
            // 
            // linkTypeComboBox
            // 
            this.linkTypeComboBox.FormattingEnabled = true;
            this.linkTypeComboBox.ImeMode = System.Windows.Forms.ImeMode.Off;
            this.linkTypeComboBox.Items.AddRange(new object[] {
            "Symbolic Link",
            "Hard Link",
            "Directory Junction"});
            this.linkTypeComboBox.Location = new System.Drawing.Point(175, 64);
            this.linkTypeComboBox.Name = "linkTypeComboBox";
            this.linkTypeComboBox.Size = new System.Drawing.Size(148, 21);
            this.linkTypeComboBox.TabIndex = 3;
            this.linkTypeComboBox.MouseHover += new System.EventHandler(this.ComboBox1MouseHover);
            // 
            // exploreButton2
            // 
            this.exploreButton2.Location = new System.Drawing.Point(329, 35);
            this.exploreButton2.Name = "exploreButton2";
            this.exploreButton2.Size = new System.Drawing.Size(75, 23);
            this.exploreButton2.TabIndex = 2;
            this.exploreButton2.Text = "Explore...";
            this.exploreButton2.UseVisualStyleBackColor = true;
            this.exploreButton2.Click += new System.EventHandler(this.Explorebutton2Click);
            // 
            // destinationLocationTextBox
            // 
            this.destinationLocationTextBox.AllowDrop = true;
            this.destinationLocationTextBox.Location = new System.Drawing.Point(11, 37);
            this.destinationLocationTextBox.Name = "destinationLocationTextBox";
            this.destinationLocationTextBox.Size = new System.Drawing.Size(312, 20);
            this.destinationLocationTextBox.TabIndex = 1;
            this.destinationLocationTextBox.DragDrop += new System.Windows.Forms.DragEventHandler(this.TextBox_DragDrop);
            this.destinationLocationTextBox.DragEnter += new System.Windows.Forms.DragEventHandler(this.TextBox_DragEnter);
            this.destinationLocationTextBox.DragOver += new System.Windows.Forms.DragEventHandler(this.TextBox_DragOver);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(8, 20);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(271, 13);
            this.label3.TabIndex = 0;
            this.label3.Text = "Please select the path to the real folder you want to link:";
            // 
            // createLinkButton
            // 
            this.createLinkButton.Location = new System.Drawing.Point(342, 278);
            this.createLinkButton.Name = "createLinkButton";
            this.createLinkButton.Size = new System.Drawing.Size(91, 23);
            this.createLinkButton.TabIndex = 6;
            this.createLinkButton.Text = "Create Link";
            this.createLinkButton.UseVisualStyleBackColor = true;
            this.createLinkButton.Click += new System.EventHandler(this.CreateLinkClick);
            // 
            // aboutButton
            // 
            this.aboutButton.Image = global::Symlink_Creator.Properties.Resources.info;
            this.aboutButton.Location = new System.Drawing.Point(13, 285);
            this.aboutButton.Name = "aboutButton";
            this.aboutButton.Size = new System.Drawing.Size(16, 16);
            this.aboutButton.TabIndex = 7;
            this.aboutButton.TabStop = false;
            this.aboutButton.Click += new System.EventHandler(this.HelpImageClick);
            // 
            // TypeSelector
            // 
            this.TypeSelector.FormattingEnabled = true;
            this.TypeSelector.ImeMode = System.Windows.Forms.ImeMode.Off;
            this.TypeSelector.Items.AddRange(new object[] {
            "Folder symbolic link",
            "File symbolic link"});
            this.TypeSelector.Location = new System.Drawing.Point(14, 19);
            this.TypeSelector.Name = "TypeSelector";
            this.TypeSelector.Size = new System.Drawing.Size(239, 21);
            this.TypeSelector.TabIndex = 1;
            this.TypeSelector.SelectedIndexChanged += new System.EventHandler(this.TypeSelectorSelectedIndexChanged);
            this.TypeSelector.MouseHover += new System.EventHandler(this.TypeSelectorMouseHover);
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.TypeSelector);
            this.groupBox3.Location = new System.Drawing.Point(12, 12);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(421, 53);
            this.groupBox3.TabIndex = 10;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Select the type of symlink that you want to create:";
            // 
            // errorProvider
            // 
            this.errorProvider.ContainerControl = this;
            // 
            // MainWindow
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(456, 317);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.aboutButton);
            this.Controls.Add(this.createLinkButton);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "MainWindow";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Symbolic Link Creator";
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.aboutButton)).EndInit();
            this.groupBox3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox linkLocationTextBox;
        private Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog folderBrowser;
        private System.Windows.Forms.Button exploreButton1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TextBox linkNameComboBox;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Button exploreButton2;
        private System.Windows.Forms.TextBox destinationLocationTextBox;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Button createLinkButton;
        private System.Windows.Forms.PictureBox aboutButton;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.ComboBox linkTypeComboBox;
        private System.Windows.Forms.OpenFileDialog filesBrowser;
        private System.Windows.Forms.ComboBox TypeSelector;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.ErrorProvider errorProvider;
    }
}

