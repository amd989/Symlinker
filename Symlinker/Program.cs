namespace Symlinker
{
    using System;

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Start unelevated so drag and drop from Explorer works. Junctions, hard links and
            // (with Developer Mode) symbolic links don't need administrator rights; the window
            // offers to restart elevated only when a link actually needs it.
            var app = new App();
            app.InitializeComponent();
            app.Run(new MainWindow(LaunchOptions.Parse(args)));
        }
    }
}
