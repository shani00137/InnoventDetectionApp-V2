using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ACGPUIO
{
    // -----------------------------------------------------------------------
    // Event args for AI (analog input) value changes
    // -----------------------------------------------------------------------
    public class AIChangedEventArgs : EventArgs
    {
        /// <summary>Which AI channel was read (0-based).</summary>
        public int Channel { get; }

        /// <summary>Raw 16-bit value (0–65535) read from the register.</summary>
        public int RawValue { get; }

        /// <summary>When the reading was taken.</summary>
        public DateTime Timestamp { get; }

        public AIChangedEventArgs(int channel, int rawValue)
        {
            Channel = channel;
            RawValue = rawValue;
            Timestamp = DateTime.Now;
        }
    }

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

        // Single shared, persistent Modbus TCP connection used by all polling
        // loops (DI + AI) so they never fight over competing sockets. Serialized
        // via _lock above.
        private TcpClient? _sharedClient;
        private NetworkStream? _sharedStream;
        private readonly object _connLock = new object();

        // Tracks the background "auto-off" task started by StartRotatorAsync
        private CancellationTokenSource _rotatorAutoOffCts;

        // DI polling
        private CancellationTokenSource _diPollCts;
        private bool _diPollRunning = false;

        // AI polling
        private CancellationTokenSource _aiPollCts;
        private bool _aiPollRunning = false;

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
        // Shared Modbus TCP connection — lazily created, reused, auto-repaired.
        // Callers must hold _lock before using _sharedStream.
        // -----------------------------------------------------------------
        private NetworkStream GetSharedStream()
        {
            lock (_connLock)
            {
                if (_sharedClient == null || !_sharedClient.Connected || _sharedStream == null)
                {
                    _sharedClient?.Dispose();
                    _sharedStream?.Dispose();

                    _sharedClient = new TcpClient();
                    _sharedClient.SendTimeout = 3000;
                    _sharedClient.ReceiveTimeout = 3000;

                    // Use ConnectAsync with a timeout so an unreachable Moxa
                    // can't block background polling threads for a long time.
                    if (!_sharedClient.ConnectAsync(_moxaIP, ModbusPort).Wait(1500))
                    {
                        _sharedClient.Dispose();
                        _sharedClient = null;
                        _sharedStream = null;
                        throw new TimeoutException("Modbus connection timed out.");
                    }

                    _sharedClient.NoDelay = true;
                    _sharedStream = _sharedClient.GetStream();
                }
                return _sharedStream;
            }
        }

        // -----------------------------------------------------------------
        // Reads one holding/input register over the shared connection.
        // Returns the raw value or null.
        // -----------------------------------------------------------------
        private async Task<int?> ReadRegisterSharedAsync(byte function, int address)
        {
            await _lock.WaitAsync();
            try
            {
                var stream = GetSharedStream();

                byte[] request = new byte[12];
                request[0] = 0x00; request[1] = 0x04; // Transaction ID
                request[2] = 0x00; request[3] = 0x00; // Protocol ID
                request[4] = 0x00; request[5] = 0x06; // Length
                request[6] = 0x01;                      // Unit ID
                request[7] = function;                  // FC03 / FC04
                request[8] = (byte)((address >> 8) & 0xFF);
                request[9] = (byte)(address & 0xFF);
                request[10] = 0x00; request[11] = 0x01; // Read 1 register

                await stream.WriteAsync(request, 0, request.Length);

                byte[] response = new byte[11];
                int bytesRead = await stream.ReadAsync(response, 0, response.Length);

                if (bytesRead >= 11 && response[7] == function && response[8] == 0x02)
                {
                    return (response[9] << 8) | response[10];
                }
                return null;
            }
            catch
            {
                // Connection may have dropped — close so GetSharedStream repairs it next time
                lock (_connLock)
                {
                    _sharedClient?.Dispose();
                    _sharedClient = null;
                    _sharedStream = null;
                }
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        // -----------------------------------------------------------------
        // Reads one DI (discrete input) over the shared connection.
        // Returns true/false or null on failure.
        // -----------------------------------------------------------------
        private async Task<bool?> ReadDISharedAsync(int channel)
        {
            await _lock.WaitAsync();
            try
            {
                var stream = GetSharedStream();

                byte[] request = new byte[12];
                request[0] = 0x00; request[1] = 0x02; // Transaction ID
                request[2] = 0x00; request[3] = 0x00; // Protocol ID
                request[4] = 0x00; request[5] = 0x06; // Length
                request[6] = 0x01;                      // Unit ID
                request[7] = 0x02;                      // FC02: Read Discrete Inputs
                request[8] = (byte)((channel >> 8) & 0xFF);
                request[9] = (byte)(channel & 0xFF);
                request[10] = 0x00; request[11] = 0x01; // Read 1 coil

                await stream.WriteAsync(request, 0, request.Length);

                byte[] response = new byte[10];
                int bytesRead = await stream.ReadAsync(response, 0, response.Length);

                if (bytesRead >= 10 && response[7] == 0x02)
                {
                    return (response[9] & 0x01) == 1;
                }
                return null;
            }
            catch
            {
                lock (_connLock)
                {
                    _sharedClient?.Dispose();
                    _sharedClient = null;
                    _sharedStream = null;
                }
                return null;
            }
            finally
            {
                _lock.Release();
            }
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
        // FC03/FC04 — single register read (used for AI analog input).
        // Tries FC04 (Read Input Registers) then FC03 (Read Holding Registers)
        // at several common Moxa ioLogik offsets, because analog-input mapping
        // varies by model/firmware. Returns raw 16-bit value + the raw response
        // bytes so the caller can diagnose when nothing matches.
        // -----------------------------------------------------------------
        private async Task<(int? Value, byte[] Raw)> ReadRegisterAsync(byte function, int address)
        {
            using var client = new TcpClient();
            client.SendTimeout = 3000;
            client.ReceiveTimeout = 3000;

            await client.ConnectAsync(_moxaIP, ModbusPort);

            using var stream = client.GetStream();

            byte[] request = new byte[12];
            request[0] = 0x00; request[1] = 0x04; // Transaction ID
            request[2] = 0x00; request[3] = 0x00; // Protocol ID
            request[4] = 0x00; request[5] = 0x06; // Length
            request[6] = 0x01;                      // Unit ID
            request[7] = function;                  // FC03 / FC04
            request[8] = (byte)((address >> 8) & 0xFF);
            request[9] = (byte)(address & 0xFF);
            request[10] = 0x00; request[11] = 0x01;  // Read 1 register

            await stream.WriteAsync(request, 0, request.Length);

            // Response: header(9 bytes) + byte count(1) + register data(2 bytes)
            byte[] response = new byte[11];
            int bytesRead = await stream.ReadAsync(response, 0, response.Length);

            byte[] raw = new byte[bytesRead];
            Array.Copy(response, raw, bytesRead);

            if (bytesRead >= 11 && response[7] == function && response[8] == 0x02)
            {
                // Big-endian 16-bit register value
                return ((response[9] << 8) | response[10], raw);
            }

            if (bytesRead == 9 && (response[7] == (function | 0x80)))
            {
                // Modbus exception frame: func|0x80 + exception code
                Log($"[AccessController] FC{function:X2} @0x{address:X4} → Modbus exception {response[8]:X2}.");
            }
            else
            {
                Log($"[AccessController] FC{function:X2} @0x{address:X4} → unexpected reply: {BitConverter.ToString(raw)}");
            }

            return (null, raw);
        }

        // -----------------------------------------------------------------
        // AI — analog input. Returns the raw 16-bit value (0-65535) or null.
        // VERIFIED mapping for ioLogik E1242-T: AI0=reg 512 (0x0200), AI1=513,
        // ... read via FC04 (Read Input Registers). We probe FC04 then FC03 at
        // the known 0x0200 offset plus common fallbacks (0x0000, 0x0800) and log
        // every valid reply, preferring a non-zero register (a stuck-at-0
        // register is usually status, not the wired analog channel).
        // -----------------------------------------------------------------
        public async Task<int?> ReadAIAsync(int channel)
        {
            int[] functions = { 0x04, 0x03 };
            int[] offsets = { 0x0200, 0x0000, 0x0800 };

            var valid = new List<(string Desc, int Value)>();

            foreach (var function in functions)
            {
                foreach (var offset in offsets)
                {
                    int address = offset + channel;
                    try
                    {
                        (int? value, _) = await ReadRegisterAsync((byte)function, address);
                        if (value.HasValue)
                        {
                            string desc = $"FC{function:X2} @0x{address:X4}";
                            Log($"[AccessController] AI{channel} {desc} → {value.Value}");
                            valid.Add((desc, value.Value));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[AccessController] AI{channel} FC{function:X2} @0x{address:X4} error: {ex.Message}");
                    }
                }
            }

            if (valid.Count > 0)
            {
                // Prefer the first register that isn't stuck at 0 — that is most
                // likely the wired analog channel. Fall back to any valid reply.
                var nonZero = valid.FirstOrDefault(v => v.Value != 0);
                var chosen = nonZero.Desc != null ? nonZero : valid[0];

                Log($"[AccessController] AI{channel} using {chosen.Desc} (value {chosen.Value}). " +
                    "Vary the input voltage — if this register changes, it is the AI channel.");
                return chosen.Value;
            }

            Log($"[AccessController] AI{channel} FAILED — no FC03/FC04 address combination returned the analog value. " +
                "Known mapping for E124x is FC04 @0x0200 + channel (AI0=0x0200, AI1=0x0201, ...).");
            return null;
        }

        // -----------------------------------------------------------------
        // FAST path — reads one AI channel directly at the single verified
        // register (FC04 @ 0x0200 + channel) over the shared connection.
        // Used by StartAIPollingAsync so the analog loop stays light and does
        // not starve the DI polling loop that runs concurrently on the same
        // connection (the 6-way probe in ReadAIAsync is only done once at
        // startup in the AI-00 port check).
        // -----------------------------------------------------------------
        public async Task<int?> ReadAIRawSharedAsync(int channel)
        {
            int address = 0x0200 + channel;
            return await ReadRegisterSharedAsync(0x04, address);
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
        // NOTE: This method returns immediately — it does NOT block until the
        // polling loop stops. The loop runs in the background. Callers can use
        // `await` to get back right away, or fire-and-forget.
        public Task StartDIPollingAsync(int channel = 0)
        {
            if (_diPollRunning)
            {
                Log("[AccessController] DI polling already running — call StopDIPolling() first.");
                return Task.CompletedTask;
            }

            _diPollCts = new CancellationTokenSource();
            _diPollRunning = true;

            Log($"[AccessController] DI polling started on channel {channel} every {DIPollingIntervalMs}ms.");

            bool? lastState = null;

            Task.Run(async () =>
            {
                while (!_diPollCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        bool? current = await ReadDISharedAsync(channel);

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

            return Task.CompletedTask;
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

        // -----------------------------------------------------------------------
        // 🔔 EVENT — fires whenever an AI (analog input) channel is read
        // Wire this up in Home.xaml.cs:
        //     _ac.AIChanged += OnAIChanged;
        // -----------------------------------------------------------------------
        public event EventHandler<AIChangedEventArgs> AIChanged;

        // How often to poll the AI registers (milliseconds).
        public int AIPollingIntervalMs { get; set; } = 500;

        // -----------------------------------------------------------------
        // Start polling an AI channel — fires AIChanged event on every read.
        // Usage in Home.xaml.cs:
        //
        //   _ac.AIChanged += OnAIChanged;
        //   await _ac.StartAIPollingAsync(channel: 0);
        //
        // Call StopAIPolling() when you no longer need it.
        // -----------------------------------------------------------------
        // NOTE: This method returns immediately — it does NOT block until the
        // polling loop stops. The loop runs in the background. Callers can use
        // `await` to get back right away, or fire-and-forget.
        public Task StartAIPollingAsync(int channel = 0)
        {
            if (_aiPollRunning)
            {
                Log("[AccessController] AI polling already running — call StopAIPolling() first.");
                return Task.CompletedTask;
            }

            _aiPollCts = new CancellationTokenSource();
            _aiPollRunning = true;

            Log($"[AccessController] AI polling started on channel {channel} every {AIPollingIntervalMs}ms.");

            Task.Run(async () =>
            {
                while (!_aiPollCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        int? value = await ReadAIRawSharedAsync(channel);
                        if (value.HasValue)
                        {
                            AIChanged?.Invoke(this, new AIChangedEventArgs(channel, value.Value));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[AccessController] AI polling error: {ex.Message}");
                    }

                    try
                    {
                        await Task.Delay(AIPollingIntervalMs, _aiPollCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }

                _aiPollRunning = false;
                Log($"[AccessController] AI polling stopped on channel {channel}.");

            }, _aiPollCts.Token);

            return Task.CompletedTask;
        }

        // -----------------------------------------------------------------
        // Stop AI polling
        // -----------------------------------------------------------------
        public void StopAIPolling()
        {
            _aiPollCts?.Cancel();
            _aiPollRunning = false;
            Log("[AccessController] AI polling stop requested.");
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
            StopAIPolling();
            _diPollCts?.Dispose();
            _aiPollCts?.Dispose();
            _rotatorAutoOffCts?.Cancel();
            _rotatorAutoOffCts?.Dispose();
            _lock?.Dispose();
            lock (_connLock)
            {
                _sharedClient?.Dispose();
                _sharedClient = null;
                _sharedStream?.Dispose();
                _sharedStream = null;
            }
        }
    }
}