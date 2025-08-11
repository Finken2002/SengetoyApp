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

    // Viktig i SQLite: aktiver FK-støtte
    using (var pragma = conn.CreateCommand())
    {
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
    }

    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS rooms (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  room_number TEXT NOT NULL UNIQUE,
  resident_name TEXT,           -- NY kolonne
  note TEXT
);
CREATE TABLE IF NOT EXISTS linens (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  room_id INTEGER NOT NULL,
  last_changed TEXT NOT NULL,
  interval_days INTEGER NOT NULL,
  paused INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_linens_room ON linens(room_id);

CREATE TABLE IF NOT EXISTS changes_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  room_id INTEGER NOT NULL,
  changed_at TEXT NOT NULL,
  note TEXT,
  FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE CASCADE
);

-- Migrasjon: legg til resident_name hvis mangler (SQLite støtter enkel ADD COLUMN)
PRAGMA table_info(rooms);
";
    cmd.ExecuteNonQuery();

    // Sjekk om resident_name finnes, hvis ikke: legg til
    using (var check = conn.CreateCommand())
    {
        check.CommandText = "PRAGMA table_info(rooms);";
        using var rd = check.ExecuteReader();
        bool hasResident = false;
        while (rd.Read())
        {
            if (string.Equals(rd.GetString(1), "resident_name", StringComparison.OrdinalIgnoreCase))
                hasResident = true;
        }
        if (!hasResident)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE rooms ADD COLUMN resident_name TEXT;";
            alter.ExecuteNonQuery();
        }
    }
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
    where += $" AND (room_number LIKE '%' || @search || '%' OR IFNULL(resident_name,'') LIKE '%' || @search || '%' OR IFNULL(note,'') LIKE '%' || @search || '%')";

var cmd = conn.CreateCommand();
cmd.CommandText = $@"
SELECT r.id, r.room_number, r.resident_name, r.note, l.last_changed, l.interval_days, l.paused
FROM rooms r
JOIN linens l ON l.room_id=r.id
WHERE {where}
ORDER BY 
  (date(l.last_changed, '+' || l.interval_days || ' days') < date('now')) DESC,
  date(l.last_changed, '+' || l.interval_days || ' ' || ' days') ASC,
  r.room_number ASC;";

            if (!string.IsNullOrEmpty(search))
                cmd.Parameters.AddWithValue("@search", search);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
{
var last = ParseIsoDate(rd.GetString(4));
var interval = rd.GetInt32(5);
var nextDue = last.AddDays(interval);
var paused = rd.GetInt32(6) == 1;

Rows.Add(new RoomRow
{
    Id = rd.GetInt32(0),
    RoomNumber = rd.GetString(1),
    ResidentName = rd.IsDBNull(2) ? "" : rd.GetString(2), // NY
    Note = rd.IsDBNull(3) ? "" : rd.GetString(3),
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
    if (dlg.ShowDialog() != true) return;

    using var conn = new SqliteConnection(_connStr);
    conn.Open();

    // Sørg for at fremmednøkler er på (for sletting m.m.)
    using (var pragma = conn.CreateCommand())
    {
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
    }

    // 1) Opprett rom hvis det er nytt
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
INSERT OR IGNORE INTO rooms (room_number, resident_name, note)
VALUES (@nr, @resident, @note);";
        cmd.Parameters.AddWithValue("@nr", dlg.RoomNumber);
        cmd.Parameters.AddWithValue("@resident", dlg.ResidentName ?? "");
        cmd.Parameters.AddWithValue("@note", dlg.Note ?? "");
        cmd.ExecuteNonQuery();
    }

    // 2) Finn room_id
    int roomId;
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT id FROM rooms WHERE room_number=@nr;";
        cmd.Parameters.AddWithValue("@nr", dlg.RoomNumber);
        roomId = Convert.ToInt32(cmd.ExecuteScalar());
    }

    // 3) Oppdater alltid personnavn og notat (om rommet fantes fra før)
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
UPDATE rooms
SET resident_name=@resident, note=@note
WHERE id=@id;";
        cmd.Parameters.AddWithValue("@resident", dlg.ResidentName ?? "");
        cmd.Parameters.AddWithValue("@note", dlg.Note ?? "");
        cmd.Parameters.AddWithValue("@id", roomId);
        cmd.ExecuteNonQuery();
    }

    // 4) UPSERT på linens (siste skiftedato + intervall)
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
INSERT INTO linens (room_id, last_changed, interval_days, paused)
VALUES (@rid, @last, @int, 0)
ON CONFLICT(room_id) DO UPDATE
SET last_changed=excluded.last_changed,
    interval_days=excluded.interval_days;";
        cmd.Parameters.AddWithValue("@rid", roomId);
        cmd.Parameters.AddWithValue("@last", ToIsoDate(dlg.LastChanged.Date));
        cmd.Parameters.AddWithValue("@int", dlg.IntervalDays);
        cmd.ExecuteNonQuery();
    }

    // 5) (Valgfritt) logg hendelsen
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
INSERT INTO changes_log (room_id, changed_at, note)
VALUES (@rid, @ts, @note);";
        cmd.Parameters.AddWithValue("@rid", roomId);
        cmd.Parameters.AddWithValue("@ts", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@note", "Opprettet/oppdatert rom");
        cmd.ExecuteNonQuery();
    }

    // 6) Oppdater UI
    LoadRows();
    FooterText = $"La til/oppdaterte rom {dlg.RoomNumber} ({dlg.ResidentName}).";
    DataContext = null; DataContext = this;
}

