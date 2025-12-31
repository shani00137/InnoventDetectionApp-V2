using System;
using System.Collections.Generic;
using System.Text;

namespace Model
{
    public class OcrResult
    {
        public int images_processed { get; set; }
        public List<string> ocr_texts { get; set; }
        public List<List<string>> barcodes { get; set; }
        public double processing_time_sec { get; set; }
    }

}
