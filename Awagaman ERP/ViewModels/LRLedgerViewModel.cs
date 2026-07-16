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
        private bool _isLoadingPage;
        private Dictionary<string, decimal> _challanLorryHireByChNo;
        private Dictionary<string, decimal> _challanLorryHireByLrNo;
        public bool IsCurrentSortAscending => GetEffectiveSortAscending();
        public string GetSortColumn() => _sortColumn;
        private string GetEffectiveSortColumn() => string.IsNullOrWhiteSpace(_sortColumn) ? "LRNo" : _sortColumn;
        private bool GetEffectiveSortAscending() => string.IsNullOrWhiteSpace(_sortColumn) ? false : _sortAscending;
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
        public string PageInfo => $"Page {CurrentPage}";

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
            if (BackendSettings.UseRemoteApi)
                _pageSize = 300;
            Entries.CollectionChanged += Entries_CollectionChanged;
        }

        public void LoadPage()
        {
            if (_isLoadingPage)
            {
                return;
            }

            _isLoadingPage = true;
            _suppressPersistence = true;
            try
            {
                PagedEntries.Clear();
                var sortColumn = GetEffectiveSortColumn();
                var sortAscending = GetEffectiveSortAscending();

                if (BackendSettings.UseRemoteApi && _repository is LRRepository)
                {
                    var result = LRRepository.GetRemoteLedgerPageResult(CurrentPage, PageSize, _searchFilter, sortColumn, sortAscending);
                    var items = result?.Items ?? new List<LREntry>();
                    _totalCount = result?.TotalCount ?? 0;
                    _countDirty = false;
                    ApplyDerivedLorryHire(items);
                    PagedEntries = new ObservableCollection<LREntry>(items);
                    MarkComments(PagedEntries, result?.CommentIds);

                    if (CurrentPage < TotalPages)
                    {
                        if (string.IsNullOrEmpty(_searchFilter))
                            _nextPageCache = _repository.GetPage(CurrentPage + 1, PageSize, sortColumn, sortAscending);
                        else
                            _nextPageCache = _repository.Search(_searchFilter, CurrentPage + 1, PageSize, sortColumn, sortAscending);
                    }
                    else { _nextPageCache = null; }

                    FilteredEntriesCount = _totalCount;
                    FilteredTotalFreight = result?.TotalFreight ?? 0m;
                    FilteredTotalBalance = result?.TotalBalance ?? 0m;
                }
                else
                {
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
                        items = _repository.GetPage(CurrentPage, PageSize, sortColumn, sortAscending);
                    }
                    else
                    {
                        items = _repository.Search(_searchFilter, CurrentPage, PageSize, sortColumn, sortAscending);
                        if (!items.Any() && CurrentPage > 1) { CurrentPage = 1; items = _repository.Search(_searchFilter, 1, PageSize, sortColumn, sortAscending); }
                    }

                    ApplyDerivedLorryHire(items);
                    PagedEntries = new ObservableCollection<LREntry>(items);
                    MarkComments(PagedEntries);

                    if (CurrentPage < TotalPages)
                    {
                        if (string.IsNullOrEmpty(_searchFilter))
                            _nextPageCache = _repository.GetPage(CurrentPage + 1, PageSize, sortColumn, sortAscending);
                        else
                            _nextPageCache = _repository.Search(_searchFilter, CurrentPage + 1, PageSize, sortColumn, sortAscending);
                    }
                    else { _nextPageCache = null; }

                    FilteredEntriesCount = _totalCount;
                    FilteredTotalFreight = _repository.GetTotalFreight(_searchFilter);
                    FilteredTotalBalance = _repository.GetTotalBalance(_searchFilter);
                }

                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                _pageLoaded = true;
            }
            finally
            {
                _suppressPersistence = false;
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
                FilteredEntriesCount = _totalCount;
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
                FilteredEntriesCount = _totalCount;
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
                string sc = GetEffectiveSortColumn();
                bool sa = GetEffectiveSortAscending();
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

        public void AcceptSavedEntry(LREntry entry)
        {
            if (entry == null)
            {
                return;
            }

            _suppressPersistence = true;
            try
            {
                if (!Entries.Any(x => (entry.Id > 0 && x.Id == entry.Id) ||
                                      (!string.IsNullOrWhiteSpace(entry.LRNo) &&
                                       string.Equals(x.LRNo, entry.LRNo, StringComparison.OrdinalIgnoreCase))))
                {
                    Entries.Add(entry);
                }
            }
            finally
            {
                _suppressPersistence = false;
            }

            var existing = PagedEntries.FirstOrDefault(x => x != null && ((entry.Id > 0 && x.Id == entry.Id) ||
                (!string.IsNullOrWhiteSpace(entry.LRNo) &&
                 string.Equals(x.LRNo, entry.LRNo, StringComparison.OrdinalIgnoreCase))));

            if (existing != null)
            {
                var index = PagedEntries.IndexOf(existing);
                if (index >= 0)
                {
                    var updated = entry;
                    ApplyDerivedLorryHire(new List<LREntry> { updated });
                    PagedEntries[index] = updated;
                }
            }
            else
            {
                _totalCount++;
                var canInsertIntoCurrentPage =
                    CurrentPage == 1 &&
                    string.IsNullOrWhiteSpace(_searchFilter) &&
                    string.Equals(GetEffectiveSortColumn(), "LRNo", StringComparison.OrdinalIgnoreCase) &&
                    !GetEffectiveSortAscending();

                if (canInsertIntoCurrentPage)
                {
                    var inserted = entry;
                    ApplyDerivedLorryHire(new List<LREntry> { inserted });
                    PagedEntries.Insert(0, inserted);
                    if (PagedEntries.Count > PageSize)
                    {
                        PagedEntries.RemoveAt(PagedEntries.Count - 1);
                    }
                }
            }

            FilteredEntriesCount = Math.Max(_totalCount, PagedEntries.Count);
            _countDirty = true;
            _nextPageCache = null;
            _prevPageCache = null;
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(PageInfo));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        }

        public void RemoveOptimisticEntry(LREntry entry)
        {
            if (entry == null)
            {
                return;
            }

            _suppressPersistence = true;
            try
            {
                var existing = Entries.FirstOrDefault(x => ReferenceEquals(x, entry) ||
                    (entry.Id > 0 && x.Id == entry.Id) ||
                    (!string.IsNullOrWhiteSpace(entry.LRNo) &&
                     string.Equals(x.LRNo, entry.LRNo, StringComparison.OrdinalIgnoreCase)));
                if (existing != null)
                {
                    Entries.Remove(existing);
                }

                existing = PagedEntries.FirstOrDefault(x => ReferenceEquals(x, entry) ||
                    (entry.Id > 0 && x.Id == entry.Id) ||
                    (!string.IsNullOrWhiteSpace(entry.LRNo) &&
                     string.Equals(x.LRNo, entry.LRNo, StringComparison.OrdinalIgnoreCase)));
                if (existing != null)
                {
                    PagedEntries.Remove(existing);
                }
            }
            finally
            {
                _suppressPersistence = false;
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

            if (string.IsNullOrWhiteSpace(normalized))
            {
                _sortColumn = "LRNo";
                _sortAscending = false;
            }
            else
            {
                _sortColumn = normalized;
                _sortAscending = ascending;
            }
            _countDirty = false;
            _nextPageCache = null;
            _prevPageCache = null;
            LoadPage();
        }

        public List<LREntry> GetFilteredEntriesForSummary()
        {
            var rows = _repository.GetAll();
            if (string.IsNullOrWhiteSpace(_searchFilter))
            {
                return rows;
            }

            var filter = _searchFilter.Trim();
            return rows.Where(entry =>
                ContainsText(entry?.LRNo, filter) ||
                ContainsText(entry?.ConsignorName, filter) ||
                ContainsText(entry?.ConsignorAddress, filter) ||
                ContainsText(entry?.ConsignorGST, filter) ||
                ContainsText(entry?.ConsigneeName, filter) ||
                ContainsText(entry?.ConsigneeAddress, filter) ||
                ContainsText(entry?.ConsigneeGST, filter) ||
                ContainsText(entry?.From, filter) ||
                ContainsText(entry?.To, filter) ||
                ContainsText(entry?.VehicleNo, filter) ||
                ContainsText(entry?.VehicleType, filter) ||
                ContainsText(entry?.PkgType, filter) ||
                ContainsText(entry?.Description, filter) ||
                ContainsText(entry?.Invoice, filter) ||
                ContainsText(entry?.Value, filter) ||
                ContainsText(entry?.BillNo, filter) ||
                ContainsText(entry?.BillParty, filter) ||
                ContainsText(entry?.Broker, filter) ||
                ContainsText(entry?.FrtType, filter) ||
                ContainsText(entry?.PayType, filter) ||
                ContainsText(entry?.Paid, filter) ||
                ContainsText(entry?.CHNo, filter))
                .ToList();
        }

        public int GetNextSr() => _repository.GetMaxSr() + 1;

        private void MarkComments(IEnumerable<LREntry> items, IEnumerable<int> commentIds = null)
        {
            try
            {
                var ids = commentIds != null
                    ? new HashSet<int>(commentIds)
                    : new Data.CommentRepository().GetLREntryIdsWithComments();
                foreach (var e in items)
                    if (e != null) e.HasComments = ids.Contains(e.Id);
            }
            catch { }
        }

        private void LoadData()
        {
            _suppressPersistence = true;
            Entries.Clear();

            var allEntries = _repository.GetAll();
            ApplyDerivedLorryHire(allEntries);
            foreach (var entry in allEntries)
            {
                Entries.Add(entry);
            }

            _suppressPersistence = false;
            FilteredEntriesCount = Entries.Count;
            FilteredTotalFreight = Entries.Sum(x => x.TotalFreight);
            FilteredTotalBalance = Entries.Sum(x => x.Bal);
        }

        private void ApplyDerivedLorryHire(IEnumerable<LREntry> items)
        {
            if (items == null)
            {
                return;
            }

            _challanLorryHireByChNo = null;
            _challanLorryHireByLrNo = null;
            EnsureChallanLorryHireCache();
            foreach (var entry in items)
            {
                if (entry == null)
                {
                    continue;
                }

                if (entry.PreserveImportedBilling) continue;

                entry.ChallanLorryHire = ResolveChallanLorryHire(entry);
            }
        }

        private decimal ResolveChallanLorryHire(LREntry entry)
        {
            if (entry == null)
            {
                return 0m;
            }

            var challanNo = NormalizeKey(entry.CHNo);
            if (!string.IsNullOrWhiteSpace(challanNo) &&
                _challanLorryHireByChNo != null &&
                _challanLorryHireByChNo.TryGetValue(challanNo, out var lorryHireByChNo))
            {
                return lorryHireByChNo;
            }

            var lrNo = NormalizeKey(entry.LRNo);
            if (!string.IsNullOrWhiteSpace(lrNo) &&
                _challanLorryHireByLrNo != null &&
                _challanLorryHireByLrNo.TryGetValue(lrNo, out var lorryHireByLrNo))
            {
                return lorryHireByLrNo;
            }

            return 0m;
        }

        private void EnsureChallanLorryHireCache()
        {
            if (_challanLorryHireByChNo != null && _challanLorryHireByLrNo != null)
            {
                return;
            }

            _challanLorryHireByChNo = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            _challanLorryHireByLrNo = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var challanRepo = new ChallanRepository { LedgerMode = "Challan" };
                foreach (var challan in challanRepo.GetAll())
                {
                    if (challan == null)
                    {
                        continue;
                    }

                    var normalizedChNo = NormalizeKey(challan.ChallanNumber);
                    if (!string.IsNullOrWhiteSpace(normalizedChNo) && !_challanLorryHireByChNo.ContainsKey(normalizedChNo))
                    {
                        _challanLorryHireByChNo[normalizedChNo] = challan.LorryHire;
                    }

                    foreach (var lrNo in SplitLrNumbers(challan.LRNumber))
                    {
                        var normalizedLrNo = NormalizeKey(lrNo);
                        if (!string.IsNullOrWhiteSpace(normalizedLrNo) && !_challanLorryHireByLrNo.ContainsKey(normalizedLrNo))
                        {
                            _challanLorryHireByLrNo[normalizedLrNo] = challan.LorryHire;
                        }
                    }
                }
            }
            catch
            {
                _challanLorryHireByChNo.Clear();
                _challanLorryHireByLrNo.Clear();
            }
        }

        private static IEnumerable<string> SplitLrNumbers(string lrNumbers)
        {
            return (lrNumbers ?? string.Empty)
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private static string NormalizeKey(string value)
        {
            return (value ?? string.Empty).Trim();
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
            // Persistence is handled by grid commit/form save paths. Saving here
            // pushes partial values while the user is still editing, which is
            // especially slow and unsafe in remote API mode.
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

        private static bool ContainsText(string value, string filter)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(filter) &&
                   value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
