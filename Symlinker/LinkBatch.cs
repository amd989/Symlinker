namespace Symlinker
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;

    using Res = Symlinker.Properties.Resources;

    /// <summary>One link in the list: the folder it goes in, its name and what it points to.</summary>
    internal sealed class LinkRow : INotifyPropertyChanged
    {
        // Each row keeps its own auto-filled name, so typing a name in one row never affects another
        private readonly LinkNameAutoFill autoFill = new();
        private readonly List<(InputField Field, string Message)> errors = new();

        private string linkLocation = string.Empty;
        private string linkName = string.Empty;
        private string target = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;

        public string LinkLocation
        {
            get => linkLocation;
            set => Set(ref linkLocation, value ?? string.Empty);
        }

        public string LinkName
        {
            get => linkName;
            set => Set(ref linkName, value ?? string.Empty);
        }

        public string Target
        {
            get => target;
            private set => Set(ref target, value);
        }

        /// <summary>Where the link will be created, as shown in the list.</summary>
        public string LinkPath => LinkFolder + linkName.Trim();

        /// <summary>The link location with a trailing separator, shown dimmed before the name. Empty when not set.</summary>
        public string LinkFolder => FolderPart(linkLocation, withName: false);

        /// <summary>The folder that holds the target, with a trailing separator, shown dimmed before the target's name.</summary>
        public string TargetFolder => FolderPart(target, withName: true);

        /// <summary>The target's own name, the part of the target that matters most in the list.</summary>
        public string TargetName => UiHelpers.GetName(target.Trim());

        /// <summary>What is wrong with this row, in screen order. Empty when nothing is.</summary>
        public IReadOnlyList<(InputField Field, string Message)> Errors => errors;

        public bool HasError => errors.Count != 0;

        /// <summary>The first problem, shown as the row's tooltip.</summary>
        public string ErrorText => HasError ? errors[0].Message : null;

        /// <summary>Sets the target and fills in the link name from it, unless the user typed their own.</summary>
        public void SetTarget(string path)
        {
            path ??= string.Empty;
            Target = path;
            var name = autoFill.Update(LinkName, UiHelpers.GetName(path));
            if (name != null)
                LinkName = name;
        }

        public void SetErrors(IEnumerable<(InputField Field, string Message)> problems)
        {
            errors.Clear();
            errors.AddRange(problems);
            OnErrorsChanged();
        }

        /// <summary>Editing a field clears its error.</summary>
        public void ClearError(InputField field)
        {
            if (errors.RemoveAll(error => error.Field == field) != 0)
                OnErrorsChanged();
        }

        public LinkInput ToInput(bool isFolder, LinkKind kind) => new(LinkLocation, LinkName, Target, isFolder, kind);

        /// <summary>
        /// The folder part of a path, ending in a separator. With <paramref name="withName"/>, the
        /// path's own name is left out (a target); otherwise the whole path is the folder (a location).
        /// </summary>
        private static string FolderPart(string path, bool withName)
        {
            var trimmed = path.Trim().TrimEnd('\\', '/');
            if (withName)
                trimmed = trimmed[..^UiHelpers.GetName(trimmed).Length].TrimEnd('\\', '/');

            return trimmed.Length == 0 ? string.Empty : trimmed + "\\";
        }

        private void OnErrorsChanged() => Raise();

        private void Set(ref string field, string value)
        {
            if (field == value)
                return;

            field = value;
            Raise();
        }

        // Every display property derives from the three fields and the errors, so refresh them all
        private void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>
    /// The links to create in one step. There is always at least one row; with a single row the
    /// window looks the same as before batches existed. The cards edit <see cref="Selected"/>.
    /// </summary>
    internal sealed class LinkBatch
    {
        public LinkBatch()
        {
            Selected = new LinkRow();
            Rows.Add(Selected);
        }

        public ObservableCollection<LinkRow> Rows { get; } = new();

        /// <summary>The row the cards show and edit.</summary>
        public LinkRow Selected { get; set; }

        /// <summary>Whether there is more than one link, so the window shows the list.</summary>
        public bool IsBatch => Rows.Count > 1;

        /// <summary>Adds an empty row in the last row's link location and selects it.</summary>
        public LinkRow Add()
        {
            var row = new LinkRow { LinkLocation = Rows[^1].LinkLocation };
            Rows.Add(row);
            Selected = row;
            return row;
        }

        /// <summary>Removes a row, keeping at least one. A removed selection moves to its neighbor.</summary>
        public void Remove(LinkRow row)
        {
            var index = Rows.IndexOf(row);
            if (index < 0 || Rows.Count == 1)
                return;

            Rows.RemoveAt(index);
            if (Selected == row)
                Selected = Rows[Math.Min(index, Rows.Count - 1)];
        }

        /// <summary>
        /// Uses several picked or dropped targets at once: the first one goes in the selected row,
        /// and each of the others gets a new row after it in the same link location.
        /// </summary>
        public void AddTargets(IReadOnlyList<string> targets)
        {
            if (targets.Count == 0)
                return;

            var current = Selected;
            current.SetTarget(targets[0]);

            var index = Rows.IndexOf(current);
            foreach (var path in targets.Skip(1))
            {
                var row = new LinkRow { LinkLocation = current.LinkLocation };
                row.SetTarget(path);
                Rows.Insert(++index, row);
            }
        }

        /// <summary>
        /// Sets a picked or dropped link location on the selected row, and on every other row that
        /// was in the same place (or had none yet), so rows added together stay together.
        /// </summary>
        public void SetLocation(string location)
        {
            var old = Selected.LinkLocation;
            foreach (var row in Rows)
            {
                if (row.LinkLocation == old || row.LinkLocation.Trim().Length == 0)
                    row.LinkLocation = location;
            }
        }

        /// <summary>
        /// A path to open the target picker next to: the row's own target, or else the nearest
        /// target above it, so consecutive rows don't need re-navigating. Null when there is none.
        /// </summary>
        public string TargetHint(LinkRow row)
        {
            for (var index = Rows.IndexOf(row); index >= 0; index--)
            {
                if (Rows[index].Target.Trim().Length != 0)
                    return Rows[index].Target;
            }

            return null;
        }

        /// <summary>
        /// Checks every row and records its problems, including two rows that would create the same
        /// link. Returns true when all rows are fine.
        /// </summary>
        public bool Validate(bool isFolder)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Rows)
            {
                var problems = InputValidator.Validate(row.LinkName, row.LinkLocation, row.Target, isFolder);
                if (!problems.Any(p => p.Field != InputField.Target) && !seen.Add(FullLinkPath(row)))
                    problems.Insert(0, (InputField.LinkName, Res.ErrorDuplicateLinkPath));

                row.SetErrors(problems);
            }

            return Rows.All(row => !row.HasError);
        }

        public void ClearErrors()
        {
            foreach (var row in Rows)
                row.SetErrors(Array.Empty<(InputField, string)>());
        }

        /// <summary>
        /// After a batch where some links weren't created, drops the ones that were, so only the
        /// rows left to fix stay in the list. When every link was created, the list stays as it is.
        /// </summary>
        public void RemoveCreated(IReadOnlyList<LinkRow> rows, IReadOnlyList<LinkOutcome> outcomes)
        {
            if (outcomes.All(outcome => outcome.Status == LinkStatus.Created))
                return;

            for (var index = 0; index < rows.Count; index++)
            {
                if (outcomes[index].Status == LinkStatus.Created)
                    Remove(rows[index]);
            }
        }

        // Name and location passed validation, so this path is well formed and the location exists
        private static string FullLinkPath(LinkRow row) => Path.GetFullPath(Path.Combine(row.LinkLocation.Trim(), row.LinkName.Trim()));
    }
}
