using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Awagaman_ERP.Models
{
    public class TrackingEntry : INotifyPropertyChanged
    {
        private int _id;
        private int _sr;
        private string _challanNo;
        private DateTime _challanDate;
        private string _from;
        private string _to;
        private string _vehicleNo;
        private string _driverMobile;
        private DateTime? _ewayBillTillDate;
        private DateTime? _dispatchDate;
        private string _dispatchTime;
        private DateTime? _deliveredDate;
        private string _deliveredTime;
        private string _latestReport;

        public ObservableCollection<ReportingTrackEntry> ReportTracks { get; } = new ObservableCollection<ReportingTrackEntry>();

        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public int Sr { get => _sr; set { _sr = value; OnPropertyChanged(); } }
        public string ChallanNo { get => _challanNo; set { _challanNo = value; OnPropertyChanged(); } }
        public DateTime ChallanDate { get => _challanDate; set { _challanDate = value; OnPropertyChanged(); } }
        public string From { get => _from; set { _from = value; OnPropertyChanged(); } }
        public string To { get => _to; set { _to = value; OnPropertyChanged(); } }
        public string VehicleNo { get => _vehicleNo; set { _vehicleNo = value; OnPropertyChanged(); } }
        public string DriverMobile { get => _driverMobile; set { _driverMobile = value; OnPropertyChanged(); } }
        public DateTime? EwayBillTillDate { get => _ewayBillTillDate; set { _ewayBillTillDate = value; OnPropertyChanged(); } }
        public DateTime? DispatchDate { get => _dispatchDate; set { _dispatchDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(Status)); } }
        public string DispatchTime { get => _dispatchTime; set { _dispatchTime = value; OnPropertyChanged(); } }
        public DateTime? DeliveredDate { get => _deliveredDate; set { _deliveredDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(Status)); } }
        public string DeliveredTime { get => _deliveredTime; set { _deliveredTime = value; OnPropertyChanged(); } }

        public string Status
        {
            get
            {
                if (_deliveredDate.HasValue) return "Delivered";
                if (_dispatchDate.HasValue) return "In Transit";
                return "Pending Dispatch";
            }
        }

        public string LatestReport
        {
            get => _latestReport;
            set { _latestReport = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
