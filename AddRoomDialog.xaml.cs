using System;
using System.Windows;

namespace SengetoyApp
{
    public partial class AddRoomDialog : Window
    {
        public string RoomNumber => RoomBox.Text.Trim();
        public string ResidentName => PersonBox.Text.Trim();
        public DateTime LastChanged => DatePickerLast.SelectedDate ?? DateTime.Today;
        public int IntervalDays
        {
            get
            {
                if (int.TryParse((IntervalBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString(), out var n))
                    return n;
                return 14;
            }
        }
        public string Note => NoteBox.Text.Trim();

        public AddRoomDialog()
        {
            InitializeComponent();
            DatePickerLast.SelectedDate = DateTime.Today;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RoomNumber))
            {
                MessageBox.Show("Skriv inn romnummer.", "Mangler felt", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
