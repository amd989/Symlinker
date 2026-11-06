namespace Symlinker
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Automation;
    using System.Windows.Automation.Peers;
    using System.Windows.Controls;
    using System.Windows.Media;

    using ControlzEx.Theming;

    using MahApps.Metro.Controls.Dialogs;

    using Res = Symlinker.Properties.Resources;

    /// <summary>Marks a text field as holding a bad value, so its template can highlight it.</summary>
    public static class InputError
    {
        /// <summary>Identifies the HasError attached property.</summary>
        public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached(
            "HasError", typeof(bool), typeof(InputError), new PropertyMetadata(false));

        /// <summary>Whether the field currently holds a bad value.</summary>
        public static bool GetHasError(DependencyObject element) => (bool)element.GetValue(HasErrorProperty);

        /// <summary>Marks or clears the field as holding a bad value.</summary>
        public static void SetHasError(DependencyObject element, bool value) => element.SetValue(HasErrorProperty, value);
    }

    /// <summary>Input checks, inline errors, launch options and the administrator restart.</summary>
    public partial class MainWindow
    {
        private const string ErrorBrushKey = "Symlinker.Brushes.Error";
        private const string OnAccentBrushKey = "Symlinker.Brushes.OnAccent";

        // Windows 11 "critical" text colors, readable on both themes
        private static readonly Color ErrorColorLight = Color.FromRgb(0xC4, 0x2B, 0x1C);
        private static readonly Color ErrorColorDark = Color.FromRgb(0xFF, 0x99, 0xA4);

        // Fields with a problem, in the order they appear on screen, with the message for each
        private readonly List<KeyValuePair<TextBox, string>> inputErrors = new();

        private LaunchOptions launchOptions;

        // Whether creating a symbolic link would need a restart as administrator
        private bool symlinksNeedAdmin;
        private bool checkingSymlinkAccess;

        private void InitializeInputs()
        {
            // The app theme is applied after the window is created, so refresh again once loaded
            UpdateThemeBrushes();
            Loaded += (s, e) => UpdateThemeBrushes();
            ThemeManager.Current.ThemeChanged += OnThemeChanged;
            Closed += (s, e) => ThemeManager.Current.ThemeChanged -= OnThemeChanged;

            linkNameTextBox.TextChanged += InputTextBox_TextChanged;
            linkLocationTextBox.TextChanged += InputTextBox_TextChanged;
            destinationLocationTextBox.TextChanged += InputTextBox_TextChanged;

            // Tell the user up front when symbolic links need administrator rights,
            // instead of only after the form is filled in
            var shield = Elevation.GetShieldIcon();
            createLinkShield.Source = shield;
            adminNoticeShield.Source = shield;
            symLinkRadio.Checked += (s, e) => UpdateAdminNotice();
            symLinkRadio.Unchecked += (s, e) => UpdateAdminNotice();
            Loaded += (s, e) => RefreshSymlinkAccess();

            // Check again when coming back to the window, e.g. after turning on Developer Mode
            Activated += (s, e) =>
            {
                if (symlinksNeedAdmin)
                    RefreshSymlinkAccess();
            };
        }

        private async void RefreshSymlinkAccess()
        {
            if (checkingSymlinkAccess)
                return;

            checkingSymlinkAccess = true;
            try
            {
                // The check writes a throwaway link to the temp folder, so keep it off the UI thread
                symlinksNeedAdmin = !await Task.Run(Elevation.CanCreateSymbolicLinks);
            }
            catch (Exception)
            {
                // Don't warn when unsure; creating the link reports its own error
                symlinksNeedAdmin = false;
            }
            finally
            {
                checkingSymlinkAccess = false;
            }

            UpdateAdminNotice();
        }

        /// <summary>Shows the UAC shield and the footer notice while a symbolic link would need administrator rights.</summary>
        private void UpdateAdminNotice()
        {
            if (adminPanel == null || createLinkShield == null || symLinkRadio == null)
                return;

            var needsAdmin = symlinksNeedAdmin && symLinkRadio.IsChecked == true;
            createLinkShield.Visibility = needsAdmin && createLinkShield.Source != null ? Visibility.Visible : Visibility.Collapsed;
            adminNoticeShield.Visibility = adminNoticeShield.Source != null ? Visibility.Visible : Visibility.Collapsed;

            // Field errors take the same spot and matter more
            adminPanel.Visibility = needsAdmin && inputErrors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void RestartAsAdmin_Click(object sender, RoutedEventArgs e) => await RestartAsAdminAsync();

        private void OnThemeChanged(object sender, ThemeChangedEventArgs e) => UpdateThemeBrushes();

        private void UpdateThemeBrushes()
        {
            var theme = ThemeManager.Current.DetectTheme(Application.Current);
            var dark = theme?.BaseColorScheme == ThemeManager.BaseColorDark;
            Resources[ErrorBrushKey] = Frozen(dark ? ErrorColorDark : ErrorColorLight);

            // Text on the accent color: black or white, whichever reads better on the user's
            // Windows accent (MahApps' IdealForeground is white even on light accents)
            if (TryFindResource("MahApps.Colors.Accent") is Color accent)
            {
                // The accent is partly transparent, so judge the color actually seen on screen
                if (TryFindResource("MahApps.Colors.ThemeBackground") is Color background)
                    accent = Blend(accent, background);

                var onAccent = ContrastRatio(accent, Colors.Black) > ContrastRatio(accent, Colors.White) ? Colors.Black : Colors.White;
                Resources[OnAccentBrushKey] = Frozen(onAccent);
            }
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Color Blend(Color top, Color bottom)
        {
            var a = top.A / 255.0;
            byte Mix(byte t, byte b) => (byte)Math.Round((t * a) + (b * (1 - a)));
            return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
        }

        /// <summary>WCAG contrast ratio between two colors (1 to 21).</summary>
        private static double ContrastRatio(Color a, Color b)
        {
            static double Channel(byte value)
            {
                var c = value / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            static double Luminance(Color c) => (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

            var la = Luminance(a);
            var lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>Fills the form from the command line, e.g. after restarting as administrator.</summary>
        private void ApplyLaunchOptions()
        {
            var options = launchOptions;
            launchOptions = null;
            if (options == null)
                return;

            // Mode first: switching it may clear a target that doesn't fit
            if (string.Equals(options.Mode, "file", StringComparison.OrdinalIgnoreCase))
                fileTypeRadio.IsChecked = true;
            else if (string.Equals(options.Mode, "folder", StringComparison.OrdinalIgnoreCase))
                folderTypeRadio.IsChecked = true;

            if (string.Equals(options.LinkType, "hard", StringComparison.OrdinalIgnoreCase) && !isFolder)
                hardLinkRadio.IsChecked = true;
            else if (string.Equals(options.LinkType, "junction", StringComparison.OrdinalIgnoreCase) && isFolder)
                junctionRadio.IsChecked = true;

            if (options.LinkLocation != null)
                linkLocationTextBox.Text = options.LinkLocation;

            // Target before name: setting the target fills in the name automatically
            if (options.Target != null)
                destinationLocationTextBox.Text = options.Target;

            if (options.LinkName != null)
                linkNameTextBox.Text = options.LinkName;
        }

        private LaunchOptions GetCurrentOptions() => new()
        {
            Mode = isFolder ? "folder" : "file",
            LinkType = hardLinkRadio.IsChecked == true ? "hard" : junctionRadio.IsChecked == true ? "junction" : "symbolic",
            LinkLocation = linkLocationTextBox.Text,
            LinkName = linkNameTextBox.Text,
            Target = destinationLocationTextBox.Text,
        };

        /// <summary>
        /// Symbolic links need administrator rights unless Developer Mode is on. Checked before
        /// anything runs, so nothing on disk changes when the link can't be created anyway.
        /// Returns false when link creation should stop here.
        /// </summary>
        private async Task<bool> EnsureCanCreateLinkAsync()
        {
            if (hardLinkRadio.IsChecked == true || junctionRadio.IsChecked == true)
                return true;

            // Check again: Developer Mode may have been turned on since the window opened
            symlinksNeedAdmin = !Elevation.CanCreateSymbolicLinks();
            UpdateAdminNotice();
            if (!symlinksNeedAdmin)
                return true;

            var settings = new MetroDialogSettings
            {
                AnimateShow = false,
                AnimateHide = false,
                AffirmativeButtonText = Res.ButtonRestartAsAdmin,
                NegativeButtonText = Res.ButtonNotNow,
                DefaultButtonFocus = MessageDialogResult.Affirmative,

                // Smaller title so long translations fit the short window without scrolling
                DialogTitleFontSize = 20,
            };

            var answer = await this.ShowMessageAsync(Res.DialogElevationTitle, Res.DialogElevation, MessageDialogStyle.AffirmativeAndNegative, settings);
            if (answer == MessageDialogResult.Affirmative)
                await RestartAsAdminAsync();

            return false;
        }

        /// <summary>Restarts as administrator with the current form values. This window closes if the user accepts the UAC prompt.</summary>
        private async Task RestartAsAdminAsync()
        {
            try
            {
                // The new window opens with the same values; this one closes
                if (Elevation.RestartElevated(GetCurrentOptions()))
                    Close();
            }
            catch (Exception exception)
            {
                await this.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.MessageBoxExceptionOcurred + "\n" + exception.Message, settings: FastDialog);
            }
        }

        /// <summary>Checks every field, marks the bad ones and shows what's wrong. Returns true when all are fine.</summary>
        private bool ValidateInputs()
        {
            inputErrors.Clear();

            var name = linkNameTextBox.Text.Trim();
            var location = linkLocationTextBox.Text.Trim();
            var target = destinationLocationTextBox.Text.Trim();

            if (name.Length == 0)
                AddInputError(linkNameTextBox, Res.ErrorLinkNameMissing);
            else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                AddInputError(linkNameTextBox, Res.ErrorLinkNameInvalid);

            if (location.Length == 0)
                AddInputError(linkLocationTextBox, Res.ErrorLinkLocationMissing);
            else if (location.Contains('"'))
                AddInputError(linkLocationTextBox, Res.InvalidQuoteInPath);
            else if (!Directory.Exists(location))
                AddInputError(linkLocationTextBox, Res.ErrorLinkLocationNotFound);

            if (target.Length == 0)
                AddInputError(destinationLocationTextBox, isFolder ? Res.ErrorTargetFolderMissing : Res.ErrorTargetFileMissing);
            else if (target.Contains('"'))
                AddInputError(destinationLocationTextBox, Res.InvalidQuoteInPath);
            else if (isFolder && File.Exists(target))
                AddInputError(destinationLocationTextBox, Res.ErrorTargetIsFile);
            else if (!isFolder && Directory.Exists(target))
                AddInputError(destinationLocationTextBox, Res.ErrorTargetIsFolder);
            else if (!(isFolder ? Directory.Exists(target) : File.Exists(target)) && !LinkPathInUse(location, name))
                AddInputError(destinationLocationTextBox, isFolder ? Res.ErrorTargetFolderNotFound : Res.ErrorTargetFileNotFound);

            ShowInputErrors();
            if (inputErrors.Count != 0)
                inputErrors[0].Key.Focus();

            return inputErrors.Count == 0;
        }

        /// <summary>
        /// Whether something already exists where the link would go. Link creation decides what
        /// to do then, so a missing target isn't reported here.
        /// </summary>
        private static bool LinkPathInUse(string location, string name)
        {
            if (location.Length == 0 || name.Length == 0)
                return false;

            try
            {
                var path = Path.Combine(location, name);
                return Directory.Exists(path) || File.Exists(path);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private void AddInputError(TextBox box, string message) => inputErrors.Add(new KeyValuePair<TextBox, string>(box, message));

        private void ClearInputErrors()
        {
            inputErrors.Clear();
            ShowInputErrors();
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Editing a field clears its error
            var removed = inputErrors.RemoveAll(error => error.Key == sender);
            if (removed != 0)
                ShowInputErrors();
        }

        private void ShowInputErrors()
        {
            foreach (var box in new[] { linkNameTextBox, linkLocationTextBox, destinationLocationTextBox })
            {
                var message = inputErrors.Where(error => error.Key == box).Select(error => error.Value).FirstOrDefault();
                InputError.SetHasError(box, message != null);
                AutomationProperties.SetHelpText(box, message ?? string.Empty);
            }

            UpdateAdminNotice();

            if (inputErrors.Count == 0)
            {
                errorPanel.Visibility = Visibility.Collapsed;
                errorText.Text = string.Empty;
                return;
            }

            errorText.Text = inputErrors[0].Value;
            errorPanel.Visibility = Visibility.Visible;

            // Let screen readers announce the new message
            UIElementAutomationPeer.CreatePeerForElement(errorText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        /// <summary>Labels the target browse button for the current mode.</summary>
        private void UpdateTargetBrowseLabel()
        {
            var label = isFolder ? Res.TooltipBrowseTargetFolder : Res.TooltipBrowseTargetFile;
            targetBrowseButton.ToolTip = label;
            AutomationProperties.SetName(targetBrowseButton, label);
        }

        /// <summary>Whether the target path still makes sense after switching between folder and file mode.</summary>
        private static bool TargetFitsMode(string target, bool folderMode)
        {
            target = target.Trim();
            if (target.Length == 0)
                return false;

            return folderMode ? Directory.Exists(target) : File.Exists(target);
        }
    }
}
