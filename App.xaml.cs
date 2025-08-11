using System;
using System.IO;
using System.Windows;

namespace SengetoyApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                var w = new MainWindow();
                w.Show();
            }
            catch (Exception ex)
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SengetoyApp");
                Directory.CreateDirectory(logDir);
                File.WriteAllText(Path.Combine(logDir, "startup_error.txt"), ex.ToString());
                MessageBox.Show(ex.ToString(), "Startup error");
                Shutdown(-1);
            }
        }
    }
}
