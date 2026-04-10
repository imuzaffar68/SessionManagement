using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SessionAdmin
{
    public partial class App : Application
    {
        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Dark calendar pop-up styling for every DatePicker in the app.
            EventManager.RegisterClassHandler(
                typeof(DatePicker),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnDatePickerLoaded));

            // Show splash; it connects to WCF then opens MainWindow.
            var splash = new SplashWindow();
            MainWindow = splash;
            splash.Show();
        }

        private void OnDatePickerLoaded(object sender, RoutedEventArgs e)
        {
            var dp = sender as DatePicker;
            if (dp == null) return;
            dp.CalendarOpened += OnCalendarOpened;
        }

        private void OnCalendarOpened(object sender, RoutedEventArgs e)
        {
            var dp = sender as DatePicker;
            if (dp?.Template == null) return;

            var popup = dp.Template.FindName("PART_Popup", dp) as Popup;
            var cal = popup?.Child as Calendar;
            if (cal == null) return;

            var dayStyle = TryFindResource("DarkCalDayBtn") as Style;
            var btnStyle = TryFindResource("DarkCalBtn") as Style;

            if (dayStyle != null) cal.CalendarDayButtonStyle = dayStyle;
            if (btnStyle != null) cal.CalendarButtonStyle = btnStyle;
        }
    }
}
