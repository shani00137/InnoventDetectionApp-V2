
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
using Model;
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
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tesseract;
using Yolov5Net.Scorer;
using Yolov5Net.Scorer.Models;

using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.Design.AxImporter;
using Color = SixLabors.ImageSharp.Color;
using FlipMode = OpenCvSharp.FlipMode;
using Font = SixLabors.Fonts.Font;
using Point = SixLabors.ImageSharp.PointF;
using Size = SixLabors.ImageSharp.Size;





namespace HumanDetection
{
    public partial class MainWindow : System.Windows.Window
    {
        private VideoCapture _capture;
   
        private Mat _frame;
        private bool _isRunning = true;
        private bool _isRunning2 = true;
        
        private YoloScorer<YoloCocoP5Model> _scorerHumanModel;
        private YoloScorer<YoloCustomModel> _scorerBoxCountingModel;
        private SixLabors.Fonts.Font _font;
        private IAudioManager audioManager;
        private bool _isAlertPlaying = false;
        private bool _isSidebarOpen = false;
      
        private const int TargetFPS = 8;
        private const int DetectionWidth = 640;
        private const int DetectionHeight = 360;
        private DispatcherTimer timer;
        private int remainingSeconds = 60;
       
        private CancellationTokenSource _processingCts;
        private bool _isProcessing = false;
        private CaptureVisionRouter? cvRouter;
        public ObservableCollection<BitmapImage> CapturedImages { get; set; }
        public ObservableCollection<ResutlModel> ResultDataList { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            MaximizeRestoreButton_Click(null, null);
            ResultDialoag.CloseClicked += ResultDialog_CloseClicked;
            ResultDialoag.RestartProcessClicked += ResultDialog_RestartProcessClicked;
            SetIndicator(SensorIndicator, true);        // Sensor ON
            SetIndicator(TemperatureIndicator, false);  // Temperature OK
            SetIndicator(HumidityIndicator, false);      // Humidity ON
            SetIndicator(MotorIndicator, false);
            Loaded += MainWindow_LoadedAsync;  // Changed to async
        Closed += (s, e) => _isRunning = false;
          
            audioManager = new AudioManager();
            CapturedImages = new ObservableCollection<BitmapImage>();
            DataContext = this;

            //  LoadCamera();

        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
          
            _frame = new Mat();
            LoadModels();
           
        }
        private async void MainWindow_LoadedAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                ResultDataList=new ObservableCollection<ResutlModel> { new ResutlModel() };
                // Show loading indicator
                LoadingOverlay.Visibility = Visibility.Visible;
                await Task.Delay(1); // Ensure UI updates

                await Task.Run(() =>
                {
                    _capture = new VideoCapture(0);
                    _frame = new Mat();
                    LoadModels();
                });

                // Enumerate all Basler cameras
                var allCameras = CameraFinder.Enumerate();
                int cameraCount = allCameras.Count;

