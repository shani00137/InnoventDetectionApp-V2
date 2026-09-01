using ACGPUIO;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace UserControls.MoxaStatus
{
    /// <summary>
    /// Test interface for the Moxa E2210 I/O module via the AccessController.
    /// Lets you manually read DI inputs, write DO outputs, and poll a DI channel
    /// while watching the AccessController events bubble up live.
    /// </summary>
    public partial class MoxaStatus : System.Windows.Controls.Page
    {
        private AccessController _ac;
        private readonly ObservableCollection<DiItem> _diItems = new();

        public MoxaStatus()
        {
            InitializeComponent();
            Loaded += MoxaStatus_Loaded;
            Unloaded += MoxaStatus_Unloaded;
        }

        private void MoxaStatus_Loaded(object sender, RoutedEventArgs e)
        {
            DiQuickList.ItemsSource = _diItems;
            for (int i = 0; i < 8; i++)
                _diItems.Add(new DiItem { Label = $"DI{i}" });
        }

        private void MoxaStatus_Unloaded(object sender, RoutedEventArgs e)
        {
            StopDiPolling();
            _ac?.Dispose();
            _ac = null;
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            });
        }

        private AccessController GetController()
        {
            if (_ac == null)
            {
                var ip = string.IsNullOrWhiteSpace(MoxaIpTxt.Text) ? "192.168.1.135" : MoxaIpTxt.Text.Trim();
                _ac = new AccessController(ip);
                _ac.Log = AppendLog;
            }
            return _ac;
        }

        private void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ac = GetController();
                // A successful DO write or DI read proves the connection works.
                ConnectionChip.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#0F3D2E");
                ConnectionTxt.Text = "Configured";
                AppendLog("Controller configured — run a DI read or DO write to verify connectivity.");
            }
            catch (Exception ex)
            {
                ConnectionChip.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#3D1F1F");
                ConnectionTxt.Text = "Error";
                AppendLog($"Connect error: {ex.Message}");
            }
        }

        private async void TestBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ac = GetController();
                var result = await ac.ReadDIAsync(0);
                if (result.HasValue)
                {
                    ConnectionChip.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#0F3D2E");
                    ConnectionTxt.Text = "Connected";
                    AppendLog($"Test OK — DI0 = {(result.Value ? "ON" : "OFF")}");
                }
                else
                {
                    ConnectionChip.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#3D1F1F");
                    ConnectionTxt.Text = "Failed";
                    AppendLog("Test failed — no valid response from device.");
                }
            }
            catch (Exception ex)
            {
                ConnectionChip.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#3D1F1F");
                ConnectionTxt.Text = "Error";
                AppendLog($"Test error: {ex.Message}");
            }
        }

        private async void DoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is System.Windows.Controls.Button btn) || string.IsNullOrWhiteSpace(btn.Tag?.ToString()))
                return;

            var parts = btn.Tag.ToString().Split(':');
            int channel = int.Parse(parts[0]);
            int status = int.Parse(parts[1]);

            bool ok = await GetController().SendDOCommand(channel, status);
            AppendLog(ok
                ? $"DO{channel} → {(status == 1 ? "ON" : "OFF")}  ✓"
                : $"DO{channel} command FAILED");
        }

        private async void ReadDiBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(DiReadChannelTxt.Text, out int channel) || channel < 0 || channel > 15)
            {
                AppendLog("Invalid DI channel (0-15).");
                return;
            }

            var result = await GetController().ReadDIAsync(channel);
            if (result.HasValue)
            {
                DiReadResultTxt.Text = result.Value ? "ON" : "OFF";
                DiReadResultTxt.Foreground =
                    (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom(
                        result.Value ? "#4ADE80" : "#9CA3AF");
            }
            else
            {
                DiReadResultTxt.Text = "READ FAIL";
                DiReadResultTxt.Foreground =
                    (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#F87171");
            }
        }

        private async void SendDoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(DoWriteChannelTxt.Text, out int channel) || channel < 0 || channel > 15)
            {
                AppendLog("Invalid DO channel (0-15).");
                return;
            }

            int status = DoWriteStatusCmb.SelectedIndex == 1 ? 1 : 0;
            bool ok = await GetController().SendDOCommand(channel, status);
            DoWriteResultTxt.Text = ok ? $"DO{channel} = {(status == 1 ? "ON" : "OFF")} sent ✓" : $"DO{channel} send FAILED";
            DoWriteResultTxt.Foreground =
                (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom(
                    ok ? "#4ADE80" : "#F87171");
        }

        private void StartDICh_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(DIPollChannelTxt.Text, out int channel) || channel < 0 || channel > 15)
            {
                AppendLog("Invalid DI polling channel (0-15).");
                return;
            }
            if (!int.TryParse(DIPollIntervalTxt.Text, out int intervalMs) || intervalMs < 50)
            {
                AppendLog("Invalid poll interval (min 50 ms).");
                return;
            }

            var ac = GetController();
            ac.DIPollingIntervalMs = intervalMs;
            ac.DIChanged += OnDiChanged;
            _ = ac.StartDIPollingAsync(channel);
            _ = MarkPolledChannelBusy(channel);
            AppendLog($"DI polling started on channel {channel} every {intervalMs} ms.");
        }

        private Task MarkPolledChannelBusy(int channel)
        {
            if (channel >= 0 && channel < _diItems.Count)
            {
                _diItems[channel].Label = $"DI{channel} (polling)";
                _diItems[channel].State = "…";
            }
            return Task.CompletedTask;
        }

        private void StopDICh_Click(object sender, RoutedEventArgs e)
        {
            StopDiPolling();
        }

        private void StopDiPolling()
        {
            _ac?.StopDIPolling();
            for (int i = 0; i < _diItems.Count; i++)
                _diItems[i].Label = $"DI{i}";
            AppendLog("DI polling stopped.");
        }

        private void OnDiChanged(object? sender, DIChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.Channel >= 0 && e.Channel < _diItems.Count)
                {
                    var item = _diItems[e.Channel];
                    item.State = e.IsActive ? "ON" : "OFF";
                    item.IsActive = e.IsActive;
                }
                AppendLog($"DI{e.Channel} state changed → {(e.IsActive ? "ON (triggered)" : "OFF (cleared)")}");
            });
        }
    }

    public class DiItem : System.ComponentModel.INotifyPropertyChanged
    {
        private string _label;
        private string _state = "-";
        private bool _isActive;
        private string _stateBrush = "#4B5563";

        public string Label
        {
            get => _label;
            set { _label = value; OnPropertyChanged(nameof(Label)); }
        }

        public string State
        {
            get => _state;
            set
            {
                _state = value;
                StateBrush = IsActive ? "#4ADE80" : "#4B5563";
                OnPropertyChanged(nameof(State));
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                OnPropertyChanged(nameof(IsActive));
            }
        }

        public string StateBrush
        {
            get => _stateBrush;
            set { _stateBrush = value; OnPropertyChanged(nameof(StateBrush)); }
        }

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}