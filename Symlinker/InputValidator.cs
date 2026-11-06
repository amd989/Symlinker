namespace Symlinker
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Windows.Media;

    using Res = Symlinker.Properties.Resources;

    /// <summary>The window's text fields, in the order they appear on screen.</summary>
    internal enum InputField
    {
        LinkName,
        LinkLocation,
        Target,
    }

    /// <summary>Checks what the user entered before a link is created.</summary>
    internal static class InputValidator
    {
        /// <summary>
        /// Returns the problem with each bad field, in screen order (name, location, target).
        /// An empty list means everything is fine.
        /// </summary>
        public static List<(InputField Field, string Message)> Validate(string linkName, string linkLocation, string target, bool isFolder)
        {
            var errors = new List<(InputField, string)>();

            var name = linkName.Trim();
            var location = linkLocation.Trim();
            target = target.Trim();

            if (name.Length == 0)
                errors.Add((InputField.LinkName, Res.ErrorLinkNameMissing));
            else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                errors.Add((InputField.LinkName, Res.ErrorLinkNameInvalid));

            if (location.Length == 0)
                errors.Add((InputField.LinkLocation, Res.ErrorLinkLocationMissing));
            else if (location.Contains('"'))
                errors.Add((InputField.LinkLocation, Res.InvalidQuoteInPath));
            else if (!Directory.Exists(location))
                errors.Add((InputField.LinkLocation, Res.ErrorLinkLocationNotFound));

            if (target.Length == 0)
                errors.Add((InputField.Target, isFolder ? Res.ErrorTargetFolderMissing : Res.ErrorTargetFileMissing));
            else if (target.Contains('"'))
                errors.Add((InputField.Target, Res.InvalidQuoteInPath));
            else if (isFolder && File.Exists(target))
                errors.Add((InputField.Target, Res.ErrorTargetIsFile));
            else if (!isFolder && Directory.Exists(target))
                errors.Add((InputField.Target, Res.ErrorTargetIsFolder));
            else if (!TargetFitsMode(target, isFolder) && !LinkPathInUse(location, name))
                errors.Add((InputField.Target, isFolder ? Res.ErrorTargetFolderNotFound : Res.ErrorTargetFileNotFound));

            return errors;
        }

        /// <summary>Whether the target path still makes sense in folder or file mode (an existing folder or file).</summary>
        public static bool TargetFitsMode(string target, bool folderMode)
        {
            target = target.Trim();
            if (target.Length == 0)
                return false;

            return folderMode ? Directory.Exists(target) : File.Exists(target);
        }

        /// <summary>
        /// Whether something already exists where the link would go. Link creation decides what
        /// to do then, so a missing target isn't reported.
        /// </summary>
        private static bool LinkPathInUse(string location, string name)
        {
            if (location.Length == 0 || name.Length == 0)
                return false;

            var path = Path.Combine(location, name);
            return Directory.Exists(path) || File.Exists(path);
        }
    }

    /// <summary>Color math for text that has to stay readable on the user's accent color.</summary>
    internal static class ColorContrast
    {
        /// <summary>Black or white, whichever reads better on <paramref name="accent"/> drawn over <paramref name="background"/>.</summary>
        public static Color TextOn(Color accent, Color? background)
        {
            // The accent is partly transparent, so judge the color actually seen on screen
            if (background is Color behind)
                accent = Blend(accent, behind);

            return ContrastRatio(accent, Colors.Black) > ContrastRatio(accent, Colors.White) ? Colors.Black : Colors.White;
        }

        /// <summary>The opaque color seen when <paramref name="top"/> is drawn over <paramref name="bottom"/>.</summary>
        public static Color Blend(Color top, Color bottom)
        {
            var a = top.A / 255.0;
            byte Mix(byte t, byte b) => (byte)Math.Round((t * a) + (b * (1 - a)));
            return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
        }

        /// <summary>WCAG contrast ratio between two colors (1 to 21).</summary>
        public static double ContrastRatio(Color a, Color b)
        {
            static double Channel(byte value)
            {
                var c = value / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            static double Luminance(Color c) => (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

            var la = Luminance(a);
            var lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
    }
}
