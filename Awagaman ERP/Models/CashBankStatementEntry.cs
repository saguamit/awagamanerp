using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Awagaman_ERP.Models
{
    public class CashBankStatementEntry : INotifyPropertyChanged
    {
        private int _id;
        private int _sr;
        private string _cbs;
        private DateTime _date = DateTime.Today;
        private string _accountName;
        private string _particulars;
        private string _remarks;
        private decimal _bankDr;
        private decimal _bankCr;
        private decimal _cashDr;
        private decimal _cashCr;

        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public int Sr { get => _sr; set { _sr = value; OnPropertyChanged(); } }
        public string CBS { get => _cbs; set { _cbs = value; OnPropertyChanged(); } }
        public DateTime Date
        {
            get => _date;
            set
            {
                _date = value;
                CBS = _date.ToString("MMM-yy");
                OnPropertyChanged();
            }
        }
        public string AccountName { get => _accountName; set { _accountName = value; OnPropertyChanged(); } }
        public string Particulars { get => _particulars; set { _particulars = value; OnPropertyChanged(); } }
        public string Remarks { get => _remarks; set { _remarks = value; OnPropertyChanged(); } }
        public decimal BankDr { get => _bankDr; set { _bankDr = value; OnPropertyChanged(); } }
        public decimal BankCr { get => _bankCr; set { _bankCr = value; OnPropertyChanged(); } }
        public decimal CashDr { get => _cashDr; set { _cashDr = value; OnPropertyChanged(); } }
        public decimal CashCr { get => _cashCr; set { _cashCr = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
