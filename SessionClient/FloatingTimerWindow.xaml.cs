using System;
using System.Windows;
using System.Windows.Input;

namespace SessionClient
{
    public partial class FloatingTimerWindow : Window
    {
        public event EventHandler RestoreRequested;

        public FloatingTimerWindow()
        {
            InitializeComponent();
            MouseLeftButtonDown += FloatingTimerWindow_MouseLeftButtonDown;
            btnRestore.Click += BtnRestore_Click;
        }

        public void SetTime(string timeText)
        {
            txtTime.Text = timeText;
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            RestoreRequested?.Invoke(this, EventArgs.Empty);
        }

        private void FloatingTimerWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
