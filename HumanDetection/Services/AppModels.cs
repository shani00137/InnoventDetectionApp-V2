using Microsoft.ML.OnnxRuntime;
using SixLabors.Fonts;
using System;
using System.IO;
using System.Threading.Tasks;
using Yolov5Net.Scorer;
using Yolov5Net.Scorer.Models;

namespace HumanDetection.Services
{
    /// <summary>
    /// Global, application-wide AI model container. The splash screen loads these
    /// ONCE so every screen (Home, Testing, LivePreview, ...) reuses the same
    /// inference sessions instead of each loading its own copy of the ONNX weights.
    /// </summary>
    public static class AppModels
    {
        private static readonly object Gate = new object();

        public static YoloScorer<YoloBoxCountingModel> BoxCounting { get; private set; }
        public static YoloScorer<YoloCocoP5Model> HumanDetection { get; private set; }
        public static Font AnnotationFont { get; private set; }
        public static bool IsLoaded { get; private set; }
        public static string LastError { get; private set; } = string.Empty;

        /// <summary>
        /// Loads the box-counting + human-detection YOLO models and the annotation
        /// font on a background thread. Safe to call multiple times.
        /// </summary>
        public static async Task<bool> LoadAsync(Action<string> status = null)
        {
            lock (Gate)
            {
                if (IsLoaded) return true;
            }

            return await Task.Run(() =>
            {
                try
                {
                    status?.Invoke("Creating DirectML inference session...");
                    var sessionOptions = new SessionOptions();
                    try
                    {
                        sessionOptions.AppendExecutionProvider_DML();
                    }
                    catch
                    {
                        sessionOptions.AppendExecutionProvider_CPU();
                    }

                    var modelPathBoxCounting =
                        Path.Combine(AppContext.BaseDirectory, "Assets/Weights/customBoxCount.onnx");
                    if (!File.Exists(modelPathBoxCounting))
                        throw new FileNotFoundException("BoxCount model missing: " + modelPathBoxCounting);

                    status?.Invoke("Loading Box Counting model...");
                    var box = new YoloScorer<YoloBoxCountingModel>(modelPathBoxCounting, sessionOptions);

                    var modelPathHuman =
                        Path.Combine(AppContext.BaseDirectory, "Assets/Weights/yolov5s.onnx");
                    if (!File.Exists(modelPathHuman))
                        throw new FileNotFoundException("Human detection model missing: " + modelPathHuman);

                    status?.Invoke("Loading Human Detection model...");
                    var human = new YoloScorer<YoloCocoP5Model>(modelPathHuman, sessionOptions);

                    var fontPath = @"C:\Windows\Fonts\consola.ttf";
                    if (!File.Exists(fontPath))
                        throw new FileNotFoundException("Annotation font missing: " + fontPath);

                    status?.Invoke("Preparing annotation font...");
                    var font = new Font(new FontCollection().Add(fontPath), 16);

                    lock (Gate)
                    {
                        BoxCounting?.Dispose();
                        HumanDetection?.Dispose();

                        BoxCounting = box;
                        HumanDetection = human;
                        AnnotationFont = font;
                        IsLoaded = true;
                        LastError = string.Empty;
                    }

                    status?.Invoke("AI models ready.");
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    Logger.LogException(ex, "AppModels.LoadAsync");
                    status?.Invoke("AI model load failed: " + ex.Message);
                    return false;
                }
            });
        }

        /// <summary>
        /// Disposes the shared inference sessions. Called on application exit.
        /// </summary>
        public static void Unload()
        {
            lock (Gate)
            {
                BoxCounting?.Dispose();
                HumanDetection?.Dispose();

                BoxCounting = null;
                HumanDetection = null;
                IsLoaded = false;
            }
        }
    }
}