using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Awagaman_ERP.Models
{
    public class VehicleEntry : INotifyPropertyChanged
    {
        private int _id;
        private int _sr;
        private string _vehicleNumber;
        private string _ownerName;
        private string _panNumber;
        private string _engineNumber;
        private string _chassisNumber;
        private string _vehicleType;

        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public int Sr { get => _sr; set { _sr = value; OnPropertyChanged(); } }
        public string VehicleNumber { get => _vehicleNumber; set { _vehicleNumber = value; OnPropertyChanged(); } }
        public string OwnerName { get => _ownerName; set { _ownerName = value; OnPropertyChanged(); } }
        public string PANNumber { get => _panNumber; set { _panNumber = value; OnPropertyChanged(); } }
        public string EngineNumber { get => _engineNumber; set { _engineNumber = value; OnPropertyChanged(); } }
        public string ChassisNumber { get => _chassisNumber; set { _chassisNumber = value; OnPropertyChanged(); } }
        public string VehicleType { get => _vehicleType; set { _vehicleType = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

