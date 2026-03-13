using System;
using System.Collections.Generic;
using System.Text;
using Yolov5Net.Scorer;

namespace Model
{
    public enum PalletSide
    {
        Front,
        Right,
        Back,
        Left,
        Top
    }

    public class ImagePredictionResult
    {
        public PalletSide Side { get; set; }

        public List<YoloPrediction> BoxPredictions { get; set; } = new();

        public double PalletHeightMeters { get; set; }

        public double AverageScore { get; set; }

        public double PalletAngleDeg { get; set; }
        public List<byte[]> BoxesImages { get; set; }
    }
}
