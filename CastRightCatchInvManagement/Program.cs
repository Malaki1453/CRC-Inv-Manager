namespace CastRightCatchInvManagement
{
    /// <summary>
    /// App entry: show a loading window, load the shared data folder, sign in, then open the main window.
    /// </summary>
    internal static class Program
    {
        /// <summary>WinForms entry point. STA is required for dialogs and the clipboard.</summary>
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new InventoryAppContext());
        }

        /// <summary>Folder, database, and sign-in. Reports status on the loading window.</summary>
        internal static async Task<bool> StartAsync(LoadingForm splash)
        {
            splash.SetStatus("Opening data…");
            await Task.Run(LoadData);
            // Splash can be closed while data loads on the background thread.
            if (splash.IsDisposed)
                return false;

            // Session restore may have failed; the workspace requires a signed-in user.
            if (!AppState.SignedIn)
            {
                splash.Hide();
                using var login = new SignInForm();
                // User cancelled or credentials were rejected.
                if (login.ShowDialog() != DialogResult.OK || !AppState.SignedIn)
                    return false;
                // Login dialog can outlive the splash if the user closed it.
                if (splash.IsDisposed)
                    return false;
                splash.Show();
                splash.Activate();
            }

            // Server clients do not have a local settings DB until after they authenticate.
            if (DataLink.IsRemote)
            {
                splash.SetStatus("Loading settings…");
                await Task.Run(AppLock.LoadSharedSettings);
            }

            // Settings load can take long enough for the user to close the splash.
            if (splash.IsDisposed)
                return false;
            splash.SetStatus("Opening workspace…");
            await Task.Yield();
            return AppState.SignedIn;
        }

        /// <summary>Restore the last folder or server, then try a stay-signed-in session.</summary>
        private static void LoadData()
        {
            AppLock.LoadSavedFolder();

            // This PC last used the inventory server; reconnect before any local files.
            if (DataLink.UseInventoryServer &&
                AppState.UseServer &&
                !string.IsNullOrWhiteSpace(AppState.ServerHost))
            {
                try
                {
                    DataLink.Connect(
                        AppState.ServerHost,
                        AppState.ServerPort,
                        AppState.ServerFingerprint);
                }
                // Keep going with a local folder if the host is unreachable.
                catch
                {
                    DataLink.Disconnect();
                }
            }
            // Shared-folder mode: create missing CSVs/DB and load company settings.
            else if (AppLock.HasFolder())
            {
                DataFiles.EnsureFilesExistOrAsk();
                AppLock.LoadSharedSettings();
                Accounts.EnsureFile();
            }

            // Stay signed in can skip the login dialog when the cookie is still valid.
            if (!AppState.SignedIn)
                Accounts.TryRestoreSession();
        }
    }

    /// <summary>
    /// Keeps the process alive while any workspace window is open.
    /// Shows a loading spinner first, then the main window.
    /// Starts the idle close when Stay signed in is on (hours set on Admin).
    /// </summary>
    internal sealed class InventoryAppContext : ApplicationContext
    {
        /// <summary>Show the splash first so a slow folder/server open still looks alive.</summary>
        public InventoryAppContext()
        {
            var splash = new LoadingForm();
            splash.Shown += async (_, _) =>
            {
                bool ok = false;
                try
                {
                    ok = await Program.StartAsync(splash);
                }
                // Surface folder/server errors instead of leaving a hung splash.
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "Cast Right Catch Inventory",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                // Sign-in cancelled, splash closed, or a load error: end the process.
                if (!ok)
                {
                    // Avoid ObjectDisposedException if the user already closed splash.
                    if (!splash.IsDisposed)
                        splash.Close();
                    ExitThread();
                    return;
                }

                splash.SetStatus("Opening workspace…");
                var main = new MainForm();
                main.Show();
                // Main is the new message-loop owner; drop the splash if it is still open.
                if (!splash.IsDisposed)
                    splash.Close();
                IdleWatch.Start();
                BankLiveWatch.Start();
            };
            splash.Show();
        }
    }
}
