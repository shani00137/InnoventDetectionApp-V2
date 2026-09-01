using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ACGPUIO
{
    // -----------------------------------------------------------------------
    // Event args for DI sensor state changes
    // -----------------------------------------------------------------------
    public class DIChangedEventArgs : EventArgs
    {
        /// <summary>Which DI channel changed (0-based).</summary>
        public int Channel { get; }

        /// <summary>New state: true = sensor triggered (ON), false = cleared (OFF).</summary>
        public bool IsActive { get; }

        /// <summary>When the change was detected.</summary>
        public DateTime Timestamp { get; }

        public DIChangedEventArgs(int channel, bool isActive)
        {
            Channel = channel;
            IsActive = isActive;
            Timestamp = DateTime.Now;
        }
    }

    public class AccessController : IDisposable
    {
        private readonly string _moxaIP;
        private const int ModbusPort = 502;

        // Prevents concurrent DO command collisions on the Modbus socket
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        // Tracks the background "auto-off" task started by StartRotatorAsync
        private CancellationTokenSource _rotatorAutoOffCts;

        // DI polling
        private CancellationTokenSource _diPollCts;
        private bool _diPollRunning = false;

        // Optional logging hook — wire this to your logger (Console, Serilog, etc.)
        public Action<string> Log { get; set; } = _ => { };

        // -----------------------------------------------------------------------
        // 🔔 EVENT — fires whenever any monitored DI channel changes state
        // Wire this up in Home.xaml.cs:
        //     _ac.DIChanged += OnSensorTriggered;
        // -----------------------------------------------------------------------
        public event EventHandler<DIChangedEventArgs> DIChanged;

        // How often to poll the DI registers (milliseconds).
        // 200ms is responsive enough for a pallet sensor without hammering the device.
        // ⚠️ ASSUMPTION: adjust if your sensor can trigger faster than this interval.
        public int DIPollingIntervalMs { get; set; } = 200;

        public AccessController(string moxaIP = "192.168.1.135")
        {
            _moxaIP = moxaIP;
        }

        // -----------------------------------------------------------------
        // CORE MODBUS/TCP — FC05: Write Single Coil (DO output)
        // -----------------------------------------------------------------
        public async Task<bool> SendDOCommand(int channel, int status)
        {
            await _lock.WaitAsync();
            try
            {
                using var client = new TcpClient();
                client.SendTimeout = 3000;
                client.ReceiveTimeout = 3000;

                await client.ConnectAsync(_moxaIP, ModbusPort);

                using var stream = client.GetStream();

                // FC05: Write Single Coil
                byte[] frame = new byte[12];
                frame[0] = 0x00; frame[1] = 0x01; // Transaction ID
                frame[2] = 0x00; frame[3] = 0x00; // Protocol ID
                frame[4] = 0x00; frame[5] = 0x06; // Length
                frame[6] = 0x01;                    // Unit ID
                frame[7] = 0x05;                    // FC05
                frame[8] = (byte)((channel >> 8) & 0xFF);
                frame[9] = (byte)(channel & 0xFF);
                frame[10] = (byte)(status == 1 ? 0xFF : 0x00);
                frame[11] = 0x00;

                await stream.WriteAsync(frame, 0, frame.Length);

                byte[] response = new byte[12];
                int bytesRead = await stream.ReadAsync(response, 0, response.Length);

                if (bytesRead >= 12 && response[7] == 0x05)
                {
                    Log($"[AccessController] Modbus success: DO{channel} → {(status == 1 ? "ON" : "OFF")}");
                    return true;
                }

                Log($"[AccessController] Modbus invalid response for DO{channel}.");
                return false;
            }
            catch (Exception ex)
            {
                Log($"[AccessController] SendDOCommand({channel},{status}) error: {ex.Message}");
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        // -----------------------------------------------------------------
        // NEW: FC02 — Read Discrete Input (DI sensor state)
        // Returns true if the DI channel is currently ON (sensor triggered),
        // false if OFF, null if the read failed.
        // ⚠️ ASSUMPTION: Moxa E2210 maps DI channels at Modbus address 0x0000
        // onward (DI0=0, DI1=1, ...). Verify in your E2210 manual under
        // "Modbus Mapping Table" — some firmware versions offset by 0x0800.
        // -----------------------------------------------------------------
        public async Task<bool?> ReadDIAsync(int channel)
        {
            try
            {
                using var client = new TcpClient();
                client.SendTimeout = 3000;
                client.ReceiveTimeout = 3000;

                await client.ConnectAsync(_moxaIP, ModbusPort);

                using var stream = client.GetStream();

                // FC02: Read Discrete Inputs — read 1 coil from the given channel address
                byte[] request = new byte[12];
                request[0] = 0x00; request[1] = 0x02; // Transaction ID
                request[2] = 0x00; request[3] = 0x00; // Protocol ID
                request[4] = 0x00; request[5] = 0x06; // Length
                request[6] = 0x01;                      // Unit ID
                request[7] = 0x02;                      // FC02: Read Discrete Inputs
                request[8] = (byte)((channel >> 8) & 0xFF);
                request[9] = (byte)(channel & 0xFF);
                request[10] = 0x00; request[11] = 0x01;  // Read 1 coil

                await stream.WriteAsync(request, 0, request.Length);

                // Response: header(9 bytes) + byte count(1) + data byte(1)
                byte[] response = new byte[10];
                int bytesRead = await stream.ReadAsync(response, 0, response.Length);

                if (bytesRead >= 10 && response[7] == 0x02)
                {
                    // Byte 9 = data byte count (should be 1)
                    // Byte 10 would be the coil status byte — bit 0 = channel state
                    // But response is only 10 bytes (0-9), so data is at index 9
                    bool isActive = (response[9] & 0x01) == 1;
                    Log($"[AccessController] DI{channel} read: {(isActive ? "ON" : "OFF")}");
                    return isActive;
                }

                Log($"[AccessController] ReadDIAsync({channel}): unexpected response.");
                return null;
            }
            catch (Exception ex)
            {
                Log($"[AccessController] ReadDIAsync({channel}) error: {ex.Message}");
                return null;
            }
        }

        // -----------------------------------------------------------------
        // NEW: FC04 — Read Input Register (AI analog input)
        // Returns the raw 16-bit analog value (0-65535), or null if the read failed.
        // ⚠️ ASSUMPTION: Moxa ioLogik maps AI channels at Modbus input register
        // address 0x0000 onward (AI0=0, AI1=1, ...). Verify in your device's
        // "Modbus Address Mapping" — some firmware versions offset by 0x0800 or
        // store the value already scaled in engineering units instead of raw.
        // -----------------------------------------------------------------
        public async Task<int?> ReadAIAsync(int channel)
        {
            try
            {
                using var client = new TcpClient();
                client.SendTimeout = 3000;
                client.ReceiveTimeout = 3000;

                await client.ConnectAsync(_moxaIP, ModbusPort);

                using var stream = client.GetStream();

                // FC04: Read Input Registers — read 1 register from the given channel address
                byte[] request = new byte[12];
                request[0] = 0x00; request[1] = 0x03; // Transaction ID
                request[2] = 0x00; request[3] = 0x00; // Protocol ID
                request[4] = 0x00; request[5] = 0x06; // Length
                request[6] = 0x01;                      // Unit ID
                request[7] = 0x04;                      // FC04: Read Input Registers
                request[8] = (byte)((channel >> 8) & 0xFF);
                request[9] = (byte)(channel & 0xFF);
                request[10] = 0x00; request[11] = 0x01;  // Read 1 register

                await stream.WriteAsync(request, 0, request.Length);

                // Response: header(9 bytes) + byte count(1) + register data(2 bytes)
                byte[] response = new byte[11];
                int bytesRead = await stream.ReadAsync(response, 0, response.Length);

                if (bytesRead >= 11 && response[7] == 0x04 && response[8] == 0x02)
                {
                    int value = (response[9] << 8) | response[10]; // big-endian 16-bit
                    Log($"[AccessController] AI{channel} read: {value}");
                    return value;
                }

                Log($"[AccessController] ReadAIAsync({channel}): unexpected response.");
                return null;
            }
            catch (Exception ex)
            {
                Log($"[AccessController] ReadAIAsync({channel}) error: {ex.Message}");
                return null;
            }
        }

        // -----------------------------------------------------------------
        // NEW: Start polling a DI channel — fires DIChanged event when the
        // sensor state changes (OFF→ON or ON→OFF).
        //
        // Usage in Home.xaml.cs:
        //
        //   _ac.DIChanged += (s, e) =>
        //   {
        //       if (e.Channel == 0 && e.IsActive)
        //       {
        //           // Sensor triggered — pallet detected!
        //           Dispatcher.Invoke(() => StartPalletDetectionProcAsync());
        //       }
        //   };
        //
        //   await _ac.StartDIPollingAsync(channel: 0);
        //
        // Call StopDIPolling() when you no longer need it.
        // ⚠️ ASSUMPTION: you have ONE sensor on DI0. If you have multiple
        // sensors on different DI channels, call StartDIPollingAsync() once
        // per channel — each call runs its own independent polling loop.
        // -----------------------------------------------------------------
        public async Task StartDIPollingAsync(int channel = 0)
        {
            if (_diPollRunning)
            {
                Log("[AccessController] DI polling already running — call StopDIPolling() first.");
                return;
            }

            _diPollCts = new CancellationTokenSource();
            _diPollRunning = true;

            Log($"[AccessController] DI polling started on channel {channel} every {DIPollingIntervalMs}ms.");

            bool? lastState = null;

            await Task.Run(async () =>
            {
                while (!_diPollCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        bool? current = await ReadDIAsync(channel);

                        if (current.HasValue)
                        {
                            // Only fire the event when state actually CHANGES
                            // (not on every poll) — avoids flooding your handler.
                            if (lastState == null || current.Value != lastState.Value)
                            {
                                lastState = current.Value;
                                Log($"[AccessController] DI{channel} state changed → {(current.Value ? "ON (triggered)" : "OFF (cleared)")}");

                                // Fire on the thread pool — handler can marshal to UI
                                // thread itself with Dispatcher.Invoke if needed.
                                DIChanged?.Invoke(this, new DIChangedEventArgs(channel, current.Value));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[AccessController] DI polling error: {ex.Message}");
                    }

                    try
                    {
                        await Task.Delay(DIPollingIntervalMs, _diPollCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }

                _diPollRunning = false;
                Log($"[AccessController] DI polling stopped on channel {channel}.");

            }, _diPollCts.Token);
        }

        // -----------------------------------------------------------------
        // Stop DI polling
        // -----------------------------------------------------------------
        public void StopDIPolling()
        {
            _diPollCts?.Cancel();
            _diPollRunning = false;
            Log("[AccessController] DI polling stop requested.");
        }

        // -----------------------------------------------------------------
        // Deprecated web mechanics — kept as stubs to preserve interface
        // -----------------------------------------------------------------
        [Obsolete("Web tokens no longer used. Modbus handles auth natively.")]
        public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(20);
        public int TokenRefreshMaxAttempts { get; set; } = 3;
        public TimeSpan TokenRefreshRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        public async Task<bool> RefreshToken() => await Task.FromResult(true);

        // -------------------------
        //      BUZZER (DO0)
        // -------------------------
        public async Task StartBuzzerAsync()
        {
            bool ok = await SendDOCommand(0, 1);
            if (!ok) Log("[AccessController] StartBuzzerAsync failed.");
        }

        public async Task OffBuzzerAsync()
        {
            bool ok = await SendDOCommand(0, 0);
            if (!ok) Log("[AccessController] OffBuzzerAsync failed.");
        }

        // -------------------------
        //      BLOWER (DO2)
        // -------------------------
        public async Task StartBlowerAsync()
        {
            bool ok = await SendDOCommand(2, 1);
            if (!ok) Log("[AccessController] StartBlowerAsync failed.");
        }

        public async Task OffBlowerAsync()
        {
            bool ok = await SendDOCommand(2, 0);
            if (!ok) Log("[AccessController] OffBlowerAsync failed.");
        }

        // -------------------------
        //      ROTATOR CONTROL
        // -------------------------
        public async Task StartRotatorAsync()
        {
            _rotatorAutoOffCts?.Cancel();
            _rotatorAutoOffCts = new CancellationTokenSource();
            var token = _rotatorAutoOffCts.Token;

            bool ok1 = await SendDOCommand(1, 1);
            if (!ok1) Log("[AccessController] StartRotatorAsync: channel 1 ON failed.");

            await Task.Delay(7000, CancellationToken.None);

            bool ok3 = await SendDOCommand(3, 1);
            if (!ok3) Log("[AccessController] StartRotatorAsync: channel 3 ON failed.");

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(4000, token);
                    await SendDOCommand(1, 0);
                    await SendDOCommand(3, 0);
                }
                catch (TaskCanceledException)
                {
                    Log("[AccessController] StartRotatorAsync auto-off cancelled (superseded).");
                }
            }, token);
        }

        public async Task TurnOnRotatorAsync()
        {
            _rotatorAutoOffCts?.Cancel();
            bool ok = await SendDOCommand(1, 1);
            if (!ok) Log("[AccessController] TurnOnRotatorAsync failed.");
        }

        public async Task StartRotatorForDurationAsync(int Seconds)
        {
            _rotatorAutoOffCts?.Cancel();
            _rotatorAutoOffCts = new CancellationTokenSource();
            var token = _rotatorAutoOffCts.Token;

            bool ok1 = await SendDOCommand(1, 1);
            if (!ok1) Log("[AccessController] StartRotatorForDurationAsync: channel 1 ON failed.");

            await Task.Delay(5000, CancellationToken.None);

            bool ok3 = await SendDOCommand(3, 1);
            if (!ok3) Log("[AccessController] StartRotatorForDurationAsync: channel 3 ON failed.");

            await Task.Delay(Seconds, token);

            _ = Task.Run(async () =>
            {
                try
                {
                    await SendDOCommand(1, 0);
                    await SendDOCommand(3, 0);
                }
                catch (Exception ex)
                {
                    Log($"[AccessController] StartRotatorForDurationAsync auto-off error: {ex.Message}");
                }
            }, CancellationToken.None);
        }

        public async Task OffRotatorAsync()
        {
            _rotatorAutoOffCts?.Cancel();
            bool ok1 = await SendDOCommand(1, 0);
            bool ok3 = await SendDOCommand(3, 0);
            if (!ok1 || !ok3)
                Log("[AccessController] OffRotatorAsync: one or more channels failed to turn off.");
        }

        public async Task StartRotatorWithWeightAsync(double currentWeight)
        {
            _rotatorAutoOffCts?.Cancel();
            _rotatorAutoOffCts = new CancellationTokenSource();
            var token = _rotatorAutoOffCts.Token;

            double heavyThreshold = 500.0;
            if (currentWeight >= heavyThreshold)
            {
                Log($"[AccessController] Heavy pallet ({currentWeight}kg) → SLOW preset.");
                await SendDOCommand(3, 1);
            }
            else
            {
                Log($"[AccessController] Light pallet ({currentWeight}kg) → FAST preset.");
                await SendDOCommand(3, 0);
            }

            bool motorStarted = await SendDOCommand(1, 1);
            if (!motorStarted) Log("[AccessController] Failed to send START to VFD.");

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(5200, token);
                    await SendDOCommand(1, 0);
                    await SendDOCommand(3, 0);
                    Log("[AccessController] Rotator cycle finished.");
                }
                catch (TaskCanceledException)
                {
                    Log("[AccessController] Rotator cycle cancelled.");
                }
            }, token);
        }

        public void Dispose()
        {
            StopDIPolling();
            _diPollCts?.Dispose();
            _rotatorAutoOffCts?.Cancel();
            _rotatorAutoOffCts?.Dispose();
            _lock?.Dispose();
        }
    }
}