                if (cameraCount == 0)
                {
                    MessageBox.Show("No Basler cameras detected.", "Camera Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                else if (cameraCount < 3)
                {
                    MessageBox.Show($"Only {cameraCount} camera(s) detected. Please connect all 3 cameras.",
                                    "Camera Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    // Optionally return or continue with available cameras
                    // return;
                }

                Console.WriteLine($"Detected {cameraCount} Basler camera(s).");

                LoadingOverlay.Visibility = Visibility.Collapsed;

                // Start tasks for each camera (up to 3)
                List<Task> cameraTasks = new List<Task>();

                if (cameraCount > 0)
                    SetCameraUI(CamFrontLightPulse, cameraCount >= 1);
                await Task.Delay(1000);
               
                cameraTasks.Add(Task.Run(() => CheckPalletStatus(allCameras[0])));

                if (cameraCount > 1)
                    //cameraTasks.Add(Task.Run(() => ReadTextFromBaslerCamera(allCameras[1])));
                    SetCameraUI(CamLeftLightPulse, cameraCount >= 1);
                

                    if (cameraCount > 2)
                    SetCameraUI(CamRightLightPulse, cameraCount >= 1);

                await Task.WhenAll(cameraTasks);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Initialization failed: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void LoadModels()
        {
            var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions();
            try
            {
                sessionOptions.AppendExecutionProvider_DML();
                // Other settings...
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DirectML failed: {ex.Message}");
                sessionOptions.AppendExecutionProvider_CPU(); // Fallback to CPU
            }
            var providers = OrtEnv.Instance().GetAvailableProviders();
            Console.WriteLine("Available providers: " + string.Join(", ", providers));
            var modelPathBoxCounting = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/Weights/customBoxCount.onnx");
            _scorerBoxCountingModel = new YoloScorer<YoloCustomModel>(modelPathBoxCounting, sessionOptions);

            var modelPathHumanDetection = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/Weights/yolov5s.onnx");
            _scorerHumanModel = new YoloScorer<YoloCocoP5Model>(modelPathHumanDetection, sessionOptions);


            var fontPath = "C:/Windows/Fonts/consola.ttf";
            _font = new SixLabors.Fonts.Font(new FontCollection().Add(fontPath), 16);



        }


        private ObservableCollection<OcrFrameResult> _ocrResults = new ObservableCollection<OcrFrameResult>();

        private CancellationTokenSource _ocrCancellationTokenSource;

        #region AIModel Detection
        private async void CheckPalletStatus(ICameraInfo cameraInfo)
        {
            try
            {
                _processingCts = new CancellationTokenSource();
                _isProcessing = true;

                using (Camera camera = new Camera(cameraInfo))
                {
                    camera.CameraOpened += Basler.Pylon.Configuration.AcquireContinuous;
                    camera.Open();
                    camera.Parameters[PLCameraInstance.MaxNumBuffer].SetValue(50);
                    camera.StreamGrabber.Start();

                    Console.WriteLine($"Camera started: {camera.CameraInfo[CameraInfoKey.ModelName]}");

                    bool palletDetected = false;

                    while (!_processingCts.Token.IsCancellationRequested && !palletDetected)
                    {
                        try
                        {
                            FlashCamera(FrontFlashEllipse);

                            using IGrabResult grabResult = camera.StreamGrabber.RetrieveResult(5000, TimeoutHandling.ThrowException);
                            if (!grabResult.GrabSucceeded)
                                continue;

                            // Convert frame to ImageSharp
                            using Mat frame = GrabResultToMat(grabResult);
                            using var ms = new MemoryStream();
                            frame.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                            ms.Position = 0;

                            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);
                            var predictions = _scorerBoxCountingModel.Predict(image)
                                .Where(p => p.Score >= 0.30f)
                                .ToList();

                            if (predictions.Any(p => p.Label.Name.Equals("box", StringComparison.OrdinalIgnoreCase)))
                            {
                                palletDetected = true;

                                // UI updates must happen on main thread
                                await Dispatcher.InvokeAsync(async () =>
                                {
                                    ShowPalletFromLeft();
                                    await Task.Delay(500);
                                    await PalletDetectedSoundStart();
                                    await Task.Delay(500);
                                    await CaptureAndDisplayAllCamerasAsync();
                                    AddResult(DateTime.Now,null ,null , null, null,null);
                                });

                                Console.WriteLine("✅ Pallet detected! Stopping camera...");
                            }
                        }
                        catch (TimeoutException)
                        {
                            Console.WriteLine("[Camera] Timeout — continuing...");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Camera] Frame error: {ex.Message}");
                        }
                    }

                    // Stop camera safely
                    camera.StreamGrabber.Stop();
                    camera.Close();

                    Console.WriteLine("[Camera] Stream stopped and camera closed.");
                }
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

        private void AddResult(DateTime? startDate = null, DateTime? endDate = null, int? boxes = null, double? palletHeight = null, double? weight = null, string remarks = null)
        {
            // Find the first existing result
            var resultModel = ResultDataList.FirstOrDefault();

            if (resultModel != null)
            {
                // Update only provided fields
                if (boxes.HasValue)
                    resultModel.TotalBoxes = boxes.Value;

                if (palletHeight.HasValue)
                    resultModel.PalletHeight = palletHeight.Value;

                if (weight.HasValue)
                    resultModel.TotalWeight = weight.Value;

                if (!string.IsNullOrWhiteSpace(remarks))
                    resultModel.OCRResult = remarks;

                if (startDate.HasValue)
                    resultModel.StartTime = startDate.Value;

                if (endDate.HasValue)
                    resultModel.EndTime = endDate.Value;
            }
            else
            {
                // Add a new record if none exists
                ResultDataList.Add(new ResutlModel
                {
                    StartTime = startDate ?? DateTime.Now,
                    EndTime = endDate ?? DateTime.Now,
                    TotalBoxes = boxes ?? 0,
                    PalletHeight = palletHeight ?? 0,
                    TotalWeight = weight ?? 0,
                    OCRResult = remarks ?? string.Empty
                });
            }
        }




        private async Task CaptureAndDisplayAllCamerasAsync()
        {
            var cameraList  = CameraFinder.Enumerate();

            var images = await CaptureSingleFrameFromAllCamerasAsync(cameraList);

            Dispatcher.Invoke(() =>
            {
                CapturedImages.Clear();
                foreach (var img in images)
                    CapturedImages.Add(img);
            });
            LoadingOverlay.Visibility = Visibility.Visible;
            ProgressTxt.Text = "Please Wait.."
;           await RunAllAIDetectionsAsync(images);
            LoadingOverlay.Visibility = Visibility.Collapsed;


            ImageDialogHost.IsOpen = true; // ✅ show popup
            PictureDialog.Visibility = Visibility.Collapsed;
            ResultDialoag.Visibility = Visibility.Visible;

        }
        public async Task<List<BitmapImage>> CaptureSingleFrameFromAllCamerasAsync(List<ICameraInfo> cameraInfos)
        {
            var capturedImages = new List<BitmapImage>();

            await Task.Run(() =>
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
                            PlayShutterSound();
                             Task.Delay(500);
                            using (IGrabResult grabResult = camera.StreamGrabber.GrabOne(3000, TimeoutHandling.ThrowException))
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
                            Console.WriteLine($"✅ Captured one frame from {camInfo[CameraInfoKey.ModelName]}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Failed to capture from {camInfo[CameraInfoKey.ModelName]}: {ex.Message}");
                    }
                }
            });

            return capturedImages;
        }
        private async Task RunAllAIDetectionsAsync(List<BitmapImage> capturedImages)
        {
            if (capturedImages == null || capturedImages.Count < 3)
            {
                Console.WriteLine("❌ Not enough images to process AI models.");
                return;
            }
            int NumberOfBox = 0;
            double PalletHeight = 0;
            string Result
            // Convert BitmapImages to ImageSharp format for inference
            var imageSharpList = capturedImages.Select(img => BitmapImageToImageSharp(img)).ToList();
            for (int i = 0; i < imageSharpList.Count; i++)
            {
                // 🧩 Parallel run — each model runs independently
                var boxTask = Task.Run(() => RunBoxCountingModel(imageSharpList[i]));
                var humanTask = Task.Run(() => RunHumanDetectionModel(imageSharpList[i]));
                var ocrTask = Task.Run(() => RunOCRModel(imageSharpList[i]));

                await Task.WhenAll(boxTask, humanTask, ocrTask);

                var boxResult = boxTask.Result;
                var humanResult = humanTask.Result;
                var ocrResult = ocrTask.Result;
                NumberOfBox += boxResult.BoxesDetected;
                PalletHeight= boxResult.PalletHeight;
            }


          
            // 🔔 Update UI with combined results
            Dispatcher.Invoke(() =>
            {
                NoBoxTxt.Text = $"Boxes: {NumberOfBox}";
                PalletHeightTxt.Text = $" {PalletHeight:F2} m";
                AddResult(null, DateTime.Now, NumberOfBox, PalletHeight, 0, ocrResult.ExtractedText);
                //HumanDetectionStatusText.Text = humanResult.HumanDetected ? "✅ Human Detected" : "❌ No Human";
                //TextExtractionStatusText.Text = $"OCR: {ocrResult.ExtractedText}";
            });
        }
        private (int BoxesDetected, double PalletHeight) RunBoxCountingModel(Image<Rgba32> image)
        {
            UpdateProgressStatus("Box Counting AI Model Start");
            // ✅ Step 1: Preprocess image (resize to 640x640 YOLOv5 input)
            using var resizedImage = image.Clone(ctx =>
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size(640, 640),
                  
                    PadColor = Color.Black
                }));

