using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Awagaman_ERP.Models
{
    public class CBSAccountEntry : INotifyPropertyChanged
    {
        private int _id;
        private int _sr;
        private string _accountName;
        private bool _isActive = true;

        public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public int Sr { get => _sr; set { _sr = value; OnPropertyChanged(); } }
        public string AccountName { get => _accountName; set { _accountName = value; OnPropertyChanged(); } }
        public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
