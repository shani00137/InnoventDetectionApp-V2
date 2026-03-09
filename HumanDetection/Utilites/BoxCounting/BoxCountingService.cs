using System;
using System.Collections.Generic;
using System.Linq;
using Yolov5Net.Scorer;

namespace Utilites.BoxCounting
{
    public class BoxCountingService
    {
        public static int CountBox(
        List<YoloPrediction> front,
        List<YoloPrediction> right,
        List<YoloPrediction> back,
        List<YoloPrediction> left)
        {
            int layers = CountLayers(front);
            int depth = CountDepth(right);

            if (layers == 0)
                layers = CountLayers(back);

            if (depth == 0)
                depth = CountDepth(left);

            int total = layers * depth;

            return total;
        }

        private static int CountLayers(List<YoloPrediction> predictions)
        {
            if (predictions == null || predictions.Count == 0)
                return 0;

            return predictions
                .Select(p => Math.Round(p.Rectangle.Y / 50.0))
                .Distinct()
                .Count();
        }

        private static int CountDepth(List<YoloPrediction> predictions)
        {
            if (predictions == null || predictions.Count == 0)
                return 0;

            return predictions
                .Select(p => Math.Round(p.Rectangle.X / 50.0))
                .Distinct()
                .Count();
        }

        //public static int CountBox(
        //    List<YoloPrediction> frontBoxes,
        //    List<YoloPrediction> rightBoxes,
        //    List<YoloPrediction> backBoxes,
        //    List<YoloPrediction> leftBoxes)
        //{
        //    // Remove overlap columns
        //    frontBoxes = RemoveOverlapColumn(frontBoxes, true);
        //    rightBoxes = RemoveOverlapColumn(rightBoxes, false);
        //    backBoxes = RemoveOverlapColumn(backBoxes, false);
        //    leftBoxes = RemoveOverlapColumn(leftBoxes, false);

        //    // Safe null handling
        //    int frontCount = frontBoxes?.Count ?? 0;
        //    int rightCount = rightBoxes?.Count ?? 0;
        //    int backCount = backBoxes?.Count ?? 0;
        //    int leftCount = leftBoxes?.Count ?? 0;

        //    // Final total
        //    int totalBoxes = frontCount + rightCount + backCount + leftCount;

        //    return totalBoxes;
        //}

        public static List<YoloPrediction> RemoveOverlapColumn(
            List<YoloPrediction> boxes,
            bool isFrontImage)
        {
            // FRONT image keeps everything
            if (isFrontImage)
                return boxes ?? new List<YoloPrediction>();

            if (boxes == null || boxes.Count == 0)
                return boxes ?? new List<YoloPrediction>();

            // Sort boxes left → right
            var sorted = boxes
                .OrderBy(b => b.Rectangle.X + b.Rectangle.Width / 2)
                .ToList();

            // Get median width
            var widths = sorted
                .Select(b => b.Rectangle.Width)
                .OrderBy(w => w)
                .ToList();

            float medianWidth = widths[widths.Count / 2];

            // First column threshold
            float firstColumnLimit =
                (sorted[0].Rectangle.X + sorted[0].Rectangle.Width / 2)
                + medianWidth * 0.6f;

            // Remove overlap column
            var filtered = sorted
                .Where(b =>
                    (b.Rectangle.X + b.Rectangle.Width / 2) > firstColumnLimit)
                .ToList();

            return filtered;
        }
    }
}
