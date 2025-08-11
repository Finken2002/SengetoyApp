using Microsoft.Data.Sqlite;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace SengetoyApp
{
    public partial class MainWindow : Window
    {
        private readonly string _appDir;
        private readonly string _dbPath;
        private readonly string _connStr;

        public ObservableCollection<RoomRow> Rows { get; } = new();
        public string StatusText { get; set; } = "";
        public string FooterText { get; set; } = "Klar.";

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SengetoyApp");
            Directory.CreateDirectory(_appDir);
            _dbPath = Path.Combine(_appDir, "data.db");
            _connStr = $"Data Source={_dbPath}";

            EnsureDatabase();

            // Kjør først når alt er på plass
            this.Loaded += (_, __) => LoadRows();
        }


        private void EnsureDatabase()
        {
            using var conn = new SqliteConnection(_connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS rooms (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  room_number TEXT NOT NULL UNIQUE,
  note TEXT
);
CREATE TABLE IF NOT EXISTS linens (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  room_id INTEGER NOT NULL,
  last_changed TEXT NOT NULL,
  interval_days INTEGER NOT NULL,
  paused INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY (room_id) REFERENCES rooms(id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_linens_room ON linens(room_id);

CREATE TABLE IF NOT EXISTS changes_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  room_id INTEGER NOT NULL,
  changed_at TEXT NOT NULL,
  note TEXT
);
";
            cmd.ExecuteNonQuery();
        }

        private static DateTime ParseIsoDate(string s)
            => DateTime.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string ToIsoDate(DateTime dt)
            => dt.ToString("yyyy-MM-dd");

        private void LoadRows()
        {
            if (FilterCombo == null || SearchBox == null || Grid == null) return;

            Rows.Clear();
            using var conn = new SqliteConnection(_connStr);
            conn.Open();

            string filter = (FilterCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Alle";
            string search = SearchBox.Text?.Trim() ?? "";

            var today = DateTime.Today;
            var weekEnd = today.AddDays(6);

            var where = "1=1";
            if (filter == "I dag")
                where += $" AND paused=0 AND date(last_changed, '+' || interval_days || ' days') = date('{ToIsoDate(today)}')";
            else if (filter == "Forfalt")
                where += $" AND paused=0 AND date(last_changed, '+' || interval_days || ' days') < date('{ToIsoDate(today)}')";
            else if (filter == "Denne uken")
                where += $" AND paused=0 AND date(last_changed, '+' || interval_days || ' days') BETWEEN date('{ToIsoDate(today)}') AND date('{ToIsoDate(weekEnd)}')";

            if (!string.IsNullOrEmpty(search))
                where += $" AND (room_number LIKE '%' || @search || '%' OR IFNULL(note,'') LIKE '%' || @search || '%')";

            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT r.id, r.room_number, r.note, l.last_changed, l.interval_days, l.paused
FROM rooms r
JOIN linens l ON l.room_id=r.id
WHERE {where}
ORDER BY 
  (date(l.last_changed, '+' || l.interval_days || ' days') < date('now')) DESC,
  date(l.last_changed, '+' || l.interval_days || ' days') ASC,
  r.room_number ASC;
";
            if (!string.IsNullOrEmpty(search))
                cmd.Parameters.AddWithValue("@search", search);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var last = ParseIsoDate(rd.GetString(3));
                var interval = rd.GetInt32(4);
                var nextDue = last.AddDays(interval);
                var paused = rd.GetInt32(5) == 1;

                Rows.Add(new RoomRow
                {
                    Id = rd.GetInt32(0),
                    RoomNumber = rd.GetString(1),
                    Note = rd.IsDBNull(2) ? "" : rd.GetString(2),
                    LastChanged = last,
                    IntervalDays = interval,
                    NextDue = nextDue,
                    Paused = paused
                });
            }

            Grid.ItemsSource = Rows;
            StatusText = $"Viser {Rows.Count} rom";
            DataContext = null; DataContext = this;
        }

        private void FilterCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            LoadRows();
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            LoadRows();
        }


        private void AddRoom_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AddRoomDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                using var conn = new SqliteConnection(_connStr);
                conn.Open();

                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO rooms(room_number, note) VALUES(@nr, @note);";
                cmd.Parameters.AddWithValue("@nr", dlg.RoomNumber);
                cmd.Parameters.AddWithValue("@note", dlg.Note ?? "");
                cmd.ExecuteNonQuery();

                cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id FROM rooms WHERE room_number=@nr;";
                cmd.Parameters.AddWithValue("@nr", dlg.RoomNumber);
                var roomId = Convert.ToInt32(cmd.ExecuteScalar());

                cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO linens(room_id, last_changed, interval_days, paused)
VALUES(@rid, @last, @int, 0)
ON CONFLICT(room_id) DO UPDATE 
SET last_changed=excluded.last_changed, interval_days=excluded.interval_days;";
                cmd.Parameters.AddWithValue("@rid", roomId);
                cmd.Parameters.AddWithValue("@last", ToIsoDate(dlg.LastChanged.Date));
                cmd.Parameters.AddWithValue("@int", dlg.IntervalDays);
                cmd.ExecuteNonQuery();

                LoadRows();
                FooterText = $"La til/oppdaterte rom {dlg.RoomNumber}.";
                DataContext = null; DataContext = this;
            }
        }

        private RoomRow? RowFromSender(object sender)
            => (sender as FrameworkElement)?.DataContext as RoomRow;

        private void MarkToday_Click(object sender, RoutedEventArgs e)
        {
            if (RowFromSender(sender) is not RoomRow row) return;
            using var conn = new SqliteConnection(_connStr);
            conn.Open();

            var today = DateTime.Today;

            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE linens SET last_changed=@d WHERE room_id=@rid;";
            cmd.Parameters.AddWithValue("@d", ToIsoDate(today));
            cmd.Parameters.AddWithValue("@rid", row.Id);
            cmd.ExecuteNonQuery();

            cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO changes_log(room_id, changed_at, note) VALUES(@rid, @ts, @note);";
            cmd.Parameters.AddWithValue("@rid", row.Id);
            cmd.Parameters.AddWithValue("@ts", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@note", "Skiftet i dag");
            cmd.ExecuteNonQuery();

            LoadRows();
            FooterText = $"Rom {row.RoomNumber}: markert som skiftet i dag.";
            DataContext = null; DataContext = this;
        }

        private void Postpone7_Click(object sender, RoutedEventArgs e)
        {
            if (RowFromSender(sender) is not RoomRow row) return;
            using var conn = new SqliteConnection(_connStr);
            conn.Open();

            var newLast = row.LastChanged.AddDays(7);
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE linens SET last_changed=@d WHERE room_id=@rid;";
            cmd.Parameters.AddWithValue("@d", ToIsoDate(newLast));
            cmd.Parameters.AddWithValue("@rid", row.Id);
            cmd.ExecuteNonQuery();

            LoadRows();
            FooterText = $"Rom {row.RoomNumber}: utsatt 7 dager.";
            DataContext = null; DataContext = this;
        }

        private void TogglePause_Click(object sender, RoutedEventArgs e)
        {
            if (RowFromSender(sender) is not RoomRow row) return;
            using var conn = new SqliteConnection(_connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE linens SET paused=@p WHERE room_id=@rid;";
            cmd.Parameters.AddWithValue("@p", row.Paused ? 0 : 1);
            cmd.Parameters.AddWithValue("@rid", row.Id);
            cmd.ExecuteNonQuery();

            LoadRows();
            FooterText = $"Rom {row.RoomNumber}: {(row.Paused ? "aktivert" : "pauset")}.";
            DataContext = null; DataContext = this;
        }

        private void ExportToday_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            using var conn = new SqliteConnection(_connStr);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT r.room_number, l.interval_days,
       date(l.last_changed) AS last_changed,
       date(l.last_changed, '+' || l.interval_days || ' days') AS next_due,
       IFNULL(r.note,'') AS note
FROM rooms r JOIN linens l ON l.room_id=r.id
WHERE l.paused=0
  AND date(l.last_changed, '+' || l.interval_days || ' days') = date('{ToIsoDate(today)}')
ORDER BY r.room_number;";
            using var rd = cmd.ExecuteReader();

            var sb = new StringBuilder();
            sb.AppendLine("Rom,Intervall,Sist skiftet,Neste,Notat");
            while (rd.Read())
            {
                var note = rd.IsDBNull(4) ? "" : rd.GetString(4);
                note = note.Replace(',', ' ');
                sb.AppendLine($"{rd.GetString(0)},{rd.GetInt32(1)},{rd.GetString(2)},{rd.GetString(3)},{note}");

            }

            var path = Path.Combine(_appDir, $"Dagens_liste_{ToIsoDate(today)}.csv");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"Lagret:\n{path}", "Eksport fullført", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public class RoomRow
    {
        public int Id { get; set; }
        public string RoomNumber { get; set; } = "";
        public string Note { get; set; } = "";
        public DateTime LastChanged { get; set; }
        public int IntervalDays { get; set; }
        public DateTime NextDue { get; set; }
        public bool Paused { get; set; }
        public string PauseButtonLabel => Paused ? "Aktiver" : "Pause";
    }
}
