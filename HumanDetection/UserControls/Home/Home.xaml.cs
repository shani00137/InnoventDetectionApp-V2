using ACGPUIO;
using Basler.Pylon;
using Dynamsoft.Core;
using Dynamsoft.CVR;
using Dynamsoft.License;
using Dynamsoft.Utility;
using HumanDetection.Model;
using HumanDetection.Utilites.Animation;
using HumanDetection.Utilites.Audio;
using HumanDetection.Utilites.PalletAPI;
using MaterialDesignThemes.Wpf;
using Microsoft.ML.OnnxRuntime;
using Microsoft.VisualBasic.ApplicationServices;
using Model;
using NAudio.CoreAudioApi;
using NAudio.Wave.Asio;
using Newtonsoft.Json;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.Internal.Vectors;
using OpenCvSharp.Text;
using OpenCvSharp.WpfExtensions;

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SQLite;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tesseract;
using Utilites;
using Utilites.Alignment;
using Utilites.BoxCounting;
using Utilites.CameraSettings;
using Utilites.PalletAPI;
using Utilites.PythonScripts;
using Utilites.Weight;
using Yolov5Net.Scorer;
using Yolov5Net.Scorer.Models;

using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.Design.AxImporter;
using Color = SixLabors.ImageSharp.Color;
using FlipMode = OpenCvSharp.FlipMode;
using Font = SixLabors.Fonts.Font;
using Image = SixLabors.ImageSharp.Image;
using Point = SixLabors.ImageSharp.PointF;
using ResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;
using Size = SixLabors.ImageSharp.Size;

namespace HumanDetection
{
    /// <summary>
    /// Interaction logic for Home.xaml
    /// </summary>
    public partial class Home : System.Windows.Controls.Page
    {
        private VideoCapture _capture;

        private Mat _frame;
        private bool _isRunning = true;
        private bool _isRunning2 = true;
        private AppSettings _settings;


        private YoloScorer<YoloCocoP5Model> _scorerHumanModel;
        private YoloScorer<YoloBoxCountingModel> _scorerBoxCountingModel;
        private SixLabors.Fonts.Font _font;
        private IAudioManager audioManager;
        private bool _isAlertPlaying = false;
        private bool _isSidebarOpen = false;
        public AccessController _ac;

        private const int TargetFPS = 8;
        private const int DetectionWidth = 640;
        private const int DetectionHeight = 360;
        private DispatcherTimer timer;
        private int elapsedSeconds = 0;

        private CancellationTokenSource _processingCts;
        private bool _isProcessing = false;
        private CaptureVisionRouter? cvRouter;
        public ObservableCollection<BitmapImage> CapturedImages { get; set; }
        public ObservableCollection<ResutlModel> ResultDataList { get; set; }
        private ScaleSerialReader? _reader;
        private OcrPythonClient _ocrHost;
        private bool _isPalletDetectionRunning = false;
        private CancellationTokenSource? _palletCts;
        public bool FirstTerm = true;
        public string pythonExe = @"C:\Users\Owner\AppData\Local\Programs\Python\Python310\python.exe";
        //public string pythonExe = @" C:\Users\USER\AppData\Local\Programs\Python\Python310\python.exe";
        private readonly SnackbarMessageQueue _messageQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(3));
        public event EventHandler<DIChangedEventArgs> DIChanged;
        private bool _IsForkleffound = false;
        private List<CapturedCameraImage> _lastCapturedCameraImages = new();
        private readonly int CamDelay = 500;
        public Home()
        {
            InitializeComponent();
            ResultDialoag.CloseClicked += ResultDialog_CloseClicked;
            ResultDialoag.RestartProcessClicked += ResultDialog_RestartProcessClicked;
            SucessDialog.CloseClicked += (s, e) =>
            {
                PictureDialog.Visibility = Visibility.Collapsed;
                ResultDialoag.Visibility = Visibility.Collapsed;
                ImageDialogHost.IsOpen = false;
            };
            SetIndicator(SensorIndicator, true);        // Sensor ON
            SetIndicator(TemperatureIndicator, false);  // Temperature OK
            SetIndicator(HumidityIndicator, false);      // Humidity ON
            SetIndicator(MotorIndicator, false);

            // Changed to async

            audioManager = new AudioManager();
            CapturedImages = new ObservableCollection<BitmapImage>();
            DataContext = this;
            Loaded += MainWindow_LoadedAsync;

        }
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {

            _frame = new Mat();
            //LoadModels();

        }
        #region Load Application Model and Devices
        private async void MainWindow_LoadedAsync(object sender, RoutedEventArgs e)
        {
            try
            {

                _settings = SettingsRepository.GetSettings();
                if (_settings != null)
                    _ac = new AccessController($"{_settings.MoxIP}");
                RotatorPowerValueText.Text = _settings.RoutatorTimer.ToString();
                RotatorSlider.Value = (double)_settings.RoutatorTimer;

                PowerValueText.Text = _settings.ConfidenceLevel.ToString();
                ConfidenceThresholdSlider.Value = double.Parse(_settings.ConfidenceLevel);

                ResultDialoag.Visibility = Visibility.Visible;

                ResultDataList = new ObservableCollection<ResutlModel>();

                // Show loading indicator
                await PrepareAllDevicesAndModels();
                snackbarMesssage.MessageQueue = _messageQueue; // Assign queue
                                                               // Subscribe to the sensor event
                _ac.DIChanged += OnSensorChanged;

                // Start polling DI channel 0 (your sensor port)
                await _ac.StartDIPollingAsync(channel: 0);

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Initialization failed: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
            }

        }
        public async Task PrepareAllDevicesAndModels()
        {
            LoadingOverlay.Visibility = Visibility.Visible;

            bool modelOk = await RunModelCheck(AiLoading, AiCheck, AiError, LoadModelsAsync);

            if (!modelOk)
            {
                ProgressTxt.Text = "AI Model loading failed!";
                //return; // stop further checks
            }


            bool gpioOk = await RunDeviceCheck(GpioLoading, GpioCheck, GpioError, CheckGpioAsync);

            if (!gpioOk)
            {
                ProgressTxt.Text = "GPIO device not reachable!";
                //return;
            }
            else
            {
                //await TurnOnRotatorAsync();
            }

            bool cameraOk = await RunDeviceCheck(CameraLoading, CameraCheck, CameraError, CheckCameraAsync);

            if (!cameraOk)
            {
                ProgressTxt.Text = "Camera not detected or insufficient cameras!";

            }
            bool apiOk = await RunDeviceCheck(
                                                APILoading,
                                                APICheck,
                                                APIError,
                                                StartFlaskApiAsync);

            if (!apiOk)
            {
                ProgressTxt.Text = "OCR service failed to start!";
                return;
            }

            await Task.Delay(500);
            bool weightOk = await RunDeviceCheck(WeightLoading, WeightCheck, WeightError, StartScaleAsync);

            if (!weightOk)
            {
                ProgressTxt.Text = "Weight scale not detected!";
                //return; // stop startup if critical
            }


            LoadingOverlay.Visibility = Visibility.Collapsed;
            //await StartPalletDetectionProcAsync();
            //WeightText.Text = "1249 KG";
        }

        private async Task RunCheck(ProgressBar loader, TextBlock success, TextBlock error, int delay)
        {
            loader.Visibility = Visibility.Visible;
            success.Visibility = Visibility.Collapsed;
            error.Visibility = Visibility.Collapsed;

            await Task.Delay(delay); // replace with real device check

            bool ok = true; // replace with actual result

            loader.Visibility = Visibility.Collapsed;
            (ok ? success : error).Visibility = Visibility.Visible;
        }
        private async Task<bool> RunModelCheck(ProgressBar loader, TextBlock success, TextBlock error, Func<Task<bool>> action)
        {
            loader.Visibility = Visibility.Visible;
            success.Visibility = Visibility.Collapsed;
            error.Visibility = Visibility.Collapsed;

            bool result = false;

            try
            {
                result = await action();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                result = false;
            }

            loader.Visibility = Visibility.Collapsed;

            if (result)
                success.Visibility = Visibility.Visible;
            else
                error.Visibility = Visibility.Visible;

            return result;
        }
        private async Task<bool> RunDeviceCheck(ProgressBar loader, TextBlock success, TextBlock error, Func<Task<bool>> action)
        {
            loader.Visibility = Visibility.Visible;
            success.Visibility = Visibility.Collapsed;
            error.Visibility = Visibility.Collapsed;

            bool result = false;

            try
            {
                result = await action();
            }
            catch
            {
                result = false;
            }

            loader.Visibility = Visibility.Collapsed;
            (result ? success : error).Visibility = Visibility.Visible;

            return result;
        }
        private async Task<bool> CheckGpioAsync()
        {
            try
            {
                if (_settings == null || string.IsNullOrWhiteSpace(_settings.MoxIP))
                    return false;

                // 1️⃣ PING CHECK
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(_settings.MoxIP, 100);

                if (reply.Status != IPStatus.Success)
                    return false;

                // 2️⃣ CREATE CONTROLLER
                _ac = new AccessController(_settings.MoxIP);

                // 3️⃣ TOKEN CHECK (REAL DEVICE TEST)
                bool tokenOk = await _ac.RefreshToken();

                return tokenOk;
            }
            catch (Exception ex)
            {
                Console.WriteLine("GPIO check failed: " + ex.Message);
                return false;
            }
        }
        private async Task<bool> CheckCameraAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var cameraList = CameraFinder.Enumerate();

                    if (cameraList == null)
                        return false;

                    int count = cameraList.Count();

                    Console.WriteLine($"Camera count detected: {count}");

