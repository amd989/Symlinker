namespace Symlinker
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.IO.Pipes;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Res = Symlinker.Properties.Resources;

    /// <summary>
    /// Creates links through a copy of Symlinker running as administrator, so the window itself
    /// stays unelevated (and keeps drag and drop). Starting it shows one UAC prompt. It then waits
    /// on a named pipe for link requests until disposed, so a caller can elevate before changing
    /// anything on disk and create the link later.
    /// </summary>
    internal sealed class ElevatedLinker : IDisposable
    {
        /// <summary>Command line switch that runs Symlinker as the helper instead of opening the window.</summary>
        public const string HelperArg = "--link-helper";

        private const int ErrorCancelled = 1223;
        private const int HelperConnectTimeoutMs = 10000;
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
        private static readonly Encoding PipeEncoding = new UTF8Encoding(false);

        private readonly NamedPipeServerStream pipe;
        private readonly Process process;
        private readonly StreamReader reader;
        private readonly StreamWriter writer;

        private ElevatedLinker(NamedPipeServerStream pipe, Process process)
        {
            this.pipe = pipe;
            this.process = process;
            reader = new StreamReader(pipe, PipeEncoding, false, 1024, leaveOpen: true);
            writer = new StreamWriter(pipe, PipeEncoding, 1024, leaveOpen: true) { AutoFlush = true };
        }

        /// <summary>
        /// Starts the helper as administrator. Returns null when the user cancels the UAC prompt.
        /// Throws when the helper starts but can't be reached.
        /// </summary>
        public static async Task<ElevatedLinker> StartAsync()
        {
            var pipeName = "Symlinker-" + Guid.NewGuid().ToString("N");

            // One instance only, so nothing else can open a second end with the same name
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            Process process = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    Arguments = string.Join(" ", HelperArg, pipeName, Environment.ProcessId.ToString(CultureInfo.InvariantCulture)),
                    UseShellExecute = true,
                    Verb = "runas",
                };

                try
                {
                    // Waits for the UAC prompt, so keep it off the UI thread
                    process = await Task.Run(() => Process.Start(startInfo));
                }
                catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
                {
                    pipe.Dispose();
                    return null;
                }

                if (process == null)
                    throw new InvalidOperationException(Res.ElevationHelperFailed);

                using var timeout = new CancellationTokenSource(StartTimeout);
                var connected = pipe.WaitForConnectionAsync(timeout.Token);
                var exited = process.WaitForExitAsync(timeout.Token);
                await Task.WhenAny(connected, exited);
                if (!connected.IsCompletedSuccessfully)
                    throw new InvalidOperationException(Res.ElevationHelperFailed);

                // Make sure the other end is the helper just started, not some other program
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientId) || clientId != (uint)process.Id)
                    throw new InvalidOperationException(Res.ElevationHelperFailed);

                return new ElevatedLinker(pipe, process);
            }
            catch
            {
                pipe.Dispose();
                process?.Dispose();
                throw;
            }
        }

        /// <summary>Creates a link as administrator.</summary>
        public async Task<LinkResult> CreateLinkAsync(LinkRequest request)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            var line = await reader.ReadLineAsync();
            if (line == null)
                throw new IOException("The administrator helper closed unexpectedly.");

            return JsonSerializer.Deserialize<LinkResult>(line) ?? throw new IOException("The administrator helper sent an empty reply.");
        }

        /// <summary>Closes the connection; the helper exits when it sees that.</summary>
        public void Dispose()
        {
            reader.Dispose();
            writer.Dispose();
            pipe.Dispose();
            process.Dispose();
        }

        /// <summary>
        /// Runs in the elevated copy: connects back to the window that started it and creates
        /// each link it is sent, until the window closes the connection. Returns the exit code.
        /// </summary>
        public static int RunHelper(string[] args)
        {
            if (args.Length != 3 || !uint.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var parentId))
                return 2;

            try
            {
                using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut);
                pipe.Connect(HelperConnectTimeoutMs);

                // Only take requests from the window that asked for elevation
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverId) || serverId != parentId)
                    return 3;

                // Not disposed: the window closing the pipe is the normal way to finish, and
                // disposing a writer then would flush into the closed pipe and throw
                var reader = new StreamReader(pipe, PipeEncoding, false, 1024, leaveOpen: true);
                var writer = new StreamWriter(pipe, PipeEncoding, 1024, leaveOpen: true) { AutoFlush = true };

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    LinkResult result;
                    try
                    {
                        var request = JsonSerializer.Deserialize<LinkRequest>(line);
                        result = LinkCommand.RunAsync(request).GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        result = LinkResult.Failed(exception.Message);
                    }

                    writer.WriteLine(JsonSerializer.Serialize(result));
                }

                return 0;
            }
            catch (Exception)
            {
                return 1;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);
    }
}
