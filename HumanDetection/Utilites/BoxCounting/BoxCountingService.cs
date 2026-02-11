using System;
using System.Collections.Generic;
using System.Text;
using Yolov5Net.Scorer;

namespace Utilites.BoxCounting
{
    public class BoxCountingService
    {
        public List<int> GetRowPattern(List<YoloPrediction> boxes)
        {
            var rows = new List<int>();

            if (!boxes.Any())
                return rows;

            var sorted = boxes.OrderBy(b => b.CenterY).ToList();
            float avgHeight = sorted.Average(b => b.Height);

            float threshold = avgHeight * 0.6f;

            var currentRow = new List<YoloPrediction> { sorted[0] };

            for (int i = 1; i < sorted.Count; i++)
            {
                if (Math.Abs(sorted[i].CenterY - currentRow[0].CenterY) < threshold)
                {
                    currentRow.Add(sorted[i]);
                }
                else
                {
                    rows.Add(currentRow.Count);
                    currentRow = new List<YoloPrediction> { sorted[i] };
                }
            }

            rows.Add(currentRow.Count);

            return rows;
        }

    }
}
