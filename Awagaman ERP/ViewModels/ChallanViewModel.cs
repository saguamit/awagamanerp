using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using Awagaman_ERP.Data;
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

        // Exposed for the Sorting event handler to determine toggle direction
        public bool IsCurrentSortAscending
        {
            get => string.IsNullOrEmpty(_sortColumn) || _sortAscending;
        }
        public string GetSortColumn() => _sortColumn;
        private List<ChallanEntry> _nextPageCache;
        private List<ChallanEntry> _prevPageCache;
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

        public string PageInfo => $"Page {CurrentPage} of {Math.Max(1, TotalPages)}";

        public decimal TotalDue => Entries.Sum(entry => entry?.Due ?? 0m);
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
            _suppressPersistence = true;
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

            List<ChallanEntry> items;
            if (_hasAdvancedFilter)
            {
                items = _repository.SearchAdvanced(_filterChallanNo, _filterLRNo, _filterFrom, _filterTo, CurrentPage, PageSize, _sortColumn, _sortAscending);
                if (!items.Any() && CurrentPage > 1) { CurrentPage = 1; items = _repository.SearchAdvanced(_filterChallanNo, _filterLRNo, _filterFrom, _filterTo, 1, PageSize, _sortColumn, _sortAscending); }
            }
            else if (string.IsNullOrEmpty(_searchFilter))
            {
                items = _repository.GetPage(CurrentPage, PageSize, _sortColumn, _sortAscending);
            }
            else
            {
                items = _repository.Search(_searchFilter, CurrentPage, PageSize, _sortColumn, _sortAscending);
                if (!items.Any() && CurrentPage > 1) { CurrentPage = 1; items = _repository.Search(_searchFilter, 1, PageSize, _sortColumn, _sortAscending); }
            }

            PagedEntries = new ObservableCollection<ChallanEntry>(items);

            // Mark challans with comments
            var commentIds = _repository.GetChallanIdsWithComments();
            foreach (var entry in PagedEntries)
                entry.HasComments = commentIds.Contains(entry.Id);

            if (CurrentPage < TotalPages)
            {
                if (_hasAdvancedFilter)
                    _nextPageCache = _repository.SearchAdvanced(_filterChallanNo, _filterLRNo, _filterFrom, _filterTo, CurrentPage + 1, PageSize, _sortColumn, _sortAscending);
                else if (string.IsNullOrEmpty(_searchFilter))
                    _nextPageCache = _repository.GetPage(CurrentPage + 1, PageSize, _sortColumn, _sortAscending);
                else
                    _nextPageCache = _repository.Search(_searchFilter, CurrentPage + 1, PageSize, _sortColumn, _sortAscending);
            }
            else
                _nextPageCache = null;

            _suppressPersistence = false;
            FilteredEntriesCount = PagedEntries.Count;
            OnPropertyChanged(nameof(PageInfo));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoFirst));
            OnPropertyChanged(nameof(CanGoLast));
            _pageLoaded = true;
        }

        public void EnsurePageLoaded()
        {
            if (!_pageLoaded || _countDirty || PagedEntries.Count == 0)
            {
                LoadPage();
            }
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
            if (_suppressPersistence) return;
            LoadPage();
            }
            
            if (_suppressPersistence || entry == null)
            {
                return;
            }

            if (e.PropertyName != nameof(ChallanEntry.Balance) && e.PropertyName != nameof(ChallanEntry.Due))
            {
                _repository.Upsert(entry);
            }
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
                System.Threading.Tasks.Task.Run(() =>
                {
                    List<ChallanEntry> data;
                    if (hasAdvanced)
                        data = _repository.SearchAdvanced(fCN, fLR, fFrom, fTo, nextPage, ps);
                    else if (hasFilter)
                        data = _repository.Search(filter, nextPage, ps);
                    else
                        data = _repository.GetPage(nextPage, ps);
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

        public void SetSort(string column, bool ascending)
        {
            _sortColumn = column ?? "";
            _sortAscending = ascending;
            _countDirty = false;
            _nextPageCache = null;
            _prevPageCache = null;
            LoadPage();
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
                FilteredEntriesCount = PagedEntries.Count;
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
            FilteredEntriesCount = PagedEntries.Count;
        }
    }
}
