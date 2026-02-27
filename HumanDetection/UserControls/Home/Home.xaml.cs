
using ACGPUIO;
using Basler.Pylon;
using Dynamsoft.Core;
using Dynamsoft.CVR;
using Dynamsoft.License;
using Dynamsoft.Utility;
using HumanDetection.Model;
using HumanDetection.Utilites.Animation;
using HumanDetection.Utilites.Audio;
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
using System.Windows.Shapes;
using System.Windows.Threading;
using Tesseract;
using Utilites;
using Utilites.BoxCounting;
using Utilites.PythonScripts;
using Utilites.Weight;
using Yolov5Net.Scorer;
using Yolov5Net.Scorer.Models;

using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.Design.AxImporter;
using Color = SixLabors.ImageSharp.Color;
using FlipMode = OpenCvSharp.FlipMode;
using Font = SixLabors.Fonts.Font;
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
        private YoloScorer<YoloCustomModel> _scorerBoxCountingModel;
        private SixLabors.Fonts.Font _font;
        private IAudioManager audioManager;
        private bool _isAlertPlaying = false;
        private bool _isSidebarOpen = false;
        public  AccessController _ac;

        private const int TargetFPS = 8;
        private const int DetectionWidth = 640;
        private const int DetectionHeight = 360;
        private DispatcherTimer timer;
        private int remainingSeconds = 180;

        private CancellationTokenSource _processingCts;
        private bool _isProcessing = false;
        private CaptureVisionRouter? cvRouter;
        public ObservableCollection<BitmapImage> CapturedImages { get; set; }
        public ObservableCollection<ResutlModel> ResultDataList { get; set; }
        private ScaleSerialReader? _reader;
        private  OcrPythonClient _ocrHost;
        private bool _isPalletDetectionRunning = false;
        private CancellationTokenSource? _palletCts;
        public bool FirstTerm=true;
        public string pythonExe = @"C:\Users\Owner\AppData\Local\Programs\Python\Python310\python.exe";
        //public string pythonExe = @" C:\Users\USER\AppData\Local\Programs\Python\Python310\python.exe";
        private readonly SnackbarMessageQueue _messageQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(3));


        public Home()
        {
            InitializeComponent();
            ResultDialoag.CloseClicked += ResultDialog_CloseClicked;
            ResultDialoag.RestartProcessClicked += ResultDialog_RestartProcessClicked;
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
                if(_settings!=null)
               _ac =new AccessController($"{_settings.MoxIP}");
        
                ResultDialoag.Visibility = Visibility.Visible;

                ResultDataList = new ObservableCollection<ResutlModel>();

                // Show loading indicator
                await PrepareAllDevicesAndModels();
              
               

               
                snackbarMesssage.MessageQueue = _messageQueue; // Assign queue

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

            bool modelOk = await RunModelCheck( AiLoading,AiCheck, AiError, LoadModelsAsync);

            if (!modelOk)
            {
                ProgressTxt.Text = "AI Model loading failed!";
                //return; // stop further checks
            }
            await Task.Delay(500);
            bool weightOk = await RunDeviceCheck( WeightLoading, WeightCheck,WeightError, StartScaleAsync);

            if (!weightOk)
            {
                ProgressTxt.Text = "Weight scale not detected!";
                //return; // stop startup if critical
            }

            bool gpioOk = await RunDeviceCheck( GpioLoading, GpioCheck, GpioError, CheckGpioAsync);

            if (!gpioOk)
            {
                ProgressTxt.Text = "GPIO device not reachable!";
                //return;
            }
            else {
                await TurnOnRotatorAsync();
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



            
            LoadingOverlay.Visibility = Visibility.Collapsed;
            await Task.Delay(1000);
            _messageQueue.Enqueue("Process start manuly");
            await StartPalletDetectionProcAsync();
        }

        private async Task RunCheck( ProgressBar loader, TextBlock success,TextBlock error,int delay)
        {
            loader.Visibility = Visibility.Visible;
            success.Visibility = Visibility.Collapsed;
            error.Visibility = Visibility.Collapsed;

            await Task.Delay(delay); // replace with real device check

            bool ok = true; // replace with actual result

            loader.Visibility = Visibility.Collapsed;
            (ok ? success : error).Visibility = Visibility.Visible;
        }
        private async Task<bool> RunModelCheck( ProgressBar loader,TextBlock success, TextBlock error, Func<Task<bool>> action)
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
        private async Task<bool> RunDeviceCheck(ProgressBar loader,TextBlock success, TextBlock error, Func<Task<bool>> action)
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

                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = "ocr_api.py",
                    WorkingDirectory = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Assets"
                    ),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var flaskProcess = new Process
                {
                    StartInfo = psi,
                    EnableRaisingEvents = true
                };

                flaskProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        Debug.WriteLine("PY: " + e.Data);
                };

                flaskProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        Debug.WriteLine("PY ERR: " + e.Data);
                };

                flaskProcess.Start();
                flaskProcess.BeginOutputReadLine();
                flaskProcess.BeginErrorReadLine();

                // VERY IMPORTANT → Wait until API actually responds
                bool alive = await WaitForFlaskApiAsync();

                return alive;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Flask start error: " + ex.Message);
                return false;
            }
        }
        private async Task<bool> WaitForFlaskApiAsync()
        {
            using var http = new HttpClient();

            for (int i = 0; i < 30; i++) // wait max ~10 sec
            {
                try
                {
                    var resp = await http.GetAsync("http://127.0.0.1:5000/ocr");
                    if (resp.StatusCode==HttpStatusCode.MethodNotAllowed)
                        return true;
                }
                catch
                {
                    // ignore while starting
                }

                await Task.Delay(1000);
            }

            return false;
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
                        new YoloScorer<YoloCustomModel>(modelPathBoxCounting, sessionOptions);

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
            Dispatcher.Invoke(async () =>
            {
                EntryTimeTxt.Text = DateTime.Now.ToString("HH:mm:ss");
                
            });
        
            //// Enumerate all Basler cameras
            var allCameras = CameraFinder.Enumerate();
            int cameraCount = allCameras.Count;
            // Start tasks for each camera (up to 3)
            List<Task> cameraTasks = new List<Task>();

            if (cameraCount > 0)
                SetCameraUI(CamFrontLightPulse, cameraCount >= 1);
            await Task.Delay(200);

            cameraTasks.Add(Task.Run(() => CheckPalletStatus(allCameras[0])));

            if (cameraCount > 1)
                //cameraTasks.Add(Task.Run(() => ReadTextFromBaslerCamera(allCameras[1])));
                SetCameraUI(CamTopLightPulse, cameraCount >= 1);


            if (cameraCount > 2)
                SetCameraUI(CamRightLightPulse, cameraCount >= 1);

            await Task.WhenAll(cameraTasks);
           



        }
        public async Task StopPalletDetectionProc()
        {
            //await StopBuzzer();
            await OffBlower();
            await OffRotatorAsync();
            Dispatcher.Invoke(async () =>
            {
                
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
        private async Task CheckPalletStatus(ICameraInfo cameraInfo)
        {
            try
            {
                bool palletAligned = false;
                _messageQueue.Enqueue($"Checking Pallet alignment");
                while (!palletAligned)
                {
                    var image = await CaptureSingleFrameFromCameraAsync(cameraInfo);
                    Image<Rgba32> convertedImage = BitmapImageToImageSharp(image);
                    ImagePredictionResult result = await RunBoxCountingModelAsync(convertedImage, PalletSide.Front);

                    // Check pallet angle
                    palletAligned = result.PalletAngleDeg > 0;

                    if (!palletAligned)
                    {
                        _messageQueue.Enqueue($"Pallet misaligned");

                        // Restart rotator
                        await TurnOnRotatorAsync();
                        await Task.Delay(5000); // Wait for rotator to stabilize
                        await StartRoutatorWithDuration(2000);

                        // Optionally, small delay before taking next picture
                        await Task.Delay(500);
                    }
                    else
                    {
                        _messageQueue.Enqueue("Pallet angle OK");
                    }
                }

                // Pallet aligned → continue normal workflow
                //await Dispatcher.InvokeAsync(async () =>
                //{
                //    ShowPalletFromLeft();
                //    await Task.Delay(100);
                //    await PalletDetectedSoundStart();
                //    await Task.Delay(100);
                //    await CaptureAndDisplayAllCamerasAsync();
                //});
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


        //Catpure Image form all Camers
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
                    var obj = new ResutlModel {
                    StartTime = DateTime.Now,
                    EndTime=null,
                    TotalBoxes=0,
                     BarcodeCodeCount=0,
                      DublicateBarcode=null,
                       ExpiryDate=null,
                        HumanDetect=null,
                         OCRResult=null,
                          PalletHeight=0,
                          Score=0,
                           SupplierName=null,
                            TotalWeight=null
                    };
                    AddResult(obj, true);

                    ReportProgress("📷 Capturing images...");
                    var cameraList = CameraFinder.Enumerate();

                    // Capture images (MAIN THREAD)
                    var images = await CaptureSingleFrameFromAllCamerasAsync(cameraList, true);

                    Dispatcher.Invoke(() =>
                    {
                        CapturedImages.Clear();
                        foreach (var img in images)
                            CapturedImages.Add(img);
                    });

                    ReportProgress("🧠 Running AI + OCR in parallel...");

                    // 🔥 RUN BOTH TASKS IN PARALLEL
                    var aiTask = Task.Run(() =>
                        RunAllAIDetectionsAsync(images)
                    );

                    StartCountdown();
                    await Task.WhenAll(aiTask);
                    var aiResult = aiTask.Result;
                    double avgScore = aiResult.AvScore;
                    bool humanDetected = aiResult.HumanDetected;
                    int numberOfBox = aiResult.NumberOfBox;
                   ScoreTxt.Text = $"{avgScore * 100:0}%";
                    LoadingOverlay.Visibility = Visibility.Collapsed;

                    _messageQueue.Enqueue("Please wait Calculate result..");

                   
                    LoadingCard.Visibility = Visibility.Visible;
                    if (humanDetected == true)
                    {
                        HumanDetectBox.Background = System.Windows.Media.Brushes.Red;
                      
                        await StartBuzzlerWithDuration(6000, 1);
                        await PlayAlertForSystem();
                        HumanDetectedTxt.Text = "Yes";
                    }
                    var cropByteList = aiResult.OCRBytes;
                    List<OcrImageResult> ocrResultList = new List<OcrImageResult>();

                    if (cropByteList.Any())
                    {
                        var api = await RunOcrAsync(cropByteList);

                        if (api?.results != null)
                        {
                            var valid = api.results
                                .Where(x => !string.IsNullOrWhiteSpace(x.Key))          // skip empty filename
                                .Where(x => string.IsNullOrWhiteSpace(x.Value?.error))  // skip error images
                                .Select(x => x.Value);

                            ocrResultList.AddRange(valid);
                        }
                    }
                    LoadingCard.Visibility = Visibility.Collapsed;
                    StopCountdown();

                    if (aiResult.maxPalletHeight>1.7)
                    {
                        HeightBox.Background = System.Windows.Media.Brushes.Red;
                        await StartBuzzer();
                       await PlayAlertForSystem();


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

                    int distinctDates = ocrResultList
                            .Where(r => r?.dates != null)
                            .SelectMany(r => r.dates)
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .Distinct()
                            .Count();

                    if (distinctDates >= 3)
                    {
                        await StartBuzzlerWithDuration(6000, 1);
                        DateExireBox.Background = System.Windows.Media.Brushes.Red;
                        DateExpireTxt.Text = "Yes";
                        await PlayAlertForSystem();
                    }

                    string OCRResultInString = "";

                    foreach (var q in ocrResultList)
                    {
                        OCRResultInString += OcrResultToString(q) + Environment.NewLine;
                    }
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
                            BarcodeCodeCount = 0,
                            DublicateBarcode = null,
                            ExpiryDate = DateExpireTxt.Text,
                            HumanDetect = humanDetected.ToString(),
                            OCRResult = OCRResultInString,
                            PalletHeight = aiResult.maxPalletHeight,
                            Score = avgScore,
                            SupplierName = null,
                            TotalWeight = WeightText.Text

                        };
                        AddResult(obj, false);
                        // OCR result available here
                       

                    });
                    string ResultTooPost =  $"AvgScore {avgScore} TotalWight {WeightText.Text} OCRResponse:{OCRResultInString}";
                    if (avgScore >= 0.60)
                    {
                        detectionPassed = true;
                        var request = new ResultRequestModel
                        {
                            ResutlModelList = ResultDataList.ToList(),
                        };
                        _messageQueue.Enqueue("70% score found..  result are saved");
                        // 🔥 POST TO API
                        await PostDetectionRequestAsync(ResultTooPost);
                        await StopPalletDetectionProc();
                        await ResetRecords();
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
        private async Task PostDetectionRequestAsync(string request)
        {
            try
            {
                LoadingCard.Visibility = Visibility.Visible;

                // Payload exactly like API expects
                var payload = new
                {
                    Name = request,
                   
                    Image = "https://adp-backend-demo.ashybay-437ca219.uaenorth.azurecontainerapps.io/core/uploads/image-1769680856546.png"
                };

                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(120)
                };

                // ✅ ADD HEADERS
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer",
                        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJwYXlsb2FkIjp7ImVtYWlsIjoia2xwMjF1c2VyQGdtYWlsLmNvbSIsInVzZXJOYW1lIjoia2xwMjEiLCJfaWQiOiI2NmI1YmM3Nzc1MDA5M2U0MWU1ODNiZTYifSwiaWF0IjoxNzY2NzU2NDQwfQ.lzAMd9HXsj-18U9TMfOij5OF8bUIkMosUYxl1rgM-pE"
                    );

                client.DefaultRequestHeaders.Add(
                    "x-api-key",
                    "99927ec1-8668-45ae-8709-2db03366e680"
                );

                var json = System.Text.Json.JsonSerializer.Serialize(
                    payload,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(
                    "https://adp-backend-demo.ashybay-437ca219.uaenorth.azurecontainerapps.io/core/thing-type/66b9a073b241574cd76f0616/adpPallet",
                    content);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"API Error: {response.StatusCode} - {error}");
                }

                _messageQueue.Enqueue("Saved Successfully");
                LoadingCard.Visibility = Visibility.Collapsed;
                await PlayAlertForSystem();
                //await StartBuzzlerWithDuration(6000, 3);
                await ResetRecords();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ API upload failed: {ex.Message}");
                LoadingCard.Visibility = Visibility.Collapsed;
            }
        }
        public async Task<List<BitmapImage>> CaptureSingleFrameFromAllCamerasAsync(List<ICameraInfo> cameraInfos, bool FirstTerm)
        {
            var capturedImages = new List<BitmapImage>();

            await Task.Run(async () =>
            {
                foreach (var camInfo in cameraInfos)
                {
                    try
                    {
                        using (var camera = new Camera(camInfo))
                        {
                            camera.CameraOpened += Basler.Pylon.Configuration.AcquireSingleFrame;
                            camera.Open();

                            FlashCamera(FrontFlashEllipse);
                            FlashCamera(LeftFlashEllipse);
                            FlashCamera(RightFlashEllipse);
                            FlashCamera(TopFlashEllipse);
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

                                    capturedImages.Add(bitmapImage);
                                    Dispatcher.Invoke(() =>
                                    {
                                        PaletImage.Source = bitmapImage;
                                    });
                                }
                            }

                            camera.Close();
                            Console.WriteLine($"✅ Captured one frame from {camInfo[CameraInfoKey.ModelName]}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Failed to capture from {camInfo[CameraInfoKey.ModelName]}: {ex.Message}");
                    }
                }

                // 🔹 EXTRA CAPTURE WHEN FirstTerm == false
                if (FirstTerm && cameraInfos.Count > 0)
                {

                    try
                    {
                        //await StartRoutatorWithDuration(_settings.RoutatorTimer!=null?(int)_settings.RoutatorTimer:20);
                        for (int i = 0; i < 3; i++)
                        {
                            await TurnOnRotatorAsync();
                            await Task.Delay(5000);
                            await StartRoutatorWithDuration(5200); //last was 5200 sec
                            var firstCamInfo = cameraInfos[0];

                            using (var camera = new Camera(firstCamInfo))
                            {
                                camera.CameraOpened += Basler.Pylon.Configuration.AcquireSingleFrame;
                                camera.Open();

                                FlashCamera(FrontFlashEllipse);
                                PlayShutterSound();

                                Task.Delay(100).Wait();

                                using (IGrabResult grabResult =
                                    camera.StreamGrabber.GrabOne(8000, TimeoutHandling.ThrowException))
                                {
                                    if (grabResult.GrabSucceeded)
                                    {
                                        using Mat frame = GrabResultToMat(grabResult);
                                        var bmp = frame.ToBitmap();
                                        var bitmapImage = ConvertBitmapToImageSource(bmp);
                                        bitmapImage.Freeze();

                                        capturedImages.Add(bitmapImage);
                                    }
                                }

                                camera.Close();
                                Console.WriteLine("📸 Extra image captured from first camera (index 0)");
                            }
                            await OffRotatorAsync();
                        }

                        //reset position of pallet
                        await TurnOnRotatorAsync();
                        await Task.Delay(5000);
                        await StartRoutatorWithDuration(5200);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Failed extra capture from first camera: {ex.Message}");
                    }
                }
            });

            return capturedImages;
        }
        public async Task<BitmapImage?> CaptureSingleFrameFromCameraAsync(ICameraInfo cameraInfo)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var camera = new Camera(cameraInfo))
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

        private async Task<(double AvScore, bool HumanDetected, int NumberOfBox, List<byte[]> OCRBytes, double maxPalletHeight)>
