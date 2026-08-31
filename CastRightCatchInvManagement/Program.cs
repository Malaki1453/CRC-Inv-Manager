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
