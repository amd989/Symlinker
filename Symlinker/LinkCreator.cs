namespace Symlinker
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Res = Symlinker.Properties.Resources;

    /// <summary>What the user entered in the window.</summary>
    internal sealed record LinkRequest(string LinkLocation, string LinkName, string Destination, bool IsFolder, LinkKind Kind);

    /// <summary>
    /// Creates links from what the user entered: validates it, asks before replacing anything,
    /// and offers to move an existing file or folder to the target and leave a link behind.
    /// </summary>
    internal sealed class LinkCreator
    {
        private readonly IDialogService dialogs;
        private readonly IMklinkRunner mklink;

        public LinkCreator(IDialogService dialogs, IMklinkRunner mklink)
        {
            this.dialogs = dialogs;
            this.mklink = mklink;
        }

        /// <summary>True while files are being moved, so the window can't be closed halfway through.</summary>
        public bool IsMoving { get; private set; }

        /// <summary>Whether two paths are on the same drive, so a move is a rename instead of a copy.</summary>
        internal Func<string, string, bool> IsSameVolume { get; set; } = LinkMover.IsSameVolume;

        /// <summary>Minimum time between progress updates while copying.</summary>
        internal TimeSpan ProgressInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        public async Task CreateAsync(LinkRequest request)
        {
            try
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
                {
                    await dialogs.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.FillBlanks);
                    return;
                }

                if (linkName.Contains('"') || linkLocation.Contains('"') || destination.Contains('"'))
                {
                    await dialogs.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.InvalidQuoteInPath);
                    return;
                }

                var linkPath = $"{linkLocation}\\{linkName}";

                // A real file or folder already sits where the link goes and the target is free:
                // offer to move it to the target and leave a link behind (#33)
                if (Directory.Exists(linkLocation) && Path.IsPathFullyQualified(destination))
                {
                    var existingPath = Path.Combine(linkLocation, linkName);
                    FileSystemInfo existing = request.IsFolder ? new DirectoryInfo(existingPath) : new FileInfo(existingPath);
                    if (existing.Exists && !LinkMover.IsLink(existing) && IsFreeMoveTarget(destination, request.IsFolder))
                    {
                        await OfferMoveAsync(request, existing, linkPath, Path.GetFullPath(destination));
                        return;
                    }
                }

                if (request.IsFolder && Directory.Exists(linkLocation) && Directory.Exists(destination))
                {
                    await ReplaceOrCreateAsync(
                        request,
                        linkPath,
                        Directory.GetDirectories(linkLocation),
                        Res.DialogFolderExistsDialog,
                        Res.DialogFolderExists,
                        path => Directory.Delete(path, true));
                }
                else if (!request.IsFolder && Directory.Exists(linkLocation) && File.Exists(destination))
                {
                    await ReplaceOrCreateAsync(
                        request,
                        linkPath,
                        Directory.GetFiles(linkLocation),
                        Res.DialogDeleteFileWarning,
                        Res.DialogDeleteFile,
                        File.Delete);
                }
                else
                {
                    await dialogs.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.FilesOrFolderNotExists);
                }
            }
            catch (Exception exception)
            {
                await dialogs.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.MessageBoxExceptionOcurred + "\n" + exception.Message);
            }
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

        /// <summary>
        /// Creates the link, first asking to delete an entry with the same name from <paramref name="siblings"/>.
        /// </summary>
        private async Task ReplaceOrCreateAsync(LinkRequest request, string linkPath, string[] siblings, string title, string question, Action<string> delete)
        {
            var existing = siblings.FirstOrDefault(e => Path.GetFileName(e).Equals(request.LinkName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!await dialogs.ConfirmAsync(title, question))
                {
                    await dialogs.ShowMessageAsync(Res.LinkCreationAbortedWarning, Res.LinkCreationAborted);
                    return;
                }

                delete(existing);
            }

            var (success, message) = await RunMklinkAsync(request, linkPath, request.Destination);
            await dialogs.ShowMessageAsync(success ? Res.MessageBoxSuccessTitle : Res.MessageBoxErrorTitle, message);
        }

        private Task<(bool Success, string Message)> RunMklinkAsync(LinkRequest request, string linkPath, string destination) =>
            mklink.RunAsync(Mklink.BuildArguments(request.IsFolder, request.Kind, linkPath, destination));

        private async Task OfferMoveAsync(LinkRequest request, FileSystemInfo existing, string linkPath, string target)
        {
            var message = string.Format(
                CultureInfo.CurrentUICulture,
                request.IsFolder ? Res.DialogMoveFolder : Res.DialogMoveFile,
                existing.Name,
                target);

            if (!await dialogs.ConfirmAsync(Res.DialogMoveTitle, message, Res.ButtonMoveAndLink, Res.ButtonCancel))
            {
                await dialogs.ShowMessageAsync(Res.LinkCreationAbortedWarning, Res.LinkCreationAborted);
                return;
            }

            IsMoving = true;
            try
            {
                await MoveAndLinkAsync(request, existing, linkPath, target);
            }
            finally
            {
                IsMoving = false;
            }
        }

        /// <summary>
        /// Moves <paramref name="source"/> to <paramref name="target"/> and creates the link where it was.
        /// Any failure before the link exists puts everything back the way it was.
        /// </summary>
        private async Task MoveAndLinkAsync(LinkRequest request, FileSystemInfo source, string linkPath, string target)
        {
            var originalPath = source.FullName;

            // The target is free (missing or an empty folder), so the source can't be inside it;
            // only the other way around needs checking
            if (LinkMover.IsSameOrInside(target, originalPath))
            {
                await dialogs.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.MoveTargetInsideSource);
                return;
            }

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
                    FileSystemInfo staged = request.IsFolder ? new DirectoryInfo(staging) : new FileInfo(staging);
                    var copyError = await CopyWithProgressAsync(staged, target);
                    if (copyError != null)
                    {
                        var title = copyError == Res.MoveCanceled ? Res.LinkCreationAbortedWarning : Res.MessageBoxErrorTitle;
                        await RollBackMoveAsync(originalPath, currentPath, target, copiedToTarget, targetWasEmptyFolder, copyError, title);
                        return;
                    }
                }

                var (linked, linkMessage) = await RunMklinkAsync(request, linkPath, target);
                if (!linked)
                {
                    await RollBackMoveAsync(originalPath, currentPath, target, copiedToTarget, targetWasEmptyFolder, string.Format(CultureInfo.CurrentUICulture, Res.MoveFailed, linkMessage));
                    return;
                }

                if (currentPath != target)
                {
                    try
                    {
                        LinkMover.Delete(currentPath);
                    }
                    catch (Exception)
                    {
                        await dialogs.ShowMessageAsync(
                            Res.MessageBoxSuccessTitle,
                            string.Format(CultureInfo.CurrentUICulture, Res.MoveCleanupFailed, currentPath));
                        return;
                    }
                }

                await dialogs.ShowMessageAsync(
                    Res.MessageBoxSuccessTitle,
                    string.Format(CultureInfo.CurrentUICulture, Res.MoveSucceeded, source.Name, target));
            }
            catch (Exception exception)
            {
                await RollBackMoveAsync(originalPath, currentPath, target, copiedToTarget, targetWasEmptyFolder, string.Format(CultureInfo.CurrentUICulture, Res.MoveFailed, exception.Message));
            }
        }

        /// <summary>Puts the original back where it was and removes any partial copy, then explains what happened.</summary>
        private async Task RollBackMoveAsync(string originalPath, string currentPath, string target, bool copiedToTarget, bool targetWasEmptyFolder, string message, string title = null)
        {
            title ??= Res.MessageBoxErrorTitle;

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
            }
            catch (Exception)
            {
                // Tell the user where their files are now; this is the original path if they never left it
                message = string.Format(CultureInfo.CurrentUICulture, Res.MoveRollbackFailed, currentPath);
                title = Res.MessageBoxErrorTitle;
            }

            await dialogs.ShowMessageAsync(title, message);
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
