namespace Symlinker
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;

    using Microsoft.Win32.SafeHandles;

    /// <summary>
    /// Checks on disk whether mklink did its job, and works out why it didn't. mklink's own text
    /// is in the language of Windows, so it is never parsed. Runs in the process that ran mklink
    /// (the window, or the elevated helper), so the checks have the same rights.
    /// </summary>
    internal static class LinkCheck
    {
        /// <summary>
        /// Whether the link exists and leads to the requested target: a symbolic link or junction
        /// stores that target, a hard link is the same file as it.
        /// </summary>
        public static bool IsCreated(LinkRequest request)
        {
            // mklink resolves a relative target against its working folder, which is ours
            var target = Path.GetFullPath(request.Target);

            var kind = LinkKinds.Parse(request.Type);
            if (kind == LinkKind.Hard)
                return File.Exists(request.Link) && IsSameFile(request.Link, target);

            FileSystemInfo link = request.Directory ? new DirectoryInfo(request.Link) : new FileInfo(request.Link);
            var stored = link.LinkTarget;
            if (stored == null)
                return false;

            // A symbolic link keeps a relative target as entered, relative to its own folder
            var linkFolder = Path.GetDirectoryName(request.Link);
            if (kind == LinkKind.Symbolic)
                target = Path.GetFullPath(request.Target, linkFolder);

            return SamePath(Path.GetFullPath(stored, linkFolder), target);
        }

        /// <summary>
        /// The most likely reason mklink failed, from what is on disk. Falls back to
        /// <see cref="LinkError.Unknown"/>, so the caller shows mklink's own text.
        /// </summary>
        /// <param name="request">The link that wasn't created.</param>
        /// <param name="existedBefore">Whether something was at the link path before mklink ran.</param>
        /// <param name="canCreateSymbolicLinks">Whether this process may create symbolic links.</param>
        /// <param name="canWriteTo">Whether this process may create files in a folder.</param>
        public static LinkError Diagnose(LinkRequest request, bool existedBefore, Func<bool> canCreateSymbolicLinks, Func<string, bool> canWriteTo)
        {
            if (existedBefore)
                return LinkError.LinkExists;

            var kind = LinkKinds.Parse(request.Type);
            if (kind == LinkKind.Hard && !LinkMover.IsSameVolume(request.Link, request.Target))
                return LinkError.HardLinkAcrossDrives;

            if (kind == LinkKind.Junction && IsNetworkPath(request.Target))
                return LinkError.JunctionNeedsLocalFolder;

            if (kind == LinkKind.Symbolic && !canCreateSymbolicLinks())
                return LinkError.NeedsAdmin;

            if (!canWriteTo(Path.GetDirectoryName(request.Link)))
                return LinkError.AccessDenied;

            return LinkError.Unknown;
        }

        /// <summary>
        /// Whether this process may create files in <paramref name="folder"/>. Tries for real with a
        /// file that deletes itself. Anything other than a denied access (like a missing folder) is
        /// not an access problem, so it counts as writable.
        /// </summary>
        public static bool CanWriteTo(string folder)
        {
            try
            {
                var probe = Path.Combine(folder, ".symlinker-probe-" + Guid.NewGuid().ToString("N"));
                using (File.Create(probe, 1, FileOptions.DeleteOnClose))
                {
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>Whether a path is on a network share or a mapped network drive.</summary>
        internal static bool IsNetworkPath(string path)
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return root.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(root).DriveType == DriveType.Network;
        }

        /// <summary>
        /// Whether two paths are the same file (same volume and file index). When either can't be
        /// opened (locked or no read access), the answer is yes: mklink already said it worked.
        /// </summary>
        internal static bool IsSameFile(string first, string second)
        {
            try
            {
                return FileId(first) == FileId(second);
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static bool SamePath(string first, string second) =>
            string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase);

        private static (uint Volume, uint High, uint Low) FileId(string path)
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // If this fails the info stays zeroed for both files, which reads as the same file
            GetFileInformationByHandle(handle, out var info);
            return (info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }
    }
}
