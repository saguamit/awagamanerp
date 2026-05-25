using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Awagaman_ERP.Data;
using Awagaman_ERP.Models;
using MahApps.Metro.Controls;

namespace Awagaman_ERP
{
    public partial class PartyLedgerWindow : MetroWindow
    {
        private readonly PartyRepository _repo = new PartyRepository();
        private List<PartyEntry> _allParties;

        public PartyLedgerWindow()
        {
            InitializeComponent();
            PopulateFromLR();
            LoadParties();
        }

        private void PopulateFromLR()
        {
            try
            {
                using (var conn = new System.Data.SQLite.SQLiteConnection(Awagaman_ERP.Data.AppDatabase.ConnectionString))
                {
                    conn.Open();
                    var names = new HashSet<string>();
                    // Import Consignors
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT DISTINCT ConsignorName, ConsignorAddress, ConsignorGST FROM LREntries WHERE ConsignorName IS NOT NULL AND ConsignorName != ''";
                        using (var r = cmd.ExecuteReader())
                            while (r.Read())
                            {
                                var name = r["ConsignorName"] as string;
                                if (string.IsNullOrWhiteSpace(name) || !names.Add(name.ToLower()) || _repo.FindByName(name) != null) continue;
                                var all = _repo.GetAll();
                                _repo.Upsert(new PartyEntry { Sr = all.Count + 1, PartyName = name.Trim(), Address = (r["ConsignorAddress"] as string)?.Trim() ?? "", GSTNo = (r["ConsignorGST"] as string)?.Trim() ?? "" });
                            }
                    }
                    // Import Consignees
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT DISTINCT ConsigneeName, ConsigneeAddress, ConsigneeGST FROM LREntries WHERE ConsigneeName IS NOT NULL AND ConsigneeName != ''";
                        using (var r = cmd.ExecuteReader())
                            while (r.Read())
                            {
                                var name = r["ConsigneeName"] as string;
                                if (string.IsNullOrWhiteSpace(name) || !names.Add(name.ToLower()) || _repo.FindByName(name) != null) continue;
                                var all = _repo.GetAll();
                                _repo.Upsert(new PartyEntry { Sr = all.Count + 1, PartyName = name.Trim(), Address = (r["ConsigneeAddress"] as string)?.Trim() ?? "", GSTNo = (r["ConsigneeGST"] as string)?.Trim() ?? "" });
                            }
                    }
                }
            }
            catch { }
        }

        private void LoadParties()
        {
            _allParties = _repo.GetAll();
            DataContext = null;
            DataContext = _allParties;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = SearchBox.Text?.Trim().ToLower() ?? "";
            if (string.IsNullOrEmpty(filter))
                DataContext = _allParties;
            else
                DataContext = _allParties.Where(p =>
                    (p.PartyName?.ToLower().Contains(filter) == true) ||
                    (p.Address?.ToLower().Contains(filter) == true) ||
                    (p.GSTNo?.ToLower().Contains(filter) == true)).ToList();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
