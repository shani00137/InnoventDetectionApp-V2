using System;
using System.Collections.Generic;

namespace Model
{
    public class OcrResult
    {
        public int barcode_count { get; set; }
        public List<BarcodeItem> barcodes { get; set; }

        public int date_count { get; set; }
        public List<DateItem> dates { get; set; }

        public int text_count { get; set; }
        public List<RawTextItem> raw_text { get; set; }
    }
    public class OcrApiResponse
    {
        public Dictionary<string, OcrImageResult> results { get; set; }
        public int total_images { get; set; }
        public double total_time_min { get; set; }
        public double total_time_sec { get; set; }
    }
    public class OcrImageResult
    {
        public int barcode_count { get; set; }
        public List<string> barcodes { get; set; }

        public int date_count { get; set; }
        public List<string> dates { get; set; }

        public int text_count { get; set; }
        public List<string> raw_text { get; set; }

        public double processing_time_sec { get; set; }

        public string error { get; set; }   // optional (unsupported file case)
    }
    public class BarcodeItem
    {
        public string value { get; set; }
        public double confidence { get; set; }
        public string source { get; set; } // "ocr" or "pyzbar"
    }
    public class DateItem
    {
        public string value { get; set; }      // "05.01.2026"
        public double confidence { get; set; }
    }
    public class RawTextItem
    {
        public string text { get; set; }
        public double confidence { get; set; }

        // 4 points, each point = [x, y]
        public List<List<double>> box { get; set; }
    }
}
