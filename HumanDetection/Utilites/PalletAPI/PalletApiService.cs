using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Utilites.PalletAPI;

namespace HumanDetection.Utilites.PalletAPI
{
    public class PalletApiService
    {
        private readonly string _token;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _uploadBaseUrl;

        public PalletApiService(string token, string apiKey, string baseUrl)
        {
            _token = token;
            _apiKey = apiKey;
            _baseUrl = baseUrl.TrimEnd('/');

            int coreIndex = _baseUrl.IndexOf("/core", StringComparison.OrdinalIgnoreCase);
            _uploadBaseUrl = coreIndex >= 0
                ? _baseUrl.Substring(0, coreIndex + "/core".Length)
                : _baseUrl;
        }

        public async Task<string?> UploadImageAsync(byte[] imageBytes, string fileName)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _token);
                client.DefaultRequestHeaders.Add("x-api-key", _apiKey);

                using var content = new MultipartFormDataContent();
                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                content.Add(imageContent, "image", fileName);

                var response = await client.PostAsync($"{_uploadBaseUrl}/upload/image", content);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"❌ Image upload failed: {response.StatusCode} - {error}");
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                if (doc.RootElement.TryGetProperty("url", out var urlProp))
                    return urlProp.GetString();
                if (doc.RootElement.TryGetProperty("imageUrl", out var imageUrlProp))
                    return imageUrlProp.GetString();
                if (doc.RootElement.TryGetProperty("path", out var pathProp))
                    return pathProp.GetString();

                return responseJson.Trim('"');
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Image upload exception: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> PostPalletDataAsync(PalletRequest payload)
        {
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(120)
                };

                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

                client.DefaultRequestHeaders.Add("x-api-key", _apiKey);

                var body = new Dictionary<string, string>
                {
                    { "name", DateTime.Now.ToString() },
                    { "palletWeight", payload.palletWeight },
                    { "palletHeight", payload.palletHeight },
                    { "NO.OfBoxs", payload.NO_OfBoxs },
                    { "startTime", payload.startTime },
                    { "endTime", payload.endTime },
                    { "trustScoreLevel", payload.trustScoreLevel },
                    { "productionDate", payload.productionDate },
                    { "exipreDate", payload.exipreDate },
                    { "barCode", payload.barCode },
                    { "palletCondition", payload.palletCondition },
                    { "humenDetection", payload.humenDetection },
                    { "image", "https://adp-backend-demo.ashybay-437ca219.uaenorth.azurecontainerapps.io/core"+"/"+payload.image }
                };

                var json = JsonSerializer.Serialize(body);

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(_baseUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"API Error: {response.StatusCode} - {error}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ API upload failed: {ex.Message}");
                return false;
            }
        }
    }
}
