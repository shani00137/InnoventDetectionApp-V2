using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ACGPUIO
{
    public class AccessController : IDisposable
    {
        private readonly string _moxaIP;
        private const int ModbusPort = 502;

        // Prevents concurrent DO command collisions on the Modbus socket
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        // Tracks the background "auto-off" task started by StartRotatorAsync
        private CancellationTokenSource _rotatorAutoOffCts;

        // Optional logging hook — wire this to your logger (Console, Serilog, etc.)
        public Action<string> Log { get; set; } = _ => { };

        public AccessController(string moxaIP = "192.168.1.135")
        {
            _moxaIP = moxaIP;
        }

        // -----------------------------------------------------------------
        // CORE MODBUS/TCP IMPLEMENTATION
        // -----------------------------------------------------------------
        public async Task<bool> SendDOCommand(int channel, int status)
        {
            await _lock.WaitAsync();
            try
            {
                using (var client = new TcpClient())
                {
                    // Set short timeouts to avoid freezing during network drops
                    client.SendTimeout = 3000;
                    client.ReceiveTimeout = 3000;

                    await client.ConnectAsync(_moxaIP, ModbusPort);

                    using (var stream = client.GetStream())
                    {
                        // Build standard Modbus/TCP Application Protocol frame (FC05: Write Single Coil)
                        byte[] frame = new byte[12];

                        // Transaction Identifier (Arbitrary, using 0x0001)
                        frame[0] = 0x00; frame[1] = 0x01;
                        // Protocol Identifier (Always 0x0000 for Modbus)
                        frame[2] = 0x00; frame[3] = 0x00;
                        // Length (Number of remaining bytes in the frame: 6 bytes)
                        frame[4] = 0x00; frame[5] = 0x06;
                        // Unit Identifier (Moxa default is usually 1)
                        frame[6] = 0x01;
                        // Function Code (0x05 = Write Single Coil)
                        frame[7] = 0x05;
                        // Coil Address (Moxa DO channels map directly: Channel 0 = 0x0000, Channel 1 = 0x0001, etc.)
                        frame[8] = (byte)((channel >> 8) & 0xFF);
                        frame[9] = (byte)(channel & 0xFF);
                        // Output Value (0xFF00 turns Coil ON, 0x0000 turns Coil OFF)
                        frame[10] = (byte)(status == 1 ? 0xFF : 0x00);
                        frame[11] = 0x00;

                        // Send the command frame
                        await stream.WriteAsync(frame, 0, frame.Length);

                        // Read response frame (Modbus/TCP response echo back is 12 bytes long)
                        byte[] response = new byte[12];
                        int bytesRead = await stream.ReadAsync(response, 0, response.Length);

                        if (bytesRead >= 12 && response[7] == 0x05)
                        {
                            Log($"[AccessController] Modbus success: DO{channel} set to {(status == 1 ? "ON" : "OFF")}");
                            return true;
                        }

                        Log($"[AccessController] Modbus invalid response payload received for DO{channel}.");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[AccessController] SendDOCommand({channel},{status}) Modbus Exception: {ex.Message}");
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        // Deprecated web mechanics safely kept as structural stubs to preserve interface signature
        [Obsolete("Web tokens are no longer used. Modbus handles authentication natively via IP connection.")]
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
            // 1. Cancel any previous auto-off timer
            _rotatorAutoOffCts?.Cancel();
            _rotatorAutoOffCts = new CancellationTokenSource();
            var token = _rotatorAutoOffCts.Token;

            // 2. Decide speed step based on weight threshold (e.g., 500 kg)
            double heavyThreshold = 500.0;
            if (currentWeight >= heavyThreshold)
            {
                Log($"[AccessController] Heavy pallet detected ({currentWeight}kg). Setting VFD to SLOW preset.");
                // Turn on the VFD's multi-step speed input
                await SendDOCommand(3, 1);
            }
            else
            {
                Log($"[AccessController] Light pallet detected ({currentWeight}kg). Keeping VFD at FAST preset.");
                // Ensure the multi-step speed input is off
                await SendDOCommand(3, 0);
            }

            // 3. Start the motor (DO1)
            bool motorStarted = await SendDOCommand(1, 1);
            if (!motorStarted) Log("[AccessController] Failed to send START command to VFD.");

            // 4. Background task to automatically stop the motor after exactly 5200 ms
            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait for your exact runtime logic
                    await Task.Delay(5200, token);

                    // Turn off the motor and clear the speed preset relay
                    await SendDOCommand(1, 0); // Stop motor
                    await SendDOCommand(3, 0); // Clear speed modifier
                    Log("[AccessController] Rotator cycle finished. Motor Stopped.");
                }
                catch (TaskCanceledException)
                {
                    Log("[AccessController] Rotator cycle was interrupted/cancelled.");
                }
            }, token);
        }
        public void Dispose()
        {
            _rotatorAutoOffCts?.Cancel();
            _rotatorAutoOffCts?.Dispose();
            _lock?.Dispose();
        }
    }
}