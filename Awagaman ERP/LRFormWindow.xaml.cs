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

namespace Awagaman_ERP
{
    public partial class LRFormWindow : MetroWindow, INotifyPropertyChanged
    {
        private LREntry _currentEntry;
        private System.Collections.Generic.IEnumerable<ChallanEntry> _challanEntries;
        private System.Collections.Generic.IEnumerable<LREntry> _existingEntries;
        private decimal? _challanLorryHire;
        public LREntry Result { get; private set; }

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
            _challanEntries = challanEntries;
            _existingEntries = existingEntries;

            if (prefillFrom != null)
            {
                CurrentEntry = new LREntry
                {
                    Date = DateTime.Today,
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
                    CHNo = entryToEdit.CHNo,
                    TotalFreight = entryToEdit.TotalFreight,
                    Hamali = entryToEdit.Hamali,
                    Detention = entryToEdit.Detention,
                    Others = entryToEdit.Others,
                    NEFT = entryToEdit.NEFT,
                    CASH = entryToEdit.CASH,
                    TDS = entryToEdit.TDS,
                    Ded = entryToEdit.Ded,
                    BillNo = entryToEdit.BillNo,
                    BillDate = entryToEdit.BillDate,
                    BILL = entryToEdit.BILL,
                    BillParty = entryToEdit.BillParty,
                    Broker = entryToEdit.Broker,
                    FrtType = entryToEdit.FrtType,
                    PayType = entryToEdit.PayType,
                    Comm = entryToEdit.Comm,
                    Paid = entryToEdit.Paid
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
                using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT LorryHire FROM Challans WHERE TRIM(COALESCE(ChallanNumber,'')) = @no LIMIT 1;";
                        cmd.Parameters.AddWithValue("@no", key);
                        var value = cmd.ExecuteScalar();
                        if (value == null || value == DBNull.Value) return null;
                        return Convert.ToDecimal(value);
                    }
                }
            }
            catch
            {
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
                var challan = FindChallanByNumber(CurrentEntry?.CHNo);
                if (challan == null) return;

                CurrentEntry.From = challan.From;
                CurrentEntry.To = challan.To;
                CurrentEntry.VehicleNo = challan.VehicleNumber;
                CurrentEntry.VehicleType = challan.VehicleType;
                CurrentEntry.Broker = challan.BrokerName;
            }
            catch { }
        }

        private void RefreshChallanLorryHire(bool clearWhenMissing)
        {
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

            var enteredLrNo = (CurrentEntry.LRNo ?? string.Empty).Trim();
            var duplicate = _existingEntries?
                .FirstOrDefault(x => x.Id != CurrentEntry.Id &&
                                     string.Equals((x.LRNo ?? string.Empty).Trim(), enteredLrNo, StringComparison.OrdinalIgnoreCase));

            if (duplicate == null && LRNoExistsInDatabase(enteredLrNo, CurrentEntry.Id))
            {
                duplicate = new LREntry { LRNo = enteredLrNo };
            }

            if (duplicate != null)
            {
                MessageBox.Show($"LR No '{CurrentEntry.LRNo}' already exists in LR Ledger.", "Duplicate Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Auto-save parties to Party Ledger
            SavePartyIfNew(CurrentEntry.ConsignorName, CurrentEntry.ConsignorAddress, CurrentEntry.ConsignorGST);
            SavePartyIfNew(CurrentEntry.ConsigneeName, CurrentEntry.ConsigneeAddress, CurrentEntry.ConsigneeGST);

            Result = CurrentEntry;
            DialogResult = true;
            Close();
        }

        private static bool LRNoExistsInDatabase(string lrNo, int excludeId)
        {
            if (string.IsNullOrWhiteSpace(lrNo)) return false;
            using (var conn = new System.Data.SQLite.SQLiteConnection(AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT COUNT(*)
FROM LREntries
WHERE TRIM(LRNo) = TRIM(@lrNo)
  AND (@excludeId <= 0 OR Id <> @excludeId);";
                    cmd.Parameters.AddWithValue("@lrNo", lrNo.Trim());
                    cmd.Parameters.AddWithValue("@excludeId", excludeId);
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
        }

        private void ConsignorName_TextChanged(object sender, TextChangedEventArgs e)
        {
            ShowSuggestions(ConsignorNameBox?.Text, ConsignorSuggestionList, ConsignorPopup);
        }

        private void ConsigneeName_TextChanged(object sender, TextChangedEventArgs e)
        {
            ShowSuggestions(ConsigneeNameBox?.Text, ConsigneeSuggestionList, ConsigneePopup);
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
                    box.Text = name;
                    popup.IsOpen = false;
                    AutoFillParty(name, onFill);
                }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                popup.IsOpen = false;
            }
        }

        private bool _ignoreSelection;

        private void ShowSuggestions(string text, ListBox listBox, Popup popup)
        {
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

        private void ApplySuggestion(ListBox listBox, TextBox textBox, Popup popup, Action<PartyEntry> onFill)
        {
            if (listBox.SelectedItem is string name)
            {
                textBox.Text = name;
                popup.IsOpen = false;
                AutoFillParty(name, onFill);
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
            catch { }
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
            catch { }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
