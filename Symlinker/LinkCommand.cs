namespace Symlinker
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading.Tasks;

    using Res = Symlinker.Properties.Resources;

    /// <summary>One link to create. Sent to the elevated helper as JSON, so it only holds plain values.</summary>
    internal sealed class LinkRequest
    {
        /// <summary>"symbolic", "hard" or "junction".</summary>
        public string Type { get; set; }

        /// <summary>Full path of the link to create.</summary>
        public string Link { get; set; }

        /// <summary>What the link points to, as entered (a relative target stays relative).</summary>
        public string Target { get; set; }

        /// <summary>Whether the link is for a folder.</summary>
        public bool Directory { get; set; }
    }

    /// <summary>Outcome of a <see cref="LinkRequest"/>, with the message to show the user.</summary>
    internal sealed class LinkResult
    {
        public bool Success { get; set; }

        public string Message { get; set; }

        public static LinkResult Failed(string message) => new() { Success = false, Message = message };
    }

    /// <summary>Creates links with <c>mklink</c>. Used by the window and by the elevated helper.</summary>
    internal static class LinkCommand
    {
        public static async Task<LinkResult> RunAsync(LinkRequest request)
        {
            var arguments = BuildArguments(request);
            if (arguments == null)
                return LinkResult.Failed(Res.InvalidQuoteInPath);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                var outputText = (await output).Trim();
                var errorText = (await error).Trim();

                if (process.ExitCode == 0)
                    return new LinkResult { Success = true, Message = outputText.Length > 0 ? outputText : Res.LinkSuccessfullyCreated };

                return LinkResult.Failed(errorText.Length > 0 ? errorText : outputText);
            }
            catch (Exception)
            {
                return LinkResult.Failed(Res.CmdNotFound);
            }
        }

        /// <summary>
        /// Builds the cmd.exe arguments. Returns null for anything that could break out of the
        /// quotes, since the elevated helper runs whatever it is sent.
        /// </summary>
        private static string BuildArguments(LinkRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Link) || string.IsNullOrWhiteSpace(request.Target))
                return null;

            if (request.Link.IndexOfAny(UnsafeChars) >= 0 || request.Target.IndexOfAny(UnsafeChars) >= 0)
                return null;

            var typeFlag = request.Type switch
            {
                "hard" => "/H ",
                "junction" => "/J ",
                "symbolic" => string.Empty,
                _ => null,
            };

            if (typeFlag == null)
                return null;

            var directory = request.Directory ? "/D " : string.Empty;
            return $"/c mklink {directory}{typeFlag}\"{request.Link}\" \"{request.Target}\"";
        }

        // Quotes end the quoted path; line breaks end the command
        private static readonly char[] UnsafeChars = { '"', '\r', '\n', '\0' };
    }
}