RunAllAIDetectionsAsync(List<BitmapImage> capturedImages)
        {
            UpdateProgressStatus("AI Model Start to detect");

            if (capturedImages == null || capturedImages.Count < 1)
            {
                MessageBox.Show("❌ Not enough images to process AI models.");
                return (0.0, false, 0, null, 0);
            }

            int NumberOfBox = 0;
            double maxPalletHeight = 0.0;
            double totalAvgScore = 0.0;
            int avgScoreCount = 0;
            bool HumanDetected = false;

            // Convert BitmapImages to ImageSharp
            var imageSharpList = capturedImages
                                    .Select(img => BitmapImageToImageSharp(img))
                                    .ToList();

            List<byte[]> ocrResults = new();

            // Store predictions per side
            List<YoloPrediction> frontBoxes = new();
            List<YoloPrediction> rightBoxes = new();
            List<YoloPrediction> backBoxes = new();
            List<YoloPrediction> leftBoxes = new();

            PalletSide[] captureOrder =
            {
        PalletSide.Front,
        PalletSide.Right,
        PalletSide.Back,
        PalletSide.Left
    };

            for (int i = 0; i < imageSharpList.Count && i < captureOrder.Length; i++)
            {
                ReportProgress($"🧠 AI Processing {i + 1}/{imageSharpList.Count}");

                var image = imageSharpList[i].Clone();
                var currentSide = captureOrder[i];

                var boxTask = RunBoxCountingModelAsync(image, currentSide);
                var humanTask = Task.Run(() => RunHumanDetectionModel(image));

                await Task.WhenAll(boxTask, humanTask);

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

                // ✅ Store predictions by side
                switch (currentSide)
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
                }

                image.Dispose();
            }

            // ✅ Count boxes properly
            NumberOfBox = BoxCountingService.CountBox(
                frontBoxes,
                rightBoxes,
                backBoxes,
                leftBoxes
            );

            // ✅ Dispose original ImageSharp images
            foreach (var img in imageSharpList)
                img.Dispose();

            // Final average score
            double finalAverageScore =
                avgScoreCount > 0 ? totalAvgScore / avgScoreCount : 0.0;

            // ✅ Update UI safely
            Dispatcher.Invoke(() =>
            {
                NoBoxTxt.Text = $"{NumberOfBox}";
                PalletHeightTxt.Text = $"{maxPalletHeight:F2} m";
            });

            return (finalAverageScore, HumanDetected, NumberOfBox, ocrResults, maxPalletHeight);
        }


        private void ReportProgress(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressTxt.Text = message;
            });
        }

        private async Task<ImagePredictionResult> RunBoxCountingModelAsync( Image<Rgba32> originalImage, PalletSide side)

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
            double confidenceThreshold = 0.60;
            if (_settings != null &&
                !string.IsNullOrWhiteSpace(_settings.ConfidenceLevel) &&
                double.TryParse(_settings.ConfidenceLevel, out var dbValue))
            {
                confidenceThreshold = dbValue;
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

            // Keep all boxes + 1 pallet
            var predictions = rawPredictions
                .Where(p => !p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase))
                .ToList();

           

            //if (tallestPallet != null)
            //    predictions.Add(tallestPallet);

            // ----------------------------------------------------
            // 5. Count boxes & pallet height
            // ----------------------------------------------------
            int boxCount = rawPredictions.Count(p =>
                p.Label.Name.Equals("box", StringComparison.OrdinalIgnoreCase));

            double palletHeightMeters = 0.0;
            if (tallestPallet != null && tallestPallet.Score>0.70)
            {
                double heightPixels = tallestPallet.Rectangle.Height; // already pixels

                double mmPerPixel = 2.50; // your calibration at reference distance

                double heightMm = heightPixels * mmPerPixel;
                palletHeightMeters = heightMm / 1000.0;
            }

            // ----------------------------------------------------
            // 6. Average confidence (box + pallet)
            // ----------------------------------------------------
            double averageScore = 0.0;
            var boxPredictions = predictions
                .Where(p => p.Label.Name.Equals("box", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (boxPredictions.Any() && tallestPallet != null)
            {
                double boxAvg = boxPredictions.Average(p => p.Score);
                averageScore = (boxAvg + tallestPallet.Score) / 2.0;
            }

            // ----------------------------------------------------
            // 7. Crop boxes/pallet from original image & convert to byte[]
            // ----------------------------------------------------
            float scaleX = (float)originalImage.Width / modelWidth;
            float scaleY = (float)originalImage.Height / modelHeight;

            var cropByteList = new List<byte[]>();

            foreach (var p in predictions)
            {
                // Scale and round to avoid floating point issues
                int x = (int)Math.Round(p.Rectangle.X * scaleX);
                int y = (int)Math.Round(p.Rectangle.Y * scaleY);
                int width = (int)Math.Round(p.Rectangle.Width * scaleX);
                int height = (int)Math.Round(p.Rectangle.Height * scaleY);

                // Skip invalid or tiny boxes
                if (width <= 0 || height <= 0) continue;

                // Make square crop based on the larger dimension
                int squareSize = Math.Max(width, height);

                // Ensure squareSize does not exceed image bounds
                squareSize = Math.Min(squareSize, originalImage.Width);
                squareSize = Math.Min(squareSize, originalImage.Height);

                // Center vertically
                int newY = y - (squareSize - height) / 2;
                int newX = x - (squareSize - width) / 2;

                // Clamp coordinates inside image bounds
                newX = Math.Max(0, Math.Min(newX, originalImage.Width - squareSize));
                newY = Math.Max(0, Math.Min(newY, originalImage.Height - squareSize));

                int newWidth = Math.Min(squareSize, originalImage.Width - newX);
                int newHeight = Math.Min(squareSize, originalImage.Height - newY);

                // Skip any invalid crops
                if (newWidth <= 0 || newHeight <= 0) continue;

                var cropRect = new SixLabors.ImageSharp.Rectangle(newX, newY, newWidth, newHeight);

                using var crop = originalImage.Clone(ctx => ctx.Crop(cropRect));

                using var ms = new MemoryStream();
                // Create unique temp file name
                //string tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BoxCrops");

                //// Ensure folder exists
                //Directory.CreateDirectory(tempFolder);

                //string fileName = $"crop_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.jpg";
                //string filePath = System.IO.Path.Combine(tempFolder, fileName);

                // Save to memory (existing logic)
                //using var ms = new MemoryStream();
                //crop.SaveAsJpeg(ms);
                crop.SaveAsJpeg(ms); // OCR works well with JPEG
                cropByteList.Add(ms.ToArray());

                //ms.Position = 0;
                //using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                //{
                //    ms.CopyTo(fs);
                //}
            }


            double palletAngleDeg = 0.0;
            OcrResult ocrResult = null;
            string combinedOcrText = "";


            // ----------------------------------------------------
            // 9. Draw detection boxes on resized image for UI
            // ----------------------------------------------------
            using (var annotated = resizedImage.Clone())
            {
                var colorBox = new Rgba32(0, 255, 0);
                var colorPallet = new Rgba32(255, 64, 64);
                var font = SixLabors.Fonts.SystemFonts.CreateFont("Arial", 14, SixLabors.Fonts.FontStyle.Bold);

                // 1️⃣ Find the largest pallet (by area)
                var largestPallet = rawPredictions
                    .Where(p => p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.Rectangle.Width * p.Rectangle.Height)
                    .FirstOrDefault();

                // 2️⃣ Draw predictions
                foreach (var p in rawPredictions)
                {
                    bool isPallet = p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase);

                    // ❌ Skip smaller pallets
                    if (isPallet && p != largestPallet)
                        continue;

                    var color = isPallet ? colorPallet : colorBox;

                    annotated.Mutate(x =>
                    {
                        x.Draw(color, 6, p.Rectangle);

                        var labelText = $"{p.Label.Name} {p.Score:P1}";
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

                        x.Fill(Color.FromRgba(0, 0, 0, 180), textBgRect);
                        x.DrawText(labelText, font, color, textLocation);
                    });
                }



                if (averageScore != null && averageScore < 0.70)
                {
                    //float scaleX1 = (float)originalImage.Width / modelWidth;
                    //float scaleY1 = (float)originalImage.Height / modelHeight;

                    //int px = (int)(tallestPallet.Rectangle.X * scaleX1);
                    //int py = (int)(tallestPallet.Rectangle.Y * scaleY1);
                    //int pw = (int)(tallestPallet.Rectangle.Width * scaleX1);
                    //int ph = (int)(tallestPallet.Rectangle.Height * scaleY1);

                    //var palletRect = new SixLabors.ImageSharp.Rectangle(px, py, pw, ph);

                    //using var palletCrop = originalImage.Clone(x => x.Crop(palletRect));

                    palletAngleDeg = -1;
                }
                else {
                    palletAngleDeg = 1;
                }

                using (var ms = new MemoryStream())
                {
                    annotated.SaveAsPng(ms);
                    ms.Seek(0, SeekOrigin.Begin);

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = ms;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    App.Current.Dispatcher.Invoke(() => CapturedImages.Add(bitmap));
                }
            }

            return new ImagePredictionResult
            {
                Side = side,
                BoxPredictions = boxPredictions,
                PalletHeightMeters = palletHeightMeters,
                AverageScore = averageScore,
                PalletAngleDeg = palletAngleDeg,
                BoxesImages=cropByteList
            };

        }

        private double CalculatePalletAngle(Image<Rgba32> palletImage)
        {
            using var ms = new MemoryStream();
            palletImage.SaveAsBmp(ms);
            byte[] imageBytes = ms.ToArray();

            Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Grayscale);

            Cv2.GaussianBlur(mat, mat, new OpenCvSharp.Size(5, 5), 0);

            Mat edges = new();
            Cv2.Canny(mat, edges, 50, 150);

            var lines = Cv2.HoughLinesP(
                edges,
                1,
                Math.PI / 180,
                100,
                minLineLength: mat.Width * 0.5,
                maxLineGap: 10
            );

            if (lines == null || lines.Length == 0)
                return 0;

            List<double> angles = new();

            foreach (var line in lines)
            {
                double angle = Math.Atan2(
                    line.P2.Y - line.P1.Y,
                    line.P2.X - line.P1.X
                ) * 180.0 / Math.PI;

                if (Math.Abs(angle) < 45)
                    angles.Add(angle);
            }

            return angles.Count > 0 ? angles.Median() : 0;
        }


        private bool RunHumanDetectionModel(Image<Rgba32> image)
        {
            UpdateProgressStatus("Human Detection AI Model Start");
            var predictions = _scorerHumanModel.Predict(image);

            bool humanDetected = predictions.Any(p =>
                p.Label.Name.Equals("person", StringComparison.OrdinalIgnoreCase) ||
                p.Label.Name.Equals("human", StringComparison.OrdinalIgnoreCase) ||
                p.Label.Name.Equals("man", StringComparison.OrdinalIgnoreCase) ||
                p.Label.Name.Equals("woman", StringComparison.OrdinalIgnoreCase));

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

                if (input.Score.HasValue)
                    resultModel.Score = input.Score;

                if (input.StartTime.HasValue)
                    resultModel.StartTime = input.StartTime;

                if (input.EndTime.HasValue)
                    resultModel.EndTime = input.EndTime;

                if (!string.IsNullOrWhiteSpace(input.HumanDetect))
                    resultModel.HumanDetect = input.HumanDetect;
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
                ImageDialogHost.IsOpen = true; // ✅ show popup
                PictureDialog.Visibility = Visibility.Visible;

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
        public async Task RestartProcess()
        {
            Dispatcher.Invoke(async () =>
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
                SetCameraUI(CamFrontLightPulse, cameraCount >= 1);
                SetCameraUI(CamTopLightPulse, cameraCount >= 2);
                CheckPalletStatus(allCameras[0]);
            }
            else {
                MessageBox.Show("No Camera found");
            }
                
        }
        public async Task ResetRecords()
        {
            Dispatcher.Invoke(async () =>
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

        private void Timer_Tick(object sender, EventArgs e)
        {
            remainingSeconds--;

            if (remainingSeconds >= 0)
            {
                CountdownText.Text = $"{remainingSeconds} Sec";
            }

            if (remainingSeconds <= 0)
            {
                StopCountdown();
            }
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

        #endregion

        // Create ONE shared controller for the whole class
       
        public async Task StartBuzzer()
        {
            await _ac.StartBuzzerAsync();
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
        public async Task TurnOnRotatorAsync()
        {
            await _ac.TurnOnRotatorAsync();

        }
        public async Task RebootDeviceAsync()
        { 
            await _ac.RebootDeviceAsync();
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
                        WeightText.Text = "ERR";
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
                        (DateTime.Now - _stableWeightStartTime.Value).TotalSeconds >= 5)
                    {
                        _isPalletDetectionRunning = true;
                        await StartPalletDetectionProcAsync();
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
