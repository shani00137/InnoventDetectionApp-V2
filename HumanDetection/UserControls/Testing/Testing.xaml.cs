using HumanDetection.Model;
using Microsoft.ML.OnnxRuntime;
using Model;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using SQLite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Utilites.BoxCounting;
using Utilites.CameraSettings;
using Yolov5Net.Scorer;
using Yolov5Net.Scorer.Models;
using Color = SixLabors.ImageSharp.Color;
using ResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;
using Size = SixLabors.ImageSharp.Size;

namespace UserControls.Testing
{
    /// <summary>
    /// Offline testing page: loads 4-5 images from disk and runs the same
    /// box-counting + human-detection + OCR pipeline as the Home page,
    /// without requiring any cameras or hardware.
    /// </summary>
    public partial class Testing : System.Windows.Controls.Page
    {
        private readonly ObservableCollection<TestImageItem> _imageItems = new();

        private YoloScorer<YoloCocoP5Model> _scorerHumanModel;
        private YoloScorer<YoloBoxCountingModel> _scorerBoxCountingModel;

        public Testing()
        {
            InitializeComponent();
            ImagesList.ItemsSource = _imageItems;
        }

        private void SelectImagesBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select 4 to 5 images",
                Multiselect = true,
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp"
            };

            if (dialog.ShowDialog() == true)
            {
                if (dialog.FileNames.Length < 4)
                {
                    MessageBox.Show("Please select at least 4 images.", "Testing",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (dialog.FileNames.Length > 5)
                {
                    MessageBox.Show("Only 5 images can be processed at a time. Using the first 5.",
                        "Testing", MessageBoxButton.OK, MessageBoxImage.Information);
                }

            _imageItems.Clear();
            int idx = 1;
            foreach (var file in dialog.FileNames.Take(5))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(file);
                bmp.EndInit();
                bmp.Freeze();

                _imageItems.Add(new TestImageItem { Image = bmp, Index = idx++ });
            }

                SelectedCountTxt.Text = $"{_imageItems.Count} image(s) selected";
                StartProcessBtn.IsEnabled = true;
            }
        }

