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
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var lrEntries = new LRRepository().GetAll();
                foreach (var lr in lrEntries)
                {
                    if (!string.IsNullOrWhiteSpace(lr.ConsignorName) &&
                        names.Add(lr.ConsignorName.Trim()) &&
                        _repo.FindByName(lr.ConsignorName) == null)
                    {
                        var all = _repo.GetAll();
                        _repo.Upsert(new PartyEntry
                        {
                            Sr = all.Count + 1,
                            PartyName = lr.ConsignorName.Trim(),
                            Address = (lr.ConsignorAddress ?? string.Empty).Trim(),
                            GSTNo = (lr.ConsignorGST ?? string.Empty).Trim()
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(lr.ConsigneeName) &&
                        names.Add(lr.ConsigneeName.Trim()) &&
                        _repo.FindByName(lr.ConsigneeName) == null)
                    {
                        var all = _repo.GetAll();
                        _repo.Upsert(new PartyEntry
                        {
                            Sr = all.Count + 1,
                            PartyName = lr.ConsigneeName.Trim(),
                            Address = (lr.ConsigneeAddress ?? string.Empty).Trim(),
                            GSTNo = (lr.ConsigneeGST ?? string.Empty).Trim()
                        });
                    }
                }
            }
            catch (System.Exception ex)
            {
                AppLogger.LogException(nameof(PopulateFromLR), ex);
            }
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
