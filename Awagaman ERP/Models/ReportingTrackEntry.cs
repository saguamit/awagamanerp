using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Awagaman_ERP.Models
{
    public class ReportingTrackEntry : INotifyPropertyChanged
    {
        private int _id;
        private int _trackingEntryId;
        private DateTime _reportDateTime;
        private string _remarks;

        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public int TrackingEntryId { get => _trackingEntryId; set { _trackingEntryId = value; OnPropertyChanged(); } }
        public DateTime ReportDateTime { get => _reportDateTime; set { _reportDateTime = value; OnPropertyChanged(); } }
        public string Remarks { get => _remarks; set { _remarks = value; OnPropertyChanged(); } }

        public string DisplayText => $"{ReportDateTime:dd-MMM-yyyy HH:mm} - {Remarks}";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
