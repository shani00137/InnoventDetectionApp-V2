using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilites.Weight
{
    public class ScaleSerialReader : IDisposable
    {
        private SerialPort? _port;

        public bool IsOpen => _port?.IsOpen == true;

        public string PortName { get; set; } = "COM3";
        public int BaudRate { get; set; } = 9600;
        public Parity Parity { get; set; } = Parity.None;
        public int DataBits { get; set; } = 8;
        public StopBits StopBits { get; set; } = StopBits.One;

        /// <summary>Example: many indicators use CRLF. Change if needed.</summary>
        public string NewLine { get; set; } = "\r\n";

        public int ReadTimeout { get; set; } = 1000;
        public int WriteTimeout { get; set; } = 1000;
        public Handshake Handshake { get; set; } = Handshake.None;

        /// <summary>
        /// Regex used to extract weight from the incoming line. Adjust based on your scale format.
        /// </summary>
        public Regex WeightRegex { get; set; } = new Regex(@"[-+]?\d+(\.\d+)?", RegexOptions.Compiled);

        public event EventHandler<string>? RawLineReceived;
        public event EventHandler<double>? WeightReceived;
        public event EventHandler<Exception>? Error;

        public void Start()
        {
            if (IsOpen) return;

            _port = new SerialPort(PortName, BaudRate, Parity, DataBits, StopBits)
            {
                Handshake = Handshake,
                NewLine = NewLine,
                ReadTimeout = ReadTimeout,
                WriteTimeout = WriteTimeout
            };

            _port.DataReceived += Port_DataReceived;
            _port.Open();
        }

        public void Stop()
        {
            if (_port == null) return;

            try
            {
                _port.DataReceived -= Port_DataReceived;

                if (_port.IsOpen)
                    _port.Close();

                _port.Dispose();
            }
            finally
            {
                _port = null;
            }
        }

        public void Send(string command)
        {
            if (_port == null || !_port.IsOpen)
                throw new InvalidOperationException("Port is not open.");

            _port.Write(command);
        }

        private void Port_DataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            if (_port == null || !_port.IsOpen) return;

            try
            {
                string line = _port.ReadLine();
                RawLineReceived?.Invoke(this, line);

                var m = WeightRegex.Match(line);
                if (m.Success && double.TryParse(m.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double weight))
                {
                    WeightReceived?.Invoke(this, weight);
                }
            }
            catch (TimeoutException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, ex);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

}
