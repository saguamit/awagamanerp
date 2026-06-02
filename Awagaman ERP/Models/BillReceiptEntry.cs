using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Awagaman_ERP.Models
{
    public class BillReceiptEntry : INotifyPropertyChanged
    {
        private int _id;
        private string _billNo;
        private string _party;
        private decimal _billTotal;
        private DateTime? _billDate;
        private DateTime _receiptDate = DateTime.Today;
        private decimal _rcvd;
        private decimal _tds;
        private decimal _ded;
        private string _mop;
        private string _mr;
        private string _remarks;
        private decimal _dueAfter;
        private DateTime _createdAt = DateTime.Now;

        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public string BillNo { get => _billNo; set { _billNo = value; OnPropertyChanged(); } }
        public string Party { get => _party; set { _party = value; OnPropertyChanged(); } }
        public decimal BillTotal { get => _billTotal; set { _billTotal = value; OnPropertyChanged(); } }
        public DateTime? BillDate { get => _billDate; set { _billDate = value; OnPropertyChanged(); } }
        public DateTime ReceiptDate { get => _receiptDate; set { _receiptDate = value; OnPropertyChanged(); } }
        public decimal RCVD { get => _rcvd; set { _rcvd = value; OnPropertyChanged(); } }
        public decimal TDS { get => _tds; set { _tds = value; OnPropertyChanged(); } }
        public decimal DED { get => _ded; set { _ded = value; OnPropertyChanged(); } }
        public string MOP { get => _mop; set { _mop = value; OnPropertyChanged(); } }
        public string MR { get => _mr; set { _mr = value; OnPropertyChanged(); } }
        public string Remarks { get => _remarks; set { _remarks = value; OnPropertyChanged(); } }
        public decimal DueAfter { get => _dueAfter; set { _dueAfter = value; OnPropertyChanged(); } }
        public DateTime CreatedAt { get => _createdAt; set { _createdAt = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
