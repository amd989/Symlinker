namespace Symlinker
{
    using System.Globalization;
    using System.IO;

    using Res = Symlinker.Properties.Resources;

    /// <summary>
    /// The title and text that tell the user how creating one link went. Built from the request
    /// and the result code, never from mklink's text (which is in the language of Windows), except
    /// as a detail for failures Symlinker can't explain itself. Self-contained so a list of results
    /// can reuse it per link.
    /// </summary>
    internal sealed record LinkResultMessage(string Title, string Message)
    {
        /// <summary>
        /// Paths longer than this are shortened in the middle so each fits on one line, after the
        /// longest label ("Destinazione:" in Italian).
        /// </summary>
        internal const int MaxPathLength = 52;

        private const string Ellipsis = "\u2026";

        /// <summary>The dialog for a finished link: what was created and where, or why it wasn't.</summary>
        public static LinkResultMessage For(LinkRequest request, LinkResult result)
        {
            if (!result.Success)
                return new LinkResultMessage(Res.ResultFailedTitle, Reason(request, result));

            var kind = KindOf(request);
            var sentence = kind == LinkKind.Hard ? Res.ResultHardLinkSentence : Res.ResultPointsToSentence;
            return new LinkResultMessage(CreatedTitle(kind), Format(sentence, NameOf(request.Link), NameOf(request.Target)) + "\n\n" + Paths(request));
        }

        /// <summary>The dialog after an existing item was moved to the target and linked from its old place.</summary>
        public static LinkResultMessage ForMove(LinkRequest request, string movedName) =>
            new(CreatedTitle(KindOf(request)), Format(Res.MoveSucceeded, movedName) + "\n\n" + Paths(request));

        /// <summary>
        /// Why a link wasn't created, in plain words. When Symlinker can't tell, this is mklink's own
        /// error text. Also suits a single row in a list of results.
        /// </summary>
        public static string Reason(LinkRequest request, LinkResult result) => result.Error switch
        {
            LinkError.InvalidPath => Res.InvalidQuoteInPath,
            LinkError.CommandNotFound => Res.CmdNotFound,
            LinkError.NotCreated => Res.ResultNotCreated,
            LinkError.LinkExists => Format(Res.ResultLinkExists, NameOf(request.Link)),
            LinkError.AccessDenied => Format(Res.ResultAccessDenied, CompactPath(Path.GetDirectoryName(request.Link))),
            LinkError.NeedsAdmin => Res.ResultNeedsAdmin,
            LinkError.HardLinkAcrossDrives => Res.ResultHardLinkAcrossDrives,
            LinkError.JunctionNeedsLocalFolder => Res.ResultJunctionNeedsLocalFolder,
            _ => string.IsNullOrWhiteSpace(result.Message) ? Res.ResultUnknownError : result.Message,
        };

        /// <summary>
        /// Shortens a long path in the middle, keeping the drive and as many of the last folders as fit:
        /// <c>D:\…\Anno 1800\accounts\Test</c>.
        /// </summary>
        internal static string CompactPath(string path, int maxLength = MaxPathLength)
        {
            if (path.Length <= maxLength)
                return path;

            var root = Path.GetPathRoot(path);
            var parts = path.Substring(root.Length).Split(new[] { '\\', '/' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return path;

            var prefix = root.Length > 0 ? root.TrimEnd('\\', '/') + "\\" + Ellipsis + "\\" : Ellipsis + "\\";
            var tail = parts[^1];
            for (var i = parts.Length - 2; i >= 0; i--)
            {
                var longer = parts[i] + "\\" + tail;
                if (prefix.Length + longer.Length > maxLength)
                    break;
                tail = longer;
            }

            return prefix + tail;
        }

        private static LinkKind KindOf(LinkRequest request) => LinkKinds.Parse(request.Type) ?? LinkKind.Symbolic;

        private static string CreatedTitle(LinkKind kind) => kind switch
        {
            LinkKind.Hard => Res.ResultHardLinkCreated,
            LinkKind.Junction => Res.ResultJunctionCreated,
            _ => Res.ResultSymbolicLinkCreated,
        };

        private static string Paths(LinkRequest request) =>
            Format(Res.ResultLinkPath, CompactPath(request.Link)) + "\n" + Format(Res.ResultTargetPath, CompactPath(request.Target));

        /// <summary>The last part of a path, or the whole path for a drive root like <c>D:\</c>.</summary>
        private static string NameOf(string path)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            return name.Length > 0 ? name : path;
        }

        private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentUICulture, format, args);
    }
}
