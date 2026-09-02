namespace CastRightCatchInvManagement
{
    /// <summary>
    /// App entry: load the shared data folder, sign in, then open the main window.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
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

            if (!AppState.SignedIn && !Accounts.TryRestoreSession())
            {
                using var login = new SignInForm();
                if (login.ShowDialog() != DialogResult.OK || !AppState.SignedIn)
                    return;
            }

            if (!AppState.SignedIn)
                return;

            if (DataLink.IsRemote)
                AppLock.LoadSharedSettings();

            Application.Run(new InventoryAppContext());
        }
    }

    /// <summary>
    /// Keeps the process alive while any workspace window is open.
    /// Starts the idle close when Stay signed in is on (hours set on Admin).
    /// </summary>
    internal sealed class InventoryAppContext : ApplicationContext
    {
        public InventoryAppContext()
        {
            var main = new MainForm();
            main.Show();
            IdleWatch.Start();
            BankLiveWatch.Start();
        }
    }
}
