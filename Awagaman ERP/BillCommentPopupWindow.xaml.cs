using System;
using System.Collections.ObjectModel;
using System.Windows;
using Awagaman_ERP.Data;
using Awagaman_ERP.Models;
using MahApps.Metro.Controls;

namespace Awagaman_ERP
{
    public partial class BillCommentPopupWindow : MetroWindow
    {
        private readonly int _billId;
        private readonly CommentRepository _repo;
        private ObservableCollection<BillComment> _comments;

        public BillCommentPopupWindow(int billId, string title)
        {
            InitializeComponent();
            _billId = billId;
            _repo = new CommentRepository();
            Title = $"Comments - {title}";
            LoadComments();
        }

        private void LoadComments()
        {
            _comments = new ObservableCollection<BillComment>(_repo.GetBillByBillId(_billId));
            CommentList.ItemsSource = _comments;
        }

        private void AddComment_Click(object sender, RoutedEventArgs e)
        {
            var text = CommentTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) { MessageBox.Show("Please enter a comment.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _repo.AddBill(new BillComment { BillId = _billId, Comment = text, CreatedAt = DateTime.Now });
            CommentTextBox.Text = "";
            LoadComments();
        }

        private void DeleteComment_Click(object sender, RoutedEventArgs e)
        {
            var comment = (sender as FrameworkElement)?.Tag as BillComment;
            if (comment == null || comment.Id <= 0) return;
            if (MessageBox.Show("Delete this comment?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _repo.DeleteBill(comment.Id);
            LoadComments();
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
