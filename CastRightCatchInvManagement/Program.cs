namespace CastRightCatchInvManagement
{
    /// <summary>
    /// App entry: show a loading window, load the shared data folder, sign in, then open the main window.
    /// </summary>
    internal static class Program
    {
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
            if (splash.IsDisposed)
                return false;

            if (!AppState.SignedIn)
            {
                splash.Hide();
                using var login = new SignInForm();
                if (login.ShowDialog() != DialogResult.OK || !AppState.SignedIn)
                    return false;
                if (splash.IsDisposed)
                    return false;
                splash.Show();
                splash.Activate();
            }

            if (DataLink.IsRemote)
            {
                splash.SetStatus("Loading settings…");
                await Task.Run(AppLock.LoadSharedSettings);
            }

            if (splash.IsDisposed)
                return false;
            splash.SetStatus("Opening workspace…");
            await Task.Yield();
            return AppState.SignedIn;
        }

        private static void LoadData()
        {
            AppLock.LoadSavedFolder();

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
                catch
                {
                    DataLink.Disconnect();
                }
            }
            else if (AppLock.HasFolder())
            {
                DataFiles.EnsureFilesExistOrAsk();
                AppLock.LoadSharedSettings();
                Accounts.EnsureFile();
            }

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
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "Cast Right Catch Inventory",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                if (!ok)
                {
                    if (!splash.IsDisposed)
                        splash.Close();
                    ExitThread();
                    return;
                }

                splash.SetStatus("Opening workspace…");
                var main = new MainForm();
                main.Show();
                if (!splash.IsDisposed)
                    splash.Close();
                IdleWatch.Start();
                BankLiveWatch.Start();
            };
            splash.Show();
        }
    }
}
