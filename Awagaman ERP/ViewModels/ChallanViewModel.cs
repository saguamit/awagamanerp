using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using Awagaman_ERP.Data;
using Awagaman_ERP.Helpers;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.ViewModels
{
    public class ChallanViewModel : INotifyPropertyChanged
    {
        private ChallanEntry _current = new ChallanEntry { Date = DateTime.Today };
        private readonly IChallanRepository _repository;
        private bool _suppressPersistence;
        private int _filteredEntriesCount;
        private decimal _filteredTotalDue;
        private int _pageSize = 2147483647;
        private int _currentPage = 1;
        private int _totalCount;
        private bool _countDirty = true;
        private bool _pageLoaded;
        private string _searchFilter = "";
        private string _filterChallanNo = "";
        private string _filterLRNo = "";
        private string _filterFrom = "";
        private string _filterTo = "";
        private bool _hasAdvancedFilter => !string.IsNullOrWhiteSpace(_filterChallanNo) || !string.IsNullOrWhiteSpace(_filterLRNo) || !string.IsNullOrWhiteSpace(_filterFrom) || !string.IsNullOrWhiteSpace(_filterTo);
        private string _sortColumn = "ChallanNumber";
        private bool _sortAscending = false;
        private bool _useLhsDerivedValues;

        // Exposed for the Sorting event handler to determine toggle direction
        public bool IsCurrentSortAscending
        {
            get => GetEffectiveSortAscending();
        }
        public string GetSortColumn() => _sortColumn;
        private string GetEffectiveSortColumn() => string.IsNullOrWhiteSpace(_sortColumn) ? "ChallanNumber" : _sortColumn;
        private bool GetEffectiveSortAscending() => string.IsNullOrWhiteSpace(_sortColumn) ? false : _sortAscending;
        private List<ChallanEntry> _nextPageCache;
        private List<ChallanEntry> _prevPageCache;
        private bool _isLoadingPage;
        private bool _isLoadingPageAsync;
        public ObservableCollection<ChallanEntry> Entries { get; } = new ObservableCollection<ChallanEntry>();
        private ObservableCollection<ChallanEntry> _pagedEntries = new ObservableCollection<ChallanEntry>();
        public ObservableCollection<ChallanEntry> PagedEntries
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

        public string PageInfo => $"Page {CurrentPage}";
        public bool HasLoadedPage => _pageLoaded && !_countDirty;
        public bool UseLhsDerivedValues
        {
            get => _useLhsDerivedValues;
            set
            {
                if (SetProperty(ref _useLhsDerivedValues, value))
                {
                    OnPropertyChanged(nameof(TotalDue));
                }
            }
        }

        public string LedgerMode
        {
            get => _repository?.LedgerMode ?? "Purchase";
            set
            {
                if (_repository == null)
                {
                    return;
                }

                var mode = string.Equals(value, "Challan", StringComparison.OrdinalIgnoreCase) ? "Challan" : "Purchase";
                if (string.Equals(_repository.LedgerMode, mode, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _repository.LedgerMode = mode;
                _countDirty = true;
                _pageLoaded = false;
                _nextPageCache = null;
                _prevPageCache = null;
                _currentPage = 1;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanGoFirst));
                OnPropertyChanged(nameof(CanGoLast));
                OnPropertyChanged(nameof(HasLoadedPage));
            }
        }

        public decimal TotalDue => Entries.Sum(entry => UseLhsDerivedValues ? (entry?.ChallanDue ?? 0m) : (entry?.Due ?? 0m));
        public int FilteredEntriesCount
        {
            get => _filteredEntriesCount;
            set => SetProperty(ref _filteredEntriesCount, value);
        }

        public decimal FilteredTotalDue
        {
            get => _filteredTotalDue;
            set => SetProperty(ref _filteredTotalDue, value);
        }

        private decimal _filteredTotalBalance;
        public decimal FilteredTotalBalance
        {
            get => _filteredTotalBalance;
            set => SetProperty(ref _filteredTotalBalance, value);
        }

        private decimal _filteredTotalAdvance;
        public decimal FilteredTotalAdvance
        {
            get => _filteredTotalAdvance;
            set => SetProperty(ref _filteredTotalAdvance, value);
        }


        // Column visibility flags (default true) with backing fields and change notification
        private bool _showSr = true;
        private bool _showChallanNumber = true;
        private bool _showDate = true;
        private bool _showLRNumber = true;
        private bool _showBrokerName = true;
        private bool _showFrom = true;
        private bool _showTo = true;
        private bool _showVehicleNumber = true;
        private bool _showVehicleType = true;
        private bool _showDriverName = true;
        private bool _showDriverMobile = true;
        private bool _showEngineNo = true;
        private bool _showLicenceNo = true;
        private bool _showPolicyNo = true;
        private bool _showChassisNo = true;
        private bool _showOwnerName = true;
        private bool _showPAN = true;
        private bool _showLorryHire = true;
        private bool _showOther;
        private bool _showLHS;
        private bool _showLessTDS = true;
        private bool _showAdvanceAmount = true;
        private bool _showAdvanceNEFT = true;
        private bool _showAdvanceCash = true;
        private bool _showAdvanceDate = true;
        private bool _showBalance = true;
        private bool _showDetention = true;
        private bool _showHamali = true;
        private bool _showDeduction = true;
        private bool _showBalancePaidNEFT = true;
        private bool _showBalancePaidCash = true;
        private bool _showBalancePaidDate = true;
        private bool _showDue = true;
        private bool _showPaidTo = true;
        private bool _showRemarks = true;
        private bool _showBillAmount = true;
        private bool _showMargin = true;

        public bool ShowSr { get => _showSr; set => SetProperty(ref _showSr, value); }
        public bool ShowChallanNumber { get => _showChallanNumber; set => SetProperty(ref _showChallanNumber, value); }
        public bool ShowDate { get => _showDate; set => SetProperty(ref _showDate, value); }
        public bool ShowLRNumber { get => _showLRNumber; set => SetProperty(ref _showLRNumber, value); }
        public bool ShowBrokerName { get => _showBrokerName; set => SetProperty(ref _showBrokerName, value); }
        public bool ShowFrom { get => _showFrom; set => SetProperty(ref _showFrom, value); }
        public bool ShowTo { get => _showTo; set => SetProperty(ref _showTo, value); }
        public bool ShowVehicleNumber { get => _showVehicleNumber; set => SetProperty(ref _showVehicleNumber, value); }
        public bool ShowVehicleType { get => _showVehicleType; set => SetProperty(ref _showVehicleType, value); }
        public bool ShowDriverName { get => _showDriverName; set => SetProperty(ref _showDriverName, value); }
        public bool ShowDriverMobile { get => _showDriverMobile; set => SetProperty(ref _showDriverMobile, value); }
        public bool ShowEngineNo { get => _showEngineNo; set => SetProperty(ref _showEngineNo, value); }
        public bool ShowLicenceNo { get => _showLicenceNo; set => SetProperty(ref _showLicenceNo, value); }
        public bool ShowPolicyNo { get => _showPolicyNo; set => SetProperty(ref _showPolicyNo, value); }
        public bool ShowChassisNo { get => _showChassisNo; set => SetProperty(ref _showChassisNo, value); }
        public bool ShowOwnerName { get => _showOwnerName; set => SetProperty(ref _showOwnerName, value); }
        public bool ShowPAN { get => _showPAN; set => SetProperty(ref _showPAN, value); }
        public bool ShowLorryHire { get => _showLorryHire; set => SetProperty(ref _showLorryHire, value); }
        public bool ShowOther { get => _showOther; set => SetProperty(ref _showOther, value); }
        public bool ShowLHS { get => _showLHS; set => SetProperty(ref _showLHS, value); }
        public bool ShowLessTDS { get => _showLessTDS; set => SetProperty(ref _showLessTDS, value); }
        public bool ShowAdvanceAmount { get => _showAdvanceAmount; set => SetProperty(ref _showAdvanceAmount, value); }
        public bool ShowAdvanceNEFT { get => _showAdvanceNEFT; set => SetProperty(ref _showAdvanceNEFT, value); }
        public bool ShowAdvanceCash { get => _showAdvanceCash; set => SetProperty(ref _showAdvanceCash, value); }
        public bool ShowAdvanceDate { get => _showAdvanceDate; set => SetProperty(ref _showAdvanceDate, value); }
        public bool ShowBalance { get => _showBalance; set => SetProperty(ref _showBalance, value); }
        public bool ShowDetention { get => _showDetention; set => SetProperty(ref _showDetention, value); }
        public bool ShowHamali { get => _showHamali; set => SetProperty(ref _showHamali, value); }
        public bool ShowDeduction { get => _showDeduction; set => SetProperty(ref _showDeduction, value); }
        public bool ShowBalancePaidNEFT { get => _showBalancePaidNEFT; set => SetProperty(ref _showBalancePaidNEFT, value); }
        public bool ShowBalancePaidCash { get => _showBalancePaidCash; set => SetProperty(ref _showBalancePaidCash, value); }
        public bool ShowBalancePaidDate { get => _showBalancePaidDate; set => SetProperty(ref _showBalancePaidDate, value); }
        public bool ShowDue { get => _showDue; set => SetProperty(ref _showDue, value); }
        public bool ShowPaidTo { get => _showPaidTo; set => SetProperty(ref _showPaidTo, value); }
        public bool ShowRemarks { get => _showRemarks; set => SetProperty(ref _showRemarks, value); }
        public bool ShowBillAmount { get => _showBillAmount; set => SetProperty(ref _showBillAmount, value); }
        public bool ShowMargin { get => _showMargin; set => SetProperty(ref _showMargin, value); }

        public ChallanEntry Current
        {
            get => _current;
            set { _current = value; OnPropertyChanged(); }
        }


        public RelayCommand AddCommand { get; }

        public ChallanViewModel(IChallanRepository repository = null)
        {
            _repository = repository ?? new ChallanRepository();
            if (BackendSettings.UseRemoteApi)
                _pageSize = 300;
            AddCommand = new RelayCommand(_ => AddEntry(), _ => CanAdd());
            Entries.CollectionChanged += Entries_CollectionChanged;
            LoadColumnSettings();
        }

        private static string ColumnSettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Awagaman ERP", "column_settings.json");

        private void LoadColumnSettings()
        {
            try
            {
                var path = ColumnSettingsPath;
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (data == null) return;
                if (data.TryGetValue("ShowSr", out var v)) _showSr = (bool)v;
                if (data.TryGetValue("ShowChallanNumber", out v)) _showChallanNumber = (bool)v;
                if (data.TryGetValue("ShowDate", out v)) _showDate = (bool)v;
                if (data.TryGetValue("ShowLRNumber", out v)) _showLRNumber = (bool)v;
                if (data.TryGetValue("ShowBrokerName", out v)) _showBrokerName = (bool)v;
                if (data.TryGetValue("ShowFrom", out v)) _showFrom = (bool)v;
                if (data.TryGetValue("ShowTo", out v)) _showTo = (bool)v;
                if (data.TryGetValue("ShowVehicleNumber", out v)) _showVehicleNumber = (bool)v;
                if (data.TryGetValue("ShowVehicleType", out v)) _showVehicleType = (bool)v;
                if (data.TryGetValue("ShowDriverName", out v)) _showDriverName = (bool)v;
                if (data.TryGetValue("ShowDriverMobile", out v)) _showDriverMobile = (bool)v;
                if (data.TryGetValue("ShowEngineNo", out v)) _showEngineNo = (bool)v;
                if (data.TryGetValue("ShowLicenceNo", out v)) _showLicenceNo = (bool)v;
                if (data.TryGetValue("ShowPolicyNo", out v)) _showPolicyNo = (bool)v;
                if (data.TryGetValue("ShowChassisNo", out v)) _showChassisNo = (bool)v;
                if (data.TryGetValue("ShowOwnerName", out v)) _showOwnerName = (bool)v;
                if (data.TryGetValue("ShowPAN", out v)) _showPAN = (bool)v;
                if (data.TryGetValue("ShowLorryHire", out v)) _showLorryHire = (bool)v;
                if (data.TryGetValue("ShowOther", out v)) _showOther = (bool)v;
                if (data.TryGetValue("ShowLHS", out v)) _showLHS = (bool)v;
                if (data.TryGetValue("ShowLessTDS", out v)) _showLessTDS = (bool)v;
                if (data.TryGetValue("ShowAdvanceAmount", out v)) _showAdvanceAmount = (bool)v;
                if (data.TryGetValue("ShowAdvanceNEFT", out v)) _showAdvanceNEFT = (bool)v;
                if (data.TryGetValue("ShowAdvanceCash", out v)) _showAdvanceCash = (bool)v;
                if (data.TryGetValue("ShowAdvanceDate", out v)) _showAdvanceDate = (bool)v;
                if (data.TryGetValue("ShowBalance", out v)) _showBalance = (bool)v;
                if (data.TryGetValue("ShowDetention", out v)) _showDetention = (bool)v;
                if (data.TryGetValue("ShowHamali", out v)) _showHamali = (bool)v;
                if (data.TryGetValue("ShowDeduction", out v)) _showDeduction = (bool)v;
                if (data.TryGetValue("ShowBalancePaidNEFT", out v)) _showBalancePaidNEFT = (bool)v;
                if (data.TryGetValue("ShowBalancePaidCash", out v)) _showBalancePaidCash = (bool)v;
                if (data.TryGetValue("ShowBalancePaidDate", out v)) _showBalancePaidDate = (bool)v;
                if (data.TryGetValue("ShowDue", out v)) _showDue = (bool)v;
                if (data.TryGetValue("ShowPaidTo", out v)) _showPaidTo = (bool)v;
                if (data.TryGetValue("ShowRemarks", out v)) _showRemarks = (bool)v;
                if (data.TryGetValue("ShowBillAmount", out v)) _showBillAmount = (bool)v;
                if (data.TryGetValue("ShowMargin", out v)) _showMargin = (bool)v;
                if (data.TryGetValue("SortColumn", out var sv)) _sortColumn = sv?.ToString() ?? "";
                if (data.TryGetValue("SortAscending", out var av)) _sortAscending = av is bool b ? b : true;
            }
            catch { }
        }

        public void SaveColumnSettings()
        {
            try
            {
                var data = new Dictionary<string, object>
                {
                    ["ShowSr"] = _showSr,
                    ["ShowChallanNumber"] = _showChallanNumber,
                    ["ShowDate"] = _showDate,
                    ["ShowLRNumber"] = _showLRNumber,
                    ["ShowBrokerName"] = _showBrokerName,
                    ["ShowFrom"] = _showFrom,
                    ["ShowTo"] = _showTo,
                    ["ShowVehicleNumber"] = _showVehicleNumber,
                    ["ShowVehicleType"] = _showVehicleType,
                    ["ShowDriverName"] = _showDriverName,
                    ["ShowDriverMobile"] = _showDriverMobile,
                    ["ShowEngineNo"] = _showEngineNo,
                    ["ShowLicenceNo"] = _showLicenceNo,
                    ["ShowPolicyNo"] = _showPolicyNo,
                    ["ShowChassisNo"] = _showChassisNo,
                    ["ShowOwnerName"] = _showOwnerName,
                    ["ShowPAN"] = _showPAN,
                    ["ShowLorryHire"] = _showLorryHire,
                    ["ShowOther"] = _showOther,
                    ["ShowLHS"] = _showLHS,
                    ["ShowLessTDS"] = _showLessTDS,
                    ["ShowAdvanceAmount"] = _showAdvanceAmount,
                    ["ShowAdvanceNEFT"] = _showAdvanceNEFT,
                    ["ShowAdvanceCash"] = _showAdvanceCash,
                    ["ShowAdvanceDate"] = _showAdvanceDate,
                    ["ShowBalance"] = _showBalance,
                    ["ShowDetention"] = _showDetention,
                    ["ShowHamali"] = _showHamali,
                    ["ShowDeduction"] = _showDeduction,
                    ["ShowBalancePaidNEFT"] = _showBalancePaidNEFT,
                    ["ShowBalancePaidCash"] = _showBalancePaidCash,
                    ["ShowBalancePaidDate"] = _showBalancePaidDate,
                    ["ShowDue"] = _showDue,
                    ["ShowPaidTo"] = _showPaidTo,
                    ["ShowRemarks"] = _showRemarks,
                    ["ShowBillAmount"] = _showBillAmount,
                    ["ShowMargin"] = _showMargin,
                    ["SortColumn"] = _sortColumn,
                    ["SortAscending"] = _sortAscending
                };
                var dir = Path.GetDirectoryName(ColumnSettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ColumnSettingsPath, new JavaScriptSerializer().Serialize(data));
            }
            catch { }
        }

        private void LoadData()
        {
            _suppressPersistence = true;
            Entries.Clear();
            foreach (var item in _repository.GetAll())
            {
                Entries.Add(item);
            }
            _suppressPersistence = false;
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
                if (_countDirty)
                {
                    if (_hasAdvancedFilter)
                        _totalCount = _repository.GetTotalCountAdvanced(_filterChallanNo, _filterLRNo, _filterFrom, _filterTo);
                    else if (string.IsNullOrEmpty(_searchFilter))
                        _totalCount = _repository.GetTotalCount();
                    else
                        _totalCount = _repository.GetTotalCount(_searchFilter);
                    _countDirty = false;
                }

                decimal totalDue = _hasAdvancedFilter
                    ? _repository.GetTotalDueAdvanced(_filterChallanNo, _filterLRNo, _filterFrom, _filterTo, UseLhsDerivedValues)
                    : string.IsNullOrEmpty(_searchFilter)
                        ? _repository.GetTotalDue("", UseLhsDerivedValues)
                        : _repository.GetTotalDue(_searchFilter, UseLhsDerivedValues);

                List<ChallanEntry> items;
                var sortColumn = GetEffectiveSortColumn();
                var sortAscending = GetEffectiveSortAscending();
                if (_hasAdvancedFilter)
                {
                    items = _repository.SearchAdvanced(_filterChallanNo, _filterLRNo, _filterFrom, _filterTo, CurrentPage, PageSize, sortColumn, sortAscending, UseLhsDerivedValues);
                    if (!items.Any() && CurrentPage > 1) { CurrentPage = 1; items = _repository.SearchAdvanced(_filterChallanNo, _filterLRNo, _filterFrom, _filterTo, 1, PageSize, sortColumn, sortAscending, UseLhsDerivedValues); }
                }
                else if (string.IsNullOrEmpty(_searchFilter))
                {
                    items = _repository.GetPage(CurrentPage, PageSize, sortColumn, sortAscending, UseLhsDerivedValues);
                }
                else
                {
                    items = _repository.Search(_searchFilter, CurrentPage, PageSize, sortColumn, sortAscending, UseLhsDerivedValues);
                    if (!items.Any() && CurrentPage > 1) { CurrentPage = 1; items = _repository.Search(_searchFilter, 1, PageSize, sortColumn, sortAscending, UseLhsDerivedValues); }
                }

                PagedEntries = new ObservableCollection<ChallanEntry>(items);

                var commentIds = _repository.GetChallanIdsWithComments();
                foreach (var entry in PagedEntries)
                    entry.HasComments = commentIds.Contains(entry.Id);

                if (!BackendSettings.UseRemoteApi && CurrentPage < TotalPages)
                {
                    if (_hasAdvancedFilter)
                        _nextPageCache = _repository.SearchAdvanced(_filterChallanNo, _filterLRNo, _filterFrom, _filterTo, CurrentPage + 1, PageSize, sortColumn, sortAscending, UseLhsDerivedValues);
                    else if (string.IsNullOrEmpty(_searchFilter))
                        _nextPageCache = _repository.GetPage(CurrentPage + 1, PageSize, sortColumn, sortAscending, UseLhsDerivedValues);
                    else
                        _nextPageCache = _repository.Search(_searchFilter, CurrentPage + 1, PageSize, sortColumn, sortAscending, UseLhsDerivedValues);
                }
                else
                {
                    _nextPageCache = null;
                }

                FilteredEntriesCount = _totalCount;
                FilteredTotalDue = totalDue;
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanGoFirst));
                OnPropertyChanged(nameof(CanGoLast));
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

        private class PageLoadResult
        {
            public int TotalCount;
            public decimal TotalDue;
            public int CurrentPage;
            public List<ChallanEntry> Items;
            public HashSet<int> CommentIds;
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
            var filterChallanNo = _filterChallanNo;
            var filterLRNo = _filterLRNo;
            var filterFrom = _filterFrom;
            var filterTo = _filterTo;
            var hasAdvancedFilter = _hasAdvancedFilter;
            var sortColumn = GetEffectiveSortColumn();
            var sortAscending = GetEffectiveSortAscending();
            var countDirty = _countDirty;

            Task.Run(() =>
            {
                var result = new PageLoadResult();
                result.CurrentPage = requestedPage;

                if (hasAdvancedFilter)
                    result.TotalDue = _repository.GetTotalDueAdvanced(filterChallanNo, filterLRNo, filterFrom, filterTo, UseLhsDerivedValues);
                else if (string.IsNullOrEmpty(searchFilter))
                    result.TotalDue = _repository.GetTotalDue("", UseLhsDerivedValues);
                else
                    result.TotalDue = _repository.GetTotalDue(searchFilter, UseLhsDerivedValues);

                var challanRepository = _repository as ChallanRepository;
                if (challanRepository != null)
                {
                    var pageResult = hasAdvancedFilter
                        ? challanRepository.GetRemotePageResult(requestedPage, requestedPageSize, null, sortColumn, sortAscending, filterChallanNo, filterLRNo, filterFrom, filterTo, UseLhsDerivedValues)
                        : challanRepository.GetRemotePageResult(requestedPage, requestedPageSize, searchFilter, sortColumn, sortAscending, null, null, null, null, UseLhsDerivedValues);

                    result.TotalCount = pageResult?.TotalCount ?? 0;
                    result.Items = pageResult?.Items ?? new List<ChallanEntry>();

                    if (!result.Items.Any() && requestedPage > 1)
                    {
                        result.CurrentPage = 1;
                        pageResult = hasAdvancedFilter
                            ? challanRepository.GetRemotePageResult(1, requestedPageSize, null, sortColumn, sortAscending, filterChallanNo, filterLRNo, filterFrom, filterTo, UseLhsDerivedValues)
                            : challanRepository.GetRemotePageResult(1, requestedPageSize, searchFilter, sortColumn, sortAscending, null, null, null, null, UseLhsDerivedValues);
                        result.TotalCount = pageResult?.TotalCount ?? 0;
                        result.Items = pageResult?.Items ?? new List<ChallanEntry>();
                    }
                }
                else
                {
                    if (countDirty)
                    {
                        if (hasAdvancedFilter)
                            result.TotalCount = _repository.GetTotalCountAdvanced(filterChallanNo, filterLRNo, filterFrom, filterTo);
                        else if (string.IsNullOrEmpty(searchFilter))
                            result.TotalCount = _repository.GetTotalCount();
                        else
                            result.TotalCount = _repository.GetTotalCount(searchFilter);
                    }
                    else
                    {
                        result.TotalCount = _totalCount;
                    }

                    if (hasAdvancedFilter)
                    {
                        result.Items = _repository.SearchAdvanced(filterChallanNo, filterLRNo, filterFrom, filterTo, requestedPage, requestedPageSize, sortColumn, sortAscending, UseLhsDerivedValues);
                        if (!result.Items.Any() && requestedPage > 1)
                        {
                            result.CurrentPage = 1;
                            result.Items = _repository.SearchAdvanced(filterChallanNo, filterLRNo, filterFrom, filterTo, 1, requestedPageSize, sortColumn, sortAscending, UseLhsDerivedValues);
                        }
                    }
                    else if (string.IsNullOrEmpty(searchFilter))
                    {
                        result.Items = _repository.GetPage(requestedPage, requestedPageSize, sortColumn, sortAscending, UseLhsDerivedValues);
                    }
                    else
                    {
                        result.Items = _repository.Search(searchFilter, requestedPage, requestedPageSize, sortColumn, sortAscending, UseLhsDerivedValues);
                        if (!result.Items.Any() && requestedPage > 1)
                        {
                            result.CurrentPage = 1;
                            result.Items = _repository.Search(searchFilter, 1, requestedPageSize, sortColumn, sortAscending, UseLhsDerivedValues);
                        }
                    }
                }

                result.CommentIds = _repository.GetChallanIdsWithComments();
                return result;
            }).ContinueWith(task =>
            {
                var dispatcher = Application.Current?.Dispatcher;
                Action apply = () =>
                {
                    try
                    {
                        if (task.Exception != null)
                        {
                            var ex = task.Exception.InnerException ?? task.Exception;
                            onError?.Invoke(ex);
                            return;
                        }

                        var result = task.Result;
                        _suppressPersistence = true;
                        _currentPage = result.CurrentPage;
                        _totalCount = result.TotalCount;
                        _countDirty = false;
                        _nextPageCache = null;
                        PagedEntries = new ObservableCollection<ChallanEntry>(result.Items ?? new List<ChallanEntry>());

                        var commentIds = result.CommentIds ?? new HashSet<int>();
                        foreach (var entry in PagedEntries)
                            entry.HasComments = commentIds.Contains(entry.Id);

                        FilteredEntriesCount = _totalCount;
                        FilteredTotalDue = result.TotalDue;
                        OnPropertyChanged(nameof(CurrentPage));
                        OnPropertyChanged(nameof(PageInfo));
                        OnPropertyChanged(nameof(TotalCount));
                        OnPropertyChanged(nameof(TotalPages));
                        OnPropertyChanged(nameof(CanGoPrevious));
                        OnPropertyChanged(nameof(CanGoNext));
                        OnPropertyChanged(nameof(CanGoFirst));
                        OnPropertyChanged(nameof(CanGoLast));
                        OnPropertyChanged(nameof(HasLoadedPage));
                        _pageLoaded = true;
                        afterLoad?.Invoke();
                    }
                    finally
                    {
                        _suppressPersistence = false;
                        _isLoadingPageAsync = false;
                    }
                };

                if (dispatcher != null && !dispatcher.CheckAccess())
                    dispatcher.BeginInvoke(apply);
                else
                    apply();
            });
        }

        public int GetNextSr() => _repository.GetMaxSr() + 1;
        public IChallanRepository GetRepository() => _repository;

        private bool CanAdd()
        {
            return !string.IsNullOrWhiteSpace(Current?.ChallanNumber);
        }

        private void AddEntry()
        {
            // ensure balance/due are calculated
            // assign Sr
            Current.Sr = Entries.Count + 1;

            // clone Current to new entry so editing Current won't change saved one
            var copy = new ChallanEntry
            {
                Id = Current.Id,
                Sr = Current.Sr,
                ChallanNumber = Current.ChallanNumber,
                Date = Current.Date,
                LRNumber = Current.LRNumber,
                BrokerName = Current.BrokerName,
                From = Current.From,
                To = Current.To,
                VehicleNumber = Current.VehicleNumber,
                VehicleType = Current.VehicleType,
                DriverName = Current.DriverName,
                DriverMobile = Current.DriverMobile,
                EngineNo = Current.EngineNo,
                LicenceNo = Current.LicenceNo,
                PolicyNo = Current.PolicyNo,
                ChassisNo = Current.ChassisNo,
                OwnerName = Current.OwnerName,
                PAN = Current.PAN,
                LorryHire = Current.LorryHire,
                LessTDS = Current.LessTDS,
                AdvanceAmount = Current.AdvanceAmount,
                AdvanceNEFT = Current.AdvanceNEFT,
                AdvanceCash = Current.AdvanceCash,
                AdvanceDate = Current.AdvanceDate,
                Detention = Current.Detention,
                Hamali = Current.Hamali,
                Deduction = Current.Deduction,
                BalancePaidNEFT = Current.BalancePaidNEFT,
                BalancePaidCash = Current.BalancePaidCash,
                BalancePaidDate = Current.BalancePaidDate,
                PaidTo = Current.PaidTo,
                Remarks = Current.Remarks,
                BillAmount = Current.BillAmount,
                Margin = Current.Margin
            };

            // Force recalculation on copy
            copy.RecalculateBalance();

            Entries.Add(copy);

            // reset current
            Current = new ChallanEntry { Date = DateTime.Today };
            OnPropertyChanged(nameof(Current));
        }

        private void Entries_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (ChallanEntry entry in e.OldItems)
                {
                    if (entry != null)
                    {
                        entry.PropertyChanged -= Entry_PropertyChanged;
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (ChallanEntry entry in e.NewItems)
                {
                    if (entry != null)
                    {
                        entry.PropertyChanged += Entry_PropertyChanged;
                    }
                }
            }

            OnPropertyChanged(nameof(TotalDue));
            if (_suppressPersistence) return;
            _countDirty = true;

            if (e.NewItems != null)
            {
                foreach (ChallanEntry entry in e.NewItems)
                {
                    _repository.Upsert(entry);
                }
            }

            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (ChallanEntry entry in e.OldItems)
                {
                    _repository.Delete(entry);
                }
            }
        }

        private void Entry_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var entry = sender as ChallanEntry;

            if (e.PropertyName == nameof(ChallanEntry.Due))
            {
                OnPropertyChanged(nameof(TotalDue));
                if (_suppressPersistence || _isLoadingPage)
                {
                    return;
                }
                return;
            }
            
            if (_suppressPersistence || entry == null)
            {
                return;
            }
            // Persistence is handled after the grid cell edit commits. Avoid
            // saving partial values while the user is typing in remote mode.
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void GoToPreviousPage()
        {
            if (!CanGoPrevious) return;
            if (CurrentPage < TotalPages) _nextPageCache = PagedEntries.ToList();
            _currentPage--;
            if (_prevPageCache != null)
            {
                PagedEntries = new ObservableCollection<ChallanEntry>(_prevPageCache);
                _prevPageCache = null;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanGoFirst));
                OnPropertyChanged(nameof(CanGoLast));
                FilteredEntriesCount = PagedEntries.Count;
            }
            else
            {
                _nextPageCache = null;
                OnPropertyChanged(nameof(CurrentPage));
                LoadPage();
            }
        }

        public void PreCacheNextPage()
        {
            if (CurrentPage < TotalPages)
            {
                int nextPage = CurrentPage + 1;
                int ps = PageSize;
                bool hasAdvanced = _hasAdvancedFilter;
                string fCN = _filterChallanNo, fLR = _filterLRNo, fFrom = _filterFrom, fTo = _filterTo;
                bool hasFilter = !string.IsNullOrEmpty(_searchFilter);
                string filter = _searchFilter;
                string sortColumn = _sortColumn;
                bool sortAscending = _sortAscending;
                System.Threading.Tasks.Task.Run(() =>
                {
                    List<ChallanEntry> data;
                    if (hasAdvanced)
                        data = _repository.SearchAdvanced(fCN, fLR, fFrom, fTo, nextPage, ps, sortColumn, sortAscending, UseLhsDerivedValues);
                    else if (hasFilter)
                        data = _repository.Search(filter, nextPage, ps, sortColumn, sortAscending, UseLhsDerivedValues);
                    else
                        data = _repository.GetPage(nextPage, ps, sortColumn, sortAscending, UseLhsDerivedValues);
                    System.Windows.Application.Current.Dispatcher.Invoke(() => { _nextPageCache = data; });
                });
            }
        }

        public void SetSearchFilter(string filter)
        {
            _searchFilter = (filter ?? "").Trim().ToLower();
            _filterChallanNo = _filterLRNo = _filterFrom = _filterTo = "";
            _countDirty = true; _nextPageCache = null; _prevPageCache = null;
            CurrentPage = 1;
        }

        public void SetAdvancedFilters(string challanNo, string lrNo, string from, string to)
        {
            _filterChallanNo = (challanNo ?? "").Trim();
            _filterLRNo = (lrNo ?? "").Trim();
            _filterFrom = (from ?? "").Trim();
            _filterTo = (to ?? "").Trim();
            _searchFilter = "";
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

        public void ShowSavedEntry(ChallanEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            var existing = PagedEntries.FirstOrDefault(x => x != null && ((entry.Id > 0 && x.Id == entry.Id) ||
                (!string.IsNullOrWhiteSpace(entry.ChallanNumber) &&
                 string.Equals(x.ChallanNumber, entry.ChallanNumber, StringComparison.OrdinalIgnoreCase))));

            var insertedNewRow = false;

            if (existing != null)
            {
                var index = PagedEntries.IndexOf(existing);
                if (index >= 0)
                {
                    PagedEntries[index] = entry;
                }
            }
            else
            {
                var insertIndex = GetInsertIndexForCurrentSort(entry);
                PagedEntries.Insert(insertIndex, entry);
                insertedNewRow = true;

                if (PagedEntries.Count > PageSize)
                {
                    PagedEntries.RemoveAt(PagedEntries.Count - 1);
                }
            }

            if (insertedNewRow)
            {
                _totalCount = Math.Max(_totalCount + 1, PagedEntries.Count);
            }
            FilteredEntriesCount = _totalCount;
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

        public void AcceptSavedEntry(ChallanEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            _suppressPersistence = true;
            try
            {
                if (!Entries.Any(x => (entry.Id > 0 && x.Id == entry.Id) ||
                                      (!string.IsNullOrWhiteSpace(entry.ChallanNumber) &&
                                       string.Equals(x.ChallanNumber, entry.ChallanNumber, StringComparison.OrdinalIgnoreCase))))
                {
                    Entries.Add(entry);
                }
            }
            finally
            {
                _suppressPersistence = false;
            }

            ShowSavedEntry(entry);
        }

        public void RemoveOptimisticEntry(ChallanEntry entry)
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
                    (!string.IsNullOrWhiteSpace(entry.ChallanNumber) &&
                     string.Equals(x.ChallanNumber, entry.ChallanNumber, StringComparison.OrdinalIgnoreCase)));
                if (existing != null)
                {
                    Entries.Remove(existing);
                }

                existing = PagedEntries.FirstOrDefault(x => ReferenceEquals(x, entry) ||
                    (entry.Id > 0 && x.Id == entry.Id) ||
                    (!string.IsNullOrWhiteSpace(entry.ChallanNumber) &&
                     string.Equals(x.ChallanNumber, entry.ChallanNumber, StringComparison.OrdinalIgnoreCase)));
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
            FilteredEntriesCount = _totalCount;
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

        private int GetInsertIndexForCurrentSort(ChallanEntry entry)
        {
            if (PagedEntries == null || PagedEntries.Count == 0)
            {
                return 0;
            }

            for (var i = 0; i < PagedEntries.Count; i++)
            {
                if (CompareEntriesForCurrentSort(entry, PagedEntries[i]) < 0)
                {
                    return i;
                }
            }

            return PagedEntries.Count;
        }

        private int CompareEntriesForCurrentSort(ChallanEntry left, ChallanEntry right)
        {
            if (left == null && right == null) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            var sortKey = GetEffectiveSortColumn().Trim().ToLowerInvariant();
            var multiplier = GetEffectiveSortAscending() ? 1 : -1;

            int CompareString(string a, string b) => string.Compare(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            int CompareDate(DateTime a, DateTime b) => DateTime.Compare(a, b);
            int CompareDecimal(decimal a, decimal b) => decimal.Compare(a, b);

            int result;
            switch (sortKey)
            {
                case "challannumber":
                    result = ChallanNumberFormatter.GetFinancialYearStart(left.ChallanNumber, left.Date)
                        .CompareTo(ChallanNumberFormatter.GetFinancialYearStart(right.ChallanNumber, right.Date));
                    if (result == 0)
                    {
                        result = ChallanNumberFormatter.GetSequence(left.ChallanNumber)
                            .CompareTo(ChallanNumberFormatter.GetSequence(right.ChallanNumber));
                    }
                    if (result == 0)
                    {
                        result = CompareString(left.ChallanNumber, right.ChallanNumber);
                    }
                    break;
                case "date":
                    result = CompareDate(left.Date, right.Date);
                    break;
                case "lrnumber":
                    result = CompareString(left.LRNumber, right.LRNumber);
                    break;
                case "brokername":
                    result = CompareString(left.BrokerName, right.BrokerName);
                    break;
                case "from":
                case "fromlocation":
                    result = CompareString(left.From, right.From);
                    break;
                case "to":
                case "tolocation":
                    result = CompareString(left.To, right.To);
                    break;
                case "vehiclenumber":
                    result = CompareString(left.VehicleNumber, right.VehicleNumber);
                    break;
                case "vehicletype":
                    result = CompareString(left.VehicleType, right.VehicleType);
                    break;
                case "lorryhire":
                    result = CompareDecimal(left.LorryHire, right.LorryHire);
                    break;
                case "other":
                case "otheramount":
                    result = CompareDecimal(left.Other, right.Other);
                    break;
                case "lhs":
                    result = CompareDecimal(left.LHS, right.LHS);
                    break;
                case "balance":
                    result = CompareDecimal(UseLhsDerivedValues ? left.ChallanBalance : left.Balance, UseLhsDerivedValues ? right.ChallanBalance : right.Balance);
                    break;
                case "due":
                    result = CompareDecimal(UseLhsDerivedValues ? left.ChallanDue : left.Due, UseLhsDerivedValues ? right.ChallanDue : right.Due);
                    break;
                case "margin":
                    result = CompareDecimal(UseLhsDerivedValues ? left.ChallanMargin : left.Margin, UseLhsDerivedValues ? right.ChallanMargin : right.Margin);
                    break;
                case "sr":
                default:
                    result = left.Sr.CompareTo(right.Sr);
                    break;
            }

            result *= multiplier;
            if (result != 0)
            {
                return result;
            }

            return multiplier * left.Id.CompareTo(right.Id);
        }

        public void SetSort(string column, bool ascending)
        {
            if (string.IsNullOrWhiteSpace(column))
            {
                _sortColumn = "ChallanNumber";
                _sortAscending = false;
            }
            else
            {
                _sortColumn = column;
                _sortAscending = ascending;
            }
            _countDirty = false;
            _nextPageCache = null;
            _prevPageCache = null;
            _pageLoaded = false;
            CurrentPage = 1;
            SaveColumnSettings();
        }

        public void ResetToLatestChallanView()
        {
            _sortColumn = "ChallanNumber";
            _sortAscending = false;
            _countDirty = true;
            _nextPageCache = null;
            _prevPageCache = null;
            _pageLoaded = false;
            CurrentPage = 1;
            SaveColumnSettings();
        }

        public void GoToNextPage()
        {
            if (!CanGoNext) return;

            _prevPageCache = PagedEntries.ToList();

            _currentPage++;

            if (_nextPageCache != null)
            {
                PagedEntries = new ObservableCollection<ChallanEntry>(_nextPageCache);
                _nextPageCache = null;

                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanGoFirst));
                OnPropertyChanged(nameof(CanGoLast));
                FilteredEntriesCount = _totalCount;
            }
            else
            {
                OnPropertyChanged(nameof(CurrentPage));
                LoadPage();
            }
        }

        public void GoToFirstPage()
        {
            CurrentPage = 1;
        }

        public void GoToLastPage()
        {
            CurrentPage = TotalPages;
        }

        public void UpdatePage()
        {
            PagedEntries.Clear();
            var pageItems = Entries.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();
            foreach (var item in pageItems)
            {
                PagedEntries.Add(item);
            }
            OnPropertyChanged(nameof(PageInfo));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoFirst));
            OnPropertyChanged(nameof(CanGoLast));
            FilteredEntriesCount = _totalCount;
        }

        public IEnumerable<ChallanEntry> GetFilteredEntriesForSummary()
        {
            IEnumerable<ChallanEntry> rows = _repository.GetAll() ?? Enumerable.Empty<ChallanEntry>();

            if (_hasAdvancedFilter)
            {
                if (!string.IsNullOrWhiteSpace(_filterChallanNo))
                    rows = rows.Where(e => ContainsText(e?.ChallanNumber, _filterChallanNo));
                if (!string.IsNullOrWhiteSpace(_filterLRNo))
                    rows = rows.Where(e => ContainsText(e?.LRNumber, _filterLRNo));
                if (!string.IsNullOrWhiteSpace(_filterFrom))
                    rows = rows.Where(e => ContainsText(e?.From, _filterFrom));
                if (!string.IsNullOrWhiteSpace(_filterTo))
                    rows = rows.Where(e => ContainsText(e?.To, _filterTo));
            }
            else if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                var search = _searchFilter;
                rows = rows.Where(e =>
                    ContainsText(e?.ChallanNumber, search) ||
                    ContainsText(e?.LRNumber, search) ||
                    ContainsText(e?.VehicleNumber, search) ||
                    ContainsText(e?.VehicleType, search) ||
                    ContainsText(e?.DriverName, search) ||
                    ContainsText(e?.BrokerName, search) ||
                    ContainsText(e?.From, search) ||
                    ContainsText(e?.To, search) ||
                    ContainsText(e?.OwnerName, search));
            }

            return rows;
        }

        private static bool ContainsText(string value, string filter)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(filter) &&
                   value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
