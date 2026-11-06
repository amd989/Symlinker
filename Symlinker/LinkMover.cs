namespace Symlinker
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading;

    /// <summary>
    /// File system helpers for moving a file or folder to a new place before a link is created in its old spot.
    /// </summary>
    internal static class LinkMover
    {
        private const int BufferSize = 4 * 1024 * 1024;

        private const FileAttributes CopiedAttributes =
            FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System |
            FileAttributes.Archive | FileAttributes.NotContentIndexed;

        // Includes hidden and system entries, and never walks into nested links
        private static readonly EnumerationOptions AllEntries = new()
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
        };

        private static readonly EnumerationOptions AllEntriesRecursive = new()
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            RecurseSubdirectories = true,
        };

        /// <summary>Whether the entry is a symbolic link or junction (not a regular file or folder).</summary>
        /// <remarks>
        /// The attribute check skips the slower link lookup for regular entries. A missing entry
        /// reports every attribute, but has no link target, so it is not a link either.
        /// </remarks>
        public static bool IsLink(FileSystemInfo info) =>
            info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget != null;

        /// <summary>Whether <paramref name="path"/> is <paramref name="parent"/> itself or somewhere inside it.</summary>
        public static bool IsSameOrInside(string path, string parent)
        {
            var child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
            return child.Equals(root, StringComparison.OrdinalIgnoreCase)
                || child.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether both paths are on the same drive, so a move is a fast rename instead of a copy.</summary>
        public static bool IsSameVolume(string first, string second) =>
            string.Equals(
                Path.GetPathRoot(Path.GetFullPath(first)),
                Path.GetPathRoot(Path.GetFullPath(second)),
                StringComparison.OrdinalIgnoreCase);

        /// <summary>Returns <paramref name="path"/>, or a numbered variant of it, that does not exist yet.</summary>
        public static string GetUnusedPath(string path)
        {
            var candidate = path;
            for (var i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++)
                candidate = path + i.ToString(CultureInfo.InvariantCulture);
            return candidate;
        }

        /// <summary>Moves a file or folder on the same drive.</summary>
        public static void Rename(string from, string to)
        {
            if (Directory.Exists(from))
                Directory.Move(from, to);
            else
                File.Move(from, to);
        }

        /// <summary>Total size of the files to copy, used for progress.</summary>
        public static long GetTotalBytes(FileSystemInfo item)
        {
            if (item is FileInfo file)
                return file.Length;

            long total = 0;
            foreach (var entry in ((DirectoryInfo)item).EnumerateFiles("*", AllEntriesRecursive))
                total += entry.Length;
            return total;
        }

        /// <summary>
        /// Copies a file or folder to <paramref name="target"/>. Nested links are recreated as links, not followed.
        /// </summary>
        public static void Copy(FileSystemInfo source, string target, Action<long> bytesCopied, CancellationToken cancellationToken)
        {
            var buffer = new byte[BufferSize];
            if (source is DirectoryInfo directory)
                CopyDirectory(directory, target, buffer, bytesCopied, cancellationToken);
            else
                CopyFile((FileInfo)source, target, buffer, bytesCopied, cancellationToken);
        }

        /// <summary>Permanently deletes a file or folder, including read-only files inside it.</summary>
        public static void Delete(string path)
        {
            var directory = new DirectoryInfo(path);
            if (!directory.Exists)
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }

                return;
            }

            if (IsLink(directory))
            {
                // Removes only the link, never what it points to
                directory.Delete();
                return;
            }

            PrepareForDelete(directory);
            directory.Attributes &= ~FileAttributes.ReadOnly;
            directory.Delete(recursive: true);
        }

        /// <summary>
        /// Clears read-only flags (which Directory.Delete refuses) and removes nested links up front.
        /// Directory.Delete unmounts junctions with an API that needs admin rights, while removing
        /// the link directly works for everyone, and never touches the folder it points to.
        /// </summary>
        private static void PrepareForDelete(DirectoryInfo directory)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos("*", AllEntries))
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
                    entry.Attributes &= ~FileAttributes.ReadOnly;

                if (IsLink(entry))
                {
                    if (entry is DirectoryInfo)
                        Directory.Delete(entry.FullName);
                    else
                        File.Delete(entry.FullName);
                }
                else if (entry is DirectoryInfo subdirectory)
                {
                    PrepareForDelete(subdirectory);
                }
            }
        }

        private static void CopyDirectory(DirectoryInfo source, string target, byte[] buffer, Action<long> bytesCopied, CancellationToken cancellationToken)
        {
            var created = Directory.CreateDirectory(target);

            foreach (var entry in source.EnumerateFileSystemInfos("*", AllEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(target, entry.Name);

                if (IsLink(entry))
                {
                    if (entry is DirectoryInfo)
                        Directory.CreateSymbolicLink(destination, entry.LinkTarget);
                    else
                        File.CreateSymbolicLink(destination, entry.LinkTarget);
                }
                else if (entry is DirectoryInfo subdirectory)
                {
                    CopyDirectory(subdirectory, destination, buffer, bytesCopied, cancellationToken);
                }
                else
                {
                    CopyFile((FileInfo)entry, destination, buffer, bytesCopied, cancellationToken);
                }
            }

            // Set after the contents, since adding entries changes the folder's timestamps
            created.CreationTimeUtc = source.CreationTimeUtc;
            created.LastWriteTimeUtc = source.LastWriteTimeUtc;
            created.Attributes = FileAttributes.Directory | (source.Attributes & CopiedAttributes);
        }

        private static void CopyFile(FileInfo source, string target, byte[] buffer, Action<long> bytesCopied, CancellationToken cancellationToken)
        {
            using (var input = new FileStream(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1))
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    bytesCopied(read);
                }
            }

            File.SetCreationTimeUtc(target, source.CreationTimeUtc);
            File.SetLastWriteTimeUtc(target, source.LastWriteTimeUtc);
            var attributes = source.Attributes & CopiedAttributes;
            File.SetAttributes(target, attributes == 0 ? FileAttributes.Normal : attributes);
        }
    }
}
