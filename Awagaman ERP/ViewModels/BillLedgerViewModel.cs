using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Awagaman_ERP.Data;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.ViewModels
{
    public class BillLedgerViewModel : INotifyPropertyChanged
    {
        private readonly BillRepository _repository = new BillRepository();
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
        private bool _isLoadingPage;
        private bool _isLoadingPageAsync;

        public bool IsCurrentSortAscending => string.IsNullOrEmpty(_sortColumn) || _sortAscending;
        public string GetSortColumn() => _sortColumn;
        public bool HasLoadedPage => _pageLoaded && !_countDirty && PagedEntries.Count > 0;

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
            if (BackendSettings.UseRemoteApi)
            {
                _pageSize = 300;
            }
        }

        public void LoadPage()
        {
            if (_isLoadingPage)
            {
                return;
            }

            _isLoadingPage = true;
            try
            {
                PagedEntries.Clear();
                if (_countDirty)
                {
                    _totalCount = string.IsNullOrEmpty(_searchFilter) ? _repository.GetTotalCount() : _repository.GetTotalCount(_searchFilter);
                    _countDirty = false;
                }

                var maxPage = Math.Max(1, TotalPages);
                if (_currentPage > maxPage)
                {
                    _currentPage = maxPage;
                    OnPropertyChanged(nameof(CurrentPage));
                }

                List<BillEntry> items;
                if (string.IsNullOrEmpty(_searchFilter))
                {
                    items = _repository.GetPage(CurrentPage, PageSize, _sortColumn, _sortAscending);
                }
                else
                {
                    items = _repository.Search(_searchFilter, CurrentPage, PageSize, _sortColumn, _sortAscending);
                    if (!items.Any() && CurrentPage > 1)
                    {
                        _currentPage = 1;
                        OnPropertyChanged(nameof(CurrentPage));
                        items = _repository.Search(_searchFilter, 1, PageSize, _sortColumn, _sortAscending);
                    }
                }

                if (!items.Any() && _currentPage > 1)
                {
                    _currentPage = 1;
                    OnPropertyChanged(nameof(CurrentPage));
                    items = string.IsNullOrEmpty(_searchFilter)
                        ? _repository.GetPage(1, PageSize, _sortColumn, _sortAscending)
                        : _repository.Search(_searchFilter, 1, PageSize, _sortColumn, _sortAscending);
                }

                PagedEntries = new ObservableCollection<BillEntry>(items);
                if (!BackendSettings.UseRemoteApi)
                {
                    MarkComments(PagedEntries);
                }
                ApplyGroupingAndDisplay(PagedEntries);
                FilteredEntriesCount = _totalCount;
                FilteredTotalDue = _repository.GetTotalDue(_searchFilter);
                _pageLoaded = true;
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanGoFirst));
                OnPropertyChanged(nameof(CanGoLast));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Bill ledger error: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                _isLoadingPage = false;
            }
        }

        public void EnsurePageLoaded()
        {
            if (!_pageLoaded || _countDirty || PagedEntries.Count == 0)
            {
                LoadPage();
            }
        }

        private class PageLoadResult
        {
            public int TotalCount;
            public decimal TotalDue;
            public int CurrentPage;
            public List<BillEntry> Items;
        }

        public void EnsurePageLoadedAsync(Action afterLoad = null, Action<Exception> onError = null)
        {
            if (!BackendSettings.UseRemoteApi)
            {
                EnsurePageLoaded();
                afterLoad?.Invoke();
                return;
            }

            if (HasLoadedPage)
            {
                afterLoad?.Invoke();
                return;
            }

            if (_isLoadingPageAsync)
            {
                return;
            }

            _isLoadingPageAsync = true;
            var requestedPage = CurrentPage;
            var requestedPageSize = PageSize;
            var searchFilter = _searchFilter;
            var sortColumn = _sortColumn;
            var sortAscending = _sortAscending;
            var countDirty = _countDirty;

            Task.Run(() =>
            {
                var totalCount = countDirty ? _repository.GetTotalCount(searchFilter) : _totalCount;
                var totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / requestedPageSize));
                var pageToLoad = Math.Min(Math.Max(1, requestedPage), totalPages);
                List<BillEntry> items;
                if (string.IsNullOrEmpty(searchFilter))
                {
                    items = _repository.GetPage(pageToLoad, requestedPageSize, sortColumn, sortAscending);
                }
                else
                {
                    items = _repository.Search(searchFilter, pageToLoad, requestedPageSize, sortColumn, sortAscending);
                    if (!items.Any() && pageToLoad > 1)
                    {
                        pageToLoad = 1;
                        items = _repository.Search(searchFilter, pageToLoad, requestedPageSize, sortColumn, sortAscending);
                    }
                }

                if (!items.Any() && pageToLoad > 1)
                {
                    pageToLoad = 1;
                    items = string.IsNullOrEmpty(searchFilter)
                        ? _repository.GetPage(pageToLoad, requestedPageSize, sortColumn, sortAscending)
                        : _repository.Search(searchFilter, pageToLoad, requestedPageSize, sortColumn, sortAscending);
                }

                return new PageLoadResult
                {
                    CurrentPage = pageToLoad,
                    TotalCount = totalCount,
                    TotalDue = _repository.GetTotalDue(searchFilter),
                    Items = items
                };
            }).ContinueWith(task =>
            {
                _isLoadingPageAsync = false;

                if (task.IsFaulted)
                {
                    var ex = task.Exception?.GetBaseException() ?? new Exception("Unable to load bill page.");
                    onError?.Invoke(ex);
                    return;
                }

                var result = task.Result;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _totalCount = result.TotalCount;
                    _countDirty = false;
                    _currentPage = result.CurrentPage;
                    PagedEntries = new ObservableCollection<BillEntry>(result.Items ?? new List<BillEntry>());
                    ApplyGroupingAndDisplay(PagedEntries);
                    FilteredEntriesCount = _totalCount;
                    FilteredTotalDue = result.TotalDue;
                    _pageLoaded = true;
                    OnPropertyChanged(nameof(CurrentPage));
                    OnPropertyChanged(nameof(TotalCount));
                    OnPropertyChanged(nameof(TotalPages));
                    OnPropertyChanged(nameof(PageInfo));
                    OnPropertyChanged(nameof(CanGoPrevious));
                    OnPropertyChanged(nameof(CanGoNext));
                    OnPropertyChanged(nameof(CanGoFirst));
                    OnPropertyChanged(nameof(CanGoLast));
                    afterLoad?.Invoke();
                });
            });
        }

        public void SetSearchFilter(string filter)
        {
            _searchFilter = (filter ?? "").Trim().ToLower();
            _countDirty = true;
            _nextPageCache = null;
            _prevPageCache = null;
            CurrentPage = 1;
        }

        public void SetSort(string column, bool ascending)
        {
            var normalized = column ?? "";
            if (string.Equals(_sortColumn ?? string.Empty, normalized, StringComparison.OrdinalIgnoreCase) &&
                _sortAscending == ascending &&
                !_countDirty &&
                _pageLoaded)
            {
                return;
            }

            _sortColumn = normalized;
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

        public void RemoveOptimisticEntry(BillEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            var existing = Entries.FirstOrDefault(x => ReferenceEquals(x, entry) ||
                (entry.Id > 0 && x.Id == entry.Id) ||
                (!string.IsNullOrWhiteSpace(entry.BillNo) &&
                 string.Equals(x.BillNo, entry.BillNo, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals((x.LRNo ?? string.Empty).Trim(), (entry.LRNo ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)));
            if (existing != null)
            {
                Entries.Remove(existing);
            }

            existing = PagedEntries.FirstOrDefault(x => ReferenceEquals(x, entry) ||
                (entry.Id > 0 && x.Id == entry.Id) ||
                (!string.IsNullOrWhiteSpace(entry.BillNo) &&
                 string.Equals(x.BillNo, entry.BillNo, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals((x.LRNo ?? string.Empty).Trim(), (entry.LRNo ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)));
            if (existing != null)
            {
                PagedEntries.Remove(existing);
                ApplyGroupingAndDisplay(PagedEntries);
            }

            _totalCount = Math.Max(0, _totalCount - 1);
            FilteredEntriesCount = Math.Max(0, _totalCount);
            _countDirty = true;
            _nextPageCache = null;
            _prevPageCache = null;
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(PageInfo));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoFirst));
            OnPropertyChanged(nameof(CanGoLast));
        }

        public void GoToNextPage()
        {
            if (!CanGoNext) return;
            _prevPageCache = PagedEntries.ToList();
            _currentPage++;
            if (_nextPageCache != null)
            {
                PagedEntries = new ObservableCollection<BillEntry>(_nextPageCache);
                if (!BackendSettings.UseRemoteApi)
                {
                    MarkComments(PagedEntries);
                }
                ApplyGroupingAndDisplay(PagedEntries);
                _nextPageCache = null;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanGoFirst));
                OnPropertyChanged(nameof(CanGoLast));
                FilteredEntriesCount = _totalCount;
                FilteredTotalDue = _repository.GetTotalDue(_searchFilter);
            }
            else
            {
                OnPropertyChanged(nameof(CurrentPage));
                LoadPage();
            }
        }

        public void GoToPreviousPage()
        {
            if (!CanGoPrevious) return;
            if (CurrentPage < TotalPages) _nextPageCache = PagedEntries.ToList();
            _currentPage--;
            if (_prevPageCache != null)
            {
                PagedEntries = new ObservableCollection<BillEntry>(_prevPageCache);
                if (!BackendSettings.UseRemoteApi)
                {
                    MarkComments(PagedEntries);
                }
                ApplyGroupingAndDisplay(PagedEntries);
                _prevPageCache = null;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanGoFirst));
                OnPropertyChanged(nameof(CanGoLast));
                FilteredEntriesCount = _totalCount;
                FilteredTotalDue = _repository.GetTotalDue(_searchFilter);
            }
            else
            {
                _nextPageCache = null;
                OnPropertyChanged(nameof(CurrentPage));
                LoadPage();
            }
        }

        public void GoToFirstPage() { CurrentPage = 1; }
        public void GoToLastPage() { CurrentPage = TotalPages; }

        public void PreCacheNextPage()
        {
            if (CurrentPage < TotalPages)
            {
                int nextPage = CurrentPage + 1;
                int pageSize = PageSize;
                bool hasFilter = !string.IsNullOrEmpty(_searchFilter);
                string filter = _searchFilter;
                string sortColumn = _sortColumn;
                bool sortAscending = _sortAscending;
                Task.Run(() =>
                {
                    var data = hasFilter
                        ? _repository.Search(filter, nextPage, pageSize, sortColumn, sortAscending)
                        : _repository.GetPage(nextPage, pageSize, sortColumn, sortAscending);
                    System.Windows.Application.Current.Dispatcher.Invoke(() => _nextPageCache = data);
                });
            }
        }

        public List<BillEntry> GetFilteredEntriesForSummary()
        {
            var rows = _repository.GetAll();
            if (string.IsNullOrWhiteSpace(_searchFilter))
            {
                return rows;
            }

            var filter = _searchFilter.Trim();
            return rows.Where(entry =>
                ContainsText(entry?.BillNo, filter) ||
                ContainsText(entry?.Party, filter) ||
                ContainsText(entry?.LRNo, filter) ||
                ContainsText(entry?.From, filter) ||
                ContainsText(entry?.To, filter) ||
                ContainsText(entry?.MR, filter) ||
                ContainsText(entry?.Remarks, filter))
                .ToList();
        }

        private static bool ContainsText(string value, string filter) =>
            !string.IsNullOrWhiteSpace(value) &&
            !string.IsNullOrWhiteSpace(filter) &&
            value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void ApplyGroupingAndDisplay(IEnumerable<BillEntry> items)
        {
            string[] colors = { "#FFFFFF", "#F0F0F0" };
            int colorIndex = 0;
            string lastGroupKey = null;
            foreach (var entry in items ?? Enumerable.Empty<BillEntry>())
            {
                var currentGroupKey = entry.GroupKey;
                bool isNewGroup = !string.Equals(currentGroupKey, lastGroupKey, StringComparison.OrdinalIgnoreCase);
                if (isNewGroup)
                {
                    lastGroupKey = currentGroupKey;
                    colorIndex = (colorIndex + 1) % colors.Length;
                }

                entry.GroupColor = colors[colorIndex];
                entry.BillNoDisplay = isNewGroup ? entry.BillNoLedgerDisplay : string.Empty;
            }
        }

        private void MarkComments(IEnumerable<BillEntry> items)
        {
            try
            {
                var itemList = items?.Where(x => x != null).ToList() ?? new List<BillEntry>();
                var ids = new CommentRepository().GetBillIdsWithComments();
                var previews = new CommentRepository().GetLatestBillCommentsByIds(itemList.Select(x => x.Id));
                foreach (var entry in itemList)
                {
                    entry.HasComments = ids.Contains(entry.Id);
                    entry.CommentPreview = previews.TryGetValue(entry.Id, out var preview) ? preview : string.Empty;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(nameof(MarkComments), ex);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
