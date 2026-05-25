using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Awagaman_ERP.Data;
using Awagaman_ERP.Models;
using MahApps.Metro.Controls;

namespace Awagaman_ERP
{
    public partial class ChallanFormWindow : MetroWindow
    {
        public ChallanEntry Result { get; private set; }
        private IEnumerable<ChallanEntry> _allEntries;
        private int _editingSr;
        private readonly IChallanRepository _repository;
        private bool _ignoreVehicleSelection;

        public ChallanFormWindow(IEnumerable<ChallanEntry> allEntries, IChallanRepository repository = null)
        {
            InitializeComponent();
            _allEntries = allEntries ?? new List<ChallanEntry>();
            _repository = repository ?? new ChallanRepository();
            _editingSr = -1;
            Result = new ChallanEntry { AdvanceDate = System.DateTime.Today };
            DataContext = Result;
        }

        public ChallanFormWindow(ChallanEntry existing, IEnumerable<ChallanEntry> allEntries, IChallanRepository repository = null)
        {
            InitializeComponent();
            _allEntries = allEntries ?? new List<ChallanEntry>();
            _repository = repository ?? new ChallanRepository();
            _editingSr = existing.Sr;

            Result = new ChallanEntry
            {
                Id = existing.Id,
                Sr = existing.Sr,
                ChallanNumber = existing.ChallanNumber,
                Date = existing.Date,
                LRNumber = existing.LRNumber,
                BrokerName = existing.BrokerName,
                From = existing.From,
                To = existing.To,
                VehicleNumber = existing.VehicleNumber,
                VehicleType = existing.VehicleType,
                DriverName = existing.DriverName,
                DriverMobile = existing.DriverMobile,
                EngineNo = existing.EngineNo,
                LicenceNo = existing.LicenceNo,
                PolicyNo = existing.PolicyNo,
                ChassisNo = existing.ChassisNo,
                OwnerName = existing.OwnerName,
                PAN = existing.PAN,
                LorryHire = existing.LorryHire,
                LessTDS = existing.LessTDS,
                AdvanceAmount = existing.AdvanceAmount,
                AdvanceNEFT = existing.AdvanceNEFT,
                AdvanceCash = existing.AdvanceCash,
                AdvanceDate = existing.AdvanceDate,
                Detention = existing.Detention,
                Hamali = existing.Hamali,
                Deduction = existing.Deduction,
                BalancePaidNEFT = existing.BalancePaidNEFT,
                BalancePaidCash = existing.BalancePaidCash,
                BalancePaidDate = existing.BalancePaidDate,
                PaidTo = existing.PaidTo,
                Remarks = existing.Remarks,
                BillAmount = existing.BillAmount,
                Margin = existing.Margin
            };
            DataContext = Result;
        }

        private void ChallanNo_LostFocus(object sender, RoutedEventArgs e)
        {
            var num = ChallanNoBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(num)) { ChallanDuplicateWarning.Visibility = Visibility.Collapsed; return; }
            try
            {
                var existing = _repository.FindByChallanNumber(num);
                ChallanDuplicateWarning.Visibility = (existing != null && existing.Sr != _editingSr) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { ChallanDuplicateWarning.Visibility = Visibility.Collapsed; }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Result.ChallanNumber))
            {
                MessageBox.Show("Challan Number cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dup = _repository.FindByChallanNumber(Result.ChallanNumber);
                if (dup != null && dup.Sr != _editingSr)
                {
                    MessageBox.Show($"Challan Number '{Result.ChallanNumber}' already exists.", "Duplicate Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(Result.LRNumber))
            {
                var inputLRs = Result.LRNumber.Split(new[] { ',', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                foreach (var other in _allEntries.Where(x => x.Sr != _editingSr && !string.IsNullOrWhiteSpace(x.LRNumber)))
                {
                    var otherLRs = other.LRNumber.Split(new[] { ',', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());
                    var overlap = inputLRs.Intersect(otherLRs, System.StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (overlap != null)
                    {
                        MessageBox.Show($"LR Number '{overlap}' is already used in Challan '{other.ChallanNumber}'.", "Duplicate Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            Result.RecalculateBalance();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void VehicleNumberBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (VehiclePopup == null || VehicleSuggestionList == null || VehicleNumberBox == null) return;
            var text = VehicleNumberBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                VehiclePopup.IsOpen = false;
                return;
            }
            var matches = new VehicleRepository().SearchByVehicleNumber(text);
            _ignoreVehicleSelection = true;
            VehicleSuggestionList.ItemsSource = matches;
            if (matches.Count > 0) VehicleSuggestionList.SelectedIndex = 0;
            _ignoreVehicleSelection = false;
            VehiclePopup.IsOpen = matches.Count > 0;
        }

        private void VehicleSuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ignoreVehicleSelection) return;
            var selected = VehicleSuggestionList?.SelectedItem as VehicleEntry;
            if (selected == null) return;
            ApplyVehicleSelection(selected);
            if (VehiclePopup != null) VehiclePopup.IsOpen = false;
        }

        private void VehicleNumberBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (VehiclePopup == null || VehicleSuggestionList == null || !VehiclePopup.IsOpen || VehicleSuggestionList.Items.Count == 0) return;
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                if (VehicleSuggestionList.SelectedIndex < VehicleSuggestionList.Items.Count - 1)
                {
                    _ignoreVehicleSelection = true;
                    VehicleSuggestionList.SelectedIndex++;
                    _ignoreVehicleSelection = false;
                    VehicleSuggestionList.ScrollIntoView(VehicleSuggestionList.SelectedItem);
                }
            }
            else if (e.Key == Key.Up)
            {
                e.Handled = true;
                if (VehicleSuggestionList.SelectedIndex > 0)
                {
                    _ignoreVehicleSelection = true;
                    VehicleSuggestionList.SelectedIndex--;
                    _ignoreVehicleSelection = false;
                    VehicleSuggestionList.ScrollIntoView(VehicleSuggestionList.SelectedItem);
                }
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                var selected = VehicleSuggestionList.SelectedItem as VehicleEntry;
                if (selected != null) ApplyVehicleSelection(selected);
                VehiclePopup.IsOpen = false;
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                VehiclePopup.IsOpen = false;
            }
        }

        private void VehicleNumberBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var vno = (Result?.VehicleNumber ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(vno)) return;
            var found = new VehicleRepository().FindByVehicleNumber(vno);
            if (found != null) ApplyVehicleSelection(found);
        }

        private void ApplyVehicleSelection(VehicleEntry v)
        {
            if (Result == null || v == null) return;
            Result.VehicleNumber = (v.VehicleNumber ?? string.Empty).Trim().ToUpperInvariant();
            Result.OwnerName = v.OwnerName;
            Result.PAN = v.PANNumber;
            Result.EngineNo = v.EngineNumber;
            Result.ChassisNo = v.ChassisNumber;
            Result.VehicleType = v.VehicleType;
        }

        private void CurrencyTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox tb && !tb.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                tb.Focus();
            }
        }

        private void CurrencyTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            if (IsZeroLike(tb.Text))
            {
                tb.SelectAll();
            }
        }

        private static bool IsZeroLike(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            var cleaned = text.Replace("₹", "").Replace(",", "").Trim();
            if (decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                return v == 0m;
            }
            if (decimal.TryParse(cleaned, out v))
            {
                return v == 0m;
            }
            return false;
        }
    }
}
