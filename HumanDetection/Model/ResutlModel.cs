using System;
using System.Collections.Generic;
using System.Text;

namespace Model
{
    public class ResutlModel
    {
        public int? TotalBoxes { get; set; }
        public double? PalletHeight { get; set; }
        public double? TotalWeight { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? OCRResult { get; set; }
    }
}
