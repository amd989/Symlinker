namespace Symlinker
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
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
    [ExcludeFromCodeCoverage(Justification = "WPF attached property plumbing.")]
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

    /// <summary>Inline errors, launch options and the administrator rights notice. The checks live in <see cref="InputValidator"/>.</summary>
    public partial class MainWindow
    {
        private const string ErrorBrushKey = "Symlinker.Brushes.Error";
        private const string OnAccentBrushKey = "Symlinker.Brushes.OnAccent";

        // Windows 11 "critical" text colors, readable on both themes
        private static readonly Color ErrorColorLight = Color.FromRgb(0xC4, 0x2B, 0x1C);
        private static readonly Color ErrorColorDark = Color.FromRgb(0xFF, 0x99, 0xA4);

        private LaunchOptions launchOptions;

        // Whether creating a symbolic link needs administrator rights (no admin, no Developer Mode)
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

            // The administrator helper stays up for the whole session; closing the window ends it
            Closed += (s, e) => linkRunner.Dispose();
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

        /// <summary>
        /// Shows the UAC shield and the footer notice while creating the link would show a UAC
        /// prompt. Once the user has approved it for this session, neither is needed.
        /// </summary>
        private void UpdateAdminNotice()
        {
            if (adminPanel == null || createLinkShield == null || symLinkRadio == null)
                return;

            var wouldPrompt = symlinksNeedAdmin && symLinkRadio.IsChecked == true && !linkRunner.HelperRunning;
            createLinkShield.Visibility = wouldPrompt && createLinkShield.Source != null ? Visibility.Visible : Visibility.Collapsed;
            adminNoticeShield.Visibility = adminNoticeShield.Source != null ? Visibility.Visible : Visibility.Collapsed;

            // Field errors take the same spot and matter more
            adminPanel.Visibility = wouldPrompt && FooterError == null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnThemeChanged(object sender, ThemeChangedEventArgs e) => UpdateThemeBrushes();

        private void UpdateThemeBrushes()
        {
            var theme = ThemeManager.Current.DetectTheme(Application.Current);
            var dark = theme?.BaseColorScheme == ThemeManager.BaseColorDark;
            Resources[ErrorBrushKey] = Frozen(dark ? ErrorColorDark : ErrorColorLight);

            // Text on the accent color: black or white, whichever reads better on the user's
            // Windows accent (MahApps' IdealForeground is white even on light accents)
            if (TryFindResource("MahApps.Colors.Accent") is Color accent)
                Resources[OnAccentBrushKey] = Frozen(ColorContrast.TextOn(accent, TryFindResource("MahApps.Colors.ThemeBackground") as Color?));
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>Fills the form from the command line.</summary>
        private void ApplyLaunchOptions()
        {
            var options = launchOptions;
            launchOptions = null;
            if (options == null)
                return;

            // Mode first: switching it may clear a target that doesn't fit
            if (options.IsFolderMode is bool folderMode)
                (folderMode ? folderTypeRadio : fileTypeRadio).IsChecked = true;

            if (options.Kind == LinkKind.Hard && !isFolder)
                hardLinkRadio.IsChecked = true;
            else if (options.Kind == LinkKind.Junction && isFolder)
                junctionRadio.IsChecked = true;

            if (options.LinkLocation != null)
                linkLocationTextBox.Text = options.LinkLocation;

            // Target before name: setting the target fills in the name automatically
            if (options.Target != null)
                destinationLocationTextBox.Text = options.Target;

            if (options.LinkName != null)
                linkNameTextBox.Text = options.LinkName;
        }

        /// <summary>
        /// Checks again whether a symbolic link would need administrator rights, since Developer
        /// Mode may have been turned on since the window opened.
        /// </summary>
        private void RefreshSymlinkAccessNow()
        {
            if (symLinkRadio.IsChecked != true)
                return;

            symlinksNeedAdmin = !Elevation.CanCreateSymbolicLinks();
            UpdateAdminNotice();
        }

        /// <summary>
        /// Checks every link, marks the bad fields and shows what's wrong. In a batch, the first row
        /// with a problem is selected so its fields show in the cards. Returns true when all are fine.
        /// </summary>
        private bool ValidateInputs()
        {
            var valid = batch.Validate(isFolder);
            if (!valid)
            {
                var row = batch.Rows.First(r => r.HasError);
                if (row != batch.Selected)
                {
                    batch.Selected = row;
                    UpdateBatchView();
                    LoadRow(row);
                }

                BoxFor(row.Errors[0].Field).Focus();
            }

            ShowInputErrors();
            return valid;
        }

        private TextBox BoxFor(InputField field) => field switch
        {
            InputField.LinkName => linkNameTextBox,
            InputField.LinkLocation => linkLocationTextBox,
            _ => destinationLocationTextBox,
        };

        private void ClearInputErrors()
        {
            batch.ClearErrors();
            ShowInputErrors();
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Editing a field clears its error; filling the cards from a row doesn't
            if (loadingRow || !batch.Selected.HasError)
                return;

            var field = sender == linkNameTextBox ? InputField.LinkName
                : sender == linkLocationTextBox ? InputField.LinkLocation
                : InputField.Target;
            batch.Selected.ClearError(field);
            ShowInputErrors();
        }

        /// <summary>
        /// The message for the footer: the selected link's first problem, or (in a batch) a pointer to
        /// the rows marked in the list. Null when nothing is wrong.
        /// </summary>
        private string FooterError =>
            batch.Selected.ErrorText ?? (batch.IsBatch && batch.Rows.Any(row => row.HasError) ? Res.ErrorBatchRows : null);

        private void ShowInputErrors()
        {
            var errors = batch.Selected.Errors;
            foreach (var box in new[] { linkNameTextBox, linkLocationTextBox, destinationLocationTextBox })
            {
                var message = errors.Where(error => BoxFor(error.Field) == box).Select(error => error.Message).FirstOrDefault();
                InputError.SetHasError(box, message != null);
                AutomationProperties.SetHelpText(box, message ?? string.Empty);
            }

            UpdateAdminNotice();

            var footer = FooterError;
            if (footer == null)
            {
                errorPanel.Visibility = Visibility.Collapsed;
                errorText.Text = string.Empty;
                return;
            }

            if (errorText.Text == footer && errorPanel.Visibility == Visibility.Visible)
                return;

            errorText.Text = footer;
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
    }
}
