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
                var w = new MainWindow();
                MainWindow = w;          // sørger for riktig ShutdownMode
                w.Show();
            }
            catch (Exception ex)
            {
                LogAndShow("Feil ved oppstart", ex);
                Shutdown(-1);
            }
        }

        // Fanger ubehandlede exceptions i UI-tråden
        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogAndShow("Uventet feil", e.Exception);
            e.Handled = true; // hindre krasj-dialog fra .NET
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
                // Som siste utvei, prøv bare å vise feilen
                MessageBox.Show($"{ex}", title);
            }
        }
    }
}
