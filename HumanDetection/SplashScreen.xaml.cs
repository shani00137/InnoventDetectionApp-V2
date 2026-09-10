using ACGPUIO;
using HumanDetection.Services;
using Material.Icons.WPF;
using SQLite;
using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Utilites.Weight;

namespace HumanDetection
{
    /// <summary>
    /// Startup splash screen. Runs all global initialization so that by the time
    /// the main window opens, AI models, weight machine, Moxa controller and the
    /// OCR service are already loaded and shared across the whole application.
    /// </summary>
    public partial class SplashScreen : Window
    {
        private bool _shutdown;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public SplashScreen()
        {
            InitializeComponent();
        }

        private async void SplashScreen_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Ui(() => MainProgress.IsIndeterminate = true);

                // 1) AI MODELS (global)
                StartCheck(AiIcon, AiLoading, orror: AiError, sub: AiSub);
                bool modelsOk = await AppModels.LoadAsync(msg => Ui(() => StatusTxt.Text = msg));
                EndCheck(ok: modelsOk, icon: AiIcon, loading: AiLoading, error: AiError,
                         sub: AiSub, okMsg: "Ready · box counting + human detection",
                         failMsg: AppModels.LastError);
                SystemHealth.Current.ModelsLoaded = modelsOk;
                SystemHealth.Current.ModelError = AppModels.LastError;
                if (IsCancelled()) return;

                // 2) MOXA CONTROLLER
                StartCheckMoxa();
                bool moxaOk = await CheckMoxaAsync();
                EndCheck(ok: moxaOk, icon: MoxaIcon, loading: GpioLoading, error: GpioError,
                         sub: GpioSub, okMsg: "Connected", failMsg: "Not reachable");
                SystemHealth.Current.MoxaOk = moxaOk;
                if (IsCancelled()) return;

                // 3) WEIGHT MACHINE
                StartCheckWeight();
                bool weightOk = await CheckWeightAsync();
                EndCheck(ok: weightOk, icon: WeightIcon, loading: WeightLoading, error: WeightError,
                         sub: WeightSub, okMsg: "Connected", failMsg: "Not detected");
                SystemHealth.Current.WeightOk = weightOk;
                if (IsCancelled()) return;

                // 4) OCR SERVICE (global)
                StartCheckOcr();
                bool ocrOk = await OcrProcessService.Current.EnsureStartedAsync(msg => Ui(() => StatusTxt.Text = msg));
                EndCheck(ok: ocrOk, icon: OcrIcon, loading: APILoading, error: APIError,
                         sub: APISub, okMsg: "Running on port 5000", failMsg: OcrProcessService.Current.LastError);
                SystemHealth.Current.OcrOk = ocrOk;
                SystemHealth.Current.OcrError = OcrProcessService.Current.LastError;
                if (IsCancelled()) return;

                Ui(() =>
                {
                    MainProgress.IsIndeterminate = false;
                    MainProgress.Value = 100;
                    ReadyTxt.Text = SystemHealth.Current.IsFullyReady ? "System Ready" : "Partially Ready";
                    StatusTxt.Text = SystemHealth.Current.IsFullyReady
                        ? "All systems ready. Starting application..."
                        : "Some components could not be initialized.";
                });

                await Task.Delay(500);
                if (IsCancelled()) return;

                OpenMainWindow();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SplashScreen init");
                Ui(() => StatusTxt.Text = "Startup failed: " + ex.Message);
                await Task.Delay(400);
                if (!IsCancelled()) OpenMainWindow();
            }
        }

        private bool IsCancelled() =>
            _shutdown || _cts.IsCancellationRequested;

        private void OpenMainWindow()
        {
            var main = new MainWindow();
            // Transfer ownership so app shutdown tracks the real main window,
            // not the splash.
            Application.Current.MainWindow = main;
            main.Show();
            _shutdown = true;
            Close();
        }

        #region Checks

        /// <summary>
        /// Verifies the Moxa controller is reachable (ping + token refresh).
        /// Uses a temporary AccessController so the running app never holds it.
        /// </summary>
        private async Task<bool> CheckMoxaAsync()
        {
            try
            {
                var settings = SettingsRepository.GetSettings();
                if (settings == null || string.IsNullOrWhiteSpace(settings.MoxIP))
                    return false;

                using var ping = new Ping();
                var reply = await ping.SendPingAsync(settings.MoxIP, 100);
                if (reply.Status != IPStatus.Success)
                    return false;

                using var controller = new AccessController(settings.MoxIP);
                return await controller.RefreshToken();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Splash check Moxa");
                return false;
            }
        }

        /// <summary>
        /// Opens the configured serial port briefly to verify the scale responds.
        /// The short-lived reader is disposed immediately so Home owns its own reader.
        /// </summary>
        private async Task<bool> CheckWeightAsync()
        {
            return await Task.Run(async () =>
            {
                try
                {
                    if (IsCancelled()) return false;

                    var settings = SettingsRepository.GetSettings();
                    if (settings == null || string.IsNullOrWhiteSpace(settings.ComPort))
                        return false;

                    using var reader = new ScaleSerialReader
                    {
                        PortName = settings.ComPort,
                        BaudRate = 9600
                    };

                    reader.Start();
                    await Task.Delay(1500);
                    return reader.IsOpen;
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "Splash check weight");
                    return false;
                }
            });
        }

        #endregion

        #region UI helpers

        private void Ui(Action action)
        {
            if (_shutdown || Dispatcher.HasShutdownStarted) return;
            Dispatcher.Invoke(action);
        }

        /// <summary>Displays a loading spinner on the given row.</summary>
        private void StartCheck(MaterialIcon icon, FrameworkElement loading, TextBlock orror, TextBlock sub)
        {
            Ui(() =>
            {
                loading.Visibility = Visibility.Visible;
                icon.Opacity = 0.35;
                if (orror != null) orror.Visibility = Visibility.Collapsed;
            });
        }

        private void StartCheckMoxa()
        {
            Ui(() => { GpioSub.Text = "Verifying Moxa network device..."; });
            StartCheck(MoxaIcon, GpioLoading, GpioError, GpioSub);
        }

        private void StartCheckWeight()
        {
            Ui(() => { WeightSub.Text = "Connecting to weight scale..."; });
            StartCheck(WeightIcon, WeightLoading, WeightError, WeightSub);
        }

        private void StartCheckOcr()
        {
            Ui(() => { APISub.Text = "Starting PaddleOCR engine..."; });
            StartCheck(OcrIcon, APILoading, APIError, APISub);
        }

        /// <summary>Marks a row done (check/error) and sets the sub-status text.</summary>
        private void EndCheck(bool ok, MaterialIcon icon, FrameworkElement loading, TextBlock error,
                              TextBlock sub, string okMsg, string failMsg)
        {
            Ui(() =>
            {
                loading.Visibility = Visibility.Collapsed;
                icon.Opacity = 1.0;
                if (error != null) error.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
                sub.Text = ok ? okMsg : (string.IsNullOrWhiteSpace(failMsg) ? "Failed" : failMsg);
                sub.Foreground = ok
                    ? new SolidColorBrush(Color.FromRgb(0x78, 0x7A, 0xFF))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));
                StatusTxt.Text = ok ? okMsg : ("Error: " + (string.IsNullOrWhiteSpace(failMsg) ? "check failed" : failMsg));
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _shutdown = true;
            _cts.Cancel();
            base.OnClosed(e);
        }

        #endregion
    }
}