using Dynamsoft.DBR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Utilites
{
    public class OCRResults
    {
        public List<BarcodeResultItem> Barcodes { get; set; }
        public List<string> TextBlocks { get; set; }
        public List<string> Dates { get; set; }
        public List<string> SKUs { get; set; }
    }
}
