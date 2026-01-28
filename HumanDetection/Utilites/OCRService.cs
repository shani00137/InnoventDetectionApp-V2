using Dynamsoft.Core;
using Dynamsoft.CVR;
using Dynamsoft.DBR;
using Dynamsoft.License;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilites
{
    public static class OCRService
    {
        private static bool licenseInitialized = false;

        public static async Task<OCRResults> ProcessImageAsync(byte[] imageBytes)
        {
            if (!licenseInitialized)
                InitializeLicense();

            return await Task.Run(() =>
            {
                using Mat image = BytesToMat(imageBytes);

                // OCR
                List<string> textBlocks = RunOcr(image);

                // Dates + SKUs
                List<string> extractedDates = DateExtractor.ExtractDates(textBlocks);
                var skus = ExtractSKUsFromText(textBlocks);

                return new OCRResults
                {
                    TextBlocks = textBlocks,
                    Dates = extractedDates,
                    SKUs = skus,
                    Barcodes = new List<BarcodeResultItem>() // optional
                };
            });
        }
        public static Mat BytesToMat(byte[] imageBytes)
        {
            return Cv2.ImDecode(imageBytes, ImreadModes.Color);
        }
        private static void InitializeLicense()
        {
            string licenseKey = "t0089bQEAAJu4pVgB4c9cHOsEy8JEKTcBr3rtUMQQfhte3kDCfeo1B4xvafY3uEplnnP/jFwL5K/nk21deYsn6AQyjnJY+Zc1LV9ZzsnalCzxK7tFsQjyBL9eTX4=";
            string errorMsg;
            int errorCode = LicenseManager.InitLicense(licenseKey, out errorMsg);

            if (errorCode != (int)EnumErrorCode.EC_OK && errorCode != (int)EnumErrorCode.EC_LICENSE_WARNING)
            {
                throw new Exception($"License initialization failed: {errorCode}, {errorMsg}");
            }

            licenseInitialized = true;
        }

        private static List<string> RunOcr(Mat image)
        {
            List<string> textBlocks = new();

            using var ocr = new PaddleOcrAll(LocalFullModels.EnglishV3, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = true,
                Enable180Classification = true
            };

            PaddleOcrResult result = ocr.Run(image);

            foreach (var region in result.Regions)
            {
                if (!string.IsNullOrWhiteSpace(region.Text))
                    textBlocks.Add(region.Text.Trim());
            }

            return textBlocks;
        }

        private static List<BarcodeResultItem> ReadBarcodes(string imagePath)
        {
            List<BarcodeResultItem> allBarcodes = new();

            using (CaptureVisionRouter cvRouter = new CaptureVisionRouter())
            {
                CapturedResult[] results = cvRouter.CaptureMultiPages(imagePath, PresetTemplate.PT_READ_BARCODES);

                if (results == null) return allBarcodes;

                foreach (var result in results)
                {
                    DecodedBarcodesResult barcodesResult = result.GetDecodedBarcodesResult();
                    BarcodeResultItem[] items = barcodesResult != null ? barcodesResult.GetItems() : null;
                    if (items != null)
                    {
                        allBarcodes.AddRange(items);
                    }
                }
            }

            return allBarcodes;
        }
        static class DateExtractor
        {
            public static string NormalizeDigits(string input)
            {
                return input
                    .Replace("٠", "0").Replace("١", "1").Replace("٢", "2")
                    .Replace("٣", "3").Replace("٤", "4").Replace("٥", "5")
                    .Replace("٦", "6").Replace("٧", "7").Replace("٨", "8")
                    .Replace("٩", "9");
            }

            public static List<string> ExtractDates(IEnumerable<string> texts)
            {
                HashSet<string> dates = new();

                string pattern = @"
                        (?:
                            \b\d{2}[-/.]\d{2}[-/.]\d{2,4}\b |   # 20-01-2025
                            \b\d{4}[-/.]\d{2}[-/.]\d{2}\b |     # 2025-01-20
                            \b\d{2}[-/.]\d{4}\b |               # 01-2025
                            \b\d{4}[-/.]\d{2}\b                 # 2025-01
                        )";

                Regex regex = new Regex(pattern, RegexOptions.IgnorePatternWhitespace);

                foreach (var text in texts)
                {
                    string normalized = NormalizeDigits(text);
                    foreach (Match match in regex.Matches(normalized))
                    {
                        dates.Add(match.Value);
                    }
                }

                return new List<string>(dates);
            }
        }
        public static List<string> ExtractSKUsFromText(List<string> ocrTextBlocks)
        {
            return ExtractSKUs(ocrTextBlocks);
        }

        public static List<string> ExtractSKUs(IEnumerable<string> texts)
        {
            HashSet<string> skus = new();

            // Match SKU after keywords: Product, SKU, Item, etc.
            // It captures everything after "SKU:" or "Product:" until whitespace or line break
            string pattern = @"(?:SKU|Product|Item)\s*[:\-]?\s*([A-Z0-9\-_]+)";

            Regex regex = new Regex(pattern, RegexOptions.IgnoreCase);

            foreach (var text in texts)
            {
                foreach (Match match in regex.Matches(text))
                {
                    if (match.Groups.Count > 1)
                    {
                        string sku = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(sku))
                            skus.Add(sku);
                    }
                }
            }

            return skus.ToList();
        }

    }
}
