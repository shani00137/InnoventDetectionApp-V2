using System;
using System.Collections.Generic;
using System.Text;

namespace Utilites
{
    public static class MathExtensions
    {
        public static double Median(this List<double> values)
        {
            if (values == null || values.Count == 0)
                return 0;

            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;

            return (sorted.Count % 2 != 0)
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }
    }

}
