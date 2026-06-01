using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Awagaman_ERP.Models
{
    public class LREntry : INotifyPropertyChanged
    {
        private int _id;
        private int _sr;
        private string _lrNo;
        private DateTime _date = DateTime.Today;
        private string _consignorName;
        private string _consignorAddress;
        private string _consignorGST;
        private string _consigneeName;
        private string _consigneeAddress;
        private string _consigneeGST;
        private string _from;
        private string _to;
        private string _vehicleNo;
        private string _vehicleType;
        private decimal _sizeL;
        private decimal _sizeW;
        private decimal _sizeH;
        private decimal _actualWeight;
        private decimal _chargedWeight;
        private int _pkg;
        private string _pkgType;
        private string _description;
        private string _invoice;
        private string _value;
        private string _chNo;
        private decimal _totalFreight;
        private decimal _hamali;
        private decimal _detention;
        private decimal _others;
        private decimal _stCharge;
        private decimal _tds;
        private decimal _ded;
        private decimal _neft;
        private decimal _cash;
        private string _billNo;
        private DateTime? _billDate;
        private decimal _billAmount;
        private string _billParty;
        private string _broker;
        private string _frtType;
        private string _payType;
        private decimal _comm;
        private string _paid;

        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public int Sr { get => _sr; set { _sr = value; OnPropertyChanged(); } }
        public string LRNo { get => _lrNo; set { _lrNo = value; OnPropertyChanged(); } }
        public DateTime Date { get => _date; set { _date = value; OnPropertyChanged(); } }
        
        public string ConsignorName { get => _consignorName; set { _consignorName = value; OnPropertyChanged(); } }
        public string ConsignorAddress { get => _consignorAddress; set { _consignorAddress = value; OnPropertyChanged(); } }
        public string ConsignorGST { get => _consignorGST; set { _consignorGST = value; OnPropertyChanged(); } }
        
        public string ConsigneeName { get => _consigneeName; set { _consigneeName = value; OnPropertyChanged(); } }
        public string ConsigneeAddress { get => _consigneeAddress; set { _consigneeAddress = value; OnPropertyChanged(); } }
        public string ConsigneeGST { get => _consigneeGST; set { _consigneeGST = value; OnPropertyChanged(); } }
        
        public string From { get => _from; set { _from = value; OnPropertyChanged(); } }
        public string To { get => _to; set { _to = value; OnPropertyChanged(); } }
        public string VehicleNo { get => _vehicleNo; set { _vehicleNo = value; OnPropertyChanged(); } }
        public string VehicleType { get => _vehicleType; set { _vehicleType = value; OnPropertyChanged(); } }
        public decimal SizeL { get => _sizeL; set { _sizeL = value; OnPropertyChanged(); } }
        public decimal SizeW { get => _sizeW; set { _sizeW = value; OnPropertyChanged(); } }
        public decimal SizeH { get => _sizeH; set { _sizeH = value; OnPropertyChanged(); } }
        public decimal ActualWeight { get => _actualWeight; set { _actualWeight = value; OnPropertyChanged(); } }
        public decimal ChargedWeight { get => _chargedWeight; set { _chargedWeight = value; OnPropertyChanged(); } }
        public int PKG { get => _pkg; set { _pkg = value; OnPropertyChanged(); } }
        public string PkgType { get => _pkgType; set { _pkgType = value; OnPropertyChanged(); } }
        public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
        public string Invoice { get => _invoice; set { _invoice = value; OnPropertyChanged(); } }
        public string Value { get => _value; set { _value = value; OnPropertyChanged(); } }
        public string CHNo { get => _chNo; set { _chNo = value; OnPropertyChanged(); } }
        
        public decimal TotalFreight 
        { 
            get => _totalFreight; 
            set 
            { 
                _totalFreight = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(TotalBill));
                OnPropertyChanged(nameof(Bal)); 
            } 
        }
        public decimal NEFT 
        { 
            get => _neft; 
            set 
            { 
                _neft = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(Bal)); 
            } 
        }
        public decimal Hamali { get => _hamali; set { _hamali = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalBill)); OnPropertyChanged(nameof(Bal)); } }
        public decimal Detention { get => _detention; set { _detention = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalBill)); OnPropertyChanged(nameof(Bal)); } }
        public decimal Others { get => _others; set { _others = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalBill)); OnPropertyChanged(nameof(Bal)); } }
        public decimal StCharge { get => _stCharge; set { _stCharge = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalBill)); OnPropertyChanged(nameof(Bal)); } }
        public decimal TDS { get => _tds; set { _tds = value; OnPropertyChanged(); OnPropertyChanged(nameof(Bal)); } }
        public decimal Ded { get => _ded; set { _ded = value; OnPropertyChanged(); OnPropertyChanged(nameof(Bal)); } }
        public decimal CASH 
        { 
            get => _cash; 
            set 
            { 
                _cash = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(Bal)); 
            } 
        }
        
        public decimal TotalBill => TotalFreight + Detention + Hamali + Others + StCharge;
        public decimal Bal => (NEFT + CASH) - TDS + Ded;
        
        public string BillNo { get => _billNo; set { _billNo = value; OnPropertyChanged(); } }
        public DateTime? BillDate { get => _billDate; set { _billDate = value; OnPropertyChanged(); } }
        public decimal BILL { get => _billAmount; set { _billAmount = value; OnPropertyChanged(); } }
        public string BillParty { get => _billParty; set { _billParty = value; OnPropertyChanged(); } }
        public string Broker { get => _broker; set { _broker = value; OnPropertyChanged(); } }
        public string FrtType { get => _frtType; set { _frtType = value; OnPropertyChanged(); } }
        public string PayType { get => _payType; set { _payType = value; OnPropertyChanged(); } }
        public decimal Comm { get => _comm; set { _comm = value; OnPropertyChanged(); } }
        public string Paid { get => _paid; set { _paid = value; OnPropertyChanged(); } }

        private bool _hasComments;
        [System.Xml.Serialization.XmlIgnore]
        public bool HasComments
        {
            get => _hasComments;
            set { _hasComments = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
