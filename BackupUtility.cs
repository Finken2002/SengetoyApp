using System;
using System.IO;
using System.Linq;

namespace SengetoyApp
{
    public static class BackupUtility
    {
        // Hvor mange backup-filer vi beholder
        private const int KeepCount = 30;

        // Hovedsti til appdata
        private static string AppDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SengetoyApp");

        private static string DbPath => Path.Combine(AppDir, "data.db");
        private static string BackupDir => Path.Combine(AppDir, "backups");

        /// <summary>
        /// Ta en backup nå (hvis databasen finnes) og roterer gamle backuper.
        /// </summary>
        public static void Run()
        {
            try
            {
                if (!File.Exists(DbPath)) return;

                Directory.CreateDirectory(BackupDir);

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var backupFile = Path.Combine(BackupDir, $"data_{timestamp}.db");

                // Kopier databasen som den er – SQLite tåler kopi når forbindelser er lukket
                File.Copy(DbPath, backupFile, overwrite: false);

                // Roter: behold kun de nyeste KeepCount filene
                var files = new DirectoryInfo(BackupDir)
                    .GetFiles("data_*.db")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .ToList();

                foreach (var f in files.Skip(KeepCount))
                {
                    try { f.Delete(); } catch { /* ignorer */ }
                }
            }
            catch
            {
                // Ikke la backup-feil krasje appen
            }
        }

        /// <summary>
        /// Valgfritt: Ta maks én backup per dag (kan kalles ved oppstart).
        /// </summary>
        public static void RunDaily()
        {
            try
            {
                if (!File.Exists(DbPath)) return;

                Directory.CreateDirectory(BackupDir);
                var todayPrefix = DateTime.Today.ToString("yyyy-MM-dd") + "_";

                bool already = new DirectoryInfo(BackupDir)
                    .GetFiles("data_*.db")
                    .Any(f => Path.GetFileName(f.FullName).StartsWith("data_" + todayPrefix, StringComparison.OrdinalIgnoreCase));

                if (!already)
                    Run();
            }
            catch
            {
                // ignorer
            }
        }
    }
}