        private async void StartProcessBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_imageItems.Count < 4)
            {
                MessageBox.Show("Please select at least 4 images first.", "Testing",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StartProcessBtn.IsEnabled = false;
            SelectImagesBtn.IsEnabled = false;
            ClearBtn.IsEnabled = false;
            ProgressTxt.Text = "Loading AI models...";
            OverlayProgressTxt.Text = "Loading AI models...";
            LoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                bool modelsOk = await LoadModelsAsync();
                if (!modelsOk)
                {
                    ProgressTxt.Text = "Model loading failed";
                    MessageBox.Show("Failed to load AI models. Check that model files exist.",
                        "Testing", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Total timer starts after models are loaded (detection + OCR).
                var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var startTime = DateTime.Now;

                ProgressTxt.Text = "Running detection on selected images...";
                OverlayProgressTxt.Text = "Running detection on selected images...";

                // Convert the selected images to CapturedCameraImage-like inputs
                var captured = _imageItems
                    .Select(item => new CapturedCameraImage { Position = MapPosition(item.Index), Image = item.Image })
                    .ToList();

                // Box counting timer (detection only).
                var boxStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var aiResult = await RunAllAIDetectionsAsync(captured);
                boxStopwatch.Stop();

                ProgressTxt.Text = "Starting OCR service...";

                // Start the OCR service
                await StartOcrServiceAsync();

                ProgressTxt.Text = "Running OCR...";

                // OCR timer (send images + wait for response).
                var ocrStopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Run OCR on cropped box images.
                List<OcrImageResult> ocrResults = new();
                if (aiResult.OCRBytes != null && aiResult.OCRBytes.Any())
                {
                    // Deduplicate identical crops before sending to OCR —
                    // identical images are read the same, so OCR runs once.
                    var uniqueCrops = aiResult.OCRBytes
                        .GroupBy(b => Convert.ToBase64String(b))
                        .Select(g => g.First())
                        .ToList();

                    var api = await RunOcrAsync(uniqueCrops);
                    if (api?.results != null)
                    {
                        ocrResults.AddRange(api.results
                            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                            .Where(x => string.IsNullOrWhiteSpace(x.Value?.error))
                            .Select(x => x.Value));
                    }
                }
                ocrStopwatch.Stop();

                OverlayProgressTxt.Text = "Finalizing results...";

                // ---- Aggregate results ----
                int distinctBarcodes = ocrResults
                    .Where(r => r?.barcodes != null)
                    .SelectMany(r => r.barcodes)
                    .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length > 6)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                var allDateStrings = ocrResults
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
                string allDatesList = string.Join(", ", parsedDates.Select(x => x.Parsed.HasValue ? x.Parsed.Value.ToString("dd.MM.yyyy") : x.Original));
                int barcodeCount = ocrResults
                    .Where(r => r?.barcodes != null)
                    .SelectMany(r => r.barcodes)
                    .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length > 6)
                    .Count();
                var distinctBarcodeList = ocrResults
                    .Where(r => r?.barcodes != null)
                    .SelectMany(r => r.barcodes)
                    .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length > 6)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList();

                // Stop the total timer before displaying.
                totalStopwatch.Stop();

                // ---- Update UI ----
                string ocrString = "";
                StringBuilder sb = new StringBuilder();
                // Performance timing details.
                sb.AppendLine("===== TIMING (after model load) =====");
                sb.AppendLine($"Box Counting Time : {boxStopwatch.Elapsed.TotalSeconds:F2} sec");
                sb.AppendLine($"OCR Time          : {ocrStopwatch.Elapsed.TotalSeconds:F2} sec");
                sb.AppendLine($"Total Time        : {totalStopwatch.Elapsed.TotalSeconds:F2} sec");
                sb.AppendLine();
                sb.AppendLine($"Score: {aiResult.AvScore:P0}");
                sb.AppendLine($"Boxes: {aiResult.NumberOfBox}");
                sb.AppendLine($"Height: {aiResult.maxPalletHeight:F2} m");
                sb.AppendLine($"Human: {(aiResult.HumanDetected ? "Yes" : "No")}");
                sb.AppendLine($"Distinct Barcodes: {distinctBarcodes} ({string.Join(", ", distinctBarcodeList)})");
                sb.AppendLine($"Distinct Dates: {distinctDates} ({allDatesList})");
                sb.AppendLine();
                foreach (var r in ocrResults)
                    sb.AppendLine(OcrResultToString(r));

                // Update the on-screen header time with the total.
                TimeTxt.Text = $"{totalStopwatch.Elapsed.TotalSeconds:F2} sec";

                ScoreTxt.Text = $"{aiResult.AvScore:P0}";
                BoxesTxt.Text = aiResult.NumberOfBox.ToString();
                HeightTxt.Text = $"{aiResult.maxPalletHeight:F2}";
                HumanTxt.Text = aiResult.HumanDetected ? "Yes" : "No";
                BarcodesTxt.Text = distinctBarcodes.ToString();
                DatesTxt.Text = distinctDates.ToString();
                OCRResultBox.Text = sb.ToString();

                // Annotated images
                var annotatedCollection = new ObservableCollection<BitmapImage>();
                if (aiResult.AnnotatedImages != null)
                {
                    foreach (var bytes in aiResult.AnnotatedImages)
                    {
                        using var ms = new MemoryStream(bytes);
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        annotatedCollection.Add(bmp);
                    }
                }
                AnnotatedList.ItemsSource = annotatedCollection;

                // ---- Save result to back office API (auto) ----
                var tokens = new[]
                {
                    "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJwYXlsb2FkIjp7ImVtYWlsIjoia2xwMjF1c2VyQGdtYWlsLmNvbSIsInVzZXJOYW1lIjoia2xwMjEiLCJfaWQiOiI2NmI1YmM3Nzc1MDA5M2U0MWU1ODNiZTYifSwiaWF0IjoxNzg3MTI5Mjk1fQ.77rrCCrNjRjPfCzkuGDo8o7JnbCRyv8SRc_o-ZuA6Ok",
                    "99927ec1-8668-45ae-8709-2db03366e680"
                };

                var payload = new Utilites.PalletAPI.PalletRequest
                {
                    name = "abc",
                    palletWeight = null,
                    palletHeight = aiResult.maxPalletHeight.ToString("F2"),
                    NO_OfBoxs = aiResult.NumberOfBox.ToString(),
                    startTime = startTime.ToString("HH:mm:ss"),
                    endTime = DateTime.Now.ToString("HH:mm:ss"),
                    trustScoreLevel = (aiResult.AvScore * 100).ToString("0"),
                    productionDate = null,
                    exipreDate = allDatesList,
                    barCode = string.Join(", ", distinctBarcodeList),
                    palletCondition = aiResult.AvScore >= 0.7 ? "Good" : "Rejected",
                    humenDetection = aiResult.HumanDetected ? "Yes" : "No",
                    image = null
                };

                var service = new HumanDetection.Utilites.PalletAPI.PalletApiService(
                    tokens[0], // Bearer token
                    tokens[1], // x-api-key
                    "https://adp-backend-demo.ashybay-437ca219.uaenorth.azurecontainerapps.io/core/thing-type/66b9a073b241574cd76f0616/adpPallet");

                await Task.Run(() => service.PostPalletDataAsync(payload));

                ProgressTxt.Text = "Detection complete";
            }
            catch (Exception ex)
            {
                ProgressTxt.Text = "Error";
                MessageBox.Show($"Detection failed: {ex.Message}", "Testing",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                StartProcessBtn.IsEnabled = true;
                SelectImagesBtn.IsEnabled = true;
                ClearBtn.IsEnabled = true;
            }
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            _imageItems.Clear();
            ImagesList.ItemsSource = null;
            ImagesList.ItemsSource = _imageItems;
            AnnotatedList.ItemsSource = null;
            SelectedCountTxt.Text = "Select 4 to 5 images";
            ScoreTxt.Text = "-";
            BoxesTxt.Text = "-";
            HeightTxt.Text = "-";
            HumanTxt.Text = "-";
            BarcodesTxt.Text = "-";
            DatesTxt.Text = "-";
            OCRResultBox.Text = "";
            TimeTxt.Text = "-";
            ProgressTxt.Text = "";
            StartProcessBtn.IsEnabled = false;
        }

        private CameraPosition MapPosition(int index) => index switch
        {
            1 => CameraPosition.Front,
            2 => CameraPosition.Right,
            3 => CameraPosition.Back,
            4 => CameraPosition.Left,
            _ => CameraPosition.Top
        };

        private async Task<bool> LoadModelsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var sessionOptions = new SessionOptions();
                    try { sessionOptions.AppendExecutionProvider_DML(); }
                    catch { sessionOptions.AppendExecutionProvider_CPU(); }

                    var modelPathBox = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/Weights/customBoxCount.onnx");
                    if (!File.Exists(modelPathBox)) throw new FileNotFoundException("BoxCount model missing");
                    _scorerBoxCountingModel = new YoloScorer<YoloBoxCountingModel>(modelPathBox, sessionOptions);

                    var modelPathHuman = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/Weights/yolov5s.onnx");
                    if (!File.Exists(modelPathHuman)) throw new FileNotFoundException("Human detection model missing");
                    _scorerHumanModel = new YoloScorer<YoloCocoP5Model>(modelPathHuman, sessionOptions);

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Model load error: " + ex.Message);
                    return false;
                }
            });
        }

        private async Task<(double AvScore, bool HumanDetected, int NumberOfBox, List<byte[]> OCRBytes, double maxPalletHeight, List<byte[]> AnnotatedImages)>
            RunAllAIDetectionsAsync(List<CapturedCameraImage> capturedImages)
        {
            if (capturedImages == null || capturedImages.Count < 1)
                return (0.0, false, 0, new List<byte[]>(), 0, new List<byte[]>());

            // Read settings once (avoids repeated DB reads per image)
            double confidenceThreshold = 0.0;
            try
            {
                var settings = SettingsRepository.GetSettings();
                if (settings != null && !string.IsNullOrWhiteSpace(settings.ConfidenceLevel) &&
                    double.TryParse(settings.ConfidenceLevel, out var dbValue))
                {
                    confidenceThreshold = Math.Clamp(dbValue / 100.0, 0.0, 1.0);
                }
            }
            catch { }

            int numberOfBox = 0;
            double maxPalletHeight = 0.0;
            double totalAvgScore = 0.0;
            int avgScoreCount = 0;
            bool humanDetected = false;

            var ocrResults = new System.Collections.Concurrent.ConcurrentBag<byte[]>();
            var annotatedImages = new System.Collections.Concurrent.ConcurrentBag<byte[]>();
            var frontBoxes = new List<YoloPrediction>();
            var topBoxes = new List<YoloPrediction>();
            var lockObj = new object();

            // Process all images SEQUENTIALLY.
            // The DirectML execution provider is NOT reliably thread-safe for
            // concurrent Run() calls; parallel inference corrupts GPU memory and
            // throws AccessViolationException. Awaited (sequential) inference
            // matches the working Home page behavior.
            foreach (var captured in capturedImages)
            {
                PalletSide side = MapPalletSide(captured.Position);
                using var image = BitmapImageToImageSharp(captured.Image);

                // Box counting first...
                var boxResult = await RunBoxCountingModelAsync(image, side, confidenceThreshold);
                // ...then human detection, also awaited (no concurrency).
                var humanResult = await Task.Run(() => RunHumanDetectionModel(image));

                lock (lockObj)
                {
                    if (boxResult.AverageScore > 0)
                    {
                        totalAvgScore += boxResult.AverageScore;
                        avgScoreCount++;
                    }
                    if (boxResult.PalletHeightMeters > maxPalletHeight)
                        maxPalletHeight = boxResult.PalletHeightMeters;

                    humanDetected |= humanResult;

                    if (boxResult.BoxesImages != null)
                        foreach (var b in boxResult.BoxesImages) ocrResults.Add(b);
                    if (boxResult.AnnotatedImage != null)
                        annotatedImages.Add(boxResult.AnnotatedImage);

                    if (side == PalletSide.Front) frontBoxes = boxResult.BoxPredictions ?? new();
                    if (side == PalletSide.Top) topBoxes = boxResult.BoxPredictions ?? new();
                }
            }

            double finalAverageScore = avgScoreCount > 0 ? totalAvgScore / avgScoreCount : 0.0;

            int topBoxCount = topBoxes.Count();
            int frontRows = BoxCountingService.CountTopRows(frontBoxes);
            numberOfBox = topBoxCount == 0 ? 1 : topBoxCount * frontRows;

            return (finalAverageScore, humanDetected, numberOfBox,
                    ocrResults.ToList(), maxPalletHeight, annotatedImages.ToList());
        }

        private PalletSide MapPalletSide(CameraPosition position) => position switch
        {
            CameraPosition.Front => PalletSide.Front,
            CameraPosition.Right => PalletSide.Right,
            CameraPosition.Back => PalletSide.Back,
            CameraPosition.Left => PalletSide.Left,
            CameraPosition.Top => PalletSide.Top,
            _ => PalletSide.Front
        };

        private async Task<ImagePredictionResult> RunBoxCountingModelAsync(Image<Rgba32> originalImage, PalletSide side, double confidenceThreshold = 0.0)
        {
            var resizedImage = originalImage.CloneAs<Rgba32>();
            int modelWidth = 640;
            int modelHeight = 640;
            resizedImage.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(modelWidth, modelHeight),
                Mode = ResizeMode.Pad,
                PadColor = Color.Black
            }));

            var rawPredictions = _scorerBoxCountingModel
                .Predict(resizedImage)
                .Where(p => p.Score >= confidenceThreshold)
                .ToList();

            var palletCandidates = rawPredictions
                .Where(p => p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var tallestPallet = palletCandidates
                .OrderByDescending(p => p.Rectangle.Height)
                .FirstOrDefault();

            var predictions = rawPredictions
                .Where(p => !p.Label.Name.Equals("pallet", StringComparison.OrdinalIgnoreCase))
                .ToList();

            double palletHeightMeters = 0.0;
            if (tallestPallet != null)
            {
                double metersPerPixel = 0.002626;
                float palletPixelHeight = tallestPallet.Rectangle.Height;
                float originalAspect = (float)originalImage.Height / originalImage.Width;
                float contentHeightIn640 = modelWidth * originalAspect;
                float padScaleFactor = contentHeightIn640 < modelHeight ? contentHeightIn640 / modelHeight : 1.0f;
                float effectivePixelHeight = palletPixelHeight / padScaleFactor;
                palletHeightMeters = Math.Clamp(effectivePixelHeight * metersPerPixel, 0.0, 3.0);
            }

            double averageScore = 0.0;
            var boxPredictions = predictions
                .Where(p => p.Label.Name.Equals("box", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (boxPredictions.Any())
                averageScore = boxPredictions.Average(p => p.Score);
            else if (palletCandidates.Any())
                averageScore = palletCandidates.Average(p => p.Score);

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

                int squareSize = Math.Min(Math.Max(width, height), Math.Min(originalImage.Width, originalImage.Height));
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
                crop.SaveAsJpeg(ms);
                cropByteList.Add(ms.ToArray());
            }

            double palletAngleDeg = tallestPallet?.Score * 100 ?? 0;
            double finalScore;
            if (palletAngleDeg > 0 && averageScore > 0) finalScore = (palletAngleDeg + averageScore) / 2;
            else if (palletAngleDeg > 0) finalScore = palletAngleDeg;
            else finalScore = averageScore;

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
                        string labelText = isPallet
                            ? $"pallet {p.Score:P1} h={palletHeightMeters:F2}m"
                            : $"{p.Label.Name} {p.Score:P1}";
                        var textLocation = new SixLabors.ImageSharp.PointF(
                            p.Rectangle.X + 5,
                            Math.Max(0, p.Rectangle.Y - 25));
                        var textBgRect = new SixLabors.ImageSharp.RectangleF(
                            textLocation.X - 3, textLocation.Y - 3, labelText.Length * 9, 22);
                        ctx.Fill(Color.FromRgba(0, 0, 0, 180), textBgRect);
                        ctx.DrawText(labelText, font, color, textLocation);
                    });
                }

                using var ms2 = new MemoryStream();
                annotated.SaveAsPng(ms2);
                annoBytes = ms2.ToArray();
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
            var predictions = _scorerHumanModel.Predict(image);
            return predictions.Any(p =>
                (p.Label.Name.Equals("person", StringComparison.OrdinalIgnoreCase) ||
                 p.Label.Name.Equals("human", StringComparison.OrdinalIgnoreCase) ||
                 p.Label.Name.Equals("man", StringComparison.OrdinalIgnoreCase) ||
                 p.Label.Name.Equals("woman", StringComparison.OrdinalIgnoreCase))
                && p.Score >= 0.82f);
        }

        private async Task StartOcrServiceAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(30);

                    // Warm up / start the OCR service so model loading there
                    // does not count against the OCR timing. Uses an empty
                    // probe request; a connection is enough to verify it is up.
                    var probe = new MultipartFormDataContent();
                    var empty = new ByteArrayContent(Array.Empty<byte>());
                    empty.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                    probe.Add(empty, "images", "probe.jpg");

                    var response = client.PostAsync("http://127.0.0.1:5000/ocr", probe)
                        .GetAwaiter().GetResult();
                    response.Dispose();
                }
                catch
                {
                    // OCR service may already be running or probing may be
                    // ignored; non-fatal — the real request happens below.
                }
            });
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
                content.Add(byteContent, "images", $"capture_{index++}.jpg");
            }

            var response = await client.PostAsync("http://127.0.0.1:5000/ocr", content);
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
            return Image.Load<Rgba32>(memory);
        }

        private string OcrResultToString(OcrImageResult result)
        {
            if (result == null) return "No OCR result";

            var sb = new StringBuilder();
            if (result.barcodes?.Any() == true)
            {
                sb.AppendLine("Barcodes:");
                foreach (var b in result.barcodes)
                    sb.AppendLine($"  * {b}");
                sb.AppendLine();
            }
            if (result.dates?.Any() == true)
            {
                sb.AppendLine("Dates:");
                foreach (var d in result.dates)
                    sb.AppendLine($"  * {d}");
                sb.AppendLine();
            }
            if (result.raw_text?.Any() == true)
            {
                sb.AppendLine("Text:");
                foreach (var t in result.raw_text)
                    sb.AppendLine($"  * {t}");
            }
            return sb.ToString();
        }
    }

    public class TestImageItem
    {
        public int Index { get; set; } = 0;
        public BitmapImage Image { get; set; }
    }
}
