using System;
using Awagaman_ERP.Models;
using Awagaman_ERP.ViewModels;
using MahApps.Metro.Controls;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Awagaman_ERP
{
    public partial class LRLedgerWindow : MetroWindow
    {
        public LRLedgerViewModel VM => DataContext as LRLedgerViewModel;

        public LRLedgerWindow()
        {
            InitializeComponent();
            DataContext = new LRLedgerViewModel();

            Loaded += (s, e) =>
            {
                EnforceLockedColumnsReadOnly();
                RefreshFilteredSummary();

                var view = CollectionViewSource.GetDefaultView(LedgerGrid.ItemsSource) as System.Collections.Specialized.INotifyCollectionChanged;
                if (view != null)
                {
                    view.CollectionChanged += (s2, e2) => RefreshFilteredSummary();
                }
            };
        }
        private void EnforceLockedColumnsReadOnly()
        {
            if (LedgerGrid == null) return;
            foreach (var col in LedgerGrid.Columns)
            {
                var h = ((col.Header ?? string.Empty).ToString() ?? string.Empty).Trim();
                if (string.Equals(h, "From", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(h, "To", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(h, "Vehicle No.", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(h, "Type", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(h, "CH No.", StringComparison.OrdinalIgnoreCase))
                {
                    col.IsReadOnly = true;
                }
            }
        }

        private void OpenLRForm_Click(object sender, RoutedEventArgs e)
        {
            var form = new LRFormWindow(existingEntries: VM.Entries);
            form.Owner = this;

            if (form.ShowDialog() == true)
            {
                var entry = form.Result;
                if (entry == null) return;
                try
                {
                    entry.Sr = VM.Entries.Count + 1;
                    VM.Entries.Add(entry);

                    SearchBox.Text = string.Empty;
                    var filter = DataGridExtensions.DataGridFilter.GetFilter(LedgerGrid);
                    if (filter != null) filter.Clear();
                    var cv = CollectionViewSource.GetDefaultView(LedgerGrid.ItemsSource);
                    if (cv != null)
                    {
                        cv.Filter = null;
                        cv.Refresh();
                    }

                    RefreshFilteredSummary();
                    LedgerGrid.SelectedItem = entry;
                    LedgerGrid.ScrollIntoView(entry);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unable to save LR entry: " + ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (VM == null || LedgerGrid == null) return;

            var cv = CollectionViewSource.GetDefaultView(LedgerGrid.ItemsSource);
            if (cv == null) return;

            string filter = SearchBox.Text.ToLower().Trim();
            if (string.IsNullOrEmpty(filter))
            {
                cv.Filter = null;
            }
            else
            {
                cv.Filter = (obj) =>
                {
                    if (obj is LREntry entry)
                    {
                        if (entry.LRNo?.ToLower().Contains(filter) == true) return true;
                        if (entry.ConsignorName?.ToLower().Contains(filter) == true) return true;
                        if (entry.ConsigneeName?.ToLower().Contains(filter) == true) return true;
                        if (entry.VehicleNo?.ToLower().Contains(filter) == true) return true;
                        if (entry.BillNo?.ToLower().Contains(filter) == true) return true;
                        if (entry.CHNo?.ToLower().Contains(filter) == true) return true;
                    }
                    return false;
                };
            }

            RefreshFilteredSummary();
        }

        private void OpenDeliveryChallans_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null) SearchBox.Text = string.Empty;

            if (LedgerGrid != null)
            {
                var filter = DataGridExtensions.DataGridFilter.GetFilter(LedgerGrid);
                if (filter != null) filter.Clear();

                LedgerGrid.UnselectAllCells();
            }
        }

        private void RefreshFilteredSummary()
        {
            if (VM == null || LedgerGrid?.ItemsSource == null) return;

            var view = CollectionViewSource.GetDefaultView(LedgerGrid.ItemsSource);
            if (view == null) return;

            var filteredEntries = view.Cast<object>()
                .OfType<LREntry>()
                .ToList();

            VM.FilteredEntriesCount = filteredEntries.Count;
            VM.FilteredTotalFreight = filteredEntries.Sum(entry => entry.TotalFreight);
            VM.FilteredTotalBalance = filteredEntries.Sum(entry => entry.Bal);

            if (SearchedTotalDueTextBlock != null)
            {
                SearchedTotalDueTextBlock.Visibility = (VM.FilteredEntriesCount < VM.Entries.Count) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void LedgerGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e?.Column == null) return;
            var col = e.Column as DataGridBoundColumn;
            var path = (col?.Binding as Binding)?.Path?.Path ?? string.Empty;
            var header = (e.Column.Header ?? string.Empty).ToString();

            bool locked =
                string.Equals(path, "From", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "To", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "VehicleNo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "VehicleType", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "CHNo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header, "From", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header, "To", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header, "Vehicle No.", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header, "Type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header, "CH No.", StringComparison.OrdinalIgnoreCase);

            if (!locked) return;

            e.Cancel = true;
            MessageBox.Show(
                "This field can only be changed from Challan Ledger.\nPlease edit the related challan to update it.",
                "Locked Field",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void LedgerGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (LedgerGrid.SelectedCells.Count < 2)
            {
                SelectedSumTextBlock.Visibility = Visibility.Collapsed;
                return;
            }

            decimal totalSum = 0;
            bool hasNumbers = false;

            foreach (var cellInfo in LedgerGrid.SelectedCells)
            {
                var item = cellInfo.Item;
                var column = cellInfo.Column as DataGridBoundColumn;

                if (column != null && column.Binding is System.Windows.Data.Binding binding)
                {
                    var propertyName = binding.Path.Path;
                    if (!string.IsNullOrEmpty(propertyName))
                    {
                        var propInfo = item.GetType().GetProperty(propertyName);
                        if (propInfo != null)
                        {
                            var type = propInfo.PropertyType;
                            if (type == typeof(decimal) || type == typeof(decimal?) ||
                                type == typeof(double) || type == typeof(double?) ||
                                type == typeof(int) || type == typeof(int?))
                            {
                                var value = propInfo.GetValue(item);
                                if (value != null)
                                {
                                    if (value is decimal d) { totalSum += d; hasNumbers = true; }
                                    else if (value is double dbl) { totalSum += (decimal)dbl; hasNumbers = true; }
                                    else if (value is int i) { totalSum += i; hasNumbers = true; }
                                }
                            }
                        }
                    }
                }
            }

            if (hasNumbers)
            {
                SelectedSumTextBlock.Text = $"Selected Sum: ₹ {totalSum:N2}";
                SelectedSumTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                SelectedSumTextBlock.Visibility = Visibility.Collapsed;
            }
        }
        
        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null || LedgerGrid == null) return;
            var selectedItems = LedgerGrid.SelectedCells.Select(c => c.Item).OfType<LREntry>().Distinct().ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("Please select at least one entry to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string msg = selectedItems.Count == 1 ? "Are you sure you want to delete this entry?" : $"Are you sure you want to delete these {selectedItems.Count} entries?";
            var result = MessageBox.Show(msg, "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                foreach (var item in selectedItems)
                {
                    VM.Entries.Remove(item);
                }
                RefreshFilteredSummary();
            }
        }
    }
}
