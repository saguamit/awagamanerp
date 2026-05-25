using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Awagaman_ERP.Models
{
    public class BillEntry : INotifyPropertyChanged
    {
        private int _id;
        private int _sr;
        private string _billNo;
        private DateTime _billDate = DateTime.Today;
        private string _party;
        private string _lrNo;
        private DateTime? _lrDate;
        private string _from;
        private string _to;
        private string _vehicleType;
        private decimal _freight;
        private decimal _detention;
        private decimal _hml;
        private decimal _othr;
        private decimal _rcvd;
        private decimal _tds;
        private decimal _ded;
        private string _mop;
        private string _mr;
        private DateTime _date = DateTime.Today;
        private bool _hasComments;

        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public int Sr { get => _sr; set { _sr = value; OnPropertyChanged(); } }
        public string BillNo { get => _billNo; set { _billNo = value; OnPropertyChanged(); } }
        public DateTime BillDate { get => _billDate; set { _billDate = value; OnPropertyChanged(); } }
        public string Party { get => _party; set { _party = value; OnPropertyChanged(); } }
        public string LRNo { get => _lrNo; set { _lrNo = value; OnPropertyChanged(); } }
        public DateTime? LRDate { get => _lrDate; set { _lrDate = value; OnPropertyChanged(); } }
        public string From { get => _from; set { _from = value; OnPropertyChanged(); } }
        public string To { get => _to; set { _to = value; OnPropertyChanged(); } }
        public string VehicleType { get => _vehicleType; set { _vehicleType = value; OnPropertyChanged(); } }
        public decimal Freight { get => _freight; set { _freight = value; OnPropertyChanged(); OnPropertyChanged(nameof(Total)); OnPropertyChanged(nameof(Due)); } }
        public decimal Detention { get => _detention; set { _detention = value; OnPropertyChanged(); OnPropertyChanged(nameof(Total)); OnPropertyChanged(nameof(Due)); } }
        public decimal HML { get => _hml; set { _hml = value; OnPropertyChanged(); OnPropertyChanged(nameof(Total)); OnPropertyChanged(nameof(Due)); } }
        public decimal OTHR { get => _othr; set { _othr = value; OnPropertyChanged(); OnPropertyChanged(nameof(Total)); OnPropertyChanged(nameof(Due)); } }
        public decimal RCVD { get => _rcvd; set { _rcvd = value; OnPropertyChanged(); OnPropertyChanged(nameof(Due)); } }
        public decimal TDS { get => _tds; set { _tds = value; OnPropertyChanged(); OnPropertyChanged(nameof(Due)); } }
        public decimal DED { get => _ded; set { _ded = value; OnPropertyChanged(); OnPropertyChanged(nameof(Due)); } }
        public string MOP { get => _mop; set { _mop = value; OnPropertyChanged(); } }
        public string MR { get => _mr; set { _mr = value; OnPropertyChanged(); } }
        public DateTime Date { get => _date; set { _date = value; OnPropertyChanged(); } }
        public decimal Total => Freight + Detention + HML + OTHR;
        public decimal Due => Total - RCVD - TDS - DED;

        [System.Xml.Serialization.XmlIgnore]
        public bool HasComments { get => _hasComments; set { _hasComments = value; OnPropertyChanged(); } }

        private string _groupColor;
        private string _billNoDisplay;
        [System.Xml.Serialization.XmlIgnore]
        public string GroupColor { get => _groupColor; set { _groupColor = value; OnPropertyChanged(); } }
        [System.Xml.Serialization.XmlIgnore]
        public string BillNoDisplay { get => _billNoDisplay; set { _billNoDisplay = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
