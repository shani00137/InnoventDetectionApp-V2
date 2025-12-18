
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
using System.ComponentModel;
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
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tesseract;
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

        private YoloScorer<YoloCocoP5Model> _scorerHumanModel;
        private YoloScorer<YoloCustomModel> _scorerBoxCountingModel;
        private SixLabors.Fonts.Font _font;
        private IAudioManager audioManager;
        private bool _isAlertPlaying = false;
        private bool _isSidebarOpen = false;
        private readonly AccessController _ac = new AccessController("192.168.127.254");

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
        private ScaleSerialReader? _reader;
        private  OcrPythonClient _ocrHost;
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
            LoadModels();

        }
        private async void MainWindow_LoadedAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                string pythonExe = @"C:\Users\Abhishaik Sharma\AppData\Local\Programs\Python\Python310\python.exe";
                string scriptPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "reader.py");

                _ocrHost = new OcrPythonClient(pythonExe, scriptPath);

                ResultDataList = new ObservableCollection<ResutlModel>();
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
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            StopScale();
        }

        private async void LoadModels()
        {
            var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions();
            try
            {
                sessionOptions.AppendExecutionProvider_DML();

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
      


        private async Task CaptureAndDisplayAllCamerasAsync()
        {
            try
            {
              
               
                await StartBlower();
                AddResult(DateTime.Now, null, null, null, null, null, false, true);

                var cameraList = CameraFinder.Enumerate();

                var images = await CaptureSingleFrameFromAllCamerasAsync(cameraList);

                Dispatcher.Invoke(() =>
                {
                    CapturedImages.Clear();
                    foreach (var img in images)
                        CapturedImages.Add(img);
                });
                StartScale();
                LoadingOverlay.Visibility = Visibility.Visible;
                ProgressTxt.Text = "Please Wait.."
                ; await RunAllAIDetectionsAsync(images);
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ResultDialoag.UpdateResults(ResultDataList);
                Panel.SetZIndex(ResultDialoag, 1); // ensure on top


                // ✅ show popup

                PictureDialog.Visibility = Visibility.Collapsed;
                ResultDialoag.Visibility = Visibility.Visible;
                LivePreviewDialoag.Visibility = Visibility.Collapsed;
                ImageDialogHost.IsOpen = true;
                await StopBuzzer();
                await OffBlower();
                await OffRotatorAsync();
            }
            catch (Exception ex)
            {
                await StopBuzzer();
                await OffBlower();
                await OffRotatorAsync();
            }
           



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
                            Task.Delay(100);
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
            if (capturedImages == null || capturedImages.Count < 1)
            {
                MessageBox.Show("❌ Not enough images to process AI models.");
                return;
            }
            int NumberOfBox = 0;
            double PalletHeight = 0;
            string OcrTxt = "";
            bool HumanDetected = false;
            // Convert BitmapImages to ImageSharp format for inference
            var imageSharpList = capturedImages.Select(img => BitmapImageToImageSharp(img)).ToList();
            var ocrBatchTask = RunOCRBatchAsync(imageSharpList);

            for (int i = 0; i < imageSharpList.Count; i++)
            {
                // 🧩 Parallel run — each model runs independently
                var boxTask = Task.Run(() => RunBoxCountingModel(imageSharpList[i]));
                var humanTask = Task.Run(() => RunHumanDetectionModel(imageSharpList[i]));
                //var ocrTask = Task.Run(() => RunOCRModel(imageSharpList[i]));

                await Task.WhenAll(boxTask, humanTask);

                var boxResult = boxTask.Result;
                var humanResult = humanTask.Result;
                //var ocrResult = ocrTask.Result;
                NumberOfBox += boxResult.BoxesDetected;
                PalletHeight = boxResult.PalletHeight;
                //OcrTxt += ocrResult;
                HumanDetected = humanResult == true ? true : true;
                if (HumanDetected == true)
                {
                    await StartBuzzer();
                }
            }


            var ocrResults = await ocrBatchTask;

           
            for (int i = 0; i < ocrResults.Length; i++)
            {
                OcrTxt += ocrResults[i] + Environment.NewLine;
            }

            // 🔔 Update UI with combined results
            Dispatcher.Invoke(() =>
            {
                NoBoxTxt.Text = $"Boxes: {NumberOfBox}";
                PalletHeightTxt.Text = $" {PalletHeight:F2} m";
                AddResult(null, DateTime.Now, NumberOfBox, PalletHeight, "", OcrTxt, HumanDetected, false);

            });
        }
        private (int BoxesDetected, double PalletHeight) RunBoxCountingModel(Image<Rgba32> image)
        {
            UpdateProgressStatus("Box Counting AI Model Start");
            // 1. Resize input image
            var resizedImage = image.CloneAs<Rgba32>();
            resizedImage.Mutate(x =>
                x.Resize(new ResizeOptions
                {
                    Size = new Size(640, 640),
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Pad,
                    PadColor = Color.Black
                }));

            // 2. Run inference on RGBA image
            var predictions = _scorerBoxCountingModel.Predict(resizedImage)
                .Where(p => p.Score >= 0.20f)
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
        private async Task<string[]> RunOCRBatchAsync(List<Image<Rgba32>> images)
        {
            string tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempFolder);

            try
            {
                for (int i = 0; i < images.Count; i++)
                {
                    var img = images[i];
                    var filePath = System.IO.Path.Combine(tempFolder, $"img_{i}.png");
                    img.Save(filePath); // save ImageSharp image as PNG
                }

                var results = await _ocrHost.RunOcrAsync(tempFolder);
                return results.Values.ToArray();
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }



        private string RunOCRModel(Image<Rgba32> image)
        {
            try
            {


                UpdateProgressStatus("OCR AI Model Start");
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
        public string ExtractTextToJson(string imagePath)
        {
            try
            {
                string pythonExe = @"C:\Users\Abhishaik Sharma\AppData\Local\Programs\Python\Python310\python.exe"; // Python path

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

        private void AddResult(DateTime? startDate = null, DateTime? endDate = null, int? boxes = null, double? palletHeight = null, string? weight = null, string remarks = null, bool humandetected = false, bool isNew = false)
        {
            // Find the first existing result
            var resultModel = ResultDataList.OrderByDescending(x => x.StartTime).FirstOrDefault();

            if (resultModel != null && isNew == false)
            {
                // Update only provided fields
                if (boxes.HasValue)
                    resultModel.TotalBoxes = boxes.Value;

                if (palletHeight.HasValue)
                    resultModel.PalletHeight = palletHeight.Value;

                if (!string.IsNullOrWhiteSpace(weight))
                    resultModel.TotalWeight = weight;

                if (!string.IsNullOrWhiteSpace(remarks))
                    resultModel.OCRResult = remarks;

                if (startDate.HasValue)
                    resultModel.StartTime = startDate.Value;

                if (endDate.HasValue)
                    resultModel.EndTime = endDate.Value;

                resultModel.HumanDetect = humandetected == true ? "Yes" : "No";
            }
            else if (isNew == true)
            {
                // Add a new record if none exists
                ResultDataList.Add(new ResutlModel
                {
                    StartTime = startDate ?? DateTime.Now,
                    EndTime = endDate ?? DateTime.Now,
                    TotalBoxes = boxes ?? 0,
                    PalletHeight = palletHeight ?? 0,
                    TotalWeight = string.Empty,
                    OCRResult = remarks ?? string.Empty,
                    HumanDetect = "No"
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
                LivePreviewDialoag.Visibility = Visibility.Collapsed;
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



        }
        public async Task RestartProcess()
        {
            ImageDialogHost.IsOpen = false;
            await StartRotator();
            MaterialDesignThemes.Wpf.DialogHost.CloseDialogCommand.Execute(null, null);
            var allCameras = CameraFinder.Enumerate();
            int cameraCount = allCameras.Count;
            if (cameraCount > 0)
            {
                SetCameraUI(CamFrontLightPulse, cameraCount >= 1);
                CheckPalletStatus(allCameras[0]);
            }
            else {
                MessageBox.Show("No Camera found");
            }
                
        }
        private void Restart_Click(object sender, EventArgs e)
        {
            RestartProcess();
        }
        private void LivePreview_Click(object sender, EventArgs e)
        {
            ImageDialogHost.IsOpen = true;
            LivePreviewDialoag.Visibility = Visibility.Visible;
            ResultDialoag.Visibility = Visibility.Collapsed;
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

        #region manage weight machine events

        private void StartScale()
        {
            if (_reader != null && _reader.IsOpen) return;

            _reader = new ScaleSerialReader
            {
                PortName = "COM3",
                BaudRate = 9600
            };

            _reader.WeightReceived += Reader_WeightReceived;
            _reader.Error += Reader_Error;

            try
            {
                _reader.Start();
            }
            catch (Exception ex)
            {
                WeightText.Text = "ERR";
                // optional: MessageBox.Show(ex.Message);
            }
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

        private void Reader_WeightReceived(object? sender, double w)
        {
            Dispatcher.Invoke(() =>
            {
                WeightText.Text = $"{w:0.##} KG";
                //AddResult(DateTime.Now, null, null, null, WeightText.Text, null, false, false);
            });
        }

        private void Reader_Error(object? sender, Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                WeightText.Text = "ERR";
            });
        }
        #endregion
    }
}
