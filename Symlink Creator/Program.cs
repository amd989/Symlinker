// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="ShiftMe, Inc.">
//   2010-2013
// </copyright>
// <summary>
//   The program.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Symlink_Creator
{
    using System;
    using System.Windows.Forms;

    /// <summary>The program.</summary>
    internal static class Program
    {
        #region Methods

        /// <summary>
        ///     The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow());
        }

        #endregion
    }
}