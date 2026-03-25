namespace Symlinker
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (string.IsNullOrEmpty((from o in args where o == "--engage" select o).FirstOrDefault()))
            {
                var processInfo = new ProcessStartInfo();
                processInfo.Verb = "runas";
                processInfo.FileName = Environment.ProcessPath;
                processInfo.Arguments = string.Join(" ", args.Concat(new[] { "--engage" }).ToArray());
                try
                {
                    var p = Process.Start(processInfo);
                    p.WaitForExit();
                }
                catch (Win32Exception)
                {
                    //Do nothing. Probably the user cancelled the UAC window or provided invalid credentials.
                }

                return;
            }
            else
            {
                var app = new App();
                app.InitializeComponent();
                app.Run(new MainWindow());
            }
        }
    }
}
