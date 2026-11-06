namespace Symlinker.Tests
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.IO.Pipes;
    using System.Text;
    using System.Threading.Tasks;

    using Res = Symlinker.Properties.Resources;

    /// <summary>
    /// Tests the pipe protocol between the window and the elevated helper, in one process and
    /// without UAC: the test holds the window's end, and the helper loop runs on a background thread.
    /// </summary>
    public sealed class ElevatedLinkerTests : IDisposable
    {
        private const int Timeout = 5000;
        private static readonly string ThisProcess = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

        private readonly TempDir temp = new();

        public void Dispose() => temp.Dispose();

        private static NamedPipeServerStream CreateServer(out string name)
        {
            name = "Symlinker-test-" + Guid.NewGuid().ToString("N");
            return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }

        /// <summary>Starts the helper loop and waits until it has connected.</summary>
        private static async Task<(NamedPipeServerStream Pipe, Task<int> Helper)> StartHelperAsync(string parentId = null)
        {
            var pipe = CreateServer(out var name);
            var helper = Task.Run(() => ElevatedLinker.RunHelper(new[] { ElevatedLinker.HelperArg, name, parentId ?? ThisProcess }, Timeout));
            await pipe.WaitForConnectionAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(Timeout), TestContext.Current.CancellationToken);
            return (pipe, helper);
        }

        private static LinkRequest Junction(string link, string target) => new() { Type = "junction", Link = link, Target = target, Directory = true };

        [Fact]
        public async Task Helper_CreatesEachLinkItIsSent_UntilTheWindowCloses()
        {
            var target = temp.Dir("target");
            var (pipe, helper) = await StartHelperAsync();
            var linker = new ElevatedLinker(pipe, Process.GetCurrentProcess());

            Assert.True(linker.IsAlive);

            var first = await linker.CreateLinkAsync(Junction(temp.PathOf("first"), target));
            var second = await linker.CreateLinkAsync(Junction(temp.PathOf("second"), target));
            var refused = await linker.CreateLinkAsync(Junction(temp.PathOf("bad\"name"), target));

            Assert.True(first.Success, first.Message);
            Assert.True(second.Success, second.Message);
            Assert.True(LinkMover.IsLink(new DirectoryInfo(temp.PathOf("second"))));
            Assert.Equal((false, Res.InvalidQuoteInPath), (refused.Success, refused.Message));

            linker.Dispose();
            Assert.Equal(0, await helper.WaitAsync(TimeSpan.FromMilliseconds(Timeout), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Helper_AnswersRequestsItCantRead_WithAnError()
        {
            var (pipe, helper) = await StartHelperAsync();
            using (pipe)
            {
                var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);

                await writer.WriteLineAsync("this is not json");
                var reply = System.Text.Json.JsonSerializer.Deserialize<LinkResult>(await reader.ReadLineAsync(TestContext.Current.CancellationToken));

                Assert.False(reply.Success);
                Assert.NotEmpty(reply.Message);
            }

            await helper.WaitAsync(TimeSpan.FromMilliseconds(Timeout), TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task Helper_RefusesAWindowItWasNotStartedFor()
        {
            // Started for another process, so the pipe's owner doesn't match
            var (pipe, helper) = await StartHelperAsync(parentId: "4");
            using (pipe)
                Assert.Equal(3, await helper.WaitAsync(TimeSpan.FromMilliseconds(Timeout), TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("")]
        [InlineData(ElevatedLinker.HelperArg + " pipe")]
        [InlineData(ElevatedLinker.HelperArg + " pipe not-a-process-id")]
        public void Helper_RejectsBadArguments(string commandLine)
        {
            var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(2, ElevatedLinker.RunHelper(args));
        }

        [Fact]
        public void Helper_GivesUp_WhenTheWindowIsGone()
        {
            var args = new[] { ElevatedLinker.HelperArg, "Symlinker-test-missing-" + Guid.NewGuid().ToString("N"), ThisProcess };

            Assert.Equal(1, ElevatedLinker.RunHelper(args, connectTimeoutMs: 100));
        }

        [Fact]
        public async Task CreateLink_Throws_WhenTheHelperClosesWithoutAnswering()
        {
            var linker = await ConnectToFakeHelperAsync(reply: null);

            var error = await Assert.ThrowsAsync<IOException>(() => linker.CreateLinkAsync(Junction(@"C:\a", @"C:\b")));
            Assert.Contains("closed", error.Message);

            // The broken pipe shows the helper is gone
            Assert.False(linker.IsAlive);
        }

        [Fact]
        public async Task CreateLink_Throws_OnAnEmptyAnswer()
        {
            var linker = await ConnectToFakeHelperAsync(reply: "null");

            var error = await Assert.ThrowsAsync<IOException>(() => linker.CreateLinkAsync(Junction(@"C:\a", @"C:\b")));
            Assert.Contains("empty", error.Message);
        }

        [Fact]
        public async Task IsAlive_IsFalse_OnceTheHelperProcessHasExited()
        {
            using var exited = Process.Start(new ProcessStartInfo("cmd", "/c exit") { CreateNoWindow = true, UseShellExecute = false });
            await exited.WaitForExitAsync(TestContext.Current.CancellationToken);
            var (pipe, helper) = await StartHelperAsync();

            using (var linker = new ElevatedLinker(pipe, exited))
                Assert.False(linker.IsAlive);

            await helper.WaitAsync(TimeSpan.FromMilliseconds(Timeout), TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task IsAlive_IsFalse_WhenTheProcessCantBeChecked()
        {
            var process = Process.GetCurrentProcess();
            process.Dispose();
            var (pipe, helper) = await StartHelperAsync();

            using (var linker = new ElevatedLinker(pipe, process))
                Assert.False(linker.IsAlive);

            await helper.WaitAsync(TimeSpan.FromMilliseconds(Timeout), TestContext.Current.CancellationToken);
        }

        /// <summary>A helper that reads one request, sends <paramref name="reply"/> (or nothing) and hangs up.</summary>
        private static async Task<ElevatedLinker> ConnectToFakeHelperAsync(string reply)
        {
            var pipe = CreateServer(out var name);
            _ = Task.Run(() =>
            {
                using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
                client.Connect(Timeout);
                var reader = new StreamReader(client, new UTF8Encoding(false), false, 1024, leaveOpen: true);
                reader.ReadLine();
                if (reply != null)
                {
                    var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                    writer.WriteLine(reply);
                    client.WaitForPipeDrain();
                }
            });

            await pipe.WaitForConnectionAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(Timeout), TestContext.Current.CancellationToken);
            return new ElevatedLinker(pipe, Process.GetCurrentProcess());
        }
    }
}
