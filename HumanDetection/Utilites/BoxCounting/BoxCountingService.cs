using System;
using System.Collections.Generic;
using System.Linq;
using Yolov5Net.Scorer;

namespace Utilites.BoxCounting
{
    public class BoxCountingService
    {
        public static int CountBoxes(
            List<YoloPrediction> frontPredictions,
            List<YoloPrediction> topPredictions)
        {
            if (frontPredictions == null || frontPredictions.Count == 0)
                return 0;

            // Total visible boxes from front
            int frontBoxCount = frontPredictions.Count;

            // Rows from top view
            int rows = CountTopRows(topPredictions);

            // Safety rule
            if (rows <= 0)
                rows = 1;

            return frontBoxCount * rows;
        }

        // Detect number of rows in top image
        private static int CountTopRows(List<YoloPrediction> predictions)
        {
            if (predictions == null || predictions.Count == 0)
                return 1;

            // sort by Y position
            var sorted = predictions
                .OrderBy(p => p.Rectangle.Y)
                .ToList();

            // estimate average height
            float avgHeight = sorted.Average(p => p.Rectangle.Height);

            // threshold for new row detection
            float rowThreshold = avgHeight * 0.6f;

            int rows = 1;

            for (int i = 1; i < sorted.Count; i++)
            {
                if (Math.Abs(sorted[i].Rectangle.Y - sorted[i - 1].Rectangle.Y) > rowThreshold)
                {
                    rows++;
                }
            }

            return rows;
        }
    }
}