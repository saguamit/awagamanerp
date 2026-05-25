using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Awagaman_ERP.Data;
using Awagaman_ERP.Models;
using MahApps.Metro.Controls;

namespace Awagaman_ERP
{
    public partial class CommentPopupWindow : MetroWindow
    {
        private readonly int _challanId;
        private readonly string _challanNumber;
        private readonly CommentRepository _repo;
        private ObservableCollection<ChallanComment> _comments;

        public CommentPopupWindow(int challanId, string challanNumber)
        {
            InitializeComponent();
            _challanId = challanId;
            _challanNumber = challanNumber;
            _repo = new CommentRepository();
            Title = $"Comments - {challanNumber}";
            LoadComments();
        }

        private void LoadComments()
        {
            _comments = new ObservableCollection<ChallanComment>(_repo.GetByChallanId(_challanId));
            CommentList.ItemsSource = _comments;
        }

        private void AddComment_Click(object sender, RoutedEventArgs e)
        {
            var text = CommentTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Please enter a comment.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _repo.Add(new ChallanComment
            {
                ChallanId = _challanId,
                Comment = text,
                CreatedAt = DateTime.Now
            });
            CommentTextBox.Text = "";
            LoadComments();
        }

        private void DeleteComment_Click(object sender, RoutedEventArgs e)
        {
            var comment = (sender as FrameworkElement)?.Tag as ChallanComment;
            if (comment == null || comment.Id <= 0) return;
            var result = MessageBox.Show("Delete this comment?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            _repo.Delete(comment.Id);
            LoadComments();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
