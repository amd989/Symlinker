namespace Symlinker
{
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;

    using Res = Symlinker.Properties.Resources;

    /// <summary>The kinds of link mklink can create.</summary>
    internal enum LinkKind
    {
        Symbolic,
        Hard,
        Junction,
    }

    /// <summary>Runs mklink and returns whether it worked, with the message to show the user.</summary>
    internal interface IMklinkRunner
    {
        Task<(bool Success, string Message)> RunAsync(string arguments);
    }

    /// <summary>Builds mklink command lines.</summary>
    internal static class Mklink
    {
        /// <summary>The cmd.exe arguments that create a link at <paramref name="linkPath"/> pointing to <paramref name="destination"/>.</summary>
        public static string BuildArguments(bool isFolder, LinkKind kind, string linkPath, string destination)
        {
            var directory = isFolder ? "/D " : string.Empty;
            var type = kind switch
            {
                LinkKind.Hard => "/H ",
                LinkKind.Junction => "/J ",
                _ => string.Empty,
            };

            return $"/c mklink {directory}{type}\"{linkPath}\" \"{destination}\"";
        }
    }

    /// <summary>Runs mklink through cmd.exe.</summary>
    internal sealed class CmdMklinkRunner : IMklinkRunner
    {
        private readonly string shell;

        public CmdMklinkRunner(string shell = "cmd")
        {
            this.shell = shell;
        }

        public async Task<(bool Success, string Message)> RunAsync(string arguments)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };

                using var process = new Process { StartInfo = processStartInfo };
                process.Start();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                var outputText = (await output).Trim();
                var errorText = (await error).Trim();

                if (process.ExitCode == 0)
                    return (true, outputText.Length > 0 ? outputText : Res.LinkSuccessfullyCreated);

                return (false, errorText.Length > 0 ? errorText : outputText);
            }
            catch (Exception)
            {
                return (false, Res.CmdNotFound);
            }
        }
    }
}
