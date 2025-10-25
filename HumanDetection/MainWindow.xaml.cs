
//using Basler.Pylon;
using HumanDetection.Model;
using HumanDetection.Utilites.Animation;
using HumanDetection.Utilites.Audio;
using Microsoft.ML.OnnxRuntime;
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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tesseract;
using Yolov5Net.Scorer;
using Yolov5Net.Scorer.Models;

using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.Design.AxImporter;
using FlipMode = OpenCvSharp.FlipMode;
using Point = SixLabors.ImageSharp.PointF;





namespace HumanDetection
{
    public partial class MainWindow : System.Windows.Window
    {
        private VideoCapture _capture;
   
        private Mat _frame;
        private bool _isRunning = true;
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
        //private Camera camera;
        private CancellationTokenSource _processingCts;
        private bool _isProcessing = false;
        public MainWindow()
        {
            InitializeComponent();
            MaximizeRestoreButton_Click(null, null);
            Loaded += MainWindow_LoadedAsync;  // Changed to async
        Closed += (s, e) => _isRunning = false;
          
            audioManager = new AudioManager();
          
            
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
                // Show loading indicator
                LoadingOverlay.Visibility = Visibility.Visible;
                await Task.Delay(1); // Ensure UI updates

                // Initialize camera and models in background
                await Task.Run(async () =>
                {
                    _capture = new VideoCapture(0);
                    _frame = new Mat();
                    LoadModels();
                });
                LoadingOverlay.Visibility = Visibility.Collapsed;
                // Start processing
               //var boxTask = Task.Run(ProcessBoxWithPylonCamera);
               //var extractTextask = Task.Run(ReadTextFromIPCamera);
                //var humanDetections = Task.Run(ProcessHumanDetection);
                //await Task.WhenAll(boxTask);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Initialization failed: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Hide loading indicator
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
            var modelPathBoxCounting = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/Weights/customBoxCount.onnx");
            _scorerBoxCountingModel = new YoloScorer<YoloCustomModel>(modelPathBoxCounting, sessionOptions);

            var modelPathHumanDetection = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/Weights/yolov5s.onnx");
            _scorerHumanModel = new YoloScorer<YoloCocoP5Model>(modelPathHumanDetection, sessionOptions);


            var fontPath = "C:/Windows/Fonts/consola.ttf";
            _font = new SixLabors.Fonts.Font(new FontCollection().Add(fontPath), 16);

           
        }


        private ObservableCollection<OcrFrameResult> _ocrResults = new ObservableCollection<OcrFrameResult>();

        private CancellationTokenSource _ocrCancellationTokenSource;

