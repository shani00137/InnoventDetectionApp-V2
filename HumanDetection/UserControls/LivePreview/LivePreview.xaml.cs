using Basler.Pylon;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.WpfExtensions;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Yolov5Net.Scorer;
using Yolov5Net.Scorer.Models;
using Color = SixLabors.ImageSharp.Color;
using FontStyle = SixLabors.Fonts.FontStyle;
using Size = SixLabors.ImageSharp.Size;


namespace UserControls.LivePreview
{
    /// <summary>
    /// Interaction logic for LivePreview.xaml
    /// </summary>
    public partial class LivePreview : Page
    {
        private YoloScorer<YoloCocoP5Model> _scorerHumanModel;
        private YoloScorer<YoloBoxCountingModel> _scorerBoxCountingModel;
        private Font _font;

        private CancellationTokenSource _cts;
        private DateTime _lastOcrTime = DateTime.MinValue;
        private CancellationTokenSource _processingCts;

        public LivePreview()
        {
            InitializeComponent();
        }

        private async void LivePreview_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowLoading("Loading AI models...");

                // Load YOLO models + font on background thread
                await Task.Run(LoadModels);

                ShowLoading("Detecting Basler cameras...");

                var cameras = CameraFinder.Enumerate();
                int camCount = cameras.Count;

