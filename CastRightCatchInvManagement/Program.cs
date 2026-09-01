namespace CastRightCatchInvManagement
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            AppLock.LoadSavedFolder();

            if (AppLock.HasFolder())
            {
                DataFiles.EnsureFilesExistOrAsk();
                AppLock.LoadSharedSettings();
                Accounts.EnsureFile();
            }

            using (var login = new SignInForm())
            {
                if (login.ShowDialog() != DialogResult.OK || !AppState.SignedIn)
                    return;
            }

            Application.Run(new InventoryAppContext());
        }
    }

    internal sealed class InventoryAppContext : ApplicationContext
    {
        public InventoryAppContext()
        {
            var main = new MainForm();
            main.Show();
        }
    }
}
