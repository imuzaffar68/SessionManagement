using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SessionAdmin
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // When any DatePicker loads, wire up its CalendarOpened event so we can
            // push the dark styles onto the popup Calendar the moment it appears.
            EventManager.RegisterClassHandler(
                typeof(DatePicker),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnDatePickerLoaded));
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
