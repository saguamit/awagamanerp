using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Awagaman_ERP.ViewModels;
using Awagaman_ERP.Models;
using Awagaman_ERP.Data;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Text;
using MahApps.Metro.Controls;
using System.Reflection;
using System.Windows.Media;

namespace Awagaman_ERP
{
    public partial class MainWindow : MetroWindow
    {
        public ChallanViewModel VM => DataContext as ChallanViewModel;
        public LRLedgerViewModel LRVM { get; private set; }
        public TrackingViewModel TrackingVM { get; private set; }
        public ViewModels.BillLedgerViewModel BillVM { get; private set; }
            private ContextMenu _columnsMenu;
        private System.Windows.Threading.DispatcherTimer _searchTimer;
        private System.Windows.Threading.DispatcherTimer _challanFilterTimer;
        private TextBox _activeSearchBox;
        private ContextMenu _lrColumnsMenu;
        private bool _onlyDueFilterEnabled;
        private readonly Dictionary<string, HashSet<string>> _challanHeaderFilters = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private Style _challanFilteredHeaderStyle;
        private bool _challanHeaderDragSelecting;
        private int _challanDragStartDisplayIndex = -1;
        private bool _challanDragAppendMode;
        private bool _lrHeaderDragSelecting;
        private int _lrDragStartDisplayIndex = -1;
        private bool _lrDragAppendMode;
        private bool _billHeaderDragSelecting;
        private int _billDragStartDisplayIndex = -1;
        private bool _billDragAppendMode;
        private readonly Dictionary<string, HashSet<string>> _lrHeaderFilters = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private Style _lrFilteredHeaderStyle;
        private readonly Dictionary<string, HashSet<string>> _billHeaderFilters = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private Style _billFilteredHeaderStyle;
        private DateTime _lastChallanBillingSyncUtc = DateTime.MinValue;
        private bool _challanBillingSyncInProgress;
        private bool _initialLrBackfillDone;
        private Window _billRdlcPreviewWindow;
        private System.Windows.Forms.Integration.WindowsFormsHost _billRdlcHost;
        private Microsoft.Reporting.WinForms.ReportViewer _billRdlcViewer;
        private string _billRdlcLastKey;
        private long _billRdlcLastTemplateStamp;
        private readonly Dictionary<string, System.Data.DataTable> _billLinesCache = new Dictionary<string, System.Data.DataTable>(StringComparer.Ordinal);
        private readonly CBSAccountRepository _cbsAccountRepo = new CBSAccountRepository();
        private readonly CashBankStatementRepository _cbsRepo = new CashBankStatementRepository();
        private List<CBSAccountEntry> _allCbsAccounts = new List<CBSAccountEntry>();
        private List<CashBankStatementEntry> _allCbsEntries = new List<CashBankStatementEntry>();
        public ObservableCollection<string> CBSAccountNames { get; } = new ObservableCollection<string>();
        private readonly Dictionary<string, HashSet<string>> _cbsHeaderFilters = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private Style _cbsFilteredHeaderStyle;
        private string _cbsSortColumn = string.Empty;
        private bool _cbsSortAscending = true;
        private bool _cbsHeaderDragSelecting;
        private int _cbsDragStartDisplayIndex = -1;
        private bool _cbsDragAppendMode;
        private const string CbsSearchPlaceholder = "Search CBS...";
        private bool _cbsGridResizeHooksAttached;
        private readonly HashSet<DataGridColumn> _cbsWidthHookedColumns = new HashSet<DataGridColumn>();
        public MainWindow()
        {
            InitializeComponent();
            try
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                if (AppVersionText != null && ver != null)
                {
                    AppVersionText.Text = $"v {ver.Major}.{ver.Minor}.{ver.Build}";
                }
            }
            catch { }
            DataContext = new ChallanViewModel();
            LRVM = new LRLedgerViewModel();
            TrackingVM = new TrackingViewModel();
            BillVM = new ViewModels.BillLedgerViewModel();

            // Debounce timer for search boxes
            _searchTimer = new System.Windows.Threading.DispatcherTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(300);
            _searchTimer.Tick += SearchTimer_Tick;

            // Debounce timer for Challan filter fields
            _challanFilterTimer = new System.Windows.Threading.DispatcherTimer();
            _challanFilterTimer.Interval = TimeSpan.FromMilliseconds(300);
            _challanFilterTimer.Tick += ChallanFilterTimer_Tick;

            if (VM != null) VM.PropertyChanged += VM_PropertyChanged;

            this.Closing += (s, e) => SaveGridSettings();

            Loaded += (s, e) =>
            {
                if (PageTitle != null) PageTitle.Text = "Dashboard";
                LoadGridSettings();
                if (!_initialLrBackfillDone)
                {
                    BackfillAllLinkedLREntriesFromChallans();
                    _initialLrBackfillDone = true;
                }
                SyncAllChallanBillingFromLR();
                RefreshDashboard();
                UpdateColumnVisibility();
                RestoreSortIndicator();
                RefreshFilteredSummary();
                RefreshCBSAccounts();
                RefreshCBSGrid();

                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(LedgerGrid.ItemsSource) as System.Collections.Specialized.INotifyCollectionChanged;
                if (view != null) view.CollectionChanged += (s2, e2) => { RefreshFilteredSummary(); RefreshDashboard(); };

                if (LRLedgerGrid != null)
                {
                    if (LRLedgerView.DataContext == null) LRLedgerView.DataContext = LRVM;
                    EnforceLRLockedColumnsReadOnly();
                    LRRefreshFilteredSummary();
                    var lrView = System.Windows.Data.CollectionViewSource.GetDefaultView(LRLedgerGrid.ItemsSource) as System.Collections.Specialized.INotifyCollectionChanged;
                    if (lrView != null) lrView.CollectionChanged += (s2, e2) => LRRefreshFilteredSummary();
                }

                if (TrackingLedgerGrid != null)
                {
                    var trackingView = CollectionViewSource.GetDefaultView(TrackingLedgerGrid.ItemsSource) as System.Collections.Specialized.INotifyCollectionChanged;
                    if (trackingView != null) trackingView.CollectionChanged += (s2, e2) => RefreshDashboard();
                }
            };
        }

        private void VM_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && e.PropertyName.StartsWith("Show"))
            {
                UpdateColumnVisibility();
                VM?.SaveColumnSettings();
            }
        }

        private void RefreshDashboard()
        {
            // Query database directly for dashboard stats (Entries collection may be empty)
            string connStr = Awagaman_ERP.Data.AppDatabase.ConnectionString;
            long totalChallans = 0, dueChallans = 0, newBookings = 0, inTransit = 0, delivered = 0;
            decimal totalDue = 0;

            using (var conn = new System.Data.SQLite.SQLiteConnection(connStr))
            {
                conn.Open();

                // Total Challans and Due
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(LorryHire - LessTDS + Detention + Hamali - Deduction - AdvanceAmount - BalancePaidNEFT - BalancePaidCash), 0) FROM Challans;";
                    using (var r = cmd.ExecuteReader()) { if (r.Read()) { totalChallans = (long)r[0]; totalDue = Convert.ToDecimal(r[1]); } }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM Challans WHERE (LorryHire - LessTDS + Detention + Hamali - Deduction - AdvanceAmount - BalancePaidNEFT - BalancePaidCash) > 0;";
                    dueChallans = Convert.ToInt64(cmd.ExecuteScalar());
                }

                // New Bookings count is derived below using combined conditions.

                // Tracking stats
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM TrackingEntries WHERE DispatchDate IS NOT NULL AND DeliveredDate IS NULL;";
                    inTransit = (long)cmd.ExecuteScalar();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM TrackingEntries WHERE DeliveredDate IS NOT NULL;";
                    delivered = (long)cmd.ExecuteScalar();
                }
            }

            // Bill total outstanding
            decimal billTotalDue = 0;
            int pendingBillCount = 0;
            using (var conn = new System.Data.SQLite.SQLiteConnection(connStr))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COALESCE(SUM(Freight + Detention + HML + OTHR + StCharge - RCVD - TDS - DED), 0) FROM Bills;";
                    billTotalDue = Convert.ToDecimal(cmd.ExecuteScalar());
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM LREntries WHERE (BillNo IS NULL OR BillNo = '');";
                    pendingBillCount = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }

            if (DashboardTotalDue != null) DashboardTotalDue.Text = $"₹ {totalDue:N2}";
            if (DashboardTotalChallans != null) DashboardTotalChallans.Text = $"{dueChallans} Due Challans";
            if (DashboardOutstanding != null) DashboardOutstanding.Text = $"₹ {billTotalDue:N2}";
            if (DashboardPendingBill != null) DashboardPendingBill.Text = pendingBillCount.ToString();

            // New Bookings grid
            {
                var allLrNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var allChallans = new List<ChallanEntry>();
                using (var conn = new System.Data.SQLite.SQLiteConnection(connStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT LRNo FROM LREntries WHERE LRNo IS NOT NULL AND TRIM(LRNo) <> '';";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var lrNo = (r["LRNo"] as string ?? string.Empty).Trim();
                                if (!string.IsNullOrWhiteSpace(lrNo)) allLrNos.Add(lrNo);
                            }
                        }
                    }

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Id, ChallanNumber, LRNumber, FromLocation, ToLocation, VehicleNumber, BrokerName, LorryHire FROM Challans ORDER BY Date DESC;";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                allChallans.Add(new ChallanEntry
                                {
                                    Id = Convert.ToInt32(r["Id"]),
                                    ChallanNumber = r["ChallanNumber"] as string,
                                    LRNumber = r["LRNumber"] as string,
                                    From = r["FromLocation"] as string,
                                    To = r["ToLocation"] as string,
                                    VehicleNumber = r["VehicleNumber"] as string,
                                    BrokerName = r["BrokerName"] as string,
                                    LorryHire = Convert.ToDecimal(r["LorryHire"]),
                                });
                            }
                        }
                    }
                }

                List<string> GetMissingLrNos(string raw)
                {
                    raw = (raw ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

                    var lrParts = raw.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(x => x.Trim())
                                     .Where(x => !string.IsNullOrWhiteSpace(x))
                                     .Distinct(StringComparer.OrdinalIgnoreCase)
                                     .ToList();
                    if (lrParts.Count == 0) return new List<string>();

                    return lrParts.Where(lr => !allLrNos.Contains(lr)).ToList();
                }

                var expandedPending = new List<ChallanEntry>();
                foreach (var ch in allChallans)
                {
                    var raw = (ch.LRNumber ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        expandedPending.Add(ch);
                        continue;
                    }

                    var missing = GetMissingLrNos(raw);
                    if (missing.Count == 0) continue;
                    foreach (var miss in missing)
                    {
                        expandedPending.Add(new ChallanEntry
                        {
                            Id = ch.Id,
                            ChallanNumber = ch.ChallanNumber,
                            LRNumber = miss,
                            From = ch.From,
                            To = ch.To,
                            VehicleNumber = ch.VehicleNumber,
                            BrokerName = ch.BrokerName
                            ,
                            LorryHire = ch.LorryHire
                        });
                    }
                }

                var newBookingItems = expandedPending.Take(50).ToList();
                newBookings = newBookingItems.Count;
                if (DashboardNewBookings != null) DashboardNewBookings.Text = newBookings.ToString();
                if (NewBookingGrid != null) NewBookingGrid.ItemsSource = newBookingItems;
            }

            // Pending Bill list
            if (DashboardPendingBillGrid != null)
            {
                var itemsList = new List<LREntry>();
                using (var conn = new System.Data.SQLite.SQLiteConnection(connStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"SELECT lr.Id, lr.LRNo, lr.ConsignorName, lr.BillParty, lr.FromLocation, lr.ToLocation, lr.VehicleNo
                            FROM LREntries lr
                            WHERE (lr.BillNo IS NULL OR lr.BillNo = '')
                            ORDER BY lr.Id DESC LIMIT 30;";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                                itemsList.Add(new LREntry
                                {
                                    Id = Convert.ToInt32(r["Id"]),
                                    LRNo = r["LRNo"] as string,
                                    BillParty = r["BillParty"] as string,
                                    ConsignorName = string.IsNullOrWhiteSpace(r["BillParty"] as string) ? (r["ConsignorName"] as string) : (r["BillParty"] as string),
                                    From = r["FromLocation"] as string,
                                    To = r["ToLocation"] as string,
                                    VehicleNo = r["VehicleNo"] as string,
                                });
                        }
                    }
                }
                DashboardPendingBillGrid.ItemsSource = itemsList;
            }
        }

        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid == null) return;

            if ((grid == LedgerGrid || grid == LRLedgerGrid || grid == BillLedgerGrid || grid == CBSGrid) &&
                Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                CopySelectedRangeFromGrid(grid);
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Left && e.Key != Key.Right && e.Key != Key.Up && e.Key != Key.Down && e.Key != Key.Enter && e.Key != Key.Tab) return;

            // While typing/editing in a TextBox, keep normal caret behavior for Left/Right keys.
            if ((e.Key == Key.Left || e.Key == Key.Right) && grid.IsKeyboardFocusWithin)
            {
                var tb = e.OriginalSource as TextBox;
                if (tb != null && !tb.IsReadOnly)
                {
                    return;
                }
            }

            // For Tab, let standard handling work but commit first.
            if (e.Key == Key.Tab)
            {
                if (!TryCommitGridEdits(grid)) e.Handled = true;
                return;
            }

            if (grid.CurrentCell.Column == null) return;
            var colIdx = grid.Columns.IndexOf(grid.CurrentCell.Column);
            var rowIdx = grid.Items.IndexOf(grid.CurrentCell.Item);
            if (colIdx < 0 || rowIdx < 0) return;

            int newCol = colIdx, newRow = rowIdx;
            if (e.Key == Key.Left && colIdx > 0) newCol = colIdx - 1;
            else if (e.Key == Key.Right && colIdx < grid.Columns.Count - 1) newCol = colIdx + 1;
            else if (e.Key == Key.Up && rowIdx > 0) newRow = rowIdx - 1;
            else if (e.Key == Key.Down && rowIdx < grid.Items.Count - 1) newRow = rowIdx + 1;
            else if (e.Key == Key.Enter && rowIdx < grid.Items.Count - 1) newRow = rowIdx + 1;
            else if (e.Key == Key.Enter && rowIdx >= grid.Items.Count - 1)
            {
                TryCommitGridEdits(grid);
                return;
            }
            else return;

            e.Handled = true;
            if (!TryCommitGridEdits(grid))
            {
                // Keep focus on current cell when commit fails (e.g., partially typed numeric value)
                return;
            }
            var newItem = grid.Items[newRow];
            grid.CurrentCell = new DataGridCellInfo(newItem, grid.Columns[newCol]);
            grid.BeginEdit();
        }

        private static bool TryCommitGridEdits(DataGrid grid)
        {
            try
            {
                if (grid == null) return false;
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetColumnBindingPath(DataGridColumn col)
        {
            if (col is DataGridTextColumn tc && tc.Binding is Binding tb) return tb.Path?.Path;
            if (col is DataGridBoundColumn bc && bc.Binding is Binding bb) return bb.Path?.Path;
            if (col is DataGridTemplateColumn tcol) return tcol.SortMemberPath;
            return null;
        }

        private static string GetCellDisplayText(object item, DataGridColumn col)
        {
            if (item == null || col == null) return string.Empty;
            var path = GetColumnBindingPath(col);
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var prop = item.GetType().GetProperty(path);
            if (prop == null) return string.Empty;
            var val = prop.GetValue(item);
            if (val == null) return string.Empty;
            if (val is DateTime dt) return dt.ToString("dd-MMM-yyyy");
            var ndt = val as DateTime?;
            if (ndt.HasValue) return ndt.Value.ToString("dd-MMM-yyyy");
            if (val is decimal dec) return dec.ToString("N2");
            if (val is double d) return d.ToString("N2");
            return Convert.ToString(val) ?? string.Empty;
        }

        private void CopySelectedRangeFromGrid(DataGrid grid)
        {
            if (grid == null || grid.SelectedCells == null || grid.SelectedCells.Count == 0) return;

            var cells = grid.SelectedCells
                .Where(c => c.Item != CollectionView.NewItemPlaceholder && c.Column != null)
                .ToList();
            if (cells.Count == 0) return;

            var rowOrder = grid.Items.Cast<object>()
                .Where(i => i != CollectionView.NewItemPlaceholder)
                .Select((item, idx) => new { item, idx })
                .ToDictionary(x => x.item, x => x.idx);

            var selectedColumns = cells
                .Select(c => c.Column)
                .Distinct()
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            var selectedRows = cells
                .Select(c => c.Item)
                .Distinct()
                .Where(r => rowOrder.ContainsKey(r))
                .OrderBy(r => rowOrder[r])
                .ToList();

            var rowCellMap = cells
                .GroupBy(c => c.Item)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(x => x.Column, x => x, EqualityComparer<DataGridColumn>.Default)
                );

            var sb = new StringBuilder();
            for (int r = 0; r < selectedRows.Count; r++)
            {
                var rowItem = selectedRows[r];
                var rowValues = new List<string>(selectedColumns.Count);

                foreach (var col in selectedColumns)
                {
                    if (rowCellMap.TryGetValue(rowItem, out var colMap) && colMap.TryGetValue(col, out var cellInfo))
                        rowValues.Add(GetCellDisplayText(cellInfo.Item, col));
                    else
                        rowValues.Add(string.Empty);
                }

                sb.Append(string.Join("\t", rowValues));
                if (r < selectedRows.Count - 1) sb.AppendLine();
            }

            if (sb.Length > 0) Clipboard.SetText(sb.ToString());
        }

        private void CopyCurrentCellFromGrid(DataGrid grid)
        {
            if (grid == null) return;

            if (grid.SelectedCells != null && grid.SelectedCells.Count > 1)
            {
                CopySelectedRangeFromGrid(grid);
                return;
            }

            if (grid.CurrentCell.Column == null || grid.CurrentCell.Item == null) return;
            var text = GetCellDisplayText(grid.CurrentCell.Item, grid.CurrentCell.Column);
            if (!string.IsNullOrWhiteSpace(text)) Clipboard.SetText(text);
        }

        private void CopySelectedRowsFromGrid(DataGrid grid)
        {
            if (grid == null || grid.SelectedItems == null || grid.SelectedItems.Count == 0) return;
            var rows = grid.SelectedItems.Cast<object>().Where(x => x != CollectionView.NewItemPlaceholder).ToList();
            if (rows.Count == 0) return;

            var cols = grid.Columns.OrderBy(c => c.DisplayIndex).Where(c => c.Visibility == Visibility.Visible).ToList();
            var sb = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                var rowVals = cols.Select(c => GetCellDisplayText(rows[i], c));
                sb.Append(string.Join("\t", rowVals));
                if (i < rows.Count - 1) sb.AppendLine();
            }
            if (sb.Length > 0) Clipboard.SetText(sb.ToString());
        }

        private void CopyColumnFromGrid(DataGrid grid, DataGridColumn column)
        {
            if (grid == null || column == null) return;

             // If multiple columns are selected (via header drag/Ctrl+click), copy the full selected block.
            var selectedCols = grid.SelectedCells
                .Where(c => c.Column != null && c.Item != CollectionView.NewItemPlaceholder)
                .Select(c => c.Column)
                .Distinct()
                .ToList();
            if (selectedCols.Count > 1)
            {
                CopySelectedRangeFromGrid(grid);
                return;
            }

            var items = grid.Items.Cast<object>().Where(i => i != CollectionView.NewItemPlaceholder).ToList();
            if (items.Count == 0) return;

            var sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(GetCellDisplayText(items[i], column));
                if (i < items.Count - 1) sb.AppendLine();
            }
            if (sb.Length > 0) Clipboard.SetText(sb.ToString());
        }

        private void NewBookingGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            var entry = e.Row.Item as ChallanEntry;
            if (entry == null || entry.Id <= 0) return;
            try
            {
                if (e.EditingElement is TextBox textBox)
                {
                    var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                    binding?.UpdateSource();
                }
                VM?.GetRepository().Upsert(entry);
                SyncAllChallanBillingFromLR();
            }
            catch { }
        }

        private void DashboardMakeBill_Click(object sender, RoutedEventArgs e)
        {
            var lr = (sender as System.Windows.Controls.Button)?.Tag as LREntry;
            if (lr == null) return;
            if (!string.IsNullOrEmpty(lr.BillNo)) { MessageBox.Show($"LR '{lr.LRNo}' already has Bill No. '{lr.BillNo}'.", "Already Billed", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var form = new BillFormWindow();
            form.Result.Party = string.IsNullOrWhiteSpace(lr.BillParty) ? lr.ConsignorName : lr.BillParty;
            form.Result.LRNo = lr.LRNo;
            form.Result.LRDate = lr.Date;
            form.Result.From = lr.From;
            form.Result.To = lr.To;
            form.Result.VehicleType = lr.VehicleType;
            form.Result.Freight = lr.TotalFreight;
            form.Result.HML = lr.Hamali;
            form.Result.Detention = lr.Detention;
            form.Result.OTHR = lr.Others;
            form.Result.StCharge = lr.StCharge;
            form.Result.RCVD = lr.NEFT + lr.CASH;
            form.Result.TDS = lr.TDS;
            form.Result.DED = lr.Ded;
            form.DataContext = form.Result;
            form.Owner = this;
            form.Closed += (_, __) =>
            {
                if (!form.WasSaved) return;
                var entry = form.Result;
                try
                {
                    SaveBillRowsFromFormEntry(entry);
                    UpdateLRBillNo(lr.Id, entry.BillNo);
                    BillVM.RefreshAfterDelete();
                    LRVM.RefreshAfterDelete();
                    BillUpdatePageUI();
                    LRUpdatePageUI();
                    RefreshDashboard();
                }
                catch (Exception ex) { MessageBox.Show("Save error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            form.Show();
        }

        private void DashboardDueCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenDeliveryChallans_Click(sender, new RoutedEventArgs());
            _onlyDueFilterEnabled = true;
            if (OnlyDueButton != null)
            {
                OnlyDueButton.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2E7D32"));
            }
            ApplyChallanDueFilter();
        }

        private void LRDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (LRLedgerGrid == null) return;
            var selected = LRLedgerGrid.SelectedItems.Cast<LREntry>().ToList();
            if (selected.Count == 0) { MessageBox.Show("Select LR entries to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (MessageBox.Show($"Delete {selected.Count} LR entr{(selected.Count == 1 ? "y" : "ies")}?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (var item in selected)
            {
                using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM LREntries WHERE Id = @id"; cmd.Parameters.AddWithValue("@id", item.Id); cmd.ExecuteNonQuery(); }
                }
            }
            LRVM.RefreshAfterDelete();
            SyncAllChallanBillingFromLR();
            LRRefreshFilteredSummary();
            RefreshDashboard();
        }

        private void OpenBillLedger_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DashboardView.Visibility = Visibility.Collapsed;
                DeliveryChallanView.Visibility = Visibility.Collapsed;
                LRLedgerView.Visibility = Visibility.Collapsed;
                TrackingLedgerView.Visibility = Visibility.Collapsed;
                PartyLedgerView.Visibility = Visibility.Collapsed;
                VehicleLedgerView.Visibility = Visibility.Collapsed;
                CBSLedgerView.Visibility = Visibility.Collapsed;

                TabBillLedger.Style = (Style)FindResource("ActiveTabButtonStyle");
                TabDashboard.Style = (Style)FindResource("TabButtonStyle");
                TabDeliveryChallans.Style = (Style)FindResource("TabButtonStyle");
                TabLRLedger.Style = (Style)FindResource("TabButtonStyle");
                TabTrackingLedger.Style = (Style)FindResource("TabButtonStyle");
                TabPartyLedger.Style = (Style)FindResource("TabButtonStyle");
                TabVehicleLedger.Style = (Style)FindResource("TabButtonStyle");
                TabCBSLedger.Style = (Style)FindResource("TabButtonStyle");
                if (PageTitle != null) PageTitle.Text = "Bill Ledger";

                BillVM.EnsurePageLoaded();
                BillLedgerView.DataContext = BillVM;
                BillLedgerView.Visibility = Visibility.Visible;
                BillUpdatePageUI();
            }
            catch (Exception ex) { MessageBox.Show("Error: " + ex.Message, "Bill Ledger Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OpenBillForm_Click(object sender, RoutedEventArgs e)
        {
            var form = new BillFormWindow();
            form.Owner = this;
            form.Closed += (_, __) =>
            {
                if (!form.WasSaved) return;
                var entry = form.Result;
                try
                {
                    SaveBillRowsFromFormEntry(entry);
                    BillVM.RefreshAfterDelete();
                    BillUpdatePageUI();
                }
                catch (Exception ex) { MessageBox.Show("Save error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            form.Show();
        }

        private void OpenBillPrefixSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Window
                {
                    Title = "Bill Prefix Settings",
                    Width = 420,
                    Height = 190,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    Background = System.Windows.Media.Brushes.White
                };

                var root = new Grid { Margin = new Thickness(14) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = "Default Bill Prefix",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var box = new TextBox
                {
                    Height = 28,
                    Text = BillPrefixSettings.GetPrefix(),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var hint = new TextBlock
                {
                    Text = "Example: FBD 26-27 (bill no will be Prefix/NextNumber)",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 12
                };

                var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var saveBtn = new Button { Content = "Save", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
                var cancelBtn = new Button { Content = "Cancel", Width = 90, Height = 30 };
                footer.Children.Add(saveBtn);
                footer.Children.Add(cancelBtn);

                Grid.SetRow(label, 0);
                Grid.SetRow(box, 1);
                Grid.SetRow(hint, 2);
                Grid.SetRow(footer, 3);
                root.Children.Add(label);
                root.Children.Add(box);
                root.Children.Add(hint);
                root.Children.Add(footer);
                dialog.Content = root;

                saveBtn.Click += (_, __) =>
                {
                    var value = (box.Text ?? string.Empty).Trim().TrimEnd('/');
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        MessageBox.Show("Prefix cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    BillPrefixSettings.SavePrefix(value);
                    dialog.DialogResult = true;
                    dialog.Close();
                };
                cancelBtn.Click += (_, __) => { dialog.DialogResult = false; dialog.Close(); };

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open bill prefix settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateBillFromLR_Click(object sender, RoutedEventArgs e)
        {
            if (LRLedgerGrid == null) return;
            var selected = LRLedgerGrid.SelectedItems.Cast<LREntry>().ToList();
            if (selected.Count == 0) { MessageBox.Show("Select LR entries to create a bill.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var unbilled = selected.Where(lr => string.IsNullOrEmpty(lr.BillNo)).ToList();
            if (unbilled.Count == 0) { MessageBox.Show("All selected LRs already have a bill.", "Already Billed", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (unbilled.Count < selected.Count)
            {
                var result = MessageBox.Show($"{selected.Count - unbilled.Count} of {selected.Count} selected LRs already have a bill. Create bill for the remaining {unbilled.Count}?", "Partial Bill", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }
            var first = unbilled[0];
            var form = new BillFormWindow();
            form.Result.Party = string.IsNullOrWhiteSpace(first.BillParty) ? first.ConsignorName : first.BillParty;
            form.Result.LRNo = string.Join(", ", unbilled.Select(lr => lr.LRNo).Where(n => !string.IsNullOrEmpty(n)));
            form.Result.LRDate = first.Date;
            form.Result.From = first.From;
            form.Result.To = first.To;
            form.Result.VehicleType = first.VehicleType;
            form.Result.Freight = unbilled.Sum(lr => lr.TotalFreight);
            form.Result.HML = unbilled.Sum(lr => lr.Hamali);
            form.Result.Detention = unbilled.Sum(lr => lr.Detention);
            form.Result.OTHR = unbilled.Sum(lr => lr.Others);
            form.Result.StCharge = unbilled.Sum(lr => lr.StCharge);
            form.Result.RCVD = unbilled.Sum(lr => lr.NEFT + lr.CASH);
            form.Result.TDS = unbilled.Sum(lr => lr.TDS);
            form.Result.DED = unbilled.Sum(lr => lr.Ded);
            form.DataContext = form.Result;

            form.Owner = this;
            form.Closed += (_, __) =>
            {
                if (!form.WasSaved) return;
                var entry = form.Result;
                try
                {
                    SaveBillRowsFromFormEntry(entry);

                    // Update each unbilled LR with the BillNo
                    foreach (var lr in unbilled)
                    {
                        if (lr.Id <= 0) continue;
                        UpdateLRBillNo(lr.Id, entry.BillNo);
                    }

                    LRVM.RefreshAfterDelete();
                    BillVM.RefreshAfterDelete();
                    LRUpdatePageUI();
                    BillUpdatePageUI();
                }
                catch (Exception ex) { MessageBox.Show("Save error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            form.Show();
        }

        private void SaveBillRowsFromFormEntry(BillEntry entry)
        {
            if (entry == null) return;

            var lrNos = (entry.LRNo ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var repo = new BillRepository();
            int nextSr = GetNextBillSr();

            // If single/no LR, keep existing behavior.
            if (lrNos.Count <= 1)
            {
                entry.Sr = nextSr;
                repo.Upsert(entry);
                if (lrNos.Count == 1)
                {
                    UpdateLRBillNoByLRNo(lrNos[0], entry.BillNo);
                }
                return;
            }

            var lrMap = new Dictionary<string, LREntry>(StringComparer.OrdinalIgnoreCase);
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var pNames = new List<string>();
                    for (int i = 0; i < lrNos.Count; i++)
                    {
                        var p = "@lr" + i;
                        pNames.Add(p);
                        cmd.Parameters.AddWithValue(p, lrNos[i]);
                    }
                    cmd.CommandText = $@"SELECT LRNo, Date, ConsignorName, BillParty, FromLocation, ToLocation, VehicleType,
                        TotalFreight, Hamali, Detention, Others, NEFT, CASH, TDS, Ded
                        FROM LREntries
                        WHERE LRNo IN ({string.Join(",", pNames)});";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var lrNo = (r["LRNo"] as string ?? string.Empty).Trim();
                            if (string.IsNullOrWhiteSpace(lrNo)) continue;
                            lrMap[lrNo] = new LREntry
                            {
                                LRNo = lrNo,
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
                                NEFT = Convert.ToDecimal(r["NEFT"]),
                                CASH = Convert.ToDecimal(r["CASH"]),
                                TDS = Convert.ToDecimal(r["TDS"]),
                                Ded = Convert.ToDecimal(r["Ded"])
                            };
                        }
                    }
                }
            }

            foreach (var lrNo in lrNos)
            {
                lrMap.TryGetValue(lrNo, out var lr);
                var row = new BillEntry
                {
                    Sr = nextSr++,
                    BillNo = entry.BillNo,
                    BillDate = entry.BillDate,
                    Party = string.IsNullOrWhiteSpace(entry.Party)
                        ? (!string.IsNullOrWhiteSpace(lr?.BillParty) ? lr.BillParty : (lr?.ConsignorName ?? entry.Party))
                        : entry.Party,
                    LRNo = lrNo,
                    LRDate = lr?.Date,
                    From = lr?.From ?? entry.From,
                    To = lr?.To ?? entry.To,
                    VehicleType = lr?.VehicleType ?? entry.VehicleType,
                    Freight = lr?.TotalFreight ?? 0m,
                    HML = lr?.Hamali ?? 0m,
                    Detention = lr?.Detention ?? 0m,
                    OTHR = lr?.Others ?? 0m,
                    StCharge = lr?.StCharge ?? 0m,
                    RCVD = lr != null ? (lr.NEFT + lr.CASH) : 0m,
                    TDS = lr?.TDS ?? 0m,
                    DED = lr?.Ded ?? 0m,
                    MOP = entry.MOP,
                    MR = entry.MR,
                    Date = entry.Date
                };
                repo.Upsert(row);
                UpdateLRBillNoByLRNo(lrNo, entry.BillNo);
            }
        }

        private int GetNextBillSr()
        {
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COALESCE(MAX(Sr), 0) FROM Bills;";
                    return Convert.ToInt32(cmd.ExecuteScalar()) + 1;
                }
            }
        }
        private void UpdateLRBillNoByLRNo(string lrNo, string billNo)
        {
            if (string.IsNullOrWhiteSpace(lrNo)) return;
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE LREntries SET BillNo = @billNo WHERE LRNo = @lrNo;";
                    cmd.Parameters.AddWithValue("@billNo", (object)billNo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@lrNo", lrNo.Trim());
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void UpdateLRBillNo(int lrId, string billNo)
        {
            if (lrId <= 0) return;
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE LREntries SET BillNo = @billNo WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@billNo", (object)billNo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@id", lrId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void OpenPartyLedgerTab_Click(object sender, RoutedEventArgs e)
        {
            DashboardView.Visibility = Visibility.Collapsed;
            DeliveryChallanView.Visibility = Visibility.Collapsed;
            LRLedgerView.Visibility = Visibility.Collapsed;
            TrackingLedgerView.Visibility = Visibility.Collapsed;
            PartyLedgerView.Visibility = Visibility.Visible;
            BillLedgerView.Visibility = Visibility.Collapsed;
            VehicleLedgerView.Visibility = Visibility.Collapsed;
            CBSLedgerView.Visibility = Visibility.Collapsed;
            TabPartyLedger.Style = (Style)FindResource("ActiveTabButtonStyle");
            TabDashboard.Style = (Style)FindResource("TabButtonStyle");
            TabDeliveryChallans.Style = (Style)FindResource("TabButtonStyle");
            TabLRLedger.Style = (Style)FindResource("TabButtonStyle");
            TabTrackingLedger.Style = (Style)FindResource("TabButtonStyle");
            TabBillLedger.Style = (Style)FindResource("TabButtonStyle");
            TabVehicleLedger.Style = (Style)FindResource("TabButtonStyle");
            TabCBSLedger.Style = (Style)FindResource("TabButtonStyle");
            if (PageTitle != null) PageTitle.Text = "Party Ledger";
            RefreshPartyGrid();
        }

        private void OpenPartyLedger_Click(object sender, RoutedEventArgs e)
        {
            OpenPartyLedgerTab_Click(sender, e);
        }

        private List<PartyEntry> _allParties;
        private List<VehicleEntry> _allVehicles;

        private void RefreshPartyGrid()
        {
            try
            {
                var repo = new PartyRepository();
                // One-time population from LR entries (background-friendly, runs only once)
                if (repo.GetAll().Count == 0)
                {
                    var toAdd = new List<PartyEntry>();
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                        {
                            conn.Open();
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "SELECT DISTINCT ConsignorName, ConsignorAddress, ConsignorGST FROM LREntries WHERE ConsignorName IS NOT NULL AND ConsignorName != ''";
                                using (var r = cmd.ExecuteReader())
                                    while (r.Read())
                                    {
                                        var n = r["ConsignorName"] as string;
                                        if (string.IsNullOrWhiteSpace(n) || names.Contains(n)) continue;
                                        names.Add(n);
                                        toAdd.Add(new PartyEntry { PartyName = n.Trim(), Address = (r["ConsignorAddress"] as string)?.Trim() ?? "", GSTNo = (r["ConsignorGST"] as string)?.Trim() ?? "" });
                                    }
                            }
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "SELECT DISTINCT ConsigneeName, ConsigneeAddress, ConsigneeGST FROM LREntries WHERE ConsigneeName IS NOT NULL AND ConsigneeName != ''";
                                using (var r = cmd.ExecuteReader())
                                    while (r.Read())
                                    {
                                        var n = r["ConsigneeName"] as string;
                                        if (string.IsNullOrWhiteSpace(n) || names.Contains(n)) continue;
                                        names.Add(n);
                                        toAdd.Add(new PartyEntry { PartyName = n.Trim(), Address = (r["ConsigneeAddress"] as string)?.Trim() ?? "", GSTNo = (r["ConsigneeGST"] as string)?.Trim() ?? "" });
                                    }
                            }
                        }
                        int sr = 1;
                        foreach (var p in toAdd) { p.Sr = sr++; repo.Upsert(p); }
                    }
                    catch { }
                }
                _allParties = repo.GetAll();
                PartyGrid.ItemsSource = _allParties;
            }
            catch { }
        }

        private void PartyGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            var entry = e.Row.Item as PartyEntry;
            if (entry == null || entry.Id <= 0) return;
            try
            {
                if (e.EditingElement is TextBox textBox)
                {
                    var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                    binding?.UpdateSource();
                }
                new PartyRepository().Upsert(entry);
            }
            catch { }
        }

        private void PartyAddRow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var repo = new PartyRepository();
                var list = repo.GetAll();
                int maxSr = 0;
                foreach (var p in list) if (p.Sr > maxSr) maxSr = p.Sr;

                var entry = new PartyEntry
                {
                    Sr = maxSr + 1,
                    PartyName = "New Party",
                    Address = string.Empty,
                    GSTNo = string.Empty
                };

                repo.Upsert(entry);
                RefreshPartyGrid();

                var added = _allParties?.FirstOrDefault(x =>
                    string.Equals((x.PartyName ?? string.Empty).Trim(), "New Party", StringComparison.OrdinalIgnoreCase) &&
                    x.Sr == entry.Sr);
                if (added != null && PartyGrid != null)
                {
                    PartyGrid.SelectedItem = added;
                    PartyGrid.ScrollIntoView(added);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to add party row: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportParty_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSV Files|*.csv;*.txt|All Files|*.*",
                Title = "Select Party Import File"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var lines = System.IO.File.ReadAllLines(dialog.FileName);
                if (lines.Length < 2)
                {
                    MessageBox.Show("CSV file has no data rows.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var headers = SplitCsvLine(lines[0]);
                var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Length; i++)
                {
                    var key = NormalizePartyHeader(headers[i]);
                    if (!colMap.ContainsKey(key)) colMap[key] = i;
                }

                int nameIdx = GetFirstIndex(colMap, "partyname", "party", "name");
                int addrIdx = GetFirstIndex(colMap, "address", "addr");
                int gstIdx = GetFirstIndex(colMap, "gst", "gstno", "gstnumber");
                if (nameIdx < 0)
                {
                    MessageBox.Show("Required column missing: Party Name", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var repo = new PartyRepository();
                var existing = repo.GetAll();
                int maxSr = existing.Count > 0 ? existing.Max(x => x.Sr) : 0;
                var existingByName = new Dictionary<string, PartyEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in existing)
                {
                    var k = (p.PartyName ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(k) && !existingByName.ContainsKey(k)) existingByName[k] = p;
                }

                int imported = 0, updated = 0, skipped = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    var parts = SplitCsvLine(lines[i]);
                    var name = GetCsvCol(parts, nameIdx).Trim();
                    if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }
                    var addr = addrIdx >= 0 ? GetCsvCol(parts, addrIdx).Trim() : string.Empty;
                    var gst = (gstIdx >= 0 ? GetCsvCol(parts, gstIdx).Trim() : string.Empty).ToUpperInvariant();

                    if (existingByName.TryGetValue(name, out var row))
                    {
                        row.Address = string.IsNullOrWhiteSpace(addr) ? row.Address : addr;
                        row.GSTNo = string.IsNullOrWhiteSpace(gst) ? row.GSTNo : gst;
                        repo.Upsert(row);
                        updated++;
                    }
                    else
                    {
                        var n = new PartyEntry { Sr = ++maxSr, PartyName = name, Address = addr, GSTNo = gst };
                        repo.Upsert(n);
                        existingByName[name] = n;
                        imported++;
                    }
                }

                RefreshPartyGrid();
                MessageBox.Show($"Party import complete.\nAdded: {imported}\nUpdated: {updated}\nSkipped: {skipped}", "Import Result", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Party import failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PartyDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PartyGrid == null) return;
                var selected = PartyGrid.SelectedItems.Cast<PartyEntry>().Where(x => x != null).ToList();
                if (selected.Count == 0)
                {
                    MessageBox.Show("Select party row(s) to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var confirm = MessageBox.Show(
                    $"Delete {selected.Count} selected part{(selected.Count == 1 ? "y" : "ies")}?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;

                using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                {
                    conn.Open();
                    foreach (var row in selected)
                    {
                        if (row.Id <= 0) continue;
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "DELETE FROM Parties WHERE Id = @id;";
                            cmd.Parameters.AddWithValue("@id", row.Id);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                RefreshPartyGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to delete selected party rows: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string NormalizePartyHeader(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "").Replace(".", "").Replace("_", "");
        }

        private static int GetFirstIndex(Dictionary<string, int> map, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (map.TryGetValue(key, out var idx)) return idx;
            }
            return -1;
        }

        private static string GetCsvCol(string[] parts, int idx)
        {
            if (parts == null || idx < 0 || idx >= parts.Length) return string.Empty;
            return parts[idx] ?? string.Empty;
        }

        private void OpenVehicleLedgerTab_Click(object sender, RoutedEventArgs e)
        {
            DashboardView.Visibility = Visibility.Collapsed;
            DeliveryChallanView.Visibility = Visibility.Collapsed;
            LRLedgerView.Visibility = Visibility.Collapsed;
            TrackingLedgerView.Visibility = Visibility.Collapsed;
            PartyLedgerView.Visibility = Visibility.Collapsed;
            BillLedgerView.Visibility = Visibility.Collapsed;
            VehicleLedgerView.Visibility = Visibility.Collapsed;
            CBSLedgerView.Visibility = Visibility.Collapsed;
            VehicleLedgerView.Visibility = Visibility.Visible;
            TabVehicleLedger.Style = (Style)FindResource("ActiveTabButtonStyle");
            TabPartyLedger.Style = (Style)FindResource("TabButtonStyle");
            TabBillLedger.Style = (Style)FindResource("TabButtonStyle");
            TabDashboard.Style = (Style)FindResource("TabButtonStyle");
            TabDeliveryChallans.Style = (Style)FindResource("TabButtonStyle");
            TabLRLedger.Style = (Style)FindResource("TabButtonStyle");
            TabTrackingLedger.Style = (Style)FindResource("TabButtonStyle");
            TabCBSLedger.Style = (Style)FindResource("TabButtonStyle");
            if (PageTitle != null) PageTitle.Text = "Vehicle Ledger";
            RefreshVehicleGrid();
        }

        private void RefreshVehicleGrid()
        {
            try
            {
                _allVehicles = new VehicleRepository().GetAll();
                if (VehicleGrid != null) VehicleGrid.ItemsSource = _allVehicles;
            }
            catch { }
        }

        private void VehicleSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_allVehicles == null || VehicleGrid == null) return;
            var filter = (VehicleSearchBox?.Text ?? string.Empty).Trim().ToLowerInvariant();
            VehicleGrid.ItemsSource = string.IsNullOrEmpty(filter)
                ? _allVehicles
                : _allVehicles.Where(v =>
                    (v.VehicleNumber ?? string.Empty).ToLowerInvariant().Contains(filter) ||
                    (v.OwnerName ?? string.Empty).ToLowerInvariant().Contains(filter) ||
                    (v.PANNumber ?? string.Empty).ToLowerInvariant().Contains(filter) ||
                    (v.EngineNumber ?? string.Empty).ToLowerInvariant().Contains(filter) ||
                    (v.ChassisNumber ?? string.Empty).ToLowerInvariant().Contains(filter) ||
                    (v.VehicleType ?? string.Empty).ToLowerInvariant().Contains(filter)).ToList();
        }

        private void VehicleGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            var entry = e.Row.Item as VehicleEntry;
            if (entry == null) return;
            try
            {
                if (e.EditingElement is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                new VehicleRepository().Upsert(entry);
            }
            catch { }
        }

        private void OpenCBSLedger_Click(object sender, RoutedEventArgs e)
        {
            DashboardView.Visibility = Visibility.Collapsed;
            DeliveryChallanView.Visibility = Visibility.Collapsed;
            LRLedgerView.Visibility = Visibility.Collapsed;
            TrackingLedgerView.Visibility = Visibility.Collapsed;
            PartyLedgerView.Visibility = Visibility.Collapsed;
            BillLedgerView.Visibility = Visibility.Collapsed;
            VehicleLedgerView.Visibility = Visibility.Collapsed;
            CBSLedgerView.Visibility = Visibility.Visible;

            TabCBSLedger.Style = (Style)FindResource("ActiveTabButtonStyle");
            TabVehicleLedger.Style = (Style)FindResource("TabButtonStyle");
            TabPartyLedger.Style = (Style)FindResource("TabButtonStyle");
            TabBillLedger.Style = (Style)FindResource("TabButtonStyle");
            TabDashboard.Style = (Style)FindResource("TabButtonStyle");
            TabDeliveryChallans.Style = (Style)FindResource("TabButtonStyle");
            TabLRLedger.Style = (Style)FindResource("TabButtonStyle");
            TabTrackingLedger.Style = (Style)FindResource("TabButtonStyle");

            if (PageTitle != null) PageTitle.Text = "Cash Bank Statement";
            RefreshCBSAccounts();
            RefreshCBSGrid();
        }

        private void RefreshCBSAccounts()
        {
            try
            {
                _allCbsAccounts = _cbsAccountRepo.GetAll();
                var names = _allCbsAccounts
                    .Where(a => a.IsActive)
                    .Select(a => (a.AccountName ?? string.Empty).Trim())
                    .Where(a => a.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(a => a)
                    .ToList();

                for (int i = CBSAccountNames.Count - 1; i >= 0; i--)
                {
                    var existing = CBSAccountNames[i];
                    if (!names.Any(n => string.Equals(n, existing, StringComparison.OrdinalIgnoreCase)))
                    {
                        CBSAccountNames.RemoveAt(i);
                    }
                }

                foreach (var name in names)
                {
                    if (!CBSAccountNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        CBSAccountNames.Add(name);
                    }
                }

                CBSGrid?.Items.Refresh();
            }
            catch { }
        }

        private void RefreshCBSGrid()
        {
            try
            {
                _allCbsEntries = BuildCBSAccountViewRows(_cbsRepo.GetAll());
                if (CBSGrid != null) CBSGrid.ItemsSource = _allCbsEntries;
                ReapplyCBSViewState();
            }
            catch { }
        }

        private static List<CashBankStatementEntry> BuildCBSAccountViewRows(IEnumerable<CashBankStatementEntry> rawRows)
        {
            return (rawRows ?? Enumerable.Empty<CashBankStatementEntry>())
                .Where(x => !string.IsNullOrWhiteSpace(x.AccountName))
                .GroupBy(x => x.AccountName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var latest = g.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).First();
                    return new CashBankStatementEntry
                    {
                        Id = latest.Id,
                        Sr = latest.Sr,
                        CBS = latest.CBS,
                        Date = g.Max(x => x.Date),
                        AccountName = latest.AccountName?.Trim(),
                        Particulars = latest.Particulars,
                        Remarks = latest.Remarks,
                        BankDr = g.Sum(x => x.BankDr),
                        BankCr = g.Sum(x => x.BankCr),
                        CashDr = g.Sum(x => x.CashDr),
                        CashCr = g.Sum(x => x.CashCr)
                    };
                })
                .OrderByDescending(x => x.Date)
                .ThenBy(x => x.AccountName)
                .ToList();
        }

        private void UpdateCBSFooter()
        {
            if (CBSRecordsTextBlock == null) return;
            var cv = CollectionViewSource.GetDefaultView(CBSGrid?.ItemsSource);
            var count = cv == null
                ? ((CBSGrid?.ItemsSource as IEnumerable<CashBankStatementEntry>)?.Count() ?? 0)
                : cv.Cast<object>().OfType<CashBankStatementEntry>().Count();
            CBSRecordsTextBlock.Text = $"Records: {count}";
        }

        private void CBSSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var filter = (CBSSearchBox?.Text ?? string.Empty).Trim();
                if (string.Equals(filter, CbsSearchPlaceholder, StringComparison.OrdinalIgnoreCase)) filter = string.Empty;
                if (CBSGrid == null) return;
                CBSGrid.ItemsSource = filter.Length == 0 ? _allCbsEntries : _cbsRepo.Search(filter);
                ReapplyCBSViewState();
            }
            catch { }
        }

        private void CBSSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (CBSSearchBox == null) return;
            if (string.Equals(CBSSearchBox.Text, CbsSearchPlaceholder, StringComparison.Ordinal))
            {
                CBSSearchBox.Text = string.Empty;
                CBSSearchBox.Foreground = Brushes.Black;
            }
        }

        private void CBSSearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (CBSSearchBox == null) return;
            if (string.IsNullOrWhiteSpace(CBSSearchBox.Text))
            {
                CBSSearchBox.Text = CbsSearchPlaceholder;
                CBSSearchBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
            }
        }

        private void CBSAddAccount_Click(object sender, RoutedEventArgs e)
        {
            var name = PromptCBSAccountName();
            if (string.IsNullOrWhiteSpace(name)) return;
            AddCBSAccount(name.Trim());
        }

        private string PromptCBSAccountName()
        {
            var dialog = new Window
            {
                Title = "Add Account",
                Owner = this,
                Width = 420,
                Height = 180,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.White
            };

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = "Account Name",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var box = new TextBox
            {
                Height = 30,
                Padding = new Thickness(6, 2, 6, 2)
            };
            var hint = new TextBlock
            {
                Text = "Type account name and press Enter or click Save.",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280")),
                Margin = new Thickness(0, 8, 0, 0)
            };
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var saveBtn = new Button { Content = "Save", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            var cancelBtn = new Button { Content = "Cancel", Width = 90, Height = 30 };
            footer.Children.Add(saveBtn);
            footer.Children.Add(cancelBtn);

            Grid.SetRow(label, 0);
            Grid.SetRow(box, 1);
            Grid.SetRow(hint, 2);
            Grid.SetRow(footer, 3);
            root.Children.Add(label);
            root.Children.Add(box);
            root.Children.Add(hint);
            root.Children.Add(footer);
            dialog.Content = root;

            string result = null;
            Action trySave = () =>
            {
                var value = (box.Text ?? string.Empty).Trim();
                if (value.Length == 0)
                {
                    MessageBox.Show(dialog, "Enter account name first.", "Validation", MessageBoxButton.OK, MessageBoxImage.Information);
                    box.Focus();
                    return;
                }
                result = value;
                dialog.DialogResult = true;
                dialog.Close();
            };

            saveBtn.Click += (_, __) => trySave();
            cancelBtn.Click += (_, __) => dialog.Close();
            box.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter)
                {
                    ke.Handled = true;
                    trySave();
                }
                else if (ke.Key == Key.Escape)
                {
                    ke.Handled = true;
                    dialog.Close();
                }
            };

            dialog.Loaded += (_, __) => box.Focus();
            dialog.ShowDialog();
            return result;
        }

        private void AddCBSAccount(string name)
        {
            try
            {
                var existing = _cbsAccountRepo.FindByName(name);
                if (existing != null)
                {
                    if (!existing.IsActive)
                    {
                        existing.IsActive = true;
                        _cbsAccountRepo.Upsert(existing);
                    }
                }
                else
                {
                    _cbsAccountRepo.Upsert(new CBSAccountEntry
                    {
                        Sr = _cbsAccountRepo.GetMaxSr() + 1,
                        AccountName = name,
                        IsActive = true
                    });
                }

                RefreshCBSAccounts();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to add account: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CBSGrid_Loaded(object sender, RoutedEventArgs e)
        {
            AttachCBSResizePersistenceHooks();
        }

        private void AttachCBSResizePersistenceHooks()
        {
            if (CBSGrid == null) return;

            if (!_cbsGridResizeHooksAttached)
            {
                var rowHeightDpd = DependencyPropertyDescriptor.FromProperty(DataGrid.RowHeightProperty, typeof(DataGrid));
                rowHeightDpd?.AddValueChanged(CBSGrid, (_, __) => SaveGridSettings());
                _cbsGridResizeHooksAttached = true;
            }

            var widthDpd = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
            if (widthDpd == null) return;

            foreach (var col in CBSGrid.Columns)
            {
                if (col == null || _cbsWidthHookedColumns.Contains(col)) continue;
                widthDpd.AddValueChanged(col, (_, __) => SaveGridSettings());
                _cbsWidthHookedColumns.Add(col);
            }
        }

        private void CBSAddRow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var entry = new CashBankStatementEntry
                {
                    Sr = _cbsRepo.GetMaxSr() + 1,
                    Date = DateTime.Today,
                    CBS = DateTime.Today.ToString("MMM-yy"),
                    AccountName = string.Empty,
                    Particulars = string.Empty,
                    Remarks = string.Empty,
                    BankDr = 0,
                    BankCr = 0,
                    CashDr = 0,
                    CashCr = 0
                };

                _cbsRepo.Upsert(entry);
                RefreshCBSGrid();

                if (CBSGrid != null)
                {
                    var list = CBSGrid.ItemsSource as IList<CashBankStatementEntry>;
                    var added = list?.FirstOrDefault(x => x.Id == entry.Id);
                    if (added != null)
                    {
                        CBSGrid.SelectedItem = added;
                        CBSGrid.ScrollIntoView(added);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to add CBS row: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CBSAddEntry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Window
                {
                    Title = "Add CBS Entry",
                    Owner = this,
                    Width = 520,
                    Height = 420,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brushes.White
                };

                var root = new Grid { Margin = new Thickness(14) };
                for (int i = 0; i < 8; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                TextBlock mkLabel(string t) => new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8), FontWeight = FontWeights.SemiBold };
                TextBox mkText() => new TextBox { Height = 30, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6, 2, 6, 2) };

                var accountLabel = mkLabel("Account");
                var accountBox = new ComboBox
                {
                    Height = 30,
                    Margin = new Thickness(0, 0, 0, 8),
                    IsEditable = true,
                    IsTextSearchEnabled = true,
                    ItemsSource = CBSAccountNames
                };
                var partLabel = mkLabel("Particulars");
                var partBox = mkText();
                var remarksLabel = mkLabel("Remarks");
                var remarksBox = mkText();
                var bankDrLabel = mkLabel("Bank Dr");
                var bankDrBox = mkText();
                var bankCrLabel = mkLabel("Bank Cr");
                var bankCrBox = mkText();
                var cashDrLabel = mkLabel("Cash Dr");
                var cashDrBox = mkText();
                var cashCrLabel = mkLabel("Cash Cr");
                var cashCrBox = mkText();

                Grid.SetRow(accountLabel, 0); Grid.SetColumn(accountLabel, 0);
                Grid.SetRow(accountBox, 0); Grid.SetColumn(accountBox, 1);
                Grid.SetRow(partLabel, 1); Grid.SetColumn(partLabel, 0);
                Grid.SetRow(partBox, 1); Grid.SetColumn(partBox, 1);
                Grid.SetRow(remarksLabel, 2); Grid.SetColumn(remarksLabel, 0);
                Grid.SetRow(remarksBox, 2); Grid.SetColumn(remarksBox, 1);
                Grid.SetRow(bankDrLabel, 3); Grid.SetColumn(bankDrLabel, 0);
                Grid.SetRow(bankDrBox, 3); Grid.SetColumn(bankDrBox, 1);
                Grid.SetRow(bankCrLabel, 4); Grid.SetColumn(bankCrLabel, 0);
                Grid.SetRow(bankCrBox, 4); Grid.SetColumn(bankCrBox, 1);
                Grid.SetRow(cashDrLabel, 5); Grid.SetColumn(cashDrLabel, 0);
                Grid.SetRow(cashDrBox, 5); Grid.SetColumn(cashDrBox, 1);
                Grid.SetRow(cashCrLabel, 6); Grid.SetColumn(cashCrLabel, 0);
                Grid.SetRow(cashCrBox, 6); Grid.SetColumn(cashCrBox, 1);

                root.Children.Add(accountLabel); root.Children.Add(accountBox);
                root.Children.Add(partLabel); root.Children.Add(partBox);
                root.Children.Add(remarksLabel); root.Children.Add(remarksBox);
                root.Children.Add(bankDrLabel); root.Children.Add(bankDrBox);
                root.Children.Add(bankCrLabel); root.Children.Add(bankCrBox);
                root.Children.Add(cashDrLabel); root.Children.Add(cashDrBox);
                root.Children.Add(cashCrLabel); root.Children.Add(cashCrBox);

                var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var saveBtn = new Button { Content = "Save", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
                var cancelBtn = new Button { Content = "Cancel", Width = 90, Height = 30 };
                footer.Children.Add(saveBtn);
                footer.Children.Add(cancelBtn);
                Grid.SetRow(footer, 9); Grid.SetColumnSpan(footer, 2);
                root.Children.Add(footer);

                decimal parseAmt(TextBox tb)
                {
                    var raw = (tb.Text ?? string.Empty).Trim();
                    if (raw.Length == 0) return 0m;
                    return decimal.TryParse(raw, out var x) ? x : 0m;
                }

                saveBtn.Click += (_, __) =>
                {
                    var account = (accountBox.Text ?? string.Empty).Trim();
                    if (account.Length == 0)
                    {
                        MessageBox.Show(dialog, "Select or enter account name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Information);
                        accountBox.Focus();
                        return;
                    }

                    AddCBSAccount(account);
                    var row = new CashBankStatementEntry
                    {
                        Sr = _cbsRepo.GetMaxSr() + 1,
                        Date = DateTime.Today,
                        CBS = DateTime.Today.ToString("MMM-yy"),
                        AccountName = account,
                        Particulars = string.Empty,
                        Remarks = string.Empty
                    };

                    row.BankDr = parseAmt(bankDrBox);
                    row.BankCr = parseAmt(bankCrBox);
                    row.CashDr = parseAmt(cashDrBox);
                    row.CashCr = parseAmt(cashCrBox);
                    row.Date = DateTime.Today;
                    row.CBS = row.Date.ToString("MMM-yy");

                    var part = (partBox.Text ?? string.Empty).Trim();
                    var rem = (remarksBox.Text ?? string.Empty).Trim();
                    if (part.Length > 0) row.Particulars = part;
                    if (rem.Length > 0) row.Remarks = rem;

                    _cbsRepo.Upsert(row);
                    RefreshCBSGrid();
                    dialog.Close();
                };

                cancelBtn.Click += (_, __) => dialog.Close();
                dialog.Content = root;
                dialog.Loaded += (_, __) => accountBox.Focus();
                dialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open CBS entry form: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private sealed class CBSAccountSummaryRow
        {
            public string AccountName { get; set; }
            public decimal BankDr { get; set; }
            public decimal BankCr { get; set; }
            public decimal CashDr { get; set; }
            public decimal CashCr { get; set; }
            public decimal TotalDr => BankDr + CashDr;
            public decimal TotalCr => BankCr + CashCr;
            public decimal Net => TotalCr - TotalDr;
        }

        private void EnsureSingleCBSRowPerAccount()
        {
            // no-op: keep transaction rows; aggregate only for CBS main grid view
        }

        private void CBSAccountSummary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new Window
                {
                    Title = "CBS Account Summary",
                    Owner = this,
                    Width = 1080,
                    Height = 620,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brushes.White
                };

                var root = new Grid { Margin = new Thickness(10) };
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var accounts = _cbsRepo.GetAll()
                    .Where(x => !string.IsNullOrWhiteSpace(x.AccountName))
                    .Select(x => x.AccountName.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();

                var accountList = new ListBox
                {
                    ItemsSource = accounts,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(accountList, 0);
                Grid.SetRow(accountList, 0);
                root.Children.Add(accountList);

                var right = new Grid();
                right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(right, 1);
                Grid.SetRow(right, 0);
                root.Children.Add(right);

                var header = new TextBlock
                {
                    Text = "Select account from left",
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                Grid.SetRow(header, 0);
                right.Children.Add(header);

                var grid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    IsReadOnly = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.All,
                    RowHeaderWidth = 0,
                    Margin = new Thickness(0)
                };
                grid.Columns.Add(new DataGridTextColumn { Header = "Date", Binding = new Binding("Date") { StringFormat = "dd.MM.yy" }, Width = 95 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Particulars", Binding = new Binding("Particulars"), Width = 240 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Remarks", Binding = new Binding("Remarks"), Width = 200 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Bank Dr", Binding = new Binding("BankDr") { StringFormat = "N2" }, Width = 110 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Bank Cr", Binding = new Binding("BankCr") { StringFormat = "N2" }, Width = 110 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Cash Dr", Binding = new Binding("CashDr") { StringFormat = "N2" }, Width = 110 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Cash Cr", Binding = new Binding("CashCr") { StringFormat = "N2" }, Width = 110 });
                grid.CellEditEnding += (s, ee) =>
                {
                    if (ee.EditAction != DataGridEditAction.Commit) return;
                    var row = ee.Row?.Item as CashBankStatementEntry;
                    if (row == null || row.Id <= 0) return;
                    try
                    {
                        if (ee.EditingElement is TextBox tb)
                        {
                            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                        }
                        row.CBS = row.Date.ToString("MMM-yy");
                        _cbsRepo.Upsert(row);
                        RefreshCBSGrid();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Unable to save summary entry: " + ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                string activeFilterColumn = null;
                var activeFilterValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var currentEntries = new List<CashBankStatementEntry>();
                Func<CashBankStatementEntry, string, string> getValue = (r, h) =>
                {
                    if (r == null) return string.Empty;
                    switch ((h ?? string.Empty).Trim())
                    {
                        case "Date": return r.Date.ToString("dd.MM.yy");
                        case "Particulars": return (r.Particulars ?? string.Empty).Trim();
                        case "Remarks": return (r.Remarks ?? string.Empty).Trim();
                        case "Bank Dr": return r.BankDr.ToString("N2");
                        case "Bank Cr": return r.BankCr.ToString("N2");
                        case "Cash Dr": return r.CashDr.ToString("N2");
                        case "Cash Cr": return r.CashCr.ToString("N2");
                        default: return string.Empty;
                    }
                };
                grid.PreviewMouseRightButtonUp += (s, me) =>
                {
                    DependencyObject source = me.OriginalSource as DependencyObject;
                    while (source != null && !(source is DataGridColumnHeader))
                        source = System.Windows.Media.VisualTreeHelper.GetParent(source);
                    var colHeader = source as DataGridColumnHeader;
                    if (colHeader?.Column == null) return;

                    string propName = null;
                    var textCol = colHeader.Column as DataGridTextColumn;
                    if (textCol?.Binding is Binding b && !string.IsNullOrWhiteSpace(b.Path?.Path))
                        propName = b.Path.Path;
                    if (string.IsNullOrWhiteSpace(propName))
                        propName = (colHeader.Column.SortMemberPath ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(propName)) return;

                    var menu = new ContextMenu();
                    var miAsc = new MenuItem { Header = "Sort A to Z" };
                    var miDesc = new MenuItem { Header = "Sort Z to A" };
                    var miClear = new MenuItem { Header = "Clear Sort" };

                    Action<bool> applySort = asc =>
                    {
                        var cv = CollectionViewSource.GetDefaultView(grid.ItemsSource);
                        if (cv == null) return;
                        cv.SortDescriptions.Clear();
                        cv.SortDescriptions.Add(new SortDescription(propName, asc ? ListSortDirection.Ascending : ListSortDirection.Descending));
                        cv.Refresh();
                        foreach (var col in grid.Columns) col.SortDirection = null;
                        colHeader.Column.SortDirection = asc ? ListSortDirection.Ascending : ListSortDirection.Descending;
                    };

                    miAsc.Click += (_, __) => applySort(true);
                    miDesc.Click += (_, __) => applySort(false);
                    miClear.Click += (_, __) =>
                    {
                        var cv = CollectionViewSource.GetDefaultView(grid.ItemsSource);
                        if (cv == null) return;
                        cv.SortDescriptions.Clear();
                        cv.Refresh();
                        foreach (var col in grid.Columns) col.SortDirection = null;
                    };

                    menu.Items.Add(miAsc);
                    menu.Items.Add(miDesc);
                    menu.Items.Add(new Separator());
                    menu.Items.Add(miClear);

                    menu.Items.Add(new Separator());
                    var filterHeader = (colHeader.Column.Header?.ToString() ?? string.Empty).Trim();
                    var miSelectAll = new MenuItem { Header = "Select All" };
                    miSelectAll.Click += (_, __) =>
                    {
                        activeFilterColumn = null;
                        activeFilterValues.Clear();
                        accountList.RaiseEvent(new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new List<object>(), new List<object>()));
                    };
                    var miClearFilter = new MenuItem { Header = "Clear Filter" };
                    miClearFilter.Click += (_, __) =>
                    {
                        activeFilterColumn = null;
                        activeFilterValues.Clear();
                        accountList.RaiseEvent(new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new List<object>(), new List<object>()));
                    };
                    menu.Items.Add(miSelectAll);
                    menu.Items.Add(miClearFilter);
                    menu.Items.Add(new Separator());
                    var values = currentEntries.Select(x => getValue(x, filterHeader))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();
                    foreach (var val in values)
                    {
                        var miVal = new MenuItem
                        {
                            Header = val,
                            IsCheckable = true,
                            IsChecked = string.Equals(activeFilterColumn, filterHeader, StringComparison.OrdinalIgnoreCase) && activeFilterValues.Contains(val),
                            StaysOpenOnClick = true
                        };
                        miVal.Click += (_, __) =>
                        {
                            if (!string.Equals(activeFilterColumn, filterHeader, StringComparison.OrdinalIgnoreCase))
                            {
                                activeFilterColumn = filterHeader;
                                activeFilterValues.Clear();
                            }
                            if (miVal.IsChecked) activeFilterValues.Add(val); else activeFilterValues.Remove(val);
                            if (activeFilterValues.Count == 0) activeFilterColumn = null;
                            accountList.RaiseEvent(new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new List<object>(), new List<object>()));
                        };
                        menu.Items.Add(miVal);
                    }
                    menu.IsOpen = true;
                    me.Handled = true;
                };

                Grid.SetRow(grid, 1);
                right.Children.Add(grid);

                var totals = new TextBlock
                {
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 0),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827"))
                };
                Grid.SetRow(totals, 2);
                right.Children.Add(totals);

                Action refreshSelected = () =>
                {
                    var account = (accountList.SelectedItem as string ?? string.Empty).Trim();
                    if (account.Length == 0)
                    {
                        grid.ItemsSource = null;
                        header.Text = "Select account from left";
                        totals.Text = string.Empty;
                        return;
                    }

                    var entries = _cbsRepo.GetAll()
                        .Where(x => string.Equals((x.AccountName ?? string.Empty).Trim(), account, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(x => x.Date)
                        .ThenByDescending(x => x.Id)
                        .ToList();
                    currentEntries = entries;
                    IEnumerable<CashBankStatementEntry> filtered = entries;
                    if (!string.IsNullOrWhiteSpace(activeFilterColumn) && activeFilterValues.Count > 0)
                        filtered = filtered.Where(x => activeFilterValues.Contains(getValue(x, activeFilterColumn)));
                    var list = filtered.ToList();
                    grid.ItemsSource = list;
                    header.Text = $"Account: {account}";
                    totals.Text = $"Total  Bank Dr: {list.Sum(x => x.BankDr):N2}   Bank Cr: {list.Sum(x => x.BankCr):N2}   Cash Dr: {list.Sum(x => x.CashDr):N2}   Cash Cr: {list.Sum(x => x.CashCr):N2}";
                };

                accountList.SelectionChanged += (_, __) => refreshSelected();
                if (accounts.Count > 0) accountList.SelectedIndex = 0;

                win.Content = root;
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open CBS summary: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CBSDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (CBSGrid == null) return;
            var selected = CBSGrid.SelectedItems.Cast<CashBankStatementEntry>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select CBS rows to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"Delete {selected.Count} selected row(s)?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var rawRows = _cbsRepo.GetAll();
                foreach (var row in selected)
                {
                    var account = (row.AccountName ?? string.Empty).Trim();
                    foreach (var tx in rawRows.Where(x => string.Equals((x.AccountName ?? string.Empty).Trim(), account, StringComparison.OrdinalIgnoreCase)).ToList())
                    {
                        _cbsRepo.Delete(tx);
                    }
                }
                RefreshCBSGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to delete rows: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CBSGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            var row = e.Row?.Item as CashBankStatementEntry;
            if (row == null) return;

            try
            {
                if (e.EditingElement is TextBox tb)
                {
                    tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                }
                else if (e.EditingElement is ComboBox cb)
                {
                    cb.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                    cb.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateSource();
                    cb.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateSource();
                    if ((row.AccountName == null || row.AccountName.Trim().Length == 0) && cb.SelectedValue is string selectedValue && selectedValue.Trim().Length > 0)
                    {
                        row.AccountName = selectedValue.Trim();
                    }
                }
                else
                {
                    var combo = FindVisualChild<ComboBox>(e.EditingElement);
                    if (combo != null)
                    {
                        combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                        combo.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateSource();
                        var text = (combo.Text ?? string.Empty).Trim();
                        if (text.Length > 0) row.AccountName = text;
                    }
                }

                row.CBS = row.Date.ToString("MMM-yy");
                _cbsRepo.Upsert(row);
                RefreshCBSGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to save CBS entry: " + ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CBSGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender != CBSGrid) return;

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
            {
                e.Handled = true;
                CBSAddEntry_Click(sender, new RoutedEventArgs());
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                e.Handled = true;
                TryCommitGridEdits(CBSGrid);
                return;
            }

            DataGrid_PreviewKeyDown(sender, e);
        }

        private void CBSGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (CBSGrid == null || CBSSelectedSumTextBlock == null)
            {
                return;
            }

            if (CBSGrid.SelectedCells.Count < 2)
            {
                CBSSelectedSumTextBlock.Visibility = Visibility.Collapsed;
                return;
            }

            decimal total = 0;
            bool hasNumbers = false;
            foreach (var cell in CBSGrid.SelectedCells)
            {
                var col = cell.Column as DataGridBoundColumn;
                if (!(col?.Binding is Binding binding)) continue;
                var path = binding.Path?.Path;
                if (string.IsNullOrWhiteSpace(path)) continue;

                var prop = cell.Item?.GetType().GetProperty(path);
                if (prop == null) continue;
                var value = prop.GetValue(cell.Item);
                if (value == null) continue;

                if (value is decimal || value is int || value is long || value is double || value is float)
                {
                    total += Convert.ToDecimal(value);
                    hasNumbers = true;
                }
            }

            if (hasNumbers)
            {
                CBSSelectedSumTextBlock.Text = $"Selected: ₹ {total:N2}";
                CBSSelectedSumTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                CBSSelectedSumTextBlock.Visibility = Visibility.Collapsed;
            }
        }

        private static readonly HashSet<string> CBSFilterableHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CBS", "Date", "A/C", "Particulars", "Remarks", "Bank Dr", "Bank Cr", "Cash Dr", "Cash Cr"
        };

        private void CBSGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInColumnHeaderResizeGripper(e.OriginalSource as DependencyObject)) return;
            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridColumnHeader))
            {
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
            var header = source as DataGridColumnHeader;
            if (header?.Column == null || CBSGrid == null) return;

            e.Handled = true;
            bool appendMode = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            _cbsHeaderDragSelecting = true;
            _cbsDragStartDisplayIndex = header.Column.DisplayIndex;
            _cbsDragAppendMode = appendMode;
            SelectCBSColumnsByDisplayRange(_cbsDragStartDisplayIndex, _cbsDragStartDisplayIndex, appendMode);
        }

        private void CBSGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_cbsHeaderDragSelecting || CBSGrid == null || e.LeftButton != MouseButtonState.Pressed) return;

            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridColumnHeader))
            {
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
            var header = source as DataGridColumnHeader;
            if (header?.Column == null || _cbsDragStartDisplayIndex < 0) return;

            SelectCBSColumnsByDisplayRange(_cbsDragStartDisplayIndex, header.Column.DisplayIndex, _cbsDragAppendMode);
            e.Handled = true;
        }

        private void CBSGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _cbsHeaderDragSelecting = false;
            _cbsDragStartDisplayIndex = -1;
            _cbsDragAppendMode = false;
        }

        private void SelectCBSColumnsByDisplayRange(int startDisplayIndex, int endDisplayIndex, bool appendMode)
        {
            if (CBSGrid == null) return;
            int lo = Math.Min(startDisplayIndex, endDisplayIndex);
            int hi = Math.Max(startDisplayIndex, endDisplayIndex);

            if (!appendMode) CBSGrid.UnselectAllCells();

            var cols = CBSGrid.Columns
                .Where(c => c.DisplayIndex >= lo && c.DisplayIndex <= hi)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            foreach (var item in CBSGrid.Items)
            {
                if (item == CollectionView.NewItemPlaceholder) continue;
                foreach (var col in cols)
                {
                    var info = new DataGridCellInfo(item, col);
                    if (!CBSGrid.SelectedCells.Contains(info)) CBSGrid.SelectedCells.Add(info);
                }
            }
        }

        private void CBSGrid_SortingMenuOnly(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
        }

        private void CBSGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            DependencyObject walker = source;
            while (walker != null &&
                   !(walker is DataGridColumnHeader) &&
                   !(walker is DataGridCell) &&
                   !(walker is DataGridRowHeader) &&
                   !(walker is DataGridRow))
            {
                walker = System.Windows.Media.VisualTreeHelper.GetParent(walker);
            }

            var header = walker as DataGridColumnHeader;
            var cell = walker as DataGridCell;
            var rowHeader = walker as DataGridRowHeader;
            var row = walker as DataGridRow;
            if (header?.Column == null && cell == null && rowHeader == null && row == null) return;

            var headerName = header?.Column != null
                ? NormalizeHeaderForSort((header.Column.Header ?? string.Empty).ToString()).Trim()
                : string.Empty;

            e.Handled = true;
            var menu = new ContextMenu();

            if (header?.Column != null)
            {
                var copyCol = new MenuItem { Header = "Copy Column" };
                var colRef = header.Column;
                copyCol.Click += (_, __) => CopyColumnFromGrid(CBSGrid, colRef);
                menu.Items.Add(copyCol);
                menu.Items.Add(new Separator());
            }

            if (rowHeader != null || row != null)
            {
                if (row != null && row.Item != null && row.Item != CollectionView.NewItemPlaceholder)
                {
                    CBSGrid.SelectedItems.Clear();
                    CBSGrid.SelectedItems.Add(row.Item);
                }
                var copyRows = new MenuItem { Header = "Copy Row(s)" };
                copyRows.Click += (_, __) => CopySelectedRowsFromGrid(CBSGrid);
                menu.Items.Add(copyRows);
                menu.IsOpen = true;
                return;
            }

            if (cell != null)
            {
                if (cell.DataContext != null) CBSGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);
                var copyCell = new MenuItem { Header = "Copy Cell" };
                copyCell.Click += (_, __) => CopyCurrentCellFromGrid(CBSGrid);
                menu.Items.Add(copyCell);
                menu.IsOpen = true;
                return;
            }

            if (header?.Column == null) return;

            var sortPath = GetCBSColumnSortPath(header.Column, headerName);
            if (!string.IsNullOrWhiteSpace(sortPath))
            {
                GetCBSSortLabels(sortPath, out var ascLabel, out var descLabel);

                var sortAsc = new MenuItem { Header = ascLabel };
                sortAsc.Click += (_, __) => ApplyCBSSort(sortPath, true);
                menu.Items.Add(sortAsc);

                var sortDesc = new MenuItem { Header = descLabel };
                sortDesc.Click += (_, __) => ApplyCBSSort(sortPath, false);
                menu.Items.Add(sortDesc);

                var clearSort = new MenuItem { Header = "Clear Sort" };
                clearSort.Click += (_, __) => ClearCBSSort();
                menu.Items.Add(clearSort);
            }

            if (!CBSFilterableHeaders.Contains(headerName))
            {
                header.ContextMenu = menu;
                menu.IsOpen = true;
                return;
            }

            menu.Items.Add(new Separator());
            var values = GetCBSHeaderFilterValues(headerName).ToList();
            var current = _cbsHeaderFilters.TryGetValue(headerName, out var curSet)
                ? new HashSet<string>(curSet, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var selectAll = new MenuItem { Header = "Select All" };
            selectAll.Click += (_, __) =>
            {
                _cbsHeaderFilters.Remove(headerName);
                ApplyCBSHeaderFilter();
            };
            menu.Items.Add(selectAll);

            var clear = new MenuItem { Header = "Clear Filter" };
            clear.Click += (_, __) =>
            {
                _cbsHeaderFilters.Remove(headerName);
                ApplyCBSHeaderFilter();
            };
            menu.Items.Add(clear);
            menu.Items.Add(new Separator());

            var searchBox = new TextBox { Width = 180, Height = 24, Margin = new Thickness(4, 2, 4, 4), ToolTip = "Search values..." };
            menu.Items.Add(new MenuItem { StaysOpenOnClick = true, Focusable = false, Header = searchBox });
            menu.Items.Add(new Separator());

            var valueItems = new List<(string Value, MenuItem Item)>();
            foreach (var val in values)
            {
                var item = new MenuItem { Header = val, IsCheckable = true, StaysOpenOnClick = true, IsChecked = current.Contains(val) };
                item.Click += (_, __) =>
                {
                    if (item.IsChecked) current.Add(val); else current.Remove(val);
                    if (current.Count == 0 || current.Count == values.Count) _cbsHeaderFilters.Remove(headerName);
                    else _cbsHeaderFilters[headerName] = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                    ApplyCBSHeaderFilter();
                };
                menu.Items.Add(item);
                valueItems.Add((val, item));
            }

            searchBox.TextChanged += (_, __) =>
            {
                var text = (searchBox.Text ?? string.Empty).Trim();
                foreach (var pair in valueItems)
                {
                    pair.Item.Visibility = string.IsNullOrWhiteSpace(text) || pair.Value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            };
            menu.Opened += (_, __) => searchBox.Focus();

            header.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private static string GetCBSColumnSortPath(DataGridColumn column, string headerName)
        {
            if (column is DataGridTextColumn textCol && textCol.Binding is Binding tb && !string.IsNullOrWhiteSpace(tb.Path?.Path))
            {
                return tb.Path.Path;
            }
            if (!string.IsNullOrWhiteSpace(column.SortMemberPath))
            {
                return column.SortMemberPath;
            }
            switch ((headerName ?? string.Empty).Trim())
            {
                case "CBS": return "CBS";
                case "Date": return "Date";
                case "A/C": return "AccountName";
                case "Particulars": return "Particulars";
                case "Remarks": return "Remarks";
                case "Bank Dr": return "BankDr";
                case "Bank Cr": return "BankCr";
                case "Cash Dr": return "CashDr";
                case "Cash Cr": return "CashCr";
                default: return string.Empty;
            }
        }

        private static void GetCBSSortLabels(string propName, out string ascLabel, out string descLabel)
        {
            var key = (propName ?? string.Empty).Trim().ToLowerInvariant();
            if (key == "date")
            {
                ascLabel = "Sort Oldest to Newest";
                descLabel = "Sort Newest to Oldest";
                return;
            }
            if (key == "bankdr" || key == "bankcr" || key == "cashdr" || key == "cashcr" || key == "sr")
            {
                ascLabel = "Sort Smallest to Largest";
                descLabel = "Sort Largest to Smallest";
                return;
            }
            ascLabel = "Sort A to Z";
            descLabel = "Sort Z to A";
        }

        private void ApplyCBSSort(string propName, bool ascending)
        {
            if (CBSGrid == null || string.IsNullOrWhiteSpace(propName)) return;
            _cbsSortColumn = propName;
            _cbsSortAscending = ascending;

            foreach (var col in CBSGrid.Columns)
            {
                col.Header = NormalizeHeaderForSort((col.Header ?? string.Empty).ToString());
                col.SortDirection = null;
            }

            var target = CBSGrid.Columns.FirstOrDefault(c => string.Equals(GetCBSColumnSortPath(c, NormalizeHeaderForSort((c.Header ?? string.Empty).ToString())), propName, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                target.SortDirection = ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
                target.Header = NormalizeHeaderForSort((target.Header ?? string.Empty).ToString()) + (ascending ? " \u25B2" : " \u25BC");
            }

            var cv = CollectionViewSource.GetDefaultView(CBSGrid.ItemsSource);
            if (cv != null)
            {
                cv.SortDescriptions.Clear();
                cv.SortDescriptions.Add(new SortDescription(propName, ascending ? ListSortDirection.Ascending : ListSortDirection.Descending));
                cv.Refresh();
            }
        }

        private void ClearCBSSort()
        {
            if (CBSGrid == null) return;
            _cbsSortColumn = string.Empty;
            _cbsSortAscending = true;
            foreach (var col in CBSGrid.Columns)
            {
                col.Header = NormalizeHeaderForSort((col.Header ?? string.Empty).ToString());
                col.SortDirection = null;
            }
            var cv = CollectionViewSource.GetDefaultView(CBSGrid.ItemsSource);
            if (cv != null)
            {
                cv.SortDescriptions.Clear();
                cv.Refresh();
            }
        }

        private IEnumerable<string> GetCBSHeaderFilterValues(string headerName)
        {
            var rows = (CBSGrid?.ItemsSource as IEnumerable<CashBankStatementEntry>) ?? Enumerable.Empty<CashBankStatementEntry>();
            return rows.Select(r => GetCBSHeaderCellValue(r, headerName))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v);
        }

        private static string GetCBSHeaderCellValue(CashBankStatementEntry row, string headerName)
        {
            if (row == null) return string.Empty;
            switch ((headerName ?? string.Empty).Trim())
            {
                case "CBS": return (row.CBS ?? string.Empty).Trim();
                case "Date": return row.Date.ToString("dd.MM.yy");
                case "A/C": return (row.AccountName ?? string.Empty).Trim();
                case "Particulars": return (row.Particulars ?? string.Empty).Trim();
                case "Remarks": return (row.Remarks ?? string.Empty).Trim();
                case "Bank Dr": return row.BankDr.ToString("N2");
                case "Bank Cr": return row.BankCr.ToString("N2");
                case "Cash Dr": return row.CashDr.ToString("N2");
                case "Cash Cr": return row.CashCr.ToString("N2");
                default: return string.Empty;
            }
        }

        private void ApplyCBSHeaderFilter()
        {
            if (CBSGrid == null) return;
            var cv = CollectionViewSource.GetDefaultView(CBSGrid.ItemsSource);
            if (cv == null) return;

            if (_cbsHeaderFilters.Count > 0)
            {
                cv.Filter = obj =>
                {
                    var row = obj as CashBankStatementEntry;
                    if (row == null) return false;
                    foreach (var kv in _cbsHeaderFilters)
                    {
                        if (kv.Value == null || kv.Value.Count == 0) continue;
                        var value = GetCBSHeaderCellValue(row, kv.Key);
                        if (!kv.Value.Contains(value)) return false;
                    }
                    return true;
                };
            }
            else
            {
                cv.Filter = null;
            }

            cv.Refresh();
            ApplyCBSHeaderFilterIndicators();
            UpdateCBSFooter();
        }

        private void ApplyCBSHeaderFilterIndicators()
        {
            if (CBSGrid == null) return;
            if (_cbsFilteredHeaderStyle == null)
            {
                var baseStyle = FindResource("DataGridHeaderStyle") as Style;
                _cbsFilteredHeaderStyle = new Style(typeof(DataGridColumnHeader), baseStyle);
                _cbsFilteredHeaderStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65100"))));
                _cbsFilteredHeaderStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            }

            foreach (var col in CBSGrid.Columns)
            {
                var headerName = NormalizeHeaderForSort((col.Header ?? string.Empty).ToString()).Trim();
                var hasFilter = _cbsHeaderFilters.TryGetValue(headerName, out var selectedSet) && selectedSet != null && selectedSet.Count > 0;
                col.HeaderStyle = hasFilter ? _cbsFilteredHeaderStyle : null;
            }
        }

        private void ReapplyCBSViewState()
        {
            if (CBSGrid == null) return;
            var cv = CollectionViewSource.GetDefaultView(CBSGrid.ItemsSource);
            if (cv == null)
            {
                UpdateCBSFooter();
                return;
            }

            cv.SortDescriptions.Clear();
            if (!string.IsNullOrWhiteSpace(_cbsSortColumn))
            {
                cv.SortDescriptions.Add(new SortDescription(_cbsSortColumn, _cbsSortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending));
            }
            ApplyCBSHeaderFilter();
            if (_cbsHeaderFilters.Count == 0)
            {
                cv.Refresh();
                ApplyCBSHeaderFilterIndicators();
                UpdateCBSFooter();
            }

            foreach (var col in CBSGrid.Columns)
            {
                col.Header = NormalizeHeaderForSort((col.Header ?? string.Empty).ToString());
                col.SortDirection = null;
            }
            if (!string.IsNullOrWhiteSpace(_cbsSortColumn))
            {
                var target = CBSGrid.Columns.FirstOrDefault(c => string.Equals(GetCBSColumnSortPath(c, NormalizeHeaderForSort((c.Header ?? string.Empty).ToString())), _cbsSortColumn, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    target.SortDirection = _cbsSortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;
                    target.Header = NormalizeHeaderForSort((target.Header ?? string.Empty).ToString()) + (_cbsSortAscending ? " \u25B2" : " \u25BC");
                }
            }
        }

        private class DueChallanOption
        {
            public int Id { get; set; }
            public string ChallanNo { get; set; }
            public string Broker { get; set; }
            public decimal LorryHire { get; set; }
            public decimal Advance { get; set; }
            public decimal BalancePaid { get; set; }
            public decimal Due { get; set; }
            public string Display => $"{ChallanNo} | LH ₹{LorryHire:N2} | Adv ₹{Advance:N2} | Due ₹{Due:N2}";
        }

        private List<string> LoadDueBrokers()
        {
            var list = new List<string>();
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT DISTINCT TRIM(COALESCE(BrokerName, '')) AS Broker
FROM Challans
WHERE (COALESCE(LorryHire,0) - COALESCE(LessTDS,0) - COALESCE(AdvanceAmount,0)
      + COALESCE(Detention,0) + COALESCE(Hamali,0) + COALESCE(Deduction,0)
      - COALESCE(BalancePaidNEFT,0) - COALESCE(BalancePaidCash,0)) > 0
  AND TRIM(COALESCE(BrokerName, '')) <> ''
ORDER BY Broker;";
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) list.Add((r["Broker"] as string ?? string.Empty).Trim());
                }
            }
            return list;
        }

        private List<DueChallanOption> LoadDueChallansByBroker(string broker)
        {
            var rows = new List<DueChallanOption>();
            var brokerKey = (broker ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(brokerKey)) return rows;
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT Id,
       ChallanNumber,
       BrokerName,
       COALESCE(LorryHire,0) AS LorryHireAmt,
       COALESCE(AdvanceAmount,0) AS AdvanceAmt,
       (COALESCE(BalancePaidNEFT,0) + COALESCE(BalancePaidCash,0)) AS PaidAmt,
       (COALESCE(LorryHire,0) - COALESCE(LessTDS,0) - COALESCE(AdvanceAmount,0)
       + COALESCE(Detention,0) + COALESCE(Hamali,0) + COALESCE(Deduction,0)
       - COALESCE(BalancePaidNEFT,0) - COALESCE(BalancePaidCash,0)) AS DueAmt
FROM Challans
WHERE TRIM(COALESCE(BrokerName,'')) = @broker
  AND (COALESCE(LorryHire,0) - COALESCE(LessTDS,0) - COALESCE(AdvanceAmount,0)
       + COALESCE(Detention,0) + COALESCE(Hamali,0) + COALESCE(Deduction,0)
       - COALESCE(BalancePaidNEFT,0) - COALESCE(BalancePaidCash,0)) > 0
ORDER BY Id DESC;";
                    cmd.Parameters.AddWithValue("@broker", brokerKey);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            rows.Add(new DueChallanOption
                            {
                                Id = Convert.ToInt32(r["Id"]),
                                ChallanNo = (r["ChallanNumber"] as string) ?? string.Empty,
                                Broker = (r["BrokerName"] as string) ?? string.Empty,
                                LorryHire = Convert.ToDecimal(r["LorryHireAmt"]),
                                Advance = Convert.ToDecimal(r["AdvanceAmt"]),
                                BalancePaid = Convert.ToDecimal(r["PaidAmt"]),
                                Due = Convert.ToDecimal(r["DueAmt"])
                            });
                }
            }
            return rows;
        }

        private void OpenChallanPay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var brokers = LoadDueBrokers();
                if (brokers.Count == 0)
                {
                    MessageBox.Show("No due challans available for payment.", "Pay Challan", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new Window
                {
                    Title = "Pay Challan (Broker-wise)",
                    Width = 560,
                    Height = 560,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    Background = System.Windows.Media.Brushes.White
                };

                var root = new Grid { Margin = new Thickness(14) };
                for (int i = 0; i < 9; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var brokerLabel = new TextBlock { Text = "Agent / Broker", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var brokerBox = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, Height = 28, Margin = new Thickness(0, 0, 0, 8), ItemsSource = brokers };
                Grid.SetRow(brokerLabel, 0); Grid.SetColumn(brokerLabel, 0); root.Children.Add(brokerLabel);
                Grid.SetRow(brokerBox, 0); Grid.SetColumn(brokerBox, 1); root.Children.Add(brokerBox);

                var challanLabel = new TextBlock { Text = "Due Challan", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var challanBox = new ComboBox { IsEditable = false, Height = 28, Margin = new Thickness(0, 0, 0, 8), DisplayMemberPath = "Display" };
                Grid.SetRow(challanLabel, 1); Grid.SetColumn(challanLabel, 0); root.Children.Add(challanLabel);
                Grid.SetRow(challanBox, 1); Grid.SetColumn(challanBox, 1); root.Children.Add(challanBox);

                var modeLabel = new TextBlock { Text = "Payment Type", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var modeBox = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
                modeBox.Items.Add("Pay Advance");
                modeBox.Items.Add("Pay Due");
                modeBox.SelectedIndex = 0;
                Grid.SetRow(modeLabel, 2); Grid.SetColumn(modeLabel, 0); root.Children.Add(modeLabel);
                Grid.SetRow(modeBox, 2); Grid.SetColumn(modeBox, 1); root.Children.Add(modeBox);

                var dueLabel = new TextBlock { Text = "Current Due", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var dueText = new TextBlock { Text = "₹ 0.00", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8) };
                Grid.SetRow(dueLabel, 3); Grid.SetColumn(dueLabel, 0); root.Children.Add(dueLabel);
                Grid.SetRow(dueText, 3); Grid.SetColumn(dueText, 1); root.Children.Add(dueText);

                var advNeftLabel = new TextBlock { Text = "Advance (NEFT)", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var advNeftBox = new TextBox { Height = 28, Text = "0", Margin = new Thickness(0, 0, 0, 8) };
                Grid.SetRow(advNeftLabel, 4); Grid.SetColumn(advNeftLabel, 0); root.Children.Add(advNeftLabel);
                Grid.SetRow(advNeftBox, 4); Grid.SetColumn(advNeftBox, 1); root.Children.Add(advNeftBox);

                var advCashLabel = new TextBlock { Text = "Advance (Cash)", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var advCashBox = new TextBox { Height = 28, Text = "0", Margin = new Thickness(0, 0, 0, 8) };
                Grid.SetRow(advCashLabel, 5); Grid.SetColumn(advCashLabel, 0); root.Children.Add(advCashLabel);
                Grid.SetRow(advCashBox, 5); Grid.SetColumn(advCashBox, 1); root.Children.Add(advCashBox);

                var advTotalLabel = new TextBlock { Text = "Advance Total", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var advTotalText = new TextBlock { Text = "₹ 0.00", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8) };
                Grid.SetRow(advTotalLabel, 6); Grid.SetColumn(advTotalLabel, 0); root.Children.Add(advTotalLabel);
                Grid.SetRow(advTotalText, 6); Grid.SetColumn(advTotalText, 1); root.Children.Add(advTotalText);

                var neftLabel = new TextBlock { Text = "Due Pay (NEFT)", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var neftBox = new TextBox { Height = 28, Text = "0", Margin = new Thickness(0, 0, 0, 8) };
                Grid.SetRow(neftLabel, 7); Grid.SetColumn(neftLabel, 0); root.Children.Add(neftLabel);
                Grid.SetRow(neftBox, 7); Grid.SetColumn(neftBox, 1); root.Children.Add(neftBox);

                var cashLabel = new TextBlock { Text = "Due Pay (Cash)", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var cashBox = new TextBox { Height = 28, Text = "0", Margin = new Thickness(0, 0, 0, 8) };
                Grid.SetRow(cashLabel, 8); Grid.SetColumn(cashLabel, 0); root.Children.Add(cashLabel);
                Grid.SetRow(cashBox, 8); Grid.SetColumn(cashBox, 1); root.Children.Add(cashBox);

                var paidToLabel = new TextBlock { Text = "Paid To", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var paidToBox = new TextBox { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
                Grid.SetRow(paidToLabel, 9); Grid.SetColumn(paidToLabel, 0); root.Children.Add(paidToLabel);
                Grid.SetRow(paidToBox, 9); Grid.SetColumn(paidToBox, 1); root.Children.Add(paidToBox);

                var remarksLabel = new TextBlock { Text = "Remarks", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Top };
                var remarksBox = new TextBox { Height = 56, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
                Grid.SetRow(remarksLabel, 10); Grid.SetColumn(remarksLabel, 0); root.Children.Add(remarksLabel);
                Grid.SetRow(remarksBox, 10); Grid.SetColumn(remarksBox, 1); root.Children.Add(remarksBox);

                var detailsText = new TextBlock
                {
                    Text = "Detention: ₹ 0.00    Hamali: ₹ 0.00    Deduction: ₹ 0.00    Less TDS: ₹ 0.00",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    Margin = new Thickness(0, 2, 0, 8)
                };
                var detailsExpander = new Expander
                {
                    Header = "+ Show Challan Details",
                    IsExpanded = false,
                    Content = detailsText
                };
                Grid.SetRow(detailsExpander, 11); Grid.SetColumnSpan(detailsExpander, 2); root.Children.Add(detailsExpander);

                var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
                var saveBtn = new Button { Content = "Save", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
                var cancelBtn = new Button { Content = "Cancel", Width = 90, Height = 30 };
                footer.Children.Add(saveBtn); footer.Children.Add(cancelBtn);
                Grid.SetRow(footer, 12); Grid.SetColumnSpan(footer, 2); root.Children.Add(footer);
                dialog.Content = root;

                MouseButtonEventHandler zeroOverwriteClick = (s, e2) =>
                {
                    if (!(s is TextBox tb)) return;
                    if (!tb.IsKeyboardFocusWithin)
                    {
                        e2.Handled = true;
                        tb.Focus();
                    }
                };
                KeyboardFocusChangedEventHandler zeroOverwriteFocus = (s, e2) =>
                {
                    if (s is TextBox tb) tb.SelectAll();
                };
                foreach (var tb in new[] { advNeftBox, advCashBox, neftBox, cashBox })
                {
                    tb.PreviewMouseLeftButtonDown += zeroOverwriteClick;
                    tb.GotKeyboardFocus += zeroOverwriteFocus;
                }

                Action refreshForBroker = () =>
                {
                    var broker = (brokerBox.Text ?? string.Empty).Trim();
                    var rows = LoadDueChallansByBroker(broker);
                    challanBox.ItemsSource = rows;
                    challanBox.SelectedIndex = rows.Count > 0 ? 0 : -1;
                    paidToBox.Text = broker;
                    dueText.Text = rows.Count > 0 ? $"₹ {rows[0].Due:N2}" : "₹ 0.00";
                };

                Action refreshAdvanceTotal = () =>
                {
                    var advNeft = ParseDecimal(advNeftBox.Text);
                    var advCash = ParseDecimal(advCashBox.Text);
                    advTotalText.Text = $"₹ {(advNeft + advCash):N2}";
                };
                Action refreshModeVisibility = () =>
                {
                    var isAdvance = string.Equals(modeBox.SelectedItem as string, "Pay Advance", StringComparison.OrdinalIgnoreCase);
                    var advanceVis = isAdvance ? Visibility.Visible : Visibility.Collapsed;
                    var dueVis = isAdvance ? Visibility.Collapsed : Visibility.Visible;
                    advNeftLabel.Visibility = advanceVis; advNeftBox.Visibility = advanceVis;
                    advCashLabel.Visibility = advanceVis; advCashBox.Visibility = advanceVis;
                    advTotalLabel.Visibility = advanceVis; advTotalText.Visibility = advanceVis;
                    neftLabel.Visibility = dueVis; neftBox.Visibility = dueVis;
                    cashLabel.Visibility = dueVis; cashBox.Visibility = dueVis;
                };

                brokerBox.SelectionChanged += (_, __) => refreshForBroker();
                brokerBox.LostFocus += (_, __) => refreshForBroker();
                challanBox.SelectionChanged += (_, __) =>
                {
                    var row = challanBox.SelectedItem as DueChallanOption;
                    dueText.Text = row != null ? $"₹ {row.Due:N2}" : "₹ 0.00";
                    if (row != null)
                    {
                        using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                        {
                            conn.Open();
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = @"SELECT COALESCE(Detention,0), COALESCE(Hamali,0), COALESCE(Deduction,0), COALESCE(LessTDS,0)
                                                    FROM Challans WHERE Id = @id;";
                                cmd.Parameters.AddWithValue("@id", row.Id);
                                using (var r = cmd.ExecuteReader())
                                {
                                    if (r.Read())
                                    {
                                        var detention = Convert.ToDecimal(r[0]);
                                        var hamali = Convert.ToDecimal(r[1]);
                                        var deduction = Convert.ToDecimal(r[2]);
                                        var lessTds = Convert.ToDecimal(r[3]);
                                        detailsText.Text = $"Detention: ₹ {detention:N2}    Hamali: ₹ {hamali:N2}    Deduction: ₹ {deduction:N2}    Less TDS: ₹ {lessTds:N2}";
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        detailsText.Text = "Detention: ₹ 0.00    Hamali: ₹ 0.00    Deduction: ₹ 0.00    Less TDS: ₹ 0.00";
                    }
                };
                advNeftBox.TextChanged += (_, __) => refreshAdvanceTotal();
                advCashBox.TextChanged += (_, __) => refreshAdvanceTotal();
                modeBox.SelectionChanged += (_, __) => refreshModeVisibility();
                cancelBtn.Click += (_, __) => { dialog.DialogResult = false; dialog.Close(); };
                saveBtn.Click += (_, __) =>
                {
                    var selected = challanBox.SelectedItem as DueChallanOption;
                    if (selected == null)
                    {
                        MessageBox.Show("Select a due challan.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    decimal advanceNeft = ParseDecimal(advNeftBox.Text);
                    decimal advanceCash = ParseDecimal(advCashBox.Text);
                    decimal advance = advanceNeft + advanceCash;
                    decimal neft = ParseDecimal(neftBox.Text);
                    decimal cash = ParseDecimal(cashBox.Text);
                    var isAdvanceMode = string.Equals(modeBox.SelectedItem as string, "Pay Advance", StringComparison.OrdinalIgnoreCase);
                    if (isAdvanceMode)
                    {
                        neft = 0m;
                        cash = 0m;
                    }
                    else
                    {
                        advanceNeft = 0m;
                        advanceCash = 0m;
                        advance = 0m;
                    }
                    if (advance <= 0m && neft <= 0m && cash <= 0m)
                    {
                        MessageBox.Show("Enter payment amount in at least one field.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"
UPDATE Challans
SET AdvanceAmount = COALESCE(AdvanceAmount,0) + @adv,
    AdvanceNEFT = COALESCE(AdvanceNEFT,0) + @advNeft,
    AdvanceCash = COALESCE(AdvanceCash,0) + @advCash,
    BalancePaidNEFT = COALESCE(BalancePaidNEFT,0) + @neft,
    BalancePaidCash = COALESCE(BalancePaidCash,0) + @cash,
    BalancePaidDate = @paidDate,
    PaidTo = CASE WHEN TRIM(COALESCE(@paidTo,'')) = '' THEN PaidTo ELSE @paidTo END,
    Remarks = CASE
                WHEN TRIM(COALESCE(@remarks,'')) = '' THEN Remarks
                WHEN TRIM(COALESCE(Remarks,'')) = '' THEN @remarks
                ELSE Remarks || ' | ' || @remarks
              END
WHERE Id = @id;";
                            cmd.Parameters.AddWithValue("@adv", advance);
                            cmd.Parameters.AddWithValue("@advNeft", advanceNeft);
                            cmd.Parameters.AddWithValue("@advCash", advanceCash);
                            cmd.Parameters.AddWithValue("@neft", neft);
                            cmd.Parameters.AddWithValue("@cash", cash);
                            cmd.Parameters.AddWithValue("@paidDate", DateTime.Today.ToString("yyyy-MM-dd"));
                            cmd.Parameters.AddWithValue("@paidTo", (paidToBox.Text ?? string.Empty).Trim());
                            cmd.Parameters.AddWithValue("@remarks", (remarksBox.Text ?? string.Empty).Trim());
                            cmd.Parameters.AddWithValue("@id", selected.Id);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    VM?.RefreshAfterDelete();
                    SyncAllChallanBillingFromLR(true);
                    UpdatePageUI();
                    RefreshDashboard();
                    dialog.DialogResult = true;
                    dialog.Close();
                };

                brokerBox.SelectedIndex = 0;
                refreshAdvanceTotal();
                refreshModeVisibility();
                refreshForBroker();
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open challan pay dialog: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // === Bill Ledger handlers ===
        private void BillSearchBox_TextChanged(object sender, TextChangedEventArgs e) { DebounceSearch(BillSearchBox); }
        private void BillRefresh_Click(object sender, RoutedEventArgs e) { if (BillSearchBox != null) BillSearchBox.Text = ""; _billHeaderFilters.Clear(); if (BillLedgerGrid != null) BillLedgerGrid.UnselectAllCells(); ApplyBillHeaderFilter(); }
        private void BillPrevPage_Click(object sender, RoutedEventArgs e) { BillVM?.GoToPreviousPage(); BillUpdatePageUI(); BillVM?.PreCacheNextPage(); }
        private void BillNextPage_Click(object sender, RoutedEventArgs e) { BillVM?.GoToNextPage(); BillUpdatePageUI(); BillVM?.PreCacheNextPage(); }
        private void BillFirstPage_Click(object sender, RoutedEventArgs e) { BillVM?.GoToFirstPage(); BillUpdatePageUI(); BillVM?.PreCacheNextPage(); }
        private void BillLastPage_Click(object sender, RoutedEventArgs e) { BillVM?.GoToLastPage(); BillUpdatePageUI(); BillVM?.PreCacheNextPage(); }
        private class PendingBillOption
        {
            public string BillNo { get; set; }
            public string Party { get; set; }
            public string LRNos { get; set; }
            public decimal Total { get; set; }
            public decimal RCVD { get; set; }
            public decimal TDS { get; set; }
            public decimal DED { get; set; }
            public decimal Due => Total - RCVD - TDS - DED;
            public string LRDisplay => $"{BillNo}  |  Due: ₹ {Due:N2}";
            public string BillWithDue => $"{BillNo}  |  LR: {LRNos}  |  Due: ₹ {Due:N2}";
        }

        private class PartyDueSummaryOption
        {
            public string Party { get; set; }
            public int Bills { get; set; }
            public decimal Due { get; set; }
        }

        private class BillDueDetailOption
        {
            public string BillNo { get; set; }
            public string LRNos { get; set; }
            public string From { get; set; }
            public string To { get; set; }
            public decimal Due { get; set; }
        }

        private class BillDueRow
        {
            public string Party { get; set; }
            public string BillNo { get; set; }
            public string LRNo { get; set; }
            public string From { get; set; }
            public string To { get; set; }
            public decimal Due { get; set; }
        }

        private List<BillDueRow> LoadBillDueRows()
        {
            var rows = new List<BillDueRow>();
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT
    COALESCE(NULLIF(TRIM(b.Party), ''), NULLIF(TRIM(lr.BillParty), ''), NULLIF(TRIM(lr.ConsignorName), ''), '') AS PartyName,
    COALESCE(TRIM(b.BillNo), '') AS BillNo,
    COALESCE(TRIM(b.LRNo), '') AS LRNo,
    COALESCE(NULLIF(TRIM(b.FromLoc), ''), NULLIF(TRIM(lr.FromLocation), ''), '') AS FromLoc,
    COALESCE(NULLIF(TRIM(b.ToLoc), ''), NULLIF(TRIM(lr.ToLocation), ''), '') AS ToLoc,
    COALESCE((b.Freight + b.Detention + b.HML + b.OTHR + b.StCharge) - (b.RCVD + b.TDS + b.DED), 0) AS DueAmt
FROM Bills b
LEFT JOIN LREntries lr ON lr.LRNo = b.LRNo
WHERE b.BillNo IS NOT NULL AND TRIM(b.BillNo) <> '';";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            rows.Add(new BillDueRow
                            {
                                Party = (r["PartyName"] as string ?? string.Empty).Trim(),
                                BillNo = (r["BillNo"] as string ?? string.Empty).Trim(),
                                LRNo = (r["LRNo"] as string ?? string.Empty).Trim(),
                                From = (r["FromLoc"] as string ?? string.Empty).Trim(),
                                To = (r["ToLoc"] as string ?? string.Empty).Trim(),
                                Due = Convert.ToDecimal(r["DueAmt"])
                            });
                        }
                    }
                }
            }
            return rows;
        }

        private List<PartyDueSummaryOption> LoadPartyDueSummaryOptions()
        {
            var rows = LoadBillDueRows().Where(x => x.Due > 0m);
            return rows
                .GroupBy(x => x.Party ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => new PartyDueSummaryOption
                {
                    Party = g.Key,
                    Bills = g.Select(x => x.BillNo).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Due = g.Sum(x => x.Due)
                })
                .OrderByDescending(x => x.Due)
                .ToList();
        }

        private List<BillDueDetailOption> LoadBillDueDetailsForParty(string party)
        {
            var rows = LoadBillDueRows()
                .Where(x => x.Due > 0m && string.Equals((x.Party ?? string.Empty).Trim(), (party ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

            return rows
                .GroupBy(x => x.BillNo, StringComparer.OrdinalIgnoreCase)
                .Select(g => new BillDueDetailOption
                {
                    BillNo = g.Key,
                    LRNos = string.Join(", ", g.Select(x => x.LRNo).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)),
                    From = string.Join(", ", g.Select(x => x.From).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)),
                    To = string.Join(", ", g.Select(x => x.To).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)),
                    Due = g.Sum(x => x.Due)
                })
                .OrderByDescending(x => x.Due)
                .ToList();
        }

        private List<PendingBillOption> LoadPendingBillOptions(string partyFilter = "", string billNoFilter = "")
        {
            var list = new List<PendingBillOption>();
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT b.BillNo,
       COALESCE(GROUP_CONCAT(DISTINCT b.LRNo), '') AS LRNos,
       COALESCE(
           NULLIF(TRIM(MAX(b.Party)), ''),
           NULLIF(TRIM(MAX(lr.BillParty)), ''),
           NULLIF(TRIM(MAX(lr.ConsignorName)), ''),
           ''
       ) AS Party,
       COALESCE(SUM(b.Freight + b.Detention + b.HML + b.OTHR + b.StCharge), 0) AS TotalAmt,
       COALESCE(SUM(b.RCVD), 0) AS RCVDAmt,
       COALESCE(SUM(b.TDS), 0) AS TDSAmt,
       COALESCE(SUM(b.DED), 0) AS DEDAmt
FROM Bills b
LEFT JOIN LREntries lr ON lr.LRNo = b.LRNo
WHERE b.BillNo IS NOT NULL AND TRIM(b.BillNo) <> ''
GROUP BY b.BillNo
HAVING COALESCE(SUM((b.Freight + b.Detention + b.HML + b.OTHR + b.StCharge) - (b.RCVD + b.TDS + b.DED)), 0) > 0
ORDER BY b.BillNo DESC;";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var item = new PendingBillOption
                            {
                                BillNo = r["BillNo"] as string,
                                LRNos = r["LRNos"] as string,
                                Party = r["Party"] as string,
                                Total = Convert.ToDecimal(r["TotalAmt"]),
                                RCVD = Convert.ToDecimal(r["RCVDAmt"]),
                                TDS = Convert.ToDecimal(r["TDSAmt"]),
                                DED = Convert.ToDecimal(r["DEDAmt"])
                            };
                            if (!string.IsNullOrWhiteSpace(partyFilter) && (item.Party ?? "").IndexOf(partyFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (!string.IsNullOrWhiteSpace(billNoFilter) && (item.BillNo ?? "").IndexOf(billNoFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            list.Add(item);
                        }
                    }
                }
            }
            return list;
        }

        private List<string> LoadPendingBillParties(string partyFilter = "")
        {
            var list = new List<string>();
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT
    COALESCE(
        NULLIF(TRIM(MAX(b.Party)), ''),
        NULLIF(TRIM(MAX(lr.BillParty)), ''),
        NULLIF(TRIM(MAX(lr.ConsignorName)), ''),
        ''
    ) AS PartyName,
    COALESCE(SUM((b.Freight + b.Detention + b.HML + b.OTHR + b.StCharge) - (b.RCVD + b.TDS + b.DED)), 0) AS DueAmt
FROM Bills b
LEFT JOIN LREntries lr ON lr.LRNo = b.LRNo
WHERE b.BillNo IS NOT NULL AND TRIM(b.BillNo) <> ''
GROUP BY b.BillNo
HAVING DueAmt > 0;";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var p = (r["PartyName"] as string ?? string.Empty).Trim();
                            if (string.IsNullOrWhiteSpace(p)) continue;
                            if (!string.IsNullOrWhiteSpace(partyFilter) && p.IndexOf(partyFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (!list.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
                            {
                                list.Add(p);
                            }
                        }
                    }
                }
            }
            return list.OrderBy(x => x).ToList();
        }

        private void OpenReceiveBillAmount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MouseButtonEventHandler overwriteZeroOnClick = (s, e2) =>
                {
                    if (!(s is TextBox tb)) return;
                    if (!tb.IsKeyboardFocusWithin)
                    {
                        e2.Handled = true;
                        tb.Focus();
                    }
                };
                KeyboardFocusChangedEventHandler overwriteZeroOnFocus = (s, e2) =>
                {
                    if (s is TextBox tb) tb.SelectAll();
                };

                var dialog = new Window
                {
                    Title = "Receive Bill Amount",
                    Width = 560,
                    Height = 520,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    Background = System.Windows.Media.Brushes.White
                };

                var root = new Grid { Margin = new Thickness(14) };
                for (int i = 0; i < 11; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var partyLabel = new TextBlock { Text = "Party Search", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var partyBox = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, Height = 28, Margin = new Thickness(0, 0, 0, 8) };
                Grid.SetRow(partyLabel, 0); Grid.SetColumn(partyLabel, 0);
                Grid.SetRow(partyBox, 0); Grid.SetColumn(partyBox, 1);
                root.Children.Add(partyLabel); root.Children.Add(partyBox);

                var billLabel = new TextBlock { Text = "Bill Number", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var billBox = new ComboBox { IsEditable = true, Height = 28, Margin = new Thickness(0, 0, 0, 8), DisplayMemberPath = "LRDisplay" };
                Grid.SetRow(billLabel, 1); Grid.SetColumn(billLabel, 0);
                Grid.SetRow(billBox, 1); Grid.SetColumn(billBox, 1);
                root.Children.Add(billLabel); root.Children.Add(billBox);

                var totalLabel = new TextBlock { Text = "Total Due", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var totalText = new TextBlock { Text = "0.00", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8) };
                Grid.SetRow(totalLabel, 2); Grid.SetColumn(totalLabel, 0);
                Grid.SetRow(totalText, 2); Grid.SetColumn(totalText, 1);
                root.Children.Add(totalLabel); root.Children.Add(totalText);

                var rcvdLabel = new TextBlock { Text = "Received", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var rcvdBox = new TextBox { Height = 28, Text = "0", Margin = new Thickness(0, 0, 0, 8) };
                rcvdBox.PreviewMouseLeftButtonDown += overwriteZeroOnClick;
                rcvdBox.GotKeyboardFocus += overwriteZeroOnFocus;
                Grid.SetRow(rcvdLabel, 3); Grid.SetColumn(rcvdLabel, 0);
                Grid.SetRow(rcvdBox, 3); Grid.SetColumn(rcvdBox, 1);
                root.Children.Add(rcvdLabel); root.Children.Add(rcvdBox);

                var tdsLabel = new TextBlock { Text = "TDS", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var tdsBox = new TextBox { Height = 28, Text = "0", Margin = new Thickness(0, 0, 0, 8) };
                tdsBox.PreviewMouseLeftButtonDown += overwriteZeroOnClick;
                tdsBox.GotKeyboardFocus += overwriteZeroOnFocus;
                Grid.SetRow(tdsLabel, 4); Grid.SetColumn(tdsLabel, 0);
                Grid.SetRow(tdsBox, 4); Grid.SetColumn(tdsBox, 1);
                root.Children.Add(tdsLabel); root.Children.Add(tdsBox);

                var dedLabel = new TextBlock { Text = "Deduction", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var dedBox = new TextBox { Height = 28, Text = "0", Margin = new Thickness(0, 0, 0, 8) };
                dedBox.PreviewMouseLeftButtonDown += overwriteZeroOnClick;
                dedBox.GotKeyboardFocus += overwriteZeroOnFocus;
                Grid.SetRow(dedLabel, 5); Grid.SetColumn(dedLabel, 0);
                Grid.SetRow(dedBox, 5); Grid.SetColumn(dedBox, 1);
                root.Children.Add(dedLabel); root.Children.Add(dedBox);

                var dueLabel = new TextBlock { Text = "Due After Entry", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var dueText = new TextBlock { Text = "0.00", FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.DarkRed, Margin = new Thickness(0, 4, 0, 8) };
                Grid.SetRow(dueLabel, 6); Grid.SetColumn(dueLabel, 0);
                Grid.SetRow(dueText, 6); Grid.SetColumn(dueText, 1);
                root.Children.Add(dueLabel); root.Children.Add(dueText);

                var mopLabel = new TextBlock { Text = "MOP", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var mopBox = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
                mopBox.Items.Add("NEFT");
                mopBox.Items.Add("CASH");
                mopBox.Items.Add("OTHER");
                mopBox.SelectedIndex = 0;
                Grid.SetRow(mopLabel, 7); Grid.SetColumn(mopLabel, 0);
                Grid.SetRow(mopBox, 7); Grid.SetColumn(mopBox, 1);
                root.Children.Add(mopLabel); root.Children.Add(mopBox);

                var mrLabel = new TextBlock { Text = "MR", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var mrBox = new TextBox { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
                Grid.SetRow(mrLabel, 8); Grid.SetColumn(mrLabel, 0);
                Grid.SetRow(mrBox, 8); Grid.SetColumn(mrBox, 1);
                root.Children.Add(mrLabel); root.Children.Add(mrBox);

                var recvDateLabel = new TextBlock { Text = "Received Date", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                var recvDate = new DatePicker { Height = 28, SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 0, 8) };
                Grid.SetRow(recvDateLabel, 9); Grid.SetColumn(recvDateLabel, 0);
                Grid.SetRow(recvDate, 9); Grid.SetColumn(recvDate, 1);
                root.Children.Add(recvDateLabel); root.Children.Add(recvDate);

                var remarksLabel = new TextBlock { Text = "Remarks (Other)", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Top };
                var remarksBox = new TextBox { Height = 56, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
                Grid.SetRow(remarksLabel, 10); Grid.SetColumn(remarksLabel, 0);
                Grid.SetRow(remarksBox, 10); Grid.SetColumn(remarksBox, 1);
                root.Children.Add(remarksLabel); root.Children.Add(remarksBox);

                var hint = new TextBlock { Text = "Only bills with due amount > 0 are shown.", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 2, 0, 0) };
                Grid.SetRow(hint, 11); Grid.SetColumnSpan(hint, 2);
                root.Children.Add(hint);

                var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
                var saveBtn = new Button { Content = "Save", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
                var cancelBtn = new Button { Content = "Cancel", Width = 90, Height = 30 };
                footer.Children.Add(saveBtn);
                footer.Children.Add(cancelBtn);
                Grid.SetRow(footer, 12); Grid.SetColumnSpan(footer, 2);
                root.Children.Add(footer);

                dialog.Content = root;

                List<PendingBillOption> options = new List<PendingBillOption>();
                string lastPartyKey = string.Empty;
                Action refreshPartySuggestions = () =>
                {
                    var typed = (partyBox.Text ?? string.Empty).Trim();
                    var parties = LoadPendingBillParties(typed);
                    partyBox.ItemsSource = parties;
                    partyBox.IsDropDownOpen = parties.Count > 0 && !string.IsNullOrWhiteSpace(typed);
                };
                Action refreshOptions = () =>
                {
                    var selectedParty = (partyBox.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(selectedParty))
                    {
                        lastPartyKey = string.Empty;
                        options = new List<PendingBillOption>();
                        billBox.ItemsSource = options;
                        billBox.SelectedItem = null;
                        billBox.Text = string.Empty;
                        totalText.Text = "0.00";
                        dueText.Text = "0.00";
                        return;
                    }

                    if (!string.Equals(lastPartyKey, selectedParty, StringComparison.OrdinalIgnoreCase))
                    {
                        billBox.SelectedItem = null;
                        billBox.Text = string.Empty;
                        lastPartyKey = selectedParty;
                    }

                    var selectedBillNo = (billBox.SelectedItem as PendingBillOption)?.BillNo;
                    if (string.IsNullOrWhiteSpace(selectedBillNo))
                    {
                        selectedBillNo = ExtractBillNoToken(billBox.Text);
                    }
                    options = LoadPendingBillOptions(selectedParty, selectedBillNo);
                    billBox.ItemsSource = options;
                    var chosen = options.FirstOrDefault(x => string.Equals(x.BillNo, selectedBillNo, StringComparison.OrdinalIgnoreCase));
                    if (chosen != null)
                    {
                        billBox.SelectedItem = chosen;
                    }
                };

                Action recomputeDue = () =>
                {
                    var selectedParty = (partyBox.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(selectedParty))
                    {
                        totalText.Text = "0.00";
                        dueText.Text = "0.00";
                        return;
                    }

                    var selected = billBox.SelectedItem as PendingBillOption;
                    decimal baseDue = selected?.Due ?? options.Sum(x => x.Due);
                    totalText.Text = baseDue.ToString("N2");
                    decimal rcvd = ParseDec(rcvdBox.Text);
                    decimal tds = ParseDec(tdsBox.Text);
                    decimal ded = ParseDec(dedBox.Text);
                    var due = baseDue - rcvd - tds - ded;
                    dueText.Text = due.ToString("N2");
                };

                partyBox.AddHandler(System.Windows.Controls.TextBox.TextChangedEvent, new TextChangedEventHandler((_, __) =>
                {
                    refreshPartySuggestions();
                    refreshOptions();
                    recomputeDue();
                }));
                partyBox.SelectionChanged += (_, __) =>
                {
                    refreshOptions();
                    recomputeDue();
                };
                billBox.SelectionChanged += (_, __) =>
                {
                    var selected = billBox.SelectedItem as PendingBillOption;
                    if (selected != null)
                    {
                        if (string.IsNullOrWhiteSpace(partyBox.Text)) partyBox.Text = selected.Party;
                    }
                    recomputeDue();
                };
                billBox.LostFocus += (_, __) =>
                {
                    var typed = ExtractBillNoToken(billBox.Text);
                    if (typed.Length == 0) return;
                    var found = options.FirstOrDefault(x => string.Equals(x.BillNo, typed, StringComparison.OrdinalIgnoreCase));
                    if (found != null) billBox.SelectedItem = found;
                    recomputeDue();
                };
                rcvdBox.TextChanged += (_, __) => recomputeDue();
                tdsBox.TextChanged += (_, __) => recomputeDue();
                dedBox.TextChanged += (_, __) => recomputeDue();
                mopBox.SelectionChanged += (_, __) => { };

                saveBtn.Click += (_, __) =>
                {
                    var selected = billBox.SelectedItem as PendingBillOption;
                    if (selected == null)
                    {
                        var typed = ExtractBillNoToken(billBox.Text);
                        selected = options.FirstOrDefault(x => string.Equals(x.BillNo, typed, StringComparison.OrdinalIgnoreCase));
                    }
                    if (selected == null)
                    {
                        MessageBox.Show("Select a valid pending bill number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    var rcvd = ParseDec(rcvdBox.Text);
                    var tds = ParseDec(tdsBox.Text);
                    var ded = ParseDec(dedBox.Text);
                    var mop = ((mopBox.SelectedItem as string) ?? "NEFT").Trim();
                    var mr = (mrBox.Text ?? string.Empty).Trim();
                    var recvDt = recvDate.SelectedDate ?? DateTime.Today;
                    if (rcvd < 0 || tds < 0 || ded < 0)
                    {
                        MessageBox.Show("Amounts cannot be negative.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    ApplyReceiveOnBill(selected.BillNo, rcvd, tds, ded, mop, mr, recvDt, remarksBox.Text ?? string.Empty);
                    BillVM?.RefreshAfterDelete();
                    BillUpdatePageUI();
                    LRVM?.RefreshAfterDelete();
                    LRUpdatePageUI();
                    RefreshDashboard();
                    dialog.DialogResult = true;
                    dialog.Close();
                };
                cancelBtn.Click += (_, __) => { dialog.DialogResult = false; dialog.Close(); };

                refreshPartySuggestions();
                refreshOptions();
                recomputeDue();
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open receive bill dialog: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenPartyBillSummary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Window
                {
                    Title = "Party-wise Bill Summary",
                    Width = 860,
                    Height = 560,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F5F7FB"))
                };

                var root = new Grid { Margin = new Thickness(14) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var headerCard = new Border
                {
                    Background = System.Windows.Media.Brushes.White,
                    BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D8E1EE")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10)
                };
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var title = new TextBlock { Text = "Party-wise Due Summary", FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                var backBtn = new Button
                {
                    Content = "Back",
                    Width = 90,
                    Height = 30,
                    Visibility = Visibility.Collapsed,
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1565C0")),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                header.Children.Add(title);
                Grid.SetColumn(backBtn, 1);
                header.Children.Add(backBtn);
                headerCard.Child = header;
                Grid.SetRow(headerCard, 0);
                root.Children.Add(headerCard);

                var partyGrid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    IsReadOnly = true,
                    CanUserAddRows = false,
                    Margin = new Thickness(0, 10, 0, 10),
                    Background = System.Windows.Media.Brushes.White,
                    BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D8E1EE")),
                    BorderThickness = new Thickness(1),
                    RowHeight = 30
                };
                partyGrid.Columns.Add(new DataGridTextColumn { Header = "Party Name", Binding = new Binding("Party"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                partyGrid.Columns.Add(new DataGridTextColumn { Header = "Bills", Binding = new Binding("Bills"), Width = 90 });
                partyGrid.Columns.Add(new DataGridTextColumn { Header = "Due Amount", Binding = new Binding("Due") { StringFormat = "₹ {0:N2}" }, Width = 160 });
                Grid.SetRow(partyGrid, 1);
                root.Children.Add(partyGrid);

                var billGrid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    IsReadOnly = true,
                    CanUserAddRows = false,
                    Margin = new Thickness(0, 10, 0, 10),
                    Visibility = Visibility.Collapsed,
                    Background = System.Windows.Media.Brushes.White,
                    BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D8E1EE")),
                    BorderThickness = new Thickness(1),
                    RowHeight = 30
                };
                billGrid.Columns.Add(new DataGridTextColumn { Header = "Bill No", Binding = new Binding("BillNo"), Width = 150 });
                billGrid.Columns.Add(new DataGridTextColumn { Header = "LR No(s)", Binding = new Binding("LRNos"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                billGrid.Columns.Add(new DataGridTextColumn { Header = "From", Binding = new Binding("From"), Width = 140 });
                billGrid.Columns.Add(new DataGridTextColumn { Header = "To", Binding = new Binding("To"), Width = 140 });
                billGrid.Columns.Add(new DataGridTextColumn { Header = "Due Amount", Binding = new Binding("Due") { StringFormat = "₹ {0:N2}" }, Width = 160 });
                Grid.SetRow(billGrid, 1);
                root.Children.Add(billGrid);

                var hint = new TextBlock
                {
                    Text = "Double-click party row to open bill details.",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    Margin = new Thickness(0, 6, 0, 0)
                };
                Grid.SetRow(hint, 2);
                root.Children.Add(hint);

                var partyData = LoadPartyDueSummaryOptions();
                partyGrid.ItemsSource = partyData;

                partyGrid.MouseDoubleClick += (_, __) =>
                {
                    var selected = partyGrid.SelectedItem as PartyDueSummaryOption;
                    if (selected == null) return;
                    var details = LoadBillDueDetailsForParty(selected.Party);
                    billGrid.ItemsSource = details;
                    partyGrid.Visibility = Visibility.Collapsed;
                    billGrid.Visibility = Visibility.Visible;
                    backBtn.Visibility = Visibility.Visible;
                    hint.Text = $"Bills with due for party: {selected.Party}";
                    title.Text = $"Bill-wise Due Details - {selected.Party}";
                    dialog.Title = $"Bill Details - {selected.Party}";
                };

                backBtn.Click += (_, __) =>
                {
                    billGrid.Visibility = Visibility.Collapsed;
                    partyGrid.Visibility = Visibility.Visible;
                    backBtn.Visibility = Visibility.Collapsed;
                    hint.Text = "Double-click party row to open bill details.";
                    title.Text = "Party-wise Due Summary";
                    dialog.Title = "Party-wise Bill Summary";
                };

                dialog.Content = root;
                dialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open party summary: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyReceiveOnBill(string billNo, decimal rcvd, decimal tds, decimal ded, string mop, string mr, DateTime receivedDate, string otherRemarks)
        {
            if (string.IsNullOrWhiteSpace(billNo)) return;
            var rows = new List<(int Id, decimal Total)>();
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"SELECT Id, COALESCE(Freight + Detention + HML + OTHR + StCharge,0) AS TotalAmt
                                        FROM Bills
                                        WHERE BillNo = @billNo
                                        ORDER BY Id;";
                    cmd.Parameters.AddWithValue("@billNo", billNo);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read()) rows.Add((Convert.ToInt32(r["Id"]), Convert.ToDecimal(r["TotalAmt"])));
                    }
                }
                if (rows.Count == 0) return;

                var totalBase = rows.Sum(x => x.Total);
                if (totalBase <= 0) totalBase = rows.Count;
                using (var tx = conn.BeginTransaction())
                {
                    decimal remR = rcvd, remT = tds, remD = ded;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var row = rows[i];
                        decimal partR, partT, partD;
                        if (i == rows.Count - 1)
                        {
                            partR = remR; partT = remT; partD = remD;
                        }
                        else
                        {
                            var ratio = (rows.Count == 1) ? 1m : (row.Total <= 0 ? (1m / rows.Count) : (row.Total / totalBase));
                            partR = Math.Round(rcvd * ratio, 2, MidpointRounding.AwayFromZero);
                            partT = Math.Round(tds * ratio, 2, MidpointRounding.AwayFromZero);
                            partD = Math.Round(ded * ratio, 2, MidpointRounding.AwayFromZero);
                            remR -= partR; remT -= partT; remD -= partD;
                        }

                        using (var upd = conn.CreateCommand())
                        {
                            upd.Transaction = tx;
                            upd.CommandText = @"UPDATE Bills
                                                SET RCVD = COALESCE(RCVD, 0) + @rcvd,
                                                    TDS = COALESCE(TDS, 0) + @tds,
                                                    DED = COALESCE(DED, 0) + @ded,
                                                    MOP = @mop,
                                                    MR = @mr,
                                                    Remarks = CASE
                                                                WHEN TRIM(COALESCE(@remarks,'')) = '' THEN Remarks
                                                                WHEN TRIM(COALESCE(Remarks,'')) = '' THEN @remarks
                                                                ELSE Remarks || ' | ' || @remarks
                                                              END,
                                                    Date = @dt
                                                WHERE Id = @id;";
                            upd.Parameters.AddWithValue("@rcvd", partR);
                            upd.Parameters.AddWithValue("@tds", partT);
                            upd.Parameters.AddWithValue("@ded", partD);
                            upd.Parameters.AddWithValue("@mop", (object)mop ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@mr", (object)mr ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@remarks", (otherRemarks ?? string.Empty).Trim());
                            upd.Parameters.AddWithValue("@dt", receivedDate.ToString("o"));
                            upd.Parameters.AddWithValue("@id", row.Id);
                            upd.ExecuteNonQuery();
                        }
                    }
                    tx.Commit();
                }
            }
        }

        private static decimal ParseDec(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0m;
            var t = text.Replace("₹", "").Replace(",", "").Trim();
            return decimal.TryParse(t, out var v) ? v : 0m;
        }

        private static string ExtractBillNoToken(string text)
        {
            var raw = (text ?? string.Empty).Trim();
            if (raw.Length == 0) return string.Empty;
            var idx = raw.IndexOf('|');
            if (idx > 0) raw = raw.Substring(0, idx).Trim();
            return raw;
        }

        private void BillUpdatePageUI()
        {
            if (BillVM == null) return;
            if (BillRecordCountText != null) BillRecordCountText.Text = $"Records: {BillVM.FilteredEntriesCount}";
            if (_billHeaderFilters.Count > 0) ApplyBillHeaderFilter();
        }

        private static readonly HashSet<string> BillFilterableHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BILL NO.", "BILL DATE", "PARTY", "LR NO.", "LR DATE", "FROM", "TO", "Vehicle Type",
            "FREIGHT", "DETENTION", "HML", "OTHR", "ST. CHARGE", "TOTAL", "RCVD", "TDS", "DED", "DUE", "MOP", "MR", "RECEIVED DATE", "REMARKS"
        };

        private IEnumerable<string> GetBillHeaderFilterValues(string headerName)
        {
            var rows = (BillVM?.PagedEntries ?? new System.Collections.ObjectModel.ObservableCollection<BillEntry>()).Where(x => x != null);
            return rows.Select(x => GetBillHeaderCellValue(x, headerName))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x);
        }

        private string GetBillHeaderCellValue(BillEntry entry, string headerName)
        {
            if (entry == null) return string.Empty;
            switch (headerName)
            {
                case "BILL NO.": return (entry.BillNoDisplay ?? entry.BillNo ?? string.Empty).Trim();
                case "BILL DATE": return entry.BillDate.ToString("dd-MMM-yyyy");
                case "PARTY": return (entry.Party ?? string.Empty).Trim();
                case "LR NO.": return (entry.LRNo ?? string.Empty).Trim();
                case "LR DATE": return entry.LRDate.HasValue ? entry.LRDate.Value.ToString("dd-MMM-yyyy") : string.Empty;
                case "FROM": return (entry.From ?? string.Empty).Trim();
                case "TO": return (entry.To ?? string.Empty).Trim();
                case "Vehicle Type": return (entry.VehicleType ?? string.Empty).Trim();
                case "FREIGHT": return entry.Freight.ToString("N2");
                case "DETENTION": return entry.Detention.ToString("N2");
                case "HML": return entry.HML.ToString("N2");
                case "OTHR": return entry.OTHR.ToString("N2");
                case "ST. CHARGE": return entry.StCharge.ToString("N2");
                case "TOTAL": return entry.Total.ToString("N2");
                case "RCVD": return entry.RCVD.ToString("N2");
                case "TDS": return entry.TDS.ToString("N2");
                case "DED": return entry.DED.ToString("N2");
                case "DUE": return entry.Due.ToString("N2");
                case "MOP": return (entry.MOP ?? string.Empty).Trim();
                case "MR": return (entry.MR ?? string.Empty).Trim();
                case "RECEIVED DATE": return entry.Date.ToString("dd-MMM-yyyy");
                case "REMARKS": return (entry.Remarks ?? string.Empty).Trim();
                default: return string.Empty;
            }
        }

        private void ApplyBillHeaderFilter()
        {
            if (BillLedgerGrid == null) return;
            var cv = CollectionViewSource.GetDefaultView(BillLedgerGrid.ItemsSource);
            if (cv == null) return;

            if (_billHeaderFilters.Count > 0)
            {
                cv.Filter = obj =>
                {
                    var entry = obj as BillEntry;
                    if (entry == null) return false;
                    foreach (var kv in _billHeaderFilters)
                    {
                        if (kv.Value == null || kv.Value.Count == 0) continue;
                        var value = GetBillHeaderCellValue(entry, kv.Key);
                        if (!kv.Value.Contains(value)) return false;
                    }
                    return true;
                };
            }
            else cv.Filter = null;

            cv.Refresh();
            ApplyBillHeaderFilterIndicators();
            UpdateBillVisibleSummaryFromView(cv);
        }

        private void ApplyBillHeaderFilterIndicators()
        {
            if (BillLedgerGrid == null) return;
            if (_billFilteredHeaderStyle == null)
            {
                var baseStyle = FindResource("DataGridHeaderStyle") as Style;
                _billFilteredHeaderStyle = new Style(typeof(DataGridColumnHeader), baseStyle);
                _billFilteredHeaderStyle.Setters.Add(new Setter(Control.BackgroundProperty, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E65100"))));
                _billFilteredHeaderStyle.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            }
            foreach (var col in BillLedgerGrid.Columns)
            {
                var headerName = NormalizeHeaderForSort((col.Header ?? string.Empty).ToString()).Trim();
                var hasFilter = _billHeaderFilters.TryGetValue(headerName, out var selectedSet) && selectedSet != null && selectedSet.Count > 0;
                col.HeaderStyle = hasFilter ? _billFilteredHeaderStyle : null;
            }
        }

        private void UpdateBillVisibleSummaryFromView(ICollectionView cv)
        {
            if (BillVM == null || cv == null) return;
            var visible = cv.Cast<object>().OfType<BillEntry>().ToList();
            BillVM.FilteredEntriesCount = visible.Count;
            BillVM.FilteredTotalDue = visible.Sum(x => x.Due);
            if (BillRecordCountText != null) BillRecordCountText.Text = $"Records: {BillVM.FilteredEntriesCount}";
        }

        private void BillLedgerGrid_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var cv = System.Windows.Data.CollectionViewSource.GetDefaultView(BillLedgerGrid.ItemsSource);
                if (cv != null && cv.CanGroup && cv.GroupDescriptions.Count == 0)
                    cv.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("BillNo"));
            }
            catch { }
        }

        private void BillLedgerGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            var col = e.Column;
            string propName = (col as DataGridTextColumn)?.Binding is System.Windows.Data.Binding b ? b.Path?.Path : (col as DataGridTemplateColumn)?.SortMemberPath;
            if (string.IsNullOrEmpty(propName) || col.CanUserSort == false) return;
            bool sameColumn = BillVM?.GetSortColumn() == propName;
            bool ascending = !sameColumn || !(BillVM?.IsCurrentSortAscending ?? true);
            foreach (var c in BillLedgerGrid.Columns)
            {
                string h = (c.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", "");
                c.Header = h; c.SortDirection = null;
            }
            col.SortDirection = ascending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending;
            col.Header = col.Header?.ToString() + (ascending ? " ▲" : " ▼");
            BillVM?.SetSort(propName, ascending);
        }

        private void BillLedgerGrid_SortingMenuOnly(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
        }

        private void BillLedgerGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInColumnHeaderResizeGripper(e.OriginalSource as DependencyObject)) return;
            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridColumnHeader)) source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            var header = source as DataGridColumnHeader;
            if (header?.Column == null || BillLedgerGrid == null) return;

            e.Handled = true;
            bool appendMode = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            _billHeaderDragSelecting = true;
            _billDragStartDisplayIndex = header.Column.DisplayIndex;
            _billDragAppendMode = appendMode;

            SelectBillColumnsByDisplayRange(_billDragStartDisplayIndex, _billDragStartDisplayIndex, appendMode);
        }

        private void BillLedgerGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_billHeaderDragSelecting || BillLedgerGrid == null || e.LeftButton != MouseButtonState.Pressed) return;

            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridColumnHeader)) source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            var header = source as DataGridColumnHeader;
            if (header?.Column == null || _billDragStartDisplayIndex < 0) return;

            SelectBillColumnsByDisplayRange(_billDragStartDisplayIndex, header.Column.DisplayIndex, _billDragAppendMode);
            e.Handled = true;
        }

        private void BillLedgerGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _billHeaderDragSelecting = false;
            _billDragStartDisplayIndex = -1;
            _billDragAppendMode = false;
        }

        private void SelectBillColumnsByDisplayRange(int startDisplayIndex, int endDisplayIndex, bool appendMode)
        {
            if (BillLedgerGrid == null) return;
            int lo = Math.Min(startDisplayIndex, endDisplayIndex);
            int hi = Math.Max(startDisplayIndex, endDisplayIndex);

            if (!appendMode) BillLedgerGrid.UnselectAllCells();

            var cols = BillLedgerGrid.Columns
                .Where(c => c.DisplayIndex >= lo && c.DisplayIndex <= hi)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            foreach (var item in BillLedgerGrid.Items)
            {
                if (item == CollectionView.NewItemPlaceholder) continue;
                foreach (var col in cols)
                {
                    var info = new DataGridCellInfo(item, col);
                    if (!BillLedgerGrid.SelectedCells.Contains(info)) BillLedgerGrid.SelectedCells.Add(info);
                }
            }
        }

        private void BillLedgerGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            DependencyObject walker = source;
            while (walker != null &&
                   !(walker is DataGridColumnHeader) &&
                   !(walker is DataGridCell) &&
                   !(walker is DataGridRowHeader) &&
                   !(walker is DataGridRow))
            {
                walker = System.Windows.Media.VisualTreeHelper.GetParent(walker);
            }
            var header = walker as DataGridColumnHeader;
            var cell = walker as DataGridCell;
            var rowHeader = walker as DataGridRowHeader;
            var row = walker as DataGridRow;
            if (header?.Column == null && cell == null && rowHeader == null && row == null) return;
            var headerName = header?.Column != null
                ? NormalizeHeaderForSort((header.Column.Header ?? string.Empty).ToString())
                : string.Empty;

            e.Handled = true;
            var menu = new ContextMenu();
            if (header?.Column != null)
            {
                var copyCol = new MenuItem { Header = "Copy Column" };
                var colRef = header.Column;
                copyCol.Click += (_, __) => CopyColumnFromGrid(BillLedgerGrid, colRef);
                menu.Items.Add(copyCol);
                menu.Items.Add(new Separator());
            }
            if (rowHeader != null || row != null)
            {
                if (row != null && row.Item != null && row.Item != CollectionView.NewItemPlaceholder)
                {
                    BillLedgerGrid.SelectedItems.Clear();
                    BillLedgerGrid.SelectedItems.Add(row.Item);
                }
                var copyRows = new MenuItem { Header = "Copy Row(s)" };
                copyRows.Click += (_, __) => CopySelectedRowsFromGrid(BillLedgerGrid);
                menu.Items.Add(copyRows);

                var selectedBill = (row?.Item ?? BillLedgerGrid.SelectedItem) as BillEntry;
                if (selectedBill != null)
                {
                    menu.Items.Add(new Separator());
                    var viewBillFormat = new MenuItem { Header = "View Bill Format" };
                    viewBillFormat.Click += (_, __) => OpenBillFormatPreview(selectedBill);
                    menu.Items.Add(viewBillFormat);
                }
                menu.IsOpen = true;
                return;
            }
            if (cell != null)
            {
                if (cell.DataContext != null) BillLedgerGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);
                var copyCell = new MenuItem { Header = "Copy Cell" };
                copyCell.Click += (_, __) => CopyCurrentCellFromGrid(BillLedgerGrid);
                menu.Items.Add(copyCell);

                var cellHeader = NormalizeHeaderForSort((cell.Column?.Header ?? string.Empty).ToString());
                if (string.Equals(cellHeader, "BILL NO.", StringComparison.OrdinalIgnoreCase))
                {
                    var billEntry = cell.DataContext as BillEntry;
                    if (billEntry != null)
                    {
                        var viewBillFormat = new MenuItem { Header = "View Bill Format" };
                        viewBillFormat.Click += (_, __) => OpenBillFormatPreview(billEntry);
                        menu.Items.Add(viewBillFormat);

                        var addComment = new MenuItem { Header = "View / Add Comment" };
                        addComment.Click += (_, __) => OpenBillCommentPopup(billEntry);
                        menu.Items.Add(addComment);
                    }
                }
                menu.IsOpen = true;
                return;
            }
            if (header?.Column == null) return;

            var sortPath = (header.Column as DataGridTextColumn)?.Binding is Binding sb
                ? sb.Path?.Path
                : (header.Column as DataGridTemplateColumn)?.SortMemberPath;
            if (string.IsNullOrWhiteSpace(sortPath)) return;

            GetBillSortLabels(sortPath, out var ascLabel, out var descLabel);

            var sortAsc = new MenuItem { Header = ascLabel };
            sortAsc.Click += (_, __) => ApplyBillSort(sortPath, true);
            menu.Items.Add(sortAsc);

            var sortDesc = new MenuItem { Header = descLabel };
            sortDesc.Click += (_, __) => ApplyBillSort(sortPath, false);
            menu.Items.Add(sortDesc);
            var clearSort = new MenuItem { Header = "Clear Sort" };
            clearSort.Click += (_, __) => ClearBillSort();
            menu.Items.Add(clearSort);

            if (!BillFilterableHeaders.Contains(headerName))
            {
                header.ContextMenu = menu;
                menu.IsOpen = true;
                return;
            }

            menu.Items.Add(new Separator());
            var values = GetBillHeaderFilterValues(headerName).ToList();
            var current = _billHeaderFilters.TryGetValue(headerName, out var curSet)
                ? new HashSet<string>(curSet, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var selectAll = new MenuItem { Header = "Select All" };
            selectAll.Click += (_, __) => { _billHeaderFilters.Remove(headerName); ApplyBillHeaderFilter(); };
            menu.Items.Add(selectAll);

            var clear = new MenuItem { Header = "Clear Filter" };
            clear.Click += (_, __) => { _billHeaderFilters.Remove(headerName); ApplyBillHeaderFilter(); };
            menu.Items.Add(clear);
            menu.Items.Add(new Separator());

            var searchBox = new TextBox { Width = 180, Height = 24, Margin = new Thickness(4, 2, 4, 4), ToolTip = "Search values..." };
            menu.Items.Add(new MenuItem { StaysOpenOnClick = true, Focusable = false, Header = searchBox });
            menu.Items.Add(new Separator());

            var valueItems = new List<(string Value, MenuItem Item)>();
            foreach (var v in values)
            {
                var item = new MenuItem { Header = v, IsCheckable = true, StaysOpenOnClick = true, IsChecked = current.Contains(v) };
                item.Click += (_, __) =>
                {
                    if (item.IsChecked) current.Add(v); else current.Remove(v);
                    if (current.Count == 0 || current.Count == values.Count) _billHeaderFilters.Remove(headerName);
                    else _billHeaderFilters[headerName] = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                    ApplyBillHeaderFilter();
                };
                menu.Items.Add(item);
                valueItems.Add((v, item));
            }

            searchBox.TextChanged += (_, __) =>
            {
                var text = (searchBox.Text ?? string.Empty).Trim();
                foreach (var pair in valueItems)
                    pair.Item.Visibility = string.IsNullOrWhiteSpace(text) || pair.Value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                        ? Visibility.Visible : Visibility.Collapsed;
            };
            menu.Opened += (_, __) => searchBox.Focus();

            header.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private static void GetBillSortLabels(string propName, out string ascLabel, out string descLabel)
        {
            var key = (propName ?? string.Empty).Trim().ToLowerInvariant();
            if (key == "billdate" || key == "lrdate" || key == "date")
            {
                ascLabel = "Sort Oldest to Newest";
                descLabel = "Sort Newest to Oldest";
                return;
            }
            if (key == "freight" || key == "detention" || key == "hml" || key == "othr" || key == "stcharge" || key == "total" ||
                key == "rcvd" || key == "tds" || key == "ded" || key == "due" || key == "sr")
            {
                ascLabel = "Sort Smallest to Largest";
                descLabel = "Sort Largest to Smallest";
                return;
            }
            ascLabel = "Sort A to Z";
            descLabel = "Sort Z to A";
        }

        private void ApplyBillSort(string propName, bool ascending)
        {
            if (BillLedgerGrid == null || BillVM == null || string.IsNullOrWhiteSpace(propName)) return;
            foreach (var c in BillLedgerGrid.Columns)
            {
                c.Header = NormalizeHeaderForSort(c.Header?.ToString() ?? string.Empty);
                c.SortDirection = null;
            }

            var target = BillLedgerGrid.Columns.FirstOrDefault(c =>
            {
                var p = (c as DataGridTextColumn)?.Binding is Binding b ? b.Path?.Path : (c as DataGridTemplateColumn)?.SortMemberPath;
                return string.Equals(p, propName, StringComparison.OrdinalIgnoreCase);
            });
            if (target != null)
            {
                target.SortDirection = ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
                target.Header = NormalizeHeaderForSort(target.Header?.ToString() ?? string.Empty) + (ascending ? " \u25B2" : " \u25BC");
            }
            BillVM.SetSort(propName, ascending);
            ApplyBillHeaderFilterIndicators();
        }

        private void ClearBillSort()
        {
            if (BillLedgerGrid == null || BillVM == null) return;
            foreach (var c in BillLedgerGrid.Columns)
            {
                c.Header = NormalizeHeaderForSort(c.Header?.ToString() ?? string.Empty);
                c.SortDirection = null;
            }
            BillVM.SetSort(string.Empty, true);
            ApplyBillHeaderFilterIndicators();
        }

        private void BillLedgerGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            var entry = e.Row.Item as BillEntry;
            if (entry == null || entry.Id <= 0) return;
            try
            {
                if (e.EditingElement is TextBox tb) { tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); }
                new BillRepository().Upsert(entry);
                SyncBillToLinkedLRs(entry);
                LRVM?.RefreshAfterDelete();
                LRUpdatePageUI();
                BillUpdatePageUI();
            }
            catch { }
        }

        private void SyncBillToLinkedLRs(BillEntry bill)
        {
            if (bill == null) return;
            var lrNos = (bill.LRNo ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (lrNos.Count == 0) return;

            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                var linked = new List<(int Id, string LRNo, decimal TotalFreight, decimal Hamali, decimal Detention, decimal Others, decimal StCharge, decimal NEFT, decimal CASH, decimal TDS, decimal Ded)>();
                using (var readCmd = conn.CreateCommand())
                {
                    var pNames = new List<string>();
                    for (int i = 0; i < lrNos.Count; i++)
                    {
                        var p = "@lr" + i;
                        pNames.Add(p);
                        readCmd.Parameters.AddWithValue(p, lrNos[i]);
                    }
                    readCmd.CommandText = $@"SELECT Id, LRNo, TotalFreight, Hamali, Detention, Others, StCharge, NEFT, CASH, TDS, Ded
                                             FROM LREntries
                                             WHERE LRNo IN ({string.Join(",", pNames)});";
                    using (var r = readCmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            linked.Add((
                                Convert.ToInt32(r["Id"]),
                                r["LRNo"] as string ?? string.Empty,
                                Convert.ToDecimal(r["TotalFreight"]),
                                Convert.ToDecimal(r["Hamali"]),
                                Convert.ToDecimal(r["Detention"]),
                                Convert.ToDecimal(r["Others"]),
                                Convert.ToDecimal(r["StCharge"]),
                                Convert.ToDecimal(r["NEFT"]),
                                Convert.ToDecimal(r["CASH"]),
                                Convert.ToDecimal(r["TDS"]),
                                Convert.ToDecimal(r["Ded"])
                            ));
                        }
                    }
                }
                if (linked.Count == 0) return;

                var currentFreight = linked.Sum(x => x.TotalFreight);
                var currentHamali = linked.Sum(x => x.Hamali);
                var currentDetention = linked.Sum(x => x.Detention);
                var currentOthers = linked.Sum(x => x.Others);
                var currentStCharge = linked.Sum(x => x.StCharge);
                var currentRcvd = linked.Sum(x => x.NEFT + x.CASH);
                var currentTds = linked.Sum(x => x.TDS);
                var currentDed = linked.Sum(x => x.Ded);

                // If bill values were manually edited, distribute those values across linked LR rows.
                // If bill values already match LR aggregate, keep LR amount rows unchanged.
                bool amountsChanged =
                    bill.Freight != currentFreight ||
                    bill.HML != currentHamali ||
                    bill.Detention != currentDetention ||
                    bill.OTHR != currentOthers ||
                    bill.StCharge != currentStCharge ||
                    bill.RCVD != currentRcvd ||
                    bill.TDS != currentTds ||
                    bill.DED != currentDed;

                using (var tx = conn.BeginTransaction())
                {
                    for (int i = 0; i < linked.Count; i++)
                    {
                        var row = linked[i];
                        var newFreight = row.TotalFreight;
                        var newHamali = row.Hamali;
                        var newDetention = row.Detention;
                        var newOthers = row.Others;
                        var newStCharge = row.StCharge;
                        var newNeft = row.NEFT;
                        var newCash = row.CASH;
                        var newTds = row.TDS;
                        var newDed = row.Ded;

                        if (amountsChanged)
                        {
                            int count = linked.Count;
                            decimal baseFreight = count == 1 ? bill.Freight : Math.Round(bill.Freight / count, 2, MidpointRounding.AwayFromZero);
                            decimal baseHamali = count == 1 ? bill.HML : Math.Round(bill.HML / count, 2, MidpointRounding.AwayFromZero);
                            decimal baseDetention = count == 1 ? bill.Detention : Math.Round(bill.Detention / count, 2, MidpointRounding.AwayFromZero);
                            decimal baseOthers = count == 1 ? bill.OTHR : Math.Round(bill.OTHR / count, 2, MidpointRounding.AwayFromZero);
                            decimal baseStCharge = count == 1 ? bill.StCharge : Math.Round(bill.StCharge / count, 2, MidpointRounding.AwayFromZero);
                            decimal baseRcvd = count == 1 ? bill.RCVD : Math.Round(bill.RCVD / count, 2, MidpointRounding.AwayFromZero);
                            decimal baseTds = count == 1 ? bill.TDS : Math.Round(bill.TDS / count, 2, MidpointRounding.AwayFromZero);
                            decimal baseDed = count == 1 ? bill.DED : Math.Round(bill.DED / count, 2, MidpointRounding.AwayFromZero);

                            newFreight = baseFreight;
                            newHamali = baseHamali;
                            newDetention = baseDetention;
                            newOthers = baseOthers;
                            newStCharge = baseStCharge;
                            newNeft = baseRcvd;
                            newCash = 0m;
                            newTds = baseTds;
                            newDed = baseDed;

                            if (i == linked.Count - 1 && linked.Count > 1)
                            {
                                newFreight = bill.Freight - linked.Take(linked.Count - 1).Sum(_ => baseFreight);
                                newHamali = bill.HML - linked.Take(linked.Count - 1).Sum(_ => baseHamali);
                                newDetention = bill.Detention - linked.Take(linked.Count - 1).Sum(_ => baseDetention);
                                newOthers = bill.OTHR - linked.Take(linked.Count - 1).Sum(_ => baseOthers);
                                newStCharge = bill.StCharge - linked.Take(linked.Count - 1).Sum(_ => baseStCharge);
                                newNeft = bill.RCVD - linked.Take(linked.Count - 1).Sum(_ => baseRcvd);
                                newTds = bill.TDS - linked.Take(linked.Count - 1).Sum(_ => baseTds);
                                newDed = bill.DED - linked.Take(linked.Count - 1).Sum(_ => baseDed);
                            }
                        }

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = @"
UPDATE LREntries SET
    TotalFreight = @freight,
    Hamali = @hamali,
    Detention = @detention,
    Others = @others,
    StCharge = @stCharge,
    NEFT = @neft,
    CASH = @cash,
    TDS = @tds,
    Ded = @ded,
    BillNo = @billNo,
    BillDate = @billDate,
    BILL = @billAmount,
    BillParty = @billParty
WHERE Id = @id;";
                            cmd.Parameters.AddWithValue("@freight", newFreight);
                            cmd.Parameters.AddWithValue("@hamali", newHamali);
                            cmd.Parameters.AddWithValue("@detention", newDetention);
                            cmd.Parameters.AddWithValue("@others", newOthers);
                            cmd.Parameters.AddWithValue("@stCharge", newStCharge);
                            cmd.Parameters.AddWithValue("@neft", newNeft);
                            cmd.Parameters.AddWithValue("@cash", newCash);
                            cmd.Parameters.AddWithValue("@tds", newTds);
                            cmd.Parameters.AddWithValue("@ded", newDed);
                            cmd.Parameters.AddWithValue("@billNo", (object)bill.BillNo ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@billDate", bill.BillDate.ToString("o"));
                            cmd.Parameters.AddWithValue("@billAmount", bill.Total);
                            cmd.Parameters.AddWithValue("@billParty", (object)bill.Party ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@id", row.Id);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    tx.Commit();
                }
            }
        }

        private ContextMenu _billColumnsMenu;
        private void ShowBillColumnsMenu_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is System.Windows.Controls.Button btn)) return;
            _billColumnsMenu = new ContextMenu();
            foreach (var col in BillLedgerGrid.Columns)
            {
                var h = (col.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", "");
                if (string.IsNullOrEmpty(h)) continue;
                var c = col;
                var item = new System.Windows.Controls.MenuItem { Header = h, IsCheckable = true, IsChecked = c.Visibility == Visibility.Visible, StaysOpenOnClick = true };
                item.Click += (_, __) => { c.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed; SaveBillColumnSettings(); };
                _billColumnsMenu.Items.Add(item);
            }
            _billColumnsMenu.PlacementTarget = btn;
            _billColumnsMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            _billColumnsMenu.IsOpen = true;
        }

        private static string BillSettingsPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Awagaman ERP", "bill_column_settings.json");
        private void SaveBillColumnSettings()
        {
            try
            {
                var lines = new List<string>();
                lines.Add($"_SortColumn:{BillVM?.GetSortColumn() ?? ""}");
                lines.Add($"_SortAscending:{BillVM?.IsCurrentSortAscending}");
                foreach (var col in BillLedgerGrid.Columns)
                {
                    var h = (col.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", "").Trim();
                    if (!string.IsNullOrEmpty(h)) lines.Add(col.Visibility == Visibility.Visible ? $"1:{h}:{(int)col.Width.DisplayValue}" : $"0:{h}:{(int)col.Width.DisplayValue}");
                }
                var dir = System.IO.Path.GetDirectoryName(BillSettingsPath);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(BillSettingsPath, string.Join("\n", lines));
            }
            catch { }
        }
        private void LoadBillColumnSettings()
        {
            try
            {
                var path = BillSettingsPath;
                if (!System.IO.File.Exists(path)) return;
                string sortCol = ""; bool sortAsc = true;
                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    if (line.StartsWith("_SortColumn:")) { sortCol = line.Substring("_SortColumn:".Length); continue; }
                    if (line.StartsWith("_SortAscending:")) { bool.TryParse(line.Substring("_SortAscending:".Length), out sortAsc); continue; }
                    var parts = line.Split(':');
                    if (parts.Length >= 2)
                    {
                        bool vis = parts[0] == "1";
                        var h = parts[1];
                        foreach (var col in BillLedgerGrid.Columns)
                        {
                            var ch = (col.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", "").Trim();
                            if (string.Equals(ch, h, StringComparison.OrdinalIgnoreCase))
                            {
                                col.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                                if (parts.Length >= 3 && double.TryParse(parts[2], out double w) && w > 10) col.Width = new DataGridLength(w);
                                break;
                            }
                        }
                    }
                }
                BillVM?.SetSort(sortCol, sortAsc);
            }
            catch { }
        }

        private void ImportBill_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "CSV Files|*.csv", Title = "Select Bill Import File" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var lines = System.IO.File.ReadAllLines(dialog.FileName);
                if (lines.Length < 2) { MessageBox.Show("CSV file has no data rows.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }
                var headers = SplitCsvLine(lines[0]);
                var colMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Length; i++)
                {
                    var key = headers[i].Trim().Replace(".", "").Replace(" ", "");
                    if (!colMap.ContainsKey(key)) colMap[key] = new List<int>();
                    colMap[key].Add(i);
                }
                var repo = new BillRepository();
                int imported = 0, errors = 0;
                var progress = ImportProgressBar;
                var status = ImportStatusText;
                if (progress != null) { progress.Visibility = Visibility.Visible; progress.Maximum = lines.Length - 1; progress.Value = 0; }
                if (status != null) status.Visibility = Visibility.Visible;

                for (int i = 1; i < lines.Length; i++)
                {
                    try
                    {
                        var parts = SplitCsvLine(lines[i]);
                        var entry = new BillEntry
                        {
                            Sr = i,
                            BillNo = GetCol(parts, colMap, "BillNo", 0) ?? GetCol(parts, colMap, "BILLNO"),
                            BillDate = ParseDate(GetCol(parts, colMap, "BillDate", 0) ?? GetCol(parts, colMap, "BILLDATE")),
                            Party = GetCol(parts, colMap, "Party", 0) ?? GetCol(parts, colMap, "PARTY"),
                            LRNo = GetCol(parts, colMap, "LRNo", 0) ?? GetCol(parts, colMap, "LRNO") ?? GetCol(parts, colMap, "LR"),
                            LRDate = ParseNullableDateCSV(GetCol(parts, colMap, "LRDate", 0) ?? GetCol(parts, colMap, "LRDATE")),
                            From = GetCol(parts, colMap, "From", 0) ?? GetCol(parts, colMap, "FROM"),
                            To = GetCol(parts, colMap, "To", 0) ?? GetCol(parts, colMap, "TO"),
                            VehicleType = GetCol(parts, colMap, "VehicleType", 0) ?? GetCol(parts, colMap, "VEHICLETYPE"),
                            Freight = ParseDecimal(GetCol(parts, colMap, "Freight", 0) ?? GetCol(parts, colMap, "FREIGHT")),
                            Detention = ParseDecimal(GetCol(parts, colMap, "Detention", 0) ?? GetCol(parts, colMap, "DETENTION")),
                            HML = ParseDecimal(GetCol(parts, colMap, "HML", 0) ?? GetCol(parts, colMap, "HML")),
                            OTHR = ParseDecimal(GetCol(parts, colMap, "OTHR", 0) ?? GetCol(parts, colMap, "OTHR")),
                            StCharge = ParseDecimal(GetCol(parts, colMap, "StCharge", 0) ?? GetCol(parts, colMap, "St. Charge")),
                            RCVD = ParseDecimal(GetCol(parts, colMap, "RCVD", 0) ?? GetCol(parts, colMap, "RCVD")),
                            TDS = ParseDecimal(GetCol(parts, colMap, "TDS", 0) ?? GetCol(parts, colMap, "TDS")),
                            DED = ParseDecimal(GetCol(parts, colMap, "DED", 0) ?? GetCol(parts, colMap, "DED")),
                            MOP = GetCol(parts, colMap, "MOP", 0) ?? GetCol(parts, colMap, "MOP"),
                            MR = GetCol(parts, colMap, "MR", 0) ?? GetCol(parts, colMap, "MR"),
                            Remarks = GetCol(parts, colMap, "Remarks", 0) ?? GetCol(parts, colMap, "REMARKS"),
                            Date = ParseDate(GetCol(parts, colMap, "Date", 0) ?? GetCol(parts, colMap, "DATE")),
                        };
                        repo.Upsert(entry);
                        imported++;
                    }
                    catch { errors++; }
                    if (progress != null) progress.Value = i;
                }
                if (progress != null) progress.Visibility = Visibility.Collapsed;
                if (status != null) status.Text = $"Imported: {imported}, Errors: {errors}";
                BillVM?.RefreshAfterDelete();
                BillUpdatePageUI();
                MessageBox.Show($"Import complete.\nImported: {imported}\nErrors: {errors}", "Import Result", MessageBoxButton.OK, imported > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex) { MessageBox.Show("Import failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private static DateTime? ParseNullableDateCSV(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTime.TryParse(value, out var dt) ? dt : (DateTime?)null;
        }

        private void PartySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_allParties == null) return;
            var filter = PartySearchBox.Text?.Trim().ToLower() ?? "";
            PartyGrid.ItemsSource = string.IsNullOrEmpty(filter)
                ? _allParties
                : _allParties.Where(p => (p.PartyName?.ToLower().Contains(filter) == true) || (p.Address?.ToLower().Contains(filter) == true) || (p.GSTNo?.ToLower().Contains(filter) == true)).ToList();
        }

        private static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') inQuotes = !inQuotes;
                else if ((c == ',' || c == '\t') && !inQuotes) { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }

        private static string GetCol(string[] parts, Dictionary<string, List<int>> map, string colName, int occurrence = 0)
        {
            if (map.TryGetValue(colName, out var indices) && occurrence < indices.Count && indices[occurrence] < parts.Length)
                return parts[indices[occurrence]];
            return null;
        }

        private static decimal ParseDecimal(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0m;
            value = value.Replace("₹", "").Replace(",", "").Replace(" ", "").Trim();
            return decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0m;
        }

        private static DateTime ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return DateTime.Today;
            if (DateTime.TryParse(value, out var dt)) return dt;
            if (int.TryParse(value, out int serial) && serial > 1) return new DateTime(1900, 1, 1).AddDays(serial - 2);
            return DateTime.Today;
        }

        private static DateTime? ParseNullableDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateTime.TryParse(value, out var dt)) return dt;
            if (int.TryParse(value, out int serial) && serial > 1) return new DateTime(1900, 1, 1).AddDays(serial - 2);
            return null;
        }

        private void SearchTimer_Tick(object sender, EventArgs e) { _searchTimer?.Stop(); if (_activeSearchBox == null) return; var box = _activeSearchBox; _activeSearchBox = null; string filter = box.Text?.ToLower().Trim() ?? ""; if (box == SearchBox) ApplyChallanSearch(filter); else if (box == LRSearchBox) ApplyLRSearch(filter); else if (box == TrackingSearchBox) ApplyTrackingSearch(filter); else if (box == BillSearchBox) ApplyBillSearch(filter); }
        private void ApplyBillSearch(string filter) { if (BillVM == null) return; BillVM.SetSearchFilter(filter); BillUpdatePageUI(); ApplyBillHeaderFilter(); }
        private void DebounceSearch(TextBox box) { if (_searchTimer == null) return; _searchTimer.Stop(); _activeSearchBox = box; _searchTimer.Start(); }
        private void ChallanFilterTimer_Tick(object sender, EventArgs e) { _challanFilterTimer?.Stop(); if (VM == null) return; VM.SetAdvancedFilters(ChallanFilterBox?.Text ?? "", LRFilterBox?.Text ?? "", FromFilterBox?.Text ?? "", ToFilterBox?.Text ?? ""); UpdatePageUI(); }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { DebounceSearch(SearchBox); }
        private void ChallanFilterBox_TextChanged(object sender, TextChangedEventArgs e) { _challanFilterTimer?.Stop(); _challanFilterTimer?.Start(); }
        private void LRFilterBox_TextChanged(object sender, TextChangedEventArgs e) { _challanFilterTimer?.Stop(); _challanFilterTimer?.Start(); }
        private void FromFilterBox_TextChanged(object sender, TextChangedEventArgs e) { _challanFilterTimer?.Stop(); _challanFilterTimer?.Start(); }
        private void ToFilterBox_TextChanged(object sender, TextChangedEventArgs e) { _challanFilterTimer?.Stop(); _challanFilterTimer?.Start(); }
        private void LRSearchBox_TextChanged(object sender, TextChangedEventArgs e) { DebounceSearch(LRSearchBox); }
        private void TrackingSearchBox_TextChanged(object sender, TextChangedEventArgs e) { DebounceSearch(TrackingSearchBox); }
        private void ApplyChallanSearch(string filter) { if (VM == null) return; VM.SetSearchFilter(filter); UpdatePageUI(); }
        private void ApplyLRSearch(string filter) { if (LRVM == null) return; LRVM.SetSearchFilter(filter); LRUpdatePageUI(); ApplyLRHeaderFilter(); }
        private void ApplyTrackingSearch(string filter) { if (TrackingVM == null || TrackingLedgerGrid == null) return; var cv = CollectionViewSource.GetDefaultView(TrackingLedgerGrid.ItemsSource); if (cv == null) return; if (string.IsNullOrEmpty(filter)) cv.Filter = null; else cv.Filter = (obj) => { if (obj is TrackingEntry te) { if (te.ChallanNo?.ToLower().Contains(filter) == true || te.VehicleNo?.ToLower().Contains(filter) == true || te.From?.ToLower().Contains(filter) == true || te.To?.ToLower().Contains(filter) == true || te.DriverMobile?.ToLower().Contains(filter) == true || te.Status?.ToLower().Contains(filter) == true) return true; } return false; }; }
        private void StatusFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { ApplyTrackingFilter(); }
        private void ApplyTrackingFilter() { if (TrackingVM == null || TrackingLedgerGrid == null) return; var cv = CollectionViewSource.GetDefaultView(TrackingLedgerGrid.ItemsSource); if (cv == null) return; string search = TrackingSearchBox.Text?.ToLower().Trim() ?? ""; string status = (StatusFilterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString(); bool filterByStatus = !string.IsNullOrEmpty(status) && status != "All Status"; bool filterBySearch = !string.IsNullOrEmpty(search); if (!filterByStatus && !filterBySearch) { cv.Filter = null; TrackingVM.FilteredEntriesCount = TrackingVM.Entries.Count; return; } cv.Filter = (obj) => { if (obj is TrackingEntry te) { if (filterByStatus && te.Status != status) return false; if (filterBySearch && (te.ChallanNo?.ToLower().Contains(search) == true || te.VehicleNo?.ToLower().Contains(search) == true || te.From?.ToLower().Contains(search) == true || te.To?.ToLower().Contains(search) == true || te.DriverMobile?.ToLower().Contains(search) == true || te.Status?.ToLower().Contains(search) == true)) return true; return !filterBySearch; } return false; }; }
        private void RestoreSortIndicator() { if (VM == null || LedgerGrid == null) return; string colName = VM.GetSortColumn(); if (string.IsNullOrEmpty(colName)) return; foreach (var c in LedgerGrid.Columns) { string prop = (c as DataGridTextColumn)?.Binding is System.Windows.Data.Binding b ? b.Path?.Path : (c as DataGridTemplateColumn)?.SortMemberPath; if (string.IsNullOrEmpty(prop)) continue; string header = (c.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", ""); if (prop.Equals(colName, StringComparison.OrdinalIgnoreCase)) { c.SortDirection = VM.IsCurrentSortAscending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending; c.Header = header + (VM.IsCurrentSortAscending ? " ▲" : " ▼"); } else { c.Header = header; c.SortDirection = null; } } }
        private void UpdatePageUI() { if (VM == null) return; if (RecordCountText != null) RecordCountText.Text = $"Records: {VM.FilteredEntriesCount}"; RefreshFilteredSummary(); ApplyChallanDueFilter(); }
        private void RefreshFilteredSummary()
        {
            if (VM == null) return;
            if (LedgerGrid != null)
            {
                var cv = CollectionViewSource.GetDefaultView(LedgerGrid.ItemsSource);
                if (cv != null)
                {
                    var visible = cv.Cast<object>().OfType<ChallanEntry>().ToList();
                    VM.FilteredEntriesCount = visible.Count;
                    VM.FilteredTotalDue = visible.Sum(entry => entry?.Due ?? 0m);
                }
                else
                {
                    VM.FilteredEntriesCount = VM.PagedEntries.Count;
                    VM.FilteredTotalDue = VM.PagedEntries.Sum(entry => entry?.Due ?? 0m);
                }
            }
            else
            {
                VM.FilteredEntriesCount = VM.PagedEntries.Count;
                VM.FilteredTotalDue = VM.PagedEntries.Sum(entry => entry?.Due ?? 0m);
            }
            if (SearchedTotalDueTextBlock != null) SearchedTotalDueTextBlock.Visibility = Visibility.Collapsed;
        }
        private void ApplyChallanDueFilter()
        {
            if (LedgerGrid == null) return;
            var cv = CollectionViewSource.GetDefaultView(LedgerGrid.ItemsSource);
            if (cv == null) return;
            bool hasHeaderFilter = _challanHeaderFilters.Count > 0;
            if (_onlyDueFilterEnabled || hasHeaderFilter)
            {
                cv.Filter = (obj) =>
                {
                    var ce = obj as ChallanEntry;
                    if (ce == null) return false;
                    if (_onlyDueFilterEnabled && ce.Due <= 0m) return false;

                    foreach (var kv in _challanHeaderFilters)
                    {
                        var selectedSet = kv.Value;
                        if (selectedSet == null || selectedSet.Count == 0) continue;
                        switch (NormalizeChallanHeaderName(kv.Key))
                        {
                            case "LR No.":
                                {
                                    var tokens = (ce.LRNumber ?? string.Empty)
                                        .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(t => t.Trim())
                                        .Where(t => !string.IsNullOrWhiteSpace(t));
                                    if (!tokens.Any(t => selectedSet.Contains(t))) return false;
                                }
                                break;
                            case "Agent/Broker":
                                if (!selectedSet.Contains((ce.BrokerName ?? string.Empty).Trim())) return false;
                                break;
                            case "From":
                                if (!selectedSet.Contains((ce.From ?? string.Empty).Trim())) return false;
                                break;
                            case "To":
                                if (!selectedSet.Contains((ce.To ?? string.Empty).Trim())) return false;
                                break;
                            case "Lorry Hire":
                                if (!selectedSet.Contains(ce.LorryHire.ToString("N2"))) return false;
                                break;
                            case "Balance":
                                if (!selectedSet.Contains(ce.Balance.ToString("N2"))) return false;
                                break;
                            case "Due":
                                if (!selectedSet.Contains(ce.Due.ToString("N2"))) return false;
                                break;
                        }
                    }

                    return true;
                };
            }
            else cv.Filter = null;
            cv.Refresh();
            ApplyChallanHeaderFilterIndicators();
            RefreshFilteredSummary();
            if (RecordCountText != null) RecordCountText.Text = $"Records: {VM?.FilteredEntriesCount ?? 0}";
        }
        private static readonly HashSet<string> ChallanFilterableHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "LR No.", "Agent/Broker", "From", "To", "Lorry Hire", "Balance", "Due" };

        private void LedgerGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInColumnHeaderResizeGripper(e.OriginalSource as DependencyObject)) return;
            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridColumnHeader)) source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            var header = source as DataGridColumnHeader;
            if (header?.Column == null || LedgerGrid == null) return;

            // Excel-like: single-click selects one column, Ctrl+click adds another column.
            e.Handled = true;
            bool appendMode = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            _challanHeaderDragSelecting = true;
            _challanDragStartDisplayIndex = header.Column.DisplayIndex;
            _challanDragAppendMode = appendMode;

            SelectChallanColumnsByDisplayRange(_challanDragStartDisplayIndex, _challanDragStartDisplayIndex, appendMode);
        }

        private void LedgerGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_challanHeaderDragSelecting || LedgerGrid == null || e.LeftButton != MouseButtonState.Pressed) return;

            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridColumnHeader)) source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            var header = source as DataGridColumnHeader;
            if (header?.Column == null || _challanDragStartDisplayIndex < 0) return;

            SelectChallanColumnsByDisplayRange(_challanDragStartDisplayIndex, header.Column.DisplayIndex, _challanDragAppendMode);
            e.Handled = true;
        }

        private void LedgerGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _challanHeaderDragSelecting = false;
            _challanDragStartDisplayIndex = -1;
            _challanDragAppendMode = false;
        }

        private void SelectChallanColumnsByDisplayRange(int startDisplayIndex, int endDisplayIndex, bool appendMode)
        {
            if (LedgerGrid == null) return;

            int lo = Math.Min(startDisplayIndex, endDisplayIndex);
            int hi = Math.Max(startDisplayIndex, endDisplayIndex);

            if (!appendMode) LedgerGrid.UnselectAllCells();

            var cols = LedgerGrid.Columns
                .Where(c => c.DisplayIndex >= lo && c.DisplayIndex <= hi)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            foreach (var item in LedgerGrid.Items)
            {
                if (item == CollectionView.NewItemPlaceholder) continue;
                foreach (var col in cols)
                {
                    var info = new DataGridCellInfo(item, col);
                    if (!LedgerGrid.SelectedCells.Contains(info)) LedgerGrid.SelectedCells.Add(info);
                }
            }
        }

        private void LedgerGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            DependencyObject walker = source;
            while (walker != null &&
                   !(walker is DataGridColumnHeader) &&
                   !(walker is DataGridCell) &&
                   !(walker is DataGridRowHeader) &&
                   !(walker is DataGridRow))
            {
                walker = System.Windows.Media.VisualTreeHelper.GetParent(walker);
            }
            var header = walker as DataGridColumnHeader;
            var cell = walker as DataGridCell;
            var rowHeader = walker as DataGridRowHeader;
            var row = walker as DataGridRow;
            if (header?.Column == null && cell == null && rowHeader == null && row == null) return;
            var headerName = header?.Column != null
                ? NormalizeChallanHeaderName((header.Column.Header ?? string.Empty).ToString())
                : string.Empty;

            e.Handled = true;
            var menu = new ContextMenu();

            if (header?.Column != null)
            {
                var copyCol = new MenuItem { Header = "Copy Column" };
                var colRef = header.Column;
                copyCol.Click += (_, __) => CopyColumnFromGrid(LedgerGrid, colRef);
                menu.Items.Add(copyCol);
                menu.Items.Add(new Separator());
            }

            if (rowHeader != null || row != null)
            {
                if (row != null && row.Item != null && row.Item != CollectionView.NewItemPlaceholder)
                {
                    LedgerGrid.SelectedItems.Clear();
                    LedgerGrid.SelectedItems.Add(row.Item);
                }
                var copyRows = new MenuItem { Header = "Copy Row(s)" };
                copyRows.Click += (_, __) => CopySelectedRowsFromGrid(LedgerGrid);
                menu.Items.Add(copyRows);
                menu.IsOpen = true;
                return;
            }

            if (cell != null)
            {
                if (cell.DataContext != null) LedgerGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);
                var copyCell = new MenuItem { Header = "Copy Cell" };
                copyCell.Click += (_, __) => CopyCurrentCellFromGrid(LedgerGrid);
                menu.Items.Add(copyCell);

                var cellHeader = NormalizeChallanHeaderName((cell.Column?.Header ?? string.Empty).ToString());
                if (string.Equals(cellHeader, "Challan No", StringComparison.OrdinalIgnoreCase))
                {
                    var challanEntry = cell.DataContext as ChallanEntry;
                    if (challanEntry != null)
                    {
                        var addComment = new MenuItem { Header = "View / Add Comment" };
                        addComment.Click += (_, __) => OpenChallanCommentPopup(challanEntry);
                        menu.Items.Add(addComment);
                    }
                }
                menu.IsOpen = true;
                return;
            }

            if (header?.Column == null) return;

            var sortPath = (header.Column as DataGridTextColumn)?.Binding is System.Windows.Data.Binding sb
                ? sb.Path?.Path
                : (header.Column as DataGridTemplateColumn)?.SortMemberPath;
            if (string.IsNullOrWhiteSpace(sortPath))
            {
                sortPath = GetChallanSortMemberPathByHeader(headerName);
            }

            if (!string.IsNullOrWhiteSpace(sortPath))
            {
                GetChallanSortLabels(sortPath, out var ascLabel, out var descLabel);

                var sortAsc = new MenuItem { Header = ascLabel };
                sortAsc.Click += (_, __) => ApplyChallanSort(sortPath, true);
                menu.Items.Add(sortAsc);

                var sortDesc = new MenuItem { Header = descLabel };
                sortDesc.Click += (_, __) => ApplyChallanSort(sortPath, false);
                menu.Items.Add(sortDesc);

                var clearSort = new MenuItem { Header = "Clear Sort" };
                clearSort.Click += (_, __) => ClearChallanSort();
                menu.Items.Add(clearSort);
                menu.Items.Add(new Separator());
            }

            if (!ChallanFilterableHeaders.Contains(headerName))
            {
                header.ContextMenu = menu;
                menu.IsOpen = true;
                return;
            }

            var values = GetHeaderFilterValues(headerName).ToList();
            var current = _challanHeaderFilters.TryGetValue(headerName, out var curSet)
                ? new HashSet<string>(curSet, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var selectAll = new MenuItem { Header = "Select All" };
            selectAll.Click += (_, __) =>
            {
                _challanHeaderFilters.Remove(headerName);
                ApplyChallanDueFilter();
            };
            menu.Items.Add(selectAll);

            var clear = new MenuItem { Header = "Clear Filter" };
            clear.Click += (_, __) =>
            {
                _challanHeaderFilters.Remove(headerName);
                ApplyChallanDueFilter();
            };
            menu.Items.Add(clear);
            menu.Items.Add(new Separator());

            var searchBox = new TextBox
            {
                Width = 180,
                Height = 24,
                Margin = new Thickness(4, 2, 4, 4),
                ToolTip = "Search values..."
            };
            var searchHost = new MenuItem
            {
                StaysOpenOnClick = true,
                Focusable = false,
                Header = searchBox
            };
            menu.Items.Add(searchHost);
            menu.Items.Add(new Separator());

            var valueItems = new List<(string Value, MenuItem Item)>();
            foreach (var v in values)
            {
                var item = new MenuItem
                {
                    Header = v,
                    IsCheckable = true,
                    StaysOpenOnClick = true,
                    IsChecked = current.Contains(v)
                };
                item.Click += (_, __) =>
                {
                    if (item.IsChecked) current.Add(v);
                    else current.Remove(v);

                    if (current.Count == 0 || current.Count == values.Count) _challanHeaderFilters.Remove(headerName);
                    else _challanHeaderFilters[headerName] = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);

                    ApplyChallanDueFilter();
                };
                menu.Items.Add(item);
                valueItems.Add((v, item));
            }

            searchBox.TextChanged += (_, __) =>
            {
                var text = (searchBox.Text ?? string.Empty).Trim();
                foreach (var pair in valueItems)
                {
                    pair.Item.Visibility = string.IsNullOrWhiteSpace(text) || pair.Value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            };

            menu.Opened += (_, __) => searchBox.Focus();
            header.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private static string GetChallanSortMemberPathByHeader(string headerName)
        {
            var h = NormalizeChallanHeaderName(headerName);
            switch (h)
            {
                case "#": return "Sr";
                case "Challan No": return "ChallanNumber";
                case "Date": return "Date";
                case "LR No.": return "LRNumber";
                case "Agent/Broker": return "BrokerName";
                case "From": return "From";
                case "To": return "To";
                case "Vehicle No": return "VehicleNumber";
                case "Vehicle Type": return "VehicleType";
                case "Driver": return "DriverName";
                case "Driver Mobile": return "DriverMobile";
                case "Engine No": return "EngineNo";
                case "Licence": return "LicenceNo";
                case "Policy": return "PolicyNo";
                case "Chassis": return "ChassisNo";
                case "Owner": return "OwnerName";
                case "PAN": return "PAN";
                case "Lorry Hire": return "LorryHire";
                case "Less TDS": return "LessTDS";
                case "Advance": return "AdvanceAmount";
                case "Adv (NEFT)": return "AdvanceNEFT";
                case "Adv (Cash)": return "AdvanceCash";
                case "Adv Date": return "AdvanceDate";
                case "Balance": return "Balance";
                case "Detention": return "Detention";
                case "Hamali": return "Hamali";
                case "Deduction": return "Deduction";
                case "Bal Paid (NEFT)": return "BalancePaidNEFT";
                case "Bal Paid (Cash)": return "BalancePaidCash";
                case "Bal Paid Date": return "BalancePaidDate";
                case "Due": return "Due";
                case "Paid To": return "PaidTo";
                case "Remarks": return "Remarks";
                case "Bill Amount": return "BillAmount";
                case "Margin": return "Margin";
                default: return string.Empty;
            }
        }

        private void ApplyChallanSort(string propName, bool ascending)
        {
            if (LedgerGrid == null || VM == null || string.IsNullOrWhiteSpace(propName)) return;

            foreach (var c in LedgerGrid.Columns)
            {
                var h = NormalizeChallanHeaderName(c.Header?.ToString() ?? string.Empty);
                c.Header = h;
                c.SortDirection = null;
            }

            var target = LedgerGrid.Columns.FirstOrDefault(c =>
            {
                var p = (c as DataGridTextColumn)?.Binding is System.Windows.Data.Binding b ? b.Path?.Path : (c as DataGridTemplateColumn)?.SortMemberPath;
                return string.Equals(p, propName, StringComparison.OrdinalIgnoreCase);
            });

            if (target != null)
            {
                target.SortDirection = ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
                var clean = NormalizeChallanHeaderName(target.Header?.ToString() ?? string.Empty);
                target.Header = clean + (ascending ? " ▲" : " ▼");
            }

            VM.SetSort(propName, ascending);
            ApplyChallanHeaderFilterIndicators();
        }

        private static void GetChallanSortLabels(string propName, out string ascLabel, out string descLabel)
        {
            var key = (propName ?? string.Empty).Trim().ToLowerInvariant();

            // Date columns
            if (key == "date" || key == "advancedate" || key == "balancepaiddate")
            {
                ascLabel = "Sort Oldest to Newest";
                descLabel = "Sort Newest to Oldest";
                return;
            }

            // Numeric columns
            if (key == "sr" || key == "lorryhire" || key == "lesstds" || key == "advanceamount" ||
                key == "advanceneft" || key == "advancecash" || key == "balance" || key == "detention" ||
                key == "hamali" || key == "deduction" || key == "balancepaidneft" || key == "balancepaidcash" ||
                key == "due" || key == "billamount" || key == "margin")
            {
                ascLabel = "Sort Smallest to Largest";
                descLabel = "Sort Largest to Smallest";
                return;
            }

            // Text columns
            ascLabel = "Sort A to Z";
            descLabel = "Sort Z to A";
        }

        private void ClearChallanSort()
        {
            if (LedgerGrid == null || VM == null) return;

            foreach (var c in LedgerGrid.Columns)
            {
                c.Header = NormalizeChallanHeaderName(c.Header?.ToString() ?? string.Empty);
                c.SortDirection = null;
            }

            VM.SetSort(string.Empty, true);
            ApplyChallanHeaderFilterIndicators();
        }

        private IEnumerable<string> GetHeaderFilterValues(string headerName)
        {
            // Use full loaded page values instead of currently filtered subset.
            IEnumerable<ChallanEntry> rows = VM?.PagedEntries ?? new System.Collections.ObjectModel.ObservableCollection<ChallanEntry>();
            switch (headerName)
            {
                case "LR No.":
                    return rows
                        .SelectMany(x => (x?.LRNumber ?? string.Empty)
                            .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim()))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x);
                case "Agent/Broker": return rows.Select(x => (x?.BrokerName ?? string.Empty).Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);
                case "From": return rows.Select(x => (x?.From ?? string.Empty).Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);
                case "To": return rows.Select(x => (x?.To ?? string.Empty).Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);
                case "Lorry Hire": return rows.Select(x => x?.LorryHire.ToString("N2")).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);
                case "Balance": return rows.Select(x => x?.Balance.ToString("N2")).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);
                case "Due": return rows.Select(x => x?.Due.ToString("N2")).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);
            }
            return Enumerable.Empty<string>();
        }
        private void ApplyChallanHeaderFilterIndicators()
        {
            if (LedgerGrid == null) return;
            if (_challanFilteredHeaderStyle == null)
            {
                var baseStyle = FindResource("DataGridHeaderStyle") as Style;
                _challanFilteredHeaderStyle = new Style(typeof(DataGridColumnHeader), baseStyle);
                _challanFilteredHeaderStyle.Setters.Add(new Setter(Control.BackgroundProperty, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E65100"))));
                _challanFilteredHeaderStyle.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            }

            foreach (var col in LedgerGrid.Columns)
            {
                var headerName = NormalizeChallanHeaderName((col.Header ?? string.Empty).ToString())
                    .Replace(" ▲", string.Empty)
                    .Replace(" ▼", string.Empty)
                    .Trim();
                var hasFilter = _challanHeaderFilters.TryGetValue(headerName, out var selectedSet) && selectedSet != null && selectedSet.Count > 0;
                col.HeaderStyle = hasFilter ? _challanFilteredHeaderStyle : null;
            }
        }
        private static string NormalizeChallanHeaderName(string header)
        {
            var h = (header ?? string.Empty).Trim();
            h = h.Replace(" ▲", string.Empty)
                 .Replace(" ▼", string.Empty)
                 .Replace(" â–²", string.Empty)
                 .Replace(" â–¼", string.Empty)
                 .Trim();
            return h;
        }

        private void LRUpdatePageUI() { if (LRVM == null) return; if (LRRecordCountText != null) LRRecordCountText.Text = $"Records: {LRVM.FilteredEntriesCount}"; LRRefreshFilteredSummary(); if (_lrHeaderFilters.Count > 0) ApplyLRHeaderFilter(); }
        private void LRRefreshFilteredSummary() { if (LRVM == null) return; if (LRSearchedRecordsTextBlock != null) LRSearchedRecordsTextBlock.Text = $"Records: {LRVM.FilteredEntriesCount}"; if (LRSearchedTotalDueTextBlock != null) { LRSearchedTotalDueTextBlock.Text = $"Filtered Balance: ₹ {LRVM.FilteredTotalBalance:N2}"; LRSearchedTotalDueTextBlock.Visibility = Visibility.Visible; } }
        private void LedgerGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e) { if (LedgerGrid.SelectedCells.Count < 2) { SelectedSumTextBlock.Visibility = Visibility.Collapsed; return; } decimal totalSum = 0; bool hasNumbers = false; var cache = new Dictionary<string, System.Reflection.PropertyInfo>(); foreach (var cellInfo in LedgerGrid.SelectedCells) { var item = cellInfo.Item; var column = cellInfo.Column as DataGridBoundColumn; if (column?.Binding is System.Windows.Data.Binding binding) { var path = binding.Path.Path; if (!cache.TryGetValue(path, out var prop)) { prop = item.GetType().GetProperty(path); cache[path] = prop; } if (prop != null) { var val = prop.GetValue(item); if (val is decimal || val is int || val is long || val is double) { totalSum += Convert.ToDecimal(val); hasNumbers = true; } } } } if (hasNumbers) { SelectedSumTextBlock.Text = $"Selected Sum: ₹ {totalSum:N2}"; SelectedSumTextBlock.Visibility = Visibility.Visible; } else SelectedSumTextBlock.Visibility = Visibility.Collapsed; }
        private void LedgerGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) { if (e.EditAction != DataGridEditAction.Commit) return; var entry = e.Row.Item as ChallanEntry; if (entry == null || entry.Id <= 0) return; try { if (e.EditingElement is TextBox textBox) { var binding = textBox.GetBindingExpression(TextBox.TextProperty); binding?.UpdateSource(); } VM?.GetRepository().Upsert(entry); SyncLinkedLREntriesFromChallan(entry); SyncAllChallanBillingFromLR(); } catch { } }
        private void LedgerGrid_Sorting(object sender, DataGridSortingEventArgs e) { e.Handled = true; var col = e.Column; string propName = (col as DataGridTextColumn)?.Binding is System.Windows.Data.Binding b ? b.Path?.Path : (col as DataGridTemplateColumn)?.SortMemberPath; if (string.IsNullOrEmpty(propName) || col.CanUserSort == false) return; bool sameColumn = VM?.GetSortColumn() == propName; bool ascending = !sameColumn || !(VM?.IsCurrentSortAscending ?? true); foreach (var c in LedgerGrid.Columns) { string h = (c.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", ""); c.Header = h; c.SortDirection = null; } col.SortDirection = ascending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending; col.Header = col.Header?.ToString() + (ascending ? " ▲" : " ▼"); VM?.SetSort(propName, ascending); }
        private void LRLedgerGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e) { if (LRLedgerGrid.SelectedCells.Count < 2) { LRSelectedSumTextBlock.Visibility = Visibility.Collapsed; return; } decimal totalSum = 0; bool hasNumbers = false; var cache = new Dictionary<string, System.Reflection.PropertyInfo>(); foreach (var cellInfo in LRLedgerGrid.SelectedCells) { var item = cellInfo.Item; var column = cellInfo.Column as DataGridBoundColumn; if (column?.Binding is System.Windows.Data.Binding binding) { var path = binding.Path.Path; if (!cache.TryGetValue(path, out var prop)) { prop = item.GetType().GetProperty(path); cache[path] = prop; } if (prop != null) { var val = prop.GetValue(item); if (val is decimal || val is int || val is long || val is double) { totalSum += Convert.ToDecimal(val); hasNumbers = true; } } } } if (hasNumbers) { LRSelectedSumTextBlock.Text = $"Selected: ₹ {totalSum:N2}"; LRSelectedSumTextBlock.Visibility = Visibility.Visible; } else LRSelectedSumTextBlock.Visibility = Visibility.Collapsed; }
        private void LRLedgerGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            var entry = e.Row.Item as LREntry;
            if (entry == null || entry.Id <= 0) return;

            try
            {
                if (e.EditingElement is TextBox textBox)
                {
                    var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                    binding?.UpdateSource();
                }

                // Keep GST values normalized in uppercase.
                entry.ConsignorGST = (entry.ConsignorGST ?? string.Empty).Trim().ToUpperInvariant();
                entry.ConsigneeGST = (entry.ConsigneeGST ?? string.Empty).Trim().ToUpperInvariant();

                if (LRVM != null)
                {
                    new LRRepository().Upsert(entry);
                    SyncLinkedBillsFromLREntry(entry);
                    SyncAllChallanBillingFromLR();
                }
            }
            catch { }
        }

        private void LRLedgerGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (!(e.EditingElement is TextBox textBox) || e.Column == null) return;
            var bindingPath = GetColumnBindingPath(e.Column) ?? string.Empty;

            // Overwrite mode for numeric LR fields so typing replaces default 0.
            if (string.Equals(bindingPath, "SizeL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "SizeW", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "SizeH", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "ActualWeight", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "ChargedWeight", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "PKG", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "TotalFreight", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "Hamali", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "Detention", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "Others", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "StCharge", StringComparison.OrdinalIgnoreCase))
            {
                textBox.Dispatcher.BeginInvoke(new Action(() => textBox.SelectAll()), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void LRNumericTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.SelectAll();
            }
        }

        private void LRNumericTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is TextBox tb)) return;
            if (!tb.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                tb.Focus();
            }
        }
        private void LRLedgerGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e?.Column == null) return;

            var bindingPath = GetColumnBindingPath(e.Column) ?? string.Empty;
            var headerName = NormalizeHeaderForSort((e.Column.Header ?? string.Empty).ToString());

            bool locked =
                string.Equals(bindingPath, "From", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "To", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "VehicleNo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "VehicleType", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "CHNo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, "From", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, "To", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, "Vehicle No.", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, "Type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, "CH No.", StringComparison.OrdinalIgnoreCase);

            if (!locked) return;

            e.Cancel = true;
            MessageBox.Show(
                "This field can only be changed from Challan Ledger.\nPlease edit the related challan to update it.",
                "Locked Field",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        private void LRLedgerGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridCell)) source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            var cell = source as DataGridCell;
            if (cell?.Column == null) return;

            var bindingPath = GetColumnBindingPath(cell.Column) ?? string.Empty;
            var headerName = NormalizeHeaderForSort((cell.Column.Header ?? string.Empty).ToString());

            bool locked =
                string.Equals(bindingPath, "From", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "To", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "VehicleNo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "VehicleType", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bindingPath, "CHNo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, "From", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, "To", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, "Vehicle No.", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, "Type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerName, "CH No.", StringComparison.OrdinalIgnoreCase);

            if (!locked) return;
            e.Handled = true;
            MessageBox.Show(
                "This field can only be changed from Challan Ledger.\nPlease edit the related challan to update it.",
                "Locked Field",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        private void SyncLinkedBillsFromLREntry(LREntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.LRNo)) return;

            var candidateBills = new List<(int Id, string LRNoList)>();
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"SELECT Id, LRNo FROM Bills
                                        WHERE (BillNo = @billNo) OR (LRNo LIKE @lrLike);";
                    cmd.Parameters.AddWithValue("@billNo", (object)entry.BillNo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@lrLike", "%" + entry.LRNo + "%");
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            candidateBills.Add((Convert.ToInt32(r["Id"]), r["LRNo"] as string ?? string.Empty));
                        }
                    }
                }

                foreach (var bill in candidateBills)
                {
                    var lrNos = bill.LRNoList
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (lrNos.Count == 0) continue;
                    if (!lrNos.Contains(entry.LRNo, StringComparer.OrdinalIgnoreCase)) continue;

                    decimal freight = 0, hamali = 0, detention = 0, others = 0, stCharge = 0, rcvd = 0, tds = 0, ded = 0;

                    using (var sumCmd = conn.CreateCommand())
                    {
                        var pNames = new List<string>();
                        for (int i = 0; i < lrNos.Count; i++)
                        {
                            var p = "@lr" + i;
                            pNames.Add(p);
                            sumCmd.Parameters.AddWithValue(p, lrNos[i]);
                        }
                        sumCmd.CommandText = $@"
SELECT
    COALESCE(SUM(TotalFreight),0),
    COALESCE(SUM(Hamali),0),
    COALESCE(SUM(Detention),0),
    COALESCE(SUM(Others),0),
    COALESCE(SUM(StCharge),0),
    COALESCE(SUM(NEFT + CASH),0),
    COALESCE(SUM(TDS),0),
    COALESCE(SUM(Ded),0)
FROM LREntries
WHERE LRNo IN ({string.Join(",", pNames)});";
                        using (var sr = sumCmd.ExecuteReader())
                        {
                            if (sr.Read())
                            {
                                freight = Convert.ToDecimal(sr[0]);
                                hamali = Convert.ToDecimal(sr[1]);
                                detention = Convert.ToDecimal(sr[2]);
                                others = Convert.ToDecimal(sr[3]);
                                stCharge = Convert.ToDecimal(sr[4]);
                                rcvd = Convert.ToDecimal(sr[5]);
                                tds = Convert.ToDecimal(sr[6]);
                                ded = Convert.ToDecimal(sr[7]);
                            }
                        }
                    }

                    using (var upd = conn.CreateCommand())
                    {
                        upd.CommandText = @"UPDATE Bills SET
                                            Freight = @freight,
                                            HML = @hml,
                                            Detention = @detention,
                                            OTHR = @othr,
                                            StCharge = @stCharge,
                                            RCVD = @rcvd,
                                            TDS = @tds,
                                            DED = @ded
                                            WHERE Id = @id;";
                        upd.Parameters.AddWithValue("@freight", freight);
                        upd.Parameters.AddWithValue("@hml", hamali);
                        upd.Parameters.AddWithValue("@detention", detention);
                        upd.Parameters.AddWithValue("@othr", others);
                        upd.Parameters.AddWithValue("@stCharge", stCharge);
                        upd.Parameters.AddWithValue("@rcvd", rcvd);
                        upd.Parameters.AddWithValue("@tds", tds);
                        upd.Parameters.AddWithValue("@ded", ded);
                        upd.Parameters.AddWithValue("@id", bill.Id);
                        upd.ExecuteNonQuery();
                    }
                }
            }

            BillVM?.RefreshAfterDelete();
            BillUpdatePageUI();
        }

        private void SyncAllChallanBillingFromLR(bool force = false)
        {
            if (_challanBillingSyncInProgress) return;
            if (!force && (DateTime.UtcNow - _lastChallanBillingSyncUtc).TotalMilliseconds < 800) return;
            try
            {
                _challanBillingSyncInProgress = true;
                using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                {
                    conn.Open();

                    var challans = new List<(int Id, string ChallanNo, string LRNumber, decimal LorryHire, decimal Detention, decimal Hamali)>();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"SELECT Id,
                                                   COALESCE(ChallanNumber, '') AS ChallanNumber,
                                                   COALESCE(LRNumber, '') AS LRNumber,
                                                   COALESCE(LorryHire, 0) AS LorryHire,
                                                   COALESCE(Detention, 0) AS Detention,
                                                   COALESCE(Hamali, 0) AS Hamali
                                            FROM Challans;";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                challans.Add((
                                    Convert.ToInt32(r["Id"]),
                                    (r["ChallanNumber"] as string ?? string.Empty).Trim(),
                                    (r["LRNumber"] as string ?? string.Empty),
                                    Convert.ToDecimal(r["LorryHire"]),
                                    Convert.ToDecimal(r["Detention"]),
                                    Convert.ToDecimal(r["Hamali"])
                                ));
                            }
                        }
                    }

                    var lrRows = new List<(string LRNo, string CHNo, decimal TotalBill)>();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"SELECT LRNo, CHNo, (COALESCE(TotalFreight,0) + COALESCE(Hamali,0) + COALESCE(Detention,0) + COALESCE(Others,0)) AS TotalBill
                                            FROM LREntries;";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                lrRows.Add((
                                    (r["LRNo"] as string ?? string.Empty).Trim(),
                                    (r["CHNo"] as string ?? string.Empty).Trim(),
                                    Convert.ToDecimal(r["TotalBill"])
                                ));
                            }
                        }
                    }

                    var lrByNo = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                    foreach (var lr in lrRows)
                    {
                        if (string.IsNullOrWhiteSpace(lr.LRNo)) continue;
                        lrByNo[lr.LRNo] = lr.TotalBill;
                    }

                    var lrByChNo = lrRows
                        .Where(x => !string.IsNullOrWhiteSpace(x.CHNo) && !string.IsNullOrWhiteSpace(x.LRNo))
                        .GroupBy(x => x.CHNo, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Select(x => x.LRNo).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

                    using (var tx = conn.BeginTransaction())
                    {
                        foreach (var ch in challans)
                        {
                            var lrSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var token in SplitLrNumbers(ch.LRNumber)) lrSet.Add(token);
                            if (!string.IsNullOrWhiteSpace(ch.ChallanNo) && lrByChNo.TryGetValue(ch.ChallanNo, out var listFromChNo))
                            {
                                foreach (var lrNo in listFromChNo) lrSet.Add(lrNo);
                            }

                            decimal billAmount = 0m;
                            foreach (var lrNo in lrSet)
                            {
                                if (lrByNo.TryGetValue(lrNo, out var total)) billAmount += total;
                            }
                            var margin = billAmount != 0m
                                ? (billAmount - ch.LorryHire + ch.Detention + ch.Hamali)
                                : 0m;

                            using (var upd = conn.CreateCommand())
                            {
                                upd.Transaction = tx;
                                upd.CommandText = "UPDATE Challans SET BillAmount = @billAmount, Margin = @margin WHERE Id = @id;";
                                upd.Parameters.AddWithValue("@billAmount", billAmount);
                                upd.Parameters.AddWithValue("@margin", margin);
                                upd.Parameters.AddWithValue("@id", ch.Id);
                                upd.ExecuteNonQuery();
                            }

                            var vmEntry = VM?.PagedEntries?.FirstOrDefault(x => x.Id == ch.Id);
                            if (vmEntry != null)
                            {
                                vmEntry.BillAmount = billAmount;
                                vmEntry.Margin = margin;
                            }
                        }
                        tx.Commit();
                    }
                }
                _lastChallanBillingSyncUtc = DateTime.UtcNow;
                if (LedgerGrid != null) LedgerGrid.Items.Refresh();
                RefreshFilteredSummary();
                RefreshDashboard();
            }
            catch { }
            finally
            {
                _challanBillingSyncInProgress = false;
            }
        }

        private static IEnumerable<string> SplitLrNumbers(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }
        private void LRLedgerGrid_Sorting(object sender, DataGridSortingEventArgs e) { e.Handled = true; var col = e.Column; string propName = (col as DataGridTextColumn)?.Binding is System.Windows.Data.Binding b ? b.Path?.Path : (col as DataGridTemplateColumn)?.SortMemberPath; if (string.IsNullOrEmpty(propName) || col.CanUserSort == false) return; bool sameColumn = LRVM?.GetSortColumn() == propName; bool ascending = !sameColumn || !(LRVM?.IsCurrentSortAscending ?? true); foreach (var c in LRLedgerGrid.Columns) { string h = (c.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", ""); c.Header = h; c.SortDirection = null; } col.SortDirection = ascending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending; col.Header = col.Header?.ToString() + (ascending ? " ▲" : " ▼"); LRVM?.SetSort(propName, ascending); }
        private void LRLedgerGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInColumnHeaderResizeGripper(e.OriginalSource as DependencyObject)) return;
            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridColumnHeader)) source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            var header = source as DataGridColumnHeader;
            if (header?.Column == null || LRLedgerGrid == null) return;

            e.Handled = true;
            bool appendMode = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            _lrHeaderDragSelecting = true;
            _lrDragStartDisplayIndex = header.Column.DisplayIndex;
            _lrDragAppendMode = appendMode;

            SelectLRColumnsByDisplayRange(_lrDragStartDisplayIndex, _lrDragStartDisplayIndex, appendMode);
        }

        private void LRLedgerGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_lrHeaderDragSelecting || LRLedgerGrid == null || e.LeftButton != MouseButtonState.Pressed) return;

            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridColumnHeader)) source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            var header = source as DataGridColumnHeader;
            if (header?.Column == null || _lrDragStartDisplayIndex < 0) return;

            SelectLRColumnsByDisplayRange(_lrDragStartDisplayIndex, header.Column.DisplayIndex, _lrDragAppendMode);
            e.Handled = true;
        }

        private void LRLedgerGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _lrHeaderDragSelecting = false;
            _lrDragStartDisplayIndex = -1;
            _lrDragAppendMode = false;
        }

        private static bool IsInColumnHeaderResizeGripper(DependencyObject source)
        {
            var walker = source;
            while (walker != null)
            {
                if (walker is Thumb) return true;
                var fe = walker as FrameworkElement;
                var name = fe?.Name ?? string.Empty;
                if (name.IndexOf("Gripper", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                walker = System.Windows.Media.VisualTreeHelper.GetParent(walker);
            }
            return false;
        }

        private void SelectLRColumnsByDisplayRange(int startDisplayIndex, int endDisplayIndex, bool appendMode)
        {
            if (LRLedgerGrid == null) return;
            int lo = Math.Min(startDisplayIndex, endDisplayIndex);
            int hi = Math.Max(startDisplayIndex, endDisplayIndex);

            if (!appendMode) LRLedgerGrid.UnselectAllCells();

            var cols = LRLedgerGrid.Columns
                .Where(c => c.DisplayIndex >= lo && c.DisplayIndex <= hi)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            foreach (var item in LRLedgerGrid.Items)
            {
                if (item == CollectionView.NewItemPlaceholder) continue;
                foreach (var col in cols)
                {
                    var info = new DataGridCellInfo(item, col);
                    if (!LRLedgerGrid.SelectedCells.Contains(info)) LRLedgerGrid.SelectedCells.Add(info);
                }
            }
        }

        private void LRLedgerGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            DependencyObject walker = source;
            while (walker != null &&
                   !(walker is DataGridColumnHeader) &&
                   !(walker is DataGridCell) &&
                   !(walker is DataGridRowHeader) &&
                   !(walker is DataGridRow))
            {
                walker = System.Windows.Media.VisualTreeHelper.GetParent(walker);
            }
            var header = walker as DataGridColumnHeader;
            var cell = walker as DataGridCell;
            var rowHeader = walker as DataGridRowHeader;
            var row = walker as DataGridRow;
            if (header?.Column == null && cell == null && rowHeader == null && row == null) return;
            var headerName = header?.Column != null
                ? NormalizeChallanHeaderName((header.Column.Header ?? string.Empty).ToString())
                : string.Empty;

            e.Handled = true;
            var menu = new ContextMenu();
            if (header?.Column != null)
            {
                var copyCol = new MenuItem { Header = "Copy Column" };
                var colRef = header.Column;
                copyCol.Click += (_, __) => CopyColumnFromGrid(LRLedgerGrid, colRef);
                menu.Items.Add(copyCol);
                menu.Items.Add(new Separator());
            }
            if (rowHeader != null || row != null)
            {
                if (row != null && row.Item != null && row.Item != CollectionView.NewItemPlaceholder)
                {
                    LRLedgerGrid.SelectedItems.Clear();
                    LRLedgerGrid.SelectedItems.Add(row.Item);
                }
                var copyRows = new MenuItem { Header = "Copy Row(s)" };
                copyRows.Click += (_, __) => CopySelectedRowsFromGrid(LRLedgerGrid);
                menu.Items.Add(copyRows);
                var selectedLr = (row?.Item as LREntry) ?? (LRLedgerGrid.SelectedItem as LREntry);
                if (selectedLr != null)
                {
                    var viewLr = new MenuItem { Header = "View LR Format" };
                    viewLr.Click += (_, __) => OpenLRFormatPreview(selectedLr);
                    menu.Items.Add(viewLr);
                }
                menu.IsOpen = true;
                return;
            }
            if (cell != null)
            {
                if (cell.DataContext != null) LRLedgerGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);
                var copyCell = new MenuItem { Header = "Copy Cell" };
                copyCell.Click += (_, __) => CopyCurrentCellFromGrid(LRLedgerGrid);
                menu.Items.Add(copyCell);
                var lrEntryForView = cell.DataContext as LREntry;
                if (lrEntryForView != null)
                {
                    var viewLr = new MenuItem { Header = "View LR Format" };
                    viewLr.Click += (_, __) => OpenLRFormatPreview(lrEntryForView);
                    menu.Items.Add(viewLr);
                }

                var cellHeader = NormalizeChallanHeaderName((cell.Column?.Header ?? string.Empty).ToString());
                if (string.Equals(cellHeader, "LR No.", StringComparison.OrdinalIgnoreCase))
                {
                    var lrEntry = cell.DataContext as LREntry;
                    if (lrEntry != null)
                    {
                        var addComment = new MenuItem { Header = "View / Add Comment" };
                        addComment.Click += (_, __) => OpenLRCommentPopup(lrEntry);
                        menu.Items.Add(addComment);
                    }
                }
                menu.IsOpen = true;
                return;
            }
            if (header?.Column == null) return;

            var sortPath = (header.Column as DataGridTextColumn)?.Binding is System.Windows.Data.Binding sb
                ? sb.Path?.Path
                : (header.Column as DataGridTemplateColumn)?.SortMemberPath;
            if (string.IsNullOrWhiteSpace(sortPath)) return;

            GetLRSortLabels(sortPath, out var ascLabel, out var descLabel);
            var sortAsc = new MenuItem { Header = ascLabel };
            sortAsc.Click += (_, __) => ApplyLRSortClean(sortPath, true);
            menu.Items.Add(sortAsc);

            var sortDesc = new MenuItem { Header = descLabel };
            sortDesc.Click += (_, __) => ApplyLRSortClean(sortPath, false);
            menu.Items.Add(sortDesc);

            var clearSort = new MenuItem { Header = "Clear Sort" };
            clearSort.Click += (_, __) => ClearLRSort();
            menu.Items.Add(clearSort);

            if (!LRFilterableHeaders.Contains(headerName))
            {
                header.ContextMenu = menu;
                menu.IsOpen = true;
                return;
            }

            menu.Items.Add(new Separator());

            var values = GetLRHeaderFilterValues(headerName).ToList();
            var current = _lrHeaderFilters.TryGetValue(headerName, out var curSet)
                ? new HashSet<string>(curSet, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var selectAll = new MenuItem { Header = "Select All" };
            selectAll.Click += (_, __) =>
            {
                _lrHeaderFilters.Remove(headerName);
                ApplyLRHeaderFilter();
            };
            menu.Items.Add(selectAll);

            var clear = new MenuItem { Header = "Clear Filter" };
            clear.Click += (_, __) =>
            {
                _lrHeaderFilters.Remove(headerName);
                ApplyLRHeaderFilter();
            };
            menu.Items.Add(clear);
            menu.Items.Add(new Separator());

            var searchBox = new TextBox
            {
                Width = 180,
                Height = 24,
                Margin = new Thickness(4, 2, 4, 4),
                ToolTip = "Search values..."
            };
            var searchHost = new MenuItem
            {
                StaysOpenOnClick = true,
                Focusable = false,
                Header = searchBox
            };
            menu.Items.Add(searchHost);
            menu.Items.Add(new Separator());

            var valueItems = new List<(string Value, MenuItem Item)>();
            foreach (var v in values)
            {
                var item = new MenuItem
                {
                    Header = v,
                    IsCheckable = true,
                    StaysOpenOnClick = true,
                    IsChecked = current.Contains(v)
                };
                item.Click += (_, __) =>
                {
                    if (item.IsChecked) current.Add(v);
                    else current.Remove(v);

                    if (current.Count == 0 || current.Count == values.Count) _lrHeaderFilters.Remove(headerName);
                    else _lrHeaderFilters[headerName] = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);

                    ApplyLRHeaderFilter();
                };
                menu.Items.Add(item);
                valueItems.Add((v, item));
            }

            searchBox.TextChanged += (_, __) =>
            {
                var text = (searchBox.Text ?? string.Empty).Trim();
                foreach (var pair in valueItems)
                {
                    pair.Item.Visibility = string.IsNullOrWhiteSpace(text) || pair.Value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            };
            menu.Opened += (_, __) => searchBox.Focus();

            header.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void ApplyLRSort(string propName, bool ascending)
        {
            if (LRLedgerGrid == null || LRVM == null || string.IsNullOrWhiteSpace(propName)) return;

            foreach (var c in LRLedgerGrid.Columns)
            {
                c.Header = NormalizeHeaderForSort(c.Header?.ToString() ?? string.Empty);
                c.SortDirection = null;
            }

            var target = LRLedgerGrid.Columns.FirstOrDefault(c =>
            {
                var p = (c as DataGridTextColumn)?.Binding is System.Windows.Data.Binding b ? b.Path?.Path : (c as DataGridTemplateColumn)?.SortMemberPath;
                return string.Equals(p, propName, StringComparison.OrdinalIgnoreCase);
            });

            if (target != null)
            {
                target.SortDirection = ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
                var clean = NormalizeChallanHeaderName(target.Header?.ToString() ?? string.Empty);
                target.Header = clean + (ascending ? " â–²" : " â–¼");
            }

            LRVM.SetSort(propName, ascending);
        }

        private static void GetLRSortLabels(string propName, out string ascLabel, out string descLabel)
        {
            var key = (propName ?? string.Empty).Trim().ToLowerInvariant();

            if (key == "date" || key == "billdate")
            {
                ascLabel = "Sort Oldest to Newest";
                descLabel = "Sort Newest to Oldest";
                return;
            }

            if (key == "sr" || key == "weight" || key == "pkg" || key == "totalfreight" ||
                key == "hamali" || key == "detention" || key == "others" || key == "stcharge" || key == "totalbill" ||
                key == "comm" || key == "paid")
            {
                ascLabel = "Sort Smallest to Largest";
                descLabel = "Sort Largest to Smallest";
                return;
            }

            ascLabel = "Sort A to Z";
            descLabel = "Sort Z to A";
        }

        private void ApplyLRSortClean(string propName, bool ascending)
        {
            if (LRLedgerGrid == null || LRVM == null || string.IsNullOrWhiteSpace(propName)) return;

            foreach (var c in LRLedgerGrid.Columns)
            {
                c.Header = NormalizeHeaderForSort(c.Header?.ToString() ?? string.Empty);
                c.SortDirection = null;
            }

            var target = LRLedgerGrid.Columns.FirstOrDefault(c =>
            {
                var p = (c as DataGridTextColumn)?.Binding is Binding b ? b.Path?.Path : (c as DataGridTemplateColumn)?.SortMemberPath;
                return string.Equals(p, propName, StringComparison.OrdinalIgnoreCase);
            });

            if (target != null)
            {
                target.SortDirection = ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
                target.Header = NormalizeHeaderForSort(target.Header?.ToString() ?? string.Empty) + (ascending ? " \u25B2" : " \u25BC");
            }

            LRVM.SetSort(propName, ascending);
        }

        private static string NormalizeHeaderForSort(string header)
        {
            var h = (header ?? string.Empty).Trim();
            return h.Replace(" \u25B2", string.Empty)
                    .Replace(" \u25BC", string.Empty)
                    .Replace(" ▲", string.Empty)
                    .Replace(" ▼", string.Empty)
                    .Replace(" â–²", string.Empty)
                    .Replace(" â–¼", string.Empty)
                    .Replace(" Ã¢â€“Â²", string.Empty)
                    .Replace(" Ã¢â€“Â¼", string.Empty)
                    .Trim();
        }

        private void ClearLRSort()
        {
            if (LRLedgerGrid == null || LRVM == null) return;

            foreach (var c in LRLedgerGrid.Columns)
            {
                c.Header = NormalizeChallanHeaderName(c.Header?.ToString() ?? string.Empty);
                c.SortDirection = null;
            }

            LRVM.SetSort(string.Empty, true);
        }

        private static readonly HashSet<string> LRFilterableHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LR No.", "Date", "Consignor Name", "Consignee Name", "From", "To", "Vehicle No.", "Type",
            "L", "W", "H", "Actual Weight", "Charged Weight", "No. of Pkg", "Pkg Type", "DESCRIPTION", "Invoice", "Value", "CH No.", "Total Freight", "Hamali", "Detention",
            "Others", "St. Charge", "Total Bill", "Bill No.", "Bill Date", "Bill Party", "Broker", "Frt Type", "To Pay/To Be Billed", "comm", "Paid"
        };

        private IEnumerable<string> GetLRHeaderFilterValues(string headerName)
        {
            var rows = (LRVM?.PagedEntries ?? new System.Collections.ObjectModel.ObservableCollection<LREntry>()).Where(x => x != null);
            return rows.Select(x => GetLRHeaderCellValue(x, headerName))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x);
        }

        private string GetLRHeaderCellValue(LREntry entry, string headerName)
        {
            if (entry == null) return string.Empty;
            switch (headerName)
            {
                case "LR No.": return (entry.LRNo ?? string.Empty).Trim();
                case "Date": return entry.Date.ToString("dd-MMM-yyyy");
                case "Consignor Name": return (entry.ConsignorName ?? string.Empty).Trim();
                case "Consignee Name": return (entry.ConsigneeName ?? string.Empty).Trim();
                case "From": return (entry.From ?? string.Empty).Trim();
                case "To": return (entry.To ?? string.Empty).Trim();
                case "Vehicle No.": return (entry.VehicleNo ?? string.Empty).Trim();
                case "Type": return (entry.VehicleType ?? string.Empty).Trim();
                case "L": return entry.SizeL.ToString("0.####################");
                case "W": return entry.SizeW.ToString("0.####################");
                case "H": return entry.SizeH.ToString("0.####################");
                case "Actual Weight": return entry.ActualWeight.ToString("0.####################");
                case "Charged Weight": return entry.ChargedWeight.ToString("0.####################");
                case "No. of Pkg": return entry.PKG.ToString();
                case "Pkg Type": return (entry.PkgType ?? string.Empty).Trim();
                case "DESCRIPTION": return (entry.Description ?? string.Empty).Trim();
                case "Invoice": return (entry.Invoice ?? string.Empty).Trim();
                case "Value": return (entry.Value ?? string.Empty).Trim();
                case "CH No.": return (entry.CHNo ?? string.Empty).Trim();
                case "Total Freight": return entry.TotalFreight.ToString("N2");
                case "Hamali": return entry.Hamali.ToString("N2");
                case "Detention": return entry.Detention.ToString("N2");
                case "Others": return entry.Others.ToString("N2");
                case "St. Charge": return entry.StCharge.ToString("N2");
                case "Total Bill": return entry.TotalBill.ToString("N2");
                case "Bill No.": return (entry.BillNo ?? string.Empty).Trim();
                case "Bill Date": return entry.BillDate.HasValue ? entry.BillDate.Value.ToString("dd-MMM-yyyy") : string.Empty;
                case "Bill Party": return (entry.BillParty ?? string.Empty).Trim();
                case "Broker": return (entry.Broker ?? string.Empty).Trim();
                case "Frt Type": return (entry.FrtType ?? string.Empty).Trim();
                case "To Pay/To Be Billed": return (entry.PayType ?? string.Empty).Trim();
                case "comm": return entry.Comm.ToString("N2");
                case "Paid": return (entry.Paid ?? string.Empty).Trim();
                default: return string.Empty;
            }
        }

        private void ApplyLRHeaderFilter()
        {
            if (LRLedgerGrid == null) return;

            var cv = CollectionViewSource.GetDefaultView(LRLedgerGrid.ItemsSource);
            if (cv == null) return;

            if (_lrHeaderFilters.Count > 0)
            {
                cv.Filter = obj =>
                {
                    var entry = obj as LREntry;
                    if (entry == null) return false;

                    foreach (var kv in _lrHeaderFilters)
                    {
                        if (kv.Value == null || kv.Value.Count == 0) continue;
                        var value = GetLRHeaderCellValue(entry, kv.Key);
                        if (!kv.Value.Contains(value)) return false;
                    }
                    return true;
                };
            }
            else cv.Filter = null;

            cv.Refresh();
            ApplyLRHeaderFilterIndicators();
            UpdateLRVisibleSummaryFromView(cv);
        }

        private void ApplyLRHeaderFilterIndicators()
        {
            if (LRLedgerGrid == null) return;
            if (_lrFilteredHeaderStyle == null)
            {
                var baseStyle = FindResource("DataGridHeaderStyle") as Style;
                _lrFilteredHeaderStyle = new Style(typeof(DataGridColumnHeader), baseStyle);
                _lrFilteredHeaderStyle.Setters.Add(new Setter(Control.BackgroundProperty, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E65100"))));
                _lrFilteredHeaderStyle.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            }

            foreach (var col in LRLedgerGrid.Columns)
            {
                var headerName = NormalizeChallanHeaderName((col.Header ?? string.Empty).ToString()).Trim();
                var hasFilter = _lrHeaderFilters.TryGetValue(headerName, out var selectedSet) && selectedSet != null && selectedSet.Count > 0;
                col.HeaderStyle = hasFilter ? _lrFilteredHeaderStyle : null;
            }
        }

        private void UpdateLRVisibleSummaryFromView(ICollectionView cv)
        {
            if (LRVM == null || cv == null) return;
            var visibleRows = cv.Cast<object>().OfType<LREntry>().ToList();
            LRVM.FilteredEntriesCount = visibleRows.Count;
            LRVM.FilteredTotalBalance = visibleRows.Sum(x => x.Bal);
            LRRefreshFilteredSummary();
        }

        private void LRLedgerGrid_SortingMenuOnly(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
        }

        private void OpenDashboard_Click(object sender, RoutedEventArgs e) { DeliveryChallanView.Visibility = Visibility.Collapsed; LRLedgerView.Visibility = Visibility.Collapsed; TrackingLedgerView.Visibility = Visibility.Collapsed; DashboardView.Visibility = Visibility.Visible; PartyLedgerView.Visibility = Visibility.Collapsed; BillLedgerView.Visibility = Visibility.Collapsed; VehicleLedgerView.Visibility = Visibility.Collapsed; CBSLedgerView.Visibility = Visibility.Collapsed; TabDashboard.Style = (Style)FindResource("ActiveTabButtonStyle"); TabDeliveryChallans.Style = (Style)FindResource("TabButtonStyle"); TabLRLedger.Style = (Style)FindResource("TabButtonStyle"); TabTrackingLedger.Style = (Style)FindResource("TabButtonStyle"); TabPartyLedger.Style = (Style)FindResource("TabButtonStyle"); TabBillLedger.Style = (Style)FindResource("TabButtonStyle"); TabVehicleLedger.Style = (Style)FindResource("TabButtonStyle"); TabCBSLedger.Style = (Style)FindResource("TabButtonStyle"); if (PageTitle != null) PageTitle.Text = "Dashboard"; RefreshDashboard(); }
        private void Refresh_Click(object sender, RoutedEventArgs e) { if (SearchBox != null) SearchBox.Text = string.Empty; if (ChallanFilterBox != null) ChallanFilterBox.Text = string.Empty; if (LRFilterBox != null) LRFilterBox.Text = string.Empty; if (FromFilterBox != null) FromFilterBox.Text = string.Empty; if (ToFilterBox != null) ToFilterBox.Text = string.Empty; _challanHeaderFilters.Clear(); _onlyDueFilterEnabled = false; if (OnlyDueButton != null) OnlyDueButton.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#455A64")); ApplyChallanDueFilter(); if (LedgerGrid != null) LedgerGrid.UnselectAllCells(); }
        private void OnlyDueButton_Click(object sender, RoutedEventArgs e)
        {
            _onlyDueFilterEnabled = !_onlyDueFilterEnabled;
            if (OnlyDueButton != null)
            {
                var color = _onlyDueFilterEnabled ? "#2E7D32" : "#455A64";
                OnlyDueButton.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            }
            ApplyChallanDueFilter();
        }
        private void ShowColumnsMenu_Click(object sender, RoutedEventArgs e) { if (VM == null || !(sender is Button button)) return; _columnsMenu = BuildColumnsMenu(); _columnsMenu.PlacementTarget = button; _columnsMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; _columnsMenu.IsOpen = true; }
        private ContextMenu BuildColumnsMenu() { var menu = new ContextMenu(); foreach (var config in GetColumnToggleConfigs()) { var item = new System.Windows.Controls.MenuItem { Header = config.Label, IsCheckable = true, IsChecked = config.GetValue(), StaysOpenOnClick = true }; item.Click += (_, __) => { config.SetValue(item.IsChecked); UpdateColumnVisibility(); }; menu.Items.Add(item); } return menu; }
        private void UpdateColumnVisibility() { if (VM == null || LedgerGrid == null) return; foreach (var col in LedgerGrid.Columns) { var header = (col.Header ?? "").ToString(); switch (header) { case "#": col.Visibility = VM.ShowSr ? Visibility.Visible : Visibility.Collapsed; break; case "Challan No": col.Visibility = VM.ShowChallanNumber ? Visibility.Visible : Visibility.Collapsed; break; case "Date": col.Visibility = VM.ShowDate ? Visibility.Visible : Visibility.Collapsed; break; case "LR No.": col.Visibility = VM.ShowLRNumber ? Visibility.Visible : Visibility.Collapsed; break; case "Agent/Broker": col.Visibility = VM.ShowBrokerName ? Visibility.Visible : Visibility.Collapsed; break; case "From": col.Visibility = VM.ShowFrom ? Visibility.Visible : Visibility.Collapsed; break; case "To": col.Visibility = VM.ShowTo ? Visibility.Visible : Visibility.Collapsed; break; case "Vehicle No": col.Visibility = VM.ShowVehicleNumber ? Visibility.Visible : Visibility.Collapsed; break; case "Vehicle Type": col.Visibility = VM.ShowVehicleType ? Visibility.Visible : Visibility.Collapsed; break; case "Driver": col.Visibility = VM.ShowDriverName ? Visibility.Visible : Visibility.Collapsed; break; case "Driver Mobile": col.Visibility = VM.ShowDriverMobile ? Visibility.Visible : Visibility.Collapsed; break; case "Engine No": col.Visibility = VM.ShowEngineNo ? Visibility.Visible : Visibility.Collapsed; break; case "Licence": col.Visibility = VM.ShowLicenceNo ? Visibility.Visible : Visibility.Collapsed; break; case "Policy": col.Visibility = VM.ShowPolicyNo ? Visibility.Visible : Visibility.Collapsed; break; case "Chassis": col.Visibility = VM.ShowChassisNo ? Visibility.Visible : Visibility.Collapsed; break; case "Owner": col.Visibility = VM.ShowOwnerName ? Visibility.Visible : Visibility.Collapsed; break; case "PAN": col.Visibility = VM.ShowPAN ? Visibility.Visible : Visibility.Collapsed; break; case "Lorry Hire": col.Visibility = VM.ShowLorryHire ? Visibility.Visible : Visibility.Collapsed; break; case "Less TDS": col.Visibility = VM.ShowLessTDS ? Visibility.Visible : Visibility.Collapsed; break; case "Advance": col.Visibility = VM.ShowAdvanceAmount ? Visibility.Visible : Visibility.Collapsed; break; case "Adv (NEFT)": col.Visibility = VM.ShowAdvanceNEFT ? Visibility.Visible : Visibility.Collapsed; break; case "Adv (Cash)": col.Visibility = VM.ShowAdvanceCash ? Visibility.Visible : Visibility.Collapsed; break; case "Adv Date": col.Visibility = VM.ShowAdvanceDate ? Visibility.Visible : Visibility.Collapsed; break; case "Balance": col.Visibility = VM.ShowBalance ? Visibility.Visible : Visibility.Collapsed; break; case "Detention": col.Visibility = VM.ShowDetention ? Visibility.Visible : Visibility.Collapsed; break; case "Hamali": col.Visibility = VM.ShowHamali ? Visibility.Visible : Visibility.Collapsed; break; case "Deduction": col.Visibility = VM.ShowDeduction ? Visibility.Visible : Visibility.Collapsed; break; case "Bal Paid (NEFT)": col.Visibility = VM.ShowBalancePaidNEFT ? Visibility.Visible : Visibility.Collapsed; break; case "Bal Paid (Cash)": col.Visibility = VM.ShowBalancePaidCash ? Visibility.Visible : Visibility.Collapsed; break; case "Bal Paid Date": col.Visibility = VM.ShowBalancePaidDate ? Visibility.Visible : Visibility.Collapsed; break; case "Due": col.Visibility = VM.ShowDue ? Visibility.Visible : Visibility.Collapsed; break; case "Paid To": col.Visibility = VM.ShowPaidTo ? Visibility.Visible : Visibility.Collapsed; break; case "Remarks": col.Visibility = VM.ShowRemarks ? Visibility.Visible : Visibility.Collapsed; break; case "Bill Amount": col.Visibility = VM.ShowBillAmount ? Visibility.Visible : Visibility.Collapsed; break; case "Margin": col.Visibility = VM.ShowMargin ? Visibility.Visible : Visibility.Collapsed; break; } } }
        private IEnumerable<(string Label, Func<bool> GetValue, Action<bool> SetValue)> GetColumnToggleConfigs() { yield return ("#", () => VM.ShowSr, value => VM.ShowSr = value); yield return ("Challan No", () => VM.ShowChallanNumber, value => VM.ShowChallanNumber = value); yield return ("Date", () => VM.ShowDate, value => VM.ShowDate = value); yield return ("LR No.", () => VM.ShowLRNumber, value => VM.ShowLRNumber = value); yield return ("Agent/Broker", () => VM.ShowBrokerName, value => VM.ShowBrokerName = value); yield return ("From", () => VM.ShowFrom, value => VM.ShowFrom = value); yield return ("To", () => VM.ShowTo, value => VM.ShowTo = value); yield return ("Vehicle No", () => VM.ShowVehicleNumber, value => VM.ShowVehicleNumber = value); yield return ("Vehicle Type", () => VM.ShowVehicleType, value => VM.ShowVehicleType = value); yield return ("Driver", () => VM.ShowDriverName, value => VM.ShowDriverName = value); yield return ("Driver Mobile", () => VM.ShowDriverMobile, value => VM.ShowDriverMobile = value); yield return ("Engine No", () => VM.ShowEngineNo, value => VM.ShowEngineNo = value); yield return ("Licence", () => VM.ShowLicenceNo, value => VM.ShowLicenceNo = value); yield return ("Policy", () => VM.ShowPolicyNo, value => VM.ShowPolicyNo = value); yield return ("Chassis", () => VM.ShowChassisNo, value => VM.ShowChassisNo = value); yield return ("Owner", () => VM.ShowOwnerName, value => VM.ShowOwnerName = value); yield return ("PAN", () => VM.ShowPAN, value => VM.ShowPAN = value); yield return ("Lorry Hire", () => VM.ShowLorryHire, value => VM.ShowLorryHire = value); yield return ("Less TDS", () => VM.ShowLessTDS, value => VM.ShowLessTDS = value); yield return ("Advance", () => VM.ShowAdvanceAmount, value => VM.ShowAdvanceAmount = value); yield return ("Adv (NEFT)", () => VM.ShowAdvanceNEFT, value => VM.ShowAdvanceNEFT = value); yield return ("Adv (Cash)", () => VM.ShowAdvanceCash, value => VM.ShowAdvanceCash = value); yield return ("Adv Date", () => VM.ShowAdvanceDate, value => VM.ShowAdvanceDate = value); yield return ("Balance", () => VM.ShowBalance, value => VM.ShowBalance = value); yield return ("Detention", () => VM.ShowDetention, value => VM.ShowDetention = value); yield return ("Hamali", () => VM.ShowHamali, value => VM.ShowHamali = value); yield return ("Deduction", () => VM.ShowDeduction, value => VM.ShowDeduction = value); yield return ("Bal Paid (NEFT)", () => VM.ShowBalancePaidNEFT, value => VM.ShowBalancePaidNEFT = value); yield return ("Bal Paid (Cash)", () => VM.ShowBalancePaidCash, value => VM.ShowBalancePaidCash = value); yield return ("Bal Paid Date", () => VM.ShowBalancePaidDate, value => VM.ShowBalancePaidDate = value); yield return ("Due", () => VM.ShowDue, value => VM.ShowDue = value); yield return ("Paid To", () => VM.ShowPaidTo, value => VM.ShowPaidTo = value); yield return ("Remarks", () => VM.ShowRemarks, value => VM.ShowRemarks = value); yield return ("Bill Amount", () => VM.ShowBillAmount, value => VM.ShowBillAmount = value); yield return ("Margin", () => VM.ShowMargin, value => VM.ShowMargin = value); }
        private void OpenChallanForm_Click(object sender, RoutedEventArgs e) { OpenChallanForm(); }
        private void OpenChallanForm() { var form = new ChallanFormWindow(VM.Entries, VM.GetRepository()); form.Owner = this; form.Closed += (_, __) => { if (!form.WasSaved) return; var entry = form.Result; entry.Sr = VM.GetNextSr(); entry.RecalculateBalance(); VM.Entries.Add(entry); SyncAllChallanBillingFromLR(); try { var trackingEntry = new TrackingEntry { ChallanNo = entry.ChallanNumber, ChallanDate = entry.Date, From = entry.From, To = entry.To, VehicleNo = entry.VehicleNumber, DriverMobile = entry.DriverMobile }; TrackingVM.AddEntry(trackingEntry); } catch { } }; form.Show(); }
        private void ResetAllData_Click(object sender, RoutedEventArgs e)
        {
            var first = MessageBox.Show(
                "This will permanently delete all data from Challan, LR, Tracking, Bill, CBS, Vehicle, Party and comments.\n\nDo you want to continue?",
                "Reset All Data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (first != MessageBoxResult.Yes) return;

            var second = MessageBox.Show(
                "Final confirmation:\n\nThis action cannot be undone.\nDelete all ledger data now?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop);
            if (second != MessageBoxResult.Yes) return;

            try
            {
                using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
DELETE FROM ReportingTracks;
DELETE FROM TrackingEntries;
DELETE FROM Challans;
DELETE FROM LREntries;
DELETE FROM Bills;
DELETE FROM CashBankStatements;
DELETE FROM CBSAccounts;
DELETE FROM VehicleLedger;
DELETE FROM Parties;
DELETE FROM ChallanComments;
DELETE FROM LRComments;
DELETE FROM BillComments;
DELETE FROM sqlite_sequence WHERE name IN ('ReportingTracks','TrackingEntries','Challans','LREntries','Bills','CashBankStatements','CBSAccounts','VehicleLedger','Parties','ChallanComments','LRComments','BillComments');";
                        cmd.ExecuteNonQuery();
                        tx.Commit();
                    }
                }

                VM?.RefreshAfterDelete();
                LRVM?.RefreshAfterDelete();
                BillVM?.RefreshAfterDelete();
                TrackingVM = new TrackingViewModel();
                if (TrackingLedgerView != null) TrackingLedgerView.DataContext = TrackingVM;
                if (TrackingLedgerGrid != null) TrackingLedgerGrid.ItemsSource = TrackingVM.Entries;

                RefreshFilteredSummary();
                LRRefreshFilteredSummary();
                BillUpdatePageUI();
                RefreshDashboard();
                RefreshCBSAccounts();
                RefreshCBSGrid();

                MessageBox.Show("All ledger data has been deleted.", "Reset Completed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Reset failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void OpenLRLedger_Click(object sender, RoutedEventArgs e)
        {
            DashboardView.Visibility = Visibility.Collapsed;
            DeliveryChallanView.Visibility = Visibility.Collapsed;
            LRLedgerView.Visibility = Visibility.Visible;
            TrackingLedgerView.Visibility = Visibility.Collapsed;
            PartyLedgerView.Visibility = Visibility.Collapsed;
            BillLedgerView.Visibility = Visibility.Collapsed;
            VehicleLedgerView.Visibility = Visibility.Collapsed;
            CBSLedgerView.Visibility = Visibility.Collapsed;
            LRVM.EnsurePageLoaded();
            if (LRLedgerView.DataContext == null) LRLedgerView.DataContext = LRVM;
            if (LRLedgerGrid != null)
            {
                var cv = CollectionViewSource.GetDefaultView(LRLedgerGrid.ItemsSource);
                if (cv != null)
                {
                    cv.Filter = null;
                    cv.Refresh();
                }
            }

            TabLRLedger.Style = (Style)FindResource("ActiveTabButtonStyle");
            TabDashboard.Style = (Style)FindResource("TabButtonStyle");
            TabDeliveryChallans.Style = (Style)FindResource("TabButtonStyle");
            TabTrackingLedger.Style = (Style)FindResource("TabButtonStyle");
            TabPartyLedger.Style = (Style)FindResource("TabButtonStyle");
            TabBillLedger.Style = (Style)FindResource("TabButtonStyle");
            TabVehicleLedger.Style = (Style)FindResource("TabButtonStyle");
            TabCBSLedger.Style = (Style)FindResource("TabButtonStyle");
            if (PageTitle != null) PageTitle.Text = "LR Ledger";
            LoadLRColumnSettings();

            // Always open LR ledger with greatest LR number first.
            LRVM?.SetSort("LRNo", false);
            if (LRLedgerGrid != null)
            {
                foreach (var c in LRLedgerGrid.Columns)
                {
                    string h = (c.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", "");
                    c.Header = h;
                    c.SortDirection = null;
                }
                var lrCol = LRLedgerGrid.Columns.FirstOrDefault(c =>
                    string.Equals((c as DataGridTemplateColumn)?.SortMemberPath, "LRNo", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals((c as DataGridTextColumn)?.Binding is Binding b ? b.Path?.Path : null, "LRNo", StringComparison.OrdinalIgnoreCase));
                if (lrCol != null)
                {
                    lrCol.SortDirection = System.ComponentModel.ListSortDirection.Descending;
                    lrCol.Header = (lrCol.Header?.ToString() ?? "LR No.") + " ▼";
                }
            }

            ApplyLRSortClean("LRNo", false);
            EnsureLRColumnVisible("Total Freight", 90);
            EnforceLRLockedColumnsReadOnly();
            LRUpdatePageUI();
            ApplyLRHeaderFilter();
        }
        private void EnforceLRLockedColumnsReadOnly()
        {
            if (LRLedgerGrid == null) return;
            foreach (var col in LRLedgerGrid.Columns)
            {
                var h = NormalizeHeaderForSort((col.Header ?? string.Empty).ToString());
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
        private void EnsureLRColumnVisible(string headerName, double minWidth)
        {
            if (LRLedgerGrid == null || string.IsNullOrWhiteSpace(headerName)) return;
            foreach (var col in LRLedgerGrid.Columns)
            {
                var h = (col.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", "").Trim();
                if (!string.Equals(h, headerName, StringComparison.OrdinalIgnoreCase)) continue;
                col.Visibility = Visibility.Visible;
                if (col.Width.DisplayValue < minWidth) col.Width = new DataGridLength(minWidth);
                break;
            }
        }
        private void OpenDeliveryChallans_Click(object sender, RoutedEventArgs e) { DashboardView.Visibility = Visibility.Collapsed; LRLedgerView.Visibility = Visibility.Collapsed; DeliveryChallanView.Visibility = Visibility.Visible; TrackingLedgerView.Visibility = Visibility.Collapsed; PartyLedgerView.Visibility = Visibility.Collapsed; BillLedgerView.Visibility = Visibility.Collapsed; VehicleLedgerView.Visibility = Visibility.Collapsed; CBSLedgerView.Visibility = Visibility.Collapsed; TabDeliveryChallans.Style = (Style)FindResource("ActiveTabButtonStyle"); TabDashboard.Style = (Style)FindResource("TabButtonStyle"); TabLRLedger.Style = (Style)FindResource("TabButtonStyle"); TabTrackingLedger.Style = (Style)FindResource("TabButtonStyle"); TabPartyLedger.Style = (Style)FindResource("TabButtonStyle"); TabBillLedger.Style = (Style)FindResource("TabButtonStyle"); TabVehicleLedger.Style = (Style)FindResource("TabButtonStyle"); TabCBSLedger.Style = (Style)FindResource("TabButtonStyle"); if (PageTitle != null) PageTitle.Text = "Delivery Challan List"; VM.EnsurePageLoaded(); SyncAllChallanBillingFromLR(); UpdatePageUI(); }
        private void OpenTrackingLedger_Click(object sender, RoutedEventArgs e) { DashboardView.Visibility = Visibility.Collapsed; DeliveryChallanView.Visibility = Visibility.Collapsed; LRLedgerView.Visibility = Visibility.Collapsed; TrackingLedgerView.Visibility = Visibility.Visible; PartyLedgerView.Visibility = Visibility.Collapsed; BillLedgerView.Visibility = Visibility.Collapsed; VehicleLedgerView.Visibility = Visibility.Collapsed; CBSLedgerView.Visibility = Visibility.Collapsed; if (TrackingLedgerView.DataContext == null) TrackingLedgerView.DataContext = TrackingVM; if (TrackingLedgerGrid != null) TrackingLedgerGrid.ItemsSource = TrackingVM.Entries; TabTrackingLedger.Style = (Style)FindResource("ActiveTabButtonStyle"); TabDashboard.Style = (Style)FindResource("TabButtonStyle"); TabDeliveryChallans.Style = (Style)FindResource("TabButtonStyle"); TabLRLedger.Style = (Style)FindResource("TabButtonStyle"); TabPartyLedger.Style = (Style)FindResource("TabButtonStyle"); TabBillLedger.Style = (Style)FindResource("TabButtonStyle"); TabVehicleLedger.Style = (Style)FindResource("TabButtonStyle"); TabCBSLedger.Style = (Style)FindResource("TabButtonStyle"); TrackingVM.FilteredEntriesCount = TrackingVM.Entries.Count; if (PageTitle != null) PageTitle.Text = "Tracking Ledger"; }
        private void OpenTrackingEntryForm_Click(object sender, RoutedEventArgs e) { var entry = TrackingLedgerGrid?.SelectedItem as TrackingEntry; if (entry == null) { MessageBox.Show("Select a tracking entry to edit.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information); return; } var form = new TrackingEntryFormWindow(entry); form.Owner = this; form.Closed += (_, __) => { TrackingVM.UpdateEntry(entry); var repo = new TrackingRepository(); var reports = repo.GetReportingTracks(entry.Id); if (reports.Count > 0) entry.LatestReport = $"{reports[reports.Count - 1].ReportDateTime:dd-MMM HH:mm} - {reports[reports.Count - 1].Remarks}"; RefreshDashboard(); }; form.Show(); }
        private async void AppVersionText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { await App.CheckForUpdatesOnDemandAsync(); }
        private void TrackingGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { OpenTrackingEntryForm_Click(sender, e); }
        private void TrackingLedgerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void QuickReportBox_GotFocus(object sender, RoutedEventArgs e) { if (QuickReportBox.Text == "Enter report update...") QuickReportBox.Text = string.Empty; }
        private void QuickAddReport_Click(object sender, RoutedEventArgs e) { var entry = TrackingLedgerGrid?.SelectedItem as TrackingEntry; if (entry == null || entry.Id <= 0) { MessageBox.Show("Select a tracking entry first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information); return; } var remarks = QuickReportBox.Text?.Trim(); if (string.IsNullOrWhiteSpace(remarks) || remarks == "Enter report update...") { MessageBox.Show("Enter report remarks.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; } try { var track = new ReportingTrackEntry { TrackingEntryId = entry.Id, ReportDateTime = DateTime.Now, Remarks = remarks }; new TrackingRepository().AddReportingTrack(track); entry.ReportTracks.Add(track); entry.LatestReport = $"{track.ReportDateTime:dd-MMM HH:mm} - {track.Remarks}"; QuickReportBox.Text = "Enter report update..."; } catch (Exception ex) { MessageBox.Show("Unable to add report: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); } }
        private void ToggleTrackingDetails_Click(object sender, RoutedEventArgs e) { var button = sender as System.Windows.Controls.Button; if (button == null) return; var entry = button.DataContext as TrackingEntry; if (entry == null) return; var row = TrackingLedgerGrid?.ItemContainerGenerator.ContainerFromItem(entry) as System.Windows.Controls.DataGridRow; if (row != null) row.DetailsVisibility = row.DetailsVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }
        private void OpenLRForm_Click(object sender, RoutedEventArgs e) { var form = new LRFormWindow(VM.Entries, LRVM.Entries); form.Owner = this; form.Closed += (_, __) => { if (!form.WasSaved) return; var entry = form.Result; if (entry == null) return; try { int maxSr = 0; foreach (var lrItem in LRVM.Entries) { if (lrItem.Sr > maxSr) maxSr = lrItem.Sr; } entry.Sr = maxSr + 1; LRVM.Entries.Add(entry); LRVM.RefreshAfterDelete(); SyncAllChallanBillingFromLR(); LRRefreshFilteredSummary(); } catch (Exception ex) { MessageBox.Show("Unable to save LR entry: " + ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error); } }; form.Show(); }
        private void LRRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (LRSearchBox != null) LRSearchBox.Text = string.Empty;
            _lrHeaderFilters.Clear();

            if (LRLedgerGrid != null)
            {
                try
                {
                    // Finish pending edit first; CollectionView.Filter cannot be changed during edit transaction.
                    LRLedgerGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                    LRLedgerGrid.CommitEdit(DataGridEditingUnit.Row, true);
                }
                catch
                {
                    try
                    {
                        LRLedgerGrid.CancelEdit(DataGridEditingUnit.Cell);
                        LRLedgerGrid.CancelEdit(DataGridEditingUnit.Row);
                    }
                    catch { }
                }

                var cv = CollectionViewSource.GetDefaultView(LRLedgerGrid.ItemsSource);
                if (cv != null)
                {
                    if (cv is System.ComponentModel.IEditableCollectionView editableView)
                    {
                        if (editableView.IsEditingItem) editableView.CommitEdit();
                        if (editableView.IsAddingNew) editableView.CommitNew();
                    }
                    cv.Filter = null;
                    cv.Refresh();
                }

                LRLedgerGrid.UnselectAllCells();
            }

            ApplyLRHeaderFilterIndicators();
            LRRefreshFilteredSummary();
        }
        private void ImportLR_Click(object sender, RoutedEventArgs e) { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "CSV Files|*.csv", Title = "Select LR Import File" }; if (dialog.ShowDialog() != true) return; try { var lines = System.IO.File.ReadAllLines(dialog.FileName); if (lines.Length < 2) { MessageBox.Show("CSV file has no data rows.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; } var headers = SplitCsvLine(lines[0]); var colMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase); for (int i = 0; i < headers.Length; i++) { var key = headers[i].Trim().Replace(".", "").Replace(" ", ""); if (!colMap.ContainsKey(key)) colMap[key] = new List<int>(); colMap[key].Add(i); } var repo = new LRRepository(); int imported = 0, errors = 0; var progress = ImportProgressBar; var status = ImportStatusText; if (progress != null) { progress.Visibility = Visibility.Visible; progress.Maximum = lines.Length - 1; progress.Value = 0; } if (status != null) status.Visibility = Visibility.Visible; for (int i = 1; i < lines.Length; i++) { try {                         var parts = SplitCsvLine(lines[i]); var entry = new LREntry(); entry.LRNo = GetCol(parts, colMap, "LRNo") ?? GetCol(parts, colMap, "LRNO") ?? GetCol(parts, colMap, "LR"); entry.Date = ParseDate(GetCol(parts, colMap, "Date") ?? GetCol(parts, colMap, "DATE")); entry.ConsignorName = GetCol(parts, colMap, "ConsignorName") ?? GetCol(parts, colMap, "Consignor"); entry.ConsignorAddress = GetCol(parts, colMap, "ConsignorAddress"); entry.ConsignorGST = GetCol(parts, colMap, "ConsignorGST"); entry.ConsigneeName = GetCol(parts, colMap, "ConsigneeName") ?? GetCol(parts, colMap, "Consignee"); entry.ConsigneeAddress = GetCol(parts, colMap, "ConsigneeAddress"); entry.ConsigneeGST = GetCol(parts, colMap, "ConsigneeGST"); entry.From = GetCol(parts, colMap, "From") ?? GetCol(parts, colMap, "FROM"); entry.To = GetCol(parts, colMap, "To") ?? GetCol(parts, colMap, "TO"); entry.VehicleNo = GetCol(parts, colMap, "VehicleNo") ?? GetCol(parts, colMap, "Vehicle"); entry.VehicleType = GetCol(parts, colMap, "VehicleType") ?? GetCol(parts, colMap, "Type"); entry.SizeL = ParseDecimal(GetCol(parts, colMap, "L") ?? GetCol(parts, colMap, "SizeL")); entry.SizeW = ParseDecimal(GetCol(parts, colMap, "W") ?? GetCol(parts, colMap, "SizeW")); entry.SizeH = ParseDecimal(GetCol(parts, colMap, "H") ?? GetCol(parts, colMap, "SizeH")); entry.ActualWeight = ParseDecimal(GetCol(parts, colMap, "ActualWeight") ?? GetCol(parts, colMap, "Actual Weight")); entry.ChargedWeight = ParseDecimal(GetCol(parts, colMap, "ChargedWeight") ?? GetCol(parts, colMap, "Charged Weight")); int.TryParse(GetCol(parts, colMap, "PKG"), out int pkg); entry.PKG = pkg; entry.PkgType = GetCol(parts, colMap, "PkgType") ?? GetCol(parts, colMap, "PackageType") ?? GetCol(parts, colMap, "Pkg Type"); entry.Description = GetCol(parts, colMap, "Description"); entry.Invoice = GetCol(parts, colMap, "Invoice"); entry.Value = GetCol(parts, colMap, "Value"); entry.CHNo = GetCol(parts, colMap, "CHNo") ?? GetCol(parts, colMap, "CHNO") ?? GetCol(parts, colMap, "ChallanNo"); entry.TotalFreight = ParseDecimal(GetCol(parts, colMap, "TotalFreight") ?? GetCol(parts, colMap, "Freight")); entry.Hamali = ParseDecimal(GetCol(parts, colMap, "Hamali")); entry.Detention = ParseDecimal(GetCol(parts, colMap, "Detention")); entry.Others = ParseDecimal(GetCol(parts, colMap, "Others") ?? GetCol(parts, colMap, "Other") ?? GetCol(parts, colMap, "OTHR")); entry.StCharge = ParseDecimal(GetCol(parts, colMap, "StCharge") ?? GetCol(parts, colMap, "St. Charge") ?? GetCol(parts, colMap, "StChargeAmount")); entry.NEFT = ParseDecimal(GetCol(parts, colMap, "NEFT")); entry.CASH = ParseDecimal(GetCol(parts, colMap, "CASH")); entry.TDS = ParseDecimal(GetCol(parts, colMap, "TDS")); entry.Ded = ParseDecimal(GetCol(parts, colMap, "Ded") ?? GetCol(parts, colMap, "DED")); entry.BillNo = GetCol(parts, colMap, "BillNo") ?? GetCol(parts, colMap, "BILLNO"); entry.BillDate = ParseNullableDate(GetCol(parts, colMap, "BillDate")); entry.BILL = ParseDecimal(GetCol(parts, colMap, "BILL")); entry.BillParty = GetCol(parts, colMap, "BillParty"); entry.Broker = GetCol(parts, colMap, "Broker"); entry.FrtType = GetCol(parts, colMap, "FrtType") ?? GetCol(parts, colMap, "FrtType"); entry.PayType = GetCol(parts, colMap, "PayType") ?? GetCol(parts, colMap, "ToPayToBeBilled") ?? GetCol(parts, colMap, "To Pay/To Be Billed"); entry.Comm = ParseDecimal(GetCol(parts, colMap, "Comm")); entry.Paid = GetCol(parts, colMap, "Paid"); repo.Upsert(entry); imported++; } catch { errors++; } if (progress != null) progress.Value = i; } if (progress != null) progress.Visibility = Visibility.Collapsed; if (status != null) status.Text = $"Imported: {imported}, Errors: {errors}"; LRVM.RefreshAfterDelete(); SyncAllChallanBillingFromLR(); LRUpdatePageUI(); MessageBox.Show($"Import complete.\nImported: {imported}\nErrors: {errors}", "Import Result", MessageBoxButton.OK, imported > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning); } catch (Exception ex) { MessageBox.Show("Import failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); } }
        private void ShowLRColumnsMenu_Click(object sender, RoutedEventArgs e) { if (!(sender is System.Windows.Controls.Button button)) return; _lrColumnsMenu = BuildLRColumnsMenu(); _lrColumnsMenu.PlacementTarget = button; _lrColumnsMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; _lrColumnsMenu.IsOpen = true; }
        private static string LRSettingsPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Awagaman ERP", "lr_column_settings.json");
        private void SaveLRColumnSettings() { try { var lines = new List<string>(); lines.Add($"_SortColumn:{LRVM?.GetSortColumn() ?? ""}"); lines.Add($"_SortAscending:{LRVM?.IsCurrentSortAscending}"); foreach (var col in LRLedgerGrid.Columns) { var h = (col.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", "").Trim(); if (!string.IsNullOrEmpty(h)) lines.Add(col.Visibility == Visibility.Visible ? $"1:{h}:{(int)col.Width.DisplayValue}" : $"0:{h}:{(int)col.Width.DisplayValue}"); } var dir = System.IO.Path.GetDirectoryName(LRSettingsPath); if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllText(LRSettingsPath, string.Join("\n", lines)); } catch { } }
        private void LoadLRColumnSettings() { try { var path = LRSettingsPath; if (!System.IO.File.Exists(path)) return; string sortCol = ""; bool sortAsc = true; foreach (var line in System.IO.File.ReadAllLines(path)) { if (line.StartsWith("_SortColumn:")) { sortCol = line.Substring("_SortColumn:".Length); continue; } if (line.StartsWith("_SortAscending:")) { bool.TryParse(line.Substring("_SortAscending:".Length), out sortAsc); continue; } var parts = line.Split(':'); if (parts.Length >= 2) { bool vis = parts[0] == "1"; var h = parts[1]; foreach (var col in LRLedgerGrid.Columns) { var ch = (col.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", "").Trim(); if (string.Equals(ch, h, StringComparison.OrdinalIgnoreCase)) { col.Visibility = vis ? Visibility.Visible : Visibility.Collapsed; if (parts.Length >= 3 && double.TryParse(parts[2], out double w) && w > 10) col.Width = new DataGridLength(w); break; } } } } LRVM?.SetSort(sortCol, sortAsc); } catch { } }
        private void ImportChallan_Click(object sender, RoutedEventArgs e) { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "CSV Files|*.csv", Title = "Select Challan Import File" }; if (dialog.ShowDialog() != true) return; if (dialog.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || dialog.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)) { MessageBox.Show("Please export your Excel file as CSV first.", "Format Not Supported", MessageBoxButton.OK, MessageBoxImage.Information); return; } try { var lines = System.IO.File.ReadAllLines(dialog.FileName); if (lines.Length < 2) { MessageBox.Show("CSV file has no data rows.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; } var headers = SplitCsvLine(lines[0]); var colMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase); for (int i = 0; i < headers.Length; i++) { var key = headers[i].Trim().Replace(".", "").Replace(" ", ""); if (!colMap.ContainsKey(key)) colMap[key] = new List<int>(); colMap[key].Add(i); } var repo = new ChallanRepository(); int imported = 0, errors = 0; int maxSr = repo.GetMaxSr(); var progress = ImportProgressBar; var status = ImportStatusText; if (progress != null) { progress.Visibility = Visibility.Visible; progress.Maximum = lines.Length - 1; progress.Value = 0; } if (status != null) status.Visibility = Visibility.Visible; for (int i = 1; i < lines.Length; i++) { try { var parts = SplitCsvLine(lines[i]); var entry = new ChallanEntry { Sr = ++maxSr, ChallanNumber = GetCol(parts, colMap, "ChallanNumber") ?? GetCol(parts, colMap, "ChallanNo") ?? GetCol(parts, colMap, "CHALLANNO"), Date = ParseDate(GetCol(parts, colMap, "Date") ?? GetCol(parts, colMap, "DATE")), LRNumber = GetCol(parts, colMap, "LRNumber") ?? GetCol(parts, colMap, "LRNumber") ?? GetCol(parts, colMap, "LRNO"), BrokerName = GetCol(parts, colMap, "BrokerName") ?? GetCol(parts, colMap, "Broker"), From = GetCol(parts, colMap, "From") ?? GetCol(parts, colMap, "FROM"), To = GetCol(parts, colMap, "To") ?? GetCol(parts, colMap, "TO"), VehicleNumber = GetCol(parts, colMap, "VehicleNumber") ?? GetCol(parts, colMap, "VehicleNo") ?? GetCol(parts, colMap, "VEHICLENO"), VehicleType = GetCol(parts, colMap, "VehicleType"), DriverName = GetCol(parts, colMap, "DriverName") ?? GetCol(parts, colMap, "Driver"), DriverMobile = GetCol(parts, colMap, "DriverMobile") ?? GetCol(parts, colMap, "DriverMobile"), EngineNo = GetCol(parts, colMap, "EngineNo"), LicenceNo = GetCol(parts, colMap, "LicenceNo"), PolicyNo = GetCol(parts, colMap, "PolicyNo"), ChassisNo = GetCol(parts, colMap, "ChassisNo"), OwnerName = GetCol(parts, colMap, "OwnerName") ?? GetCol(parts, colMap, "Owner"), PAN = GetCol(parts, colMap, "PAN"), LorryHire = ParseDecimal(GetCol(parts, colMap, "LorryHire") ?? GetCol(parts, colMap, "LorryHire")), LessTDS = ParseDecimal(GetCol(parts, colMap, "LessTDS")), AdvanceAmount = ParseDecimal(GetCol(parts, colMap, "AdvanceAmount") ?? GetCol(parts, colMap, "Advance")), AdvanceNEFT = ParseDecimal(GetCol(parts, colMap, "AdvanceNEFT")), AdvanceCash = ParseDecimal(GetCol(parts, colMap, "AdvanceCash")), AdvanceDate = ParseNullableDate(GetCol(parts, colMap, "AdvanceDate")), Detention = ParseDecimal(GetCol(parts, colMap, "Detention")), Hamali = ParseDecimal(GetCol(parts, colMap, "Hamali")), Deduction = ParseDecimal(GetCol(parts, colMap, "Deduction")), BalancePaidNEFT = ParseDecimal(GetCol(parts, colMap, "BalancePaidNEFT")), BalancePaidCash = ParseDecimal(GetCol(parts, colMap, "BalancePaidCash")), BalancePaidDate = ParseNullableDate(GetCol(parts, colMap, "BalancePaidDate")), PaidTo = GetCol(parts, colMap, "PaidTo"), Remarks = GetCol(parts, colMap, "Remarks"), BillAmount = ParseDecimal(GetCol(parts, colMap, "BillAmount")), Margin = ParseDecimal(GetCol(parts, colMap, "Margin")) }; entry.SuppressCalculations = true; entry.RecalculateBalance(); repo.Upsert(entry); imported++; } catch { errors++; } if (progress != null) progress.Value = i; if (status != null) status.Text = $"Importing {i}/{lines.Length - 1}"; } if (progress != null) progress.Visibility = Visibility.Collapsed; if (status != null) status.Text = $"Imported: {imported}, Errors: {errors}"; VM.RefreshAfterDelete(); SyncAllChallanBillingFromLR(); UpdatePageUI(); RefreshDashboard(); MessageBox.Show($"Challan import complete.\nImported: {imported}\nErrors: {errors}", "Import Result", MessageBoxButton.OK, imported > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning); } catch (Exception ex) { MessageBox.Show("Import failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); } }
        private void EditSelected_Click(object sender, RoutedEventArgs e) { var item = LedgerGrid.SelectedItem as ChallanEntry; if (item == null) return; var form = new ChallanFormWindow(item, VM.Entries, VM.GetRepository()); form.Owner = this; form.Closed += (_, __) => { if (!form.WasSaved) return; var updated = form.Result; updated.RecalculateBalance(); try { VM.GetRepository().Upsert(updated); } catch { } var idx = VM.Entries.IndexOf(item); if (idx >= 0) VM.Entries[idx] = updated; SyncLinkedLREntriesFromChallan(updated); SyncAllChallanBillingFromLR(); }; form.Show(); }
        private void MakeBuilty_Click(object sender, RoutedEventArgs e)
        {
            var item = LedgerGrid.SelectedItem as ChallanEntry;
            if (item == null)
            {
                MessageBox.Show("Select a Challan entry first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!string.IsNullOrWhiteSpace(item.LRNumber))
            {
                var result = MessageBox.Show($"Challan '{item.ChallanNumber}' already has LR No. '{item.LRNumber}'.\nCreate another LR?", "LR Exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }

            var form = new LRFormWindow(VM.Entries, LRVM.Entries, prefillFrom: item);
            form.Owner = this;
            form.Closed += (_, __) =>
            {
                if (!form.WasSaved) return;
                var entry = form.Result;
                if (entry == null) return;
                try
                {
                    int maxSr = 0;
                    foreach (var lrItem in LRVM.Entries) { if (lrItem.Sr > maxSr) maxSr = lrItem.Sr; }
                    entry.Sr = maxSr + 1;
                    LRVM.Entries.Add(entry);
                    UpdateChallanLRNumbers(item.Id, entry.LRNo);
                    RefreshDashboard();
                    VM.RefreshAfterDelete();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unable to save LR entry: " + ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            form.Show();
        }
        private void DeleteSelected_Click(object sender, RoutedEventArgs e) { if (LedgerGrid == null) return; var selected = LedgerGrid.SelectedItems.Cast<ChallanEntry>().ToList(); if (selected.Count == 0) { MessageBox.Show("Select Challan entries to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information); return; } if (MessageBox.Show($"Delete {selected.Count} challan entr{(selected.Count == 1 ? "y" : "ies")}?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; foreach (var entry in selected) { try { using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString)) { conn.Open(); using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM Challans WHERE Id = @id"; cmd.Parameters.AddWithValue("@id", entry.Id); cmd.ExecuteNonQuery(); } } } catch { } } VM.RefreshAfterDelete(); RefreshFilteredSummary(); RefreshDashboard(); }
        private void LedgerPreviewKeyDown(object sender, KeyEventArgs e) { DataGrid_PreviewKeyDown(sender, e); }
        private void OpenChallanCommentPopup(ChallanEntry entry)
        {
            if (entry == null || entry.Id <= 0) return;
            var popup = new CommentPopupWindow(entry.Id, entry.ChallanNumber);
            popup.Owner = this;
            popup.Show();
            VM?.RefreshAfterDelete();
        }

        private void OpenLRCommentPopup(LREntry entry)
        {
            if (entry == null || entry.Id <= 0) return;
            var popup = new LRCommentPopupWindow(entry.Id, entry.LRNo);
            popup.Owner = this;
            popup.Show();
            LRVM?.RefreshAfterDelete();
        }

        private void OpenBillCommentPopup(BillEntry entry)
        {
            if (entry == null || entry.Id <= 0) return;
            var title = string.IsNullOrWhiteSpace(entry.BillNo) ? $"Bill Id {entry.Id}" : $"Bill {entry.BillNo}";
            var popup = new BillCommentPopupWindow(entry.Id, title);
            popup.Owner = this;
            popup.Show();
            BillVM?.RefreshAfterDelete();
        }

        private void AddChallanComment_Click(object sender, RoutedEventArgs e) { var entry = (sender as System.Windows.Controls.MenuItem)?.Tag as ChallanEntry; OpenChallanCommentPopup(entry); }
        private void AddLRComment_Click(object sender, RoutedEventArgs e) { var entry = (sender as System.Windows.Controls.MenuItem)?.Tag as LREntry; OpenLRCommentPopup(entry); }
        private void DashboardMakeLR_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            var entry = btn?.Tag as ChallanEntry;
            if (entry == null) return;

            var form = new LRFormWindow(VM.Entries, LRVM.Entries, prefillFrom: entry);
            if (!string.IsNullOrWhiteSpace(entry.LRNumber)) form.CurrentEntry.LRNo = entry.LRNumber.Trim();
            form.Owner = this;
            form.Closed += (_, __) =>
            {
                if (!form.WasSaved) return;
                var lrEntry = form.Result;
                if (lrEntry == null) return;
                try
                {
                    int maxSr = 0;
                    foreach (var lrItem in LRVM.Entries) { if (lrItem.Sr > maxSr) maxSr = lrItem.Sr; }
                    lrEntry.Sr = maxSr + 1;
                    LRVM.Entries.Add(lrEntry);
                    UpdateChallanLRNumbers(entry.Id, lrEntry.LRNo);
                    LRVM.RefreshAfterDelete();
                    VM.RefreshAfterDelete();
                    RefreshDashboard();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unable to save LR entry: " + ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            form.Show();
        }
        private void UpdateChallanLRNumbers(int challanId, string newLrNo)
        {
            if (challanId <= 0 || string.IsNullOrWhiteSpace(newLrNo)) return;
            using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
            {
                conn.Open();
                string existing = string.Empty;
                using (var read = conn.CreateCommand())
                {
                    read.CommandText = "SELECT LRNumber FROM Challans WHERE Id = @id;";
                    read.Parameters.AddWithValue("@id", challanId);
                    existing = (read.ExecuteScalar() as string) ?? string.Empty;
                }

                var lrList = existing
                    .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (!lrList.Contains(newLrNo.Trim(), StringComparer.OrdinalIgnoreCase)) lrList.Add(newLrNo.Trim());
                var merged = string.Join(", ", lrList);

                using (var upd = conn.CreateCommand())
                {
                    upd.CommandText = "UPDATE Challans SET LRNumber = @lr WHERE Id = @id;";
                    upd.Parameters.AddWithValue("@lr", merged);
                    upd.Parameters.AddWithValue("@id", challanId);
                    upd.ExecuteNonQuery();
                }
            }
            SyncAllChallanBillingFromLR();
        }

        private void SyncLinkedLREntriesFromChallan(ChallanEntry challan)
        {
            if (challan == null) return;
            var challanNo = (challan.ChallanNumber ?? string.Empty).Trim();
            var lrTokens = SplitLrNumbers(challan.LRNumber).ToList();
            if (string.IsNullOrWhiteSpace(challanNo) && lrTokens.Count == 0) return;

            try
            {
                using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                {
                    conn.Open();
                    var pNames = new List<string>();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Parameters.AddWithValue("@chNo", challanNo);
                        for (int i = 0; i < lrTokens.Count; i++)
                        {
                            var pn = "@lr" + i;
                            pNames.Add(pn);
                            cmd.Parameters.AddWithValue(pn, lrTokens[i]);
                        }

                        var lrIn = pNames.Count > 0 ? $" OR LRNo IN ({string.Join(",", pNames)}) " : string.Empty;
                        cmd.CommandText = $@"UPDATE LREntries
SET FromLocation = @fromLoc,
    ToLocation = @toLoc,
    VehicleNo = @vehicleNo,
    VehicleType = @vehicleType,
    Broker = @broker
WHERE (TRIM(COALESCE(CHNo,'')) = TRIM(COALESCE(@chNo,''))) {lrIn};";
                        cmd.Parameters.AddWithValue("@fromLoc", (object)(challan.From ?? string.Empty) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@toLoc", (object)(challan.To ?? string.Empty) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@vehicleNo", (object)(challan.VehicleNumber ?? string.Empty) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@vehicleType", (object)(challan.VehicleType ?? string.Empty) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@broker", (object)(challan.BrokerName ?? string.Empty) ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }

                if (LRVM?.Entries != null)
                {
                    var set = new HashSet<string>(lrTokens, StringComparer.OrdinalIgnoreCase);
                    foreach (var lr in LRVM.Entries)
                    {
                        var lrChNo = (lr.CHNo ?? string.Empty).Trim();
                        var lrNo = (lr.LRNo ?? string.Empty).Trim();
                        if ((!string.IsNullOrWhiteSpace(challanNo) && string.Equals(lrChNo, challanNo, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrWhiteSpace(lrNo) && set.Contains(lrNo)))
                        {
                            lr.From = challan.From;
                            lr.To = challan.To;
                            lr.VehicleNo = challan.VehicleNumber;
                            lr.VehicleType = challan.VehicleType;
                            lr.Broker = challan.BrokerName;
                        }
                    }
                }
            }
            catch { }
        }

        private void BackfillAllLinkedLREntriesFromChallans()
        {
            try
            {
                var challans = new List<ChallanEntry>();
                using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"SELECT Id, ChallanNumber, LRNumber, BrokerName, FromLocation, ToLocation, VehicleNumber, VehicleType
                                            FROM Challans
                                            ORDER BY Id;";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                challans.Add(new ChallanEntry
                                {
                                    Id = Convert.ToInt32(r["Id"]),
                                    ChallanNumber = (r["ChallanNumber"] as string) ?? string.Empty,
                                    LRNumber = (r["LRNumber"] as string) ?? string.Empty,
                                    BrokerName = (r["BrokerName"] as string) ?? string.Empty,
                                    From = (r["FromLocation"] as string) ?? string.Empty,
                                    To = (r["ToLocation"] as string) ?? string.Empty,
                                    VehicleNumber = (r["VehicleNumber"] as string) ?? string.Empty,
                                    VehicleType = (r["VehicleType"] as string) ?? string.Empty
                                });
                            }
                        }
                    }
                }

                foreach (var ch in challans)
                {
                    SyncLinkedLREntriesFromChallan(ch);
                }
            }
            catch { }
        }
        private void LoadGridSettings() { try { var path = GridSettingsPath; if (!System.IO.File.Exists(path)) return; var json = System.IO.File.ReadAllText(path); var data = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); if (data == null) return; if (data.TryGetValue("ChallanRowHeight", out var v) && LedgerGrid != null) LedgerGrid.RowHeight = Convert.ToDouble(v); if (data.TryGetValue("LRRowHeight", out v) && LRLedgerGrid != null) LRLedgerGrid.RowHeight = Convert.ToDouble(v); if (data.TryGetValue("TrackingRowHeight", out v) && TrackingLedgerGrid != null) TrackingLedgerGrid.RowHeight = Convert.ToDouble(v); if (data.TryGetValue("BillRowHeight", out v) && BillLedgerGrid != null) BillLedgerGrid.RowHeight = Convert.ToDouble(v); if (data.TryGetValue("CBSRowHeight", out v) && CBSGrid != null) CBSGrid.RowHeight = Convert.ToDouble(v); RestoreColumnWidthsFromDict(LedgerGrid, "Challan", data); RestoreColumnWidthsFromDict(LRLedgerGrid, "LR", data); RestoreColumnWidthsFromDict(TrackingLedgerGrid, "Tracking", data); RestoreColumnWidthsFromDict(BillLedgerGrid, "Bill", data); RestoreColumnWidthsFromDict(CBSGrid, "CBS", data); RestoreColumnWidthsFromDict(NewBookingGrid, "DashboardNewBooking", data); RestoreColumnWidthsFromDict(DashboardPendingBillGrid, "DashboardPendingBill", data); } catch { } }
        private void SaveGridSettings() { try { var data = new Dictionary<string, object>(); if (LedgerGrid != null) { data["ChallanRowHeight"] = LedgerGrid.RowHeight; SaveColumnWidthsToDict(LedgerGrid, "Challan", data); } if (LRLedgerGrid != null) { data["LRRowHeight"] = LRLedgerGrid.RowHeight; SaveColumnWidthsToDict(LRLedgerGrid, "LR", data); } if (TrackingLedgerGrid != null) { data["TrackingRowHeight"] = TrackingLedgerGrid.RowHeight; SaveColumnWidthsToDict(TrackingLedgerGrid, "Tracking", data); } if (BillLedgerGrid != null) { data["BillRowHeight"] = BillLedgerGrid.RowHeight; SaveColumnWidthsToDict(BillLedgerGrid, "Bill", data); } if (CBSGrid != null) { data["CBSRowHeight"] = CBSGrid.RowHeight; SaveColumnWidthsToDict(CBSGrid, "CBS", data); } SaveColumnWidthsToDict(NewBookingGrid, "DashboardNewBooking", data); SaveColumnWidthsToDict(DashboardPendingBillGrid, "DashboardPendingBill", data); var dir = System.IO.Path.GetDirectoryName(GridSettingsPath); if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllText(GridSettingsPath, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(data)); } catch { } }
        private void SaveColumnWidthsToDict(DataGrid grid, string prefix, Dictionary<string, object> data) { foreach (var col in grid.Columns) { var h = (col.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", "").Trim(); if (!string.IsNullOrEmpty(h)) data[$"{prefix}_W_{h}"] = col.Width.DisplayValue; } }
        private void RestoreColumnWidthsFromDict(DataGrid grid, string prefix, Dictionary<string, object> data) { if (grid == null || data == null) return; foreach (var col in grid.Columns) { var h = (col.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", "").Trim(); if (string.IsNullOrEmpty(h)) continue; var key = $"{prefix}_W_{h}"; if (data.TryGetValue(key, out var val) && val != null) { double w = Convert.ToDouble(val); if (w > 10) col.Width = new DataGridLength(w); } } }
        private static string GridSettingsPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Awagaman ERP", "grid_settings.json");
        private ContextMenu BuildLRColumnsMenu() { var menu = new ContextMenu(); foreach (var col in LRLedgerGrid.Columns) { var header = (col.Header?.ToString() ?? "").Replace(" ▲", "").Replace(" ▼", ""); if (string.IsNullOrEmpty(header)) continue; var column = col; var item = new System.Windows.Controls.MenuItem { Header = header, IsCheckable = true, IsChecked = column.Visibility == Visibility.Visible, StaysOpenOnClick = true }; item.Click += (_, __) => { column.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed; SaveLRColumnSettings(); }; menu.Items.Add(item); } return menu; }
        private void OpenLRFormatPreview(LREntry entry)
        {
            if (entry == null) return;
            try
            {
                var templatePath = ResolveLRFormatTemplatePath();
                if (string.IsNullOrWhiteSpace(templatePath) || !System.IO.File.Exists(templatePath))
                {
                    MessageBox.Show("LR format template not found.\nExpected file: LR format.png", "Template Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(templatePath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                var dialog = new Window
                {
                    Title = $"LR View - {entry.LRNo}",
                    Owner = this,
                    Width = Math.Min(1200, Math.Max(900, bmp.PixelWidth + 80)),
                    Height = Math.Min(900, Math.Max(700, bmp.PixelHeight + 120)),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = System.Windows.Media.Brushes.White
                };

                var root = new Grid { Margin = new Thickness(10) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var fitView = new Viewbox
                {
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly
                };

                var canvas = new Canvas
                {
                    Width = bmp.PixelWidth,
                    Height = bmp.PixelHeight,
                    Background = System.Windows.Media.Brushes.White
                };

                var image = new Image
                {
                    Source = bmp,
                    Width = bmp.PixelWidth,
                    Height = bmp.PixelHeight,
                    Stretch = System.Windows.Media.Stretch.Fill
                };
                canvas.Children.Add(image);

                var fields = new List<(string Key, string Label, double X, double Y, double W, double H, string Value)>
                {
                    ("lr_no", "LR No", 42, 34, 180, 24, $"{entry.LRNo}"),
                    ("date", "Date", 690, 34, 180, 24, $"{entry.Date:dd-MMM-yyyy}"),
                    ("consignor", "Consignor", 42, 74, 320, 24, $"{entry.ConsignorName}"),
                    ("consignor_addr", "Consignor Address", 42, 102, 320, 48, $"{entry.ConsignorAddress}"),
                    ("consignor_gst", "Consignor GST", 42, 130, 320, 24, $"{entry.ConsignorGST}"),
                    ("consignee", "Consignee", 42, 108, 320, 24, $"{entry.ConsigneeName}"),
                    ("consignee_addr", "Consignee Address", 42, 158, 320, 48, $"{entry.ConsigneeAddress}"),
                    ("consignee_gst", "Consignee GST", 42, 186, 320, 24, $"{entry.ConsigneeGST}"),
                    ("route", "From", 42, 214, 180, 24, $"{entry.From}"),
                    ("route_to", "To", 430, 214, 180, 24, $"{entry.To}"),
                    ("vehicle", "Vehicle No", 42, 242, 180, 24, $"{entry.VehicleNo}"),
                    ("vehicle_type", "Type", 430, 242, 140, 24, $"{entry.VehicleType}"),
                    ("actual_weight", "Actual Weight", 42, 270, 180, 24, $"{entry.ActualWeight:0.####################}"),
                    ("charged_weight", "Charged Weight", 250, 270, 160, 24, $"{entry.ChargedWeight:0.####################}"),
                    ("pay_type", "To Pay/To Be Billed", 430, 270, 260, 24, $"{entry.PayType}"),
                    ("no_of_pkg", "No. of Pkg", 42, 298, 160, 24, $"{entry.PKG}"),
                    ("package_type", "Package Type", 250, 298, 160, 24, $"{entry.PkgType}"),
                    ("size_l", "L", 430, 298, 70, 24, $"{entry.SizeL:0.####################}"),
                    ("size_w", "W", 520, 298, 70, 24, $"{entry.SizeW:0.####################}"),
                    ("size_h", "H", 610, 298, 70, 24, $"{entry.SizeH:0.####################}"),
                    ("description", "Description", 42, 326, 480, 48, $"{entry.Description}"),
                    ("invoice", "Invoice", 42, 354, 220, 24, $"{entry.Invoice}"),
                    ("value", "Value", 280, 354, 140, 24, $"{entry.Value}")
                };

                var savedLayout = LoadLRFormatLayout();
                var overlayMap = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
                var textMap = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
                bool editMode = false;
                bool dragging = false;
                Point dragStart = new Point();
                Point dragOrigin = new Point();
                Border draggingBorder = null;
                string selectedKey = null;
                Action autoSaveLayout = null;

                Action refreshOverlayTexts = () =>
                {
                    foreach (var f in fields)
                    {
                        if (!textMap.TryGetValue(f.Key, out var tb)) continue;
                        tb.Text = editMode ? $"{f.Label}: {f.Value}" : f.Value;
                    }
                };

                foreach (var f in fields)
                {
                    var layout = savedLayout.TryGetValue(f.Key, out var l) ? l : new LRFieldLayout { X = f.X, Y = f.Y, Width = f.W, Height = f.H, FontSize = 14, Bold = true };
                    var border = new Border
                    {
                        Tag = f.Key,
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 255, 255, 255)),
                        BorderBrush = System.Windows.Media.Brushes.Transparent,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(2, 0, 2, 0),
                        Width = layout.Width > 20 ? layout.Width : f.W,
                        Height = layout.Height > 12 ? layout.Height : f.H,
                        Child = new TextBlock
                        {
                            Text = f.Value ?? string.Empty,
                            FontSize = layout.FontSize <= 0 ? 14 : layout.FontSize,
                            FontWeight = layout.Bold ? FontWeights.SemiBold : FontWeights.Normal,
                            Foreground = System.Windows.Media.Brushes.Black,
                            TextWrapping = TextWrapping.Wrap,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    };
                    Canvas.SetLeft(border, layout.X);
                    Canvas.SetTop(border, layout.Y);
                    overlayMap[f.Key] = border;
                    textMap[f.Key] = border.Child as TextBlock;
                    canvas.Children.Add(border);

                    border.MouseLeftButtonDown += (_, ev) =>
                    {
                        selectedKey = f.Key;
                        foreach (var k in overlayMap.Keys)
                        {
                            overlayMap[k].BorderBrush = (editMode && string.Equals(k, selectedKey, StringComparison.OrdinalIgnoreCase))
                                ? System.Windows.Media.Brushes.OrangeRed
                                : (editMode ? System.Windows.Media.Brushes.DodgerBlue : System.Windows.Media.Brushes.Transparent);
                        }
                        if (!editMode) return;
                        dragging = true;
                        draggingBorder = border;
                        dragStart = ev.GetPosition(canvas);
                        dragOrigin = new Point(Canvas.GetLeft(border), Canvas.GetTop(border));
                        border.CaptureMouse();
                        ev.Handled = true;
                    };
                    border.MouseMove += (_, ev) =>
                    {
                        if (!editMode || !dragging || draggingBorder != border) return;
                        var cur = ev.GetPosition(canvas);
                        var dx = cur.X - dragStart.X;
                        var dy = cur.Y - dragStart.Y;
                        Canvas.SetLeft(border, Math.Max(0, dragOrigin.X + dx));
                        Canvas.SetTop(border, Math.Max(0, dragOrigin.Y + dy));
                    };
                    border.MouseLeftButtonUp += (_, ev) =>
                    {
                        if (draggingBorder == border)
                        {
                            dragging = false;
                            draggingBorder = null;
                            border.ReleaseMouseCapture();
                            autoSaveLayout();
                            ev.Handled = true;
                        }
                    };
                }

                fitView.Child = canvas;
                Grid.SetRow(fitView, 1);
                root.Children.Add(fitView);

                var topBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                var editToggle = new CheckBox { Content = "Edit Layout", VerticalAlignment = VerticalAlignment.Center };
                var fontIncBtn = new Button { Content = "A+", Width = 40, Height = 26, Margin = new Thickness(10, 0, 6, 0) };
                var fontDecBtn = new Button { Content = "A-", Width = 40, Height = 26, Margin = new Thickness(0, 0, 6, 0) };
                var boldBtn = new Button { Content = "B", Width = 32, Height = 26, FontWeight = FontWeights.Bold };
                var widthIncBtn = new Button { Content = "W+", Width = 40, Height = 26, Margin = new Thickness(10, 0, 6, 0) };
                var widthDecBtn = new Button { Content = "W-", Width = 40, Height = 26, Margin = new Thickness(0, 0, 6, 0) };
                var heightIncBtn = new Button { Content = "H+", Width = 40, Height = 26, Margin = new Thickness(0, 0, 6, 0) };
                var heightDecBtn = new Button { Content = "H-", Width = 40, Height = 26, Margin = new Thickness(0, 0, 6, 0) };
                var downloadBtn = new Button { Content = "Download", Width = 90, Height = 30, Padding = new Thickness(8, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0) };
                var printBtn = new Button { Content = "Print", Width = 70, Height = 30, Padding = new Thickness(8, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                var emailBtn = new Button { Content = "Email", Width = 70, Height = 30, Padding = new Thickness(8, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                var whatsappBtn = new Button { Content = "WhatsApp", Width = 90, Height = 30, Padding = new Thickness(8, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center };
                topBar.Children.Add(editToggle);
                topBar.Children.Add(fontIncBtn);
                topBar.Children.Add(fontDecBtn);
                topBar.Children.Add(boldBtn);
                topBar.Children.Add(widthIncBtn);
                topBar.Children.Add(widthDecBtn);
                topBar.Children.Add(heightIncBtn);
                topBar.Children.Add(heightDecBtn);
                topBar.Children.Add(downloadBtn);
                topBar.Children.Add(printBtn);
                topBar.Children.Add(emailBtn);
                topBar.Children.Add(whatsappBtn);
                Grid.SetRow(topBar, 0);
                root.Children.Add(topBar);

                Action<string> saveCanvasAsPng = filePath =>
                {
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)Math.Ceiling(canvas.Width),
                        (int)Math.Ceiling(canvas.Height),
                        96, 96,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(canvas);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using (var fs = new System.IO.FileStream(filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                    {
                        encoder.Save(fs);
                    }
                };
                autoSaveLayout = () =>
                {
                    var map = new Dictionary<string, LRFieldLayout>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in overlayMap)
                    {
                        var tb = kv.Value.Child as TextBlock;
                        map[kv.Key] = new LRFieldLayout
                        {
                            X = Canvas.GetLeft(kv.Value),
                            Y = Canvas.GetTop(kv.Value),
                            Width = kv.Value.Width,
                            Height = kv.Value.Height,
                            FontSize = tb?.FontSize ?? 14,
                            Bold = tb?.FontWeight == FontWeights.Bold || tb?.FontWeight == FontWeights.SemiBold
                        };
                    }
                    SaveLRFormatLayout(map);
                };

                editToggle.Checked += (_, __) =>
                {
                    editMode = true;
                    foreach (var kv in overlayMap)
                    {
                        kv.Value.BorderBrush = System.Windows.Media.Brushes.DodgerBlue;
                        kv.Value.Cursor = Cursors.SizeAll;
                    }
                    refreshOverlayTexts();
                };
                editToggle.Unchecked += (_, __) =>
                {
                    editMode = false;
                    foreach (var kv in overlayMap)
                    {
                        kv.Value.BorderBrush = System.Windows.Media.Brushes.Transparent;
                        kv.Value.Cursor = Cursors.Arrow;
                    }
                    refreshOverlayTexts();
                };
                fontIncBtn.Click += (_, __) =>
                {
                    if (!editMode || string.IsNullOrWhiteSpace(selectedKey) || !textMap.TryGetValue(selectedKey, out var tb)) return;
                    tb.FontSize = Math.Min(48, tb.FontSize + 1);
                    autoSaveLayout();
                };
                fontDecBtn.Click += (_, __) =>
                {
                    if (!editMode || string.IsNullOrWhiteSpace(selectedKey) || !textMap.TryGetValue(selectedKey, out var tb)) return;
                    tb.FontSize = Math.Max(8, tb.FontSize - 1);
                    autoSaveLayout();
                };
                boldBtn.Click += (_, __) =>
                {
                    if (!editMode || string.IsNullOrWhiteSpace(selectedKey) || !textMap.TryGetValue(selectedKey, out var tb)) return;
                    tb.FontWeight = tb.FontWeight == FontWeights.Bold ? FontWeights.Normal : FontWeights.Bold;
                    autoSaveLayout();
                };
                widthIncBtn.Click += (_, __) =>
                {
                    if (!editMode || string.IsNullOrWhiteSpace(selectedKey) || !overlayMap.TryGetValue(selectedKey, out var b)) return;
                    b.Width = Math.Min(900, b.Width + 10);
                    autoSaveLayout();
                };
                widthDecBtn.Click += (_, __) =>
                {
                    if (!editMode || string.IsNullOrWhiteSpace(selectedKey) || !overlayMap.TryGetValue(selectedKey, out var b)) return;
                    b.Width = Math.Max(30, b.Width - 10);
                    autoSaveLayout();
                };
                heightIncBtn.Click += (_, __) =>
                {
                    if (!editMode || string.IsNullOrWhiteSpace(selectedKey) || !overlayMap.TryGetValue(selectedKey, out var b)) return;
                    b.Height = Math.Min(300, b.Height + 6);
                    autoSaveLayout();
                };
                heightDecBtn.Click += (_, __) =>
                {
                    if (!editMode || string.IsNullOrWhiteSpace(selectedKey) || !overlayMap.TryGetValue(selectedKey, out var b)) return;
                    b.Height = Math.Max(18, b.Height - 6);
                    autoSaveLayout();
                };
                downloadBtn.Click += (_, __) =>
                {
                    try
                    {
                        var copyDialog = new Window
                        {
                            Title = "Select LR Copy Type",
                            Owner = dialog,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            Width = 320,
                            Height = 230,
                            ResizeMode = ResizeMode.NoResize,
                            Background = System.Windows.Media.Brushes.White
                        };
                        var copyRoot = new Grid { Margin = new Thickness(14) };
                        copyRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        copyRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        copyRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                        var copyTitle = new TextBlock
                        {
                            Text = "Choose copies to download",
                            FontSize = 14,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 0, 0, 10)
                        };
                        Grid.SetRow(copyTitle, 0);
                        copyRoot.Children.Add(copyTitle);

                        var cbConsignor = new CheckBox { Content = "Consignor Copy", IsChecked = true, Margin = new Thickness(0, 2, 0, 6) };
                        var cbConsignee = new CheckBox { Content = "Consignee Copy", Margin = new Thickness(0, 2, 0, 6) };
                        var cbLorry = new CheckBox { Content = "Lorry Copy", Margin = new Thickness(0, 2, 0, 6) };
                        var copyStack = new StackPanel();
                        copyStack.Children.Add(cbConsignor);
                        copyStack.Children.Add(cbConsignee);
                        copyStack.Children.Add(cbLorry);
                        Grid.SetRow(copyStack, 1);
                        copyRoot.Children.Add(copyStack);

                        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                        var okBtn = new Button { Content = "OK", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
                        var cancelBtn = new Button { Content = "Cancel", Width = 80, Height = 30, IsCancel = true };
                        okBtn.Click += (sOk, eOk) => copyDialog.DialogResult = true;
                        btnRow.Children.Add(okBtn);
                        btnRow.Children.Add(cancelBtn);
                        Grid.SetRow(btnRow, 2);
                        copyRoot.Children.Add(btnRow);

                        copyDialog.Content = copyRoot;
                        if (copyDialog.ShowDialog() != true) return;

                        var selectedCopies = new List<string>();
                        if (cbConsignor.IsChecked == true) selectedCopies.Add("CONSIGNOR COPY");
                        if (cbConsignee.IsChecked == true) selectedCopies.Add("CONSIGNEE COPY");
                        if (cbLorry.IsChecked == true) selectedCopies.Add("LORRY COPY");
                        if (selectedCopies.Count == 0)
                        {
                            MessageBox.Show("Select at least one copy type.", "LR Format", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        var sfd = new Microsoft.Win32.SaveFileDialog
                        {
                            Title = "Download LR Format",
                            Filter = "PNG Image|*.png|PDF Document|*.pdf",
                            FileName = $"{(entry.LRNo ?? "LR").Replace("/", "-")}_{(selectedCopies.Count == 1 ? (selectedCopies[0].StartsWith("LORRY") ? "Lorry" : selectedCopies[0].StartsWith("CONSIGNOR") ? "Consignor" : "Consignee") : "Copies")}"
                        };
                        if (sfd.ShowDialog(dialog) != true) return;
                        var selectedExt = (System.IO.Path.GetExtension(sfd.FileName) ?? string.Empty).ToLowerInvariant();

                        string ResolveCopyTemplatePath(string copyLabel)
                        {
                            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                            var roots = new[]
                            {
                                Environment.CurrentDirectory,
                                baseDir,
                                System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\")),
                                System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\..\")),
                                System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\..\..\")),
                            };

                            string[] names;
                            if (string.Equals(copyLabel, "CONSIGNOR COPY", StringComparison.OrdinalIgnoreCase))
                                names = new[] { "LR consginor.png", "LR consignor.png", "lr consginor.png", "lr consignor.png" };
                            else if (string.Equals(copyLabel, "LORRY COPY", StringComparison.OrdinalIgnoreCase))
                                names = new[] { "Lorry Lr.png", "Lorry LR.png", "lorry lr.png" };
                            else
                                names = new[] { "LR format.png" };

                            foreach (var r in roots)
                            {
                                foreach (var n in names)
                                {
                                    var p = System.IO.Path.Combine(r, n);
                                    if (System.IO.File.Exists(p)) return p;
                                }
                            }
                            return ResolveLRFormatTemplatePath();
                        }

                        System.Windows.Media.Imaging.BitmapImage LoadBitmapFromPath(string p)
                        {
                            var b = new System.Windows.Media.Imaging.BitmapImage();
                            b.BeginInit();
                            b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            b.UriSource = new Uri(p, UriKind.Absolute);
                            b.EndInit();
                            b.Freeze();
                            return b;
                        }

                        System.Windows.Media.Imaging.RenderTargetBitmap RenderBaseBitmap()
                        {
                            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                                (int)Math.Ceiling(canvas.Width),
                                (int)Math.Ceiling(canvas.Height),
                                96, 96,
                                System.Windows.Media.PixelFormats.Pbgra32);
                            rtb.Render(canvas);
                            return rtb;
                        }

                        void SaveBitmap(System.Windows.Media.Imaging.BitmapSource imageBmp, string path)
                        {
                            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(imageBmp));
                            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                            {
                                encoder.Save(fs);
                            }
                        }

                        void SaveBitmapAsPdf(System.Windows.Media.Imaging.BitmapSource imageBmp, string path)
                        {
                            byte[] jpegBytes;
                            using (var ms = new System.IO.MemoryStream())
                            {
                                var jpeg = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 90 };
                                jpeg.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(imageBmp));
                                jpeg.Save(ms);
                                jpegBytes = ms.ToArray();
                            }

                            int pxW = imageBmp.PixelWidth;
                            int pxH = imageBmp.PixelHeight;
                            var pageW = pxW * 72.0 / 96.0;
                            var pageH = pxH * 72.0 / 96.0;
                            var content = $"q {pageW:F2} 0 0 {pageH:F2} 0 0 cm /Im0 Do Q";

                            var offsets = new List<long>();
                            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                            using (var bw = new System.IO.BinaryWriter(fs, Encoding.ASCII))
                            {
                                void WriteAscii(string s) => bw.Write(Encoding.ASCII.GetBytes(s));
                                offsets.Add(0);
                                WriteAscii("%PDF-1.4\n");

                                offsets.Add(fs.Position);
                                WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                                offsets.Add(fs.Position);
                                WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                                offsets.Add(fs.Position);
                                WriteAscii($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageW:F2} {pageH:F2}] /Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");

                                var contentBytes = Encoding.ASCII.GetBytes(content);
                                offsets.Add(fs.Position);
                                WriteAscii($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
                                bw.Write(contentBytes);
                                WriteAscii("\nendstream\nendobj\n");

                                offsets.Add(fs.Position);
                                WriteAscii($"5 0 obj\n<< /Type /XObject /Subtype /Image /Width {pxW} /Height {pxH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
                                bw.Write(jpegBytes);
                                WriteAscii("\nendstream\nendobj\n");

                                var xrefStart = fs.Position;
                                WriteAscii($"xref\n0 {offsets.Count}\n");
                                WriteAscii("0000000000 65535 f \n");
                                for (int i = 1; i < offsets.Count; i++) WriteAscii($"{offsets[i]:D10} 00000 n \n");
                                WriteAscii($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF");
                            }
                        }

                        var originalPreviewSource = image.Source;
                        var originalImageWidth = image.Width;
                        var originalImageHeight = image.Height;
                        var originalCanvasWidth = canvas.Width;
                        var originalCanvasHeight = canvas.Height;
                        try
                        {
                            if (selectedCopies.Count == 1)
                            {
                                var copyTemplatePath = ResolveCopyTemplatePath(selectedCopies[0]);
                                var selectedTemplate = LoadBitmapFromPath(copyTemplatePath);
                                image.Source = selectedTemplate;
                                image.Width = selectedTemplate.PixelWidth;
                                image.Height = selectedTemplate.PixelHeight;
                                canvas.Width = selectedTemplate.PixelWidth;
                                canvas.Height = selectedTemplate.PixelHeight;
                                canvas.UpdateLayout();
                                var bmpOut = RenderBaseBitmap();
                                if (selectedExt == ".pdf") SaveBitmapAsPdf(bmpOut, sfd.FileName);
                                else SaveBitmap(bmpOut, sfd.FileName);
                            }
                            else
                            {
                                var dir = System.IO.Path.GetDirectoryName(sfd.FileName) ?? string.Empty;
                                var baseName = System.IO.Path.GetFileNameWithoutExtension(sfd.FileName);
                                foreach (var copy in selectedCopies)
                                {
                                    var copyTemplatePathItem = ResolveCopyTemplatePath(copy);
                                    var selectedTemplate = LoadBitmapFromPath(copyTemplatePathItem);
                                    image.Source = selectedTemplate;
                                    image.Width = selectedTemplate.PixelWidth;
                                    image.Height = selectedTemplate.PixelHeight;
                                    canvas.Width = selectedTemplate.PixelWidth;
                                    canvas.Height = selectedTemplate.PixelHeight;
                                    canvas.UpdateLayout();
                                    var suffix = copy.StartsWith("LORRY", StringComparison.OrdinalIgnoreCase)
                                        ? "Lorry"
                                        : (copy.StartsWith("CONSIGNOR", StringComparison.OrdinalIgnoreCase) ? "Consignor" : "Consignee");
                                    var outPath = System.IO.Path.Combine(dir, $"{baseName}_{suffix}{(selectedExt == ".pdf" ? ".pdf" : ".png")}");
                                    var bmpOut = RenderBaseBitmap();
                                    if (selectedExt == ".pdf") SaveBitmapAsPdf(bmpOut, outPath);
                                    else SaveBitmap(bmpOut, outPath);
                                }
                            }
                        }
                        finally
                        {
                            image.Source = originalPreviewSource;
                            image.Width = originalImageWidth;
                            image.Height = originalImageHeight;
                            canvas.Width = originalCanvasWidth;
                            canvas.Height = originalCanvasHeight;
                            canvas.UpdateLayout();
                        }

                        MessageBox.Show("LR format downloaded.", "LR Format", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception exSave)
                    {
                        MessageBox.Show("Unable to download: " + exSave.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                printBtn.Click += (_, __) =>
                {
                    try
                    {
                        var pd = new PrintDialog();
                        if (pd.ShowDialog() != true) return;

                        var page = new Grid { Width = pd.PrintableAreaWidth, Height = pd.PrintableAreaHeight, Background = System.Windows.Media.Brushes.White };
                        var vb = new Viewbox { Stretch = System.Windows.Media.Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(16) };
                        vb.Child = new System.Windows.Shapes.Rectangle
                        {
                            Width = canvas.Width,
                            Height = canvas.Height,
                            Fill = new System.Windows.Media.VisualBrush(canvas)
                        };
                        page.Children.Add(vb);
                        page.Measure(new Size(pd.PrintableAreaWidth, pd.PrintableAreaHeight));
                        page.Arrange(new Rect(new Point(0, 0), page.DesiredSize));
                        pd.PrintVisual(page, $"LR {entry.LRNo}");
                    }
                    catch (Exception exPrint)
                    {
                        MessageBox.Show("Unable to print: " + exPrint.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                emailBtn.Click += (_, __) =>
                {
                    try
                    {
                        var body = $"Please find LR details for LR No: {entry.LRNo}.";
                        var uri = "mailto:?subject=" + Uri.EscapeDataString($"LR {entry.LRNo}") + "&body=" + Uri.EscapeDataString(body);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
                    }
                    catch (Exception exMail)
                    {
                        MessageBox.Show("Unable to open email: " + exMail.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                whatsappBtn.Click += (_, __) =>
                {
                    try
                    {
                        var text = $"LR {entry.LRNo} | Date {entry.Date:dd-MMM-yyyy} | From {entry.From} | To {entry.To}";
                        var url = "https://wa.me/?text=" + Uri.EscapeDataString(text);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch (Exception exWa)
                    {
                        MessageBox.Show("Unable to open WhatsApp: " + exWa.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                var closeBtn = new Button { Content = "Close", Width = 90, Height = 30, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
                closeBtn.Click += (_, __) => dialog.Close();
                Grid.SetRow(closeBtn, 2);
                root.Children.Add(closeBtn);

                dialog.Content = root;
                refreshOverlayTexts();
                dialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open LR view: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private sealed class BillPreviewData
        {
            public string Party { get; set; }
            public string PartyAddress { get; set; }
            public string PartyGST { get; set; }
            public string PartyStateCode { get; set; }
            public string BillNo { get; set; }
            public string BillDate { get; set; }
            public string Total { get; set; }
            public string TotalWords { get; set; }
            public List<BillPreviewLine> Lines { get; set; }
        }

        private sealed class BillPreviewLine
        {
            public string LRNo { get; set; }
            public string LRDate { get; set; }
            public string Invoice { get; set; }
            public string Vehicle { get; set; }
            public string From { get; set; }
            public string To { get; set; }
            public string FreightRoute { get; set; }
            public string ChargesBreakdown { get; set; }
            public string WeightOrType { get; set; }
            public string Rate { get; set; }
            public string Amount { get; set; }
        }

        private sealed class BillPreviewDraft
        {
            public List<BillPreviewLine> Lines { get; set; } = new List<BillPreviewLine>();
            public List<double> RowHeights { get; set; } = new List<double>();
        }

        private sealed class BillFormatTextBox
        {
            public string Text { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double FontSize { get; set; }
            public bool Wrap { get; set; }
        }

        private sealed class BillFormatLayout
        {
            public double FontSize { get; set; }
            public bool WrapText { get; set; }
            public double RowHeight { get; set; }
            public double RowSpacing { get; set; }
            public bool BoldText { get; set; }
            public bool ItalicText { get; set; }
            public string Alignment { get; set; }
            public int ExtraRows { get; set; }
            public List<BillFormatTextBox> TextBoxes { get; set; }
        }

        private static string BillFormatLayoutPath =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Awagaman ERP", "bill_format_layout.txt");

        private static BillFormatLayout LoadBillFormatLayout()
        {
            var layout = new BillFormatLayout
            {
                FontSize = 13,
                WrapText = true,
                RowHeight = 42,
                RowSpacing = 0,
                BoldText = false,
                ItalicText = false,
                Alignment = "Center",
                ExtraRows = 0,
                TextBoxes = new List<BillFormatTextBox>()
            };
            try
            {
                if (!System.IO.File.Exists(BillFormatLayoutPath)) return layout;
                foreach (var line in System.IO.File.ReadAllLines(BillFormatLayoutPath))
                {
                    var raw = (line ?? string.Empty).Trim();
                    if (raw.StartsWith("FONT|", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = raw.Split('|');
                        if (p.Length >= 2 && double.TryParse(p[1], out var fs) && fs > 6 && fs < 60) layout.FontSize = fs;
                    }
                    else if (raw.StartsWith("WRAP|", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = raw.Split('|');
                        if (p.Length >= 2 && bool.TryParse(p[1], out var w)) layout.WrapText = w;
                    }
                    else if (raw.StartsWith("ROWS|", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = raw.Split('|');
                        if (p.Length >= 2 && int.TryParse(p[1], out var r) && r >= 0 && r <= 20) layout.ExtraRows = r;
                    }
                    else if (raw.StartsWith("ROWH|", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = raw.Split('|');
                        if (p.Length >= 2 && double.TryParse(p[1], out var rh) && rh >= 24 && rh <= 80) layout.RowHeight = rh;
                    }
                    else if (raw.StartsWith("ROWSP|", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = raw.Split('|');
                        if (p.Length >= 2 && double.TryParse(p[1], out var rs) && rs >= 0 && rs <= 120) layout.RowSpacing = rs;
                    }
                    else if (raw.StartsWith("BOLD|", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = raw.Split('|');
                        if (p.Length >= 2 && bool.TryParse(p[1], out var b)) layout.BoldText = b;
                    }
                    else if (raw.StartsWith("ITALIC|", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = raw.Split('|');
                        if (p.Length >= 2 && bool.TryParse(p[1], out var it)) layout.ItalicText = it;
                    }
                    else if (raw.StartsWith("ALIGN|", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = raw.Split('|');
                        if (p.Length >= 2) layout.Alignment = string.IsNullOrWhiteSpace(p[1]) ? "Center" : p[1].Trim();
                    }
                    else if (raw.StartsWith("TB|", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = raw.Split('|');
                        if (p.Length < 8) continue;
                        double x, y, w, h, fs;
                        bool wrap;
                        if (!double.TryParse(p[1], out x) || !double.TryParse(p[2], out y) || !double.TryParse(p[3], out w) || !double.TryParse(p[4], out h) || !double.TryParse(p[5], out fs) || !bool.TryParse(p[6], out wrap)) continue;
                        var text = p[7].Replace("\\n", "\n");
                        layout.TextBoxes.Add(new BillFormatTextBox
                        {
                            X = x,
                            Y = y,
                            Width = w,
                            Height = h,
                            FontSize = fs,
                            Wrap = wrap,
                            Text = text
                        });
                    }
                }
            }
            catch { }
            return layout;
        }

        private static void SaveBillFormatLayout(BillFormatLayout layout)
        {
            try
            {
                if (layout == null) return;
                var dir = System.IO.Path.GetDirectoryName(BillFormatLayoutPath);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                var lines = new List<string>
                {
                    $"FONT|{layout.FontSize}",
                    $"WRAP|{layout.WrapText}",
                    $"ROWH|{layout.RowHeight}",
                    $"ROWSP|{layout.RowSpacing}",
                    $"BOLD|{layout.BoldText}",
                    $"ITALIC|{layout.ItalicText}",
                    $"ALIGN|{(string.IsNullOrWhiteSpace(layout.Alignment) ? "Center" : layout.Alignment)}",
                    $"ROWS|{layout.ExtraRows}"
                };
                if (layout.TextBoxes != null)
                {
                    foreach (var tb in layout.TextBoxes)
                    {
                        var t = (tb.Text ?? string.Empty).Replace("\r", string.Empty).Replace("\n", "\\n");
                        lines.Add($"TB|{tb.X}|{tb.Y}|{tb.Width}|{tb.Height}|{tb.FontSize}|{tb.Wrap}|{t}");
                    }
                }
                System.IO.File.WriteAllLines(BillFormatLayoutPath, lines);
            }
            catch { }
        }

        private static string GetBillPreviewDraftPath(string billNo)
        {
            var safe = string.IsNullOrWhiteSpace(billNo) ? "bill" : string.Join("_", (billNo ?? "bill").Split(System.IO.Path.GetInvalidFileNameChars()));
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Awagaman ERP", "bill_preview_drafts");
            return System.IO.Path.Combine(dir, safe + ".json");
        }

        private static BillPreviewDraft LoadBillPreviewDraft(string billNo)
        {
            try
            {
                var path = GetBillPreviewDraftPath(billNo);
                if (!System.IO.File.Exists(path)) return null;
                var json = System.IO.File.ReadAllText(path);
                var draft = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<BillPreviewDraft>(json);
                return draft;
            }
            catch { return null; }
        }

        private static void SaveBillPreviewDraft(string billNo, BillPreviewDraft draft)
        {
            try
            {
                if (draft == null) return;
                var path = GetBillPreviewDraftPath(billNo);
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                var json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(draft);
                System.IO.File.WriteAllText(path, json);
            }
            catch { }
        }

        private void OpenBillFormatPreview(BillEntry entry)
        {
            if (entry == null) return;
            try
            {
                var party = new PartyRepository().FindByName(entry.Party ?? string.Empty);
                var billRows = GetBillEntriesForBillNo(entry.BillNo);
                if (billRows.Count == 0) billRows.Add(entry);

                var totalAmount = billRows.Sum(x => x.Total);
                var stateCode = ((party?.GSTNo ?? string.Empty).Trim().Length >= 2)
                    ? (party.GSTNo.Trim().Substring(0, 2))
                    : string.Empty;

                var preview = new BillPreviewData
                {
                    Party = (entry.Party ?? string.Empty).Trim(),
                    PartyAddress = (party?.Address ?? string.Empty).Trim(),
                    PartyGST = (party?.GSTNo ?? string.Empty).Trim(),
                    PartyStateCode = stateCode,
                    BillNo = (entry.BillNo ?? string.Empty).Trim(),
                    BillDate = entry.BillDate.ToString("dd-MMM-yyyy"),
                    Total = totalAmount.ToString("N2"),
                    TotalWords = NumberToWords((long)Math.Round(totalAmount, 0)) + " Only",
                    Lines = BuildBillPreviewLinesForGrid(billRows)
                };
                EnsureStChargeSummaryRow(preview.Lines, billRows.Sum(x => x.StCharge));
                var loadedDraft = LoadBillPreviewDraft(preview.BillNo);
                if (loadedDraft != null && loadedDraft.Lines != null && loadedDraft.Lines.Count > 0)
                    preview.Lines = loadedDraft.Lines.Select(x => new BillPreviewLine
                    {
                        LRNo = x?.LRNo ?? string.Empty,
                        LRDate = x?.LRDate ?? string.Empty,
                        Invoice = x?.Invoice ?? string.Empty,
                        Vehicle = x?.Vehicle ?? string.Empty,
                        From = x?.From ?? string.Empty,
                        To = x?.To ?? string.Empty,
                        ChargesBreakdown = x?.ChargesBreakdown ?? string.Empty,
                        WeightOrType = x?.WeightOrType ?? string.Empty,
                        Rate = x?.Rate ?? string.Empty,
                        Amount = x?.Amount ?? string.Empty
                    }).ToList();
                EnsureStChargeSummaryRow(preview.Lines, billRows.Sum(x => x.StCharge));
                NormalizeInvoiceCellsInPreviewLines(preview.Lines);

                var dialog = new Window
                {
                    Title = $"Bill View - {preview.BillNo}",
                    Owner = this,
                    Width = 1180,
                    Height = 840,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = System.Windows.Media.Brushes.White
                };

                var root = new Grid { Margin = new Thickness(10) };
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var billLayout = LoadBillFormatLayout();
                var undoStack = new Stack<List<BillPreviewLine>>();
                var redoStack = new Stack<List<BillPreviewLine>>();
                bool suppressHistory = false;
                bool hasUnsavedChanges = false;
                bool allowCloseWithoutPrompt = false;
                var historyTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };

                Func<IEnumerable<BillPreviewLine>, List<BillPreviewLine>> cloneLines = lines =>
                {
                    return (lines ?? Enumerable.Empty<BillPreviewLine>())
                        .Select(x => new BillPreviewLine
                        {
                            LRNo = x?.LRNo ?? string.Empty,
                            LRDate = x?.LRDate ?? string.Empty,
                            Invoice = x?.Invoice ?? string.Empty,
                            Vehicle = x?.Vehicle ?? string.Empty,
                            From = x?.From ?? string.Empty,
                            To = x?.To ?? string.Empty,
                            ChargesBreakdown = x?.ChargesBreakdown ?? string.Empty,
                            WeightOrType = x?.WeightOrType ?? string.Empty,
                            Rate = x?.Rate ?? string.Empty,
                            Amount = x?.Amount ?? string.Empty
                        }).ToList();
                };
                Func<List<BillPreviewLine>, List<BillPreviewLine>, bool> sameLines = (a, b) =>
                {
                    if (ReferenceEquals(a, b)) return true;
                    if (a == null || b == null) return false;
                    if (a.Count != b.Count) return false;
                    for (int i = 0; i < a.Count; i++)
                    {
                        var x = a[i];
                        var y = b[i];
                        if ((x?.LRNo ?? string.Empty) != (y?.LRNo ?? string.Empty)) return false;
                        if ((x?.LRDate ?? string.Empty) != (y?.LRDate ?? string.Empty)) return false;
                        if ((x?.Invoice ?? string.Empty) != (y?.Invoice ?? string.Empty)) return false;
                        if ((x?.Vehicle ?? string.Empty) != (y?.Vehicle ?? string.Empty)) return false;
                        if ((x?.From ?? string.Empty) != (y?.From ?? string.Empty)) return false;
                        if ((x?.To ?? string.Empty) != (y?.To ?? string.Empty)) return false;
                        if ((x?.ChargesBreakdown ?? string.Empty) != (y?.ChargesBreakdown ?? string.Empty)) return false;
                        if ((x?.WeightOrType ?? string.Empty) != (y?.WeightOrType ?? string.Empty)) return false;
                        if ((x?.Rate ?? string.Empty) != (y?.Rate ?? string.Empty)) return false;
                        if ((x?.Amount ?? string.Empty) != (y?.Amount ?? string.Empty)) return false;
                    }
                    return true;
                };
                List<BillPreviewLine> currentHistoryState = cloneLines(preview.Lines);
                Func<BillFormatLayout, BillFormatLayout> cloneLayout = src =>
                {
                    if (src == null) return new BillFormatLayout();
                    return new BillFormatLayout
                    {
                        FontSize = src.FontSize,
                        WrapText = src.WrapText,
                        RowHeight = src.RowHeight,
                        RowSpacing = src.RowSpacing,
                        BoldText = src.BoldText,
                        ItalicText = src.ItalicText,
                        Alignment = src.Alignment,
                        ExtraRows = src.ExtraRows,
                        TextBoxes = (src.TextBoxes ?? new List<BillFormatTextBox>()).Select(t => new BillFormatTextBox
                        {
                            Text = t?.Text ?? string.Empty,
                            X = t?.X ?? 0,
                            Y = t?.Y ?? 0,
                            Width = t?.Width ?? 0,
                            Height = t?.Height ?? 0,
                            FontSize = t?.FontSize ?? 0,
                            Wrap = t?.Wrap == true
                        }).ToList()
                    };
                };
                Action<BillFormatLayout, BillFormatLayout> copyLayout = (from, to) =>
                {
                    if (from == null || to == null) return;
                    to.FontSize = from.FontSize;
                    to.WrapText = from.WrapText;
                    to.RowHeight = from.RowHeight;
                    to.RowSpacing = from.RowSpacing;
                    to.BoldText = from.BoldText;
                    to.ItalicText = from.ItalicText;
                    to.Alignment = from.Alignment;
                    to.ExtraRows = from.ExtraRows;
                    to.TextBoxes = (from.TextBoxes ?? new List<BillFormatTextBox>()).Select(t => new BillFormatTextBox
                    {
                        Text = t?.Text ?? string.Empty,
                        X = t?.X ?? 0,
                        Y = t?.Y ?? 0,
                        Width = t?.Width ?? 0,
                        Height = t?.Height ?? 0,
                        FontSize = t?.FontSize ?? 0,
                        Wrap = t?.Wrap == true
                    }).ToList();
                };
                var loadedLayoutSnapshot = cloneLayout(billLayout);
                var loadedLinesSnapshot = cloneLines(preview.Lines);
                var loadedRowHeightsSnapshot = (loadedDraft?.RowHeights ?? new List<double>()).ToList();

                var viewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = Brushes.White
                };

                Expander inlineEditor = null;
                Action recordHistoryFromGrid = () => { };
                Action rebuildPreview = () =>
                {
                    var dragEnabled = true;
                    const double editorLetterheadTopSpace = 90;
                    const double editorLetterheadBottomSpace = 62;
                    viewer.Content = BuildBillPrintVisual(preview, billLayout, dragEnabled, null, loadedRowHeightsSnapshot, editorLetterheadTopSpace, editorLetterheadBottomSpace);

                    var liveGrid = FindVisualChild<DataGrid>(viewer);
                    if (liveGrid != null)
                    {
                        List<BillPreviewLine> preEditState = null;

                        liveGrid.BeginningEdit += (s, e) =>
                        {
                            if (suppressHistory) return;
                            preEditState = cloneLines(currentHistoryState);
                        };

                        liveGrid.CellEditEnding += (s, e) =>
                        {
                            if (suppressHistory || e.EditAction != DataGridEditAction.Commit) return;
                            liveGrid.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    liveGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                                    liveGrid.CommitEdit(DataGridEditingUnit.Row, true);
                                    var state = cloneLines((liveGrid.ItemsSource as IEnumerable<BillPreviewLine>) ?? preview.Lines);
                                    var oldState = preEditState ?? cloneLines(currentHistoryState);
                                    if (!sameLines(oldState, state))
                                    {
                                        undoStack.Push(cloneLines(oldState));
                                        currentHistoryState = cloneLines(state);
                                        preview.Lines = cloneLines(state);
                                        redoStack.Clear();
                                        hasUnsavedChanges = true;
                                    }
                                }
                                catch { }
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        };
                        liveGrid.RowEditEnding += (s, e) =>
                        {
                            if (suppressHistory || e.EditAction != DataGridEditAction.Commit) return;
                            liveGrid.Dispatcher.BeginInvoke(new Action(() => recordHistoryFromGrid()), System.Windows.Threading.DispatcherPriority.Background);
                        };
                        liveGrid.CurrentCellChanged += (s, e) =>
                        {
                            if (suppressHistory) return;
                            liveGrid.Dispatcher.BeginInvoke(new Action(() => recordHistoryFromGrid()), System.Windows.Threading.DispatcherPriority.Background);
                        };
                        var oc = liveGrid.ItemsSource as ObservableCollection<BillPreviewLine>;
                        if (oc != null)
                        {
                            oc.CollectionChanged += (s, e) =>
                            {
                                if (suppressHistory) return;
                                try
                                {
                                    var state = cloneLines(oc);
                                    if (sameLines(currentHistoryState, state)) return;
                                    undoStack.Push(cloneLines(currentHistoryState));
                                    currentHistoryState = cloneLines(state);
                                    preview.Lines = cloneLines(state);
                                    redoStack.Clear();
                                    hasUnsavedChanges = true;
                                }
                                catch { }
                            };
                        }
                    }
                };
                recordHistoryFromGrid = () =>
                {
                    if (suppressHistory) return;
                    try
                    {
                        var grid = FindVisualChild<DataGrid>(viewer);
                        if (grid == null) return;
                        grid.CommitEdit(DataGridEditingUnit.Cell, true);
                        grid.CommitEdit(DataGridEditingUnit.Row, true);
                        var state = cloneLines((grid.ItemsSource as IEnumerable<BillPreviewLine>) ?? preview.Lines);
                        if (sameLines(currentHistoryState, state)) return;
                        undoStack.Push(cloneLines(currentHistoryState));
                        currentHistoryState = cloneLines(state);
                        preview.Lines = cloneLines(state);
                        redoStack.Clear();
                        hasUnsavedChanges = true;
                    }
                    catch { }
                };
                Action trackHistoryTick = () =>
                {
                    if (suppressHistory) return;
                    if (!viewer.IsKeyboardFocusWithin) return;
                    // Do not interrupt active typing inside editable cell text boxes.
                    if (Keyboard.FocusedElement is TextBox) return;
                    recordHistoryFromGrid();
                };
                historyTimer.Tick += (s, e) => trackHistoryTick();
                Action syncPreviewFromGrid = () =>
                {
                    try
                    {
                        var grid = FindVisualChild<DataGrid>(viewer);
                        if (grid == null) return;
                        grid.CommitEdit(DataGridEditingUnit.Cell, true);
                        grid.CommitEdit(DataGridEditingUnit.Row, true);
                        var items = (grid.ItemsSource as IEnumerable<BillPreviewLine>)?.ToList();
                        if (items == null) return;
                        preview.Lines = items.Select(x => new BillPreviewLine
                        {
                            LRNo = x?.LRNo ?? string.Empty,
                            LRDate = x?.LRDate ?? string.Empty,
                            Invoice = x?.Invoice ?? string.Empty,
                            Vehicle = x?.Vehicle ?? string.Empty,
                            From = x?.From ?? string.Empty,
                            To = x?.To ?? string.Empty,
                            ChargesBreakdown = x?.ChargesBreakdown ?? string.Empty,
                            WeightOrType = x?.WeightOrType ?? string.Empty,
                            Rate = x?.Rate ?? string.Empty,
                            Amount = x?.Amount ?? string.Empty
                        }).ToList();

                        decimal total = 0m;
                        foreach (var line in preview.Lines)
                        {
                            if (decimal.TryParse((line?.Amount ?? string.Empty).Trim(), out var amt))
                                total += amt;
                        }
                        preview.Total = total.ToString("N2");
                        preview.TotalWords = NumberToWords((long)Math.Round(total, 0)) + " Only";
                        currentHistoryState = cloneLines(preview.Lines);
                    }
                    catch { }
                };
                Func<List<double>> captureCurrentRowHeights = () =>
                {
                    var result = new List<double>();
                    try
                    {
                        var grid = FindVisualChild<DataGrid>(viewer);
                        if (grid == null) return result;
                        grid.UpdateLayout();
                        for (int i = 0; i < grid.Items.Count; i++)
                        {
                            var row = grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                            if (row != null && row.ActualHeight > 0)
                                result.Add(row.ActualHeight);
                            else
                                result.Add(grid.RowHeight > 0 ? grid.RowHeight : 42);
                        }
                    }
                    catch { }
                    return result;
                };
                rebuildPreview();
                Grid.SetRow(viewer, 0);
                root.Children.Add(viewer);
                historyTimer.Start();

                Action<bool> flushActiveEdit = (trackHistory) =>
                {
                    if (suppressHistory) return;
                    try
                    {
                        var liveGrid = FindVisualChild<DataGrid>(viewer);
                        if (liveGrid == null) return;
                        liveGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                        liveGrid.CommitEdit(DataGridEditingUnit.Row, true);
                    }
                    catch { }
                    if (trackHistory) recordHistoryFromGrid();
                };
                var billUndoCmd = new RoutedCommand("BillUndo", typeof(Window));
                var billRedoCmd = new RoutedCommand("BillRedo", typeof(Window));
                dialog.InputBindings.Add(new KeyBinding(billUndoCmd, new KeyGesture(Key.Z, ModifierKeys.Control)));
                dialog.InputBindings.Add(new KeyBinding(billRedoCmd, new KeyGesture(Key.Z, ModifierKeys.Control | ModifierKeys.Shift)));
                Action doBillUndo = () =>
                {
                    if (!dialog.IsActive) return;
                    flushActiveEdit(true);
                    if (undoStack.Count == 0) return;
                    suppressHistory = true;
                    try
                    {
                        var prev = undoStack.Count > 0 ? undoStack.Pop() : null;
                        if (prev == null) return;
                        redoStack.Push(cloneLines(currentHistoryState));
                        currentHistoryState = cloneLines(prev);
                        preview.Lines = cloneLines(prev);
                        rebuildPreview();
                    }
                    finally
                    {
                        suppressHistory = false;
                    }
                };
                Action doBillRedo = () =>
                {
                    if (!dialog.IsActive) return;
                    flushActiveEdit(true);
                    if (redoStack.Count == 0) return;
                    suppressHistory = true;
                    try
                    {
                        var next = redoStack.Count > 0 ? redoStack.Pop() : null;
                        if (next == null) return;
                        undoStack.Push(cloneLines(currentHistoryState));
                        currentHistoryState = cloneLines(next);
                        preview.Lines = cloneLines(next);
                        rebuildPreview();
                    }
                    finally
                    {
                        suppressHistory = false;
                    }
                };
                dialog.CommandBindings.Add(new CommandBinding(billUndoCmd, (s, e) =>
                {
                    doBillUndo();
                    e.Handled = true;
                }));
                dialog.CommandBindings.Add(new CommandBinding(billRedoCmd, (s, e) =>
                {
                    doBillRedo();
                    e.Handled = true;
                }));
                dialog.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((s, e) =>
                {
                    if (!dialog.IsActive) return;
                    if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
                    {
                        doBillRedo();
                        e.Handled = true;
                        return;
                    }
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Z)
                    {
                        doBillUndo();
                        e.Handled = true;
                    }
                }), true);

                var closeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
                var resetBtn = new Button { Content = "Reset", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
                var saveBtn = new Button { Content = "Save", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
                var actionsBtn = new Button { Content = "Actions ▾", Width = 110, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
                var closeBtn = new Button { Content = "Close", Width = 90, Height = 30 };
                Action doPrint = () =>
                {
                    try
                    {
                        syncPreviewFromGrid();
                        var rowHeights = captureCurrentRowHeights();
                        const double letterheadTopSpace = 90;
                        const double letterheadBottomSpace = 62;
                        var currentVisual = BuildBillPrintVisual(preview, billLayout, false, null, rowHeights, letterheadTopSpace, letterheadBottomSpace);

                        var pd = new PrintDialog();
                        if (pd.ShowDialog() != true) return;
                        pd.PrintVisual(currentVisual, $"Bill {preview?.BillNo}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Print failed: " + ex.Message, "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                var actionsMenu = new ContextMenu();
                var miPreview = new MenuItem { Header = "Print Preview" };
                miPreview.Click += (_, __) =>
                {
                    try
                    {
                        syncPreviewFromGrid();
                        var rowHeights = captureCurrentRowHeights();
                        const double letterheadTopSpace = 90;
                        const double letterheadBottomSpace = 62;
                        var tempPdf = System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(),
                            $"BillPreview_{((preview?.BillNo ?? "Bill").Replace("/", "-"))}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                        // Preview should visually match Bill View Format.
                        var pageForPreview = BuildBillPrintVisual(preview, billLayout, true, null, rowHeights, letterheadTopSpace, letterheadBottomSpace, true, false);
                        const double exportDpi = 600;
                        var logicalWidth = pageForPreview.Width > 0 ? pageForPreview.Width : 1100;
                        pageForPreview.Measure(new Size(logicalWidth, double.PositiveInfinity));
                        pageForPreview.Arrange(new Rect(0, 0, logicalWidth, pageForPreview.DesiredSize.Height));
                        pageForPreview.UpdateLayout();
                        var logicalHeight = Math.Max(1, pageForPreview.DesiredSize.Height);
                        var bmpPreview = RenderElementToBitmap(pageForPreview, logicalWidth, logicalHeight, exportDpi);
                        SaveBitmapAsPdf(bmpPreview, tempPdf, exportDpi);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tempPdf) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Unable to open bill preview: " + ex.Message, "Print Preview", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                actionsMenu.Items.Add(miPreview);

                var miPrint = new MenuItem { Header = "Print" };
                miPrint.Click += (_, __) =>
                {
                    try
                    {
                        doPrint();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Unable to open print window: " + ex.Message, "Print", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                actionsMenu.Items.Add(miPrint);
                actionsMenu.Items.Add(new Separator());

                var miDownload = new MenuItem { Header = "Download" };
                miDownload.Click += (_, __) =>
                {
                    try
                    {
                        syncPreviewFromGrid();
                        var rowHeights = captureCurrentRowHeights();
                        const double letterheadTopSpace = 90;
                        const double letterheadBottomSpace = 62;
                        var sfd = new Microsoft.Win32.SaveFileDialog
                        {
                            Title = "Download Bill Format",
                            Filter = "PDF Document|*.pdf",
                            FileName = $"{((preview?.BillNo ?? "Bill").Replace("/", "-"))}.pdf"
                        };
                        if (sfd.ShowDialog(dialog) != true) return;
                        var pageForExport = BuildBillPrintVisual(preview, billLayout, false, null, rowHeights, letterheadTopSpace, letterheadBottomSpace);
                        const double exportDpi = 600;
                        var bmpOut = RenderElementToBitmap(pageForExport, 1056, 816, exportDpi);
                        SaveBitmapAsPdf(bmpOut, sfd.FileName, exportDpi);
                        MessageBox.Show("Bill format downloaded.", "Download", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Download failed: " + ex.Message, "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                actionsMenu.Items.Add(miDownload);

                var miWhatsapp = new MenuItem { Header = "WhatsApp" };
                miWhatsapp.Click += (_, __) =>
                {
                    try
                    {
                        syncPreviewFromGrid();
                        var rowHeights = captureCurrentRowHeights();
                        const double letterheadTopSpace = 90;
                        const double letterheadBottomSpace = 62;
                        var tempPng = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Bill_{(preview?.BillNo ?? "Bill").Replace("/", "-")}.png");
                        var pageForExport = BuildBillPrintVisual(preview, billLayout, false, null, rowHeights, letterheadTopSpace, letterheadBottomSpace);
                        const double exportDpi = 600;
                        var bmpOut = RenderElementToBitmap(pageForExport, 1056, 816, exportDpi);
                        SaveBitmapAsPng(bmpOut, tempPng);
                        var text = Uri.EscapeDataString($"Bill {preview?.BillNo}\nFile: {tempPng}");
                        var uri = $"https://wa.me/?text={text}";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Unable to open WhatsApp share: " + ex.Message, "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                actionsMenu.Items.Add(miWhatsapp);

                var miEmail = new MenuItem { Header = "Email" };
                miEmail.Click += (_, __) =>
                {
                    try
                    {
                        syncPreviewFromGrid();
                        var rowHeights = captureCurrentRowHeights();
                        const double letterheadTopSpace = 90;
                        const double letterheadBottomSpace = 62;
                        var tempPng = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Bill_{(preview?.BillNo ?? "Bill").Replace("/", "-")}.png");
                        var pageForExport = BuildBillPrintVisual(preview, billLayout, false, null, rowHeights, letterheadTopSpace, letterheadBottomSpace);
                        const double exportDpi = 600;
                        var bmpOut = RenderElementToBitmap(pageForExport, 1056, 816, exportDpi);
                        SaveBitmapAsPng(bmpOut, tempPng);
                        var subject = Uri.EscapeDataString($"Bill {preview?.BillNo}");
                        var body = Uri.EscapeDataString($"Please find the bill file.\n\nSaved at:\n{tempPng}");
                        var uri = $"mailto:?subject={subject}&body={body}";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Unable to open email share: " + ex.Message, "Email", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                actionsMenu.Items.Add(miEmail);

                actionsBtn.Click += (_, __) =>
                {
                    actionsBtn.ContextMenu = actionsMenu;
                    actionsMenu.PlacementTarget = actionsBtn;
                    actionsMenu.IsOpen = true;
                };

                inlineEditor = new Expander
                {
                    Header = "Edit Bill Format",
                    IsExpanded = true,
                    Margin = new Thickness(0, 8, 0, 0),
                    Visibility = Visibility.Collapsed
                };
                var editorRoot = new Grid { Margin = new Thickness(6) };
                editorRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var editTop = new StackPanel { Orientation = Orientation.Horizontal };
                editTop.Children.Add(new TextBlock { Text = "Font", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var fontBox = new TextBox { Width = 60, Text = billLayout.FontSize.ToString("0.##"), Margin = new Thickness(0, 0, 12, 0) };
                editTop.Children.Add(fontBox);
                var wrapCheck = new CheckBox { Content = "Wrap text", IsChecked = billLayout.WrapText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                editTop.Children.Add(wrapCheck);
                editTop.Children.Add(new TextBlock { Text = "Rows", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var rowsBox = new TextBox { Width = 55, Text = billLayout.ExtraRows.ToString(), Margin = new Thickness(0, 0, 8, 0) };
                editTop.Children.Add(rowsBox);
                editTop.Children.Add(new TextBlock { Text = "Row H", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var rowHeightBox = new TextBox { Width = 60, Text = billLayout.RowHeight.ToString("0.##"), Margin = new Thickness(0, 0, 8, 0) };
                editTop.Children.Add(rowHeightBox);
                editTop.Children.Add(new TextBlock { Text = "Row Space", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var rowSpacingBox = new TextBox { Width = 60, Text = billLayout.RowSpacing.ToString("0.##"), Margin = new Thickness(0, 0, 8, 0) };
                editTop.Children.Add(rowSpacingBox);
                var boldCheck = new CheckBox { Content = "Bold", IsChecked = billLayout.BoldText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                var italicCheck = new CheckBox { Content = "Italic", IsChecked = billLayout.ItalicText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                editTop.Children.Add(boldCheck);
                editTop.Children.Add(italicCheck);
                editTop.Children.Add(new TextBlock { Text = "Align", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var alignPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
                var alignLeftBtn = new Button { Content = "≡", Width = 30, Height = 26, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Left Align" };
                var alignCenterBtn = new Button { Content = "≣", Width = 30, Height = 26, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Center Align" };
                var alignRightBtn = new Button { Content = "☰", Width = 30, Height = 26, ToolTip = "Right Align" };
                alignPanel.Children.Add(alignLeftBtn);
                alignPanel.Children.Add(alignCenterBtn);
                alignPanel.Children.Add(alignRightBtn);
                editTop.Children.Add(alignPanel);

                string selectedAlign = string.IsNullOrWhiteSpace(billLayout.Alignment) ? "Center" : billLayout.Alignment;
                Action refreshAlignButtons = () =>
                {
                    var active = new SolidColorBrush(Color.FromRgb(214, 228, 246));
                    var inactive = Brushes.White;
                    alignLeftBtn.Background = string.Equals(selectedAlign, "Left", StringComparison.OrdinalIgnoreCase) ? active : inactive;
                    alignCenterBtn.Background = string.Equals(selectedAlign, "Center", StringComparison.OrdinalIgnoreCase) ? active : inactive;
                    alignRightBtn.Background = string.Equals(selectedAlign, "Right", StringComparison.OrdinalIgnoreCase) ? active : inactive;
                };
                Action<string> applyAlign = align =>
                {
                    selectedAlign = align;
                    refreshAlignButtons();
                    var previewGrid = FindVisualChild<DataGrid>(viewer);
                    if (previewGrid != null && previewGrid.SelectedCells != null && previewGrid.SelectedCells.Count > 0)
                    {
                        ApplyAlignmentToSelectedCells(previewGrid, selectedAlign);
                        hasUnsavedChanges = true;
                        return;
                    }
                    billLayout.Alignment = selectedAlign;
                    hasUnsavedChanges = true;
                    rebuildPreview();
                };
                alignLeftBtn.Click += (_, __) => applyAlign("Left");
                alignCenterBtn.Click += (_, __) => applyAlign("Center");
                alignRightBtn.Click += (_, __) => applyAlign("Right");
                refreshAlignButtons();
                var applyBtn = new Button { Content = "Apply", Width = 70 };
                editTop.Children.Add(applyBtn);
                Grid.SetRow(editTop, 0);
                editorRoot.Children.Add(editTop);
                Action applyEditorInputsToLayout = () =>
                {
                    if (double.TryParse((fontBox.Text ?? string.Empty).Trim(), out var fs))
                        billLayout.FontSize = Math.Max(8, Math.Min(40, fs));
                    if (double.TryParse((rowHeightBox.Text ?? string.Empty).Trim(), out var rh))
                        billLayout.RowHeight = Math.Max(18, Math.Min(80, rh));
                    if (double.TryParse((rowSpacingBox.Text ?? string.Empty).Trim(), out var rs))
                        billLayout.RowSpacing = Math.Max(0, Math.Min(120, rs));
                    billLayout.WrapText = wrapCheck.IsChecked == true;
                    billLayout.BoldText = boldCheck.IsChecked == true;
                    billLayout.ItalicText = italicCheck.IsChecked == true;
                    billLayout.Alignment = selectedAlign;
                    if (int.TryParse((rowsBox.Text ?? string.Empty).Trim(), out var rows))
                        billLayout.ExtraRows = Math.Max(0, Math.Min(20, rows));
                };
                applyBtn.Click += (_, __) =>
                {
                    applyEditorInputsToLayout();
                    hasUnsavedChanges = true;
                    rebuildPreview();
                };
                var inlinePanel = new Border
                {
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Margin = new Thickness(0, 8, 0, 0),
                    Padding = new Thickness(0),
                    Child = editorRoot
                };
                Grid.SetRow(inlinePanel, 1);
                root.Children.Add(inlinePanel);

                saveBtn.Click += (_, __) =>
                {
                    try
                    {
                        applyEditorInputsToLayout();
                        syncPreviewFromGrid();
                        var rowHeights = captureCurrentRowHeights();
                        SaveBillFormatLayout(billLayout);
                        SaveBillPreviewDraft(preview.BillNo, new BillPreviewDraft
                        {
                            Lines = cloneLines(preview.Lines),
                            RowHeights = rowHeights
                        });
                        loadedLayoutSnapshot = cloneLayout(billLayout);
                        loadedLinesSnapshot = cloneLines(preview.Lines);
                        loadedRowHeightsSnapshot = (rowHeights ?? new List<double>()).ToList();
                        hasUnsavedChanges = false;
                        MessageBox.Show("Bill format saved.", "Save", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Unable to save bill format: " + ex.Message, "Save", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                resetBtn.Click += (_, __) =>
                {
                    var confirm = MessageBox.Show(
                        "Reset bill view to freshly loaded state?\nUnsaved changes will be lost.",
                        "Reset Bill Format",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.OK) return;
                    suppressHistory = true;
                    try
                    {
                        copyLayout(loadedLayoutSnapshot, billLayout);
                        preview.Lines = cloneLines(loadedLinesSnapshot);
                        currentHistoryState = cloneLines(preview.Lines);
                        undoStack.Clear();
                        redoStack.Clear();
                        fontBox.Text = billLayout.FontSize.ToString("0.##");
                        wrapCheck.IsChecked = billLayout.WrapText;
                        rowsBox.Text = billLayout.ExtraRows.ToString();
                        rowHeightBox.Text = billLayout.RowHeight.ToString("0.##");
                        rowSpacingBox.Text = billLayout.RowSpacing.ToString("0.##");
                        boldCheck.IsChecked = billLayout.BoldText;
                        italicCheck.IsChecked = billLayout.ItalicText;
                        selectedAlign = string.IsNullOrWhiteSpace(billLayout.Alignment) ? "Center" : billLayout.Alignment;
                        refreshAlignButtons();
                        rebuildPreview();
                        hasUnsavedChanges = false;
                    }
                    finally
                    {
                        suppressHistory = false;
                    }
                };
                closeBtn.Click += (_, __) => dialog.Close();
                dialog.Closing += (_, e) =>
                {
                    if (allowCloseWithoutPrompt) return;
                    if (!hasUnsavedChanges) return;
                    var confirm = MessageBox.Show(
                        "You have unsaved changes. Close without saving?",
                        "Unsaved Changes",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.OK)
                    {
                        e.Cancel = true;
                        return;
                    }
                    allowCloseWithoutPrompt = true;
                };
                dialog.Closed += (_, __) =>
                {
                    try { historyTimer.Stop(); } catch { }
                };
                closeRow.Children.Add(resetBtn);
                closeRow.Children.Add(saveBtn);
                closeRow.Children.Add(actionsBtn);
                closeRow.Children.Add(closeBtn);
                Grid.SetRow(closeRow, 2);
                root.Children.Add(closeRow);

                dialog.Content = root;
                dialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open bill format view: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenBillFormatEditor(Window owner, BillFormatLayout layout, Action onChanged)
        {
            if (layout == null) return;
            var dialog = new Window
            {
                Title = "Edit Bill Format",
                Owner = owner ?? this,
                Width = 860,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.White
            };

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(new TextBlock { Text = "Font Size", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var fontBox = new TextBox { Width = 70, Text = layout.FontSize.ToString("0.##"), Margin = new Thickness(0, 0, 14, 0) };
            top.Children.Add(fontBox);
            var wrapCheck = new CheckBox { Content = "Wrap Text", IsChecked = layout.WrapText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            top.Children.Add(wrapCheck);
            top.Children.Add(new TextBlock { Text = "Extra Rows", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var rowsBox = new TextBox { Width = 60, Text = layout.ExtraRows.ToString(), Margin = new Thickness(0, 0, 10, 0) };
            top.Children.Add(rowsBox);
            var addRowBtn = new Button { Content = "+ Row", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            var addTextBtn = new Button { Content = "+ Text Box", Width = 100 };
            top.Children.Add(addRowBtn);
            top.Children.Add(addTextBtn);
            Grid.SetRow(top, 0);
            root.Children.Add(top);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = true,
                ItemsSource = layout.TextBoxes,
                SelectionMode = DataGridSelectionMode.Single,
                Margin = new Thickness(0, 10, 0, 10)
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Text", Binding = new Binding("Text"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "X", Binding = new Binding("X"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Y", Binding = new Binding("Y"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = "W", Binding = new Binding("Width"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = "H", Binding = new Binding("Height"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Font", Binding = new Binding("FontSize"), Width = 80 });
            var wrapCol = new DataGridCheckBoxColumn { Header = "Wrap", Binding = new Binding("Wrap"), Width = 70 };
            grid.Columns.Add(wrapCol);
            Grid.SetRow(grid, 1);
            root.Children.Add(grid);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var applyBtn = new Button { Content = "Apply", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            var closeBtn = new Button { Content = "Close", Width = 90 };
            footer.Children.Add(applyBtn);
            footer.Children.Add(closeBtn);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            addRowBtn.Click += (_, __) =>
            {
                layout.ExtraRows = Math.Max(0, layout.ExtraRows + 1);
                rowsBox.Text = layout.ExtraRows.ToString();
            };
            addTextBtn.Click += (_, __) =>
            {
                layout.TextBoxes.Add(new BillFormatTextBox { Text = "New Text", X = 80, Y = 80, Width = 220, Height = 28, FontSize = layout.FontSize, Wrap = layout.WrapText });
                grid.Items.Refresh();
            };

            applyBtn.Click += (_, __) =>
            {
                try
                {
                    if (double.TryParse((fontBox.Text ?? string.Empty).Trim(), out var fs))
                        layout.FontSize = Math.Max(8, Math.Min(40, fs));
                    layout.WrapText = wrapCheck.IsChecked == true;
                    if (int.TryParse((rowsBox.Text ?? string.Empty).Trim(), out var rows))
                        layout.ExtraRows = Math.Max(0, Math.Min(20, rows));
                    SaveBillFormatLayout(layout);
                    onChanged?.Invoke();
                }
                catch { }
            };
            closeBtn.Click += (_, __) => dialog.Close();
            dialog.Content = root;
            dialog.Show();
        }

        private void ShowBillPrintPreview(BillPreviewData preview, BillFormatLayout layout = null)
        {
            try
            {
                var win = new Window
                {
                    Title = $"Print Preview - Bill {preview?.BillNo}",
                    Owner = this,
                    Width = 980,
                    Height = 760,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brushes.White
                };
                var pageVisual = BuildBillPrintVisual(preview, layout);
                pageVisual.Width = 1056; // Letter landscape @96 DPI (11in x 8.5in)
                pageVisual.Measure(new Size(1056, 816));
                pageVisual.Arrange(new Rect(new Point(0, 0), new Size(1056, 816)));
                pageVisual.UpdateLayout();

                var viewer = new ScrollViewer
                {
                    Content = pageVisual,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = Brushes.DimGray,
                    Padding = new Thickness(16)
                };
                win.Content = viewer;
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open print preview: " + ex.Message, "Preview Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private System.Windows.Documents.FixedDocument BuildBillFixedDocument(BillPreviewData preview, double pageWidth, double pageHeight, BillFormatLayout layout = null)
        {
            var printRoot = BuildBillPrintVisual(preview, layout);
            printRoot.Width = pageWidth;
            printRoot.Measure(new Size(pageWidth, pageHeight));
            printRoot.Arrange(new Rect(new Point(0, 0), new Size(pageWidth, pageHeight)));
            printRoot.UpdateLayout();

            var fixedDoc = new System.Windows.Documents.FixedDocument();
            var fixedPage = new System.Windows.Documents.FixedPage
            {
                Width = pageWidth,
                Height = pageHeight
            };
            System.Windows.Documents.FixedPage.SetLeft(printRoot, 0);
            System.Windows.Documents.FixedPage.SetTop(printRoot, 0);
            fixedPage.Children.Add(printRoot);

            var pageContent = new System.Windows.Documents.PageContent();
            ((System.Windows.Markup.IAddChild)pageContent).AddChild(fixedPage);
            fixedDoc.Pages.Add(pageContent);
            return fixedDoc;
        }

        private static System.Windows.Media.Imaging.RenderTargetBitmap RenderElementToBitmap(FrameworkElement element, int pixelWidth, int pixelHeight)
        {
            element.Width = pixelWidth;
            element.Measure(new Size(pixelWidth, pixelHeight));
            element.Arrange(new Rect(new Point(0, 0), new Size(pixelWidth, pixelHeight)));
            element.UpdateLayout();
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                pixelWidth, pixelHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(element);
            return rtb;
        }

        private static System.Windows.Media.Imaging.RenderTargetBitmap RenderElementToBitmap(FrameworkElement element, double logicalWidth, double logicalHeight, double dpi)
        {
            if (dpi <= 0) dpi = 96;
            var pixelWidth = Math.Max(1, (int)Math.Round(logicalWidth * dpi / 96.0));
            var pixelHeight = Math.Max(1, (int)Math.Round(logicalHeight * dpi / 96.0));
            element.Width = logicalWidth;
            element.Height = logicalHeight;
            element.Measure(new Size(logicalWidth, logicalHeight));
            element.Arrange(new Rect(new Point(0, 0), new Size(logicalWidth, logicalHeight)));
            element.UpdateLayout();
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                pixelWidth, pixelHeight, dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(element);
            return rtb;
        }

        private static void SaveBitmapAsPng(System.Windows.Media.Imaging.BitmapSource imageBmp, string path)
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(imageBmp));
            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            {
                encoder.Save(fs);
            }
        }

        private static void SaveBitmapAsPdf(System.Windows.Media.Imaging.BitmapSource imageBmp, string path, double sourceDpi = 96)
        {
            var source = imageBmp;
            if (source.Format != System.Windows.Media.PixelFormats.Bgra32)
            {
                source = new System.Windows.Media.Imaging.FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            }

            int pxW = source.PixelWidth;
            int pxH = source.PixelHeight;
            int srcStride = pxW * 4;
            var srcBytes = new byte[srcStride * pxH];
            source.CopyPixels(srcBytes, srcStride, 0);
            int rgbStride = pxW * 3;
            var rgbBytes = new byte[rgbStride * pxH];
            for (int y = 0; y < pxH; y++)
            {
                int srcRow = y * srcStride;
                int dstRow = y * rgbStride;
                for (int x = 0; x < pxW; x++)
                {
                    int s = srcRow + (x * 4);
                    int d = dstRow + (x * 3);
                    rgbBytes[d + 0] = srcBytes[s + 2];
                    rgbBytes[d + 1] = srcBytes[s + 1];
                    rgbBytes[d + 2] = srcBytes[s + 0];
                }
            }
            var imageBytes = rgbBytes;
            if (sourceDpi <= 0) sourceDpi = 96;
            var pageW = pxW * 72.0 / sourceDpi;
            var pageH = pxH * 72.0 / sourceDpi;
            var content = $"q {pageW:F2} 0 0 {pageH:F2} 0 0 cm /Im0 Do Q";

            var offsets = new List<long>();
            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            using (var bw = new System.IO.BinaryWriter(fs, Encoding.ASCII))
            {
                void WriteAscii(string s) => bw.Write(Encoding.ASCII.GetBytes(s));
                offsets.Add(0);
                WriteAscii("%PDF-1.4\n");

                offsets.Add(fs.Position);
                WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                offsets.Add(fs.Position);
                WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                offsets.Add(fs.Position);
                WriteAscii($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageW:F2} {pageH:F2}] /Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");

                var contentBytes = Encoding.ASCII.GetBytes(content);
                offsets.Add(fs.Position);
                WriteAscii($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
                bw.Write(contentBytes);
                WriteAscii("\nendstream\nendobj\n");

                offsets.Add(fs.Position);
                WriteAscii($"5 0 obj\n<< /Type /XObject /Subtype /Image /Width {pxW} /Height {pxH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {imageBytes.Length} >>\nstream\n");
                bw.Write(imageBytes);
                WriteAscii("\nendstream\nendobj\n");

                var xrefStart = fs.Position;
                WriteAscii($"xref\n0 {offsets.Count}\n");
                WriteAscii("0000000000 65535 f \n");
                for (int i = 1; i < offsets.Count; i++) WriteAscii($"{offsets[i]:D10} 00000 n \n");
                WriteAscii($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF");
            }
        }

        private static void SaveBillAsVectorPdf(BillPreviewData preview, string path, BillFormatLayout layout = null)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            layout = layout ?? new BillFormatLayout();
            var baseFont = layout.FontSize <= 0 ? 13 : layout.FontSize;
            string Esc(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            }
            string Txt(double x, double y, double size, string text, bool bold = false)
            {
                var font = bold ? "/F2" : "/F1";
                return $"BT {font} {size} Tf 1 0 0 1 {x.ToString("0.##", inv)} {y.ToString("0.##", inv)} Tm ({Esc(text ?? string.Empty)}) Tj ET\n";
            }
            string Line(double x1, double y1, double x2, double y2)
            {
                return $"{x1.ToString("0.##", inv)} {y1.ToString("0.##", inv)} m {x2.ToString("0.##", inv)} {y2.ToString("0.##", inv)} l S\n";
            }
            string MapY(double topY, double pageHeight) { return (pageHeight - topY).ToString("0.##", inv); }

            var lines = (preview?.Lines ?? new List<BillPreviewLine>()).ToList();
            var extraRows = Math.Max(0, layout.ExtraRows);
            for (int i = 0; i < extraRows; i++) lines.Add(new BillPreviewLine());

            // Match BuildBillPrintVisual coordinates:
            // width=1100, padding=24, topCanvas=150, table geometry and footer spacing.
            const double uiPageW = 1100;
            const double uiPad = 24;
            const double uiTopCanvasH = 118;
            var rowHeight = layout.RowHeight >= 24 ? layout.RowHeight : 76;

            // Letter landscape points.
            const double pw = 792;
            const double ph = 612;
            var scale = (pw - 8) / uiPageW;
            double SX(double x) { return x * scale + 4; }
            double SY(double yFromTop) { return ph - (yFromTop * scale) - 6; }

            var sb = new StringBuilder();
            sb.Append("0 0 0 RG 0 0 0 rg 0.8 w\n");

            // Header (same as BuildBillPrintVisual)
            var hbLeft = uiPad + 24;
            var hbTop = uiPad + 30;
            var hbW = 240.0;
            var hbH = 72.0;
            var hbBottom = hbTop + hbH;
            sb.Append($"{SX(hbLeft).ToString("0.##", inv)} {SY(hbBottom).ToString("0.##", inv)} {(hbW * scale).ToString("0.##", inv)} {(hbH * scale).ToString("0.##", inv)} re S\n");
            sb.Append(Line(SX(hbLeft), SY(hbTop + hbH / 2), SX(hbLeft + hbW), SY(hbTop + hbH / 2)));
            sb.Append(Txt(SX(hbLeft + 8), SY(hbTop + 20), (baseFont + 1) * scale, $"Bill No. : {preview?.BillNo}", true));
            sb.Append(Txt(SX(hbLeft + 8), SY(hbTop + 56), (baseFont + 1) * scale, $"Date : {preview?.BillDate}"));

            var toX = uiPad + 360;
            sb.Append(Txt(SX(toX), SY(uiPad + 18), (baseFont + 3) * scale, "To", true));
            sb.Append(Txt(SX(uiPad + 430), SY(uiPad + 40), (baseFont + 2) * scale, preview?.Party, true));
            sb.Append(Txt(SX(uiPad + 430), SY(uiPad + 58), baseFont * scale, preview?.PartyAddress));
            sb.Append(Txt(SX(uiPad + 430), SY(uiPad + 76), baseFont * scale, $"Party GST No. : {preview?.PartyGST}"));
            sb.Append(Txt(SX(uiPad + 686), SY(uiPad + 76), baseFont * scale, $"State Code : {preview?.PartyStateCode}"));

            // Table (mapped from DataGrid columns)
            var tX = uiPad;
            var tTop = uiPad + uiTopCanvasH + 2;
            double[] cw = { 90, 90, 150, 110, 290, 110, 90, 110 };
            double tW = cw.Sum();
            double h1 = 24;
            double h2 = 18;
            double rowY = tTop;

            sb.Append($"{SX(tX).ToString("0.##", inv)} {SY(rowY + h1 + h2).ToString("0.##", inv)} {(tW * scale).ToString("0.##", inv)} {((h1 + h2) * scale).ToString("0.##", inv)} re S\n");
            double colX = tX;
            for (int i = 0; i < cw.Length; i++)
            {
                sb.Append(Line(SX(colX), SY(rowY), SX(colX), SY(rowY + h1 + h2)));
                colX += cw[i];
            }
            sb.Append(Line(SX(tX + tW), SY(rowY), SX(tX + tW), SY(rowY + h1 + h2)));
            sb.Append(Line(SX(tX), SY(rowY + h1), SX(tX + tW), SY(rowY + h1)));
            sb.Append(Txt(SX(tX + 22), SY(rowY + 16), (baseFont - 1) * scale, "C/N No", true));
            sb.Append(Txt(SX(tX + cw[0] + 26), SY(rowY + 16), (baseFont - 1) * scale, "Date", true));
            sb.Append(Txt(SX(tX + cw[0] + cw[1] + 34), SY(rowY + 16), (baseFont - 1) * scale, "Invoice No", true));
            sb.Append(Txt(SX(tX + cw[0] + cw[1] + cw[2] + 18), SY(rowY + 16), (baseFont - 1) * scale, "Vehicle No.", true));
            var freightX = tX + cw[0] + cw[1] + cw[2] + cw[3];
            sb.Append(Txt(SX(freightX + 18), SY(rowY + 14), Math.Max(6, (baseFont - 2) * scale), "FREIGHT CHARGES FOR TRANSPORTATION", true));
            sb.Append(Txt(SX(freightX + 6), SY(rowY + 34), Math.Max(6, (baseFont - 3) * scale), "From", true));
            sb.Append(Txt(SX(freightX + cw[4] - 24), SY(rowY + 34), Math.Max(6, (baseFont - 3) * scale), "To", true));
            sb.Append(Txt(SX(freightX + cw[4] + 20), SY(rowY + 16), (baseFont - 1) * scale, "Weight", true));
            sb.Append(Txt(SX(freightX + cw[4] + cw[5] + 26), SY(rowY + 16), (baseFont - 1) * scale, "Rate", true));
            sb.Append(Txt(SX(freightX + cw[4] + cw[5] + cw[6] + 20), SY(rowY + 16), (baseFont - 1) * scale, "Amount", true));

            rowY += (h1 + h2);
            foreach (var r in lines)
            {
                if (SY(rowY + rowHeight) < 92) break;
                sb.Append($"{SX(tX).ToString("0.##", inv)} {SY(rowY + rowHeight).ToString("0.##", inv)} {(tW * scale).ToString("0.##", inv)} {(rowHeight * scale).ToString("0.##", inv)} re S\n");
                colX = tX;
                for (int i = 0; i < cw.Length; i++)
                {
                    sb.Append(Line(SX(colX), SY(rowY), SX(colX), SY(rowY + rowHeight)));
                    colX += cw[i];
                }
                sb.Append(Line(SX(tX + tW), SY(rowY), SX(tX + tW), SY(rowY + rowHeight)));

                var rowCenter = rowY + (rowHeight / 2);
                sb.Append(Txt(SX(tX + 4), SY(rowCenter + 4), Math.Max(6, (baseFont - 2) * scale), r?.LRNo));
                sb.Append(Txt(SX(tX + cw[0] + 4), SY(rowCenter + 4), Math.Max(6, (baseFont - 2) * scale), r?.LRDate));
                sb.Append(Txt(SX(tX + cw[0] + cw[1] + 4), SY(rowCenter + 4), Math.Max(6, (baseFont - 2) * scale), r?.Invoice));
                sb.Append(Txt(SX(tX + cw[0] + cw[1] + cw[2] + 4), SY(rowCenter + 4), Math.Max(6, (baseFont - 2) * scale), r?.Vehicle));
                sb.Append(Txt(SX(freightX + 8), SY(rowY + 18), Math.Max(6, (baseFont - 2) * scale), r?.From, true));
                sb.Append(Txt(SX(freightX + cw[4] - 84), SY(rowY + 18), Math.Max(6, (baseFont - 2) * scale), r?.To, true));
                sb.Append(Txt(SX(freightX + 8), SY(rowY + 36), Math.Max(6, (baseFont - 2) * scale), r?.ChargesBreakdown));
                sb.Append(Txt(SX(freightX + cw[4] + 4), SY(rowCenter + 4), Math.Max(6, (baseFont - 2) * scale), r?.WeightOrType));
                sb.Append(Txt(SX(freightX + cw[4] + cw[5] + 4), SY(rowCenter + 4), Math.Max(6, (baseFont - 2) * scale), r?.Rate));
                sb.Append(Txt(SX(freightX + cw[4] + cw[5] + cw[6] + 4), SY(rowCenter + 4), Math.Max(6, (baseFont - 2) * scale), r?.Amount));
                rowY += rowHeight;
            }

            // Footer
            var totalTop = rowY + 10;
            sb.Append(Txt(SX(uiPad), SY(totalTop + 14), baseFont * scale, $"Rupees in Words : {preview?.TotalWords}", true));
            var totalBoxW = 120.0;
            var totalBoxH = 22.0;
            var totalBoxX = uiPageW - uiPad - totalBoxW;
            sb.Append($"{SX(totalBoxX).ToString("0.##", inv)} {SY(totalTop + totalBoxH).ToString("0.##", inv)} {(totalBoxW * scale).ToString("0.##", inv)} {(totalBoxH * scale).ToString("0.##", inv)} re S\n");
            sb.Append(Txt(SX(totalBoxX - 64), SY(totalTop + 14), baseFont * scale, "Total", true));
            sb.Append(Txt(SX(totalBoxX + 8), SY(totalTop + 14), (baseFont + 1) * scale, preview?.Total, true));
            sb.Append(Txt(SX(uiPad), SY(totalTop + 44), 12 * scale, "Encl.......Ack / ment"));
            sb.Append(Txt(SX(uiPageW - 340), SY(totalTop + 44), 13 * scale, "For Awagaman Trans Logistics", true));

            if (layout.TextBoxes != null)
            {
                for (int i = 0; i < layout.TextBoxes.Count; i++)
                {
                    var t = layout.TextBoxes[i];
                    if (t == null || string.IsNullOrWhiteSpace(t.Text)) continue;
                    sb.Append(Txt(SX(t.X), SY(t.Y + (t.FontSize > 0 ? t.FontSize : baseFont)), (t.FontSize > 0 ? t.FontSize : baseFont) * scale, t.Text));
                }
            }

            var contentBytes = Encoding.ASCII.GetBytes(sb.ToString());
            var offsets = new List<long>();
            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            using (var bw = new System.IO.BinaryWriter(fs, Encoding.ASCII))
            {
                void WriteAscii(string s) => bw.Write(Encoding.ASCII.GetBytes(s));
                offsets.Add(0);
                WriteAscii("%PDF-1.4\n");

                offsets.Add(fs.Position);
                WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                offsets.Add(fs.Position);
                WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                offsets.Add(fs.Position);
                WriteAscii($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pw.ToString("0.##", inv)} {ph.ToString("0.##", inv)}] /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> /Contents 4 0 R >>\nendobj\n");
                offsets.Add(fs.Position);
                WriteAscii($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
                bw.Write(contentBytes);
                WriteAscii("\nendstream\nendobj\n");
                offsets.Add(fs.Position);
                WriteAscii("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");
                offsets.Add(fs.Position);
                WriteAscii("6 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>\nendobj\n");

                var xrefStart = fs.Position;
                WriteAscii($"xref\n0 {offsets.Count}\n");
                WriteAscii("0000000000 65535 f \n");
                for (int i = 1; i < offsets.Count; i++) WriteAscii($"{offsets[i]:D10} 00000 n \n");
                WriteAscii($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF");
            }
        }

        private bool ExportBillPdfFromExcelTemplate(BillPreviewData preview, string pdfPath)
        {
            var templatePath = FindBillExcelTemplatePath();
            if (string.IsNullOrWhiteSpace(templatePath) || !System.IO.File.Exists(templatePath)) return false;
            var tempXlsx = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bill_export_{Guid.NewGuid():N}.xlsx");
            object excelApp = null;
            object workbooks = null;
            object workbook = null;
            object worksheet = null;
            try
            {
                System.IO.File.Copy(templatePath, tempXlsx, true);
                var excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null) return false;
                excelApp = Activator.CreateInstance(excelType);
                excelType.InvokeMember("Visible", System.Reflection.BindingFlags.SetProperty, null, excelApp, new object[] { false });
                excelType.InvokeMember("DisplayAlerts", System.Reflection.BindingFlags.SetProperty, null, excelApp, new object[] { false });

                workbooks = excelType.InvokeMember("Workbooks", System.Reflection.BindingFlags.GetProperty, null, excelApp, null);
                var wbType = workbooks.GetType();
                workbook = wbType.InvokeMember("Open", System.Reflection.BindingFlags.InvokeMethod, null, workbooks, new object[] { tempXlsx });
                var bookType = workbook.GetType();
                var sheets = bookType.InvokeMember("Worksheets", System.Reflection.BindingFlags.GetProperty, null, workbook, null);
                worksheet = sheets.GetType().InvokeMember("Item", System.Reflection.BindingFlags.GetProperty, null, sheets, new object[] { 1 });

                object GetCell(int row, int col)
                {
                    var cells = worksheet.GetType().InvokeMember("Cells", System.Reflection.BindingFlags.GetProperty, null, worksheet, null);
                    var cell = cells.GetType().InvokeMember("Item", System.Reflection.BindingFlags.GetProperty, null, cells, new object[] { row, col });
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(cells);
                    return cell;
                }

                void SetCellByRc(int row, int col, object value)
                {
                    var cell = GetCell(row, col);
                    cell.GetType().InvokeMember("Value2", System.Reflection.BindingFlags.SetProperty, null, cell, new object[] { value });
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(cell);
                }

                string CellText(int row, int col)
                {
                    var cell = GetCell(row, col);
                    var value = cell.GetType().InvokeMember("Value2", System.Reflection.BindingFlags.GetProperty, null, cell, null);
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(cell);
                    return (value == null ? string.Empty : Convert.ToString(value)).Trim();
                }

                bool ContainsInsensitive(string s, string token)
                {
                    return !string.IsNullOrWhiteSpace(s) && s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
                }

                var usedRange = worksheet.GetType().InvokeMember("UsedRange", System.Reflection.BindingFlags.GetProperty, null, worksheet, null);
                int maxRow = Convert.ToInt32(usedRange.GetType().InvokeMember("Rows", System.Reflection.BindingFlags.GetProperty, null, usedRange, null).GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, usedRange.GetType().InvokeMember("Rows", System.Reflection.BindingFlags.GetProperty, null, usedRange, null), null));
                int maxCol = Convert.ToInt32(usedRange.GetType().InvokeMember("Columns", System.Reflection.BindingFlags.GetProperty, null, usedRange, null).GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, usedRange.GetType().InvokeMember("Columns", System.Reflection.BindingFlags.GetProperty, null, usedRange, null), null));
                System.Runtime.InteropServices.Marshal.ReleaseComObject(usedRange);

                // Find anchors by label text from updated template.
                int billNoLabelRow = 0, billNoLabelCol = 0;
                int billDateLabelRow = 0, billDateLabelCol = 0;
                int toLabelRow = 0, toLabelCol = 0;
                int headerRow = 0;
                for (int r = 1; r <= Math.Min(maxRow, 120); r++)
                {
                    for (int c = 1; c <= Math.Min(maxCol, 40); c++)
                    {
                        var t = CellText(r, c);
                        if (billNoLabelRow == 0 && (ContainsInsensitive(t, "bill no") || ContainsInsensitive(t, "billno")))
                        {
                            billNoLabelRow = r; billNoLabelCol = c;
                        }
                        if (billDateLabelRow == 0 && ContainsInsensitive(t, "date"))
                        {
                            if (ContainsInsensitive(t, "bill") || (billNoLabelRow != 0 && Math.Abs(r - billNoLabelRow) <= 3))
                            {
                                billDateLabelRow = r; billDateLabelCol = c;
                            }
                        }
                        if (toLabelRow == 0 && t.Equals("To", StringComparison.OrdinalIgnoreCase))
                        {
                            toLabelRow = r; toLabelCol = c;
                        }
                        if (headerRow == 0 && (ContainsInsensitive(t, "c/n no") || ContainsInsensitive(t, "cn no") || ContainsInsensitive(t, "lr no")))
                        {
                            headerRow = r;
                        }
                    }
                }

                if (billNoLabelRow == 0) { billNoLabelRow = 4; billNoLabelCol = 2; }
                if (billDateLabelRow == 0) { billDateLabelRow = billNoLabelRow + 1; billDateLabelCol = billNoLabelCol; }
                if (toLabelRow == 0) { toLabelRow = billNoLabelRow; toLabelCol = 6; }
                if (headerRow == 0) headerRow = 11;

                // Write header data near detected labels.
                SetCellByRc(billNoLabelRow, Math.Min(maxCol, billNoLabelCol + 1), preview?.BillNo ?? string.Empty);
                SetCellByRc(billDateLabelRow, Math.Min(maxCol, billDateLabelCol + 1), preview?.BillDate ?? string.Empty);
                SetCellByRc(toLabelRow + 1, Math.Min(maxCol, toLabelCol + 1), preview?.Party ?? string.Empty);
                SetCellByRc(toLabelRow + 2, Math.Min(maxCol, toLabelCol + 1), preview?.PartyAddress ?? string.Empty);
                SetCellByRc(toLabelRow + 3, Math.Min(maxCol, toLabelCol + 1), $"Party GST No. : {preview?.PartyGST ?? string.Empty}");
                SetCellByRc(toLabelRow + 4, Math.Min(maxCol, toLabelCol + 1), $"State Code : {preview?.PartyStateCode ?? string.Empty}");

                // Detect actual column positions from header row text.
                int colLr = 1, colDate = 2, colInvoice = 3, colVehicle = 4, colFromTo = 5, colWeight = 8, colRate = 9, colAmount = 10;
                for (int c = 1; c <= Math.Min(maxCol, 40); c++)
                {
                    var h = CellText(headerRow, c);
                    if (ContainsInsensitive(h, "c/n") || ContainsInsensitive(h, "lr no")) colLr = c;
                    else if (ContainsInsensitive(h, "date")) colDate = c;
                    else if (ContainsInsensitive(h, "invoice")) colInvoice = c;
                    else if (ContainsInsensitive(h, "vehicle")) colVehicle = c;
                    else if (ContainsInsensitive(h, "freight") || (ContainsInsensitive(h, "from") && ContainsInsensitive(h, "to"))) colFromTo = c;
                    else if (ContainsInsensitive(h, "weight")) colWeight = c;
                    else if (ContainsInsensitive(h, "rate")) colRate = c;
                    else if (ContainsInsensitive(h, "amount")) colAmount = c;
                }

                var startRow = headerRow + 1;
                var lines = (preview?.Lines ?? new List<BillPreviewLine>()).ToList();
                for (int i = 0; i < lines.Count; i++)
                {
                    var row = startRow + i;
                    var r = lines[i];
                    SetCellByRc(row, colLr, r?.LRNo ?? string.Empty);
                    SetCellByRc(row, colDate, r?.LRDate ?? string.Empty);
                    SetCellByRc(row, colInvoice, r?.Invoice ?? string.Empty);
                    SetCellByRc(row, colVehicle, r?.Vehicle ?? string.Empty);
                    SetCellByRc(row, colFromTo, (r?.From ?? string.Empty) + "  TO  " + (r?.To ?? string.Empty));
                    if (!string.IsNullOrWhiteSpace(r?.ChargesBreakdown)) SetCellByRc(row + 1, colFromTo, r.ChargesBreakdown);
                    SetCellByRc(row, colWeight, r?.WeightOrType ?? string.Empty);
                    SetCellByRc(row, colRate, r?.Rate ?? string.Empty);
                    SetCellByRc(row, colAmount, r?.Amount ?? string.Empty);
                }

                var footerRow = Math.Max(startRow + (lines.Count * 2) + 1, 24);
                SetCellByRc(footerRow, Math.Max(1, colLr), $"Rupees in Words : {preview?.TotalWords ?? string.Empty}");
                SetCellByRc(footerRow, Math.Max(1, colAmount), preview?.Total ?? string.Empty);

                // Landscape + fit-to-page for print quality and stable output
                var pageSetup = worksheet.GetType().InvokeMember("PageSetup", System.Reflection.BindingFlags.GetProperty, null, worksheet, null);
                pageSetup.GetType().InvokeMember("Orientation", System.Reflection.BindingFlags.SetProperty, null, pageSetup, new object[] { 2 }); // xlLandscape
                pageSetup.GetType().InvokeMember("Zoom", System.Reflection.BindingFlags.SetProperty, null, pageSetup, new object[] { false });
                pageSetup.GetType().InvokeMember("FitToPagesWide", System.Reflection.BindingFlags.SetProperty, null, pageSetup, new object[] { 1 });
                pageSetup.GetType().InvokeMember("FitToPagesTall", System.Reflection.BindingFlags.SetProperty, null, pageSetup, new object[] { 1 });
                System.Runtime.InteropServices.Marshal.ReleaseComObject(pageSetup);

                // ExportAsFixedFormat(Type:=0 => PDF)
                bookType.InvokeMember("ExportAsFixedFormat", System.Reflection.BindingFlags.InvokeMethod, null, workbook,
                    new object[] { 0, pdfPath, 0, true, false, Type.Missing, Type.Missing, false, Type.Missing });
                return System.IO.File.Exists(pdfPath);
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (workbook != null)
                    {
                        workbook.GetType().InvokeMember("Close", System.Reflection.BindingFlags.InvokeMethod, null, workbook, new object[] { false });
                    }
                }
                catch { }
                try
                {
                    if (excelApp != null)
                    {
                        excelApp.GetType().InvokeMember("Quit", System.Reflection.BindingFlags.InvokeMethod, null, excelApp, null);
                    }
                }
                catch { }
                if (worksheet != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(worksheet);
                if (workbook != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(workbook);
                if (workbooks != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(workbooks);
                if (excelApp != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(excelApp);
                try { if (System.IO.File.Exists(tempXlsx)) System.IO.File.Delete(tempXlsx); } catch { }
            }
        }

        private bool ExportBillPdfFromRdlc(BillPreviewData preview, string pdfPath)
        {
            try
            {
                var reportPath = FindBillRdlcPath();
                if (string.IsNullOrWhiteSpace(reportPath) || !System.IO.File.Exists(reportPath)) return false;

                var report = new Microsoft.Reporting.WinForms.LocalReport
                {
                    ReportPath = reportPath
                };
                ApplyBillReportData(report, preview);

                string mimeType, encoding, extension;
                string[] streams;
                Microsoft.Reporting.WinForms.Warning[] warnings;
                var deviceInfo =
                    "<DeviceInfo>" +
                    "<OutputFormat>PDF</OutputFormat>" +
                    "<PageWidth>11in</PageWidth>" +
                    "<PageHeight>8.5in</PageHeight>" +
                    "<MarginTop>0.2in</MarginTop>" +
                    "<MarginLeft>0.2in</MarginLeft>" +
                    "<MarginRight>0.2in</MarginRight>" +
                    "<MarginBottom>0.2in</MarginBottom>" +
                    "</DeviceInfo>";

                var bytes = report.Render("PDF", deviceInfo, out mimeType, out encoding, out extension, out streams, out warnings);
                System.IO.File.WriteAllBytes(pdfPath, bytes);
                return System.IO.File.Exists(pdfPath);
            }
            catch
            {
                return false;
            }
        }

        private string BuildBillPreviewKey(BillPreviewData preview)
        {
            var sb = new StringBuilder();
            sb.Append((preview?.BillNo ?? string.Empty).Trim()).Append("|");
            sb.Append((preview?.BillDate ?? string.Empty).Trim()).Append("|");
            sb.Append((preview?.Party ?? string.Empty).Trim()).Append("|");
            sb.Append((preview?.Total ?? string.Empty).Trim()).Append("|");
            var lines = (preview?.Lines ?? new List<BillPreviewLine>());
            foreach (var x in lines)
            {
                sb.Append((x?.LRNo ?? string.Empty).Trim()).Append("^");
                sb.Append((x?.LRDate ?? string.Empty).Trim()).Append("^");
                sb.Append((x?.Invoice ?? string.Empty).Trim()).Append("^");
                sb.Append((x?.Vehicle ?? string.Empty).Trim()).Append("^");
                sb.Append((x?.From ?? string.Empty).Trim()).Append("^");
                sb.Append((x?.To ?? string.Empty).Trim()).Append("^");
                sb.Append((x?.ChargesBreakdown ?? string.Empty).Trim()).Append("^");
                sb.Append((x?.WeightOrType ?? string.Empty).Trim()).Append("^");
                sb.Append((x?.Rate ?? string.Empty).Trim()).Append("^");
                sb.Append((x?.Amount ?? string.Empty).Trim()).Append("|");
            }
            return sb.ToString();
        }

        private System.Data.DataTable BuildBillLinesTable(BillPreviewData preview)
        {
            var linesTable = new System.Data.DataTable("BillLines");
            linesTable.Columns.Add("LRNo", typeof(string));
            linesTable.Columns.Add("LRDate", typeof(string));
            linesTable.Columns.Add("Invoice", typeof(string));
            linesTable.Columns.Add("Vehicle", typeof(string));
            linesTable.Columns.Add("From", typeof(string));
            linesTable.Columns.Add("To", typeof(string));
            linesTable.Columns.Add("RouteAndCharges", typeof(string));
            linesTable.Columns.Add("WeightOrType", typeof(string));
            linesTable.Columns.Add("Rate", typeof(string));
            linesTable.Columns.Add("Amount", typeof(string));
            linesTable.Columns.Add("FreightAmt", typeof(decimal));
            linesTable.Columns.Add("HamaliAmt", typeof(decimal));
            linesTable.Columns.Add("DetentionAmt", typeof(decimal));
            linesTable.Columns.Add("OtherAmt", typeof(decimal));

            var lines = (preview?.Lines ?? new List<BillPreviewLine>()).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                var x = lines[i];
                var routeAndCharges = string.Empty;
                if (!string.IsNullOrWhiteSpace(x?.ChargesBreakdown))
                {
                    // Keep only charge-particular names under From/To with visual spacing.
                    routeAndCharges = Environment.NewLine + x.ChargesBreakdown;
                }
                linesTable.Rows.Add(
                    x?.LRNo ?? string.Empty,
                    x?.LRDate ?? string.Empty,
                    x?.Invoice ?? string.Empty,
                    x?.Vehicle ?? string.Empty,
                    x?.From ?? string.Empty,
                    x?.To ?? string.Empty,
                    routeAndCharges,
                    x?.WeightOrType ?? string.Empty,
                    x?.Rate ?? string.Empty,
                    x?.Amount ?? string.Empty,
                    ParseAmountPart(x?.Amount, 0),
                    ParseAmountPart(x?.Amount, 2),
                    ParseAmountPart(x?.Amount, 3),
                    ParseAmountPart(x?.Amount, 4)
                );
            }
            return linesTable;
        }

        private static decimal ParseAmountPart(string amountLines, int partIndex)
        {
            if (string.IsNullOrWhiteSpace(amountLines)) return 0m;
            var parts = (amountLines ?? string.Empty).Split(new[] { '\n' }, StringSplitOptions.None)
                .Select(x => (x ?? string.Empty).Trim())
                .ToArray();
            if (partIndex < 0 || partIndex >= parts.Length) return 0m;
            if (decimal.TryParse(parts[partIndex], out var v)) return v;
            return 0m;
        }

        private void ApplyBillReportData(Microsoft.Reporting.WinForms.LocalReport report, BillPreviewData preview)
        {
            var key = BuildBillPreviewKey(preview);
            if (!_billLinesCache.TryGetValue(key, out var table))
            {
                table = BuildBillLinesTable(preview);
                _billLinesCache[key] = table;
            }
            report.DataSources.Clear();
            report.DataSources.Add(new Microsoft.Reporting.WinForms.ReportDataSource("BillLines", table));
            report.SetParameters(new[]
            {
                new Microsoft.Reporting.WinForms.ReportParameter("BillNo", preview?.BillNo ?? string.Empty),
                new Microsoft.Reporting.WinForms.ReportParameter("BillDate", preview?.BillDate ?? string.Empty),
                new Microsoft.Reporting.WinForms.ReportParameter("Party", preview?.Party ?? string.Empty),
                new Microsoft.Reporting.WinForms.ReportParameter("PartyAddress", preview?.PartyAddress ?? string.Empty),
                new Microsoft.Reporting.WinForms.ReportParameter("PartyGST", preview?.PartyGST ?? string.Empty),
                new Microsoft.Reporting.WinForms.ReportParameter("PartyStateCode", preview?.PartyStateCode ?? string.Empty),
                new Microsoft.Reporting.WinForms.ReportParameter("TotalWords", preview?.TotalWords ?? string.Empty),
                new Microsoft.Reporting.WinForms.ReportParameter("Total", preview?.Total ?? string.Empty)
            });
        }

        private bool TryShowBillRdlcPreview(BillPreviewData preview, out string error)
        {
            error = null;
            var reportPath = FindBillRdlcPath();
            if (string.IsNullOrWhiteSpace(reportPath) || !System.IO.File.Exists(reportPath))
            {
                error = "RDLC report file not found.";
                return false;
            }

            try
            {
                var currentKey = BuildBillPreviewKey(preview);
                var templateStamp = System.IO.File.GetLastWriteTimeUtc(reportPath).Ticks;
                if (_billRdlcViewer == null || _billRdlcHost == null || _billRdlcPreviewWindow == null)
                {
                    _billRdlcViewer = new Microsoft.Reporting.WinForms.ReportViewer
                    {
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        ProcessingMode = Microsoft.Reporting.WinForms.ProcessingMode.Local
                    };
                    _billRdlcViewer.SetDisplayMode(Microsoft.Reporting.WinForms.DisplayMode.PrintLayout);
                    _billRdlcViewer.ZoomMode = Microsoft.Reporting.WinForms.ZoomMode.PageWidth;
                    _billRdlcViewer.ShowPrintButton = true;
                    _billRdlcViewer.ShowExportButton = true;
                    _billRdlcViewer.ShowRefreshButton = false;

                    _billRdlcHost = new System.Windows.Forms.Integration.WindowsFormsHost { Child = _billRdlcViewer };
                    _billRdlcPreviewWindow = new Window
                    {
                        Title = "Bill Print Preview",
                        Owner = this,
                        Width = 1200,
                        Height = 850,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = _billRdlcHost
                    };
                    _billRdlcPreviewWindow.Closed += (_, __) =>
                    {
                        _billRdlcLastKey = null;
                        _billRdlcLastTemplateStamp = 0;
                        _billRdlcPreviewWindow = null;
                        _billRdlcHost = null;
                        _billRdlcViewer = null;
                    };
                }

                _billRdlcPreviewWindow.Title = $"Bill Print Preview - {preview?.BillNo}";
                if (!string.Equals(_billRdlcLastKey, currentKey, StringComparison.Ordinal) || _billRdlcLastTemplateStamp != templateStamp)
                {
                    _billRdlcViewer.LocalReport.ReportPath = reportPath;
                    ApplyBillReportData(_billRdlcViewer.LocalReport, preview);
                    _billRdlcViewer.RefreshReport();
                    _billRdlcLastKey = currentKey;
                    _billRdlcLastTemplateStamp = templateStamp;
                }

                _billRdlcPreviewWindow.Show();
                _billRdlcPreviewWindow.Activate();
                return true;
            }
            catch (Exception ex)
            {
                error = FlattenExceptionMessage(ex);
                return false;
            }
        }

        private static string FlattenExceptionMessage(Exception ex)
        {
            if (ex == null) return string.Empty;
            var parts = new List<string>();
            var cur = ex;
            while (cur != null)
            {
                if (!string.IsNullOrWhiteSpace(cur.Message)) parts.Add(cur.Message.Trim());
                cur = cur.InnerException;
            }
            return string.Join(" -> ", parts);
        }

        private static void ApplyAlignmentToSelectedCells(DataGrid grid, string alignment)
        {
            if (grid == null || grid.SelectedCells == null || grid.SelectedCells.Count == 0) return;
            var textAlignment = TextAlignment.Center;
            var horizontalAlignment = HorizontalAlignment.Center;
            if (string.Equals(alignment, "Left", StringComparison.OrdinalIgnoreCase))
            {
                textAlignment = TextAlignment.Left;
                horizontalAlignment = HorizontalAlignment.Left;
            }
            else if (string.Equals(alignment, "Right", StringComparison.OrdinalIgnoreCase))
            {
                textAlignment = TextAlignment.Right;
                horizontalAlignment = HorizontalAlignment.Right;
            }

            foreach (var sc in grid.SelectedCells)
            {
                if (sc.Item == null || sc.Column == null) continue;
                var root = sc.Column.GetCellContent(sc.Item) as FrameworkElement;
                if (root == null) continue;

                if (root is TextBlock tbRoot)
                {
                    tbRoot.TextAlignment = textAlignment;
                    tbRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
                else if (root is TextBox tbxRoot)
                {
                    tbxRoot.TextAlignment = textAlignment;
                    tbxRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
                    tbxRoot.HorizontalContentAlignment = horizontalAlignment;
                }

                foreach (var tb in FindVisualChildren<TextBlock>(root))
                {
                    tb.TextAlignment = textAlignment;
                    tb.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
                foreach (var tbx in FindVisualChildren<TextBox>(root))
                {
                    tbx.TextAlignment = textAlignment;
                    tbx.HorizontalAlignment = HorizontalAlignment.Stretch;
                    tbx.HorizontalContentAlignment = horizontalAlignment;
                }
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T hit) return hit;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T hit) yield return hit;
                foreach (var nested in FindVisualChildren<T>(child))
                    yield return nested;
            }
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T hit) return hit;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            return FindParent<T>(child);
        }

        private static void CopySelectedCellsToClipboard(DataGrid grid)
        {
            try
            {
                if (grid == null || grid.SelectedCells == null || grid.SelectedCells.Count == 0) return;
                var selected = grid.SelectedCells
                    .Where(c => c.Item != null && c.Column != null)
                    .Select(c => new
                    {
                        Row = grid.Items.IndexOf(c.Item),
                        Col = c.Column.DisplayIndex,
                        Value = GetCellValueAsText(c.Item, c.Column)
                    })
                    .Where(x => x.Row >= 0 && x.Col >= 0)
                    .OrderBy(x => x.Row)
                    .ThenBy(x => x.Col)
                    .ToList();
                if (selected.Count == 0) return;

                var minRow = selected.Min(x => x.Row);
                var maxRow = selected.Max(x => x.Row);
                var minCol = selected.Min(x => x.Col);
                var maxCol = selected.Max(x => x.Col);
                var map = selected.ToDictionary(x => $"{x.Row}:{x.Col}", x => x.Value ?? string.Empty);
                var sb = new StringBuilder();
                for (int r = minRow; r <= maxRow; r++)
                {
                    for (int c = minCol; c <= maxCol; c++)
                    {
                        if (c > minCol) sb.Append('\t');
                        map.TryGetValue($"{r}:{c}", out var v);
                        sb.Append(v ?? string.Empty);
                    }
                    if (r < maxRow) sb.AppendLine();
                }
                Clipboard.SetText(sb.ToString());
            }
            catch { }
        }

        private static void PasteClipboardIntoGrid(DataGrid grid)
        {
            try
            {
                if (grid == null) return;
                var text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                if (string.IsNullOrWhiteSpace(text)) return;
                var start = grid.CurrentCell;
                if (start.Item == null || start.Column == null) return;
                var startRow = grid.Items.IndexOf(start.Item);
                var startCol = start.Column.DisplayIndex;
                if (startRow < 0 || startCol < 0) return;

                var rows = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (int r = 0; r < rows.Length; r++)
                {
                    if (string.IsNullOrEmpty(rows[r]) && r == rows.Length - 1) break;
                    var cols = rows[r].Split('\t');
                    var rowIndex = startRow + r;
                    if (rowIndex < 0 || rowIndex >= grid.Items.Count) break;
                    var item = grid.Items[rowIndex];
                    for (int c = 0; c < cols.Length; c++)
                    {
                        var colIndex = startCol + c;
                        var col = grid.Columns.FirstOrDefault(x => x.DisplayIndex == colIndex);
                        if (col == null || col.IsReadOnly) continue;
                        SetCellValueFromText(item, col, cols[c]);
                    }
                }
                grid.Items.Refresh();
            }
            catch { }
        }

        private static string GetCellValueAsText(object item, DataGridColumn column)
        {
            var prop = GetBoundPropertyName(column);
            if (string.IsNullOrWhiteSpace(prop) || item == null) return string.Empty;
            var pi = item.GetType().GetProperty(prop);
            if (pi == null) return string.Empty;
            var v = pi.GetValue(item, null);
            return v == null ? string.Empty : Convert.ToString(v);
        }

        private static void SetCellValueFromText(object item, DataGridColumn column, string text)
        {
            var prop = GetBoundPropertyName(column);
            if (string.IsNullOrWhiteSpace(prop) || item == null) return;
            var pi = item.GetType().GetProperty(prop);
            if (pi == null || !pi.CanWrite) return;
            var t = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
            object value = text;
            try
            {
                if (t == typeof(string)) value = text ?? string.Empty;
                else if (t == typeof(int)) value = int.TryParse(text, out var iv) ? iv : 0;
                else if (t == typeof(decimal)) value = decimal.TryParse(text, out var dv) ? dv : 0m;
                else if (t == typeof(double)) value = double.TryParse(text, out var x) ? x : 0d;
                else if (t == typeof(DateTime)) value = DateTime.TryParse(text, out var dt) ? dt : DateTime.MinValue;
                else value = Convert.ChangeType(text, t);
            }
            catch { return; }
            pi.SetValue(item, value, null);
        }

        private static string GetBoundPropertyName(DataGridColumn column)
        {
            if (column is DataGridTextColumn tc && tc.Binding is Binding b && b.Path != null)
            {
                return b.Path.Path;
            }
            if (column is DataGridTemplateColumn t && !string.IsNullOrWhiteSpace(t.SortMemberPath))
            {
                return t.SortMemberPath;
            }
            return null;
        }

        private static string FindBillRdlcPath()
        {
            var candidates = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports", "BillReport.rdlc"),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Reports\BillReport.rdlc")),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Reports\BillReport.rdlc")),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\Reports\BillReport.rdlc"))
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (System.IO.File.Exists(candidates[i])) return candidates[i];
            }
            return null;
        }

        private static string FindBillExcelTemplatePath()
        {
            var candidates = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bill format.xlsx"),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\bill format.xlsx")),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\bill format.xlsx")),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\bill format.xlsx"))
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (System.IO.File.Exists(candidates[i])) return candidates[i];
            }
            return null;
        }

        private bool ValidateBillExcelTemplate(out string error)
        {
            error = null;
            var templatePath = FindBillExcelTemplatePath();
            if (string.IsNullOrWhiteSpace(templatePath) || !System.IO.File.Exists(templatePath))
            {
                error = "Template file 'bill format.xlsx' not found.";
                return false;
            }

            object excelApp = null;
            object workbooks = null;
            object workbook = null;
            object worksheet = null;
            try
            {
                var excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    error = "Microsoft Excel is not installed.";
                    return false;
                }
                excelApp = Activator.CreateInstance(excelType);
                excelType.InvokeMember("Visible", System.Reflection.BindingFlags.SetProperty, null, excelApp, new object[] { false });
                excelType.InvokeMember("DisplayAlerts", System.Reflection.BindingFlags.SetProperty, null, excelApp, new object[] { false });

                workbooks = excelType.InvokeMember("Workbooks", System.Reflection.BindingFlags.GetProperty, null, excelApp, null);
                workbook = workbooks.GetType().InvokeMember("Open", System.Reflection.BindingFlags.InvokeMethod, null, workbooks, new object[] { templatePath, Type.Missing, true });
                var sheets = workbook.GetType().InvokeMember("Worksheets", System.Reflection.BindingFlags.GetProperty, null, workbook, null);
                worksheet = sheets.GetType().InvokeMember("Item", System.Reflection.BindingFlags.GetProperty, null, sheets, new object[] { 1 });

                bool foundBillNo = false, foundDate = false, foundTo = false, foundHeader = false;
                for (int r = 1; r <= 120; r++)
                {
                    for (int c = 1; c <= 40; c++)
                    {
                        var cells = worksheet.GetType().InvokeMember("Cells", System.Reflection.BindingFlags.GetProperty, null, worksheet, null);
                        var cell = cells.GetType().InvokeMember("Item", System.Reflection.BindingFlags.GetProperty, null, cells, new object[] { r, c });
                        var v = cell.GetType().InvokeMember("Value2", System.Reflection.BindingFlags.GetProperty, null, cell, null);
                        var t = ((v == null) ? string.Empty : Convert.ToString(v)).Trim();
                        if (t.IndexOf("bill no", StringComparison.OrdinalIgnoreCase) >= 0) foundBillNo = true;
                        if (t.Equals("date", StringComparison.OrdinalIgnoreCase) || t.IndexOf("bill date", StringComparison.OrdinalIgnoreCase) >= 0) foundDate = true;
                        if (t.Equals("to", StringComparison.OrdinalIgnoreCase)) foundTo = true;
                        if (t.IndexOf("freight charges", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("c/n no", StringComparison.OrdinalIgnoreCase) >= 0) foundHeader = true;
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(cell);
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(cells);
                    }
                }

                if (!foundBillNo || !foundDate || !foundTo || !foundHeader)
                {
                    error = "Template is missing required labels (Bill No, Date, To, or table header).";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { if (workbook != null) workbook.GetType().InvokeMember("Close", System.Reflection.BindingFlags.InvokeMethod, null, workbook, new object[] { false }); } catch { }
                try { if (excelApp != null) excelApp.GetType().InvokeMember("Quit", System.Reflection.BindingFlags.InvokeMethod, null, excelApp, null); } catch { }
                if (worksheet != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(worksheet);
                if (workbook != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(workbook);
                if (workbooks != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(workbooks);
                if (excelApp != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(excelApp);
            }
        }

        private FrameworkElement BuildBillPrintVisual(BillPreviewData preview, BillFormatLayout layout = null, bool enableDrag = false, Action onLayoutChanged = null, List<double> rowHeightsOverride = null, double letterheadTopSpace = 0, double letterheadBottomSpace = 0, bool verticalOnlyBorders = false, bool showRowSelector = true)
        {
            layout = layout ?? LoadBillFormatLayout();
            var baseFont = layout.FontSize <= 0 ? 13 : layout.FontSize;
            var wrapMode = layout.WrapText;
            var baseRowHeight = layout.RowHeight >= 24 ? layout.RowHeight : 42;
            var rowSpacing = layout.RowSpacing >= 0 ? layout.RowSpacing : 0;
            var rowHeight = baseRowHeight + rowSpacing;
            const double billTableWidth = 1040; // 90+90+150+110+290+110+90+110
            var bodyWeight = layout.BoldText ? FontWeights.SemiBold : FontWeights.Normal;
            var bodyStyle = layout.ItalicText ? FontStyles.Italic : FontStyles.Normal;
            var alignText = (layout.Alignment ?? "Center").Trim();
            var hAlign = HorizontalAlignment.Center;
            var tAlign = TextAlignment.Center;
            if (string.Equals(alignText, "Left", StringComparison.OrdinalIgnoreCase))
            {
                hAlign = HorizontalAlignment.Left;
                tAlign = TextAlignment.Left;
            }
            else if (string.Equals(alignText, "Right", StringComparison.OrdinalIgnoreCase))
            {
                hAlign = HorizontalAlignment.Right;
                tAlign = TextAlignment.Right;
            }
            Func<string, string> normalizeForRender = text =>
            {
                if (string.IsNullOrEmpty(text)) return string.Empty;
                var t = text.Replace("\r", string.Empty);
                return t.Trim('\n', '\t', ' ');
            };
            var allLines = (preview?.Lines ?? new List<BillPreviewLine>())
                .Select(x => new BillPreviewLine
                {
                    LRNo = normalizeForRender(x?.LRNo),
                    LRDate = normalizeForRender(x?.LRDate),
                    Invoice = normalizeForRender(x?.Invoice),
                    Vehicle = normalizeForRender(x?.Vehicle),
                    From = normalizeForRender(x?.From),
                    To = normalizeForRender(x?.To),
                    ChargesBreakdown = normalizeForRender(x?.ChargesBreakdown),
                    WeightOrType = normalizeForRender(x?.WeightOrType),
                    Rate = normalizeForRender(x?.Rate),
                    Amount = normalizeForRender(x?.Amount)
                })
                .ToList();
            for (int i = 0; i < Math.Max(0, layout.ExtraRows); i++) allLines.Add(new BillPreviewLine());
            var editableLines = new ObservableCollection<BillPreviewLine>(allLines);

            var page = new Border
            {
                Width = 1100,
                Padding = new Thickness(24),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                BorderThickness = new Thickness(1),
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            TextOptions.SetTextFormattingMode(page, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(page, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(page, TextHintingMode.Fixed);
            RenderOptions.SetBitmapScalingMode(page, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(page, EdgeMode.Unspecified);
            RenderOptions.SetClearTypeHint(page, ClearTypeHint.Enabled);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(0, letterheadBottomSpace + 24)) });
            page.Child = root;

            // Fixed-position header using positions derived from provided bill format.xlsx.
            var topCanvas = new Canvas { Height = 118, Margin = new Thickness(0, Math.Max(0, letterheadTopSpace), 0, 2) };
            var billMetaBox = new Border
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Width = 240,
                Height = 72
            };
            var billMetaGrid = new Grid();
            billMetaGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            billMetaGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            billMetaGrid.Children.Add(new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 1) });
            var billNoText = new TextBlock
            {
                Text = $"Bill No. : {preview.BillNo}",
                FontSize = baseFont + 1,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            billMetaGrid.Children.Add(billNoText);

            var billDateText = new TextBlock
            {
                Text = $"Date : {preview.BillDate}",
                FontSize = baseFont + 1,
                Foreground = Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetRow(billDateText, 1);
            billMetaGrid.Children.Add(billDateText);
            billMetaBox.Child = billMetaGrid;
            Canvas.SetLeft(billMetaBox, 24);   // left aligned as requested
            Canvas.SetTop(billMetaBox, 30);
            topCanvas.Children.Add(billMetaBox);

            var toText = new TextBlock
            {
                Text = "To",
                FontSize = baseFont + 3,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black
            };
            Canvas.SetLeft(toText, 360);      // Excel: row4 around col E
            Canvas.SetTop(toText, 4);
            topCanvas.Children.Add(toText);

            var partyBox = new Border
            {
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0)
            };
            var partyStack = new StackPanel();
            partyStack.Children.Add(new TextBlock { Text = preview.Party, FontSize = baseFont + 2, FontWeight = FontWeights.Bold, TextWrapping = wrapMode ? TextWrapping.Wrap : TextWrapping.NoWrap });
            if (!string.IsNullOrWhiteSpace(preview.PartyAddress))
                partyStack.Children.Add(new TextBlock { Text = preview.PartyAddress, FontSize = baseFont, TextWrapping = wrapMode ? TextWrapping.Wrap : TextWrapping.NoWrap, Margin = new Thickness(0, 4, 0, 0) });
            var gstStateRow = new Grid { Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            gstStateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            gstStateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            var gstText = new TextBlock
            {
                Text = $"Party GST No. : {preview.PartyGST}",
                FontSize = baseFont,
                TextWrapping = wrapMode ? TextWrapping.Wrap : TextWrapping.NoWrap
            };
            var stateCodeText = new TextBlock
            {
                Text = $"State Code : {preview.PartyStateCode}",
                FontSize = baseFont,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(14, 0, 0, 0)
            };
            Grid.SetColumn(stateCodeText, 1);
            gstStateRow.Children.Add(gstText);
            gstStateRow.Children.Add(stateCodeText);
            partyStack.Children.Add(gstStateRow);
            partyBox.Child = partyStack;

            Canvas.SetLeft(partyBox, 430);      // Excel: row5-row8 around col F-L area
            Canvas.SetTop(partyBox, 26);
            partyBox.Width = 610;
            topCanvas.Children.Add(partyBox);

            root.Children.Add(topCanvas);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeColumns = enableDrag,
                IsReadOnly = !enableDrag,
                // Use a single custom row-resize path in edit mode for consistent behavior.
                CanUserResizeRows = false,
                EnableRowVirtualization = false,
                EnableColumnVirtualization = false,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
                RowHeaderWidth = (enableDrag && !verticalOnlyBorders && showRowSelector) ? 28 : 0,
                HeadersVisibility = (enableDrag && !verticalOnlyBorders && showRowSelector) ? DataGridHeadersVisibility.All : DataGridHeadersVisibility.Column,
                GridLinesVisibility = (enableDrag && !verticalOnlyBorders && showRowSelector) ? DataGridGridLinesVisibility.All : DataGridGridLinesVisibility.Vertical,
                HorizontalGridLinesBrush = (enableDrag && !verticalOnlyBorders && showRowSelector) ? new SolidColorBrush(Color.FromRgb(210, 210, 210)) : Brushes.Transparent,
                VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                RowHeight = enableDrag ? double.NaN : rowHeight,
                MinRowHeight = 24,
                FontSize = baseFont,
                FontWeight = bodyWeight,
                FontStyle = bodyStyle,
                ItemsSource = editableLines,
                ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,
                Background = Brushes.White,
                RowBackground = Brushes.White,
                AlternatingRowBackground = Brushes.White,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            if (!enableDrag || verticalOnlyBorders || !showRowSelector)
            {
                ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Disabled);
                ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Disabled);
                ScrollViewer.SetCanContentScroll(grid, false);
            }
            if (rowHeightsOverride != null && rowHeightsOverride.Count > 0)
            {
                if (enableDrag)
                {
                    // Ensure row heights are applied even when rendering offscreen (preview/PDF),
                    // where Loaded timing can differ.
                    grid.LoadingRow += (s, e) =>
                    {
                        var idx = e.Row.GetIndex();
                        if (idx >= 0 && idx < rowHeightsOverride.Count)
                        {
                            var h = rowHeightsOverride[idx];
                            if (h > 0) e.Row.Height = h;
                        }
                    };

                    RoutedEventHandler applyOnce = null;
                    applyOnce = (s, e) =>
                    {
                        grid.Loaded -= applyOnce;
                        grid.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                grid.UpdateLayout();
                                for (int i = 0; i < Math.Min(grid.Items.Count, rowHeightsOverride.Count); i++)
                                {
                                    var row = grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                                    if (row == null) continue;
                                    var h = rowHeightsOverride[i];
                                    if (h > 0) row.Height = h;
                                }
                            }
                            catch { }
                        }), System.Windows.Threading.DispatcherPriority.Loaded);
                    };
                    grid.Loaded += applyOnce;
                }
                else
                {
                    grid.LoadingRow += (s, e) =>
                    {
                        var idx = e.Row.GetIndex();
                        if (idx >= 0 && idx < rowHeightsOverride.Count)
                        {
                            var h = rowHeightsOverride[idx];
                            if (h > 0) e.Row.Height = h;
                        }
                    };
                }
            }
            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(220, 220, 220))));
            cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0.5)));
            cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
            cellStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
            cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
            var selectedTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(217, 237, 255))));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
            selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(124, 181, 236))));
            cellStyle.Triggers.Add(selectedTrigger);
            if (!enableDrag || verticalOnlyBorders || !showRowSelector)
            {
                // In print-preview mode, use DataGrid vertical grid lines only.
                // Per-cell borders create tiny seams ("cuts") between rows on some DPI settings.
                cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
                cellStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
                // Keep text anchored at top when row heights are increased.
                cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Top));
                cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 2, 2, 0)));

                // Force top placement at template level to avoid theme-level vertical centering.
                var cellTemplate = new ControlTemplate(typeof(DataGridCell));
                var border = new FrameworkElementFactory(typeof(Border));
                border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
                border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
                border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

                var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
                presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
                presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
                presenter.SetValue(TextBlock.TextAlignmentProperty, tAlign);
                border.AppendChild(presenter);
                cellTemplate.VisualTree = border;
                cellStyle.Setters.Add(new Setter(Control.TemplateProperty, cellTemplate));
            }
            grid.CellStyle = cellStyle;
            if (!enableDrag || verticalOnlyBorders || !showRowSelector)
            {
                var printRowStyle = new Style(typeof(DataGridRow));
                printRowStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Top));
                printRowStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top));
                grid.RowStyle = printRowStyle;
            }

            if (enableDrag)
            {
                VirtualizingPanel.SetIsVirtualizing(grid, false);
                var editRowStyle = new Style(typeof(DataGridRow));
                editRowStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 24.0));
                editRowStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, double.NaN));
                grid.RowStyle = editRowStyle;
                DataGridRow resizingRow = null;
                double resizeStartY = 0;
                double resizeStartHeight = 0;
                bool manualRowResize = false;

                var cellMenu = new ContextMenu();
                var insertAboveItem = new MenuItem { Header = "Insert Row Above" };
                var insertBelowItem = new MenuItem { Header = "Insert Row Below" };
                var deleteRowItem = new MenuItem { Header = "Delete Row" };
                cellMenu.Items.Add(insertAboveItem);
                cellMenu.Items.Add(insertBelowItem);
                cellMenu.Items.Add(new Separator());
                cellMenu.Items.Add(deleteRowItem);
                grid.ContextMenu = cellMenu;

                Action<bool> insertRow = insertAbove =>
                {
                    grid.CommitEdit(DataGridEditingUnit.Cell, true);
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
                    var current = grid.CurrentCell;
                    if (current.Item == null) return;
                    var rowIndex = editableLines.IndexOf(current.Item as BillPreviewLine);
                    if (rowIndex < 0) return;
                    var insertIndex = insertAbove ? rowIndex : rowIndex + 1;
                    insertIndex = Math.Max(0, Math.Min(insertIndex, editableLines.Count));
                    var newLine = new BillPreviewLine();
                    editableLines.Insert(insertIndex, newLine);
                    grid.SelectedCells.Clear();
                    grid.CurrentCell = new DataGridCellInfo(newLine, grid.Columns.FirstOrDefault());
                    grid.SelectedItem = newLine;
                    grid.ScrollIntoView(newLine);
                    grid.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (rowHeightsOverride == null) return;
                            rowHeightsOverride.Clear();
                            for (int i = 0; i < grid.Items.Count; i++)
                            {
                                var rowContainer = grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                                rowHeightsOverride.Add(rowContainer != null && rowContainer.ActualHeight > 0 ? rowContainer.ActualHeight : rowHeight);
                            }
                        }
                        catch { }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                };

                insertAboveItem.Click += (s, e) => insertRow(true);
                insertBelowItem.Click += (s, e) => insertRow(false);
                deleteRowItem.Click += (s, e) =>
                {
                    grid.CommitEdit(DataGridEditingUnit.Cell, true);
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
                    var current = grid.CurrentCell;
                    if (current.Item == null) return;
                    var row = current.Item as BillPreviewLine;
                    if (row == null) return;
                    var idx = editableLines.IndexOf(row);
                    if (idx < 0) return;
                    editableLines.RemoveAt(idx);
                    if (editableLines.Count == 0) return;
                    var nextIdx = Math.Min(idx, editableLines.Count - 1);
                    var nextRow = editableLines[nextIdx];
                    var firstCol = grid.Columns.FirstOrDefault();
                    if (firstCol != null)
                    {
                        grid.CurrentCell = new DataGridCellInfo(nextRow, firstCol);
                        grid.SelectedCells.Clear();
                        grid.SelectedCells.Add(grid.CurrentCell);
                    }
                    grid.SelectedItem = nextRow;
                    grid.ScrollIntoView(nextRow);
                    grid.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (rowHeightsOverride == null) return;
                            rowHeightsOverride.Clear();
                            for (int i = 0; i < grid.Items.Count; i++)
                            {
                                var rowContainerAfterDelete = grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                                rowHeightsOverride.Add(rowContainerAfterDelete != null && rowContainerAfterDelete.ActualHeight > 0 ? rowContainerAfterDelete.ActualHeight : rowHeight);
                            }
                        }
                        catch { }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                };

                grid.ContextMenuOpening += (s, e) =>
                {
                    var hit = Mouse.DirectlyOver as DependencyObject;
                    var cell = FindParent<DataGridCell>(hit);
                    if (cell == null) return;
                    cell.Focus();
                    grid.CurrentCell = new DataGridCellInfo(cell);
                    grid.SelectedCells.Clear();
                    grid.SelectedCells.Add(grid.CurrentCell);
                };

                grid.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    var source = e.OriginalSource as DependencyObject;
                    var hitRow = FindParent<DataGridRow>(source);
                    if (hitRow != null)
                    {
                        var hitHeader = FindParent<DataGridRowHeader>(source);
                        var posInRow = e.GetPosition(hitRow);
                        var nearBottomRow = posInRow.Y >= Math.Max(0, hitRow.ActualHeight - 8);
                        var nearBottomHeader = false;
                        if (hitHeader != null)
                        {
                            var posInHeader = e.GetPosition(hitHeader);
                            nearBottomHeader = posInHeader.Y >= Math.Max(0, hitHeader.ActualHeight - 8);
                        }
                        if (nearBottomRow || nearBottomHeader)
                        {
                            manualRowResize = true;
                            resizingRow = hitRow;
                            resizeStartY = e.GetPosition(grid).Y;
                            resizeStartHeight = hitRow.ActualHeight > 0 ? hitRow.ActualHeight : (grid.RowHeight > 0 ? grid.RowHeight : 24);
                            hitRow.CaptureMouse();
                            e.Handled = true;
                            return;
                        }
                    }

                    var hit = e.OriginalSource as DependencyObject;
                    var cell = FindParent<DataGridCell>(hit);
                    if (cell == null) return;
                    if (cell.IsEditing) return;
                    cell.Focus();
                    grid.CurrentCell = new DataGridCellInfo(cell);
                };
                grid.PreviewMouseMove += (s, e) =>
                {
                    var source = e.OriginalSource as DependencyObject;
                    if (!manualRowResize)
                    {
                        var hoverRow = FindParent<DataGridRow>(source);
                        if (hoverRow != null)
                        {
                            var hoverHeader = FindParent<DataGridRowHeader>(source);
                            var posInRow = e.GetPosition(hoverRow);
                            var nearBottomRow = posInRow.Y >= Math.Max(0, hoverRow.ActualHeight - 8);
                            var nearBottomHeader = false;
                            if (hoverHeader != null)
                            {
                                var posInHeader = e.GetPosition(hoverHeader);
                                nearBottomHeader = posInHeader.Y >= Math.Max(0, hoverHeader.ActualHeight - 8);
                            }
                            grid.Cursor = (nearBottomRow || nearBottomHeader) ? Cursors.SizeNS : Cursors.Arrow;
                        }
                        else
                        {
                            grid.Cursor = Cursors.Arrow;
                        }
                        return;
                    }

                    if (resizingRow == null) return;
                    var currentY = e.GetPosition(grid).Y;
                    var delta = currentY - resizeStartY;
                    var target = Math.Max(grid.MinRowHeight > 0 ? grid.MinRowHeight : 24, resizeStartHeight + delta);
                    resizingRow.Height = target;
                    e.Handled = true;
                };
                grid.PreviewMouseLeftButtonUp += (s, e) =>
                {
                    if (!manualRowResize) return;
                    manualRowResize = false;
                    if (resizingRow != null)
                    {
                        try { resizingRow.ReleaseMouseCapture(); } catch { }
                        resizingRow = null;
                    }
                    grid.Cursor = Cursors.Arrow;
                    grid.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (rowHeightsOverride == null) return;
                            rowHeightsOverride.Clear();
                            for (int i = 0; i < grid.Items.Count; i++)
                            {
                                var row = grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                                rowHeightsOverride.Add(row != null && row.ActualHeight > 0 ? row.ActualHeight : rowHeight);
                            }
                        }
                        catch { }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                    e.Handled = true;
                };

                grid.PreparingCellForEdit += (s, e) =>
                {
                    // Keep a spreadsheet-like experience while editing cells.
                    if (e.EditingElement is TextBox editor)
                    {
                        grid.Dispatcher.BeginInvoke(new Action(() => editor.SelectAll()), System.Windows.Threading.DispatcherPriority.Input);
                    }
                };
                grid.PreviewKeyDown += (s, e) =>
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.A)
                    {
                        grid.SelectAllCells();
                        e.Handled = true;
                        return;
                    }
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.C)
                    {
                        CopySelectedCellsToClipboard(grid);
                        e.Handled = true;
                        return;
                    }
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.V)
                    {
                        PasteClipboardIntoGrid(grid);
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Delete)
                    {
                        try { grid.CommitEdit(DataGridEditingUnit.Cell, true); } catch { }
                        try { grid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
                        foreach (var sc in grid.SelectedCells)
                        {
                            if (sc.Item == null || sc.Column == null || sc.Column.IsReadOnly) continue;
                            SetCellValueFromText(sc.Item, sc.Column, string.Empty);
                        }
                        grid.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var view = CollectionViewSource.GetDefaultView(grid.ItemsSource ?? grid.Items);
                                view?.Refresh();
                            }
                            catch { }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Enter)
                    {
                        var current = grid.CurrentCell;
                        if (current.Item == null || current.Column == null) return;
                        var rowIndex = grid.Items.IndexOf(current.Item);
                        var nextIndex = Math.Min(rowIndex + 1, grid.Items.Count - 1);
                        if (nextIndex >= 0 && nextIndex < grid.Items.Count)
                        {
                            var nextItem = grid.Items[nextIndex];
                            grid.CurrentCell = new DataGridCellInfo(nextItem, current.Column);
                            grid.SelectedCells.Clear();
                            grid.SelectedCells.Add(grid.CurrentCell);
                            grid.ScrollIntoView(nextItem, current.Column);
                            e.Handled = true;
                        }
                    }
                };
            }
            var centeredHeaderStyle = new Style(typeof(DataGridColumnHeader));
            centeredHeaderStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            centeredHeaderStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            centeredHeaderStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            grid.ColumnHeaderStyle = centeredHeaderStyle;

            var centeredTextStyle = new Style(typeof(TextBlock));
            centeredTextStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, tAlign));
            centeredTextStyle.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
            centeredTextStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top));
            centeredTextStyle.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(2, 2, 2, 0)));

            var invoiceTextStyle = new Style(typeof(TextBlock), centeredTextStyle);
            invoiceTextStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top));
            invoiceTextStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));

            var invoiceEditStyle = new Style(typeof(TextBox));
            invoiceEditStyle.Setters.Add(new Setter(TextBox.TextAlignmentProperty, tAlign));
            invoiceEditStyle.Setters.Add(new Setter(TextBox.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
            invoiceEditStyle.Setters.Add(new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Top));
            invoiceEditStyle.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap));
            invoiceEditStyle.Setters.Add(new Setter(TextBox.AcceptsReturnProperty, true));
            invoiceEditStyle.Setters.Add(new Setter(TextBox.BorderThicknessProperty, new Thickness(0)));
            invoiceEditStyle.Setters.Add(new Setter(TextBox.BackgroundProperty, Brushes.Transparent));

            Func<string, string, double, Style, DataGridColumn> makeCol = (header, path, width, style) =>
            {
                if (enableDrag)
                {
                    if (string.Equals(path, "Invoice", StringComparison.Ordinal))
                        return new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width, ElementStyle = style, EditingElementStyle = invoiceEditStyle };
                    return new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width, ElementStyle = style };
                }

                var dt = new DataTemplate();
                var tb = new FrameworkElementFactory(typeof(TextBlock));
                tb.SetBinding(TextBlock.TextProperty, new Binding(path));
                tb.SetValue(TextBlock.TextWrappingProperty, wrapMode ? TextWrapping.Wrap : TextWrapping.NoWrap);
                tb.SetValue(TextBlock.TextAlignmentProperty, tAlign);
                tb.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
                tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top);
                tb.SetValue(TextBlock.MarginProperty, new Thickness(2, 2, 2, 0));
                dt.VisualTree = tb;
                return new DataGridTemplateColumn { Header = header, Width = width, CellTemplate = dt };
            };

            grid.Columns.Add(makeCol("C/N No", "LRNo", 90, centeredTextStyle));
            grid.Columns.Add(makeCol("Date", "LRDate", 90, centeredTextStyle));
            grid.Columns.Add(makeCol("Invoice No", "Invoice", 150, invoiceTextStyle));
            grid.Columns.Add(makeCol("Vehicle No.", "Vehicle", 110, centeredTextStyle));
            var freightHeader = new StackPanel { Orientation = Orientation.Vertical };
            freightHeader.Children.Add(new TextBlock
            {
                Text = "FREIGHT CHARGES FOR TRANSPORTATION",
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
            var freightHeaderSub = new Grid { Margin = new Thickness(4, 2, 4, 0) };
            freightHeaderSub.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            freightHeaderSub.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var fromHdr = new TextBlock { Text = "From", HorizontalAlignment = HorizontalAlignment.Left, FontSize = Math.Max(10, baseFont - 2) };
            var toHdr = new TextBlock { Text = "To", HorizontalAlignment = HorizontalAlignment.Right, FontSize = Math.Max(10, baseFont - 2), Margin = new Thickness(0, 0, 24, 0) };
            Grid.SetColumn(fromHdr, 0);
            Grid.SetColumn(toHdr, 1);
            freightHeaderSub.Children.Add(fromHdr);
            freightHeaderSub.Children.Add(toHdr);
            freightHeader.Children.Add(freightHeaderSub);

            var freightCellTemplate = new DataTemplate();
            var freightCellStack = new FrameworkElementFactory(typeof(StackPanel));
            freightCellStack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            freightCellStack.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            freightCellStack.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

            FrameworkElementFactory chargesCell;
            if (enableDrag)
            {
                chargesCell = new FrameworkElementFactory(typeof(TextBox));
                chargesCell.SetBinding(TextBox.TextProperty, new Binding("ChargesBreakdown") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                chargesCell.SetValue(TextBox.TextWrappingProperty, wrapMode ? TextWrapping.Wrap : TextWrapping.NoWrap);
                chargesCell.SetValue(TextBox.AcceptsReturnProperty, true);
                chargesCell.SetValue(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Top);
                chargesCell.SetValue(TextBox.HorizontalContentAlignmentProperty, hAlign);
                chargesCell.SetValue(TextBox.FontSizeProperty, baseFont);
                chargesCell.SetValue(TextBox.MarginProperty, new Thickness(0));
                chargesCell.SetValue(TextBox.ForegroundProperty, Brushes.Black);
                chargesCell.SetValue(TextBox.TextAlignmentProperty, tAlign);
                chargesCell.SetValue(TextBox.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
                chargesCell.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
                chargesCell.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
            }
            else
            {
                chargesCell = new FrameworkElementFactory(typeof(TextBlock));
                chargesCell.SetBinding(TextBlock.TextProperty, new Binding("ChargesBreakdown"));
                chargesCell.SetValue(TextBlock.TextWrappingProperty, wrapMode ? TextWrapping.Wrap : TextWrapping.NoWrap);
                chargesCell.SetValue(TextBlock.FontSizeProperty, baseFont);
                chargesCell.SetValue(TextBlock.MarginProperty, new Thickness(2, 2, 2, 0));
                chargesCell.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
                chargesCell.SetValue(TextBlock.TextAlignmentProperty, tAlign);
                chargesCell.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
                chargesCell.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top);
            }

            freightCellStack.AppendChild(chargesCell);
            freightCellTemplate.VisualTree = freightCellStack;

            var freightCellStyle = new Style(typeof(DataGridCell), cellStyle);
            freightCellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = freightHeader,
                CellTemplate = freightCellTemplate,
                CellStyle = freightCellStyle,
                Width = 290
            });
            grid.Columns.Add(makeCol("Weight", "WeightOrType", 110, centeredTextStyle));
            grid.Columns.Add(makeCol("Rate", "Rate", 90, centeredTextStyle));
            var amountTemplate = new DataTemplate();
            var amountText = new FrameworkElementFactory(typeof(TextBlock));
            amountText.SetBinding(TextBlock.TextProperty, new Binding("Amount"));
            amountText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            amountText.SetValue(TextBlock.TextAlignmentProperty, tAlign);
            amountText.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            amountText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top);
            amountText.SetValue(TextBlock.MarginProperty, new Thickness(2, 0, 2, 0));
            amountTemplate.VisualTree = amountText;
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Amount", CellTemplate = amountTemplate, Width = 110 });
            if (!enableDrag || verticalOnlyBorders || !showRowSelector)
            {
                // Keep a stable letterhead-sized table even when there is only 1 bill line.
                const int minPrintRows = 10;
                while (editableLines.Count < minPrintRows)
                    editableLines.Add(new BillPreviewLine());

                // Prevent trailing filler area that looks like an extra column after Amount in print/preview.
                grid.HorizontalAlignment = HorizontalAlignment.Left;
                grid.Width = billTableWidth;
                var headerHeight = grid.ColumnHeaderHeight > 0 ? grid.ColumnHeaderHeight : 46;
                var contentRows = Math.Max(1, editableLines.Count);
                double rowsHeight = 0;
                for (int i = 0; i < contentRows; i++)
                {
                    var h = (rowHeightsOverride != null && i < rowHeightsOverride.Count && rowHeightsOverride[i] > 0)
                        ? rowHeightsOverride[i]
                        : rowHeight;
                    rowsHeight += h;
                }
                grid.Height = headerHeight + rowsHeight + 2;
            }
            Grid.SetRow(grid, 2);
            root.Children.Add(grid);

            var totalRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            if (!enableDrag || verticalOnlyBorders)
            {
                totalRow.HorizontalAlignment = HorizontalAlignment.Left;
                totalRow.Width = billTableWidth;
            }
            totalRow.Children.Add(new TextBlock { Text = $"Rupees in Words : {preview.TotalWords}", FontSize = baseFont, FontWeight = FontWeights.SemiBold, TextWrapping = wrapMode ? TextWrapping.Wrap : TextWrapping.NoWrap });
            var totalLabel = new TextBlock { Text = "Total", FontSize = baseFont, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(totalLabel, 1);
            totalRow.Children.Add(totalLabel);
            var totalValue = new Border
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock { Text = preview.Total, FontSize = baseFont, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right }
            };
            Grid.SetColumn(totalValue, 2);
            totalRow.Children.Add(totalValue);
            Grid.SetRow(totalRow, 3);
            root.Children.Add(totalRow);

            var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
            if (!enableDrag || verticalOnlyBorders)
            {
                footer.HorizontalAlignment = HorizontalAlignment.Left;
                footer.Width = billTableWidth;
            }
            footer.Children.Add(new TextBlock { Text = "Encl…………Ack / ment", FontSize = 12 });
            var sign = new TextBlock { Text = "For Awagaman Trans Logistics", FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(sign, 1);
            footer.Children.Add(sign);
            Grid.SetRow(footer, 4);
            root.Children.Add(footer);

            if (layout.TextBoxes != null && layout.TextBoxes.Count > 0)
            {
                var overlay = new Canvas { IsHitTestVisible = enableDrag };
                Grid.SetRowSpan(overlay, 5);
                foreach (var t in layout.TextBoxes)
                {
                    FrameworkElement tb;
                    TextBox editBox = null;
                    if (enableDrag)
                    {
                        editBox = new TextBox
                        {
                            Text = t.Text ?? string.Empty,
                            FontSize = t.FontSize > 0 ? t.FontSize : baseFont,
                            TextWrapping = t.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                            Width = t.Width > 10 ? t.Width : 180,
                            Height = t.Height > 10 ? t.Height : 24,
                            Foreground = Brushes.Black,
                            Background = Brushes.Transparent,
                            BorderThickness = new Thickness(0),
                            AcceptsReturn = true,
                            VerticalContentAlignment = VerticalAlignment.Top
                        };
                        editBox.TextChanged += (s, e) =>
                        {
                            t.Text = editBox.Text ?? string.Empty;
                            SaveBillFormatLayout(layout);
                            onLayoutChanged?.Invoke();
                        };
                        tb = editBox;
                    }
                    else
                    {
                        tb = new TextBlock
                        {
                            Text = t.Text ?? string.Empty,
                            FontSize = t.FontSize > 0 ? t.FontSize : baseFont,
                            TextWrapping = t.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                            Width = t.Width > 10 ? t.Width : 180,
                            Height = t.Height > 10 ? t.Height : 24,
                            Foreground = Brushes.Black
                        };
                    }
                    if (!enableDrag)
                    {
                        Canvas.SetLeft(tb, t.X);
                        Canvas.SetTop(tb, t.Y);
                        overlay.Children.Add(tb);
                    }
                    else
                    {
                        var moveGrip = new Border
                        {
                            Height = 18,
                            Background = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0),
                            Cursor = Cursors.SizeAll
                        };
                        var resizeGrip = new Border
                        {
                            Width = 10,
                            Height = 10,
                            Background = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Bottom,
                            Margin = new Thickness(0),
                            Cursor = Cursors.SizeNWSE
                        };
                        var holderGrid = new Grid();
                        holderGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
                        holderGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        Grid.SetRow(moveGrip, 0);
                        Grid.SetRow(tb, 1);
                        Grid.SetRow(resizeGrip, 1);
                        holderGrid.Children.Add(tb);
                        holderGrid.Children.Add(moveGrip);
                        holderGrid.Children.Add(resizeGrip);

                        var holder = new Border
                        {
                            BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                            BorderThickness = new Thickness(1),
                            Background = Brushes.Transparent,
                            Child = holderGrid,
                            Cursor = Cursors.SizeAll,
                            ToolTip = "Drag: Alt + Left mouse (or Right-drag)"
                        };
                        holder.Width = t.Width > 10 ? t.Width : 180;
                        holder.Height = (t.Height > 10 ? t.Height : 24) + 18;
                        Canvas.SetLeft(holder, t.X);
                        Canvas.SetTop(holder, t.Y);

                        Point dragStart = new Point();
                        double startX = 0;
                        double startY = 0;
                        bool dragging = false;

                        moveGrip.MouseLeftButtonDown += (s, e) =>
                        {
                            dragging = true;
                            dragStart = e.GetPosition(overlay);
                            startX = Canvas.GetLeft(holder);
                            startY = Canvas.GetTop(holder);
                            moveGrip.CaptureMouse();
                            e.Handled = true;
                        };
                        moveGrip.MouseMove += (s, e) =>
                        {
                            if (!dragging) return;
                            var p = e.GetPosition(overlay);
                            var dx = p.X - dragStart.X;
                            var dy = p.Y - dragStart.Y;
                            var nx = Math.Max(0, startX + dx);
                            var ny = Math.Max(0, startY + dy);
                            Canvas.SetLeft(holder, nx);
                            Canvas.SetTop(holder, ny);
                        };
                        moveGrip.MouseLeftButtonUp += (s, e) =>
                        {
                            if (!dragging) return;
                            dragging = false;
                            moveGrip.ReleaseMouseCapture();
                            t.X = Canvas.GetLeft(holder);
                            t.Y = Canvas.GetTop(holder);
                            SaveBillFormatLayout(layout);
                            onLayoutChanged?.Invoke();
                            e.Handled = true;
                        };

                        // Guaranteed move gesture: Alt + Left drag anywhere on the textbox container.
                        bool altDragging = false;
                        Point altDragStart = new Point();
                        double altStartX = 0;
                        double altStartY = 0;
                        holder.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            if ((Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt) return;
                            altDragging = true;
                            altDragStart = e.GetPosition(overlay);
                            altStartX = Canvas.GetLeft(holder);
                            altStartY = Canvas.GetTop(holder);
                            holder.CaptureMouse();
                            e.Handled = true;
                        };
                        holder.PreviewMouseMove += (s, e) =>
                        {
                            if (!altDragging) return;
                            var p = e.GetPosition(overlay);
                            var dx = p.X - altDragStart.X;
                            var dy = p.Y - altDragStart.Y;
                            Canvas.SetLeft(holder, Math.Max(0, altStartX + dx));
                            Canvas.SetTop(holder, Math.Max(0, altStartY + dy));
                        };
                        holder.PreviewMouseLeftButtonUp += (s, e) =>
                        {
                            if (!altDragging) return;
                            altDragging = false;
                            holder.ReleaseMouseCapture();
                            t.X = Canvas.GetLeft(holder);
                            t.Y = Canvas.GetTop(holder);
                            SaveBillFormatLayout(layout);
                            onLayoutChanged?.Invoke();
                            e.Handled = true;
                        };

                        // Drag from anywhere on text box using right mouse button
                        bool rightDragging = false;
                        Point rightDragStart = new Point();
                        double rightStartX = 0;
                        double rightStartY = 0;
                        holder.PreviewMouseRightButtonDown += (s, e) =>
                        {
                            rightDragging = true;
                            rightDragStart = e.GetPosition(overlay);
                            rightStartX = Canvas.GetLeft(holder);
                            rightStartY = Canvas.GetTop(holder);
                            holder.CaptureMouse();
                            e.Handled = true;
                        };
                        holder.PreviewMouseMove += (s, e) =>
                        {
                            if (!rightDragging) return;
                            var p = e.GetPosition(overlay);
                            var dx = p.X - rightDragStart.X;
                            var dy = p.Y - rightDragStart.Y;
                            var nx = Math.Max(0, rightStartX + dx);
                            var ny = Math.Max(0, rightStartY + dy);
                            Canvas.SetLeft(holder, nx);
                            Canvas.SetTop(holder, ny);
                        };
                        holder.PreviewMouseRightButtonUp += (s, e) =>
                        {
                            if (!rightDragging) return;
                            rightDragging = false;
                            holder.ReleaseMouseCapture();
                            t.X = Canvas.GetLeft(holder);
                            t.Y = Canvas.GetTop(holder);
                            SaveBillFormatLayout(layout);
                            onLayoutChanged?.Invoke();
                            e.Handled = true;
                        };

                        Point resizeStart = new Point();
                        double startW = 0;
                        double startH = 0;
                        bool resizing = false;
                        resizeGrip.MouseLeftButtonDown += (s, e) =>
                        {
                            resizing = true;
                            resizeStart = e.GetPosition(overlay);
                            startW = holder.Width;
                            startH = holder.Height;
                            resizeGrip.CaptureMouse();
                            e.Handled = true;
                        };
                        resizeGrip.MouseMove += (s, e) =>
                        {
                            if (!resizing) return;
                            var p = e.GetPosition(overlay);
                            var dx = p.X - resizeStart.X;
                            var dy = p.Y - resizeStart.Y;
                            holder.Width = Math.Max(40, startW + dx);
                            holder.Height = Math.Max(20, startH + dy);
                            if (tb is TextBox etb)
                            {
                                etb.Width = holder.Width;
                                etb.Height = Math.Max(20, holder.Height - 18);
                            }
                        };
                        resizeGrip.MouseLeftButtonUp += (s, e) =>
                        {
                            if (!resizing) return;
                            resizing = false;
                            resizeGrip.ReleaseMouseCapture();
                            t.Width = holder.Width;
                            t.Height = Math.Max(20, holder.Height - 18);
                            SaveBillFormatLayout(layout);
                            onLayoutChanged?.Invoke();
                            e.Handled = true;
                        };

                        overlay.Children.Add(holder);
                    }
                }
                root.Children.Add(overlay);
            }

            return page;
        }

        private List<BillEntry> GetBillEntriesForBillNo(string billNo)
        {
            var key = (billNo ?? string.Empty).Trim();
            var rows = new List<BillEntry>();
            if (key.Length == 0) return rows;
            try
            {
                using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"SELECT Id, Sr, BillNo, BillDate, Party, LRNo, LRDate, FromLoc, ToLoc, VehicleType, Freight, Detention, HML, OTHR, StCharge, RCVD, TDS, DED, MOP, MR, Remarks, Date
                                            FROM Bills
                                            WHERE TRIM(COALESCE(BillNo,'')) = @bill
                                            ORDER BY Sr, Id;";
                        cmd.Parameters.AddWithValue("@bill", key);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                rows.Add(new BillEntry
                                {
                                    Id = Convert.ToInt32(r["Id"]),
                                    Sr = Convert.ToInt32(r["Sr"]),
                                    BillNo = r["BillNo"] as string,
                                    BillDate = DateTime.TryParse(r["BillDate"] as string, out var bd) ? bd : DateTime.Today,
                                    Party = r["Party"] as string,
                                    LRNo = r["LRNo"] as string,
                                    LRDate = DateTime.TryParse(r["LRDate"] as string, out var ld) ? ld : (DateTime?)null,
                                    From = r["FromLoc"] as string,
                                    To = r["ToLoc"] as string,
                                    VehicleType = r["VehicleType"] as string,
                                    Freight = Convert.ToDecimal(r["Freight"]),
                                    Detention = Convert.ToDecimal(r["Detention"]),
                                    HML = Convert.ToDecimal(r["HML"]),
                                    OTHR = Convert.ToDecimal(r["OTHR"]),
                                    StCharge = Convert.ToDecimal(r["StCharge"]),
                                    RCVD = Convert.ToDecimal(r["RCVD"]),
                                    TDS = Convert.ToDecimal(r["TDS"]),
                                    DED = Convert.ToDecimal(r["DED"]),
                                    MOP = r["MOP"] as string,
                                    MR = r["MR"] as string,
                                    Remarks = r["Remarks"] as string,
                                    Date = DateTime.TryParse(r["Date"] as string, out var dt) ? dt : DateTime.Today
                                });
                            }
                        }
                    }
                }
            }
            catch { }
            return rows;
        }

        private static string NumberToWords(long number)
        {
            if (number == 0) return "Zero";
            if (number < 0) return "Minus " + NumberToWords(Math.Abs(number));
            string[] units = { "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
            string[] tens = { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };
            if (number < 20) return units[number];
            if (number < 100) return tens[number / 10] + (number % 10 > 0 ? " " + units[number % 10] : "");
            if (number < 1000) return units[number / 100] + " Hundred" + (number % 100 > 0 ? " " + NumberToWords(number % 100) : "");
            if (number < 100000) return NumberToWords(number / 1000) + " Thousand" + (number % 1000 > 0 ? " " + NumberToWords(number % 1000) : "");
            if (number < 10000000) return NumberToWords(number / 100000) + " Lakh" + (number % 100000 > 0 ? " " + NumberToWords(number % 100000) : "");
            return NumberToWords(number / 10000000) + " Crore" + (number % 10000000 > 0 ? " " + NumberToWords(number % 10000000) : "");
        }

        private static string BuildChargesLabelLines(decimal hamali, decimal detention, decimal other)
        {
            var hasParticulars = hamali != 0m || detention != 0m || other != 0m;
            if (!hasParticulars) return string.Empty;
            var lines = new List<string>();
            // Keep one blank line so charge labels align with Amount column lines after freight.
            lines.Add(string.Empty);
            if (hamali != 0m) lines.Add("Hamali");
            if (detention != 0m) lines.Add("Detention");
            if (other != 0m) lines.Add("Other");
            return string.Join("\n", lines);
        }

        private static string BuildAmountLines(decimal freight, decimal hamali, decimal detention, decimal other)
        {
            var lines = new List<string>();
            // First line maps to freight (From/To transport charge).
            if (freight != 0m) lines.Add(freight.ToString("0.##"));

            // Keep one visual gap before charge particulars so labels and amounts align.
            var hasParticulars = hamali != 0m || detention != 0m || other != 0m;
            if (hasParticulars)
            {
                lines.Add(string.Empty);
                if (hamali != 0m) lines.Add(hamali.ToString("0.##"));
                if (detention != 0m) lines.Add(detention.ToString("0.##"));
                if (other != 0m) lines.Add(other.ToString("0.##"));
            }
            return string.Join("\n", lines);
        }

        private static string BuildAmountParticularLines(decimal freight, decimal hamali, decimal detention, decimal other)
        {
            var lines = new List<string>();
            lines.Add("Freight");
            if (hamali != 0m) lines.Add("Hamali");
            if (detention != 0m) lines.Add("Detention");
            if (other != 0m) lines.Add("Other");
            return string.Join("\n", lines);
        }

        private List<BillPreviewLine> BuildBillPreviewLinesForGrid(List<BillEntry> billRows)
        {
            var output = new List<BillPreviewLine>();
            var rows = (billRows ?? new List<BillEntry>()).Where(x => x.Total != 0m).ToList();
            var totalStChargeForBill = rows.Sum(x => x.StCharge);
            foreach (var x in rows)
            {
                var lrNo = (x.LRNo ?? string.Empty).Trim();
                var lrDate = x.LRDate.HasValue ? x.LRDate.Value.ToString("dd.MM.yy") : string.Empty;
                var invoiceValues = SplitInvoiceValues(GetLRInvoice(x.LRNo));
                var vehicle = GetLRVehicleNo(x.LRNo);
                var from = (x.From ?? string.Empty).Trim();
                var to = (x.To ?? string.Empty).Trim();
                var weight = (x.VehicleType ?? string.Empty).Trim();
                var blockRows = new List<BillPreviewLine>();

                // Base freight line
                blockRows.Add(new BillPreviewLine
                {
                    LRNo = lrNo,
                    LRDate = lrDate,
                    Invoice = string.Empty,
                    Vehicle = vehicle,
                    From = from,
                    To = to,
                    ChargesBreakdown = string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to)
                        ? string.Empty
                        : ((from ?? string.Empty) + "        " + (to ?? string.Empty)).Trim(),
                    WeightOrType = weight,
                    Rate = string.Empty,
                    Amount = x.Freight != 0m ? x.Freight.ToString("0.##") : string.Empty
                });

                // Individual charge lines with amount in separate cells.
                if (x.HML != 0m)
                {
                    blockRows.Add(new BillPreviewLine
                    {
                        LRNo = string.Empty, LRDate = string.Empty, Invoice = string.Empty, Vehicle = string.Empty,
                        From = "Hamali", To = string.Empty,
                        ChargesBreakdown = "Hamali",
                        WeightOrType = string.Empty,
                        Rate = string.Empty,
                        Amount = x.HML.ToString("0.##")
                    });
                }
                if (x.Detention != 0m)
                {
                    blockRows.Add(new BillPreviewLine
                    {
                        LRNo = string.Empty, LRDate = string.Empty, Invoice = string.Empty, Vehicle = string.Empty,
                        From = "Detention", To = string.Empty,
                        ChargesBreakdown = "Detention",
                        WeightOrType = string.Empty,
                        Rate = string.Empty,
                        Amount = x.Detention.ToString("0.##")
                    });
                }
                if (x.OTHR != 0m)
                {
                    blockRows.Add(new BillPreviewLine
                    {
                        LRNo = string.Empty, LRDate = string.Empty, Invoice = string.Empty, Vehicle = string.Empty,
                        From = "Other", To = string.Empty,
                        ChargesBreakdown = "Other",
                        WeightOrType = string.Empty,
                        Rate = string.Empty,
                        Amount = x.OTHR.ToString("0.##")
                    });
                }

                // Fill invoice values with comma-separated packing in each invoice cell first.
                // Overflow to next rows only when invoices exceed one cell's readable capacity.
                var invoiceCellValues = PackInvoiceValuesForCells(invoiceValues);
                for (int invIndex = 0; invIndex < invoiceCellValues.Count; invIndex++)
                {
                    if (invIndex < blockRows.Count)
                    {
                        blockRows[invIndex].Invoice = invoiceCellValues[invIndex];
                    }
                    else
                    {
                        // Only if invoices exceed available block rows, append invoice-only rows.
                        blockRows.Add(new BillPreviewLine
                        {
                            LRNo = string.Empty,
                            LRDate = string.Empty,
                            Invoice = invoiceCellValues[invIndex],
                            Vehicle = string.Empty,
                            From = string.Empty,
                            To = string.Empty,
                            ChargesBreakdown = string.Empty,
                            WeightOrType = string.Empty,
                            Rate = string.Empty,
                            Amount = string.Empty
                        });
                    }
                }

                output.AddRange(blockRows);
            }

            // Bill-level St. Charge should appear once at the end as a summary row.
            if (totalStChargeForBill != 0m)
            {
                output.Add(new BillPreviewLine
                {
                    LRNo = string.Empty,
                    LRDate = string.Empty,
                    Invoice = string.Empty,
                    Vehicle = string.Empty,
                    From = "St. Charge",
                    To = string.Empty,
                    ChargesBreakdown = "St. Charge",
                    WeightOrType = string.Empty,
                    Rate = string.Empty,
                    Amount = totalStChargeForBill.ToString("0.##")
                });
            }
            return output;
        }

        private static List<string> PackInvoiceValuesForCells(List<string> invoiceValues)
        {
            var values = (invoiceValues ?? new List<string>())
                .Select(v => (v ?? string.Empty).Trim())
                .Where(v => v.Length > 0)
                .ToList();
            var packed = new List<string>();
            if (values.Count == 0) return packed;

            // Keep each invoice-cell line readable in current invoice column width.
            const int maxCharsPerCell = 24;
            var current = string.Empty;
            for (int i = 0; i < values.Count; i++)
            {
                var next = values[i];
                var candidate = string.IsNullOrEmpty(current) ? next : (current + ", " + next);
                if (candidate.Length <= maxCharsPerCell || string.IsNullOrEmpty(current))
                {
                    current = candidate;
                    continue;
                }

                packed.Add(current);
                current = next;
            }

            if (!string.IsNullOrEmpty(current))
                packed.Add(current);

            return packed;
        }

        private static void EnsureStChargeSummaryRow(List<BillPreviewLine> lines, decimal totalStChargeForBill)
        {
            if (lines == null) return;

            // Remove any previously generated St. Charge summary row(s)
            lines.RemoveAll(x =>
                string.Equals((x?.ChargesBreakdown ?? string.Empty).Trim(), "St. Charge", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(x?.LRNo) &&
                string.IsNullOrWhiteSpace(x?.Vehicle));

            if (totalStChargeForBill == 0m) return;

            lines.Add(new BillPreviewLine
            {
                LRNo = string.Empty,
                LRDate = string.Empty,
                Invoice = string.Empty,
                Vehicle = string.Empty,
                From = "St. Charge",
                To = string.Empty,
                ChargesBreakdown = "St. Charge",
                WeightOrType = string.Empty,
                Rate = string.Empty,
                Amount = totalStChargeForBill.ToString("0.##")
            });
        }

        private static void NormalizeInvoiceCellsInPreviewLines(List<BillPreviewLine> lines)
        {
            if (lines == null || lines.Count == 0) return;

            var i = 0;
            while (i < lines.Count)
            {
                var lrNo = ((lines[i]?.LRNo) ?? string.Empty).Trim();
                if (lrNo.Length == 0)
                {
                    i++;
                    continue;
                }

                var groupStart = i;
                var groupEnd = lines.Count - 1;
                for (int j = i + 1; j < lines.Count; j++)
                {
                    var nextLr = ((lines[j]?.LRNo) ?? string.Empty).Trim();
                    if (nextLr.Length > 0)
                    {
                        groupEnd = j - 1;
                        break;
                    }
                }

                var values = new List<string>();
                for (int j = groupStart; j <= groupEnd; j++)
                    values.AddRange(SplitInvoiceValues(lines[j]?.Invoice));

                var packed = PackInvoiceValuesForCells(values);
                for (int j = groupStart; j <= groupEnd; j++)
                    lines[j].Invoice = string.Empty;

                var capacity = Math.Max(0, groupEnd - groupStart + 1);
                for (int j = 0; j < Math.Min(capacity, packed.Count); j++)
                    lines[groupStart + j].Invoice = packed[j];

                if (packed.Count > capacity)
                {
                    var insertAt = groupEnd + 1;
                    for (int j = capacity; j < packed.Count; j++)
                    {
                        lines.Insert(insertAt, new BillPreviewLine
                        {
                            LRNo = string.Empty,
                            LRDate = string.Empty,
                            Invoice = packed[j],
                            Vehicle = string.Empty,
                            From = string.Empty,
                            To = string.Empty,
                            ChargesBreakdown = string.Empty,
                            WeightOrType = string.Empty,
                            Rate = string.Empty,
                            Amount = string.Empty
                        });
                        insertAt++;
                    }
                    groupEnd = insertAt - 1;
                }

                i = groupEnd + 1;
            }
        }


        private string GetLRInvoice(string lrNo)
        {
            var key = (lrNo ?? string.Empty).Trim();
            if (key.Length == 0) return string.Empty;
            try
            {
                using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Invoice FROM LREntries WHERE TRIM(COALESCE(LRNo,'')) = @lr LIMIT 1;";
                        cmd.Parameters.AddWithValue("@lr", key);
                        var obj = cmd.ExecuteScalar();
                        return (obj == null || obj == DBNull.Value) ? string.Empty : Convert.ToString(obj);
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FormatInvoiceForDisplay(string rawInvoice)
        {
            var raw = (rawInvoice ?? string.Empty).Trim();
            if (raw.Length == 0) return string.Empty;

            var normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = new List<string>();
            foreach (var line in normalized.Split('\n'))
            {
                var parts = line.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    var single = (line ?? string.Empty).Trim();
                    if (single.Length > 0) lines.Add(single);
                    continue;
                }
                for (int i = 0; i < parts.Length; i++)
                {
                    var val = (parts[i] ?? string.Empty).Trim();
                    if (val.Length > 0) lines.Add(val);
                }
            }

            return string.Join("\n", lines);
        }

        private static List<string> SplitInvoiceValues(string rawInvoice)
        {
            var raw = (rawInvoice ?? string.Empty).Trim();
            var result = new List<string>();
            if (raw.Length == 0) return result;
            var normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in normalized.Split('\n'))
            {
                var parts = line.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    var single = (line ?? string.Empty).Trim();
                    if (single.Length > 0) result.Add(single);
                    continue;
                }
                for (int i = 0; i < parts.Length; i++)
                {
                    var val = (parts[i] ?? string.Empty).Trim();
                    if (val.Length > 0) result.Add(val);
                }
            }
            return result;
        }

        private string GetLRVehicleNo(string lrNo)
        {
            var key = (lrNo ?? string.Empty).Trim();
            if (key.Length == 0) return string.Empty;
            try
            {
                using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT VehicleNo FROM LREntries WHERE TRIM(COALESCE(LRNo,'')) = @lr LIMIT 1;";
                        cmd.Parameters.AddWithValue("@lr", key);
                        var obj = cmd.ExecuteScalar();
                        return (obj == null || obj == DBNull.Value) ? string.Empty : Convert.ToString(obj);
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private sealed class LRFieldLayout
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double FontSize { get; set; }
            public bool Bold { get; set; }
        }

        private static string LRFormatLayoutPath =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Awagaman ERP", "lr_format_layout.txt");

        private static string LRFormatDefaultLayoutPath =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lr_format_layout.default.txt");

        private static Dictionary<string, LRFieldLayout> LoadLRFormatLayoutFromFile(string path)
        {
            var map = new Dictionary<string, LRFieldLayout>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return map;
                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    var parts = (line ?? string.Empty).Split('|');
                    if (parts.Length < 3) continue;
                    if (!double.TryParse(parts[1], out var x)) continue;
                    if (!double.TryParse(parts[2], out var y)) continue;
                    var fs = 14d;
                    var bold = true;
                    var w = 220d;
                    var h = 24d;
                    if (parts.Length >= 4) double.TryParse(parts[3], out fs);
                    if (parts.Length >= 5) bool.TryParse(parts[4], out bold);
                    if (parts.Length >= 6) double.TryParse(parts[5], out w);
                    if (parts.Length >= 7) double.TryParse(parts[6], out h);
                    var key = (parts[0] ?? string.Empty).Trim();
                    if (key.Length == 0) continue;
                    map[key] = new LRFieldLayout { X = x, Y = y, Width = w, Height = h, FontSize = fs, Bold = bold };
                }
            }
            catch { }
            return map;
        }

        private static Dictionary<string, LRFieldLayout> LoadLRFormatLayout()
        {
            // Priority: user-saved layout (LocalAppData) -> packaged default layout (app folder).
            var userMap = LoadLRFormatLayoutFromFile(LRFormatLayoutPath);
            if (userMap.Count > 0) return userMap;
            return LoadLRFormatLayoutFromFile(LRFormatDefaultLayoutPath);
        }

        private static void SaveLRFormatLayout(Dictionary<string, LRFieldLayout> map)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(LRFormatLayoutPath);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                var lines = new List<string>();
                foreach (var kv in map)
                {
                    lines.Add($"{kv.Key}|{kv.Value.X}|{kv.Value.Y}|{kv.Value.FontSize}|{kv.Value.Bold}|{kv.Value.Width}|{kv.Value.Height}");
                }
                System.IO.File.WriteAllLines(LRFormatLayoutPath, lines);
            }
            catch { }
        }

        private static string ResolveLRFormatTemplatePath()
        {
            var candidates = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LR format.png"),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\LR format.png")),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\LR format.png")),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\LR format.png"))
            };
            foreach (var p in candidates)
            {
                if (System.IO.File.Exists(p)) return p;
            }
            return string.Empty;
        }

        private static string ResolveBillFormatTemplatePath()
        {
            var candidates = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bill format.png"),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\bill format.png")),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\bill format.png")),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\bill format.png"))
            };
            foreach (var p in candidates)
            {
                if (System.IO.File.Exists(p)) return p;
            }
            return string.Empty;
        }
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (CBSLedgerView != null && CBSLedgerView.Visibility == Visibility.Visible)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
                {
                    e.Handled = true;
                    CBSAddRow_Click(sender, new RoutedEventArgs());
                    return;
                }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
                {
                    e.Handled = true;
                    TryCommitGridEdits(CBSGrid);
                    return;
                }
            }
        }
        private void ToggleSidebar_Click(object sender, RoutedEventArgs e) { }
        private void TrackingRefresh_Click(object sender, RoutedEventArgs e) { if (TrackingSearchBox != null) TrackingSearchBox.Text = string.Empty; if (StatusFilterCombo != null) StatusFilterCombo.SelectedIndex = 0; if (TrackingLedgerGrid != null) TrackingLedgerGrid.UnselectAllCells(); }
    }
}