private void DeleteRoom_Click(object sender, RoutedEventArgs e)
{
    if (RowFromSender(sender) is not RoomRow row) return;

    var confirm = MessageBox.Show(
        $"Slette rom {row.RoomNumber} ({row.ResidentName})?\nDette kan ikke angres.",
        "Bekreft sletting",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);

    if (confirm != MessageBoxResult.Yes) return;

    try
    {
        using var conn = new SqliteConnection(_connStr);
        conn.Open();

        // Slett eksplisitt i riktig rekkefølge (for gamle skjema uten ON DELETE CASCADE)
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM changes_log WHERE room_id=@id;";
            cmd.Parameters.AddWithValue("@id", row.Id);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM linens WHERE room_id=@id;";
            cmd.Parameters.AddWithValue("@id", row.Id);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM rooms WHERE id=@id;";
            cmd.Parameters.AddWithValue("@id", row.Id);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();

        LoadRows();
        FooterText = $"Slettet rom {row.RoomNumber}.";
        DataContext = null; DataContext = this;
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Klarte ikke å slette rommet:\n{ex.Message}", "Feil ved sletting",
            MessageBoxButton.OK, MessageBoxImage.Error);
        // ikke crash appen – bare returner
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

        private void MarkPrevious_Click(object sender, RoutedEventArgs e)
        {
            if (RowFromSender(sender) is not RoomRow row) return;

            // Foreslå en fornuftig startdato (i går, men minimum sist skiftet):
            var start = DateTime.Today.AddDays(-1);
            if (start < row.LastChanged) start = row.LastChanged;

            var dlg = new ChooseDateDialog(start) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var chosen = dlg.SelectedDate.Date;

            // Sikring: ikke aksepter fremtidsdato
            if (chosen > DateTime.Today)
            {
                MessageBox.Show("Dato kan ikke være i fremtiden.", "Ugyldig dato",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var conn = new SqliteConnection(_connStr);
            conn.Open();

            // Oppdater last_changed til valgt dato
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE linens SET last_changed=@d WHERE room_id=@rid;";
            cmd.Parameters.AddWithValue("@d", ToIsoDate(chosen));
            cmd.Parameters.AddWithValue("@rid", row.Id);
            cmd.ExecuteNonQuery();

            // Logg hendelsen
            cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO changes_log(room_id, changed_at, note) VALUES(@rid, @ts, @note);";
            cmd.Parameters.AddWithValue("@rid", row.Id);
            cmd.Parameters.AddWithValue("@ts", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@note", $"Skiftet tidligere: {ToIsoDate(chosen)}");
            cmd.ExecuteNonQuery();

            LoadRows();
            FooterText = $"Rom {row.RoomNumber}: satt skiftedato til {ToIsoDate(chosen)}.";
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
    }

    public class RoomRow
    {
        public int Id { get; set; }
        public string RoomNumber { get; set; } = "";
        public string ResidentName { get; set; } = "";
        public string Note { get; set; } = "";
        public DateTime LastChanged { get; set; }
        public int IntervalDays { get; set; }
        public DateTime NextDue { get; set; }
        public bool Paused { get; set; }
        public string PauseButtonLabel => Paused ? "Aktiver" : "Pause";
    }
}