        private async void ReadTextFromImage()
        {
            
            _ocrCancellationTokenSource?.Cancel();
            _ocrCancellationTokenSource = new CancellationTokenSource();

            try
            {
                Dispatcher.Invoke(() =>
                {
                    _ocrResults.Clear();
                    TextExtractionStatusText.Text = "Initializing OCR...";
                    TextExtractionStatusText.Visibility = Visibility.Visible;
                });

                string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

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

                    TextExtractionStatusText.Text = "Scanning";

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
                        LastImage.Source = ConvertBitmapToImageSource(BitmapConverter.ToBitmap(clonedForDisplay));
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
                                        OcrText.Text = text;
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

        private async void ReadTextFromIPCamera()
        {
            string ipCameraUrl = "http://192.168.1.83:8080/video";
            try
            {
                Dispatcher.Invoke(() =>
                {
                    _ocrResults.Clear();
                    TextExtractionStatusText.Text = "Connecting to IP Camera...";
                    TextExtractionStatusText.Visibility = Visibility.Visible;
                });

                StringBuilder allResults = new StringBuilder();
                string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

                using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                using var capture = new VideoCapture(ipCameraUrl);

                if (!capture.IsOpened())
                {
                    MessageBox.Show("Failed to open IP camera stream.");
                    return;
                }

                using var frameMat = new Mat();
                int frameIntervalMs = 1000;

                while (true)
                {
                    capture.Read(frameMat);
                    if (frameMat.Empty())
                        continue;

                    // OPTIONAL: Preprocessing
                    Cv2.CvtColor(frameMat, frameMat, ColorConversionCodes.BGR2GRAY);
                    Cv2.GaussianBlur(frameMat, frameMat, new OpenCvSharp.Size(3, 3), 0);
                    Cv2.Threshold(frameMat, frameMat, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);

                    using var bitmap = BitmapConverter.ToBitmap(frameMat);

                    Dispatcher.Invoke(() =>
                    {
                        LastImage.Source = ConvertBitmapToImageSource(bitmap);
                        TextExtractionStatusText.Text = "Processing IP camera frame...";
                    });

                    using var ms = new MemoryStream();
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;

                    using var pix = Pix.LoadFromMemory(ms.ToArray());
                    using var page = engine.Process(pix);
                    string text = page.GetText().Trim();

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        Console.WriteLine($"OCR from IP Camera:\n{text}");
                        allResults.AppendLine("[Frame]");
                        allResults.AppendLine(text);
                        allResults.AppendLine();

                        Dispatcher.Invoke(() =>
                        {
                            OcrText.Text = text;
                        });
                    }

                    await Task.Delay(frameIntervalMs);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("IP Camera OCR error:\n" + ex.Message);
                });
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
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memory;
                bitmapImage.EndInit();
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

        private void ProcessHumanDetection()
        {
            _capture = new VideoCapture(0);
            ShowStatus(HumanDetectionStatusText, "Connecting to IP 192.168.0.81...");
            Thread.Sleep(500);
            ShowStatus(HumanDetectionStatusText, "Connected. Getting preview...");
            Thread.Sleep(500);
            ShowStatus(HumanDetectionStatusText, "Preview ready. Starting object detection...");
            Thread.Sleep(1000);
            HideStatus(HumanDetectionStatusText);
          
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
                var predictions = _scorerHumanModel.Predict(image);
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
                    if (label == "person" || label == "man" || label == "woman" || label == "human" || label == "child")
                    {
                        humanDetected = true;

                    }
                    else { }

                    image.Mutate(ctx => ctx.DrawText(
                        $"{prediction.Label.Name} ({score})",
                        _font, prediction.Label.Color, new Point(x, y)));
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
                    HumanDetectionCameraFeed.Source = bitmapImage;
                });
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
                    BoxCountingCameraFeed.Source = bitmapImage;
                });
            }
        }
        private CancellationTokenSource _cts;
        private Task _processingTask;

        private void ProcessBoxWithVideoFile()
        {

            //string videoPath = "Assets/boxvideo.mp4";
            //var videoCapture = new VideoCapture(videoPath);

            //if (!videoCapture.IsOpened())
            //{
            //    MessageBox.Show("Failed to open video file!");
            //    return;
            //}

            //_cts = new CancellationTokenSource();

            //string IPAddress = "http://192.168.1.93:8080/video";
            ShowStatus(BoxCountingStatusText, "Connecting to webcam...");
            Thread.Sleep(1000);
            ShowStatus(BoxCountingStatusText, "Video loaded. Initializing detection...");
            Thread.Sleep(500);
            var videoCapture = new VideoCapture(1); // Use default webcam

            if (!videoCapture.IsOpened())
            {
                MessageBox.Show("Failed to open webcam!");
                return;
            }
            HideStatus(BoxCountingStatusText);
            StartCountdown();
            _cts = new CancellationTokenSource();
            _isRunning = true;

            _processingTask = Task.Run(() =>
            {
                Mat frame = new Mat();
                
                int frameSkipCounter = 0;

                while (_isRunning && !_cts.Token.IsCancellationRequested)
                {
                    if (!videoCapture.Read(frame) || frame.Empty()) continue;

                    // Skip every other frame for performance
                    if (++frameSkipCounter % 2 != 0) continue;

                    using var cloned = frame.Clone();
                    //if (frame.Width > frame.Height)
                    //{
                    //    // Rotate 90 degrees counter-clockwise to portrait mode
                    //    Cv2.Transpose(cloned, cloned);        // Transpose
                    //    Cv2.Flip(cloned, cloned, FlipMode.Y); // Vertical flip
                    //}
                    var bitmap = cloned.ToBitmap();

                    // Convert to ImageSharp image
                    using var ms = new MemoryStream();
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                    ms.Position = 0;
                    using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

                    // YOLOv5 ONNX inference
                    var predictions = _scorerBoxCountingModel.Predict(image);
                    var filtered = predictions.Where(p => p.Score >= 0.25f).ToList();

                    // Draw predictions using OpenCvSharp
                    foreach (var pred in filtered)
                    {
                        // Fix for CS1503: Convert 'float' to 'int' explicitly when using RectangleF properties
                        var rect = new OpenCvSharp.Rect(
                            (int)pred.Rectangle.Left,
                            (int)pred.Rectangle.Top,
                            (int)pred.Rectangle.Width,
                            (int)pred.Rectangle.Height
                        );
                        Scalar color = Scalar.Yellow;
                        Cv2.Rectangle(cloned, rect, color, 2);

                        Cv2.PutText(
                         cloned,
                         $"{pred.Label.Name} ({Math.Round(pred.Score * 100)}%)",
                         new OpenCvSharp.Point(rect.X, rect.Y - 10),
                         HersheyFonts.HersheySimplex,
                         0.5,
                         color,
                         1);

                    }

                    // Convert frame to WPF BitmapImage
                    var bitmapImage = cloned.ToBitmapSource();
                    bitmapImage.Freeze();

                    // Update WPF UI
                    Dispatcher.Invoke(() =>
                    {
                        BoxCountingCameraFeed.Source = bitmapImage;
                        NoBoxTxt.Text = $"Boxes Detected: {filtered.Count}";

                        var pallet = filtered.FirstOrDefault(p => p.Label.Name.ToLower() == "pallet");
                        if (pallet != null)
                        {
                            // Fix for CS0266: Explicitly cast 'float' to 'int' when calculating heightPixels
                            int heightPixels = (int)(pallet.Rectangle.Bottom - pallet.Rectangle.Top);
                            double mmPerPixel = 2.0; // Calibration value
                            double palletHeightMeters = (heightPixels * mmPerPixel) / 1000.0;
                            //PalletHeightTxt.Text = $"{palletHeightMeters:F2} m";
                        }
                    });

                    // ~30 FPS throttle
                    Thread.Sleep(30);
                }

                videoCapture.Release();
                _isRunning = false;
            });
        }

        //private void ProcessBoxWithPylonCamera()
        //{
        //    try
        //    {

        //        ShowStatus(BoxCountingStatusText, "Connecting to Basler camera...");
        //        Task.Delay(1000);
        //        HideStatus(BoxCountingStatusText);
        //        if (_isProcessing) return;
        //        _isProcessing = true;
        //        _processingCts = new CancellationTokenSource();
        //        using (Camera camera = new Camera())
        //        {
        //            Console.WriteLine("Using device: {0}", camera.CameraInfo[CameraInfoKey.ModelName]);
        //            Console.WriteLine();

        //            camera.CameraOpened += Basler.Pylon.Configuration.AcquireContinuous;
        //            camera.Open();
        //            camera.Parameters[PLCameraInstance.MaxNumBuffer].SetValue(5);

        //            // Create cancellation token source for the processing task
        //            var cts = new CancellationTokenSource();
        //            bool isRunning = true;

        //            // Start the processing task
        //            Task processingTask = Task.Run(() =>
        //            {
        //                while (isRunning && !cts.Token.IsCancellationRequested)
        //                {
        //                    // This will be replaced with the grab result processing
        //                    Thread.Sleep(10);
        //                }
        //            });

        //            camera.StreamGrabber.Start();

        //            while (!_processingCts.Token.IsCancellationRequested)
        //            {
        //                IGrabResult grabResult = camera.StreamGrabber.RetrieveResult(5000, TimeoutHandling.ThrowException);
        //                using (grabResult)
        //                {
        //                    if (grabResult.GrabSucceeded)
        //                    {
        //                        // Convert the grab result to OpenCV Mat
        //                        Mat frame = GrabResultToMat(grabResult);

        //                        // Skip every other frame for performance
        //                        ;

        //                        using var cloned = frame.Clone();
        //                        var bitmap = cloned.ToBitmap();

        //                        // Convert to ImageSharp image
        //                        using var ms = new MemoryStream();
        //                        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
        //                        ms.Position = 0;
        //                        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

        //                        // YOLOv5 ONNX inference
        //                        var predictions = _scorerBoxCountingModel.Predict(image);
        //                        var filtered = predictions.Where(p => p.Score >= 0.25f).ToList();

        //                        // Draw predictions using OpenCvSharp
        //                        foreach (var pred in filtered)
        //                        {
        //                            var rect = new OpenCvSharp.Rect(
        //                                (int)pred.Rectangle.Left,
        //                                (int)pred.Rectangle.Top,
        //                                (int)pred.Rectangle.Width,
        //                                (int)pred.Rectangle.Height
        //                            );
        //                            Scalar color = Scalar.Yellow;
        //                            Cv2.Rectangle(cloned, rect, color, 2);

        //                            Cv2.PutText(
        //                                cloned,
        //                                $"{pred.Label.Name} ({Math.Round(pred.Score * 100)}%)",
        //                                new OpenCvSharp.Point(rect.X, rect.Y - 10),
        //                                HersheyFonts.HersheySimplex,
        //                                0.5,
        //                                color,
        //                                1);

        //                        }

        //                        // Convert frame to WPF BitmapImage
        //                        var bitmapImage = cloned.ToBitmapSource();
        //                        bitmapImage.Freeze();

        //                        // Update WPF UI
        //                        Dispatcher.Invoke(() =>
        //                        {
        //                            BoxCountingCameraFeed.Source = bitmapImage;
        //                            NoBoxTxt.Text = $"Boxes Detected: {filtered.Count}";

        //                            var pallet = filtered.FirstOrDefault(p => p.Label.Name.ToLower() == "pallet");
        //                            if (pallet != null)
        //                            {
        //                                int heightPixels = (int)(pallet.Rectangle.Bottom - pallet.Rectangle.Top);
        //                                double mmPerPixel = 2.0; // Calibration value
        //                                double palletHeightMeters = (heightPixels * mmPerPixel) / 1000.0;
        //                                //PalletHeightTxt.Text = $"{palletHeightMeters:F2} m";
        //                            }
        //                        });
        //                    }
        //                    else
        //                    {
        //                        Console.WriteLine("Error: {0} {1}", grabResult.ErrorCode, grabResult.ErrorDescription);
        //                    }
        //                }
        //            }

        //            // Clean up
        //            isRunning = false;
        //            cts.Cancel();
        //            processingTask.Wait();

        //            camera.StreamGrabber.Stop();
        //            camera.Close();
        //        }
        //    }
        //    catch (Exception e)
        //    {
        //        Console.Error.WriteLine("Exception: {0}", e.Message);

        //    }
        //    finally
        //    {
        //        Console.Error.WriteLine("\nPress enter to exit.");
        //        Console.ReadLine();
        //    }
        //}
        //private Mat GrabResultToMat(IGrabResult grabResult)
        //{
        //    // Get the pixel data as byte array
        //    byte[] buffer = (byte[])grabResult.PixelData;

        //    // Create empty mat with correct dimensions
        //    Mat mat = new Mat(grabResult.Height, grabResult.Width, MatType.CV_8UC1);

        //    // Copy data using Marshal (safe approach)
        //    Marshal.Copy(buffer, 0, mat.Data, buffer.Length);

        //    // Convert grayscale to color (BGR)
        //    Mat colorMat = new Mat();
        //    Cv2.CvtColor(mat, colorMat, ColorConversionCodes.GRAY2BGR);

        //    // Optional: Apply color map for visualization (requires OpenCvSharp 4.x)
        //    // Mat colored = new Mat();
        //    // Cv2.ApplyColorMap(colorMat, colored, ColormapTypes.Jet);
        //    // return colored;

        //    return colorMat;
        //}

        private void ProcessBoxFromLiveCamera()
        {
            string rtspUrl = "rtsp://admin:ChShani%40786@192.168.24.110:554/Streaming/Channels/102/";

            ShowStatus(BoxCountingStatusText, "Connecting to camera...");
            Thread.Sleep(500);

            var videoCapture = new VideoCapture(rtspUrl);
            if (!videoCapture.IsOpened())
            {
                ShowStatus(BoxCountingStatusText, "Failed to connect to RTSP sub-stream.");
                return;
            }

            ShowStatus(BoxCountingStatusText, "Connected. Starting detection...");
            Thread.Sleep(1000);
            HideStatus(BoxCountingStatusText);

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
                    BoxCountingCameraFeed.Source = bitmapImage;
                    NoBoxTxt.Text = $"Boxes Detected: {filtered.Count}";
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
                ShowStatus(BoxCountingStatusText, "Loading image...");
                Thread.Sleep(500);

                string imagePath = "Assets/boxes.png"; // Path to your image file
                if (!File.Exists(imagePath))
                {
                    MessageBox.Show("Image file not found!");
                    return;
                }

                ShowStatus(BoxCountingStatusText, "Image loaded. Initializing detection...");
                Thread.Sleep(500);
                HideStatus(BoxCountingStatusText);

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
                    BoxCountingCameraFeed.Source = bitmapImage;
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
        #endregion


    }
}
