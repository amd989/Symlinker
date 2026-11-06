namespace Symlinker
{
    using System;
    using System.Threading.Tasks;

    using Res = Symlinker.Properties.Resources;

    /// <summary>Creates links for <see cref="LinkCreator"/>, asking for administrator rights first when a link needs them.</summary>
    internal interface ILinkRunner
    {
        /// <summary>
        /// Gets ready to create <paramref name="request"/>: shows the UAC prompt if this link needs
        /// administrator rights and they weren't granted yet. Call it before anything on disk changes.
        /// Returns false when link creation should stop: the prompt was declined (nothing is shown)
        /// or the administrator helper couldn't start (an error is shown).
        /// </summary>
        Task<bool> PrepareAsync(LinkRequest request);

        Task<LinkResult> RunAsync(LinkRequest request);
    }

    /// <summary>A running copy of Symlinker with administrator rights that creates links on request.</summary>
    internal interface ILinkHelper : IDisposable
    {
        bool IsAlive { get; }

        Task<LinkResult> CreateLinkAsync(LinkRequest request);
    }

    /// <summary>
    /// Runs mklink unelevated, except symbolic links while they need administrator rights: those go
    /// through an elevated helper, started on the first one (one UAC prompt) and kept until disposed.
    /// </summary>
    internal sealed class LinkRunner : ILinkRunner, IDisposable
    {
        private readonly IDialogService dialogs;
        private readonly Func<bool> symlinksNeedAdmin;
        private readonly Func<Task<ILinkHelper>> startHelper;
        private readonly Func<LinkRequest, Task<LinkResult>> runUnelevated;
        private readonly Action helperChanged;

        private ILinkHelper helper;

        /// <param name="dialogs">Shows an error when the helper can't start.</param>
        /// <param name="symlinksNeedAdmin">Whether symbolic links currently need administrator rights.</param>
        /// <param name="startHelper">Starts the helper (with a UAC prompt). Returns null when the prompt is declined.</param>
        /// <param name="runUnelevated">Creates a link in this process.</param>
        /// <param name="helperChanged">Called after a helper starts, e.g. to hide the UAC shield.</param>
        public LinkRunner(
            IDialogService dialogs,
            Func<bool> symlinksNeedAdmin,
            Func<Task<ILinkHelper>> startHelper,
            Func<LinkRequest, Task<LinkResult>> runUnelevated,
            Action helperChanged)
        {
            this.dialogs = dialogs;
            this.symlinksNeedAdmin = symlinksNeedAdmin;
            this.startHelper = startHelper;
            this.runUnelevated = runUnelevated;
            this.helperChanged = helperChanged;
        }

        /// <summary>Whether administrator rights were granted and the helper is still running, so no prompt would show.</summary>
        public bool HelperRunning => helper?.IsAlive == true;

        public async Task<bool> PrepareAsync(LinkRequest request)
        {
            if (!NeedsAdmin(request) || HelperRunning)
                return true;

            // The helper went away (e.g. ended from Task Manager); start a new one
            Dispose();

            try
            {
                helper = await startHelper();
            }
            catch (Exception exception)
            {
                await dialogs.ShowMessageAsync(Res.MessageBoxErrorTitle, Res.MessageBoxExceptionOcurred + "\n" + exception.Message);
                return false;
            }

            helperChanged();
            return helper != null;
        }

        public async Task<LinkResult> RunAsync(LinkRequest request)
        {
            try
            {
                return helper != null && NeedsAdmin(request)
                    ? await helper.CreateLinkAsync(request)
                    : await runUnelevated(request);
            }
            catch (Exception exception)
            {
                return LinkResult.Failed(Res.MessageBoxExceptionOcurred + "\n" + exception.Message);
            }
        }

        /// <summary>Closes the helper, if one is running.</summary>
        public void Dispose()
        {
            helper?.Dispose();
            helper = null;
        }

        private bool NeedsAdmin(LinkRequest request) => request.Type == LinkKinds.ToName(LinkKind.Symbolic) && symlinksNeedAdmin();
    }
}
