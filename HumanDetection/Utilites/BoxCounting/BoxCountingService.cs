using System;
using System.Collections.Generic;
using System.Linq;
using Yolov5Net.Scorer;

namespace Utilites.BoxCounting
{
    public class BoxCountingService
    {
        public static int CountBoxes(List<YoloPrediction> frontPredictions, List<YoloPrediction> rightPredictions)
        {
            if (frontPredictions == null || rightPredictions == null)
                return 0;

            int layers = CountLayers(frontPredictions); // vertical stacks
            int width = CountWidth(frontPredictions);   // horizontal boxes in front
            int depth = CountDepth(rightPredictions);   // horizontal boxes from right

            return layers * width * depth;
        }

        // Count vertical layers from front view
        private static int CountLayers(List<YoloPrediction> predictions)
        {
            if (predictions == null || predictions.Count == 0)
                return 0;

            return predictions
                .Select(p => Math.Round(p.Rectangle.Y / 50.0))
                .Distinct()
                .Count();
        }

        // Count boxes horizontally in front image
        private static int CountWidth(List<YoloPrediction> predictions)
        {
            if (predictions == null || predictions.Count == 0)
                return 0;

            return predictions
                .Select(p => Math.Round(p.Rectangle.X / 50.0))
                .Distinct()
                .Count();
        }

        // Count boxes depth from right image
        private static int CountDepth(List<YoloPrediction> predictions)
        {
            if (predictions == null || predictions.Count == 0)
                return 0;

            return predictions
                .Select(p => Math.Round(p.Rectangle.X / 50.0))
                .Distinct()
                .Count();
        }
    }
}