using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HumanDetection.Services
{
    /// <summary>
    /// Owns the single PaddleOCR python HTTP service (ocr_api.py on port 5000).
    /// The splash screen starts it once; the whole application shares it instead
    /// of Home (or any other screen) killing and re-starting it on every visit.
    /// </summary>
    public sealed class OcrProcessService
    {
        public static OcrProcessService Current { get; } = new OcrProcessService();

        private Process _process;
        private readonly object _gate = new object();

        public string PythonExe { get; set; } = @"C:\Users\Owner\AppData\Local\Programs\Python\Python310\python.exe";

        public bool IsRunning { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        public Task<bool> EnsureStartedAsync() => EnsureStartedAsync(null);

        /// <summary>
        /// Ensures the OCR flask service is up. Reuses an already-running instance.
        /// </summary>
        public async Task<bool> EnsureStartedAsync(Action<string> status)
        {
            lock (_gate)
            {
                if (IsRunning || (_process != null && !_process.HasExited))
                {
                    status?.Invoke("OCR service already running.");
                    return true;
                }
            }

            try
            {
                status?.Invoke("Cleaning OCR port 5000...");
                KillProcessesUsingPort(5000);

                status?.Invoke("Starting OCR service...");
                _process = StartPythonApi("ocr_api.py");

                bool alive = await WaitForApiAsync("http://127.0.0.1:5000/ocr");
                lock (_gate) IsRunning = alive;

                if (alive)
                {
                    status?.Invoke("OCR service ready.");
                }
                else
                {
                    LastError = "OCR service did not respond in time.";
                    status?.Invoke(LastError);
                }

                return alive;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Logger.LogException(ex, "OcrProcessService.EnsureStartedAsync");
                status?.Invoke("OCR service failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Polls the OCR health URL until it responds (or timeout).
        /// </summary>
        private async Task<bool> WaitForApiAsync(string url)
        {
            using var http = new HttpClient();
            for (int i = 0; i < 45; i++)
            {
                try
                {
                    var resp = await http.GetAsync(url);
                    // OCR routes only POST /ocr, so GET returns 405 → that means it is alive.
                    if (resp.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                        return true;
                    if (resp.IsSuccessStatusCode)
                        return true;
                }
                catch
                {
                    // service still starting
                }

                await Task.Delay(1000);
            }

            return false;
        }

        private Process StartPythonApi(string scriptName)
        {
            var psi = new ProcessStartInfo
            {
                FileName = PythonExe,
                Arguments = scriptName,
                WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "Assets"),
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
                    Debug.WriteLine("[ocr_api] " + e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Debug.WriteLine("[ocr_api ERR] " + e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        /// <summary>
        /// Kills whatever currently listens on the given TCP port.
        /// </summary>
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
                    var parts = Regex.Split(line.Trim(), @"\s+");
                    if (parts.Length >= 5 && int.TryParse(parts[^1], out int pid))
                    {
                        try
                        {
                            Process.GetProcessById(pid).Kill(true);
                            Debug.WriteLine($"Killed process PID {pid} on port {port}");
                        }
                        catch
                        {
                            // already gone or no permission
                        }
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Stops the OCR python process. Called on application exit.
        /// </summary>
        public void Shutdown()
        {
            lock (_gate)
            {
                IsRunning = false;
                try
                {
                    if (_process != null && !_process.HasExited)
                        _process.Kill(true);
                }
                catch
                {
                }

                _process = null;
            }
        }
    }
}