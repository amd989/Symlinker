namespace Symlinker.Tests
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>A fresh folder under %TEMP%, removed with everything in it when the test ends.</summary>
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "Symlinker.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string PathOf(params string[] parts) => Path.Combine(Root, Path.Combine(parts));

        public string Dir(params string[] parts) => Directory.CreateDirectory(PathOf(parts)).FullName;

        public string File(string relativePath, string content = "content")
        {
            var path = PathOf(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        /// <summary>Creates a junction (a folder link that never needs administrator rights).</summary>
        public DirectoryInfo Junction(string linkPath, string target)
        {
            using var process = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{linkPath}\" \"{target}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new IOException($"mklink /J failed for {linkPath}");

            return new DirectoryInfo(linkPath);
        }

        public void Dispose()
        {
            try
            {
                ClearReadOnly(new DirectoryInfo(Root));
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort; a leftover temp folder must not fail a test
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void ClearReadOnly(DirectoryInfo directory)
        {
            directory.Attributes &= ~FileAttributes.ReadOnly;
            foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }))
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
                if (entry is DirectoryInfo subdirectory && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    ClearReadOnly(subdirectory);
            }
        }
    }

    /// <summary>
    /// Whether this process may create symbolic links (Developer Mode or elevation).
    /// Tests that need them are skipped otherwise, which also drops coverage below the gate.
    /// </summary>
    internal static class SymlinkSupport
    {
        public const string SkipReason = "Symbolic links need Developer Mode or an elevated prompt.";

        private static readonly Lazy<bool> Probe = new(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), "symlinker-tests-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.CreateSymbolicLink(path, path + "-target");
                File.Delete(path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        });

        public static bool Available => Probe.Value;

        public static void Require() => Assert.SkipUnless(Available, SkipReason);
    }

    /// <summary>Records dialogs instead of showing them, and answers questions with <see cref="Answer"/>.</summary>
    internal sealed class FakeDialogs : IDialogService
    {
        public List<(string Title, string Message)> Messages { get; } = new();

        public List<(string Title, string Message, string Yes, string No)> Questions { get; } = new();

        public bool Answer { get; set; } = true;

        public FakeProgressDialog Progress { get; } = new();

        public (string Title, string Message) Last => Messages[^1];

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add((title, message));
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message, string affirmativeText = null, string negativeText = null)
        {
            Questions.Add((title, message, affirmativeText, negativeText));
            return Task.FromResult(Answer);
        }

        public Task<IProgressDialog> ShowProgressAsync(string title, string message, string cancelText)
        {
            Progress.Shown = (title, message, cancelText);
            return Task.FromResult<IProgressDialog>(Progress);
        }
    }

    internal sealed class FakeProgressDialog : IProgressDialog
    {
        private int messageCount;

        public event EventHandler Canceled;

        public (string Title, string Message, string CancelText)? Shown { get; set; }

        public bool Indeterminate { get; private set; }

        public bool Closed { get; private set; }

        public ConcurrentQueue<double> Values { get; } = new();

        public ConcurrentQueue<string> ShownMessages { get; } = new();

        /// <summary>Runs once, when the first progress message is shown (after the size is known, before copying).</summary>
        public Action BeforeCopy { get; set; }

        public void Cancel() => Canceled?.Invoke(this, EventArgs.Empty);

        public void SetIndeterminate() => Indeterminate = true;

        public void SetProgress(double value) => Values.Enqueue(value);

        public void SetMessage(string message)
        {
            ShownMessages.Enqueue(message);
            if (Interlocked.Increment(ref messageCount) == 1)
                BeforeCopy?.Invoke();
        }

        public Task CloseAsync()
        {
            Closed = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>Records mklink calls; <see cref="Handler"/> decides the result.</summary>
    internal sealed class FakeMklink : IMklinkRunner
    {
        public List<string> Calls { get; } = new();

        public Func<string, (bool Success, string Message)> Handler { get; set; } = _ => (true, "created");

        public Task<(bool Success, string Message)> RunAsync(string arguments)
        {
            Calls.Add(arguments);
            return Task.FromResult(Handler(arguments));
        }
    }
}
