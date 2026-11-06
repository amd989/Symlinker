namespace Symlinker
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    [ExcludeFromCodeCoverage(Justification = "Entry point that starts the WPF application.")]
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Hidden mode: the elevated copy that creates symbolic links for an unelevated window
            if (args.Length > 0 && args[0] == ElevatedLinker.HelperArg)
                return ElevatedLinker.RunHelper(args);

            // Start unelevated so drag and drop from Explorer works. Junctions, hard links and
            // (with Developer Mode) symbolic links don't need administrator rights; otherwise only
            // the link creation step runs elevated, with a single UAC prompt.
            var app = new App();
            app.InitializeComponent();
            return app.Run(new MainWindow(LaunchOptions.Parse(args)));
        }
    }
}
