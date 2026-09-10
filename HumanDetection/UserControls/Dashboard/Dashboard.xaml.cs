using HumanDetection.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HumanDetection
{
    /// <summary>
    /// Landing dashboard shown right after the splash screen. Displays system
    /// readiness (AI models / weight / Moxa / OCR) and quick navigation.
    /// </summary>
    public partial class Dashboard : Page
    {
        private readonly DispatcherTimer _clock = new DispatcherTimer();

        public Dashboard()
        {
            InitializeComponent();
            _clock.Interval = TimeSpan.FromSeconds(1);
            _clock.Tick += (s, e) =>
            {
                if (ClockTxt != null)
                    ClockTxt.Text = DateTime.Now.ToString("dd MMM yyyy · HH:mm:ss");
            };
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _clock.Start();
            RefreshStatus();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _clock.Stop();
        }

        private void RefreshStatus()
        {
            var health = SystemHealth.Current;

            // AI models
            ModelsStatus.Text = health.ModelsLoaded ? "Ready" : "Not Loaded";
            ModelsStatus.Foreground = Brush(health.ModelsLoaded ? "#787AFF" : "#FF5252");

            // Weight
            WeightStatus.Text = health.WeightOk ? "Connected" : "Disconnected";
            WeightStatus.Foreground = Brush(health.WeightOk ? "#787AFF" : "#FF5252");

            // Moxa
            MoxaStatus.Text = health.MoxaOk ? "Connected" : "Disconnected";
            MoxaStatus.Foreground = Brush(health.MoxaOk ? "#787AFF" : "#FF5252");

            // OCR
            OcrStatus.Text = health.OcrOk ? "Running" : "Stopped";
            OcrStatus.Foreground = Brush(health.OcrOk ? "#787AFF" : "#FF5252");

            // Banner
            bool ready = health.IsFullyReady;
            ReadyTitle.Text = ready ? "System Ready" : $"{health.ReadyCount}/{health.TotalCount} Components Online";
            ReadyTitle.Foreground = Brush(ready ? "#787AFF" : "#FBBF24");
            ReadyIcon.Kind = ready ? Material.Icons.MaterialIconKind.CheckCircleOutline
                                   : Material.Icons.MaterialIconKind.AlertCircleOutline;
            ReadyIcon.Foreground = Brush(ready ? "#787AFF" : "#FBBF24");

            GreetingTxt.Text = $"System overview · {DateTime.Now:dddd, dd MMM yyyy}";
        }

        private static Brush Brush(string hex) =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        private MainWindow GetMainWindow() => Window.GetWindow(this) as MainWindow;

        private void StartDetection_Click(object sender, RoutedEventArgs e)
            => GetMainWindow()?.NavigateToHome();

        private void GoHome_Click(object sender, RoutedEventArgs e)
            => GetMainWindow()?.NavigateToHome();

        private void GoReports_Click(object sender, RoutedEventArgs e)
            => GetMainWindow()?.NavigateToReports();

        private void GoSettings_Click(object sender, RoutedEventArgs e)
            => GetMainWindow()?.NavigateToSettings();

        private void GoMoxa_Click(object sender, RoutedEventArgs e)
            => GetMainWindow()?.NavigateToMoxaStatus();
    }
}