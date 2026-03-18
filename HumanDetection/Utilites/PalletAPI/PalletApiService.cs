using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Utilites.PalletAPI;

namespace HumanDetection.Utilites.PalletAPI
{
    public class PalletApiService
    {
        private readonly string _token;
        private readonly string _apiKey;
        private readonly string _url;

        public PalletApiService(string token, string apiKey, string url)
        {
            _token = token;
            _apiKey = apiKey;
            _url = url;
        }

        public async Task<bool> PostPalletDataAsync(PalletRequest payload)
        {
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(120)
                };

                // Headers
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

                client.DefaultRequestHeaders.Add("x-api-key", _apiKey);

                var json = JsonSerializer.Serialize(payload);

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(_url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"API Error: {response.StatusCode} - {error}");
                }

                return true; // ✅ SUCCESS
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ API upload failed: {ex.Message}");
                return false; // ❌ FAILED
            }
        }
    }
}