                    return count > 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Camera check failed: " + ex.Message);
                    return false;
                }
            });
        }
        public async Task<bool> StartFlaskApiAsync()
        {
            try
            {
                KillProcessesUsingPort(5000);
                //KillProcessesUsingPort(5001);

                // Start BOTH APIs
                var ocrProcess = StartPythonApi("ocr_api.py");
                //var palletProcess = StartPythonApi("pallet_api.py");

                // Wait for both APIs
                bool ocrAlive = await WaitForApiAsync("http://127.0.0.1:5000/ocr");
                //bool palletAlive = await WaitForApiAsync("http://127.0.0.1:5001/predict");

                return ocrAlive;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Flask start error: " + ex.Message);
                return false;
            }
        }
        private async Task<bool> WaitForApiAsync(string url)
        {
            using var http = new HttpClient();

            for (int i = 0; i < 30; i++)
            {
                try
                {
                    var resp = await http.GetAsync(url);

                    // OCR returns 405 → OK
                    if (resp.StatusCode == HttpStatusCode.MethodNotAllowed)
                    {
                        return true;
                    }
                }
                catch
                {
                    // still starting
                }

                await Task.Delay(1000);
            }

            return false;
        }
        private Process StartPythonApi(string scriptName)
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = scriptName,
                WorkingDirectory = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets"
                ),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Debug.WriteLine($"[{scriptName}] " + e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Debug.WriteLine($"[{scriptName} ERR] " + e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }
        public static void KillProcessesUsingPort(int port)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c netstat -ano | findstr :{port}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    // Example line:
                    // TCP    0.0.0.0:9000     0.0.0.0:0     LISTENING     12345
                    var parts = Regex.Split(line.Trim(), @"\s+");
                    if (parts.Length >= 5 && int.TryParse(parts[^1], out int pid))
                    {
                        try
                        {
                            Process.GetProcessById(pid).Kill(true);
                            Debug.WriteLine($"Killed process PID {pid} using port {port}");
                        }
                        catch { /* ignore access denied */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("KillProcessesUsingPort error: " + ex.Message);
            }
        }
        private async Task<bool> LoadModelsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {

                    var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions();

                    try
                    {
                        sessionOptions.AppendExecutionProvider_DML();
                    }
                    catch
                    {
                        sessionOptions.AppendExecutionProvider_CPU();
                    }

                    var modelPathBoxCounting =
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "Assets/Weights/customBoxCount.onnx");

                    if (!File.Exists(modelPathBoxCounting))
                        throw new FileNotFoundException("BoxCount model missing");

                    _scorerBoxCountingModel =
                        new YoloScorer<YoloBoxCountingModel>(modelPathBoxCounting, sessionOptions);

                    var modelPathHumanDetection =
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "Assets/Weights/yolov5s.onnx");

                    if (!File.Exists(modelPathHumanDetection))
                        throw new FileNotFoundException("Human detection model missing");

                    _scorerHumanModel =
                        new YoloScorer<YoloCocoP5Model>(modelPathHumanDetection, sessionOptions);

                    var fontPath = @"C:\Windows\Fonts\consola.ttf";
                    if (!File.Exists(fontPath))
                        throw new FileNotFoundException("Font missing");

                    _font = new SixLabors.Fonts.Font(
                        new FontCollection().Add(fontPath), 16);

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Model load error: " + ex.Message);
                    return false;
                }
            });
        }

        #endregion
        public async Task StartPalletDetectionProcAsync()
        {

            Dispatcher.Invoke(() =>
            {
                EntryTimeTxt.Text = DateTime.Now.ToString("HH:mm:ss");
            });

            var allCameras = CameraFinder.Enumerate();
            List<Task> cameraTasks = new List<Task>();

            foreach (var camera in allCameras)
            {
                string serial = camera[CameraInfoKey.SerialNumber];
                CameraPosition position = CameraHelper.GetCameraPosition(serial);

                switch (position)
                {
                    case CameraPosition.Front:
                        SetCameraUI(CamFrontLightPulse, true);
                        cameraTasks.Add(Task.Run(() => CheckPalletStatus(camera, CameraPosition.Front)));
                        break;

                    case CameraPosition.Top:
                        SetCameraUI(CamTopLightPulse, true);
                        //cameraTasks.Add(Task.Run(() => CheckPalletStatus(camera, CameraPosition.Top)));
                        break;

                    case CameraPosition.Left:
                        SetCameraUI(CamLeftLightPulse, true);
                        break;

                    case CameraPosition.Right:
                        SetCameraUI(CamRightLightPulse, true);
                        break;
                }
            }

            await Task.WhenAll(cameraTasks);
        }
        public async Task StopPalletDetectionProc()
        {
            //await StopBuzzer();
            await OffBlower();
            await OffRotatorAsync();

            Dispatcher.Invoke(async () =>
            {
                PictureDialog.Visibility = Visibility.Collapsed;
                ResultDialoag.Visibility = Visibility.Collapsed;
                SucessDialog.Visibility = Visibility.Visible;
                ImageDialogHost.IsOpen = true;


                ExitTimeTxt.Text = DateTime.Now.ToString("HH:mm:ss");
            });
        }



        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            StopScale();
        }


        private ObservableCollection<OcrFrameResult> _ocrResults = new ObservableCollection<OcrFrameResult>();

        private CancellationTokenSource _ocrCancellationTokenSource;

        #region AIModel Detection
        private async Task CheckPalletStatus(ICameraInfo cameraInfo, CameraPosition position)
        {
            try
            {
                ResetCountdown();

                //int fullRotation = 80000;
                //int step = GetRotatorDurationInMilliseconds(_lastWeight)-2000;



                //List<(int time, double score)> scanResults = new();

                //_messageQueue.Enqueue("Scanning pallet alignment");



                int elapsed = 0;
                Dispatcher.Invoke(() =>
                {

                    LoadingCard.Visibility = Visibility.Visible;
                    ImageDialogHost.IsOpen = true;
                    LoadingOverlay.Visibility = Visibility.Visible;
                    ProgressTxt.Text = "Starting capture...";
                    PictureDialog.Visibility = Visibility.Collapsed;
                    ResultDialoag.Visibility = Visibility.Collapsed;
                    SucessDialog.Visibility = Visibility.Collapsed;


                });

                //while (elapsed <= fullRotation)
                //{
                //    var image = await CaptureSingleFrameFromCameraAsync(cameraInfo);
                //    Dispatcher.Invoke(() =>
                //    {
                //        PaletImage.Source = image;
                //        QuickCamPreview.Source = image;
                //    });
                //    Image<Rgba32> convertedImage = BitmapImageToImageSharp(image);



                //    var resizedImage = convertedImage.CloneAs<Rgba32>();
                //    int modelWidth = 640;
                //    int modelHeight = 640;

                //    resizedImage.Mutate(x =>
                //        x.Resize(new ResizeOptions
                //        {
                //            Size = new Size(modelWidth, modelHeight),
                //            Mode = ResizeMode.Pad,
                //            PadColor = Color.Black
                //        }));

                //    // ----------------------------------------------------
                //    // 2. Confidence threshold
                //    // ----------------------------------------------------
                //    double confidenceThreshold = 0.0;

                //    if (_settings != null &&
                //        !string.IsNullOrWhiteSpace(_settings.ConfidenceLevel) &&
                //        double.TryParse(_settings.ConfidenceLevel, out var dbValue))
                //    {
                //        confidenceThreshold = Math.Clamp(dbValue / 100.0, 0.0, 1.0);
                //    }

                //    // ----------------------------------------------------
                //    // 3. Run model prediction
                //    // ----------------------------------------------------
                //    List<YoloPrediction> rawPredictions = _scorerBoxCountingModel
                //        .Predict(resizedImage)
                //        .Where(p => p.Score >= confidenceThreshold)
                //        .ToList();

                //    double score = 0;
                //    if (rawPredictions.Any())
                //    {
                //        var palletPredictions = rawPredictions
                //            .Where(p => p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase))
                //            .ToList();

                //        double boxAvg;

                //        if (palletPredictions.Count == 0)
                //        {
                //            // No "pallet" label found — fall back to averaging "box" predictions
                //            // instead of leaving score at 0.
                //            var boxPredictions = rawPredictions
                //                .Where(p => p.Label.Name.Equals("box", StringComparison.OrdinalIgnoreCase))
                //                .ToList();

                //            boxAvg = boxPredictions.Count()>4
                //                ? boxPredictions.Average(p => p.Score)
                //                : 0.0;
                //        }
                //        else
                //        {
                //            boxAvg = palletPredictions.Average(p => p.Score);
                //        }

                //        score = boxAvg;
                //    }

                //    scanResults.Add((elapsed, score));
                //    Dispatcher.Invoke(() =>
                //    {
                //        ScoreTxt.Text = $"{score * 100:0}%";
                //        LoadingOverlay.Visibility = Visibility.Visible;
                //        ProgressTxt.Text ="Score: "+ ScoreTxt.Text;
                //    });

                //    if (score >= 0.85)
                //    {
                //        break;
                //    }


                //    await StartRoutatorWithDuration(step);

                //    elapsed += step;
                //    var bitmap = ImageSharpToBitmapImage(resizedImage);

                //    Dispatcher.Invoke(() =>
                //    {
                //        PaletImage.Source = bitmap;

                //        //ScoreTxt.Text = $"{score * 100:0}%";
                //    });
                //    await Task.Delay(100);
                //}



                // ---------------------------------------------------------------
                // FIX (confirmed bug): Dispatcher.Invoke(async () => ...) does NOT
                // await the lambda body — it's typed as Action, so Invoke returns
                // as soon as the first `await` inside is hit. That means the
                // `finally` block below (_isProcessing = false) used to run BEFORE
                // CaptureAndDisplayAllCamerasAsync actually finished.
                // Switched to Dispatcher.InvokeAsync(...).Task and awaited it.
                // ---------------------------------------------------------------
                await Dispatcher.InvokeAsync(async () =>
                {
                    ShowPalletFromLeft();
                    await Task.Delay(100);
                    await PalletDetectedSoundStart();
                    await Task.Delay(100);
                    await CaptureAndDisplayAllCamerasAsync();
                }).Task.Unwrap();





            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Camera] Exception: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                Console.WriteLine("[Camera] Process stopped.");
            }
        }
        private BitmapImage ImageSharpToBitmapImage(Image<Rgba32> image)
        {
            using (var ms = new MemoryStream())
            {
                image.SaveAsBmp(ms); // or SaveAsPng
                ms.Seek(0, SeekOrigin.Begin);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze(); // 🔥 important for cross-thread

                return bitmap;
            }
        }
        private async Task CaptureAndDisplayAllCamerasAsync()
        {
            try
            {
                await StartBlower();
                bool detectionPassed = false;
                int attempTaken = 0;

                Dispatcher.Invoke(() =>
                {
                    LoadingOverlay.Visibility = Visibility.Visible;
                    ProgressTxt.Text = "Starting capture...";
                });
                
                while (!detectionPassed)
                {
                    attempTaken++;
                    var obj = new ResutlModel
                    {
                        StartTime = DateTime.Now,
                        EndTime = null,
                        TotalBoxes = 0,
                        BarcodeCodeCount = 0,
                        DublicateBarcode = null,
                        ExpiryDate = null,
                        HumanDetect = null,
                        OCRResult = null,
                        PalletHeight = 0,
                        Score = "",
                        SupplierName = null,
                        TotalWeight = null
                    };
                    AddResult(obj, true);

                    ReportProgress("📷 Capturing images...");
                    var cameraList = CameraFinder.Enumerate();

                    // Capture front + back images IN PARALLEL
                    var frontCaptureTask = CaptureSingleFrameFromAllCamerasAsync(cameraList, false);
                    StartCountdown();
                    var backCaptureTask = CaptureSingleFrameFromAllCamerasAsync(cameraList, true);
                    
                    await Task.WhenAll(frontCaptureTask, backCaptureTask);

                    var imagesWithCamPosition = frontCaptureTask.Result;
                    var backSideImages = backCaptureTask.Result;
                    _lastCapturedCameraImages = imagesWithCamPosition;


                    ReportProgress("🧠 Running AI + OCR in parallel...");

                    // Run front AI (full) and back AI (only needed for OCR bytes) IN PARALLEL
                    var aiTask = Task.Run(() => RunAllAIDetectionsAsync(imagesWithCamPosition));
                    var aiTaskBack = Task.Run(() => RunAllAIDetectionsAsync(backSideImages));

                    await Task.WhenAll(aiTask, aiTaskBack);
                    StopCountdown();
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    LoadingCard.Visibility = Visibility.Collapsed;
                    _messageQueue.Enqueue("Please wait Calculate result..");
                    var aiResult = aiTask.Result;
                    var aiResultBack = aiTaskBack.Result;
                    double avgScore = aiResult.AvScore;
                    bool humanDetected = aiResult.HumanDetected;
                    int numberOfBox = aiResult.NumberOfBox;
                    ScoreTxt.Text = $"{avgScore * 100:0}%";


                    // Run OCR sequentially — local Python server can't handle concurrent requests
                    var cropByteList = aiResult.OCRBytes;
                    var cropByteListBack = aiResultBack.OCRBytes;
                    List<OcrImageResult> ocrResultList = new List<OcrImageResult>();

                    if (cropByteList != null && cropByteList.Any())
                    {
                        var api = await RunOcrAsync(cropByteList);
                        if (api?.results != null)
                        {
                            var valid = api.results
                                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                                .Where(x => string.IsNullOrWhiteSpace(x.Value?.error))
                                .Select(x => x.Value);
                            ocrResultList.AddRange(valid);
                        }
                    }
                    if (cropByteListBack != null && cropByteListBack.Any())
                    {
                        var api = await RunOcrAsync(cropByteListBack);
                        if (api?.results != null)
                        {
                            var valid = api.results
                                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                                .Where(x => string.IsNullOrWhiteSpace(x.Value?.error))
                                .Select(x => x.Value);
                            ocrResultList.AddRange(valid);
                        }
                    }

                    _messageQueue.Enqueue("Please wait Calculate result..");
                  
                    
                    // ✅ Update UI safely
                    Dispatcher.Invoke(() =>
                    {
                        NoBoxTxt.Text = aiResult.NumberOfBox.ToString();
                        PalletHeightTxt.Text = $"{aiResult.maxPalletHeight:F2} m";
                    });

                    Dispatcher.Invoke(() =>
                    {
                        //CapturedImages.Clear();
                        foreach (var img in imagesWithCamPosition)
                            CapturedImages.Add(img.Image);
                    });

                    if (aiResult.maxPalletHeight > 1.7)
                    {
                        HeightBox.Background = System.Windows.Media.Brushes.Red;
                        await StartBuzzer();
                        await PlayAlertForSystem();


                    }
                    
                    if (humanDetected == true)
                    {
                        HumanDetectBox.Background = System.Windows.Media.Brushes.Red;

                        await StartBuzzlerWithDuration(6000, 1);
                        await PlayAlertForSystem();
                        HumanDetectedTxt.Text = "Yes";
                    }
                    int distinctBarcodes = ocrResultList
                                    .Where(r => r?.barcodes != null)
                                    .SelectMany(r => r.barcodes)
                                    .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length > 6)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .Count();

                    if (distinctBarcodes > 1)
                    {
                        await StartBuzzlerWithDuration(6000, 1);
                        SKUDetectBox.Background = System.Windows.Media.Brushes.Red;
                        BarcodeSKUText.Text = "Yes";
                        await PlayAlertForSystem();
                    }

                    var allDateStrings = ocrResultList
                            .Where(r => r?.dates != null)
                            .SelectMany(r => r.dates)
                            .Where(d => !string.IsNullOrWhiteSpace(d))
                            .Distinct()
                            .ToList();

                    var dateFormats = new[] { "dd.MM.yyyy", "MM.dd.yyyy", "yyyy.MM.dd", "dd.MM.yy", "dd/MM/yyyy", "MM/dd/yyyy", "dd-MM-yyyy", "yyyy-dd-MM", "dd.MM", "MM.dd" };

                    var parsedDates = allDateStrings
                            .Select(d =>
                            {
                                var trimmed = d.Trim();
                                DateTime? parsed = null;
                                foreach (var fmt in dateFormats)
                                {
                                    if (DateTime.TryParseExact(trimmed, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                                    {
                                        parsed = dt;
                                        break;
                                    }
                                }
                                return new { Parsed = parsed, Original = trimmed };
                            })
                            .OrderBy(x => x.Parsed.HasValue ? 0 : 1)
                            .ThenBy(x => x.Parsed)
                            .ThenBy(x => x.Original)
                            .ToList();

                    int distinctDates = parsedDates.Count;
                    string AllDatesList = string.Join(", ", parsedDates.Select(x => x.Parsed.HasValue ? x.Parsed.Value.ToString("dd.MM.yyyy") : x.Original));
                    int barcodeCounts = ocrResultList
                                        .Where(r => r?.barcodes != null)
                                        .SelectMany(r => r.barcodes)
                                        .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length > 6)
                                        .Count();

                    var distinctBarcodeItems = ocrResultList
                                       .Where(r => r?.barcodes != null)
                                       .SelectMany(r => r.barcodes)
                                       .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length > 6)
                                       .Distinct()
                                       .OrderBy(v => v)
                                       .ToList();
                    string barcodeList = string.Join(", ", distinctBarcodeItems);

                    if (distinctDates >= 3)
                    {
                        await StartBuzzlerWithDuration(2000, 1);
                        DateExireBox.Background = System.Windows.Media.Brushes.Red;
                        DateExpireTxt.Text = "Yes";
                        await PlayAlertForSystem();
                    }

                    string OCRResultInString = "";

                    foreach (var q in ocrResultList)
                    {
                        OCRResultInString += OcrResultToString(q) + Environment.NewLine;
                    }
                    List<OcrGridItem> gridItems = new List<OcrGridItem>();

                    int index = 1;
                    for (int i = 0; i < 3; i++)
                    {
                        await StartBuzzer();
                        await Task.Delay(200);
                        await StopBuzzer();
                    }
                    foreach (var item in ocrResultList)
                    {


                        gridItems.Add(new OcrGridItem
                        {
                            ImageIndex = index++,
                            Barcodes = item.barcodes != null ? string.Join(", ", item.barcodes) : "",
                            Dates = item.dates != null ? string.Join(", ", item.dates) : ""
                        });

                    }
                    int LableCount = ocrResultList
    .Count(r => r?.dates != null && r.dates.Any(d => !string.IsNullOrWhiteSpace(d)));
                    Dispatcher.Invoke(() =>
                    {
                        ExitTimeTxt.Text = DateTime.Now.ToString("HH:mm:ss");
                        ResultDialoag.UpdateResults(ResultDataList);
                        ScoreTxt.Text = $"{avgScore * 100:0}%";
                        var obj = new ResutlModel
                        {
                            StartTime = DateTime.Now,
                            EndTime = DateTime.Now,
                            TotalBoxes = numberOfBox,
                            BarcodeCodeCount = barcodeCounts,
                            DublicateBarcodeCount = distinctBarcodes,
                            ExpiryDate = DateExpireTxt.Text,
                            HumanDetect = humanDetected==true?"Yes":"NO",
                            OCRResult = OCRResultInString,
                            PalletHeight = aiResult.maxPalletHeight,
                            Score = ScoreTxt.Text,
                            SupplierName = null,
                            TotalWeight = WeightText.Text,
                            AllDatesList = AllDatesList,
                            DateList = parsedDates.Select(x => x.Parsed.HasValue ? x.Parsed.Value.ToString("dd.MM.yyyy") : x.Original).ToList(),
                            BarcodeList = barcodeList,
                            BarcodeListItems = ocrResultList
                                .Where(r => r?.barcodes != null)
                                .SelectMany(r => r.barcodes)
                                .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length > 6)
                                .Distinct()
                                .ToList(),
                            GridItems = gridItems,
                            LableCount = LableCount,
                            DateCount = distinctDates

                        };
                        AddResult(obj, false);
                        // OCR result available here


                    });
                    string ResultTooPost = $"AvgScore {avgScore} TotalWight {WeightText.Text} OCRResponse:{OCRResultInString}";
                    if (avgScore >= 0.60)
                    {
                        detectionPassed = true;
                        var request = new ResultRequestModel
                        {
                            ResutlModelList = ResultDataList.ToList(),
                        };
                        _messageQueue.Enqueue("70% score found..  result are saved");
                        // 🔥 POST TO API
                        var payload = new PalletRequest
                        {
                            name = "Pallet Scan",

                            palletWeight = WeightText.Text,
                            palletHeight = aiResult.maxPalletHeight.ToString(),
                            NO_OfBoxs = numberOfBox.ToString(),

                            startTime = EntryTimeTxt.Text,
                            endTime = ExitTimeTxt.Text,

                            trustScoreLevel = (avgScore * 100).ToString("0"),

                            productionDate = null,
                            exipreDate = distinctDates.ToString(),

                            barCode = barcodeList,

                            palletCondition = avgScore >= 0.7 ? "Good" : "Rejected",

                            humenDetection = humanDetected ? "Yes" : "No",

                            // ⚠️ ASSUMPTION: this hardcoded URL looks like a leftover
                            // test/demo image rather than the actual captured pallet
                            // image. Left exactly as-is since I can't confirm whether
                            // a real upload step exists elsewhere that this was meant
                            // to be replaced by.
                            image = "https://adp-backend-demo.ashybay-437ca219.uaenorth.azurecontainerapps.io/core/uploads/image-1769754805619.jpg"
                        };

                        // ⚠️ ASSUMPTION: "YOUR_TOKEN_HERE" looks like an unfilled
                        // placeholder. Left exactly as-is — move to _settings/config
                        // once you confirm the real token value.
                        //var service = new PalletApiService(
                        //       "YOUR_TOKEN_HERE",
                        //       "99927ec1-8668-45ae-8709-2db03366e680",
                        //       "https://adp-backend-demo.ashybay-437ca219.uaenorth.azurecontainerapps.io/core/thing-type/66b9a073b241574cd76f0616/adpPallet"
                        //   );

                        //bool isSuccess = await service.PostPalletDataAsync(payload);
                        await StopPalletDetectionProc();

                        try
                        {
                            string baseDir = @"C:\AdPortresult";
                            string timeStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                            string saveDir = System.IO.Path.Combine(baseDir, timeStamp);
                            Directory.CreateDirectory(saveDir);

                            string imgDir = System.IO.Path.Combine(saveDir, "Images");
                            Directory.CreateDirectory(imgDir);

                            int imgIdx = 1;
                            foreach (var camImg in imagesWithCamPosition)
                            {
                                string fileName = $"{camImg.Position}_{imgIdx++}.png";
                                string filePath = System.IO.Path.Combine(imgDir, fileName);
                                using var fileStream = new FileStream(filePath, FileMode.Create);
                                var encoder = new PngBitmapEncoder();
                                encoder.Frames.Add(BitmapFrame.Create(camImg.Image));
                                encoder.Save(fileStream);
                            }

                            string annoDir = System.IO.Path.Combine(saveDir, "Annotated");
                            Directory.CreateDirectory(annoDir);
                            string[] sideNames = { "Front", "Right", "Back", "Left", "Top" };
                            for (int a = 0; a < aiResult.AnnotatedImages.Count && a < sideNames.Length; a++)
                            {
                                string filePath = System.IO.Path.Combine(annoDir, $"{sideNames[a]}_annotated.png");
                                File.WriteAllBytes(filePath, aiResult.AnnotatedImages[a]);
                            }

                            string resultText =
                                $"=== Pallet Detection Result ===\r\n" +
                                $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                                $"Score: {avgScore * 100:0}%\r\n" +
                                $"Human Detected: {humanDetected}\r\n" +
                                $"Number of Boxes: {numberOfBox}\r\n" +
                                $"Pallet Height: {aiResult.maxPalletHeight}m\r\n" +
                                $"Weight: {WeightText.Text}\r\n" +
                                $"Entry Time: {EntryTimeTxt.Text}\r\n" +
                                $"Exit Time: {ExitTimeTxt.Text}\r\n" +
                                $"Barcodes: {barcodeList}\r\n" +
                                $"Distinct Barcodes: {distinctBarcodes}\r\n" +
                                $"Dates: {AllDatesList}\r\n" +
                                $"Distinct Dates: {distinctDates}\r\n" +
                                $"Pallet Condition: {payload.palletCondition}\r\n" +
                                $"OCR Results:\r\n{OCRResultInString}\r\n";
                            File.WriteAllText(System.IO.Path.Combine(saveDir, "result.txt"), resultText);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to save local results: {ex.Message}");
                        }


                        //await ResetRecords();
                    }
                    else
                    {
                        _messageQueue.Enqueue("System has found score less then 70, process restart..");
                        if (attempTaken >= 3)
                        {
                            await StopPalletDetectionProc();
                            break;
                        }

                        ReportProgress("🔄 Repositioning pallet...");
                        await TurnOnRotatorAsync();
                        await Task.Delay(8000);
                        await StartRoutatorWithDuration(5200);
                        FirstTerm = false;
                    }
                }
            }
            catch (Exception ex)
            {
                await StopBuzzer();
                await OffBlower();
                await OffRotatorAsync();
                MessageBox.Show(ex.Message);
            }
        }
        /// <summary>
        /// Save Result to API
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>

        public async Task<List<CapturedCameraImage>> CaptureSingleFrameFromAllCamerasAsync(List<ICameraInfo> cameraInfos, bool FirstTerm)
        {
            var capturedImages = new List<CapturedCameraImage>();
            // Thread-safe collection to gather results from parallel tasks safely
            var concurrentCapturedImages = new System.Collections.Concurrent.ConcurrentBag<CapturedCameraImage>();

            if (FirstTerm == false)
            {
                // Run all camera captures completely in parallel
                var captureTasks = cameraInfos.Select(async camInfo =>
                {
                    try
                    {
                        CameraPosition position = CameraHelper.GetCameraPosition(camInfo[CameraInfoKey.SerialNumber]);

                        // ===============================
                        // TOP CAMERA → ONLY 1 IMAGE
                        // ===============================
                        if (position == CameraPosition.Top)
                        {
                            using var camera = new Basler.Pylon.Camera(camInfo);
                            camera.CameraOpened += Basler.Pylon.Configuration.AcquireSingleFrame;
                            camera.Open();

                            FlashCamera(TopFlashEllipse);
                            PlayShutterSound();

                            using IGrabResult grabResult = camera.StreamGrabber.GrabOne(CamDelay, TimeoutHandling.ThrowException);

                            if (grabResult.GrabSucceeded)
                            {
                                using Mat frame = GrabResultToMat(grabResult);
                                var bmp = frame.ToBitmap();
                                var bitmapImage = ConvertBitmapToImageSource(bmp);
                                bitmapImage.Freeze();

                                concurrentCapturedImages.Add(new CapturedCameraImage
                                {
                                    Position = CameraPosition.Top,
                                    Image = bitmapImage
                                });

                                Dispatcher.Invoke(() =>
                                {
                                    QuickCamPreview.Source = bitmapImage;
                                });
                            }

                            camera.Close();
                            Console.WriteLine("📸 Top camera image captured");
                        }
                        if (position == CameraPosition.Front)
                        {
                            using var camera = new Basler.Pylon.Camera(camInfo);
                            camera.CameraOpened += Basler.Pylon.Configuration.AcquireSingleFrame;
                            camera.Open();

                            FlashCamera(TopFlashEllipse);
                            PlayShutterSound();

                            using IGrabResult grabResult = camera.StreamGrabber.GrabOne(CamDelay, TimeoutHandling.ThrowException);

                            if (grabResult.GrabSucceeded)
                            {
                                using Mat frame = GrabResultToMat(grabResult);
                                using Mat rotatedFrame = RotateImage90Degrees(frame);
                                var bmp = rotatedFrame.ToBitmap();
                                var bitmapImage = ConvertBitmapToImageSource(bmp);
                                bitmapImage.Freeze();

                                concurrentCapturedImages.Add(new CapturedCameraImage
                                {
                                    Position = CameraPosition.Front,
                                    Image = bitmapImage
                                });

                                Dispatcher.Invoke(() =>
                                {
                                    QuickCamPreview.Source = bitmapImage;
                                });
                            }

                            camera.Close();
                            Console.WriteLine("📸 Top camera image captured");
                        }
                        if (position == CameraPosition.Right)
                        {
                            using var camera = new Basler.Pylon.Camera(camInfo);
                            camera.CameraOpened += Basler.Pylon.Configuration.AcquireSingleFrame;
                            camera.Open();

                            FlashCamera(TopFlashEllipse);
                            PlayShutterSound();

                            using IGrabResult grabResult = camera.StreamGrabber.GrabOne(CamDelay, TimeoutHandling.ThrowException);

                            if (grabResult.GrabSucceeded)
                            {
                                using Mat frame = GrabResultToMat(grabResult);
                                using Mat rotatedFrame = RotateImage90Degrees(frame);
                                var bmp = rotatedFrame.ToBitmap();
                                var bitmapImage = ConvertBitmapToImageSource(bmp);
                                bitmapImage.Freeze();

                                concurrentCapturedImages.Add(new CapturedCameraImage
                                {
                                    Position = CameraPosition.Right,
                                    Image = bitmapImage
                                });

                                Dispatcher.Invoke(() =>
                                {
                                    QuickCamPreview.Source = bitmapImage;
                                });
                            }

                            camera.Close();
                            Console.WriteLine("📸 Top camera image captured");
                        }
                        if (position == CameraPosition.Left)
                        {
                            using var camera = new Basler.Pylon.Camera(camInfo);
                            camera.CameraOpened += Basler.Pylon.Configuration.AcquireSingleFrame;
                            camera.Open();

                            FlashCamera(TopFlashEllipse);
                            PlayShutterSound();

                            using IGrabResult grabResult = camera.StreamGrabber.GrabOne(CamDelay, TimeoutHandling.ThrowException);

                            if (grabResult.GrabSucceeded)
                            {
                                using Mat frame = GrabResultToMat(grabResult);
                                using Mat rotatedFrame = RotateImage90Degrees(frame);
                                var bmp = rotatedFrame.ToBitmap();
                                var bitmapImage = ConvertBitmapToImageSource(bmp);
                                bitmapImage.Freeze();

                                concurrentCapturedImages.Add(new CapturedCameraImage
                                {
                                    Position = CameraPosition.Left,
                                    Image = bitmapImage
                                });

                                Dispatcher.Invoke(() =>
                                {
                                    QuickCamPreview.Source = bitmapImage;
                                });
                            }

                            camera.Close();
                            Console.WriteLine("📸 Top camera image captured");
                        }
                        // ======================================
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Capture failed: {ex.Message}");
                    }
                });

                // Wait until all parallel threads finish capturing their frames
                await Task.WhenAll(captureTasks);
            }
            else
            {
                // Wait until forklift leaves before starting rotation
                _messageQueue.Enqueue("Wait for Forkleft go away");
                ReportProgress("Wait for Forkleft go away");
                while (_IsForkleffound)
                {
                    await StartBuzzer();
                    await Task.Delay(200);
                    await StopBuzzer();
                }
                ReportProgress("Forkleft go away");

                //int sect = GetRotatorDurationInMilliseconds(_lastWeight);
               int timeSpan= GetRotationMotorTimeMilliseconds(90, _lastWeight);
           
                await StartRoutatorWithDuration(timeSpan);
              

                // Run the secondary camera loop routines in parallel
                var captureTasksElse = cameraInfos.Select(async camInfo =>
                {
                    try
                    {
                        CameraPosition position = CameraHelper.GetCameraPosition(camInfo[CameraInfoKey.SerialNumber]);
                        // FRONT CAMERA → 4 SIDES (ROTATION)
                        // ======================================
                        if (position == CameraPosition.Front)
                        {
                            var sides = new[]
                            {
                        CameraPosition.Left
                    };

                            foreach (var side in sides)
                            {
                                using var camera = new Basler.Pylon.Camera(camInfo);
                                camera.CameraOpened += Basler.Pylon.Configuration.AcquireSingleFrame;
                                camera.Open();

                                FlashCamera(FrontFlashEllipse);
                                PlayShutterSound();

                                using IGrabResult grabResult = camera.StreamGrabber.GrabOne(CamDelay, TimeoutHandling.ThrowException);

                                if (grabResult.GrabSucceeded)
                                {
                                    using Mat frame = GrabResultToMat(grabResult);
                                    using Mat rotatedFrame = RotateImage90Degrees(frame);

                                    var bitmapImage = ConvertBitmapToImageSource(rotatedFrame.ToBitmap());
                                    bitmapImage.Freeze();

                                    concurrentCapturedImages.Add(new CapturedCameraImage
                                    {
                                        Position = side,
                                        Image = bitmapImage
                                    });

                                    Dispatcher.Invoke(() =>
                                    {
                                        PaletImage.Source = bitmapImage;
                                        QuickCamPreview.Source = bitmapImage;
                                    });

                                    Console.WriteLine($"📸 Captured {side} side from Front camera");
                                }

                                camera.Close();

                                // rotate pallet for next side
                                if (side != CameraPosition.Left)
                                {
                                    _messageQueue.Enqueue($"Image From: {side}");

                                    int duration = _settings.RoutatorTimer != null ? (int)_settings.RoutatorTimer : 0;

                                    var sec = GetRotatorDurationInMilliseconds(_lastWeight);
                                    await StartRoutatorWithDuration((int)sec);
                                    await Task.Delay(1000);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Capture failed: {ex.Message}");
                    }
                });

                await Task.WhenAll(captureTasksElse);
            }

            // Convert the thread-safe concurrent bag results back to standard list layout
            capturedImages.AddRange(concurrentCapturedImages);
            return capturedImages;
        }
        private Mat ApplyDigitalZoom(Mat frame, double zoomFactor = 1.5)
        {
            // zoomFactor > 1.0 zooms in. e.g. 1.5 = crop to 66% of frame, then scale back up.
            int newWidth = (int)(frame.Width / zoomFactor);
            int newHeight = (int)(frame.Height / zoomFactor);

            int x = (frame.Width - newWidth) / 2;
            int y = (frame.Height - newHeight) / 2;

            var roi = new OpenCvSharp.Rect(x, y, newWidth, newHeight);
            using var cropped = new Mat(frame, roi);

            var zoomed = new Mat();
            Cv2.Resize(cropped, zoomed, frame.Size(), 0, 0, InterpolationFlags.Cubic);

            return zoomed;
        }

        private Mat RotateImage90Degrees(Mat frame)
        {
            var rotated = new Mat();
            Cv2.Rotate(frame, rotated, RotateFlags.Rotate90Clockwise);
            return rotated;
        }
        public int GetRotatorDurationInMilliseconds(double currentWeight)
        {
            double statisTime = 5.4;
            // Ensure weight is not negative
            if (currentWeight < 0) currentWeight = 0;
            return (int)(Math.Round(currentWeight * statisTime));
        }
        public static int GetRotationMotorTimeMilliseconds(double angleDegrees, double palletWeightKg)
        {
            if (angleDegrees <= 0)
                return 0;

            const double referenceAngle = 90.0;

            const double emptyTimeFor90Degree = 5.08;
            const double noEffectWeightKg = 550.0;
            const double heavyWeightKg = 950.0;
            const double heavyTimeFor90Degree = 5.30;

            const double safetyMarginSeconds = 0.05;

            palletWeightKg = Math.Max(0, palletWeightKg);

            double secondsPerKgAfterLimit =
                (heavyTimeFor90Degree - emptyTimeFor90Degree) /
                (heavyWeightKg - noEffectWeightKg);

            double timeFor90Degree = emptyTimeFor90Degree;

            if (palletWeightKg > noEffectWeightKg)
            {
                timeFor90Degree += (palletWeightKg - noEffectWeightKg) * secondsPerKgAfterLimit;
            }

            double finalSeconds =
                (timeFor90Degree * (angleDegrees / referenceAngle)) + safetyMarginSeconds;

            return (int)Math.Ceiling(finalSeconds * 1000);
        }
        public async Task<BitmapImage?> CaptureSingleFrameFromCameraAsync(ICameraInfo cameraInfo)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var camera = new Basler.Pylon.Camera(cameraInfo))
                    {

                        camera.CameraOpened += Basler.Pylon.Configuration.AcquireSingleFrame;
                        camera.Open();

                        // 🔦 Flash + sound (adjust if needed)
                        FlashCamera(FrontFlashEllipse);
                        PlayShutterSound();

                        using (IGrabResult grabResult =
                            camera.StreamGrabber.GrabOne(3000, TimeoutHandling.ThrowException))
                        {
                            if (grabResult.GrabSucceeded)
                            {
                                using Mat frame = GrabResultToMat(grabResult);

                                var bmp = frame.ToBitmap();
                                var bitmapImage = ConvertBitmapToImageSource(bmp);
                                bitmapImage.Freeze();
                                //QuickCamPreview.Source= bitmapImage;
                                Console.WriteLine($"📸 Captured frame from {cameraInfo[CameraInfoKey.ModelName]}");
                                return bitmapImage;
                            }
                        }

                        camera.Close();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Capture failed from {cameraInfo[CameraInfoKey.ModelName]}: {ex.Message}");
                }

                return null;
            });
        }

        private async Task<(double AvScore, bool HumanDetected, int NumberOfBox, List<byte[]> OCRBytes, double maxPalletHeight, List<byte[]> AnnotatedImages)>
