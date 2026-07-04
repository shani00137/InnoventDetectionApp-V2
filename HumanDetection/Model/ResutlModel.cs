using System;
using System.Collections.Generic;
using System.Text;

namespace Model
{
    public class ResutlModel
    {
        public int? TotalBoxes { get; set; }
        public double? PalletHeight { get; set; }
        public string? TotalWeight { get; set; }
        public string? ExpiryDate { get; set; }
        public string? SupplierName { get; set; }
        public int? BarcodeCodeCount { get; set; }
        public bool? DublicateBarcode { get; set; }
        public int? DublicateBarcodeCount { get; set; }
        public string? HumanDetect { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? OCRResult { get; set; }
        public double? Score { get; set; }
        public string? Name {get;set;}
        public string? AllDatesList { get; set; }
        public string? BarcodeList { get; set; }
        public List<OcrGridItem>? GridItems { get; set; }
        public int? LableCount { get; set; }
        public int? DateCount { get; set; }


    }
}
