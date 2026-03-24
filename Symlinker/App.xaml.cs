namespace Symlinker
{
    using System.Windows;

    using ControlzEx.Theming;

    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Sync the app theme with the Windows OS accent color and light/dark mode
            // Sync both light/dark mode AND accent color with Windows settings
            ThemeManager.Current.ThemeSyncMode = ThemeSyncMode.SyncAll;
            ThemeManager.Current.SyncTheme();
        }
    }
}
