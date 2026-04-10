using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SessionManagement.UI
{
    // ── Dialog type ───────────────────────────────────────────────
    public enum AppDialogType { Error, Info, Warning, Question }

    // ── Window ───────────────────────────────────────────────────
    internal sealed class AppDialogWindow : Window
    {
        private readonly TextBlock _lblIcon;
        private readonly Border    _iconBorder;
        private readonly Button    _btnYes;
        private readonly Button    _btnNo;

        public AppDialogWindow(string title, string message, AppDialogType type, bool showCancel)
        {
            WindowStyle           = WindowStyle.None;
            AllowsTransparency    = false;
            Width                 = 400;
            SizeToContent         = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode            = ResizeMode.NoResize;
            Background            = Hex("#0F1117");

            // ── outer border ──────────────────────────────────────
            var outer = new Border
            {
                Background      = Hex("#0F1117"),
                BorderBrush     = Hex("#2D3748"),
                BorderThickness = new Thickness(1)
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── title bar ─────────────────────────────────────────
            var titleBar = new Border { Background = Hex("#0D0F17"), Cursor = Cursors.SizeAll };
            titleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed) DragMove();
            };
            Grid.SetRow(titleBar, 0);

            var titleGrid = new Grid { Margin = new Thickness(12, 0, 0, 0) };
            titleGrid.Children.Add(new TextBlock
            {
                Text              = title,
                FontSize          = 12,
                FontWeight        = FontWeights.SemiBold,
                Foreground        = Hex("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center
            });

            var closeBtn = new Button
            {
                Content             = "✕",
                Width               = 32,
                Height              = 28,
                Background          = Brushes.Transparent,
                Foreground          = Hex("#475569"),
                BorderThickness     = new Thickness(0),
                FontSize            = 14,
                Cursor              = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center,
                Template            = MakeButtonTemplate()
            };
            closeBtn.Click += (_, __) => { DialogResult = false; Close(); };
            titleGrid.Children.Add(closeBtn);
            titleBar.Child = titleGrid;

            // ── content area ──────────────────────────────────────
            var content = new Border { Background = Hex("#171B26") };
            Grid.SetRow(content, 1);

            var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };

            // icon + heading / message
            var topRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 20)
            };

            _iconBorder = new Border
            {
                Width             = 44,
                Height            = 44,
                CornerRadius      = new CornerRadius(12),
                Margin            = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            _lblIcon = new TextBlock
            {
                FontSize            = 22,
                Foreground          = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            _iconBorder.Child = _lblIcon;

            var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textCol.Children.Add(new TextBlock
            {
                Text       = title,
                FontSize   = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Hex("#F1F5F9"),
                Margin     = new Thickness(0, 0, 0, 6)
            });
            textCol.Children.Add(new TextBlock
            {
                Text         = message,
                FontSize     = 12,
                Foreground   = Hex("#94A3B8"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth     = 290
            });

            topRow.Children.Add(_iconBorder);
            topRow.Children.Add(textCol);

            // buttons
            var btnRow = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            _btnNo = new Button
            {
                Content         = "No",
                Height          = 36,
                Width           = 90,
                Margin          = new Thickness(0, 0, 8, 0),
                Background      = Hex("#1E2433"),
                Foreground      = Hex("#94A3B8"),
                BorderBrush     = Hex("#2D3748"),
                BorderThickness = new Thickness(1),
                FontFamily      = new FontFamily("Segoe UI"),
                FontSize        = 13,
                Cursor          = Cursors.Hand,
                Visibility      = showCancel ? Visibility.Visible : Visibility.Collapsed,
                Template        = MakeButtonTemplate()
            };
            _btnNo.Click += (_, __) => { DialogResult = false; Close(); };

            _btnYes = new Button
            {
                Content         = "OK",
                Height          = 36,
                Width           = 90,
                BorderThickness = new Thickness(0),
                Foreground      = Brushes.White,
                FontFamily      = new FontFamily("Segoe UI"),
                FontSize        = 13,
                FontWeight      = FontWeights.SemiBold,
                Cursor          = Cursors.Hand,
                Template        = MakeButtonTemplate()
            };
            _btnYes.Click += (_, __) => { DialogResult = true; Close(); };

            btnRow.Children.Add(_btnNo);
            btnRow.Children.Add(_btnYes);

            stack.Children.Add(topRow);
            stack.Children.Add(btnRow);
            content.Child = stack;

            root.Children.Add(titleBar);
            root.Children.Add(content);
            outer.Child = root;
            Content = outer;

            ApplyType(type);
        }

        // ── type-specific colours ─────────────────────────────────
        private void ApplyType(AppDialogType type)
        {
            switch (type)
            {
                case AppDialogType.Error:
                    _lblIcon.Text          = "✗";
                    _iconBorder.Background = Gradient("#450A0A", "#DC2626");
                    _btnYes.Background     = Gradient("#7F1D1D", "#DC2626");
                    _btnYes.Content        = "OK";
                    break;

                case AppDialogType.Info:
                    _lblIcon.Text          = "ℹ";
                    _iconBorder.Background = Gradient("#1E3A5F", "#4F8EF7");
                    _btnYes.Background     = Gradient("#1E3A5F", "#4F8EF7");
                    _btnYes.Content        = "OK";
                    break;

                case AppDialogType.Warning:
                    _lblIcon.Text          = "⚠";
                    _iconBorder.Background = Gradient("#451A03", "#D97706");
                    _btnYes.Background     = Gradient("#78350F", "#D97706");
                    _btnYes.Content        = "OK";
                    break;

                case AppDialogType.Question:
                    _lblIcon.Text          = "?";
                    _iconBorder.Background = Gradient("#1E3A5F", "#4F8EF7");
                    _btnYes.Background     = Gradient("#1E3A5F", "#4F8EF7");
                    _btnYes.Content        = "Yes";
                    _btnNo.Content         = "No";
                    break;
            }
        }

        // ── helpers ───────────────────────────────────────────────
        private static SolidColorBrush Hex(string hex)
            => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        private static LinearGradientBrush Gradient(string from, string to)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(from), 0));
            b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(to),   1));
            return b;
        }

        // Rounded-corner button template (replaces aero chrome).
        private static ControlTemplate MakeButtonTemplate()
        {
            var t = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetBinding(Border.BackgroundProperty,
                new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                              Path = new PropertyPath(BackgroundProperty) });
            border.SetBinding(Border.BorderBrushProperty,
                new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                              Path = new PropertyPath(BorderBrushProperty) });
            border.SetBinding(Border.BorderThicknessProperty,
                new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                              Path = new PropertyPath(BorderThicknessProperty) });

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
            border.AppendChild(cp);

            t.VisualTree = border;
            return t;
        }
    }

    // ── Public static API ─────────────────────────────────────────
    public static class AppDialog
    {
        // Resolves owner: explicit > active window > MainWindow.
        private static Window GetOwner(Window explicitOwner)
        {
            if (explicitOwner != null) return explicitOwner;
            if (Application.Current == null) return null;
            foreach (Window w in Application.Current.Windows)
                if (w.IsActive) return w;
            return Application.Current.MainWindow;
        }

        public static void ShowError(string msg, string title = "Error", Window owner = null)
        {
            var dlg = new AppDialogWindow(title, msg, AppDialogType.Error, false)
                      { Owner = GetOwner(owner) };
            dlg.ShowDialog();
        }

        public static void ShowInfo(string msg, string title = "Information", Window owner = null)
        {
            var dlg = new AppDialogWindow(title, msg, AppDialogType.Info, false)
                      { Owner = GetOwner(owner) };
            dlg.ShowDialog();
        }

        public static void ShowWarning(string msg, string title = "Warning", Window owner = null)
        {
            var dlg = new AppDialogWindow(title, msg, AppDialogType.Warning, false)
                      { Owner = GetOwner(owner) };
            dlg.ShowDialog();
        }

        public static bool Confirm(string msg, string title = "Confirm", Window owner = null)
        {
            var dlg = new AppDialogWindow(title, msg, AppDialogType.Question, true)
                      { Owner = GetOwner(owner) };
            return dlg.ShowDialog() == true;
        }
    }
}