                if (camCount < 3)
                {
                    HideLoading();
                    MessageBox.Show($"Only {camCount} camera(s) detected. 3 are required (Box, Human, OCR).",
                        "Camera Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Map cameras by index:
                // 0 = box counting, 1 = human, 2 = OCR
                var boxCamInfo = cameras[0];
                var humanCamInfo = cameras[1];
                var ocrCamInfo = cameras[2];

                _cts = new CancellationTokenSource();

                ShowLoading("Starting live AI preview...");

                // Start independent loops per camera
                _ = Task.Run(() => ProcessBoxWithPylonCamera());
                //_ = Task.Run(() => CameraLoopHuman(humanCamInfo, _cts.Token));
                //_ = Task.Run(() => CameraLoopOcr(ocrCamInfo, _cts.Token));

                // small delay so first frames begin
                await Task.Delay(800);

                HideLoading();
            }
            catch (Exception ex)
            {
                HideLoading();
                MessageBox.Show($"Live preview init failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LivePreview_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch { }
        }

        #region Model & Camera

        private void LoadModels()
        {
            var sessionOptions = new SessionOptions();
            try
            {
                sessionOptions.AppendExecutionProvider_DML();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DirectML not available, falling back to CPU: {ex.Message}");
                sessionOptions.AppendExecutionProvider_CPU();
            }

            var modelPathBoxCounting = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets/Weights/customBoxCount.onnx");
            _scorerBoxCountingModel = new YoloScorer<YoloBoxCountingModel>(modelPathBoxCounting, sessionOptions);

            var modelPathHumanDetection = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets/Weights/yolov5s.onnx");
            _scorerHumanModel = new YoloScorer<YoloCocoP5Model>(modelPathHumanDetection, sessionOptions);

            var fontPath = @"C:\Windows\Fonts\consola.ttf";
            _font = new Font(new FontCollection().Add(fontPath), 16);
        }
        private void ProcessBoxWithPylonCamera()
        {
            try
            {
                _processingCts = new CancellationTokenSource();

                using (Camera camera = new Camera())
                {
                    Console.WriteLine("Using device: {0}", camera.CameraInfo[CameraInfoKey.ModelName]);
                    Console.WriteLine();

                    camera.CameraOpened += Basler.Pylon.Configuration.AcquireContinuous;
                    camera.Open();
                    camera.Parameters[PLCameraInstance.MaxNumBuffer].SetValue(5);

                    // Background processing flag (kept from your original code)
                    var cts = new CancellationTokenSource();
                    bool isRunning = true;

                    Task processingTask = Task.Run(() =>
                    {
                        while (isRunning && !cts.Token.IsCancellationRequested)
                        {
                            Thread.Sleep(10);
                        }
                    });

                    camera.StreamGrabber.Start();

                    while (!_processingCts.Token.IsCancellationRequested)
                    {
                        IGrabResult grabResult = camera.StreamGrabber.RetrieveResult(5000, TimeoutHandling.ThrowException);
                        using (grabResult)
                        {
                            if (grabResult.GrabSucceeded)
                            {
                                // ✅ Start status (same as your first snippet)
                               

                                // ✅ Convert the grab result to OpenCV Mat
                                Mat frame = GrabResultToMat(grabResult);

                                // (Optional) you can still skip every other frame if needed
                                // for performance, but I’m leaving it simple/always-process here.

                                // ✅ Convert Mat -> Bitmap
                                using var cloned = frame.Clone();
                                using var bitmap = cloned.ToBitmap();

                                // ✅ Convert Bitmap -> ImageSharp image
                                using var ms = new MemoryStream();
                                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                                ms.Position = 0;
                                using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

                                // ✅ Step 1: Preprocess image (resize to 640x640 with pad)
                                using var resizedImage = image.Clone(ctx =>
                                    ctx.Resize(new ResizeOptions
                                    {
                                        Size = new Size(640, 640),
                                        PadColor = Color.Black
                                    }));

                                // ✅ Step 2: YOLOv5 ONNX model inference (same logic)
                                var predictions = _scorerBoxCountingModel
                                    .Predict(resizedImage)
                                    .Where(p => p.Score >= 0.60f)
                                    .ToList();

                                int boxCount = predictions.Count(p =>
                                    p.Label.Name.Equals("box", StringComparison.OrdinalIgnoreCase));

                                var pallet = predictions.FirstOrDefault(p =>
                                    p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase));

                                double palletHeightMeters = 0.0;
                                if (pallet != null)
                                {
                                    int heightPixels = (int)(pallet.Rectangle.Bottom - pallet.Rectangle.Top);
                                    double mmPerPixel = 2.0; // ✅ Same calibration as your other function
                                    palletHeightMeters = (heightPixels * mmPerPixel) / 1000.0;
                                }

                                // ✅ Step 3: Draw detection boxes on resizedImage
                                using var annotated = resizedImage.Clone();
                                var colorBox = new Rgba32(0, 255, 0);      // Green for box
                                var colorPallet = new Rgba32(255, 64, 64); // Soft red for pallet
                                var font = SixLabors.Fonts.SystemFonts.CreateFont(
                                    "Arial", 14, SixLabors.Fonts.FontStyle.Bold);

                                foreach (var p in predictions)
                                {
                                    var color = p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase)
                                        ? colorPallet
                                        : colorBox;

                                    annotated.Mutate(x =>
                                    {
                                        // Bold/thick rectangle
                                        x.Draw(color, 6, p.Rectangle);

                                        var labelText = $"{p.Label.Name} {p.Score:P1}";
                                        var textLocation = new SixLabors.ImageSharp.PointF(
                                            p.Rectangle.X + 5,
                                            p.Rectangle.Y - 25);

                                        var textBgRect = new SixLabors.ImageSharp.RectangleF(
                                            textLocation.X - 3, textLocation.Y - 3,
                                            labelText.Length * 9, 22);

                                        x.Fill(SixLabors.ImageSharp.Color.FromRgba(0, 0, 0, 180), textBgRect);
                                        x.DrawText(labelText, font, color, textLocation);
                                    });
                                }

                                // ✅ Step 4: Convert annotated ImageSharp image → WPF BitmapImage
                                using var outStream = new MemoryStream();
                                annotated.SaveAsPng(outStream);
                                outStream.Position = 0;

                                var bitmapImage = new BitmapImage();
                                bitmapImage.BeginInit();
                                bitmapImage.StreamSource = outStream;
                                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                                bitmapImage.EndInit();
                                bitmapImage.Freeze();

                                // ✅ Step 5: Update WPF UI (preview + text values)
                                Dispatcher.Invoke(() =>
                                {
                                    // Show annotated frame
                                    BoxPreviewImage.Source = bitmapImage;

                                    // If you want to show counts / height in UI:
                                    // NoBoxTxt.Text = $"Boxes Detected: {boxCount}";
                                    // PalletHeightTxt.Text = $"{palletHeightMeters:F2} m";
                                });
                            }
                            else
                            {
                                Console.WriteLine("Error: {0} {1}", grabResult.ErrorCode, grabResult.ErrorDescription);
                            }
                        }
                    }

                    // Clean up
                    isRunning = false;
                    cts.Cancel();
                    processingTask.Wait();

                    camera.StreamGrabber.Stop();
                    camera.Close();
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Exception: {0}", e.Message);
            }
            finally
            {
                Console.Error.WriteLine("\nPress enter to exit.");
                Console.ReadLine();
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
        // --------- CAMERA LOOP: BOX COUNTING (CAM 0) ----------
        private async Task CameraLoopBox(ICameraInfo cameraInfo, CancellationToken token)
        {
            try
            {
                using (var camera = new Camera(cameraInfo))
                {
                    camera.CameraOpened += Basler.Pylon.Configuration.AcquireContinuous;
                    camera.Open();
                    camera.Parameters[PLCameraInstance.MaxNumBuffer].SetValue(8);
                    camera.StreamGrabber.Start();

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            using (IGrabResult grabResult =
                                camera.StreamGrabber.RetrieveResult(5000, TimeoutHandling.ThrowException))
                            {
                                if (!grabResult.GrabSucceeded)
                                    continue;

                                using Mat mat = GrabResultToMat(grabResult);
                                using var bmp = mat.ToBitmap();

                                Image<Rgba32> imageSharp = BitmapToImageSharp(bmp);

                                // Box detection + overlay for left preview
                                BitmapImage boxPreview = CreateBoxCountingPreview(imageSharp);

                                Dispatcher.Invoke(() =>
                                {
                                    BoxPreviewImage.Source = boxPreview;
                                });

                                imageSharp.Dispose();
                            }
                        }
                        catch (TimeoutException)
                        {
                            // just continue
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[LivePreview Box] Frame error: {ex.Message}");
                        }

                        await Task.Delay(20, token); // small delay
                    }

                    camera.StreamGrabber.Stop();
                    camera.Close();
                }
            }
            catch (OperationCanceledException)
            {
                // normal when leaving page
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LivePreview Box] Camera loop error: {ex.Message}");
            }
        }

