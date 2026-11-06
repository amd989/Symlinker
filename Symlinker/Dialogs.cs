namespace Symlinker
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using MahApps.Metro.Controls;
    using MahApps.Metro.Controls.Dialogs;

    /// <summary>The dialogs the link logic needs, so it can run without a window.</summary>
    internal interface IDialogService
    {
        Task ShowMessageAsync(string title, string message);

        /// <summary>Asks a yes/no question. Returns true when the user picks the affirmative button.</summary>
        Task<bool> ConfirmAsync(string title, string message, string affirmativeText = null, string negativeText = null);

        Task<IProgressDialog> ShowProgressAsync(string title, string message, string cancelText);
    }

    /// <summary>An open, cancelable progress dialog.</summary>
    internal interface IProgressDialog
    {
        event EventHandler Canceled;

        void SetIndeterminate();

        void SetProgress(double value);

        void SetMessage(string message);

        Task CloseAsync();
    }

    /// <summary>Shows dialogs inside a MahApps window.</summary>
    [ExcludeFromCodeCoverage(Justification = "Thin wrapper over MahApps dialogs, which need a live window.")]
    internal sealed class MetroDialogService : IDialogService
    {
        private readonly MetroWindow window;

        public MetroDialogService(MetroWindow window)
        {
            this.window = window;
        }

        public Task ShowMessageAsync(string title, string message) =>
            window.ShowMessageAsync(title, message, settings: CreateSettings());

        public async Task<bool> ConfirmAsync(string title, string message, string affirmativeText = null, string negativeText = null)
        {
            var settings = CreateSettings();
            if (affirmativeText != null)
                settings.AffirmativeButtonText = affirmativeText;
            if (negativeText != null)
                settings.NegativeButtonText = negativeText;

            var answer = await window.ShowMessageAsync(title, message, MessageDialogStyle.AffirmativeAndNegative, settings);
            return answer == MessageDialogResult.Affirmative;
        }

        public async Task<IProgressDialog> ShowProgressAsync(string title, string message, string cancelText)
        {
            var settings = CreateSettings();
            settings.NegativeButtonText = cancelText;
            var controller = await window.ShowProgressAsync(title, message, isCancelable: true, settings: settings);
            return new MetroProgressDialog(controller);
        }

        private static MetroDialogSettings CreateSettings() => new()
        {
            AnimateShow = false,
            AnimateHide = false,
        };

        private sealed class MetroProgressDialog : IProgressDialog
        {
            private readonly ProgressDialogController controller;

            public MetroProgressDialog(ProgressDialogController controller)
            {
                this.controller = controller;
            }

            public event EventHandler Canceled
            {
                add => controller.Canceled += value;
                remove => controller.Canceled -= value;
            }

            public void SetIndeterminate() => controller.SetIndeterminate();

            public void SetProgress(double value) => controller.SetProgress(value);

            public void SetMessage(string message) => controller.SetMessage(message);

            public Task CloseAsync() => controller.CloseAsync();
        }
    }
}
