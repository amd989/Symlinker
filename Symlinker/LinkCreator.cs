namespace Symlinker
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    using Res = Symlinker.Properties.Resources;

    /// <summary>What the user entered in the window.</summary>
    internal sealed record LinkInput(string LinkLocation, string LinkName, string Destination, bool IsFolder, LinkKind Kind);

    /// <summary>What became of one link.</summary>
    internal enum LinkStatus
    {
        Created,
        Failed,

        /// <summary>The user said no (to replacing or moving, or canceled a move).</summary>
        Skipped,
    }

    /// <summary>
    /// How creating one link went, with the dialog that reports it. <paramref name="Notable"/> marks a
    /// created link whose message the batch summary should still show (e.g. a leftover copy to delete).
    /// </summary>
    internal sealed record LinkOutcome(LinkStatus Status, string Title, string Message, bool Notable = false)
    {
        public static LinkOutcome Error(string message) => new(LinkStatus.Failed, Res.MessageBoxErrorTitle, message);

        public static LinkOutcome Aborted() => new(LinkStatus.Skipped, Res.LinkCreationAbortedWarning, Res.LinkCreationAborted);
    }

    /// <summary>What creating one link involves, worked out (and agreed to) before anything on disk changes.</summary>
    internal sealed class LinkPlan
    {
        /// <summary>The link to create. Null when <see cref="Outcome"/> is already decided.</summary>
        public LinkRequest Request { get; init; }

        /// <summary>An existing entry at the link path to delete first, with <see cref="Delete"/>.</summary>
        public string Existing { get; init; }

        public Action<string> Delete { get; init; }

        /// <summary>A real file or folder at the link path to move to the target first.</summary>
        public FileSystemInfo MoveSource { get; init; }

        /// <summary>Set when the link won't be created (bad input or the user said no), or once it was tried.</summary>
        public LinkOutcome Outcome { get; set; }
    }

    /// <summary>
    /// Creates links from what the user entered: validates it, asks before replacing anything,
    /// and offers to move an existing file or folder to the target and leave a link behind.
    /// </summary>
    internal sealed class LinkCreator
    {
        private readonly IDialogService dialogs;
        private readonly ILinkRunner runner;

        public LinkCreator(IDialogService dialogs, ILinkRunner runner)
        {
            this.dialogs = dialogs;
            this.runner = runner;
        }

        /// <summary>True while files are being moved, so the window can't be closed halfway through.</summary>
        public bool IsMoving { get; private set; }

        /// <summary>Whether two paths are on the same drive, so a move is a rename instead of a copy.</summary>
        internal Func<string, string, bool> IsSameVolume { get; set; } = LinkMover.IsSameVolume;

        /// <summary>Minimum time between progress updates while copying.</summary>
        internal TimeSpan ProgressInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>Whether this copy runs from the Microsoft Store package.</summary>
        internal Func<bool> IsPackaged { get; set; } = Elevation.IsPackaged;

        /// <summary>The user's AppData folder (the parent of Local and Roaming).</summary>
        internal string AppDataFolder { get; set; } =
            Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        /// <summary>Creates one link, asking before replacing or moving anything, and shows how it went.</summary>
        public async Task CreateAsync(LinkInput request)
        {
            try
            {
                var plan = await PlanAsync(request, inBatch: false);
                if (plan.Outcome == null)
                {
                    // Ask for administrator rights before anything on disk changes; a declined prompt quietly stops here
                    if (!await runner.PrepareAsync(plan.Request))
                        return;

                    plan.Outcome = await ExecuteAsync(plan);
                }

                await dialogs.ShowMessageAsync(plan.Outcome.Title, plan.Outcome.Message);
            }
            catch (Exception exception)
            {
                await dialogs.ShowMessageAsync(Res.MessageBoxErrorTitle, ExceptionMessage(exception));
            }
        }

        /// <summary>
        /// Creates several links in one step. Every question (replace, or move to the target) is asked
        /// first, then administrator rights once, before anything on disk changes. A link that fails
        /// doesn't stop the others, and one summary at the end lists what happened.
        /// Returns the outcome of each input, or null when the UAC prompt was declined (nothing changed).
        /// </summary>
        public async Task<IReadOnlyList<LinkOutcome>> CreateBatchAsync(IReadOnlyList<LinkInput> inputs)
        {
            if (inputs.Count == 0)
                return Array.Empty<LinkOutcome>();

            var plans = new List<LinkPlan>();
            foreach (var input in inputs)
            {
                try
                {
                    plans.Add(await PlanAsync(input, inBatch: true));
                }
                catch (Exception exception)
                {
                    plans.Add(new LinkPlan { Outcome = LinkOutcome.Error(ExceptionMessage(exception)) });
                }
            }

            // One kind of link for the whole batch, so one check settles administrator rights for all of them
            var pending = plans.Where(plan => plan.Outcome == null).ToList();
            if (pending.Count != 0 && !await runner.PrepareAsync(pending[0].Request))
                return null;

            foreach (var plan in pending)
                plan.Outcome = await ExecuteAsync(plan);

            var outcomes = plans.Select(plan => plan.Outcome).ToList();
            await dialogs.ShowMessageAsync(
                outcomes.All(outcome => outcome.Status == LinkStatus.Created) ? Res.MessageBoxSuccessTitle : Res.BatchSummaryPartialTitle,
                Summarize(inputs, outcomes));
            return outcomes;
        }

        /// <summary>The batch summary: how many links were created, then which ones and which not (and why).</summary>
        private static string Summarize(IReadOnlyList<LinkInput> inputs, IReadOnlyList<LinkOutcome> outcomes)
        {
            // Links in the folder most of the batch goes in are listed by name, which is much shorter;
            // the others by their full path
            static string Folder(LinkInput input) => input.LinkLocation.Trim().TrimEnd('\\', '/');
            var mainFolder = inputs.GroupBy(Folder, StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count()).First().Key;

            string Line(LinkInput input, LinkOutcome outcome, bool withReason)
            {
                var path = string.Equals(Folder(input), mainFolder, StringComparison.OrdinalIgnoreCase)
                    ? input.LinkName.Trim()
                    : Path.Combine(input.LinkLocation.Trim(), input.LinkName.Trim());
                return withReason ? path + ": " + Regex.Replace(outcome.Message.Trim(), @"\s*\n\s*", " ") : path;
            }

            var created = outcomes.Count(outcome => outcome.Status == LinkStatus.Created);
            var text = new StringBuilder(string.Format(CultureInfo.CurrentUICulture, Res.BatchSummaryCount, created, outcomes.Count));

            if (created != 0)
            {
                text.Append("\n\n").Append(Res.BatchSummaryCreated);
                for (var index = 0; index < outcomes.Count; index++)
                {
                    if (outcomes[index].Status == LinkStatus.Created)
                        text.Append('\n').Append(Line(inputs[index], outcomes[index], outcomes[index].Notable));
                }
            }

            if (created != outcomes.Count)
            {
                text.Append("\n\n").Append(Res.BatchSummaryNotCreated);
                for (var index = 0; index < outcomes.Count; index++)
                {
                    if (outcomes[index].Status != LinkStatus.Created)
                        text.Append('\n').Append(Line(inputs[index], outcomes[index], true));
                }
            }

            return text.ToString();
        }

        private static string ExceptionMessage(Exception exception) => Res.MessageBoxExceptionOcurred + "\n" + exception.Message;

        /// <summary>
        /// Works out what creating the link involves and asks the user any question it needs, without
        /// changing anything on disk. The plan already has an <see cref="LinkPlan.Outcome"/> when the
        /// link won't be created (bad input, or the user said no).
        /// </summary>
        private async Task<LinkPlan> PlanAsync(LinkInput request, bool inBatch)
        {
            request = request with
            {
                LinkLocation = request.LinkLocation.Trim(),
                LinkName = request.LinkName.Trim(),
                Destination = request.Destination.Trim(),
            };

            var linkLocation = request.LinkLocation;
            var linkName = request.LinkName;
            var destination = request.Destination;

            if (string.IsNullOrWhiteSpace(linkLocation) || string.IsNullOrWhiteSpace(linkName) || string.IsNullOrWhiteSpace(destination))
                return new LinkPlan { Outcome = LinkOutcome.Error(Res.FillBlanks) };

            if (linkName.Contains('"') || linkLocation.Contains('"') || destination.Contains('"'))
                return new LinkPlan { Outcome = LinkOutcome.Error(Res.InvalidQuoteInPath) };

            // Full path: the administrator helper runs in a different working folder
            var linkPath = Path.GetFullPath(Path.Combine(linkLocation, linkName));

            // A real file or folder already sits where the link goes and the target is free:
            // offer to move it to the target and leave a link behind (#33)
            if (Directory.Exists(linkLocation) && Path.IsPathFullyQualified(destination))
            {
                FileSystemInfo existing = request.IsFolder ? new DirectoryInfo(linkPath) : new FileInfo(linkPath);
                if (existing.Exists && !LinkMover.IsLink(existing) && IsFreeMoveTarget(destination, request.IsFolder))
                    return await PlanMoveAsync(request, existing, linkPath, Path.GetFullPath(destination), inBatch);
            }

            if (request.IsFolder && Directory.Exists(linkLocation) && Directory.Exists(destination))
            {
                return await PlanReplaceAsync(
                    request,
                    linkPath,
                    Directory.GetDirectories(linkLocation),
                    Res.DialogFolderExistsDialog,
                    Res.DialogFolderExists,
                    path => Directory.Delete(path, true),
                    inBatch);
            }

            if (!request.IsFolder && Directory.Exists(linkLocation) && File.Exists(destination))
            {
                return await PlanReplaceAsync(
                    request,
                    linkPath,
                    Directory.GetFiles(linkLocation),
                    Res.DialogDeleteFileWarning,
                    Res.DialogDeleteFile,
                    File.Delete,
                    inBatch);
            }

            return new LinkPlan { Outcome = LinkOutcome.Error(Res.FilesOrFolderNotExists) };
        }

        /// <summary>
        /// Whether the target path is free to receive a moved item: it doesn't exist yet,
        /// or (in folder mode) it is an empty folder.
        /// </summary>
        private static bool IsFreeMoveTarget(string destination, bool isFolder)
        {
            if (File.Exists(destination))
                return false;

            if (!Directory.Exists(destination))
                return true;

            return isFolder && !Directory.EnumerateFileSystemEntries(destination).Any();
        }

        /// <summary>In a batch, a question starts with the link it is about, since several may be asked in a row.</summary>
        private static string Question(string message, string linkPath, bool inBatch) => inBatch ? linkPath + "\n\n" + message : message;

        /// <summary>
        /// Plans the link, first asking to delete an entry with the same name from <paramref name="siblings"/>.
        /// </summary>
        private async Task<LinkPlan> PlanReplaceAsync(LinkInput request, string linkPath, string[] siblings, string title, string question, Action<string> delete, bool inBatch)
        {
            var existing = siblings.FirstOrDefault(e => Path.GetFileName(e).Equals(request.LinkName, StringComparison.OrdinalIgnoreCase));
            if (existing != null && !await dialogs.ConfirmAsync(title, Question(question, linkPath, inBatch)))
                return new LinkPlan { Outcome = LinkOutcome.Aborted() };

            return new LinkPlan
            {
                Request = ToLinkRequest(request, linkPath, request.Destination),
                Existing = existing,
                Delete = delete,
            };
        }

        private static LinkRequest ToLinkRequest(LinkInput input, string linkPath, string target) => new()
        {
            Type = LinkKinds.ToName(input.Kind),
            Link = linkPath,
            Target = target,
            Directory = input.IsFolder,
        };

        private async Task<LinkPlan> PlanMoveAsync(LinkInput request, FileSystemInfo existing, string linkPath, string target, bool inBatch)
        {
            // In the Store package, Windows keeps files this process creates under AppData in a
            // private per-package copy: other apps can't see them and uninstalling deletes them.
            // Links are fine (mklink runs in cmd.exe), but moved files would end up hidden there.
            if (IsPackaged() && LinkMover.IsSameOrInside(target, AppDataFolder))
                return new LinkPlan { Outcome = LinkOutcome.Error(Res.MoveIntoAppDataUnsupported) };

            var message = string.Format(
                CultureInfo.CurrentUICulture,
                request.IsFolder ? Res.DialogMoveFolder : Res.DialogMoveFile,
                existing.Name,
                target);

            if (!await dialogs.ConfirmAsync(Res.DialogMoveTitle, Question(message, linkPath, inBatch), Res.ButtonMoveAndLink, Res.ButtonCancel))
                return new LinkPlan { Outcome = LinkOutcome.Aborted() };

            return new LinkPlan { Request = ToLinkRequest(request, linkPath, target), MoveSource = existing };
        }

        /// <summary>Carries out a plan: moves and links, or replaces and links, or just links. Never throws.</summary>
        private async Task<LinkOutcome> ExecuteAsync(LinkPlan plan)
        {
            try
            {
                if (plan.MoveSource != null)
                {
                    IsMoving = true;
                    try
                    {
                        return await MoveAndLinkAsync(plan.Request.Directory, plan.MoveSource, plan.Request);
                    }
                    finally
                    {
                        IsMoving = false;
                    }
                }

                if (plan.Existing != null)
                    plan.Delete(plan.Existing);

                var result = await runner.RunAsync(plan.Request);
                return result.Success
                    ? new LinkOutcome(LinkStatus.Created, Res.MessageBoxSuccessTitle, result.Message)
                    : LinkOutcome.Error(result.Message);
            }
            catch (Exception exception)
            {
                return LinkOutcome.Error(ExceptionMessage(exception));
            }
        }

        /// <summary>
        /// Moves <paramref name="source"/> to the link's target and creates the link where it was.
        /// Any failure before the link exists puts everything back the way it was.
        /// </summary>
        private async Task<LinkOutcome> MoveAndLinkAsync(bool isFolder, FileSystemInfo source, LinkRequest linkRequest)
        {
            var target = linkRequest.Target;
            var originalPath = source.FullName;

            // The target is free (missing or an empty folder), so the source can't be inside it;
            // only the other way around needs checking
            if (LinkMover.IsSameOrInside(target, originalPath))
                return LinkOutcome.Error(Res.MoveTargetInsideSource);

            var targetWasEmptyFolder = Directory.Exists(target);

            // Where the original currently lives; it only leaves originalPath once we start moving
            var currentPath = originalPath;
            var copiedToTarget = false;

            try
            {
                // Creates the folder that holds the target (a no-op when the target is a drive root)
                Directory.CreateDirectory(Path.GetFullPath(Path.Combine(target, "..")));

                // Only ever an empty folder here (checked before offering the move)
                if (targetWasEmptyFolder)
                    Directory.Delete(target);

                if (IsSameVolume(originalPath, target))
                {
                    // Same drive: a rename, instant and atomic
                    LinkMover.Rename(originalPath, target);
                    currentPath = target;
                }
                else
                {
                    // Different drive: set the original aside first. This fails right away if
                    // something has its files open, before any time is spent copying.
                    var staging = LinkMover.GetUnusedPath(originalPath + ".symlinker-move");
                    LinkMover.Rename(originalPath, staging);
                    currentPath = staging;

                    copiedToTarget = true;
                    FileSystemInfo staged = isFolder ? new DirectoryInfo(staging) : new FileInfo(staging);
                    var copyError = await CopyWithProgressAsync(staged, target);
                    if (copyError != null)
                    {
                        var outcome = copyError == Res.MoveCanceled
                            ? new LinkOutcome(LinkStatus.Skipped, Res.LinkCreationAbortedWarning, copyError)
                            : LinkOutcome.Error(copyError);
                        return RollBackMove(originalPath, currentPath, target, copiedToTarget, targetWasEmptyFolder, outcome);
                    }
                }

                var linked = await runner.RunAsync(linkRequest);
                if (!linked.Success)
                    return RollBackMove(originalPath, currentPath, target, copiedToTarget, targetWasEmptyFolder, MoveFailed(linked.Message));

                if (currentPath != target)
                {
                    try
                    {
                        LinkMover.Delete(currentPath);
                    }
                    catch (Exception)
                    {
                        // The link works; the user only needs to know about the leftover copy
                        return new LinkOutcome(
                            LinkStatus.Created,
                            Res.MessageBoxSuccessTitle,
                            string.Format(CultureInfo.CurrentUICulture, Res.MoveCleanupFailed, currentPath),
                            Notable: true);
                    }
                }

                return new LinkOutcome(
                    LinkStatus.Created,
                    Res.MessageBoxSuccessTitle,
                    string.Format(CultureInfo.CurrentUICulture, Res.MoveSucceeded, source.Name, target));
            }
            catch (Exception exception)
            {
                return RollBackMove(originalPath, currentPath, target, copiedToTarget, targetWasEmptyFolder, MoveFailed(exception.Message));
            }
        }

        private static LinkOutcome MoveFailed(string reason) =>
            LinkOutcome.Error(string.Format(CultureInfo.CurrentUICulture, Res.MoveFailed, reason));

        /// <summary>
        /// Puts the original back where it was and removes any partial copy. Returns
        /// <paramref name="outcome"/>, or where the files are now if they couldn't be put back.
        /// </summary>
        private static LinkOutcome RollBackMove(string originalPath, string currentPath, string target, bool copiedToTarget, bool targetWasEmptyFolder, LinkOutcome outcome)
        {
            try
            {
                // Only remove the target when it is our own copy, never the only copy of the files
                // (a copy only happens after the original was set aside, so it's never at the target)
                if (copiedToTarget)
                    LinkMover.Delete(target);

                if (currentPath != originalPath)
                    LinkMover.Rename(currentPath, originalPath);

                if (targetWasEmptyFolder)
                    Directory.CreateDirectory(target);

                return outcome;
            }
            catch (Exception)
            {
                // Tell the user where their files are now; this is the original path if they never left it
                return LinkOutcome.Error(string.Format(CultureInfo.CurrentUICulture, Res.MoveRollbackFailed, currentPath));
            }
        }

        /// <summary>
        /// Copies with a cancelable progress dialog. Returns null when the copy finished,
        /// otherwise the message to show the user.
        /// </summary>
        private async Task<string> CopyWithProgressAsync(FileSystemInfo source, string target)
        {
            var progressDialog = await dialogs.ShowProgressAsync(Res.ProgressMoveTitle, Res.ProgressMovePreparing, Res.ButtonCancel);
            progressDialog.SetIndeterminate();

            using var cancellation = new CancellationTokenSource();
            progressDialog.Canceled += (s, e) => cancellation.Cancel();

            try
            {
                var total = await Task.Run(() => LinkMover.GetTotalBytes(source), cancellation.Token);

                void Show(long done)
                {
                    progressDialog.SetProgress(total == 0 ? 1 : Math.Min(1, (double)done / total));
                    progressDialog.SetMessage(string.Format(CultureInfo.CurrentUICulture, Res.ProgressMoveMessage, UiHelpers.FormatBytes(done), UiHelpers.FormatBytes(total)));
                }

                Show(0);
                IProgress<long> progress = new Progress<long>(Show);
                var interval = ProgressInterval;
                var stopwatch = Stopwatch.StartNew();
                var lastReport = TimeSpan.Zero;
                long copied = 0;

                await Task.Run(
                    () => LinkMover.Copy(
                        source,
                        target,
                        bytes =>
                        {
                            copied += bytes;
                            if (stopwatch.Elapsed - lastReport >= interval)
                            {
                                lastReport = stopwatch.Elapsed;
                                progress.Report(copied);
                            }
                        },
                        cancellation.Token),
                    cancellation.Token);

                return null;
            }
            catch (OperationCanceledException)
            {
                return Res.MoveCanceled;
            }
            catch (Exception exception)
            {
                return string.Format(CultureInfo.CurrentUICulture, Res.MoveFailed, exception.Message);
            }
            finally
            {
                await progressDialog.CloseAsync();
            }
        }
    }
}
