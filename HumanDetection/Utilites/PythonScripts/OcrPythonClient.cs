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

            // Read exactly one JSON line response
            string? output = await _pythonProcess.StandardOutput.ReadLineAsync();

            // If python wrote error to stderr, grab it for debugging
            if (string.IsNullOrWhiteSpace(output))
            {
                string err = await _pythonProcess.StandardError.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(err))
                    throw new Exception("Python STDERR: " + err);

                return new Dictionary<string, string>();
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(output);
            return dict ?? new Dictionary<string, string>();
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
