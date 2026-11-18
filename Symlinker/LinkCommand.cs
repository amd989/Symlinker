namespace Symlinker
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading.Tasks;

    /// <summary>The kinds of link mklink can create.</summary>
    internal enum LinkKind
    {
        Symbolic,
        Hard,
        Junction,
    }

    /// <summary>The names used for <see cref="LinkKind"/> on the command line and in <see cref="LinkRequest.Type"/>.</summary>
    internal static class LinkKinds
    {
        public static string ToName(LinkKind kind) => kind switch
        {
            LinkKind.Hard => "hard",
            LinkKind.Junction => "junction",
            _ => "symbolic",
        };

        /// <summary>Reads a link type name (any case). Returns null when it isn't one.</summary>
        public static LinkKind? Parse(string name) =>
            string.Equals(name, "symbolic", StringComparison.OrdinalIgnoreCase) ? LinkKind.Symbolic
            : string.Equals(name, "hard", StringComparison.OrdinalIgnoreCase) ? LinkKind.Hard
            : string.Equals(name, "junction", StringComparison.OrdinalIgnoreCase) ? LinkKind.Junction
            : null;
    }

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

    /// <summary>
    /// Why a link wasn't created. Sent over the helper's pipe as a number, so only add values at the end.
    /// </summary>
    internal enum LinkError
    {
        /// <summary>The link was created.</summary>
        None = 0,

        /// <summary>mklink failed for a reason Symlinker can't tell; <see cref="LinkResult.Message"/> has its text.</summary>
        Unknown = 1,

        /// <summary>A path has a quote or line break, so mklink wasn't run.</summary>
        InvalidPath = 2,

        /// <summary>cmd.exe couldn't be started.</summary>
        CommandNotFound = 3,

        /// <summary>mklink said it worked, but the link isn't there or points somewhere else.</summary>
        NotCreated = 4,

        /// <summary>Something already exists at the link path.</summary>
        LinkExists = 5,

        /// <summary>Windows denied writing to the folder that holds the link.</summary>
        AccessDenied = 6,

        /// <summary>Symbolic links need administrator rights or Developer Mode.</summary>
        NeedsAdmin = 7,

        /// <summary>A hard link and its target must be on the same drive.</summary>
        HardLinkAcrossDrives = 8,

        /// <summary>A junction can't point to a network folder.</summary>
        JunctionNeedsLocalFolder = 9,
    }

    /// <summary>
    /// Outcome of a <see cref="LinkRequest"/>. Only holds plain values, since the elevated helper
    /// sends it back as JSON. <see cref="LinkResultMessage"/> turns it into text for the user.
    /// </summary>
    internal sealed class LinkResult
    {
        public bool Success { get; set; }

        public LinkError Error { get; set; }

        /// <summary>mklink's own output (or an exception's message), in the language of Windows.</summary>
        public string Message { get; set; }

        public static LinkResult Created(string message = null) => new() { Success = true, Message = message };

        public static LinkResult Failed(string message) => Failed(LinkError.Unknown, message);

        public static LinkResult Failed(LinkError error, string message = null) => new() { Success = false, Error = error, Message = message };
    }

    /// <summary>Creates links with <c>mklink</c>. Used by the window and by the elevated helper.</summary>
    internal static class LinkCommand
    {
        public static Task<LinkResult> RunAsync(LinkRequest request) => RunAsync(request, "cmd");

        /// <summary>
        /// Runs mklink through <paramref name="shell"/>, which tests replace to check a missing shell.
        /// Success is decided by the exit code and then checked on disk; mklink's text is only kept as a detail.
        /// </summary>
        internal static async Task<LinkResult> RunAsync(LinkRequest request, string shell)
        {
            var arguments = BuildArguments(request);
            if (arguments == null)
                return LinkResult.Failed(LinkError.InvalidPath);

            // Checked first: after a failure, whatever is at the link path was already there
            var existedBefore = Path.Exists(request.Link);

            var run = await RunShellAsync(shell, arguments);
            if (run == null)
                return LinkResult.Failed(LinkError.CommandNotFound);

            var (exitCode, output, error) = run.Value;
            if (exitCode == 0)
                return LinkCheck.IsCreated(request) ? LinkResult.Created(output) : LinkResult.Failed(LinkError.NotCreated, output);

            var reason = LinkCheck.Diagnose(request, existedBefore, Elevation.CanCreateSymbolicLinks, LinkCheck.CanWriteTo);
            return LinkResult.Failed(reason, error.Length > 0 ? error : output);
        }

        /// <summary>Runs the shell and collects its output. Returns null when it can't be started.</summary>
        private static async Task<(int ExitCode, string Output, string Error)?> RunShellAsync(string shell, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = shell,
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

                return (process.ExitCode, (await output).Trim(), (await error).Trim());
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Builds the cmd.exe arguments. Returns null for anything that could break out of the
        /// quotes, since the elevated helper runs whatever it is sent.
        /// </summary>
        internal static string BuildArguments(LinkRequest request)
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
