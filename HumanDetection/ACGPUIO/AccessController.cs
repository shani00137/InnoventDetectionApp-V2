using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ACGPUIO
{
    public class AccessController : IDisposable
    {
        private readonly HttpClient _http;
        private string _token = "";
        private readonly string _moxaIP;

        public AccessController(string moxaIP = "192.168.1.135")
        {
            _moxaIP = moxaIP;
            _http = new HttpClient();
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
                    // Console.WriteLine("New token: " + _token);
                    return true;
                }

                // Token not found
                return false;
            }
            catch
            {
                return false;
            }
        }

        // -------------------------
        //        CORE DO
        // -------------------------
        public async Task<bool> SendDOCommand(int channel, int status)
        {
            // 1) If we don't have token yet, get it first
            if (string.IsNullOrEmpty(_token))
            {
                bool gotToken = await RefreshToken();
                if (!gotToken)
                    return false;
            }

            try
            {
                string url =
                    $"http://{_moxaIP}/set_521.htm" +
                    $"?CHANNEL_NO={channel}" +
                    $"&DO_MODE_C=0" +
                    $"&DO_STATUS_ENABLE={status}" +
                    $"&PWM_LO_C=1&PWM_HI_C=1&PWM_CNT_C=0&PWM_START_C=&DO_VALUE_P=0&PWM_START_P=" +
                    $"&DO_VALUE_S=0&PWM_START_S=&ALIAS_CHANNEL=DO&LOGIC_0=OFF&LOGIC_1=ON" +
                    $"&token={_token}";

                string resp = await _http.GetStringAsync(url);

                // 2) If response looks like token/login page again, token might be expired.
                //    Try refreshing token once and retry.
                if (IsTokenExpiredResponse(resp))
                {
                    bool gotToken = await RefreshToken();
                    if (!gotToken)
                        return false;

                    // retry once with new token
                    url =
                        $"http://{_moxaIP}/set_521.htm" +
                        $"?CHANNEL_NO={channel}" +
                        $"&DO_MODE_C=0" +
                        $"&DO_STATUS_ENABLE={status}" +
                        $"&PWM_LO_C=1&PWM_HI_C=1&PWM_CNT_C=0&PWM_START_C=&DO_VALUE_P=0&PWM_START_P=" +
                        $"&DO_VALUE_S=0&PWM_START_S=&ALIAS_CHANNEL=DO&LOGIC_0=OFF&LOGIC_1=ON" +
                        $"&token={_token}";

                    resp = await _http.GetStringAsync(url);
                }

                // You can also parse resp here to verify success if MOXA returns OK text.
                return true;
            }
            catch
            {
                return false;
            }
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
            await SendDOCommand(0, 1);
        }

        public async Task OffBuzzerAsync()
        {
            // DO0 (buzzer) OFF
            await SendDOCommand(0, 0);
        }

        // -------------------------
        //     BLOWER (DO1)
        // -------------------------
        public async Task StartBlowerAsync()
        {
            // DO1 (blower) ON
            await SendDOCommand(2, 1);
        }

        public async Task OffBlowerAsync()
        {
            // DO1 (blower) OFF
            await SendDOCommand(2, 0);
        }

        public async Task StartRotatorAsync()
        {
            // DO1 (rotator) ON
            await SendDOCommand(1, 1);
            await Task.Delay(7000);
            await SendDOCommand(3, 1);

            _ = Task.Run(async () =>
            {
                await Task.Delay(4000); // Wait 4 seconds
                await SendDOCommand(1, 0); // Turn off
                await SendDOCommand(3, 0); // Turn off
            });

        }
        public async Task TurnOnRotatorAsync()
        {
            // DO1 (rotator) ON
            await SendDOCommand(1, 1);          

        }
        public async Task StartRotatorForDurationAsync(int Seconds)
        {
            // DO1 (rotator) ON
           
            await SendDOCommand(3, 1);

            await Task.Delay(Seconds);
            _ = Task.Run(async () =>
            {
                // Wait 4 seconds
                await SendDOCommand(1, 0); // Turn off
                await SendDOCommand(3, 0); // Turn off

                
            });

        }

        public async Task OffRotatorAsync()
        {
            // DO1 (rotator) OFF
            await SendDOCommand(1, 0);
            await SendDOCommand(3, 0);
        }

        public void Dispose()
        {
            _http?.Dispose();
        }
    }
}
