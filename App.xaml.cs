using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SengetoyApp
{
    public partial class App : Application
    {
        protected void OnStartup(object sender, StartupEventArgs e)
        {
            try
            {
                // Valgfritt: ta maks én backup per dag ved oppstart
                BackupUtility.RunDaily();

                var w = new MainWindow();
                MainWindow = w;
                w.Show();
            }
            catch (Exception ex)
            {
                LogAndShow("Feil ved oppstart", ex);
                Shutdown(-1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Automatisk backup ved avslutning
            BackupUtility.Run();
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogAndShow("Uventet feil", e.Exception);
            e.Handled = true;
            Shutdown(-1);
        }

        private void LogAndShow(string title, Exception ex)
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SengetoyApp");
                Directory.CreateDirectory(logDir);
                var path = Path.Combine(logDir, "startup_error.txt");
                File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{ex}");
                MessageBox.Show($"{ex}", title);
            }
            catch
            {
                MessageBox.Show($"{ex}", title);
            }
        }
    }
}