            // ✅ Step 2: Run YOLOv5 ONNX model inference
            var predictions = _scorerBoxCountingModel.Predict(resizedImage)
                .Where(p => p.Score >= 0.30f)
                .ToList();

            int boxCount = predictions.Count(p => p.Label.Name.Equals("box", StringComparison.OrdinalIgnoreCase));
            var pallet = predictions.FirstOrDefault(p => p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase));

            double palletHeightMeters = 0.0;
            if (pallet != null)
            {
                int heightPixels = (int)(pallet.Rectangle.Bottom - pallet.Rectangle.Top);
                double mmPerPixel = 2.0;
                palletHeightMeters = (heightPixels * mmPerPixel) / 1000.0;
            }

            // ✅ Step 3: Draw detection boxes on image
            using (var annotated = resizedImage.Clone())
            {
                var colorBox = new Rgba32(0, 255, 0);    // Green for box
                var colorPallet = new Rgba32(255, 64, 64); // Soft red for pallet
                var font = SixLabors.Fonts.SystemFonts.CreateFont("Arial", 14, SixLabors.Fonts.FontStyle.Bold);

                foreach (var p in predictions)
                {
                    var color = p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase) ? colorPallet : colorBox;

                    annotated.Mutate(x =>
                    {
                        // ✅ Draw bold box (thicker)
                        x.Draw(color, 6, p.Rectangle);

                        // ✅ Label with background rectangle for readability
                        var labelText = $"{p.Label.Name} {p.Score:P1}";
                        var textLocation = new SixLabors.ImageSharp.PointF(p.Rectangle.X + 5, p.Rectangle.Y - 25);
                        var textBgRect = new SixLabors.ImageSharp.RectangleF(
                            textLocation.X - 3, textLocation.Y - 3,
                            labelText.Length * 9, 22);

                        x.Fill(SixLabors.ImageSharp.Color.FromRgba(0, 0, 0, 180), textBgRect);
                        x.DrawText(labelText, font, color, textLocation);
                    });
                }

                // ✅ Step 4: Convert annotated image to BitmapImage for WPF display
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

                    // ✅ Step 5: Add to WPF UI list safely
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        CapturedImages.Add(bitmap);
                    });
                }
            }

            return (boxCount, palletHeightMeters);
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

        private string RunOCRModel(Image<Rgba32> image)
        {
            try
            {


                UpdateProgressStatus( "OCR AI Model Start");
                using var tempStream = new MemoryStream();
                image.SaveAsPng(tempStream);
                tempStream.Position = 0;

                string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ocr_{Guid.NewGuid()}.png");
                File.WriteAllBytes(tempPath, tempStream.ToArray());

                string text = ExtractTextToJson(tempPath); // existing OCR call
                return text;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OCR] Error: {ex.Message}");
                return "Error reading text";
            }
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
            ImageDialogHost.IsOpen = false;
            MaterialDesignThemes.Wpf.DialogHost.CloseDialogCommand.Execute(null, null);
            var allCameras = CameraFinder.Enumerate();
            int cameraCount = allCameras.Count;
            if (cameraCount > 0)
                SetCameraUI(CamFrontLightPulse, cameraCount >= 1);
          CheckPalletStatus(allCameras[0]);

        }


        #endregion

        private async void ReadTextFromImage()
        {
            
            _ocrCancellationTokenSource?.Cancel();
            _ocrCancellationTokenSource = new CancellationTokenSource();

            try
            {
                Dispatcher.Invoke(() =>
                {
                    //_ocrResults.Clear();
                    //TextExtractionStatusText.Text = "Initializing OCR...";
                    //TextExtractionStatusText.Visibility = Visibility.Visible;
                });

                string tessDataPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

                using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                using var capture = new VideoCapture(0); // 0 or 1 depending on your webcam

                if (!capture.IsOpened())
                {
                    MessageBox.Show("Failed to open webcam.");
                    return;
                }

                int frameIntervalMs = 33; // ~30 FPS
                int ocrEveryNFrames = 15;
                int frameCount = 0;

                var token = _ocrCancellationTokenSource.Token;
                Dispatcher.Invoke(() =>
                {

                    //TextExtractionStatusText.Text = "Scanning";

                });
                while (!token.IsCancellationRequested)
                {
                    using var frameMat = new Mat();
                    capture.Read(frameMat);
                    if (frameMat.Empty())
                        continue;

                    // Clone frame for OCR and UI separately
                    Mat clonedForDisplay = frameMat.Clone();
                    Mat clonedForOCR = frameMat.Clone();

                    // Update UI - non-blocking
                    Dispatcher.Invoke(() =>
                    {
                        //LastImage.Source = ConvertBitmapToImageSource(BitmapConverter.ToBitmap(clonedForDisplay));
                    });

                    // OCR only every Nth frame to reduce CPU load
                    if (frameCount % ocrEveryNFrames == 0)
                    {
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                // Preprocessing
                                Cv2.CvtColor(clonedForOCR, clonedForOCR, ColorConversionCodes.BGR2GRAY);
                                Cv2.GaussianBlur(clonedForOCR, clonedForOCR, new OpenCvSharp.Size(3, 3), 0);
                                Cv2.Threshold(clonedForOCR, clonedForOCR, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);

                                using var bitmap = BitmapConverter.ToBitmap(clonedForOCR);
                                using var ms = new MemoryStream();
                                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                ms.Position = 0;

                                using var pix = Pix.LoadFromMemory(ms.ToArray());
                                using var page = engine.Process(pix);
                                string text = page.GetText().Trim();

                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    Console.WriteLine($"OCR Detected:\n{text}");
                                    Dispatcher.Invoke(() =>
                                    {
                                        //OcrText.Text = text;
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() => MessageBox.Show("OCR error:\n" + ex.Message));
                            }
                        });
                    }

                    frameCount++;
                    await Task.Delay(frameIntervalMs, token); // Frame render delay
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("OCR system error:\n" + ex.Message);
                });
            }
        }

        
        private async void ReadTextFromBaslerCamera(ICameraInfo cameraInfo)
        {
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    //_ocrResults.Clear();
                    //TextExtractionStatusText.Text = "Connecting to Basler camera OCR...";
                    //TextExtractionStatusText.Visibility = Visibility.Visible;
                });

                // 🔐 Initialize Dynamsoft License
                int errorCode = LicenseManager.InitLicense("t0160pgQAAHH+80VZPgVcgrgeEzXZn5NtSwGoe3j2Vb2ZdszposhChRqHvNWN/0UCwn8WbQBMA6Xwnqo1XPH7omOcjkytQg8B4iYgiNh9eJhFIcvnba5j9mesyzFdD+MniKLJAab2N7ea2k0eW9N5P+uT9ybyKZocYGp/M++zzVQnr2qf6Ts2mxxgan+z2OdfJs7tuLXo9fJDsMFZpwxvMAryBw==;t0159pgQAALBuAkleU1UNMkpkWXNXke0lL9BImrJu2OBq6YfaTc6sEd6li/uzglypOkCx6DLyhKU9Zo01LobsyVgj2EKHq5guAoIZyYeHWQlZv6c5j0nv+Gye4nM3/oIomjrAZH9zq6ndNPuu57Ge9anXJvIpmjrAZH8z77PNpJNXtc/4HZtNHWCyv1ns85Z5pDFtLXqefgg2OOuo4Q03//IQ", out string errorMsg);
                if (errorCode != (int)EnumErrorCode.EC_OK && errorCode != (int)EnumErrorCode.EC_LICENSE_WARNING)
                {
                    MessageBox.Show($"Dynamsoft License error: {errorMsg}");
                    return;
                }
               
                using var cvRouter = new CaptureVisionRouter();
                using var imageIo = new ImageIO();
                using (Camera camera = new Camera(cameraInfo))
                {

                    camera.CameraOpened += Basler.Pylon.Configuration.AcquireContinuous;
                    camera.Open();
                    camera.Parameters[PLCameraInstance.MaxNumBuffer].SetValue(5);

                    _processingCts = new CancellationTokenSource();
                    var token = _processingCts.Token;
                    Dispatcher.BeginInvoke(() =>
                    {

                        //TextExtractionStatusText.Visibility = Visibility.Collapsed;
                    });
                    camera.StreamGrabber.Start();

                    await Task.Run(async () =>
                    {
                        int frameCounter = 0;

                        while (!token.IsCancellationRequested)
                        {
                            try
                            {
                                IGrabResult grabResult = camera.StreamGrabber.RetrieveResult(5000, TimeoutHandling.ThrowException);
                                using (grabResult)
                                {
                                    if (!grabResult.GrabSucceeded)
                                        continue;

                                    // Convert camera frame to Bitmap
                                    Mat frameMat = GrabResultToMat(grabResult);
                                    using var bitmap = BitmapConverter.ToBitmap(frameMat);

                                    // Optionally show image in UI


                                    // Save to temp file for Dynamsoft input
                                    string tempImagePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"frame_{frameCounter++}.png");
                                    bitmap.Save(tempImagePath, System.Drawing.Imaging.ImageFormat.Png);
                                    await Task.Delay(1000);
                                    ExtractTextToJson(tempImagePath);


                                    Dispatcher.BeginInvoke(() =>
                                    {
                                        //LastImage.Source = ConvertBitmapToImageSource(bitmap);

                                    });
                                    await Task.Delay(500, token); // Adjust frame delay
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.BeginInvoke(() =>
                                {
                                    MessageBox.Show("Dynamsoft OCR Error:\n" + ex.Message);
                                });
                            }
                        }
                    }, token);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    MessageBox.Show("Unexpected error:\n" + ex.Message);
                });
            }
        }


        public string ExtractTextToJson(string imagePath)
        {
            try
            {
                string pythonExe = @"C:\Users\Abhishaik Sharma\AppData\Local\Programs\Python\Python313\python.exe"; // Python path

                var scriptPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "reader.py");

                string ocrText = RunOCR(pythonExe, scriptPath, imagePath);

                return ocrText;
            }
            catch (Exception ex)
            {
                // Handle exception or log it
                // You can return an empty string or an error message
                return $"Error during OCR: {ex.Message}";
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
        private string CleanOcrText(string rawText)
        {
            string[] keywords = new[]
            {
        "PRODUCT", "SUPPLIER", "CATEGORY", "CONTAINER", "DATE",
        "PRODUCTION", "EXPIRE", "QUALITY", "COST", "BUYER"
    };

            var lines = rawText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line =>
                    !string.IsNullOrWhiteSpace(line) &&
                    !line.StartsWith("[Frame", StringComparison.OrdinalIgnoreCase) &&
                    keywords.Any(k => line.ToUpper().Contains(k)))
                .Distinct()
                .ToList();

            return string.Join(Environment.NewLine, lines);
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

        private string CorrectSpelling(string text)
        {
            var words = text.Split(' ');
            var corrected = new List<string>();

            foreach (var word in words)
            {
                //var suggestions = symSpell.Lookup(word, Verbosity.Closest, maxEditDistance: 2);
                //corrected.Add(suggestions.FirstOrDefault()?.term ?? word);
            }

            return string.Join(" ", corrected);
        }


        // Convert System.Drawing.Bitmap to Tesseract Pix
        private Pix ConvertBitmapToPix(Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Pix.LoadFromMemory(ms.ToArray());
        }

        // Convert System.Drawing.Bitmap to WPF BitmapImage
        private BitmapImage ConvertToBitmapImage(Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            var bmpImage = new BitmapImage();
            bmpImage.BeginInit();
            bmpImage.CacheOption = BitmapCacheOption.OnLoad;
            bmpImage.StreamSource = ms;
            bmpImage.EndInit();
            bmpImage.Freeze();
            return bmpImage;
        }

        private void ProcessHumanDetection(ICameraInfo cameraInfo)
        {
            try
            {
                //Dispatcher.Invoke(() => ShowStatus(HumanDetectionStatusText, "Connecting to Basler camera..."));
                //Thread.Sleep(500);
                //Dispatcher.Invoke(() => HideStatus(HumanDetectionStatusText));

                using (Camera camera3 = new Camera(cameraInfo))
                {
                    camera3.CameraOpened += Basler.Pylon.Configuration.AcquireContinuous;
                    camera3.Open();
                    camera3.Parameters[PLCameraInstance.MaxNumBuffer].SetValue(20);
                    camera3.StreamGrabber.Start();

                    Console.WriteLine($"Human Detection Camera started: {camera3.CameraInfo[CameraInfoKey.ModelName]}");
                    _isRunning2 = true;

                    while (_isRunning2)
                    {
                        try
                        {
                            using IGrabResult grabResult = camera3.StreamGrabber.RetrieveResult(5000, TimeoutHandling.ThrowException);
                            if (!grabResult.GrabSucceeded) continue;

                            Mat frame = GrabResultToMat(grabResult);
                            using var cloned = frame.Clone();

                            using var ms = new MemoryStream();
                            cloned.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                            ms.Position = 0;
                            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

                            var predictions = _scorerHumanModel.Predict(image);
                            bool humanDetected = false;

                            foreach (var pred in predictions)
                            {
                                var label = pred.Label.Name.ToLower();
                                if (label is "person" or "man" or "woman" or "human" or "child")
                                    humanDetected = true;

                                var rect = pred.Rectangle;
                                image.Mutate(ctx =>
                                {
                                    ctx.DrawPolygon(new SixLabors.ImageSharp.Drawing.Processing.Pen(pred.Label.Color, 2),
                                        new Point(rect.Left, rect.Top),
                                        new Point(rect.Right, rect.Top),
                                        new Point(rect.Right, rect.Bottom),
                                        new Point(rect.Left, rect.Bottom));
                                    ctx.DrawText($"{pred.Label.Name} ({Math.Round(pred.Score, 2)})",
                                        _font, pred.Label.Color, new Point(rect.Left, rect.Top - 20));
                                });
                            }

                            Dispatcher.Invoke(async () =>
                            {
                                await TriggerHumanDetectedEffectAsync(humanDetected);
                            });

                            using var outStream = new MemoryStream();
                            image.SaveAsBmp(outStream);
                            outStream.Position = 0;

                            var bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.StreamSource = outStream;
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.EndInit();
                            bitmapImage.Freeze();

                            Dispatcher.Invoke(() =>
                            {
                                
                                //HumanDetectionCameraFeed.Source = bitmapImage;
                            });
                        }
                        catch (TimeoutException)
                        {
                            Console.WriteLine("[Human] Timeout — continuing...");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Human] Frame error: {ex.Message}");
                        }
                    }

                    camera3.StreamGrabber.Stop();
                    camera3.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Human] Exception: {ex.Message}");
                //Dispatcher.Invoke(() => ShowStatus(HumanDetectionStatusText, "Error: " + ex.Message));
            }
        }


        private void ProcessBoxCount()
        {
            while (_isRunning)
            {
                _capture.Read(_frame);
                if (_frame.Empty()) continue;

                using var cloned = _frame.Clone();
                var bitmap = cloned.ToBitmap(); // System.Drawing.Bitmap
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                ms.Position = 0;

                using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

                var predictions = _scorerBoxCountingModel.Predict(image);

                bool humanDetected = false;
                foreach (var prediction in predictions)
                {
                    var label = prediction.Label.Name.ToLower();
                    var score = Math.Round(prediction.Score, 2);
                    var (x, y) = (prediction.Rectangle.Left - 3, prediction.Rectangle.Top - 23);

                    image.Mutate(ctx => ctx.DrawPolygon(new SixLabors.ImageSharp.Drawing.Processing.Pen(prediction.Label.Color, 2),
                        new Point(prediction.Rectangle.Left, prediction.Rectangle.Top),
                        new Point(prediction.Rectangle.Right, prediction.Rectangle.Top),
                        new Point(prediction.Rectangle.Right, prediction.Rectangle.Bottom),
                        new Point(prediction.Rectangle.Left, prediction.Rectangle.Bottom)));
              

                    image.Mutate(ctx => ctx.DrawText(
                        $"{prediction.Label.Name} ({score})",
                        _font, prediction.Label.Color, new Point(x, y)));
                }
              

                using var outStream = new MemoryStream();
                image.SaveAsBmp(outStream);
                outStream.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = outStream;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                Dispatcher.Invoke(() =>
                {
                    
                });
            }
        }
        private CancellationTokenSource _cts;
        private Task _processingTask;

       
        private void ProcessBoxWithPylonCamera(ICameraInfo cameraInfo)
        {
            try
            {
                

                _processingCts = new CancellationTokenSource();
                _isProcessing = true;

                using (Camera camera2 = new Camera(cameraInfo))
                {
                    camera2.CameraOpened += Basler.Pylon.Configuration.AcquireContinuous;
                    camera2.Open();
                    camera2.Parameters[PLCameraInstance.MaxNumBuffer].SetValue(50);
                    camera2.StreamGrabber.Start();

                    Console.WriteLine($"Box Camera started: {camera2.CameraInfo[CameraInfoKey.ModelName]}");
                    
                    while (!_processingCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            FlashCamera(FrontFlashEllipse);
                            ShowPalletFromLeft();
                            ;

                            using IGrabResult grabResult = camera2.StreamGrabber.RetrieveResult(5000, TimeoutHandling.ThrowException);
                            if (!grabResult.GrabSucceeded) continue;

                            Mat frame = GrabResultToMat(grabResult);
                            using var cloned = frame.Clone();

                            // Convert to ImageSharp
                            using var ms = new MemoryStream();
                            cloned.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                            ms.Position = 0;
                            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

                            var predictions = _scorerBoxCountingModel.Predict(image)
                                .Where(p => p.Score >= 0.30f).ToList();

                            foreach (var pred in predictions)
                            {
                                var rect = new OpenCvSharp.Rect(
                                    (int)pred.Rectangle.Left,
                                    (int)pred.Rectangle.Top,
                                    (int)pred.Rectangle.Width,
                                    (int)pred.Rectangle.Height
                                );
                                Cv2.Rectangle(cloned, rect, Scalar.Yellow, 2);
                                Cv2.PutText(cloned, $"{pred.Label.Name} ({Math.Round(pred.Score * 100)}%)",
                                    new OpenCvSharp.Point(rect.X, rect.Y - 10), HersheyFonts.HersheySimplex,
                                    0.5, Scalar.Yellow, 1);
                            }

                            var bitmapImage = cloned.ToBitmapSource();
                            bitmapImage.Freeze();

                            Dispatcher.Invoke(() =>
                            {
                                //BoxCountingCameraFeed.Source = bitmapImage;
                                NoBoxTxt.Text = $"Boxes Detected: {predictions.Count}";
                                var pallet = predictions.FirstOrDefault(p => p.Label.Name.ToLower() == "pallet");
                                if (pallet != null)
                                {
                                    // Fix for CS0266: Explicitly cast 'float' to 'int' when calculating heightPixels
                                    int heightPixels = (int)(pallet.Rectangle.Bottom - pallet.Rectangle.Top);
                                    double mmPerPixel = 2.0; // Calibration value
                                    double palletHeightMeters = (heightPixels * mmPerPixel) / 1000.0;
                                    PalletHeightTxt.Text = $"{palletHeightMeters:F2} m";
                                }
                            });
                        }
                        catch (TimeoutException)
                        {
                            Console.WriteLine("[Box] Timeout — continuing...");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Box] Frame error: {ex.Message}");
                        }
                    }

                    camera2.StreamGrabber.Stop();
                    camera2.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Box] Exception: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                Console.WriteLine("[Box] Process stopped.");
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

        private void ProcessBoxFromLiveCamera()
        {
            string rtspUrl = "rtsp://admin:ChShani%40786@192.168.24.110:554/Streaming/Channels/102/";

            //ShowStatus(BoxCountingStatusText, "Connecting to camera...");
            Thread.Sleep(500);

            var videoCapture = new VideoCapture(rtspUrl);
            if (!videoCapture.IsOpened())
            {
                //ShowStatus(BoxCountingStatusText, "Failed to connect to RTSP sub-stream.");
                return;
            }

            //ShowStatus(BoxCountingStatusText, "Connected. Starting detection...");
            //Thread.Sleep(1000);
            //HideStatus(BoxCountingStatusText);

            _isRunning = true;
            _frame = new Mat();
            var frameInterval = TimeSpan.FromMilliseconds(1000.0 / 10); // Target ~10 FPS
            var lastFrameTime = DateTime.UtcNow;

            while (_isRunning && videoCapture.Read(_frame))
            {
                if (_frame.Empty()) continue;

                if ((DateTime.UtcNow - lastFrameTime) < frameInterval)
                    continue;

                lastFrameTime = DateTime.UtcNow;

                using var resizedFrame = new Mat();
                Cv2.Resize(_frame, resizedFrame, new OpenCvSharp.Size(640, 360));

                using var bitmap = resizedFrame.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                ms.Position = 0;

                using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

                var predictions = _scorerBoxCountingModel.Predict(image);
                var filtered = predictions.Where(p => p.Score >= 0.25f).ToList();

                foreach (var prediction in filtered)
                {
                    var label = prediction.Label.Name.ToLower();
                    var score = Math.Round(prediction.Score, 2);
                    var (x, y) = (prediction.Rectangle.Left - 3, prediction.Rectangle.Top - 23);
                    var color = _classColors.ContainsKey(label) ? _classColors[label] : new Rgba32(255, 255, 0);

                    image.Mutate(ctx => ctx.DrawPolygon(
                        new SixLabors.ImageSharp.Drawing.Processing.Pen(color, 2),
                        new Point(prediction.Rectangle.Left, prediction.Rectangle.Top),
                        new Point(prediction.Rectangle.Right, prediction.Rectangle.Top),
                        new Point(prediction.Rectangle.Right, prediction.Rectangle.Bottom),
                        new Point(prediction.Rectangle.Left, prediction.Rectangle.Bottom)));

                    image.Mutate(ctx => ctx.DrawText(
                        $"{prediction.Label.Name} ({score})",
                        _font, color, new Point(x, y)));

                    if (label == "pallet")
                    {
                        int palletHeightPixels = (int)Math.Round(prediction.Rectangle.Bottom - prediction.Rectangle.Top);

                        double mmPerPixel = 2.0;  // Replace with your calibration
                        double palletHeightMM = palletHeightPixels * mmPerPixel;
                        double palletHeightMeters = palletHeightMM / 1000.0;

                        Dispatcher.Invoke(() =>
                        {
                            PalletHeightTxt.Text = $"{palletHeightMeters:F2} m";
                        });
                    }
                }

                using var outStream = new MemoryStream();
                image.SaveAsBmp(outStream);
                outStream.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = outStream;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                Dispatcher.Invoke(() =>
                {
                    //BoxCountingCameraFeed.Source = bitmapImage;
                    //NoBoxTxt.Text = $"Boxes Detected: {filtered.Count}";
                });
            }

            videoCapture.Release();
            _isRunning = false;
        }


        private readonly Dictionary<string, Rgba32> _classColors = new()
            {
                { "box", new Rgba32(255, 0, 0) },      // Red
                { "pallet", new Rgba32(0, 255, 0) },   // Green
                { "27", new Rgba32(0, 0, 255) }        // Blue
            };
        private void ProcessBoxWithImageFile()
        {
            try
            {
                //ShowStatus(BoxCountingStatusText, "Loading image...");
                Thread.Sleep(500);

                string imagePath = "Assets/boxes.png"; // Path to your image file
                if (!File.Exists(imagePath))
                {
                    MessageBox.Show("Image file not found!");
                    return;
                }

                //ShowStatus(BoxCountingStatusText, "Image loaded. Initializing detection...");
                //Thread.Sleep(500);
                //HideStatus(BoxCountingStatusText);

                using var imageStream = File.OpenRead(imagePath);
                using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imageStream);

                var predictions = _scorerBoxCountingModel.Predict(image);
                var filtered = predictions.Where(p => p.Score >= 0.25f).ToList();

                foreach (var prediction in predictions)
                {
                    var label = prediction.Label.Name.ToLower();
                    var score = Math.Round(prediction.Score, 2);
                    var (x, y) = (prediction.Rectangle.Left - 3, prediction.Rectangle.Top - 23);

                    // Draw bounding box
                    image.Mutate(ctx => ctx.DrawPolygon(
                        new SixLabors.ImageSharp.Drawing.Processing.Pen(prediction.Label.Color, 2),
                        new Point(prediction.Rectangle.Left, prediction.Rectangle.Top),
                        new Point(prediction.Rectangle.Right, prediction.Rectangle.Top),
                        new Point(prediction.Rectangle.Right, prediction.Rectangle.Bottom),
                        new Point(prediction.Rectangle.Left, prediction.Rectangle.Bottom)));

                    // Draw label
                    image.Mutate(ctx => ctx.DrawText(
                        $"{prediction.Label.Name} ({score})",
                        _font, prediction.Label.Color, new Point(x, y)));
                }

                // Convert processed image to BitmapImage
                using var outStream = new MemoryStream();
                image.SaveAsBmp(outStream);
                outStream.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = outStream;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                Dispatcher.Invoke(() =>
                {
                    //BoxCountingCameraFeed.Source = bitmapImage;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing image: {ex.Message}");
            }
        }

        private void ShowStatus(TextBlock statusBlock, string message)
        {
            Dispatcher.Invoke(() =>
            {
                statusBlock.Text = message;
                statusBlock.Visibility = Visibility.Visible;
                statusBlock.Opacity = 1;
            });
        }

        private void HideStatus(TextBlock statusBlock)
        {
            Dispatcher.Invoke(() =>
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1));
                fadeOut.Completed += (s, e) => statusBlock.Visibility = Visibility.Collapsed;
                statusBlock.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            });
        }
        #region Basic layout and animation methods
        private async Task StartImageAnimation()
        {
            await AnimationHelper.AnimateLightPulseAsync(CamRightLightPulse);
            await AnimationHelper.AnimateLightPulseAsync(CamFrontLightPulse);

            //while (true)
            //{
            //    await TranslateImageAsync(PaletImage, 30);
            //    await TranslateImageAsync(PaletImage, -30);
            //}
        }

        private async Task TranslateImageAsync(UIElement element, double offset)
        {
            var translateAnimation = new DoubleAnimation
            {
                To = offset,
                Duration = TimeSpan.FromSeconds(1),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            // Create a TranslateTransform and apply it to the element
            var transform = new TranslateTransform();
            element.RenderTransform = transform;

            // Animate the translation
            transform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);

            await Task.Delay(1000); // Wait for the animation duration
        }
        public void AnimateImage()
        {
            _ = StartImageAnimation();
        }
        private async Task TriggerHumanDetectedEffectAsync(bool isFound)
        {
            if (isFound)
            {
                if (!_isAlertPlaying)
                {
                    _isAlertPlaying = true;
                    audioManager.Play("Resources/Audio/warning.wav"); // Path should be relative to the .exe location
                }
                await AnimationHelper.ScaleToAsync(HumanImage, 1.2, 200, true);  // scale up
                await AnimationHelper.ScaleToAsync(HumanImage, 1.0, 200, false); // scale down

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






        public async void LoadCamera()
        {
            //  await ProcessMjpegStreamAsync("172.16.98.78",8080);

        }
        public async Task<bool> IsCameraAvailableAsync(string ipAddress)
        {
            try
            {
                if (!ipAddress.StartsWith("http"))
                    ipAddress = $"http://{ipAddress}"; // Default port for IP Webcam

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var request = new HttpRequestMessage(HttpMethod.Head, ipAddress);
                var response = await client.SendAsync(request);

                return response.IsSuccessStatusCode ||
                       (int)response.StatusCode < 500; // Accept 401, 403, etc. as "alive"
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        #region Window UI Controls
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                this.MaxHeight = double.PositiveInfinity; // Reset max height
            }
            else
            {
                var desktopWorkingArea = SystemParameters.WorkArea; // Get area excluding taskbar
                this.MaxHeight = desktopWorkingArea.Height + 10;
                this.MaxWidth = desktopWorkingArea.Width + 12;


                this.WindowState = WindowState.Maximized;

            }
        }
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
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
                System.Windows.Application.Current.Shutdown(); // Stops the WPF application
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


    }
}