RunAllAIDetectionsAsync(List<CapturedCameraImage> capturedImages)
        {
            UpdateProgressStatus("AI Model Start to detect");

            if (capturedImages == null || capturedImages.Count < 1)
            {
                MessageBox.Show("❌ Not enough images to process AI models.");

                // FIX (confirmed bug): returning `null` for OCRBytes caused a
                // NullReferenceException later at `cropByteList.Any()` in
                // CaptureAndDisplayAllCamerasAsync. Return an empty list instead.
                return (0.0, false, 0, new List<byte[]>(), 0, new List<byte[]>());
            }

            int NumberOfBox = 0;
            double maxPalletHeight = 0.0;
            double totalAvgScore = 0.0;
            int avgScoreCount = 0;
            bool HumanDetected = false;

            // Convert BitmapImages to ImageSharp
            var imageSharpList = capturedImages
                                    .Select(img => BitmapImageToImageSharp(img.Image))
                                    .ToList();

            List<byte[]> ocrResults = new();
            List<byte[]> annotatedImages = new();

            // Store predictions per side
            List<YoloPrediction> frontBoxes = new();
            List<YoloPrediction> rightBoxes = new();
            List<YoloPrediction> backBoxes = new();
            List<YoloPrediction> leftBoxes = new();
            List<YoloPrediction> topBoxes = new();

            PalletSide[] captureOrder =
            {
                PalletSide.Front,
                PalletSide.Right,
                PalletSide.Back,
                PalletSide.Left,
                PalletSide.Top
            };

            foreach (var captured in capturedImages)
            {
                PalletSide side;
                switch (captured.Position)
                {
                    case CameraPosition.Front:
                        side = PalletSide.Front;
                        break;
                    case CameraPosition.Right:
                        side = PalletSide.Right;
                        break;

                    // FIX (confirmed bug): this case was MISSING entirely.
                    // CameraPosition.Back fell through to `default` below and was
                    // mislabeled as PalletSide.Front, overwriting the real Front
                    // result and leaving backBoxes permanently empty.
                    case CameraPosition.Back:
                        side = PalletSide.Back;
                        break;

                    case CameraPosition.Left:
                        side = PalletSide.Left;
                        break;
                    case CameraPosition.Top:
                        side = PalletSide.Top;
                        break;
                    default:
                        side = PalletSide.Front;
                        break;
                }
                using var image = BitmapImageToImageSharp(captured.Image);


                var boxTask = RunBoxCountingModelAsync(image, side);
                var humanTask = Task.Run(() => RunHumanDetectionModel(image));

                await Task.WhenAll(boxTask);

                var boxResult = boxTask.Result;
                var humanResult = humanTask.Result;

                // ✅ Collect average score
                if (boxResult.AverageScore > 0)
                {
                    totalAvgScore += boxResult.AverageScore;
                    avgScoreCount++;
                }

                // ✅ Track max pallet height
                if (boxResult.PalletHeightMeters > maxPalletHeight)
                {
                    maxPalletHeight = boxResult.PalletHeightMeters;
                }

                // ✅ Human detection
                HumanDetected |= humanResult;

                // ✅ OCR images
                if (boxResult.BoxesImages != null)
                    ocrResults.AddRange(boxResult.BoxesImages);

                // ✅ Annotated image
                if (boxResult.AnnotatedImage != null)
                    annotatedImages.Add(boxResult.AnnotatedImage);

                // ✅ Store predictions by side
                switch (side)
                {
                    case PalletSide.Front:
                        frontBoxes = boxResult.BoxPredictions ?? new();
                        break;

                    case PalletSide.Right:
                        rightBoxes = boxResult.BoxPredictions ?? new();
                        break;

                    case PalletSide.Back:
                        backBoxes = boxResult.BoxPredictions ?? new();
                        break;

                    case PalletSide.Left:
                        leftBoxes = boxResult.BoxPredictions ?? new();
                        break;
                    case PalletSide.Top:
                        topBoxes = boxResult.BoxPredictions ?? new(); break;
                }

                image.Dispose();
            }

            // ✅ Dispose original ImageSharp images
            foreach (var img in imageSharpList)
                img.Dispose();

            // Final average score
            double finalAverageScore =
                avgScoreCount > 0 ? totalAvgScore / avgScoreCount : 0.0;

            int topBoxCount = topBoxes.Count();
            int frontRows = BoxCountingService.CountTopRows(frontBoxes);
            NumberOfBox = topBoxCount == 0 ? 1 : topBoxCount * frontRows;

            return (finalAverageScore, HumanDetected, NumberOfBox, ocrResults, maxPalletHeight, annotatedImages);
        }

        private void ReportProgress(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressTxt.Text = message;
            });
        }

        private async Task<ImagePredictionResult> RunBoxCountingModelAsync(Image<Rgba32> originalImage, PalletSide side)
        {
            UpdateProgressStatus("Box Counting AI Model Start");

            // ----------------------------------------------------
            // 1. Resize input image for model
            // ----------------------------------------------------
            var resizedImage = originalImage.CloneAs<Rgba32>();
            int modelWidth = 640;
            int modelHeight = 640;

            resizedImage.Mutate(x =>
                x.Resize(new ResizeOptions
                {
                    Size = new Size(modelWidth, modelHeight),
                    Mode = ResizeMode.Pad,
                    PadColor = Color.Black
                }));

            // ----------------------------------------------------
            // 2. Confidence threshold
            // ----------------------------------------------------
            double confidenceThreshold = 0.0;

            if (_settings != null &&
                !string.IsNullOrWhiteSpace(_settings.ConfidenceLevel) &&
                double.TryParse(_settings.ConfidenceLevel, out var dbValue))
            {
                confidenceThreshold = Math.Clamp(dbValue / 100.0, 0.0, 1.0);
            }

            // ----------------------------------------------------
            // 3. Run model prediction
            // ----------------------------------------------------
            var rawPredictions = _scorerBoxCountingModel
                .Predict(resizedImage)
                .Where(p => p.Score >= confidenceThreshold)
                .ToList();

            // ----------------------------------------------------
            // 4. Select tallest pallet
            // ----------------------------------------------------
            var palletCandidates = rawPredictions
                .Where(p => p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var tallestPallet = palletCandidates
                .OrderByDescending(p => p.Rectangle.Height)
                .FirstOrDefault();

            // Keep all non-pallet predictions (boxes etc.)
            var predictions = rawPredictions
                .Where(p => !p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int boxCount = rawPredictions.Count(p =>
                p.Label.Name.Equals("box", StringComparison.OrdinalIgnoreCase));

            // ----------------------------------------------------
            // 5. Calculate pallet height in meters
            //
            // HOW THIS WORKS:
            // The model runs on a 640×640 resized image. The pallet bounding box
            // height (in pixels at 640px scale) is converted to real-world meters
            // using a calibration value: how many meters does 1 pixel represent
            // at your camera's mounting distance/angle.
            //
            // CALIBRATION (one-time, do this once per installation):
            //   1. Place a pallet or object of KNOWN height (e.g. exactly 1.20m)
            //      in front of the camera in the normal scanning position.
            //   2. Run the model — note the pallet bounding box height in pixels
            //      on the 640px-wide image (print it with Log/Console.WriteLine).
            //   3. metersPerPixel = knownHeightMeters / measuredPixelHeight
            //      e.g. if a 1.20m pallet measures 420px tall:
            //           metersPerPixel = 1.20 / 420 = 0.002857
            //   4. Store that value in _settings.MetersPerPixel (add to your
            //      AppSettings class and settings UI), or hardcode it below until
            //      you add the settings field.
            //
            // ⚠️ ASSUMPTION: metersPerPixel defaults to 0.003 here (roughly
            // correct for a ~1.2m pallet filling ~400px of a 640px frame at
            // typical warehouse camera distances). REPLACE this with your real
            // calibrated value — the number will be wrong until you do.
            // ----------------------------------------------------
            double palletHeightMeters = 0.0;

            if (tallestPallet != null)
            {
                // Read calibration from settings if available, otherwise use default
                double metersPerPixel = 0.002626; // ← REPLACE with your calibrated value



                // The pallet bounding box height in the 640×640 model image
                float palletPixelHeight = tallestPallet.Rectangle.Height;

                // Also account for how the image was padded when resized to 640×640:
                // if the original image was not square, black padding was added, which
                // compresses the actual content into fewer pixels. We need to reverse
                // that scaling to get the true pixel height in the content area.
                float originalAspect = (float)originalImage.Height / originalImage.Width;
                float contentHeightIn640 = modelWidth * originalAspect; // how many of the 640px rows are real content

                // If content fills less than 640 rows (padding added top/bottom),
                // the content was scaled down by this factor:
                float padScaleFactor = contentHeightIn640 < modelHeight
                    ? contentHeightIn640 / modelHeight
                    : 1.0f;

                // Effective pixel height in the unpadded content area
                float effectivePixelHeight = palletPixelHeight / padScaleFactor;

                palletHeightMeters = effectivePixelHeight * metersPerPixel;

                // Sanity clamp — a pallet taller than 3m or shorter than 0.1m is
                // almost certainly a detection error, not a real pallet.
                palletHeightMeters = Math.Clamp(palletHeightMeters, 0.0, 3.0);


            }
            else
            {

            }

            // ----------------------------------------------------
            // 6. Box average score
            // ----------------------------------------------------
            double averageScore = 0.0;
            var boxPredictions = predictions
                .Where(p => p.Label.Name.Equals("box", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (boxPredictions.Any())
            {
                averageScore = boxPredictions.Average(p => p.Score);
            }
            else if (palletCandidates.Any())
            {
                // Fallback: no "box" labels found — use pallet score instead
                averageScore = palletCandidates.Average(p => p.Score);
            }

            // ----------------------------------------------------
            // 7. Crop boxes from ORIGINAL high-res image for OCR
            //    (coordinates scaled from 640px model space back to original)
            // ----------------------------------------------------
            float scaleX = (float)originalImage.Width / modelWidth;
            float scaleY = (float)originalImage.Height / modelHeight;

            var cropByteList = new List<byte[]>();

            foreach (var p in predictions)
            {
                int x = (int)Math.Round(p.Rectangle.X * scaleX);
                int y = (int)Math.Round(p.Rectangle.Y * scaleY);
                int width = (int)Math.Round(p.Rectangle.Width * scaleX);
                int height = (int)Math.Round(p.Rectangle.Height * scaleY);

                if (width <= 0 || height <= 0) continue;

                int squareSize = Math.Max(width, height);
                squareSize = Math.Min(squareSize, originalImage.Width);
                squareSize = Math.Min(squareSize, originalImage.Height);

                int newX = x - (squareSize - width) / 2;
                int newY = y - (squareSize - height) / 2;

                newX = Math.Max(0, Math.Min(newX, originalImage.Width - squareSize));
                newY = Math.Max(0, Math.Min(newY, originalImage.Height - squareSize));

                int newWidth = Math.Min(squareSize, originalImage.Width - newX);
                int newHeight = Math.Min(squareSize, originalImage.Height - newY);

                if (newWidth <= 0 || newHeight <= 0) continue;

                var cropRect = new SixLabors.ImageSharp.Rectangle(newX, newY, newWidth, newHeight);
                using var crop = originalImage.Clone(ctx => ctx.Crop(cropRect));
                using var ms = new MemoryStream();

                string tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BoxCrops");
                Directory.CreateDirectory(tempFolder);

                string fileName = $"crop_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.jpg";
                string filePath = System.IO.Path.Combine(tempFolder, fileName);

                crop.SaveAsJpeg(ms);
                cropByteList.Add(ms.ToArray());

                ms.Position = 0;
                //using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                //ms.CopyTo(fs);
            }

            // ----------------------------------------------------
            // 8. Draw detection boxes on resized image for UI
            // ----------------------------------------------------
            double palletAngleDeg = tallestPallet?.Score * 100 ?? 0;

            double finalScore;
            if (palletAngleDeg > 0 && averageScore > 0)
                finalScore = (palletAngleDeg + averageScore) / 2;
            else if (palletAngleDeg > 0)
                finalScore = palletAngleDeg;
            else
                finalScore = averageScore;

            byte[] annoBytes;

            using (var annotated = resizedImage.Clone())
            {
                var colorBox = new Rgba32(0, 255, 0);
                var colorPallet = new Rgba32(255, 64, 64);
                var font = SixLabors.Fonts.SystemFonts.CreateFont("Arial", 14, SixLabors.Fonts.FontStyle.Bold);

                var largestPallet = rawPredictions
                    .Where(p => p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.Rectangle.Width * p.Rectangle.Height)
                    .FirstOrDefault();

                foreach (var p in rawPredictions)
                {
                    bool isPallet = p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase);
                    if (isPallet && p != largestPallet) continue;

                    var color = isPallet ? colorPallet : colorBox;

                    annotated.Mutate(ctx =>
                    {
                        ctx.Draw(color, 6, p.Rectangle);

                        // Show height on the pallet box label
                        string labelText = isPallet
                            ? $"pallet {p.Score:P1} h={palletHeightMeters:F2}m"
                            : $"{p.Label.Name} {p.Score:P1}";

                        var textLocation = new SixLabors.ImageSharp.PointF(
                            p.Rectangle.X + 5,
                            Math.Max(0, p.Rectangle.Y - 25)
                        );

                        var textBgRect = new SixLabors.ImageSharp.RectangleF(
                            textLocation.X - 3,
                            textLocation.Y - 3,
                            labelText.Length * 9,
                            22
                        );

                        ctx.Fill(Color.FromRgba(0, 0, 0, 180), textBgRect);
                        ctx.DrawText(labelText, font, color, textLocation);
                    });
                }

                using var ms = new MemoryStream();
                annotated.SaveAsPng(ms);
                annoBytes = ms.ToArray();
                ms.Seek(0, SeekOrigin.Begin);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                App.Current.Dispatcher.Invoke(() => CapturedImages.Add(bitmap));
            }

            return new ImagePredictionResult
            {
                Side = side,
                BoxPredictions = boxPredictions,
                PalletHeightMeters = palletHeightMeters,
                AverageScore = averageScore,
                PalletAngleDeg = palletAngleDeg,
                BoxesImages = cropByteList,
                AnnotatedImage = annoBytes
            };
        }


        private bool RunHumanDetectionModel(Image<Rgba32> image)
        {
            UpdateProgressStatus("Human Detection AI Model Start");

            var predictions = _scorerHumanModel.Predict(image);

            bool humanDetected = predictions.Any(p =>
                (
                    p.Label.Name.Equals("person", StringComparison.OrdinalIgnoreCase) ||
                    p.Label.Name.Equals("human", StringComparison.OrdinalIgnoreCase) ||
                    p.Label.Name.Equals("man", StringComparison.OrdinalIgnoreCase) ||
                    p.Label.Name.Equals("woman", StringComparison.OrdinalIgnoreCase)
                )
                && p.Score >= 0.82f   // ✅ confidence check
            );

            return humanDetected;
        }


        private async Task<OcrApiResponse> RunOcrAsync(List<byte[]> images)
        {

            using var client = new HttpClient();
            using var content = new MultipartFormDataContent();
            client.Timeout = TimeSpan.FromMinutes(5);
            int index = 0;

            foreach (var img in images)
            {
                var byteContent = new ByteArrayContent(img);
                byteContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

                string fileName = $"capture_{index++}.jpg";   // ✅ unique name
                content.Add(byteContent, "images", fileName);
            }

            // ⚠️ ASSUMPTION — NOT CHANGED: no try/catch here means a transient
            // OCR API failure (service restart, timeout) throws all the way up to
            // CaptureAndDisplayAllCamerasAsync's catch block, which fully aborts
            // the pallet cycle (buzzer/blower/rotator off + MessageBox) rather than
            // just retrying OCR. Left as-is since changing retry behavior is a
            // design decision — happy to add a scoped retry here if you want it.
            var response = await client.PostAsync(
                "http://127.0.0.1:5000/ocr",
                content
            );

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<OcrApiResponse>(json);
        }


        private Image<Rgba32> BitmapImageToImageSharp(BitmapImage bitmapImage)
        {
            using var memory = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapImage));
            encoder.Save(memory);
            memory.Position = 0;
            return SixLabors.ImageSharp.Image.Load<Rgba32>(memory);
        }
        private BitmapImage ConvertBitmapToImageSource(Bitmap bitmap)
        {
            // Clone the original bitmap to release any underlying locks (thread-safety)
            using (var clonedBitmap = new Bitmap(bitmap))
            using (var memory = new MemoryStream())
            {
                // Save cloned bitmap to memory stream
                clonedBitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;

                // Load BitmapImage fully from the stream BEFORE disposing it
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad; // Important: Load into memory
                bitmapImage.StreamSource = new MemoryStream(memory.ToArray()); // Independent stream
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Optional: Makes it cross-thread accessible
                return bitmapImage;
            }
        }

        public string RunOCR(string pythonExe, string scriptPath, string imagePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\" \"{imagePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();

                process.WaitForExit();

                if (!string.IsNullOrEmpty(errors))
                {
                    throw new Exception("Python OCR Error: " + errors);
                }

                return output.Trim();
            }
        }
        private Mat GrabResultToMat(IGrabResult grabResult)
        {
            // Get the pixel data as byte array
            byte[] buffer = (byte[])grabResult.PixelData;

            // Create empty mat with correct dimensions
            Mat mat = new Mat(grabResult.Height, grabResult.Width, MatType.CV_8UC1);

            // Copy data using Marshal (safe approach)
            Marshal.Copy(buffer, 0, mat.Data, buffer.Length);

            // Convert grayscale to color (BGR)
            Mat colorMat = new Mat();
            Cv2.CvtColor(mat, colorMat, ColorConversionCodes.GRAY2BGR);

            // Optional: Apply color map for visualization (requires OpenCvSharp 4.x)
            // Mat colored = new Mat();
            // Cv2.ApplyColorMap(colorMat, colored, ColormapTypes.Jet);
            // return colored;

            return colorMat;
        }

        private void AddResult(ResutlModel input, bool isNew = false)
        {
            if (input == null)
                return;

            // Get latest result
            var resultModel = ResultDataList
                                .OrderByDescending(x => x.StartTime)
                                .FirstOrDefault();

            // 🔹 UPDATE EXISTING RESULT
            if (resultModel != null && !isNew)
            {
                if (input.TotalBoxes.HasValue)
                    resultModel.TotalBoxes = input.TotalBoxes;

                if (input.PalletHeight.HasValue)
                    resultModel.PalletHeight = input.PalletHeight;

                if (!string.IsNullOrWhiteSpace(input.TotalWeight))
                    resultModel.TotalWeight = input.TotalWeight;

                if (!string.IsNullOrWhiteSpace(input.ExpiryDate))
                    resultModel.ExpiryDate = input.ExpiryDate;

                if (!string.IsNullOrWhiteSpace(input.SupplierName))
                    resultModel.SupplierName = input.SupplierName;

                if (input.BarcodeCodeCount.HasValue)
                    resultModel.BarcodeCodeCount = input.BarcodeCodeCount;

                if (input.DublicateBarcode.HasValue)
                    resultModel.DublicateBarcode = input.DublicateBarcode;

                if (!string.IsNullOrWhiteSpace(input.OCRResult))
                    resultModel.OCRResult = input.OCRResult;

                if (!string.IsNullOrWhiteSpace(input.Score))
                    resultModel.Score = input.Score;

                if (input.StartTime.HasValue)
                    resultModel.StartTime = input.StartTime;

                if (input.EndTime.HasValue)
                    resultModel.EndTime = input.EndTime;

                if (!string.IsNullOrWhiteSpace(input.HumanDetect))
                    resultModel.HumanDetect = input.HumanDetect;
                if (!string.IsNullOrWhiteSpace(input.AllDatesList))
                    resultModel.AllDatesList = input.AllDatesList;
                if (input.DublicateBarcodeCount.HasValue)
                    resultModel.DublicateBarcodeCount = input.DublicateBarcodeCount;
                if (!string.IsNullOrWhiteSpace(input.BarcodeList))
                    resultModel.BarcodeList = input.BarcodeList;
                if (input.LableCount.HasValue)
                    resultModel.LableCount = input.LableCount;
                if (input.GridItems.Count > 0)
                    resultModel.GridItems = input.GridItems;
                if (input.DateCount.HasValue)
                    resultModel.DateCount = input.DateCount;
                if (input.DateList != null)
                    resultModel.DateList = input.DateList;
                if (input.BarcodeListItems != null)
                    resultModel.BarcodeListItems = input.BarcodeListItems;

            }
            // 🔹 ADD NEW RESULT
            else if (isNew)
            {
                ResultDataList.Add(new ResutlModel
                {
                    StartTime = input.StartTime ?? DateTime.Now,
                    EndTime = input.EndTime,
                    TotalBoxes = input.TotalBoxes,
                    PalletHeight = input.PalletHeight,
                    TotalWeight = input.TotalWeight,
                    ExpiryDate = input.ExpiryDate,
                    SupplierName = input.SupplierName,
                    BarcodeCodeCount = input.BarcodeCodeCount,
                    DublicateBarcode = input.DublicateBarcode,
                    OCRResult = input.OCRResult,
                    Score = input.Score,
                    HumanDetect = string.IsNullOrWhiteSpace(input.HumanDetect)
                                    ? "No"
                                    : input.HumanDetect
                });
            }
        }




        private void Image_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Child is System.Windows.Controls.Image img)
            {
                DialogImage.Source = img.Source;
                var clickedImage = img.Source as BitmapImage;
                var match = _lastCapturedCameraImages.FirstOrDefault(x =>
                    ReferenceEquals(x.Image, clickedImage));
                var positionName = match?.Position.ToString() ?? "Unknown";
                DialogImagePosition.Text = $"Position: {positionName}";
                ImageDialogHost.IsOpen = true;
                PictureDialog.Visibility = Visibility.Visible;
                SucessDialog.Visibility = Visibility.Collapsed;
                ResultDialoag.Visibility = Visibility.Collapsed;
            }
        }

        private void CloseDialog_Click(object sender, RoutedEventArgs e)
        {
            ImageDialogHost.IsOpen = false; // ✅ close popup
        }
        private void ResultDialog_CloseClicked(object sender, EventArgs e)
        {
            // Close the dialog
            ImageDialogHost.IsOpen = false;
            MaterialDesignThemes.Wpf.DialogHost.CloseDialogCommand.Execute(null, null);
        }

        private void ResultDialog_RestartProcessClicked(object sender, EventArgs e)
        {
            RestartProcess();

            RebootDeviceAsync();

        }
        private async void Rotate_Click(object sender, EventArgs e)
        {
            int sec = GetRotatorDurationInMilliseconds(_lastWeight) - 1000;
            await StartRoutatorWithDuration((int)sec);
        }
        public async Task RestartProcess()
        {
            // FIX (cleanup, low risk): this was `Dispatcher.Invoke(async () => ...)`
            // with no real awaits inside (just two property assignments), so the
            // original async-not-awaited issue had no practical effect here. Kept
            // functionally identical, just removed the unnecessary `async` to avoid
            // the same trap being copy-pasted elsewhere with real awaits added later.
            Dispatcher.Invoke(() =>
            {
                EntryTimeTxt.Text = DateTime.Now.ToString("HH:mm:ss");
                ExitTimeTxt.Text = "";

            });
            DateExireBox.Background = System.Windows.Media.Brushes.Transparent;
            SKUDetectBox.Background = System.Windows.Media.Brushes.Transparent;
            HumanDetectBox.Background = System.Windows.Media.Brushes.Transparent;
            HeightBox.Background = System.Windows.Media.Brushes.Transparent;
            NoBoxTxt.Text = "0";
            PalletHeightTxt.Text = "0";
            HumanDetectedTxt.Text = "";
            BarcodeSKUText.Text = "";
            DateExpireTxt.Text = "";
            ScoreTxt.Text = "0";
            await StopBuzzer();



            ImageDialogHost.IsOpen = false;
            //await StartRotator();
            MaterialDesignThemes.Wpf.DialogHost.CloseDialogCommand.Execute(null, null);
            var allCameras = CameraFinder.Enumerate();
            int cameraCount = allCameras.Count;

            if (cameraCount > 0)
            {
                foreach (var camera in allCameras)
                {
                    string serial = camera[CameraInfoKey.SerialNumber];
                    CameraPosition position = CameraHelper.GetCameraPosition(serial);

                    switch (position)
                    {
                        case CameraPosition.Front:
                            SetCameraUI(CamFrontLightPulse, true);
                            _ = CheckPalletStatus(camera, CameraPosition.Front);
                            break;

                        case CameraPosition.Top:
                            SetCameraUI(CamTopLightPulse, true);
                            break;

                        case CameraPosition.Left:
                            SetCameraUI(CamLeftLightPulse, true);
                            break;

                        case CameraPosition.Right:
                            SetCameraUI(CamRightLightPulse, true);
                            break;
                    }
                }
            }
            else
            {
                MessageBox.Show("No Camera found");
            }

        }
        public async Task ResetRecords()
        {
            Dispatcher.Invoke(() =>
            {
                EntryTimeTxt.Text = "";
                ExitTimeTxt.Text = "";

            });
            DateExireBox.Background = System.Windows.Media.Brushes.Transparent;
            SKUDetectBox.Background = System.Windows.Media.Brushes.Transparent;
            HumanDetectBox.Background = System.Windows.Media.Brushes.Transparent;
            HeightBox.Background = System.Windows.Media.Brushes.Transparent;
            NoBoxTxt.Text = "0";
            PalletHeightTxt.Text = "0";
            HumanDetectedTxt.Text = "";
            BarcodeSKUText.Text = "";
            DateExpireTxt.Text = "";
            ScoreTxt.Text = "0";
            await StopBuzzer();
            //ResultDataList.Clear();
            //CapturedImages.Clear();
            //var obj = new ResutlModel
            //{
            //    StartTime = DateTime.Now,
            //    EndTime = null,
            //    TotalBoxes = 0,
            //    BarcodeCodeCount = 0,
            //    DublicateBarcode = null,
            //    ExpiryDate = null,
            //    HumanDetect = null,
            //    OCRResult = null,
            //    PalletHeight = 0,
            //    Score = 0,
            //    SupplierName = null,
            //    TotalWeight = null
            //};
            //AddResult(obj, true);

        }
        private void Restart_Click(object sender, EventArgs e)
        {
            RestartProcess();
        }
        private async void StopProc_Click(object sender, EventArgs e)
        {
            await StopPalletDetectionProc();
        }
        private void ShowResult_Click(object sender, EventArgs e)
        {
            // ✅ show popup

            PictureDialog.Visibility = Visibility.Collapsed;
            ResultDialoag.Visibility = Visibility.Visible;
            SucessDialog.Visibility = Visibility.Collapsed;

            ImageDialogHost.IsOpen = true;

        }
        private string OcrResultToString(OcrImageResult result)
        {
            if (result == null)
                return "No OCR result";

            var sb = new StringBuilder();

            // ---- BAR CODES ----
            if (result.barcodes?.Any() == true)
            {
                sb.AppendLine("📦 Barcodes:");
                foreach (var b in result.barcodes)
                {
                    sb.AppendLine($"  • {b}");
                }
                sb.AppendLine();
            }

            // ---- DATES ----
            if (result.dates?.Any() == true)
            {
                sb.AppendLine("📅 Dates:");
                foreach (var d in result.dates)
                {
                    sb.AppendLine($"  • {d}");
                }
                sb.AppendLine();
            }

            // ---- RAW TEXT ----
            if (result.raw_text?.Any() == true)
            {
                sb.AppendLine("📝 Text:");
                foreach (var t in result.raw_text)
                {
                    sb.AppendLine($"  • {t}");
                }
            }

            return sb.ToString();
        }
        #endregion
        #region UIControls
        private void StartCountdown()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;
            timer.Start();
        }
        // Public method to stop the timer from outside
        public void StopCountdown()
        {
            if (timer != null && timer.IsEnabled)
            {
                timer.Stop();
                //System.Windows.Application.Current.Shutdown(); // Stops the WPF application
            }
        }
        public void ResetCountdown()
        {
            if (timer != null)
            {
                timer.Stop();
            }

            elapsedSeconds = 0;
            Dispatcher.Invoke(() =>
            {
                CountdownText.Text = "0 Sec";
            });
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            elapsedSeconds++;
            CountdownText.Text = $"{elapsedSeconds} Sec";
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopCountdown();
        }
        private void FlashCamera(UIElement targetElement)
        {
            Dispatcher.Invoke(() =>
            {
                // Get the base storyboard from UI thread
                if (FindResource("CameraFlashStoryboard") is Storyboard baseStoryboard)
                {
                    // Clone so we can modify safely
                    Storyboard storyboard = baseStoryboard.Clone();

                    // Set the animation target on UI thread
                    Storyboard.SetTarget(storyboard.Children[0], targetElement);

                    // Begin animation on UI thread
                    storyboard.Begin();
                }

                // Play sound (also safe on UI thread)
                //PlayShutterSound();
            });
        }

        private async void PlayShutterSound()
        {
            try
            {
                audioManager.Play("Resources/Audio/shutter.mp3");
                await Task.Delay(500); // short shutter sound
                audioManager.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing shutter sound: {ex.Message}");
            }
        }
        private async Task PalletDetectedSoundStart()
        {
            try
            {
                audioManager.Play("Resources/Audio/pallet.wav");
                await Task.Delay(500); // short shutter sound
                audioManager.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing shutter sound: {ex.Message}");
            }
        }
        private async Task PlayAlertForSystem()
        {
            try
            {
                audioManager.Play("Resources/Audio/warning.wav");
                await Task.Delay(500); // short shutter sound
                audioManager.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing shutter sound: {ex.Message}");
            }
        }


        private void SetCameraUI(Border borderControl, bool isAvailable)
        {
            if (borderControl == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (isAvailable)
                {
                    borderControl.BorderBrush = (SolidColorBrush)(new BrushConverter().ConvertFromString("#00E108")); // green green
                    if (borderControl.Child is StackPanel panel &&
                        panel.Children[0] is Grid grid &&
                        grid.Children[0] is Ellipse ellipse)
                    {
                        ellipse.Opacity = 1;
                    }

                }
                else
                {
                    borderControl.BorderBrush = (SolidColorBrush)(new BrushConverter().ConvertFromString("#FF0000")); // red
                    if (borderControl.Child is StackPanel panel &&
                        panel.Children[0] is Grid grid &&
                        grid.Children[0] is Ellipse ellipse)
                    {
                        ellipse.Opacity = 0;
                    }
                }
            });
        }
        private void ShowPalletFromLeft()
        {
            Dispatcher.Invoke(() =>
            {
                PaletImage.Opacity = 1; // ensure visible
                var storyboard = (Storyboard)FindResource("ShowPalletFromTopStoryboard");
                storyboard.Begin(this, true);
            });
        }
        private void UpdateProgressStatus(String Status)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressTxt.Text = Status;
            });
        }
        private void SetIndicator(System.Windows.Controls.Image indicator, bool isOn)
        {
            string imageUri = isOn
                ? "pack://application:,,,/Resources/Images/on_indicator.png"   // Red (ON)
                : "pack://application:,,,/Resources/Images/off_indicator.png"; // Green (OFF)

            indicator.Source = new BitmapImage(new Uri(imageUri));
        }
        private void ConfidenceThresholder_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PowerValueText != null)
                PowerValueText.Text = ((int)e.NewValue).ToString();
            Task.Delay(100);
            var settings = _settings;
            settings.ConfidenceLevel = e.NewValue.ToString();
            SettingsRepository.UpdateConfidenceThresHoldSettings(settings);
            Task.Delay(100);
            _settings = SettingsRepository.GetSettings();
        }
        private void Rotator_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RotatorPowerValueText != null)
                RotatorPowerValueText.Text = ((int)e.NewValue).ToString();
            Task.Delay(100);
            var settings = _settings;
            settings.RoutatorTimer = ((int)e.NewValue);
            SettingsRepository.UpdateRotatorSettings(settings);
            Task.Delay(100);
            _settings = SettingsRepository.GetSettings();

        }


        #endregion

        // Create ONE shared controller for the whole class

        public async Task StartBuzzer()
        {
            await _ac.StartBuzzerAsync();
        }
        public async Task StartRotatorWithWeightAsync()
        {
            await _ac.StartRotatorWithWeightAsync(_lastWeight);
        }
        public async Task StopBuzzer()
        {
            await _ac.OffBuzzerAsync();
        }

        public async Task StartBlower()
        {
            await _ac.StartBlowerAsync();

        }


        public async Task OffBlower()
        {
            await _ac.OffBlowerAsync();
        }
        public async Task StartRotator()
        {
            await _ac.StartRotatorAsync();
        }
        public async Task OffRotatorAsync()
        {
            await _ac.OffRotatorAsync();

        }
        public async Task StartRoutatorWithDuration(int sec)
        {
            await _ac.StartRotatorForDurationAsync(sec);

        }
        public async Task StartRotatorReverseForDurationAsync(int sec)
        {
            //await _ac.StartRotatorReverseForDurationAsync(sec);

        }
        public async Task TurnOnRotatorAsync()
        {
            await _ac.StartRotatorAsync();

        }
        public async Task RebootDeviceAsync()
        {
            //await _ac.RebootDeviceAsync();
        }
        private void OnSensorChanged(object sender, DIChangedEventArgs e)
        {
            if (e.Channel == 0 && e.IsActive)
            {
                _IsForkleffound = true;
                _messageQueue.Enqueue("Fork Left Arrived");
                Dispatcher.Invoke(() =>
                {
                    ForkliftImage.Visibility = Visibility.Visible;
                    ForkliftImage.Opacity = 1;
                    ForkliftFlashEllipse.Opacity = 1;
                    if (FindResource("ForkliftDetectedStoryboard") is Storyboard baseStoryboard)
                    {
                        Storyboard storyboard = baseStoryboard.Clone();
                        foreach (var child in storyboard.Children)
                        {
                            Storyboard.SetTarget(child, ForkliftImage);
                        }
                        storyboard.Begin();
                    }
                    if (FindResource("CameraFlashStoryboard") is Storyboard flashBase)
                    {
                        Storyboard flashSb = flashBase.Clone();
                        Storyboard.SetTarget(flashSb.Children[0], ForkliftFlashEllipse);
                        flashSb.Begin();
                    }
                });
            }
            else if (e.Channel == 0 && !e.IsActive)
            {
                _messageQueue.Enqueue("Fork go away");
                _IsForkleffound = false;
                Dispatcher.Invoke(() =>
                {
                    ForkliftImage.Opacity = 0;
                    ForkliftImage.Visibility = Visibility.Collapsed;
                    ForkliftFlashEllipse.Opacity = 0;
                });
            }
        }
        public async Task StartBuzzlerWithDuration(int duration, int repeat)
        {
            for (int i = 0; i < repeat; i++)
            {
                await StartBuzzer();              // 🔔 ON
                await Task.Delay(duration);       // ON duration

                await StopBuzzer();               // 🔕 OFF
                await Task.Delay(1000);           // 1 second rest
            }
        }

        #region manage weight machine events

        private async Task<bool> StartScaleAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_reader != null && _reader.IsOpen)
                        return true;

                    _reader = new ScaleSerialReader
                    {
                        PortName = _settings.ComPort,
                        BaudRate = 9600
                    };

                    _reader.WeightReceived += Reader_WeightReceived;
                    _reader.Error += Reader_Error;

                    _reader.Start(); // THIS CAN THROW

                    return true; // SUCCESS
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        WeightText.Text = "ERROR";
                    });

                    Console.WriteLine("Scale error: " + ex.Message);
                    return false; // FAILURE
                }
            });
        }

        private void StopScale()
        {
            if (_reader == null) return;

            _reader.WeightReceived -= Reader_WeightReceived;
            _reader.Error -= Reader_Error;

            _reader.Stop();
            _reader.Dispose();
            _reader = null;
        }
        private DateTime? _stableWeightStartTime = null;
        private const double WeightThreshold = 5.0;     // kg
        private const double WeightTolerance = 0.2;      // ± fluctuation allowed
        private double _lastWeight = 0;

        private void Reader_WeightReceived(object? sender, double w)
        {
            Dispatcher.Invoke(async () =>
            {
                WeightText.Text = $"{w:0.##} KG";

                // BELOW threshold → reset everything
                if (w < WeightThreshold)
                {
                    _stableWeightStartTime = null;

                    if (_isPalletDetectionRunning)
                    {
                        _isPalletDetectionRunning = false;
                        await StopPalletDetectionProc();
                    }
                    return;
                }

                // Weight fluctuation detected → reset stability timer
                if (Math.Abs(w - _lastWeight) > WeightTolerance)
                {
                    _stableWeightStartTime = DateTime.Now;
                }
                else
                {
                    // Start stability timer if not started yet
                    _stableWeightStartTime ??= DateTime.Now;

                    // Check if weight stable for 5 seconds
                    if (!_isPalletDetectionRunning &&
                        (DateTime.Now - _stableWeightStartTime.Value).TotalSeconds >= 7)
                    {
                        if (_IsForkleffound == false)
                        {
                            _isPalletDetectionRunning = true;
                            await StartPalletDetectionProcAsync();
                        }
                    }
                }

                _lastWeight = w;
            });
        }

        private void Reader_Error(object? sender, Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                WeightText.Text = "ERR";
            });
        }
        private async Task TriggerHumanDetectedEffectAsync(bool isFound)
        {
            if (isFound)
            {
                await StartBuzzer();
                await Task.Delay(8000);
                //if (!_isAlertPlaying)
                //{
                //    _isAlertPlaying = true;
                //    audioManager.Play("Resources/Audio/warning.wav"); // Play warning sound
                //}

                // Play animation repeatedly for 4 seconds
                var startTime = DateTime.Now;
                //while ((DateTime.Now - startTime).TotalSeconds < 3)
                //{
                //    await AnimationHelper.ScaleToAsync(HumanImage, 1.2, 200, true);  // scale up
                //    await AnimationHelper.ScaleToAsync(HumanImage, 1.0, 200, false); // scale down
                //}

                //// Stop sound after 4 seconds
                //if (_isAlertPlaying)
                //{
                //    _isAlertPlaying = false;
                //    audioManager.Stop();
                //}
                await StopBuzzer();
            }
            else
            {
                if (_isAlertPlaying)
                {
                    _isAlertPlaying = false;
                    audioManager.Stop();
                }
            }
        }

        #endregion
    }
}