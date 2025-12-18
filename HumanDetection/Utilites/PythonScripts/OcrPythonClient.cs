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

        public async Task<Dictionary<string, string>> RunOcrAsync(string folderPath)
        {
            if (_pythonProcess == null || _pythonProcess.HasExited)
                throw new Exception("Python OCR process is not running.");

            await _pythonProcess.StandardInput.WriteLineAsync(folderPath);
            await _pythonProcess.StandardInput.FlushAsync();

            string output = await _pythonProcess.StandardOutput.ReadLineAsync();

            if (string.IsNullOrEmpty(output))
                return new Dictionary<string, string>();

            return JsonSerializer.Deserialize<Dictionary<string, string>>(output);
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
