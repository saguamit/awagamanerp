using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Awagaman_ERP.Models
{
    public class ChallanEntry : INotifyPropertyChanged
    {
        private int _id;
        private int _sr;
        private string _challanNumber;
        private DateTime _date = DateTime.Today;
        private string _lrNumber;
        private string _brokerName;
        private string _from;
        private string _to;
        private string _vehicleNumber;
        private string _vehicleType;
        private string _driverName;
        private string _driverMobile;
        private string _engineNo;
        private string _licenceNo;
        private string _policyNo;
        private string _chassisNo;
        private string _ownerName;
        private string _pan;
        private decimal _lorryHire;
        private decimal _lessTds;
        private decimal _advanceAmount;
        private decimal _advanceNeft;
        private decimal _advanceCash;
        private bool _isCalculatingAdvance;
        private bool _suppressCalculations;
        private DateTime? _advanceDate;
        private decimal _detention;
        private decimal _hamali;
        private decimal _deduction;
        private decimal _balancePaidNeft;
        private decimal _balancePaidCash;
        private DateTime? _balancePaidDate;
        private string _paidTo;
        private string _remarks;
        private decimal _billAmount;
        private decimal _margin;

        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public int Sr { get => _sr; set { _sr = value; OnPropertyChanged(); } }
        public string ChallanNumber { get => _challanNumber; set { _challanNumber = value; OnPropertyChanged(); } }
        public DateTime Date { get => _date; set { _date = value; OnPropertyChanged(); } }
        public string LRNumber 
        { 
            get => _lrNumber; 
            set 
            { 
                _lrNumber = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(FirstLRNumber)); 
                OnPropertyChanged(nameof(LRExtraCount)); 
                OnPropertyChanged(nameof(LRTooltipText)); 
                OnPropertyChanged(nameof(LRExtraText)); 
            } 
        }
        public string BrokerName { get => _brokerName; set { _brokerName = value; OnPropertyChanged(); } }
        public string From { get => _from; set { _from = value; OnPropertyChanged(); } }
        public string To { get => _to; set { _to = value; OnPropertyChanged(); } }
        public string VehicleNumber
        {
            get => _vehicleNumber;
            set
            {
                _vehicleNumber = string.IsNullOrWhiteSpace(value) ? value : value.ToUpperInvariant();
                OnPropertyChanged();
            }
        }
        public string VehicleType { get => _vehicleType; set { _vehicleType = value; OnPropertyChanged(); } }
        public string DriverName { get => _driverName; set { _driverName = value; OnPropertyChanged(); } }
        public string DriverMobile { get => _driverMobile; set { _driverMobile = value; OnPropertyChanged(); } }
        public string EngineNo { get => _engineNo; set { _engineNo = value; OnPropertyChanged(); } }
        public string LicenceNo { get => _licenceNo; set { _licenceNo = value; OnPropertyChanged(); } }
        public string PolicyNo { get => _policyNo; set { _policyNo = value; OnPropertyChanged(); } }
        public string ChassisNo { get => _chassisNo; set { _chassisNo = value; OnPropertyChanged(); } }
        public string OwnerName { get => _ownerName; set { _ownerName = value; OnPropertyChanged(); } }
        public string PAN { get => _pan; set { _pan = value; OnPropertyChanged(); } }

        public decimal LorryHire
        {
            get => _lorryHire;
            set
            {
                if (_lorryHire != value)
                {
                    _lorryHire = value;
                    OnPropertyChanged();
                    if (!_suppressCalculations)
                    {
                        ResetAdvanceBreakup();
                        RecalculateBalance();
                    }
                }
            }
        }

        public decimal LessTDS
        {
            get => _lessTds;
            set { _lessTds = value; OnPropertyChanged(); if (!_suppressCalculations) RecalculateBalance(); }
        }

        public decimal AdvanceAmount
        {
            get => _advanceAmount;
            set
            {
                if (_advanceAmount != value)
                {
                    _advanceAmount = value;
                    OnPropertyChanged();

                    if (!_suppressCalculations)
                    {
                        if (!_isCalculatingAdvance)
                        {
                            _isCalculatingAdvance = true;
                            AdvanceNEFT = 0;
                            AdvanceCash = 0;
                            _isCalculatingAdvance = false;
                        }
                        RecalculateBalance();
                    }
                }
            }
        }

        public decimal AdvanceNEFT
        {
            get => _advanceNeft;
            set
            {
                if (_advanceNeft != value)
                {
                    _advanceNeft = value;
                    OnPropertyChanged();

                    if (!_isCalculatingAdvance)
                    {
                        _isCalculatingAdvance = true;
                        if (_advanceAmount > 0)
                        {
                            AdvanceCash = _advanceAmount - _advanceNeft;
                        }
                        _isCalculatingAdvance = false;
                        RecalculateBalance();
                    }
                }
            }
        }

        public decimal AdvanceCash
        {
            get => _advanceCash;
            set
            {
                if (_advanceCash != value)
                {
                    _advanceCash = value;
                    OnPropertyChanged();

                    if (!_isCalculatingAdvance)
                    {
                        _isCalculatingAdvance = true;
                        if (_advanceAmount > 0)
                        {
                            AdvanceNEFT = _advanceAmount - _advanceCash;
                        }
                        _isCalculatingAdvance = false;
                        RecalculateBalance();
                    }
                }
            }
        }

        public DateTime? AdvanceDate { get => _advanceDate; set { _advanceDate = value; OnPropertyChanged(); } }

        [System.Xml.Serialization.XmlIgnore]
        public decimal Balance { get; set; }

        public decimal Detention { get => _detention; set { _detention = value; OnPropertyChanged(); if (!_suppressCalculations) RecalculateBalance(); } }
        public decimal Hamali { get => _hamali; set { _hamali = value; OnPropertyChanged(); if (!_suppressCalculations) RecalculateBalance(); } }
        public decimal Deduction
        {
            get => _deduction;
            set { _deduction = value; OnPropertyChanged(); if (!_suppressCalculations) RecalculateBalance(); }
        }

        public decimal BalancePaidNEFT
        {
            get => _balancePaidNeft;
            set { _balancePaidNeft = value; OnPropertyChanged(); OnPropertyChanged(nameof(BalancePaid)); if (!_suppressCalculations) RecalculateDue(); }
        }

        public decimal BalancePaidCash
        {
            get => _balancePaidCash;
            set { _balancePaidCash = value; OnPropertyChanged(); OnPropertyChanged(nameof(BalancePaid)); if (!_suppressCalculations) RecalculateDue(); }
        }

        [System.Xml.Serialization.XmlIgnore]
        public decimal BalancePaid => BalancePaidNEFT + BalancePaidCash;

        public DateTime? BalancePaidDate { get => _balancePaidDate; set { _balancePaidDate = value; OnPropertyChanged(); } }

        [System.Xml.Serialization.XmlIgnore]
        public decimal Due { get; set; }

        public string PaidTo { get => _paidTo; set { _paidTo = value; OnPropertyChanged(); } }
        public string Remarks { get => _remarks; set { _remarks = value; OnPropertyChanged(); } }
        public decimal BillAmount { get => _billAmount; set { _billAmount = value; OnPropertyChanged(); } }
        public decimal Margin { get => _margin; set { _margin = value; OnPropertyChanged(); } }

        [System.Xml.Serialization.XmlIgnore]
        public bool SuppressCalculations
        {
            get => _suppressCalculations;
            set => _suppressCalculations = value;
        }

        private bool _hasComments;
        [System.Xml.Serialization.XmlIgnore]
        public bool HasComments
        {
            get => _hasComments;
            set { _hasComments = value; OnPropertyChanged(); }
        }

        [System.Xml.Serialization.XmlIgnore]
        public string FirstLRNumber
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LRNumber)) return "";
                var parts = LRNumber.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? parts[0].Trim() : "";
            }
        }

        [System.Xml.Serialization.XmlIgnore]
        public string LRExtraCount
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LRNumber)) return "";
                var parts = LRNumber.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 1 ? $"(+{parts.Length - 1})" : "";
            }
        }

        [System.Xml.Serialization.XmlIgnore]
        public string LRTooltipText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LRNumber)) return "";
                var parts = LRNumber.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                return string.Join("\n", System.Linq.Enumerable.Select(parts, p => p.Trim()));
            }
        }

        [System.Xml.Serialization.XmlIgnore]
        public string LRExtraText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LRNumber)) return "";
                var parts = LRNumber.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 1) return "";
                return string.Join("\n", System.Linq.Enumerable.Select(System.Linq.Enumerable.Skip(parts, 1), p => p.Trim()));
            }
        }

        public void RecalculateBalance()
        {
            var effectiveHire = LorryHire - LessTDS;
            var adjustments = Detention + Hamali - Deduction;
            Balance = effectiveHire + adjustments - AdvanceAmount;
            OnPropertyChanged(nameof(Balance));
            RecalculateDue();
        }

        private void ResetAdvanceBreakup()
        {
            if (_advanceAmount == 0 && _advanceNeft == 0 && _advanceCash == 0)
            {
                return;
            }

            _isCalculatingAdvance = true;
            _advanceAmount = 0;
            _advanceNeft = 0;
            _advanceCash = 0;
            _isCalculatingAdvance = false;

            OnPropertyChanged(nameof(AdvanceAmount));
            OnPropertyChanged(nameof(AdvanceNEFT));
            OnPropertyChanged(nameof(AdvanceCash));
        }

        private void RecalculateDue()
        {
            Due = Balance - BalancePaid;
            OnPropertyChanged(nameof(Due));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
