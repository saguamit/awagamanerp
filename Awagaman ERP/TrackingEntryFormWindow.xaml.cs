using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Awagaman_ERP.Data;
using Awagaman_ERP.Models;
using MahApps.Metro.Controls;

namespace Awagaman_ERP
{
    public partial class TrackingEntryFormWindow : MetroWindow
    {
        private readonly TrackingRepository _repository = new TrackingRepository();
        public ObservableCollection<ReportingTrackEntry> ReportingTracks { get; } = new ObservableCollection<ReportingTrackEntry>();

        public TrackingEntry Entry { get; private set; }

        public TrackingEntryFormWindow(TrackingEntry entry)
        {
            InitializeComponent();

            Entry = entry;
            DataContext = Entry;
            LoadReportingTracks();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FocusManager.SetFocusedElement(this, this);
            Keyboard.Focus(this);
        }

        private void LoadReportingTracks()
        {
            ReportingTracks.Clear();
            if (Entry == null) return;

            foreach (var track in _repository.GetReportingTracks(Entry.Id))
            {
                ReportingTracks.Add(track);
            }

            // Bind the list manually since it's not the DataContext
            var listBox = FindName("ReportingTracksListBox") as System.Windows.Controls.ListBox;
            if (listBox != null)
            {
                listBox.ItemsSource = ReportingTracks;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            FocusManager.SetFocusedElement(this, this);
            Keyboard.ClearFocus();

            try
            {
                _repository.Upsert(Entry);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to save: " + ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AddReport_Click(object sender, RoutedEventArgs e)
        {
            if (Entry == null || Entry.Id <= 0)
            {
                MessageBox.Show("Save the entry first before adding reports.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var remarks = ReportRemarksBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(remarks) || remarks == "Enter update...")
            {
                MessageBox.Show("Please enter report remarks.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var track = new ReportingTrackEntry
                {
                    TrackingEntryId = Entry.Id,
                    ReportDateTime = DateTime.Now,
                    Remarks = remarks
                };
                _repository.AddReportingTrack(track);
                ReportingTracks.Add(track);
                ReportRemarksBox.Text = "Enter update...";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to add report: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReportRemarksBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (ReportRemarksBox.Text == "Enter update...")
            {
                ReportRemarksBox.Text = string.Empty;
            }
        }
    }
}
