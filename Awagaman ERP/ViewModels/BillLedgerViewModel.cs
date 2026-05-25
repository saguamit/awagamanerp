using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Awagaman_ERP.Data;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.ViewModels
{
    public class BillLedgerViewModel : INotifyPropertyChanged
    {
        private readonly BillRepository _repository = new BillRepository();
        private bool _suppressPersistence;
        private int _pageSize = 100000;
        private int _currentPage = 1;
        private int _totalCount;
        private bool _countDirty = true;
        private bool _pageLoaded;
        private string _searchFilter = "";
        private string _sortColumn = "BillNo";
        private bool _sortAscending = false;
        private List<BillEntry> _nextPageCache;
        private List<BillEntry> _prevPageCache;
        private int _filteredEntriesCount;
        private decimal _filteredTotalDue;

        public bool IsCurrentSortAscending => string.IsNullOrEmpty(_sortColumn) || _sortAscending;
        public string GetSortColumn() => _sortColumn;

        public ObservableCollection<BillEntry> Entries { get; } = new ObservableCollection<BillEntry>();
        private ObservableCollection<BillEntry> _pagedEntries = new ObservableCollection<BillEntry>();
        public ObservableCollection<BillEntry> PagedEntries
        {
            get => _pagedEntries;
            set { _pagedEntries = value; OnPropertyChanged(); }
        }

        public int PageSize { get => _pageSize; set { _pageSize = value; OnPropertyChanged(); LoadPage(); } }
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (value < 1) value = 1;
                if (value > Math.Max(1, TotalPages)) value = Math.Max(1, TotalPages);
                _currentPage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanGoFirst));
                OnPropertyChanged(nameof(CanGoLast));
                LoadPage();
            }
        }
        public int TotalCount => _totalCount;
        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)_totalCount / PageSize));
        public bool CanGoPrevious => CurrentPage > 1;
        public bool CanGoNext => CurrentPage < TotalPages;
        public bool CanGoFirst => CurrentPage > 1;
        public bool CanGoLast => CurrentPage < TotalPages;
        public string PageInfo => $"Page {CurrentPage} of {Math.Max(1, TotalPages)}";

        public int FilteredEntriesCount { get => _filteredEntriesCount; set { _filteredEntriesCount = value; OnPropertyChanged(); } }
        public decimal FilteredTotalDue { get => _filteredTotalDue; set { _filteredTotalDue = value; OnPropertyChanged(); } }

        public BillLedgerViewModel()
        {
        }

        public void LoadPage()
        {
            try
            {
                _suppressPersistence = true;
                PagedEntries.Clear();
                if (_countDirty)
                {
                    _totalCount = string.IsNullOrEmpty(_searchFilter) ? _repository.GetTotalCount() : _repository.GetTotalCount(_searchFilter);
                    _countDirty = false;
                }
                List<BillEntry> items;
                if (string.IsNullOrEmpty(_searchFilter))
                    items = _repository.GetPage(CurrentPage, PageSize, _sortColumn, _sortAscending);
                else
                {
                    items = _repository.Search(_searchFilter, CurrentPage, PageSize, _sortColumn, _sortAscending);
                    if (!items.Any() && CurrentPage > 1) { CurrentPage = 1; items = _repository.Search(_searchFilter, 1, PageSize, _sortColumn, _sortAscending); }
                }
                PagedEntries = new ObservableCollection<BillEntry>(items);

                // Assign group colors by BillNo
                string[] colors = { "#FFFFFF", "#F0F0F0" };
                int ci = 0;
                string lastBillNo = null;
                foreach (var e in PagedEntries)
                {
                    bool isNewGroup = e.BillNo != lastBillNo;
                    if (isNewGroup) { lastBillNo = e.BillNo; ci = (ci + 1) % colors.Length; }
                    e.GroupColor = colors[ci];
                    e.BillNoDisplay = isNewGroup ? e.BillNo : string.Empty;
                }

                _suppressPersistence = false;
                FilteredEntriesCount = PagedEntries.Count;
                FilteredTotalDue = PagedEntries.Sum(e => e?.Due ?? 0);
                _pageLoaded = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Bill ledger error: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                _suppressPersistence = false;
            }
        }

        public void EnsurePageLoaded()
        {
            if (!_pageLoaded || _countDirty || PagedEntries.Count == 0)
            {
                LoadPage();
            }
        }

        public void SetSearchFilter(string filter)
        {
            _searchFilter = (filter ?? "").Trim().ToLower();
            _countDirty = true; _nextPageCache = null; _prevPageCache = null;
            CurrentPage = 1;
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

        public void RefreshAfterDelete()
        {
            _countDirty = true;
            _nextPageCache = null;
            _prevPageCache = null;
            LoadPage();
        }

        public void GoToNextPage()
        {
            if (!CanGoNext) return;
            _prevPageCache = PagedEntries.ToList();
            _currentPage++;
            if (_nextPageCache != null)
            {
                PagedEntries = new ObservableCollection<BillEntry>(_nextPageCache);
                MarkComments(PagedEntries);
                _nextPageCache = null;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanGoFirst));
                OnPropertyChanged(nameof(CanGoLast));
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
                PagedEntries = new ObservableCollection<BillEntry>(_prevPageCache);
                MarkComments(PagedEntries);
                _prevPageCache = null;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanGoFirst));
                OnPropertyChanged(nameof(CanGoLast));
                FilteredEntriesCount = PagedEntries.Count;
            }
            else { _nextPageCache = null; OnPropertyChanged(nameof(CurrentPage)); LoadPage(); }
        }

        public void GoToFirstPage() { CurrentPage = 1; }
        public void GoToLastPage() { CurrentPage = TotalPages; }

        public void PreCacheNextPage()
        {
            if (CurrentPage < TotalPages)
            {
                int np = CurrentPage + 1, ps = PageSize;
                bool hf = !string.IsNullOrEmpty(_searchFilter);
                string f = _searchFilter, sc = _sortColumn;
                bool sa = _sortAscending;
                System.Threading.Tasks.Task.Run(() =>
                {
                    var data = hf ? _repository.Search(f, np, ps, sc, sa) : _repository.GetPage(np, ps, sc, sa);
                    System.Windows.Application.Current.Dispatcher.Invoke(() => _nextPageCache = data);
                });
            }
        }

        private void MarkComments(IEnumerable<BillEntry> items)
        {
            try
            {
                var ids = new CommentRepository().GetBillIdsWithComments();
                foreach (var e in items)
                    if (e != null) e.HasComments = ids.Contains(e.Id);
            }
            catch { }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
