namespace HumanDetection.Services
{
    /// <summary>
    /// Lightweight global status flags populated by the splash screen at startup.
    /// Screens such as the Dashboard read this to show live system readiness.
    /// </summary>
    public sealed class SystemHealth
    {
        public static SystemHealth Current { get; } = new SystemHealth();

        public bool ModelsLoaded { get; set; }
        public bool WeightOk { get; set; }
        public bool MoxaOk { get; set; }
        public bool OcrOk { get; set; }

        public string ModelError { get; set; } = string.Empty;
        public string OcrError { get; set; } = string.Empty;

        public bool IsFullyReady => ModelsLoaded && WeightOk && MoxaOk && OcrOk;

        public int ReadyCount
        {
            get
            {
                int count = 0;
                if (ModelsLoaded) count++;
                if (WeightOk) count++;
                if (MoxaOk) count++;
                if (OcrOk) count++;
                return count;
            }
        }

        public int TotalCount => 4;
    }
}