        // --------- CAMERA LOOP: HUMAN DETECTION (CAM 1) ----------
        private async Task CameraLoopHuman(ICameraInfo cameraInfo, CancellationToken token)
        {
            try
            {
                using (var camera = new Camera(cameraInfo))
                {
                    camera.CameraOpened += Basler.Pylon.Configuration.AcquireContinuous;
                    camera.Open();
                    camera.Parameters[PLCameraInstance.MaxNumBuffer].SetValue(8);
                    camera.StreamGrabber.Start();

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            using (IGrabResult grabResult =
                                camera.StreamGrabber.RetrieveResult(5000, TimeoutHandling.ThrowException))
                            {
                                if (!grabResult.GrabSucceeded)
                                    continue;

                                using Mat mat = GrabResultToMat(grabResult);
                                using var bmp = mat.ToBitmap();

                                Image<Rgba32> imageSharp = BitmapToImageSharp(bmp);

                                // Human detection + overlay for right preview
                                BitmapImage humanPreview = CreateHumanDetectionPreview(imageSharp);

                                Dispatcher.Invoke(() =>
                                {
                                    HumanPreviewImage.Source = humanPreview;
                                });

                                imageSharp.Dispose();
                            }
                        }
                        catch (TimeoutException)
                        {
                            // just continue
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[LivePreview Human] Frame error: {ex.Message}");
                        }

                        await Task.Delay(20, token);
                    }

                    camera.StreamGrabber.Stop();
                    camera.Close();
                }
            }
            catch (OperationCanceledException)
            {
                // normal when leaving page
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LivePreview Human] Camera loop error: {ex.Message}");
            }
        }

        // --------- CAMERA LOOP: OCR (CAM 2) ----------
        private async Task CameraLoopOcr(ICameraInfo cameraInfo, CancellationToken token)
        {
            try
            {
                using (var camera = new Camera(cameraInfo))
                {
                    camera.CameraOpened += Basler.Pylon.Configuration.AcquireContinuous;
                    camera.Open();
                    camera.Parameters[PLCameraInstance.MaxNumBuffer].SetValue(4);
                    camera.StreamGrabber.Start();

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            using (IGrabResult grabResult =
                                camera.StreamGrabber.RetrieveResult(5000, TimeoutHandling.ThrowException))
                            {
                                if (!grabResult.GrabSucceeded)
                                    continue;

                                using Mat mat = GrabResultToMat(grabResult);
                                using var bmp = mat.ToBitmap();

                                Image<Rgba32> imageSharp = BitmapToImageSharp(bmp);

                                // Run OCR roughly every 2 seconds
                                string ocrText = null;
                                if ((DateTime.Now - _lastOcrTime).TotalSeconds > 2)
                                {
                                    _lastOcrTime = DateTime.Now;
                                    ocrText = RunOCRModel(imageSharp);
                                }

                                if (!string.IsNullOrWhiteSpace(ocrText))
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        OcrResultTextBox.Text = ocrText;
                                    });
                                }

                                imageSharp.Dispose();
                            }
                        }
                        catch (TimeoutException)
                        {
                            // just continue
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[LivePreview OCR] Frame error: {ex.Message}");
                        }

                        await Task.Delay(50, token); // OCR camera can be a bit slower
                    }

                    camera.StreamGrabber.Stop();
                    camera.Close();
                }
            }
            catch (OperationCanceledException)
            {
                // normal
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LivePreview OCR] Camera loop error: {ex.Message}");
            }
        }

        private Mat GrabResultToMat(IGrabResult grabResult)
        {
            byte[] buffer = (byte[])grabResult.PixelData;
            Mat mat = new Mat(grabResult.Height, grabResult.Width, MatType.CV_8UC1);
            Marshal.Copy(buffer, 0, mat.Data, buffer.Length);

            Mat colorMat = new Mat();
            Cv2.CvtColor(mat, colorMat, ColorConversionCodes.GRAY2BGR);
            mat.Dispose();
            return colorMat;
        }

        #endregion

        #region Previews

        private BitmapImage CreateBoxCountingPreview(Image<Rgba32> image)
        {
            using var resized = image.Clone(ctx =>
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size(640, 640),
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Pad,
                    PadColor = Color.Black
                }));

            var predictions = _scorerBoxCountingModel.Predict(resized)
                .Where(p => p.Score >= 0.60f)
                .ToList();

            using var annotated = resized.Clone();

            var colorBox = new Rgba32(0, 255, 0);
            var colorPallet = new Rgba32(255, 64, 64);
            var font = SixLabors.Fonts.SystemFonts.CreateFont("Arial", 14, SixLabors.Fonts.FontStyle.Bold);

            foreach (var p in predictions)
            {
                var color = p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase)
                    ? colorPallet
                    : colorBox;

                annotated.Mutate(x =>
                {
                    x.Draw(color, 4, p.Rectangle);

                    var label = $"{p.Label.Name} {p.Score:P1}";
                    var textLocation = new SixLabors.ImageSharp.PointF(p.Rectangle.X + 4, p.Rectangle.Y - 22);
                    var textBgRect = new SixLabors.ImageSharp.RectangleF(
                        textLocation.X - 3, textLocation.Y - 3,
                        label.Length * 9, 22);

                    x.Fill(Color.FromRgba(0, 0, 0, 180), textBgRect);
                    x.DrawText(label, font, color, textLocation);
                });
            }

            return ImageSharpToBitmapImage(annotated);
        }

        private BitmapImage CreateHumanDetectionPreview(Image<Rgba32> image)
        {
            using var resized = image.Clone(ctx =>
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size(640, 640),
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Pad,
                    PadColor = Color.Black
                }));

            var predictions = _scorerHumanModel.Predict(resized)
                .Where(p => p.Score >= 0.40f)
                .Where(p =>
                    p.Label.Name.Equals("person", StringComparison.OrdinalIgnoreCase) ||
                    p.Label.Name.Equals("human", StringComparison.OrdinalIgnoreCase) ||
                    p.Label.Name.Equals("man", StringComparison.OrdinalIgnoreCase) ||
                    p.Label.Name.Equals("woman", StringComparison.OrdinalIgnoreCase))
                .ToList();

            using var annotated = resized.Clone();
            var colorHuman = new Rgba32(0, 128, 255);
            var font = SixLabors.Fonts.SystemFonts.CreateFont("Arial", 14, FontStyle.Bold);

            foreach (var p in predictions)
            {
                annotated.Mutate(x =>
                {
                    x.Draw(colorHuman, 4, p.Rectangle);

                    var label = $"{p.Label.Name} {p.Score:P1}";
                    var textLocation = new SixLabors.ImageSharp.PointF(p.Rectangle.X + 4, p.Rectangle.Y - 22);
                    var textBgRect = new SixLabors.ImageSharp.RectangleF(
                        textLocation.X - 3, textLocation.Y - 3,
                        label.Length * 9, 22);

                    x.Fill(Color.FromRgba(0, 0, 0, 180), textBgRect);
                    x.DrawText(label, font, colorHuman, textLocation);
                });
            }

            return ImageSharpToBitmapImage(annotated);
        }

        #endregion

        #region OCR

        private string RunOCRModel(Image<Rgba32> image)
        {
            try
            {
                using var ms = new MemoryStream();
                image.SaveAsPng(ms);
                ms.Position = 0;

                string tempPath = Path.Combine(Path.GetTempPath(), $"ocr_live_{Guid.NewGuid()}.png");
                File.WriteAllBytes(tempPath, ms.ToArray());

                string pythonExe = @"C:\Users\Abhishaik Sharma\AppData\Local\Programs\Python\Python310\python.exe";
                var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "reader.py");

                return RunOCR(pythonExe, scriptPath, tempPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LivePreview OCR] {ex.Message}");
                return string.Empty;
            }
        }

        private string RunOCR(string pythonExe, string scriptPath, string imagePath)
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
                    Debug.WriteLine("Python OCR Error: " + errors);

                return output.Trim();
            }
        }

        #endregion

        #region Helpers

        private Image<Rgba32> BitmapToImageSharp(System.Drawing.Bitmap bitmap)
        {
            using var memory = new MemoryStream();
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
            memory.Position = 0;
            return SixLabors.ImageSharp.Image.Load<Rgba32>(memory);
        }

        private BitmapImage ImageSharpToBitmapImage(Image<Rgba32> image)
        {
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            ms.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(ms.ToArray());
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void ShowLoading(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingText.Text = message;
            });
        }

        private void HideLoading()
        {
            Dispatcher.Invoke(() =>
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            });
        }

        #endregion
    }
}
