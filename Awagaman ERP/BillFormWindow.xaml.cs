using System;
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
    public partial class BillFormWindow : MetroWindow
    {
        public BillEntry Result { get; private set; }
        public bool WasSaved { get; private set; }
        private readonly bool _isEditMode;
        private bool _ignorePartySelection;

        public BillFormWindow()
        {
            InitializeComponent();
            Result = new BillEntry { BillDate = DateTime.Today, Date = DateTime.Today };
            DataContext = Result;
            _isEditMode = false;
            Loaded += BillFormWindow_Loaded;
        }

        public BillFormWindow(BillEntry existing)
        {
            InitializeComponent();
            Result = new BillEntry
            {
                Id = existing.Id, Sr = existing.Sr,
                BillNo = existing.BillNo, BillDate = existing.BillDate, Party = existing.Party,
                LRNo = existing.LRNo, LRDate = existing.LRDate,
                From = existing.From, To = existing.To, VehicleType = existing.VehicleType,
                Freight = existing.Freight, Detention = existing.Detention, HML = existing.HML,
                OTHR = existing.OTHR, StCharge = existing.StCharge, RCVD = existing.RCVD, TDS = existing.TDS, DED = existing.DED,
                MOP = existing.MOP, MR = existing.MR, Date = existing.Date
            };
            DataContext = Result;
            _isEditMode = true;
            Loaded += BillFormWindow_Loaded;
        }

        private void BillFormWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isEditMode)
                {
                    ApplyCreateModeLayout();
                    if (string.IsNullOrWhiteSpace(Result?.BillNo))
                    {
                        var prefix = BillPrefixSettings.GetPrefix();
                        Result.BillNo = $"{prefix}/";
                    }
                }
                if (_isEditMode) return;
                if (string.IsNullOrWhiteSpace(Result?.LRNo)) return;
                // If values are already set explicitly, preserve them.
                var hasManualValues = Result.Freight != 0 || Result.Detention != 0 || Result.HML != 0 || Result.OTHR != 0 || Result.StCharge != 0 || Result.RCVD != 0 || Result.TDS != 0 || Result.DED != 0;
                if (hasManualValues) return;
                PopulateFromLRNumbers(Result.LRNo);
            }
            catch { }
        }

        private void ApplyCreateModeLayout()
        {
            if (LRDateFieldPanel != null) LRDateFieldPanel.Visibility = Visibility.Collapsed;
            if (FromFieldPanel != null) FromFieldPanel.Visibility = Visibility.Collapsed;
            if (ToFieldPanel != null) ToFieldPanel.Visibility = Visibility.Collapsed;
            if (VehicleTypeFieldPanel != null) VehicleTypeFieldPanel.Visibility = Visibility.Collapsed;
            if (AmountsSectionBorder != null) AmountsSectionBorder.Visibility = Visibility.Collapsed;
        }

        private void NumericTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb) tb.SelectAll();
        }

        private void NumericTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is TextBox tb)) return;
            if (!tb.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                tb.Focus();
            }
        }

        private void BillPartyBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (BillPartyBox == null || BillPartyPopup == null || BillPartySuggestionList == null) return;
            var text = BillPartyBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                BillPartyPopup.IsOpen = false;
                return;
            }

            var matches = new PartyRepository().SearchNames(text);
            _ignorePartySelection = true;
            BillPartySuggestionList.ItemsSource = matches;
            if (matches.Count > 0) BillPartySuggestionList.SelectedIndex = 0;
            _ignorePartySelection = false;
            BillPartyPopup.IsOpen = matches.Count > 0;
        }

        private void BillPartySuggestionList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_ignorePartySelection) return;
            if (BillPartySuggestionList?.SelectedItem is string name)
            {
                if (BillPartyBox != null) BillPartyBox.Text = name;
                if (BillPartyPopup != null) BillPartyPopup.IsOpen = false;
                Result.Party = name;
            }
        }

        private void BillPartyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (BillPartyPopup == null || BillPartySuggestionList == null) return;
            if (!BillPartyPopup.IsOpen || BillPartySuggestionList.Items.Count == 0) return;

            if (e.Key == System.Windows.Input.Key.Down)
            {
                e.Handled = true;
                if (BillPartySuggestionList.SelectedIndex < BillPartySuggestionList.Items.Count - 1)
                {
                    _ignorePartySelection = true;
                    BillPartySuggestionList.SelectedIndex++;
                    _ignorePartySelection = false;
                    BillPartySuggestionList.ScrollIntoView(BillPartySuggestionList.SelectedItem);
                }
            }
            else if (e.Key == System.Windows.Input.Key.Up)
            {
                e.Handled = true;
                if (BillPartySuggestionList.SelectedIndex > 0)
                {
                    _ignorePartySelection = true;
                    BillPartySuggestionList.SelectedIndex--;
                    _ignorePartySelection = false;
                    BillPartySuggestionList.ScrollIntoView(BillPartySuggestionList.SelectedItem);
                }
            }
            else if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                if (BillPartySuggestionList.SelectedItem is string name)
                {
                    if (BillPartyBox != null) BillPartyBox.Text = name;
                    BillPartyPopup.IsOpen = false;
                    Result.Party = name;
                }
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                BillPartyPopup.IsOpen = false;
            }
        }

        private void PopulateFromLRNumbers(string lrNumbers)
        {
            var list = (lrNumbers ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (list.Count == 0) return;

            var matched = new List<LREntry>();
            using (var c = new System.Data.SQLite.SQLiteConnection(Data.AppDatabase.ConnectionString))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    var pNames = new List<string>();
                    for (int i = 0; i < list.Count; i++)
                    {
                        var p = "@p" + i;
                        pNames.Add(p);
                        cmd.Parameters.AddWithValue(p, list[i]);
                    }
                    cmd.CommandText = $@"SELECT LRNo, Date, ConsignorName, BillParty, FromLocation, ToLocation, VehicleType,
                        TotalFreight, Hamali, Detention, Others, StCharge, NEFT, CASH, TDS, Ded
                        FROM LREntries WHERE LRNo IN ({string.Join(",", pNames)});";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            matched.Add(new LREntry
                            {
                                LRNo = r["LRNo"] as string,
                                Date = DateTime.TryParse(r["Date"] as string, out var dt) ? dt : DateTime.Today,
                                ConsignorName = r["ConsignorName"] as string,
                                BillParty = r["BillParty"] as string,
                                From = r["FromLocation"] as string,
                                To = r["ToLocation"] as string,
                                VehicleType = r["VehicleType"] as string,
                                TotalFreight = Convert.ToDecimal(r["TotalFreight"]),
                                Hamali = Convert.ToDecimal(r["Hamali"]),
                                Detention = Convert.ToDecimal(r["Detention"]),
                                Others = Convert.ToDecimal(r["Others"]),
                                StCharge = Convert.ToDecimal(r["StCharge"]),
                                NEFT = Convert.ToDecimal(r["NEFT"]),
                                CASH = Convert.ToDecimal(r["CASH"]),
                                TDS = Convert.ToDecimal(r["TDS"]),
                                Ded = Convert.ToDecimal(r["Ded"])
                            });
                        }
                    }
                }
            }

            if (matched.Count == 0) return;
            var first = matched[0];
            Result.Party = GetPreferredBillParty(first);
            if (!Result.LRDate.HasValue) Result.LRDate = first.Date;
            if (string.IsNullOrWhiteSpace(Result.From)) Result.From = first.From;
            if (string.IsNullOrWhiteSpace(Result.To)) Result.To = first.To;
            if (string.IsNullOrWhiteSpace(Result.VehicleType)) Result.VehicleType = first.VehicleType;

            Result.Freight = matched.Sum(lr => lr.TotalFreight);
            Result.HML = matched.Sum(lr => lr.Hamali);
            Result.Detention = matched.Sum(lr => lr.Detention);
            Result.OTHR = matched.Sum(lr => lr.Others);
            Result.StCharge = matched.Sum(lr => lr.StCharge);
            Result.RCVD = matched.Sum(lr => lr.NEFT + lr.CASH);
            Result.TDS = matched.Sum(lr => lr.TDS);
            Result.DED = matched.Sum(lr => lr.Ded);
        }

        private void SelectLRs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var existingParty = (Result.Party ?? string.Empty).Trim();
                var hasSelectedLrs = (Result.LRNo ?? string.Empty)
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Any(s => s.Length > 0);
                // If bill party is selected, always filter by that party.
                // If party is empty, allow all when no LR is selected yet.
                var filterParty = !string.IsNullOrWhiteSpace(existingParty)
                    ? existingParty
                    : (hasSelectedLrs ? existingParty : string.Empty);
                var unbilled = new List<LREntry>();
                using (var c = new System.Data.SQLite.SQLiteConnection(Data.AppDatabase.ConnectionString))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = @"SELECT lr.Id, lr.LRNo, lr.BillNo, lr.ConsignorName, lr.BillParty, lr.FromLocation, lr.ToLocation, lr.VehicleNo, lr.VehicleType, lr.Date,
                            lr.TotalFreight, lr.Hamali, lr.Detention, lr.Others, lr.StCharge, lr.NEFT, lr.CASH, lr.TDS, lr.Ded
                            FROM LREntries lr
                            WHERE (lr.BillNo IS NULL OR lr.BillNo = '')
                            ORDER BY lr.LRNo;";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var consignorName = (r["ConsignorName"] as string) ?? string.Empty;
                                var billParty = (r["BillParty"] as string) ?? string.Empty;
                                var effectiveParty = string.IsNullOrWhiteSpace(billParty) ? consignorName : billParty;
                                if (!string.IsNullOrWhiteSpace(filterParty) && !IsSameParty(effectiveParty, filterParty))
                                    continue;

                                unbilled.Add(new LREntry
                                {
                                    Id = Convert.ToInt32(r["Id"]),
                                    LRNo = r["LRNo"] as string,
                                    Date = DateTime.TryParse(r["Date"] as string, out var dt) ? dt : DateTime.Today,
                                    From = r["FromLocation"] as string,
                                    To = r["ToLocation"] as string,
                                    VehicleType = r["VehicleType"] as string,
                                    ConsignorName = consignorName,
                                    BillParty = billParty,
                                    TotalFreight = Convert.ToDecimal(r["TotalFreight"]),
                                    Hamali = Convert.ToDecimal(r["Hamali"]),
                                    Detention = Convert.ToDecimal(r["Detention"]),
                                    Others = Convert.ToDecimal(r["Others"]),
                                    StCharge = Convert.ToDecimal(r["StCharge"]),
                                    NEFT = Convert.ToDecimal(r["NEFT"]),
                                    CASH = Convert.ToDecimal(r["CASH"]),
                                    TDS = Convert.ToDecimal(r["TDS"]),
                                    Ded = Convert.ToDecimal(r["Ded"]),
                                    BillNo = r["BillNo"] as string,
                                });
                            }
                        }
                    }
                }

                if (unbilled.Count == 0)
                {
                    var msg = string.IsNullOrWhiteSpace(filterParty)
                        ? "No unbilled LR numbers available."
                        : $"No unbilled LR numbers available for party '{filterParty}'.";
                    MessageBox.Show(msg, "No Matching LR", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new Window
                {
                    Title = string.IsNullOrWhiteSpace(filterParty) ? "Select LR Numbers" : $"Select LR Numbers - {filterParty}",
                    Width = 500,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Background = System.Windows.Media.Brushes.White
                };

                var grid = new System.Windows.Controls.DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    IsReadOnly = true,
                    SelectionMode = System.Windows.Controls.DataGridSelectionMode.Extended,
                    Margin = new Thickness(10),
                    ItemsSource = unbilled
                };
                grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "LR No", Binding = new System.Windows.Data.Binding("LRNo"), Width = 100 });
                grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "Date", Binding = new System.Windows.Data.Binding("Date") { StringFormat = "dd-MMM-yyyy" }, Width = 90 });
                grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "From", Binding = new System.Windows.Data.Binding("From"), Width = 80 });
                grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "To", Binding = new System.Windows.Data.Binding("To"), Width = 80 });
                grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "Party", Binding = new System.Windows.Data.Binding("BillParty"), Width = 120 });

                var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(10) };
                var okBtn = new System.Windows.Controls.Button { Content = "OK", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
                var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Width = 80, Height = 30 };
                btnPanel.Children.Add(okBtn);
                btnPanel.Children.Add(cancelBtn);

                var root = new System.Windows.Controls.Grid();
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                var headerText = string.IsNullOrWhiteSpace(filterParty)
                    ? "Select LR numbers (use Ctrl+Click for multiple)"
                    : $"Select LR numbers for party '{filterParty}' (use Ctrl+Click for multiple)";
                var header = new System.Windows.Controls.TextBlock { Text = headerText, FontSize = 13, Margin = new Thickness(10, 10, 10, 0) };
                System.Windows.Controls.Grid.SetRow(header, 0);
                System.Windows.Controls.Grid.SetRow(grid, 1);
                System.Windows.Controls.Grid.SetRow(btnPanel, 2);
                root.Children.Add(header);
                root.Children.Add(grid);
                root.Children.Add(btnPanel);
                dialog.Content = root;
                dialog.DataContext = unbilled;

                okBtn.Click += (_, __) =>
                {
                    if (grid.SelectedItems.Count == 0)
                    {
                        MessageBox.Show("Select at least one LR number.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var items = grid.SelectedItems.Cast<LREntry>().ToList();
                    var selectedParty = GetPreferredBillParty(items[0]);
                    if (string.IsNullOrWhiteSpace(selectedParty))
                    {
                        MessageBox.Show("Selected LR number does not have a party name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (items.Any(lr => !IsSameParty(GetPreferredBillParty(lr), selectedParty)))
                    {
                        MessageBox.Show("Only LR numbers with the same party can be selected.", "Party Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(filterParty) && !IsSameParty(filterParty, selectedParty))
                    {
                        MessageBox.Show($"Selected LR numbers belong to '{selectedParty}', but the bill party is '{filterParty}'.", "Party Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var selected = items.Select(lr => lr.LRNo).Where(n => !string.IsNullOrEmpty(n));
                    var existing = (Result.LRNo ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
                    var newlyAdded = items.Where(lr => !string.IsNullOrWhiteSpace(lr.LRNo) && !existingSet.Contains(lr.LRNo)).ToList();
                    var combined = existing.Concat(selected).Distinct(StringComparer.OrdinalIgnoreCase);
                    Result.LRNo = string.Join(", ", combined);
                    Result.Party = selectedParty;
                    if (!Result.LRDate.HasValue) Result.LRDate = items[0].Date;
                    if (string.IsNullOrWhiteSpace(Result.From)) Result.From = items[0].From;
                    if (string.IsNullOrWhiteSpace(Result.To)) Result.To = items[0].To;
                    if (string.IsNullOrWhiteSpace(Result.VehicleType)) Result.VehicleType = items[0].VehicleType;

                    // Fill bill amounts from newly selected LR rows.
                    var source = newlyAdded.Count > 0 ? newlyAdded : items;
                    Result.Freight += source.Sum(lr => lr.TotalFreight);
                    Result.HML += source.Sum(lr => lr.Hamali);
                    Result.Detention += source.Sum(lr => lr.Detention);
                    Result.OTHR += source.Sum(lr => lr.Others);
                    Result.StCharge += source.Sum(lr => lr.StCharge);
                    Result.RCVD += source.Sum(lr => lr.NEFT + lr.CASH);
                    Result.TDS += source.Sum(lr => lr.TDS);
                    Result.DED += source.Sum(lr => lr.Ded);
                    dialog.DialogResult = true;
                    dialog.Close();
                };
                cancelBtn.Click += (_, __) => { dialog.DialogResult = false; dialog.Close(); };

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool IsSameParty(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPreferredBillParty(LREntry lr)
        {
            if (lr == null) return string.Empty;
            var billParty = (lr.BillParty ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(billParty)) return billParty;
            return (lr.ConsignorName ?? string.Empty).Trim();
        }

        private void LRNoBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isEditMode) return;
                if (string.IsNullOrWhiteSpace(Result?.LRNo)) return;
                PopulateFromLRNumbers(Result.LRNo);
            }
            catch { }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Result.BillNo))
            {
                MessageBox.Show("Bill Number cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(Result.Party))
            {
                MessageBox.Show("Party Name is mandatory.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var hasAnyLr = (Result.LRNo ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Any(x => x.Length > 0);
            if (!hasAnyLr)
            {
                MessageBox.Show("LR Number is mandatory.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            WasSaved = true;
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
            {
                DialogResult = true;
            }
            Close();
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

        private void BillNoBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                var raw = (Result?.BillNo ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(raw)) return;

                // If user enters only a number, apply default prefix automatically.
                if (int.TryParse(raw, out var onlyNumber))
                {
                    var prefix = BillPrefixSettings.GetPrefix();
                    Result.BillNo = $"{prefix}/{onlyNumber}";
                    return;
                }

                // If user enters custom prefix only, append next number for that prefix.
                if (!raw.Contains("/"))
                {
                    var next = GetNextBillSequence(raw);
                    Result.BillNo = $"{raw}/{next}";
                }
            }
            catch { }
        }

        private static int GetNextBillSequence(string prefix)
        {
            var pfx = (prefix ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(pfx)) pfx = BillPrefixSettings.GetPrefix();

            var max = 0;
            using (var c = new System.Data.SQLite.SQLiteConnection(Data.AppDatabase.ConnectionString))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT BillNo FROM Bills WHERE BillNo LIKE @pfx";
                    cmd.Parameters.AddWithValue("@pfx", pfx + "/%");
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var billNo = (r["BillNo"] as string) ?? string.Empty;
                            var idx = billNo.LastIndexOf('/');
                            if (idx < 0 || idx == billNo.Length - 1) continue;
                            if (int.TryParse(billNo.Substring(idx + 1).Trim(), out var n) && n > max)
                            {
                                max = n;
                            }
                        }
                    }
                }
            }

            return max + 1;
        }
    }
}
