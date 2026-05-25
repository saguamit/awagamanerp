using System;
using System.Collections.ObjectModel;
using System.Windows;
using Awagaman_ERP.Data;
using Awagaman_ERP.Models;
using MahApps.Metro.Controls;

namespace Awagaman_ERP
{
    public partial class LRCommentPopupWindow : MetroWindow
    {
        private readonly int _lrEntryId;
        private readonly string _lrNo;
        private readonly CommentRepository _repo;
        private ObservableCollection<LRComment> _comments;

        public LRCommentPopupWindow(int lrEntryId, string lrNo)
        {
            InitializeComponent();
            _lrEntryId = lrEntryId;
            _lrNo = lrNo;
            _repo = new CommentRepository();
            Title = $"Comments - LR {lrNo}";
            LoadComments();
        }

        private void LoadComments()
        {
            _comments = new ObservableCollection<LRComment>(_repo.GetLRByEntryId(_lrEntryId));
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
            _repo.AddLR(new LRComment
            {
                LREntryId = _lrEntryId,
                Comment = text,
                CreatedAt = DateTime.Now
            });
            CommentTextBox.Text = "";
            LoadComments();
        }

        private void DeleteComment_Click(object sender, RoutedEventArgs e)
        {
            var comment = (sender as FrameworkElement)?.Tag as LRComment;
            if (comment == null || comment.Id <= 0) return;
            var result = MessageBox.Show("Delete this comment?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            _repo.DeleteLR(comment.Id);
            LoadComments();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
