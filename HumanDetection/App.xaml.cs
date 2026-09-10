using HumanDetection.Services;
using System;
using System.IO;
using System.Text;
using System.Windows;
using Serilog;
using SQLite;

namespace HumanDetection
{
    public static class Logger
    {
        private static readonly string logFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "app_log_.txt");

        public static ILogger Log { get; private set; } = Serilog.Log.Logger;

        public static void Initialize()
        {
            var logDir = Path.GetDirectoryName(logFilePath);
            Directory.CreateDirectory(logDir);

            Serilog.Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.File(
                    path: logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            Log = Serilog.Log.Logger;
            Log.Information("=== Application started ===");
        }

        public static void LogException(Exception ex, string context = "")
        {
            Log.Error(ex, "{Context} Exception: {Message}", context, ex.Message);
        }
    }

    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Logger.Initialize();
            Database.Initialize();
            SetupGlobalExceptionHandling();
            base.OnStartup(e);

            // Show the splash screen first; it loads all global resources
            // (AI models, Moxa, weight machine, OCR) before the main window opens.
            var splash = new SplashScreen();
            MainWindow = splash;
            splash.Show();
        }

        private void SetupGlobalExceptionHandling()
        {
            this.DispatcherUnhandledException += (s, args) =>
            {
                Logger.LogException(args.Exception, "DispatcherUnhandledException");
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Logger.LogException(ex, "AppDomain.UnhandledException");
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Logger.LogException(args.Exception, "TaskScheduler.UnobservedTaskException");
                args.SetObserved();
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Release globally-shared native resources (ONNX inference sessions,
            // OCR python process) before the process exits.
            try { AppModels.Unload(); } catch { }
            try { OcrProcessService.Current.Shutdown(); } catch { }

            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
