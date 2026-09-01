using System;

namespace SQLite
{
    public enum DetectionStatus
    {
        Success,
        Failed,
        LessScore
    }

    public class DetectionResultModel
    {
        public int Id { get; set; }
        public DateTime ScanDate { get; set; }
        public string Status { get; set; }
        public double Score { get; set; }
        public int TotalBoxes { get; set; }
        public double PalletHeight { get; set; }
        public string Weight { get; set; }
        public string HumanDetected { get; set; }
        public int BarcodeCount { get; set; }
        public string BarcodeList { get; set; }
        public int DateCount { get; set; }
        public string DateList { get; set; }
        public string OCRResult { get; set; }
        public string EntryTime { get; set; }
        public string ExitTime { get; set; }
        public string ImagesPath { get; set; }
        public string AnnotatedPath { get; set; }
        public string ResultFilePath { get; set; }
        public int Attempts { get; set; }
        public string Task1StartTime { get; set; }
        public string Task1EndTime { get; set; }
        public string Task2StartTime { get; set; }
        public string Task2EndTime { get; set; }

        public string Task1TimeRange => string.IsNullOrEmpty(Task1StartTime) ? "" : $"{Task1StartTime} - {Task1EndTime}";
        public string Task2TimeRange => string.IsNullOrEmpty(Task2StartTime) ? "" : $"{Task2StartTime} - {Task2EndTime}";
    }
}
