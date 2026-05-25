using Awagaman_ERP.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Awagaman_ERP.Data;

namespace Awagaman_ERP.ViewModels
{
    public class TrackingViewModel : INotifyPropertyChanged
    {
        private readonly ITrackingRepository _repository;
        private bool _suppressPersistence;
        private TrackingEntry _selectedEntry;

        public ObservableCollection<TrackingEntry> Entries { get; } = new ObservableCollection<TrackingEntry>();
        public ObservableCollection<ReportingTrackEntry> CurrentReportingTracks { get; } = new ObservableCollection<ReportingTrackEntry>();

        private int _filteredEntriesCount;
        public int FilteredEntriesCount
        {
            get => _filteredEntriesCount;
            set => SetProperty(ref _filteredEntriesCount, value);
        }

        public TrackingEntry SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                _selectedEntry = value;
                OnPropertyChanged();
                LoadReportingTracks();
            }
        }

        public TrackingViewModel(ITrackingRepository repository = null)
        {
            _repository = repository ?? new TrackingRepository();
            LoadData();
        }

        public void LoadData()
        {
            _suppressPersistence = true;
            Entries.Clear();

            foreach (var entry in _repository.GetAll())
            {
                Entries.Add(entry);
            }

            // Load latest report and full report tracks for each entry
            var latestReports = _repository.GetLatestReportForAll();
            foreach (var entry in Entries)
            {
                if (latestReports.TryGetValue(entry.Id, out var report))
                {
                    entry.LatestReport = report;
                }
                foreach (var track in _repository.GetReportingTracks(entry.Id))
                {
                    entry.ReportTracks.Add(track);
                }
            }

            _suppressPersistence = false;
            FilteredEntriesCount = Entries.Count;
        }

        public void LoadReportingTracks()
        {
            CurrentReportingTracks.Clear();
            if (_selectedEntry == null) return;

            foreach (var track in _repository.GetReportingTracks(_selectedEntry.Id))
            {
                CurrentReportingTracks.Add(track);
            }
        }

        public void AddReportingTrack(string remarks)
        {
            if (_selectedEntry == null || string.IsNullOrWhiteSpace(remarks)) return;

            var track = new ReportingTrackEntry
            {
                TrackingEntryId = _selectedEntry.Id,
                ReportDateTime = DateTime.Now,
                Remarks = remarks.Trim()
            };

            _repository.AddReportingTrack(track);
            CurrentReportingTracks.Add(track);
            _selectedEntry.LatestReport = $"{track.ReportDateTime:dd-MMM HH:mm} - {track.Remarks}";
        }

        public void AddEntry(TrackingEntry entry)
        {
            if (entry == null) return;
            entry.Sr = Entries.Count + 1;
            Entries.Add(entry);

            if (!_suppressPersistence)
            {
                _repository.Upsert(entry);
            }

            FilteredEntriesCount = Entries.Count;
        }

        public void UpdateEntry(TrackingEntry entry)
        {
            if (entry == null) return;
            if (!_suppressPersistence)
            {
                _repository.Upsert(entry);
            }
        }

        public void DeleteEntry(TrackingEntry entry)
        {
            if (entry == null) return;
            Entries.Remove(entry);
            _repository.Delete(entry);
            FilteredEntriesCount = Entries.Count;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
