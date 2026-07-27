using System;
using System.Collections.Generic;
using System.Windows;
using MahApps.Metro.Controls;
using Awagaman_ERP.Models;
using Awagaman_ERP.Data;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Awagaman_ERP
{
    public partial class LRFormWindow : MetroWindow, INotifyPropertyChanged
    {
        private LREntry _currentEntry;
        private System.Collections.Generic.IEnumerable<ChallanEntry> _challanEntries;
        private decimal? _challanLorryHire;
        private readonly DispatcherTimer _partySuggestionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        private string _pendingSuggestionText;
        private ListBox _pendingSuggestionListBox;
        private Popup _pendingSuggestionPopup;
        private bool _suppressSuggestionQueue;
        public LREntry Result { get; private set; }
        public bool WasSaved { get; private set; }

        public LREntry CurrentEntry
        {
            get => _currentEntry;
            set
            {
                if (_currentEntry != null)
                {
                    _currentEntry.PropertyChanged -= CurrentEntry_PropertyChanged;
                }
                _currentEntry = value;
                if (_currentEntry != null)
                {
                    _currentEntry.PropertyChanged += CurrentEntry_PropertyChanged;
                }
                OnPropertyChanged(nameof(CurrentEntry));
            }
        }
        public decimal? ChallanLorryHire
        {
            get => _challanLorryHire;
            set
            {
                _challanLorryHire = value;
                OnPropertyChanged(nameof(ChallanLorryHire));
                OnPropertyChanged(nameof(HasChallanLorryHire));
            }
        }
        public bool HasChallanLorryHire => ChallanLorryHire.HasValue;

        public LRFormWindow(
            System.Collections.Generic.IEnumerable<ChallanEntry> challanEntries = null,
            System.Collections.Generic.IEnumerable<LREntry> existingEntries = null,
            LREntry entryToEdit = null,
            ChallanEntry prefillFrom = null)
        {
            InitializeComponent();
            ConfigureDebounceTimers();
            _challanEntries = challanEntries;
            if (prefillFrom != null)
            {
                CurrentEntry = new LREntry
                {
                    Date = prefillFrom.Date == default(DateTime) ? DateTime.Today : prefillFrom.Date,
                    From = prefillFrom.From,
                    To = prefillFrom.To,
                    VehicleNo = prefillFrom.VehicleNumber,
                    VehicleType = prefillFrom.VehicleType,
                    CHNo = prefillFrom.ChallanNumber
                };
                ChallanLorryHire = prefillFrom.LorryHire;
            }
            else if (entryToEdit != null)
            {
                // Clone or edit directly. For simplicity, we create a basic clone
                CurrentEntry = new LREntry
                {
                    Id = entryToEdit.Id,
                    Sr = entryToEdit.Sr,
                    LRNo = entryToEdit.LRNo,
                    Date = entryToEdit.Date,
                    ConsignorName = entryToEdit.ConsignorName,
                    ConsignorAddress = entryToEdit.ConsignorAddress,
                    ConsignorGST = entryToEdit.ConsignorGST,
                    ConsigneeName = entryToEdit.ConsigneeName,
                    ConsigneeAddress = entryToEdit.ConsigneeAddress,
                    ConsigneeGST = entryToEdit.ConsigneeGST,
                    From = entryToEdit.From,
                    To = entryToEdit.To,
                    VehicleNo = entryToEdit.VehicleNo,
                    VehicleType = entryToEdit.VehicleType,
                    SizeL = entryToEdit.SizeL,
                    SizeW = entryToEdit.SizeW,
                    SizeH = entryToEdit.SizeH,
                    ActualWeight = entryToEdit.ActualWeight,
                    ChargedWeight = entryToEdit.ChargedWeight,
                    PKG = entryToEdit.PKG,
                    PkgType = entryToEdit.PkgType,
                    Description = entryToEdit.Description,
                    Invoice = entryToEdit.Invoice,
                    Value = entryToEdit.Value,
                    CHNo = entryToEdit.CHNo,
                    TotalFreight = entryToEdit.TotalFreight,
                    Hamali = entryToEdit.Hamali,
                    Detention = entryToEdit.Detention,
                    Others = entryToEdit.Others,
                    StCharge = entryToEdit.StCharge,
                    NEFT = entryToEdit.NEFT,
                    CASH = entryToEdit.CASH,
                    TDS = entryToEdit.TDS,
                    Ded = entryToEdit.Ded,
                    BillNo = entryToEdit.BillNo,
                    BillDate = entryToEdit.BillDate,
                    BILL = entryToEdit.BILL,
                    ChallanLorryHire = entryToEdit.ChallanLorryHire,
                    BillParty = entryToEdit.BillParty,
                    Broker = entryToEdit.Broker,
                    FrtType = entryToEdit.FrtType,
                    PayType = entryToEdit.PayType,
                    Comm = entryToEdit.Comm,
                    Paid = entryToEdit.Paid,
                    PreserveImportedBilling = entryToEdit.PreserveImportedBilling
                };
            }
            else
            {
                CurrentEntry = new LREntry { Date = DateTime.Today };
                ChallanLorryHire = null;
            }

            DataContext = this;
            ApplyChallanDetailsFromCHNo();
            RefreshChallanLorryHire(clearWhenMissing: false);
        }

        private void ConfigureDebounceTimers()
        {
            _partySuggestionTimer.Tick += PartySuggestionTimer_Tick;
            Closed += (s, e) => _partySuggestionTimer.Stop();
        }

        private void LRNo_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CurrentEntry.LRNo) || _challanEntries == null) return;

            // If CH No is already set and valid, keep that challan locked.
            var lockedByChNo = FindChallanByNumber(CurrentEntry?.CHNo);
            if (lockedByChNo != null)
            {
                CurrentEntry.From = lockedByChNo.From;
                CurrentEntry.To = lockedByChNo.To;
                CurrentEntry.VehicleNo = lockedByChNo.VehicleNumber;
                CurrentEntry.VehicleType = lockedByChNo.VehicleType;
                CurrentEntry.CHNo = lockedByChNo.ChallanNumber;
                ChallanLorryHire = lockedByChNo.LorryHire;
                return;
            }

            string enteredLr = CurrentEntry.LRNo.Trim().ToLower();
            ChallanEntry matchingChallan = null;

            foreach (var challan in _challanEntries)
            {
                if (string.IsNullOrWhiteSpace(challan.LRNumber)) continue;

                var parts = challan.LRNumber.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                bool found = false;
                foreach (var part in parts)
                {
                    if (string.Equals(part.Trim(), enteredLr, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }

                // Keep matching strict to avoid switching to the wrong challan (e.g. LR-1 matching LR-10).
                if (!found && string.Equals((challan.LRNumber ?? string.Empty).Trim(), enteredLr, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                }

                if (found)
                {
                    matchingChallan = challan;
                    break;
                }
            }

            if (matchingChallan == null)
            {
                matchingChallan = FindChallanByNumber(CurrentEntry?.CHNo);
                if (matchingChallan == null)
                {
                    // Do not clear existing challan lorry hire while user is typing other fields.
                    // Keep the last resolved value unless CH No. is explicitly changed to another valid challan.
                    return;
                }
            }

            CurrentEntry.From = matchingChallan.From;
            CurrentEntry.To = matchingChallan.To;
            CurrentEntry.VehicleNo = matchingChallan.VehicleNumber;
            CurrentEntry.VehicleType = matchingChallan.VehicleType;
            CurrentEntry.CHNo = matchingChallan.ChallanNumber;
            RefreshChallanLorryHire(clearWhenMissing: false);
        }

        private ChallanEntry FindChallanByNumber(string challanNo)
        {
            var key = (challanNo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key) || _challanEntries == null) return null;
            return _challanEntries.FirstOrDefault(c =>
                string.Equals((c?.ChallanNumber ?? string.Empty).Trim(), key, StringComparison.OrdinalIgnoreCase));
        }

        private decimal? FindChallanLorryHireFromDatabase(string challanNo)
        {
            var key = (challanNo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key)) return null;
            try
            {
                var challan = new ChallanRepository().GetAll()
                    .FirstOrDefault(c => string.Equals((c.ChallanNumber ?? string.Empty).Trim(), key, StringComparison.OrdinalIgnoreCase));
                return challan != null ? challan.LorryHire : (decimal?)null;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(nameof(FindChallanLorryHireFromDatabase), ex);
                return null;
            }
        }

        private void CurrentEntry_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e?.PropertyName == nameof(LREntry.CHNo))
            {
                ApplyChallanDetailsFromCHNo();
                RefreshChallanLorryHire(clearWhenMissing: false);
            }
        }

        private void ApplyChallanDetailsFromCHNo()
        {
            try
            {
                if (CurrentEntry?.PreserveImportedBilling == true)
                {
                    return;
                }

                var challan = FindChallanByNumber(CurrentEntry?.CHNo);
                if (challan == null) return;

                CurrentEntry.From = challan.From;
                CurrentEntry.To = challan.To;
                CurrentEntry.VehicleNo = challan.VehicleNumber;
                CurrentEntry.VehicleType = challan.VehicleType;
                CurrentEntry.Broker = challan.BrokerName;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(nameof(ApplyChallanDetailsFromCHNo), ex);
            }
        }

        private void RefreshChallanLorryHire(bool clearWhenMissing)
        {
            if (CurrentEntry?.PreserveImportedBilling == true)
            {
                ChallanLorryHire = CurrentEntry.ChallanLorryHire;
                return;
            }

            var challan = FindChallanByNumber(CurrentEntry?.CHNo);
            if (challan != null)
            {
                ChallanLorryHire = challan.LorryHire;
            }
            else
            {
                var lorryHire = FindChallanLorryHireFromDatabase(CurrentEntry?.CHNo);
                if (lorryHire.HasValue)
                {
                    ChallanLorryHire = lorryHire.Value;
                    return;
                }
            }

            if (clearWhenMissing)
            {
                ChallanLorryHire = null;
            }
        }

        private void NumericBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.SelectAll();
            }
        }

        private void NumericBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is TextBox tb)) return;
            if (!tb.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                tb.Focus();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            FocusManager.SetFocusedElement(this, this);
            Keyboard.ClearFocus();

            if (string.IsNullOrWhiteSpace(CurrentEntry?.LRNo))
            {
                MessageBox.Show("LR No cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentEntry?.ConsignorName))
            {
                MessageBox.Show("Consignor Name is mandatory.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ConsignorNameBox?.Focus();
                ConsignorNameBox?.SelectAll();
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentEntry?.ConsignorAddress))
            {
                MessageBox.Show("Consignor Address is mandatory.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ConsignorAddressBox?.Focus();
                ConsignorAddressBox?.SelectAll();
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentEntry?.ConsignorGST))
            {
                MessageBox.Show("Consignor GST is mandatory.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ConsignorGSTBox?.Focus();
                ConsignorGSTBox?.SelectAll();
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentEntry?.ConsigneeName))
            {
                MessageBox.Show("Consignee Name is mandatory.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ConsigneeNameBox?.Focus();
                ConsigneeNameBox?.SelectAll();
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentEntry?.ConsigneeAddress))
            {
                MessageBox.Show("Consignee Address is mandatory.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentEntry?.ConsigneeGST))
            {
                MessageBox.Show("Consignee GST is mandatory.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var enteredLrNo = (CurrentEntry.LRNo ?? string.Empty).Trim();
            if (LRNoExistsInDatabase(enteredLrNo, CurrentEntry.Id))
            {
                MessageBox.Show($"LR No '{CurrentEntry.LRNo}' already exists in LR Ledger.", "Duplicate Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentEntry?.BillParty))
            {
                MessageBox.Show("Bill Party is mandatory.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                BillPartyBox?.Focus();
                BillPartyBox?.SelectAll();
                return;
            }

            Result = CurrentEntry;
            WasSaved = true;
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
            {
                DialogResult = true;
            }
            Close();
        }

        private static bool LRNoExistsInDatabase(string lrNo, int excludeId)
        {
            if (string.IsNullOrWhiteSpace(lrNo)) return false;
            return new LRRepository().ExistsLRNo(lrNo, excludeId);
        }

        private void ConsignorName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSuggestionQueue) return;
            QueueSuggestions(ConsignorNameBox?.Text, ConsignorSuggestionList, ConsignorPopup);
        }

        private void ConsigneeName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSuggestionQueue) return;
            QueueSuggestions(ConsigneeNameBox?.Text, ConsigneeSuggestionList, ConsigneePopup);
        }

        private void BillPartyBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSuggestionQueue) return;
            QueueSuggestions(BillPartyBox?.Text, BillPartySuggestionList, BillPartyPopup);
        }

        private void LRFormWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.Enter || e.Key == Key.Escape)
            {
                if (ConsignorPopup.IsOpen && ConsignorNameBox.IsFocused)
                {
                    HandleSuggestionKey(e, ConsignorSuggestionList, ConsignorNameBox, ConsignorPopup, val =>
                    {
                        if (CurrentEntry != null)
                        {
                            CurrentEntry.ConsignorAddress = val.Address;
                            CurrentEntry.ConsignorGST = val.GSTNo;
                        }
                    });
                }
                else if (ConsigneePopup.IsOpen && ConsigneeNameBox.IsFocused)
                {
                    HandleSuggestionKey(e, ConsigneeSuggestionList, ConsigneeNameBox, ConsigneePopup, val =>
                    {
                        if (CurrentEntry != null)
                        {
                            CurrentEntry.ConsigneeAddress = val.Address;
                            CurrentEntry.ConsigneeGST = val.GSTNo;
                        }
                    });
                }
                else if (BillPartyPopup.IsOpen && BillPartyBox.IsFocused)
                {
                    HandleSuggestionKey(e, BillPartySuggestionList, BillPartyBox, BillPartyPopup, _ => { });
                }
            }
        }

        private void HandleSuggestionKey(KeyEventArgs e, ListBox list, TextBox box, Popup popup, Action<PartyEntry> onFill)
        {
            if (!popup.IsOpen || list.Items.Count == 0) return;

            if (e.Key == Key.Down)
            {
                e.Handled = true;
                if (list.SelectedIndex < list.Items.Count - 1)
                {
                    _ignoreSelection = true;
                    list.SelectedIndex++;
                    _ignoreSelection = false;
                    list.ScrollIntoView(list.SelectedItem);
                }
            }
            else if (e.Key == Key.Up)
            {
                e.Handled = true;
                if (list.SelectedIndex > 0)
                {
                    _ignoreSelection = true;
                    list.SelectedIndex--;
                    _ignoreSelection = false;
                    list.ScrollIntoView(list.SelectedItem);
                }
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (list.SelectedItem is string name)
                {
                    ApplySuggestionValue(box, popup, name, onFill);
                }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                popup.IsOpen = false;
            }
        }

        private bool _ignoreSelection;

        private void QueueSuggestions(string text, ListBox listBox, Popup popup)
        {
            if (popup == null || listBox == null) return;
            _partySuggestionTimer.Stop();
            _pendingSuggestionText = text;
            _pendingSuggestionListBox = listBox;
            _pendingSuggestionPopup = popup;

            if (string.IsNullOrWhiteSpace(text))
            {
                popup.IsOpen = false;
                return;
            }

            _partySuggestionTimer.Start();
        }

        private void PartySuggestionTimer_Tick(object sender, EventArgs e)
        {
            _partySuggestionTimer.Stop();
            ShowSuggestions(_pendingSuggestionText, _pendingSuggestionListBox, _pendingSuggestionPopup);
        }

        private void ShowSuggestions(string text, ListBox listBox, Popup popup)
        {
            if (popup == null || listBox == null) return;
            if (string.IsNullOrWhiteSpace(text))
            {
                popup.IsOpen = false;
                return;
            }
            var matches = new PartyRepository().SearchNames(text);
            _ignoreSelection = true;
            listBox.ItemsSource = matches;
            if (matches.Count > 0) listBox.SelectedIndex = 0;
            _ignoreSelection = false;
            popup.IsOpen = matches.Count > 0;
        }

        private void ConsignorSuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ignoreSelection) return;
            ApplySuggestion(ConsignorSuggestionList, ConsignorNameBox, ConsignorPopup, val =>
            {
                if (CurrentEntry != null)
                {
                    CurrentEntry.ConsignorAddress = val.Address;
                    CurrentEntry.ConsignorGST = val.GSTNo;
                }
            });
        }

        private void ConsignorSuggestionList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ApplySuggestionFromMouseClick(ConsignorSuggestionList, ConsignorNameBox, ConsignorPopup, e, val =>
            {
                if (CurrentEntry != null)
                {
                    CurrentEntry.ConsignorAddress = val.Address;
                    CurrentEntry.ConsignorGST = val.GSTNo;
                }
            });
        }

        private void ConsigneeSuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ignoreSelection) return;
            ApplySuggestion(ConsigneeSuggestionList, ConsigneeNameBox, ConsigneePopup, val =>
            {
                if (CurrentEntry != null)
                {
                    CurrentEntry.ConsigneeAddress = val.Address;
                    CurrentEntry.ConsigneeGST = val.GSTNo;
                }
            });
        }

        private void ConsigneeSuggestionList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ApplySuggestionFromMouseClick(ConsigneeSuggestionList, ConsigneeNameBox, ConsigneePopup, e, val =>
            {
                if (CurrentEntry != null)
                {
                    CurrentEntry.ConsigneeAddress = val.Address;
                    CurrentEntry.ConsigneeGST = val.GSTNo;
                }
            });
        }

        private void BillPartySuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ignoreSelection) return;
            ApplySuggestion(BillPartySuggestionList, BillPartyBox, BillPartyPopup, _ => { });
        }

        private void BillPartySuggestionList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ApplySuggestionFromMouseClick(BillPartySuggestionList, BillPartyBox, BillPartyPopup, e, _ => { });
        }

        private void ApplySuggestion(ListBox listBox, TextBox textBox, Popup popup, Action<PartyEntry> onFill)
        {
            if (listBox.SelectedItem is string name)
            {
                ApplySuggestionValue(textBox, popup, name, onFill);
            }
        }

        private void ApplySuggestionFromMouseClick(ListBox listBox, TextBox textBox, Popup popup, MouseButtonEventArgs e, Action<PartyEntry> onFill)
        {
            var clickedItem = ItemsControl.ContainerFromElement(listBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            var name = clickedItem?.Content as string;
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            _ignoreSelection = true;
            listBox.SelectedItem = name;
            _ignoreSelection = false;
            ApplySuggestionValue(textBox, popup, name, onFill);
            e.Handled = true;
        }

        private void ApplySuggestionValue(TextBox textBox, Popup popup, string name, Action<PartyEntry> onFill)
        {
            _partySuggestionTimer.Stop();
            _suppressSuggestionQueue = true;
            try
            {
                textBox.Text = name;
                popup.IsOpen = false;
                AutoFillParty(name, onFill);
                textBox.CaretIndex = textBox.Text.Length;
            }
            finally
            {
                _suppressSuggestionQueue = false;
            }
        }

        private void PartyName_LostFocus(object sender, RoutedEventArgs e)
        {
            AutoFillParty(CurrentEntry?.ConsignorName, val =>
            {
                if (CurrentEntry != null)
                {
                    if (string.IsNullOrWhiteSpace(CurrentEntry.ConsignorAddress)) CurrentEntry.ConsignorAddress = val.Address;
                    if (string.IsNullOrWhiteSpace(CurrentEntry.ConsignorGST)) CurrentEntry.ConsignorGST = val.GSTNo;
                }
            });
        }

        private void ConsigneeName_LostFocus(object sender, RoutedEventArgs e)
        {
            AutoFillParty(CurrentEntry?.ConsigneeName, val =>
            {
                if (CurrentEntry != null)
                {
                    if (string.IsNullOrWhiteSpace(CurrentEntry.ConsigneeAddress)) CurrentEntry.ConsigneeAddress = val.Address;
                    if (string.IsNullOrWhiteSpace(CurrentEntry.ConsigneeGST)) CurrentEntry.ConsigneeGST = val.GSTNo;
                }
            });
        }

        private void AutoFillParty(string name, Action<PartyEntry> onFound)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var party = new PartyRepository().FindByName(name);
                if (party != null) onFound(party);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(nameof(AutoFillParty), ex);
            }
        }

        private void SavePartyIfNew(string name, string address, string gst)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var repo = new PartyRepository();
                if (repo.FindByName(name) == null)
                {
                    var all = repo.GetAll();
                    int maxSr = all.Count > 0 ? all.Max(x => x.Sr) : 0;
                    repo.Upsert(new PartyEntry { Sr = maxSr + 1, PartyName = name.Trim(), Address = address?.Trim() ?? "", GSTNo = gst?.Trim() ?? "" });
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(nameof(SavePartyIfNew), ex);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            WasSaved = false;
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
            {
                DialogResult = false;
            }
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
