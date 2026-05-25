using Awagaman_ERP.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Awagaman_ERP.Data;

namespace Awagaman_ERP.ViewModels
{
    public class LRLedgerViewModel : INotifyPropertyChanged
    {
        private readonly ILRRepository _repository;
        private bool _suppressPersistence;
        private int _pageSize = 2147483647;
        private int _currentPage = 1;
        private int _totalCount;
        private bool _countDirty = true;
        private bool _pageLoaded;
        private string _searchFilter = "";
        private string _sortColumn = "LRNo";
        private bool _sortAscending = false;
        public bool IsCurrentSortAscending => string.IsNullOrEmpty(_sortColumn) || _sortAscending;
        public string GetSortColumn() => _sortColumn;
        private List<LREntry> _nextPageCache;
        private List<LREntry> _prevPageCache;

        public ObservableCollection<LREntry> Entries { get; } = new ObservableCollection<LREntry>();

        private ObservableCollection<LREntry> _pagedEntries = new ObservableCollection<LREntry>();
        public ObservableCollection<LREntry> PagedEntries
        {
            get => _pagedEntries;
            set { _pagedEntries = value; OnPropertyChanged(); }
        }

        public int PageSize
        {
            get => _pageSize;
            set { _pageSize = value; OnPropertyChanged(); LoadPage(); }
        }

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (value < 1) value = 1;
                var max = Math.Max(1, TotalPages);
                if (value > max) value = max;
                _currentPage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                LoadPage();
            }
        }

        public int TotalCount => _totalCount;
        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)_totalCount / PageSize));
        public bool CanGoPrevious => CurrentPage > 1;
        public bool CanGoNext => CurrentPage < TotalPages;
        public string PageInfo => $"Page {CurrentPage} of {Math.Max(1, TotalPages)}";

        private int _filteredEntriesCount;
        public int FilteredEntriesCount
        {
            get => _filteredEntriesCount;
            set => SetProperty(ref _filteredEntriesCount, value);
        }

        private decimal _filteredTotalFreight;
        public decimal FilteredTotalFreight
        {
            get => _filteredTotalFreight;
            set => SetProperty(ref _filteredTotalFreight, value);
        }

        private decimal _filteredTotalBalance;
        public decimal FilteredTotalBalance
        {
            get => _filteredTotalBalance;
            set => SetProperty(ref _filteredTotalBalance, value);
        }

        public LRLedgerViewModel(ILRRepository repository = null)
        {
            _repository = repository ?? new LRRepository();
            Entries.CollectionChanged += Entries_CollectionChanged;
        }

        public void LoadPage()
        {
            _suppressPersistence = true;
            PagedEntries.Clear();
            if (_countDirty)
            {
                _totalCount = string.IsNullOrEmpty(_searchFilter)
                    ? _repository.GetTotalCount()
                    : _repository.GetTotalCount(_searchFilter);
                _countDirty = false;
            }

            List<LREntry> items;
            if (string.IsNullOrEmpty(_searchFilter))
            {
                items = _repository.GetPage(CurrentPage, PageSize, _sortColumn, _sortAscending);
            }
            else
            {
                items = _repository.Search(_searchFilter, CurrentPage, PageSize, _sortColumn, _sortAscending);
                if (!items.Any() && CurrentPage > 1) { CurrentPage = 1; items = _repository.Search(_searchFilter, 1, PageSize, _sortColumn, _sortAscending); }
            }

            PagedEntries = new ObservableCollection<LREntry>(items);
            MarkComments(PagedEntries);

            if (CurrentPage < TotalPages)
            {
                if (string.IsNullOrEmpty(_searchFilter))
                    _nextPageCache = _repository.GetPage(CurrentPage + 1, PageSize, _sortColumn, _sortAscending);
                else
                    _nextPageCache = _repository.Search(_searchFilter, CurrentPage + 1, PageSize, _sortColumn, _sortAscending);
            }
            else { _nextPageCache = null; }

            _suppressPersistence = false;
            FilteredEntriesCount = PagedEntries.Count;
            OnPropertyChanged(nameof(PageInfo));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            _pageLoaded = true;
        }

        public void EnsurePageLoaded()
        {
            if (!_pageLoaded || _countDirty || PagedEntries.Count == 0)
            {
                LoadPage();
            }
        }

        public void GoToNextPage()
        {
            if (!CanGoNext) return;
            _prevPageCache = PagedEntries.ToList();
            _currentPage++;
            if (_nextPageCache != null)
            {
                PagedEntries = new ObservableCollection<LREntry>(_nextPageCache);
                MarkComments(PagedEntries);
                _nextPageCache = null;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                FilteredEntriesCount = PagedEntries.Count;
            }
            else { OnPropertyChanged(nameof(CurrentPage)); LoadPage(); }
        }

        public void GoToPreviousPage()
        {
            if (!CanGoPrevious) return;
            if (CurrentPage < TotalPages) _nextPageCache = PagedEntries.ToList();
            _currentPage--;
            if (_prevPageCache != null)
            {
                PagedEntries = new ObservableCollection<LREntry>(_prevPageCache);
                MarkComments(PagedEntries);
                _prevPageCache = null;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                FilteredEntriesCount = PagedEntries.Count;
            }
            else { _nextPageCache = null; OnPropertyChanged(nameof(CurrentPage)); LoadPage(); }
        }

        public void GoToFirstPage() { CurrentPage = 1; }
        public void PreCacheNextPage()
        {
            if (CurrentPage < TotalPages)
            {
                int np = CurrentPage + 1, ps = PageSize;
                bool hf = !string.IsNullOrEmpty(_searchFilter);
                string f = _searchFilter;
                string sc = _sortColumn;
                bool sa = _sortAscending;
                System.Threading.Tasks.Task.Run(() =>
                {
                    var data = hf ? _repository.Search(f, np, ps, sc, sa) : _repository.GetPage(np, ps, sc, sa);
                    System.Windows.Application.Current.Dispatcher.Invoke(() => _nextPageCache = data);
                });
            }
        }

        public void SetSearchFilter(string filter)
        {
            _searchFilter = (filter ?? "").Trim().ToLower();
            _countDirty = true; _nextPageCache = null; _prevPageCache = null;
            CurrentPage = 1;
        }

        public void RefreshAfterDelete()
        {
            _countDirty = true;
            _nextPageCache = null;
            _prevPageCache = null;
            LoadPage();
        }

        public void SetSort(string column, bool ascending)
        {
            _sortColumn = column ?? "";
            _sortAscending = ascending;
            _countDirty = false;
            _nextPageCache = null;
            _prevPageCache = null;
            LoadPage();
        }

        public int GetNextSr() => _repository.GetMaxSr() + 1;

        private void MarkComments(IEnumerable<LREntry> items)
        {
            try
            {
                var ids = new Data.CommentRepository().GetLREntryIdsWithComments();
                foreach (var e in items)
                    if (e != null) e.HasComments = ids.Contains(e.Id);
            }
            catch { }
        }

        private void LoadData()
        {
            _suppressPersistence = true;
            Entries.Clear();

            foreach (var entry in _repository.GetAll())
            {
                Entries.Add(entry);
            }

            _suppressPersistence = false;
            FilteredEntriesCount = Entries.Count;
            FilteredTotalFreight = Entries.Sum(x => x.TotalFreight);
            FilteredTotalBalance = Entries.Sum(x => x.Bal);
        }

        private void Entries_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (LREntry entry in e.OldItems)
                {
                    if (entry != null)
                    {
                        entry.PropertyChanged -= Entry_PropertyChanged;
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (LREntry entry in e.NewItems)
                {
                    if (entry != null)
                    {
                        entry.PropertyChanged += Entry_PropertyChanged;
                    }
                }
            }

            if (_suppressPersistence) return;

            if (e.NewItems != null)
            {
                foreach (LREntry entry in e.NewItems)
                {
                    _repository.Upsert(entry);
                }
            }

            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (LREntry entry in e.OldItems)
                {
                    _repository.Delete(entry);
                }
            }
        }

        private void Entry_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_suppressPersistence) return;

            var entry = sender as LREntry;
            if (entry == null) return;

            if (e.PropertyName != nameof(LREntry.Bal))
            {
                _repository.Upsert(entry);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
