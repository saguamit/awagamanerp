using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Awagaman_ERP
{
    public class CustomDataGrid : DataGrid
    {
        private ScrollViewer _scrollViewer;

        protected override void OnInitialized(System.EventArgs e)
        {
            base.OnInitialized(e);
            _scrollViewer = this.FindVisualChild<ScrollViewer>();
        }

        protected override void OnSelectedCellsChanged(SelectedCellsChangedEventArgs e)
        {
            double hOffset = _scrollViewer?.HorizontalOffset ?? 0;
            base.OnSelectedCellsChanged(e);
            if (hOffset > 0 && _scrollViewer != null)
                _scrollViewer.ScrollToHorizontalOffset(hOffset);
        }
    }

    internal static class VisualTreeHelperExtensions
    {
        internal static T FindVisualChild<T>(this DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
