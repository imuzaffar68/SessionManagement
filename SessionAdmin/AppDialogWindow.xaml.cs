using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SessionAdmin
{
    public enum AppDialogType
    {
        Error,
        Info,
        Warning,
        Question
    }

    public partial class AppDialogWindow : Window
    {
        public AppDialogWindow(string title, string message, AppDialogType type, bool showCancel)
        {
            InitializeComponent();

            lblWindowTitle.Text = title;
            lblHeading.Text     = title;
            lblMessage.Text     = message;

            btnNo.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;

            switch (type)
            {
                case AppDialogType.Error:
                    lblIcon.Text              = "✗";
                    iconBorder.Background     = MakeGradient("#450A0A", "#DC2626");
                    btnYes.Content            = "OK";
                    btnYes.Style              = (Style)FindResource("BtnDanger");
                    break;

                case AppDialogType.Info:
                    lblIcon.Text              = "ℹ";
                    iconBorder.Background     = MakeGradient("#1E3A5F", "#4F8EF7");
                    btnYes.Content            = "OK";
                    btnYes.Style              = (Style)FindResource("BtnPrimary");
                    break;

                case AppDialogType.Warning:
                    lblIcon.Text              = "⚠";
                    iconBorder.Background     = MakeGradient("#451A03", "#D97706");
                    btnYes.Content            = "OK";
                    btnYes.Style              = (Style)FindResource("BtnAmber");
                    break;

                case AppDialogType.Question:
                    lblIcon.Text              = "?";
                    iconBorder.Background     = MakeGradient("#1E3A5F", "#4F8EF7");
                    btnYes.Content            = "Yes";
                    btnNo.Content             = "No";
                    btnYes.Style              = (Style)FindResource("BtnPrimary");
                    break;
            }
        }

        private static LinearGradientBrush MakeGradient(string colorStart, string colorEnd)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint   = new Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString(colorStart), 0));
            brush.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString(colorEnd), 1));
            return brush;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void btnWindowClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void btnYes_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void btnNo_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public static class AppDialog
    {
        private static Window GetOwner()
        {
            return Application.Current?.MainWindow;
        }

        public static void ShowError(string msg, string title = "Error")
        {
            var dlg = new AppDialogWindow(title, msg, AppDialogType.Error, false)
            {
                Owner = GetOwner()
            };
            dlg.ShowDialog();
        }

        public static void ShowInfo(string msg, string title = "Information")
        {
            var dlg = new AppDialogWindow(title, msg, AppDialogType.Info, false)
            {
                Owner = GetOwner()
            };
            dlg.ShowDialog();
        }

        public static void ShowWarning(string msg, string title = "Warning")
        {
            var dlg = new AppDialogWindow(title, msg, AppDialogType.Warning, false)
            {
                Owner = GetOwner()
            };
            dlg.ShowDialog();
        }

        public static bool Confirm(string msg, string title = "Confirm")
        {
            var dlg = new AppDialogWindow(title, msg, AppDialogType.Question, true)
            {
                Owner = GetOwner()
            };
            return dlg.ShowDialog() == true;
        }
    }
}
