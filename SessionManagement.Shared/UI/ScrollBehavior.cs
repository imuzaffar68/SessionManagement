using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SessionManagement.UI
{
    /// <summary>
    /// Attached property that enables Shift+MouseWheel horizontal scrolling on any
    /// ItemsControl (DataGrid, ListView, ListBox).  WPF does not support horizontal
    /// mouse-wheel scroll natively; this behavior intercepts the event and forwards
    /// it to the control's internal ScrollViewer.
    /// </summary>
    public static class ScrollBehavior
    {
        public static readonly DependencyProperty EnableHorizontalScrollProperty =
            DependencyProperty.RegisterAttached(
                "EnableHorizontalScroll",
                typeof(bool),
                typeof(ScrollBehavior),
                new PropertyMetadata(false, OnEnableChanged));

        public static bool GetEnableHorizontalScroll(DependencyObject obj)
            => (bool)obj.GetValue(EnableHorizontalScrollProperty);

        public static void SetEnableHorizontalScroll(DependencyObject obj, bool value)
            => obj.SetValue(EnableHorizontalScrollProperty, value);

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is UIElement element)) return;

            if ((bool)e.NewValue)
                element.PreviewMouseWheel += OnPreviewMouseWheel;
            else
                element.PreviewMouseWheel -= OnPreviewMouseWheel;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
                return;

            var sv = FindScrollViewer(sender as DependencyObject);
            if (sv == null) return;

            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv) return sv;
                var found = FindScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
