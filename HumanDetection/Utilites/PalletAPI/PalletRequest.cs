using System;
using System.Collections.Generic;
using System.Text;

namespace Utilites.PalletAPI
{
    public class PalletRequest
    {
        public string name { get; set; }
        public string palletWeight { get; set; }
        public string palletHeight { get; set; }
        public string NO_OfBoxs { get; set; } // use this OR dictionary if API strictly needs dot
        public string startTime { get; set; }
        public string endTime { get; set; }
        public string trustScoreLevel { get; set; }
        public string productionDate { get; set; }
        public string exipreDate { get; set; }
        public string barCode { get; set; }
        public string palletCondition { get; set; }
        public string humenDetection { get; set; }
        public string image { get; set; }
    }
}
