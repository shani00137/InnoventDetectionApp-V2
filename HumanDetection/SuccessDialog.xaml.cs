using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HumanDetection
{
    public partial class SuccessDialog : UserControl
    {
        public event EventHandler CloseClicked;

        private DispatcherTimer _timer;
        private int _seconds;

        public SuccessDialog()
        {
            InitializeComponent();
            StartTimer();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseClicked?.Invoke(this, EventArgs.Empty);
        }

        private void StartTimer()
        {
            _seconds = 0;
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _seconds++;
            int minutes = _seconds / 60;
            int secs = _seconds % 60;
            //TimerText.Text = $"{minutes:D2}:{secs:D2}";
        }

        public void StopTimer()
        {
            _timer?.Stop();
        }

        public void ResetTimer()
        {
            _seconds = 0;
            //TimerText.Text = "00:00";
        }

        /// <summary>
        /// Populates the summary shown when the process completes.
        /// Pass null/empty for any field that wasn't available.
        /// </summary>
        public void SetTiming(string capture, string boxCounting, string ocr, string total)
        {
            if (TimingCapture != null) TimingCapture.Text = string.IsNullOrEmpty(capture) ? "--" : capture;
            if (TimingBoxCounting != null) TimingBoxCounting.Text = string.IsNullOrEmpty(boxCounting) ? "--" : boxCounting;
            if (TimingOcr != null) TimingOcr.Text = string.IsNullOrEmpty(ocr) ? "--" : ocr;
            if (TimingTotal != null) TimingTotal.Text = string.IsNullOrEmpty(total) ? "--" : total;
        }
    }
}
