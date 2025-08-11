using System;
using System.Windows;

namespace SengetoyApp
{
    public partial class ChooseDateDialog : Window
    {
      public DateTime SelectedDate => DateBox.SelectedDate ?? DateTime.Today;

      public ChooseDateDialog(DateTime? defaultDate = null)
      {
          InitializeComponent();
          // Forslag: i går hvis i dag allerede er dekket av "Skiftet i dag"
          DateBox.SelectedDate = defaultDate ?? DateTime.Today.AddDays(-1);
          DateBox.DisplayDateEnd = DateTime.Today; // kan ikke velge fremtid
      }

      private void Ok_Click(object sender, RoutedEventArgs e)
      {
          if (DateBox.SelectedDate is null)
          {
              ErrorText.Text = "Velg en gyldig dato.";
              return;
          }
          if (DateBox.SelectedDate > DateTime.Today)
          {
              ErrorText.Text = "Dato kan ikke være i fremtiden.";
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
