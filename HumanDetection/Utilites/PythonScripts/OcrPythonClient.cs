using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Utilites.PythonScripts
{
    public class OcrPythonClient : IDisposable
    {
        private Process _pythonProcess;

        public OcrPythonClient(string pythonExe, string scriptPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _pythonProcess = Process.Start(psi) ?? throw new Exception("Failed to start Python process.");
        }

        public async Task<Dictionary<string, string>> RunOcrFromMemoryAsync(
      Dictionary<string, byte[]> images)
        {
            if (_pythonProcess == null || _pythonProcess.HasExited)
                throw new Exception("Python OCR process is not running.");

            var payload = images.ToDictionary(
                x => x.Key,
                x => Convert.ToBase64String(x.Value)
            );

            string json = JsonSerializer.Serialize(payload);

            await _pythonProcess.StandardInput.WriteLineAsync(json);
            await _pythonProcess.StandardInput.FlushAsync();

            string output = await _pythonProcess.StandardOutput.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(output))
                return new Dictionary<string, string>();

            return JsonSerializer.Deserialize<Dictionary<string, string>>(output)
                   ?? new Dictionary<string, string>();
        }


        public void Dispose()
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                _pythonProcess.Kill();
                _pythonProcess.Dispose();
            }
        }
    }

}
