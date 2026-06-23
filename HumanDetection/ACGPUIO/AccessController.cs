using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ACGPUIO
{
    public class AccessController : IDisposable
    {
        private readonly HttpClient _http;
        private string _token = "";
        private readonly string _moxaIP;

        // Prevents concurrent token refresh / DO command races
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        // Tracks the background "auto-off" task started by StartRotatorAsync,
        // so a second call doesn't fight with the first one.
        private CancellationTokenSource _rotatorAutoOffCts;

        // Optional logging hook — wire this to your logger (Console, Serilog, etc.)
        public Action<string> Log { get; set; } = _ => { };

        public AccessController(string moxaIP = "192.168.1.135")
        {
            _moxaIP = moxaIP;
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5) // avoid indefinite hangs on network issues
            };
        }

        // -------------------------
        //  Refresh token from 05_21.htm
        // -------------------------
        public async Task<bool> RefreshToken()
        {
            try
            {
                // This page returns the HTML form that contains "token" hidden input
                string url = $"http://{_moxaIP}/05_21.htm?CHANNEL_NO=0";

                string html = await _http.GetStringAsync(url);

                var tokenRegex = new Regex("name=\"token\"\\s+value=\"([^\"]+)\"");
                var match = tokenRegex.Match(html);

                if (match.Success)
                {
                    _token = match.Groups[1].Value;
                    Log($"[AccessController] New token acquired: {_token}");
                    return true;
                }

                Log("[AccessController] Token not found in response.");
                return false;
            }
            catch (Exception ex)
            {
                Log($"[AccessController] RefreshToken error: {ex.Message}");
                return false;
            }
        }

        // -------------------------
        //        CORE DO
        // -------------------------
        public async Task<bool> SendDOCommand(int channel, int status)
        {
            // Serialize access so two simultaneous calls don't clobber _token
            // or fire overlapping requests against the same Moxa session.
            await _lock.WaitAsync();
            try
            {
                // 1) If we don't have token yet, get it first
                if (string.IsNullOrEmpty(_token))
                {
                    bool gotToken = await RefreshToken();
                    if (!gotToken)
                    {
                        Log($"[AccessController] SendDOCommand({channel},{status}) aborted: no token.");
                        return false;
                    }
                }

                try
                {
                    string url = BuildDoUrl(channel, status, _token);
                    string resp = await _http.GetStringAsync(url);

                    // 2) If response looks like token/login page again, token might be expired.
                    //    Try refreshing token once and retry.
                    if (IsTokenExpiredResponse(resp))
                    {
                        Log($"[AccessController] Token expired, refreshing for channel {channel}.");

                        bool gotToken = await RefreshToken();
                        if (!gotToken)
                            return false;

                        // retry once with new token
                        url = BuildDoUrl(channel, status, _token);
                        resp = await _http.GetStringAsync(url);

                        if (IsTokenExpiredResponse(resp))
                        {
                            Log($"[AccessController] SendDOCommand({channel},{status}) failed after token retry.");
                            return false;
                        }
                    }

                    // You can also parse resp here to verify success if MOXA returns OK text.
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"[AccessController] SendDOCommand({channel},{status}) error: {ex.Message}");
                    return false;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private string BuildDoUrl(int channel, int status, string token)
        {
            return $"http://{_moxaIP}/set_521.htm" +
                   $"?CHANNEL_NO={channel}" +
                   $"&DO_MODE_C=0" +
                   $"&DO_STATUS_ENABLE={status}" +
                   $"&PWM_LO_C=1&PWM_HI_C=1&PWM_CNT_C=0&PWM_START_C=&DO_VALUE_P=0&PWM_START_P=" +
                   $"&DO_VALUE_S=0&PWM_START_S=&ALIAS_CHANNEL=DO&LOGIC_0=OFF&LOGIC_1=ON" +
                   $"&token={token}";
        }

        // Simple heuristic: if the response contains the token input again,
        // it usually means we were redirected to the form / session expired.
        private bool IsTokenExpiredResponse(string html)
        {
            if (string.IsNullOrEmpty(html))
                return false;

            return html.Contains("name=\"token\"") || html.Contains("LOGIN", StringComparison.OrdinalIgnoreCase);
        }

        // -------------------------
        //     BUZZER (DO0)
        // -------------------------
        public async Task StartBuzzerAsync()
        {
            // DO0 (buzzer) ON
            bool ok = await SendDOCommand(0, 1);
            if (!ok) Log("[AccessController] StartBuzzerAsync failed.");
        }

        public async Task OffBuzzerAsync()
        {
            // DO0 (buzzer) OFF
            bool ok = await SendDOCommand(0, 0);
            if (!ok) Log("[AccessController] OffBuzzerAsync failed.");
        }

        // -------------------------
        //     BLOWER (DO2)
        // -------------------------
        public async Task StartBlowerAsync()
        {
            // DO2 (blower) ON
            bool ok = await SendDOCommand(2, 1);
            if (!ok) Log("[AccessController] StartBlowerAsync failed.");
        }

        public async Task OffBlowerAsync()
        {
            // DO2 (blower) OFF
            bool ok = await SendDOCommand(2, 0);
            if (!ok) Log("[AccessController] OffBlowerAsync failed.");
        }

        public async Task StartRotatorAsync()
        {
            // Cancel any previous pending auto-off so it doesn't turn the
            // rotator off underneath a fresh start.
            _rotatorAutoOffCts?.Cancel();
            _rotatorAutoOffCts = new CancellationTokenSource();
            var token = _rotatorAutoOffCts.Token;

            // DO1 (rotator) ON
            bool ok1 = await SendDOCommand(1, 1);
            if (!ok1) Log("[AccessController] StartRotatorAsync: channel 1 ON failed.");

            await Task.Delay(7000, CancellationToken.None);

            bool ok3 = await SendDOCommand(3, 1);
            if (!ok3) Log("[AccessController] StartRotatorAsync: channel 3 ON failed.");

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(4000, token); // Wait 4 seconds
                    await SendDOCommand(1, 0); // Turn off
                    await SendDOCommand(3, 0); // Turn off
                }
                catch (TaskCanceledException)
                {
                    // Superseded by a newer StartRotatorAsync/TurnOnRotatorAsync/OffRotatorAsync call.
                    Log("[AccessController] StartRotatorAsync auto-off cancelled (superseded).");
                }
            }, token);
        }

        public async Task TurnOnRotatorAsync()
        {
            // Cancel any pending auto-off from a previous StartRotatorAsync call
            _rotatorAutoOffCts?.Cancel();

            // DO1 (rotator) ON
            bool ok = await SendDOCommand(1, 1);
            if (!ok) Log("[AccessController] TurnOnRotatorAsync failed.");
        }

        public async Task StartRotatorForDurationAsync(int Seconds)
        {
            _rotatorAutoOffCts?.Cancel();
            _rotatorAutoOffCts = new CancellationTokenSource();
            var token = _rotatorAutoOffCts.Token;

            // DO1 (rotator) ON
            bool ok1 = await SendDOCommand(1, 1);
            if (!ok1) Log("[AccessController] StartRotatorForDurationAsync: channel 1 ON failed.");

            await Task.Delay(5000, CancellationToken.None);

            bool ok3 = await SendDOCommand(3, 1);
            if (!ok3) Log("[AccessController] StartRotatorForDurationAsync: channel 3 ON failed.");

            // NOTE: kept the original parameter meaning intact (no behavior change
            // to avoid surprising callers), but flagging this clearly:
            // "Seconds" is passed straight into Task.Delay(int milliseconds).
            // If you intend this to be seconds, call it as:
            //     StartRotatorForDurationAsync(seconds * 1000)
            // or change the line below to Task.Delay(Seconds * 1000, token).
            await Task.Delay(Seconds, token);

            _ = Task.Run(async () =>
            {
                try
                {
                    await SendDOCommand(1, 0); // Turn off
                    await SendDOCommand(3, 0); // Turn off
                }
                catch (Exception ex)
                {
                    Log($"[AccessController] StartRotatorForDurationAsync auto-off error: {ex.Message}");
                }
            }, CancellationToken.None);
        }

        public async Task OffRotatorAsync()
        {
            // Cancel any pending auto-off task so it doesn't re-fire later
            _rotatorAutoOffCts?.Cancel();

            // DO1 (rotator) OFF
            bool ok1 = await SendDOCommand(1, 0);
            bool ok3 = await SendDOCommand(3, 0);

            if (!ok1 || !ok3)
                Log("[AccessController] OffRotatorAsync: one or more channels failed to turn off.");
        }

        public void Dispose()
        {
            _rotatorAutoOffCts?.Cancel();
            _rotatorAutoOffCts?.Dispose();
            _lock?.Dispose();
            _http?.Dispose();
        }
    }
}