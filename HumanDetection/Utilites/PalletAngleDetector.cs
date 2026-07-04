using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace Utilites.Alignment
{
    /// <summary>
    /// Result of a single pallet-skew detection pass.
    /// </summary>
    public class PalletSkewResult
    {
        /// <summary>True if a corner edge was found at all.</summary>
        public bool EdgeFound { get; set; }

        /// <summary>Angle of the detected corner edge from true vertical, in degrees.
        /// Sign convention: positive = edge leans right at the bottom (pallet's
        /// near corner is rotated such that the rotator should turn LEFT to
        /// straighten it); negative = opposite. This matches DetermineDirection().</summary>
        public double AngleFromVerticalDeg { get; set; }

        /// <summary>"LEFT", "RIGHT", or "ALIGNED" — what to tell the rotator.</summary>
        public string CorrectionDirection { get; set; } = "ALIGNED";

        /// <summary>The fitted line's endpoints in image pixel coordinates,
        /// at the working (resized) resolution — useful for drawing/debugging.</summary>
        public Point TopPoint { get; set; }
        public Point BottomPoint { get; set; }

        /// <summary>How many raw Hough segments contributed to the fitted line.
        /// Low counts (1-2) mean a thinner detection — treat with more caution.</summary>
        public int SupportingSegments { get; set; }
    }

    /// <summary>
    /// Detects the skew angle of a pallet's near-vertical corner edge in a
    /// camera frame, using the pipeline validated against real warehouse
    /// images: CLAHE contrast boost -> Canny -> HoughLinesP -> restrict to the
    /// expected corner region -> least-squares line fit.
    ///
    /// IMPORTANT CALIBRATION NOTE (carried over from our discussion):
    /// The angle this reports is a 2D image-plane measurement. It can include
    /// a fixed offset caused by camera mounting position / lens distortion,
    /// not just real pallet rotation. Before trusting this in production:
    ///   1. Place a pallet you KNOW is straight/centered on the rotator.
    ///   2. Run DetectSkew() on it and read AngleFromVerticalDeg.
    ///   3. That value is your camera's baseline offset — set BaselineOffsetDeg
    ///      to it so all future readings are corrected against it.
    /// </summary>
    public class PalletAngleDetector
    {
        /// <summary>
        /// Camera/lens baseline offset in degrees, measured against a known-straight
        /// pallet (see class remarks above). Defaults to 0 until calibrated.
        /// </summary>
        public double BaselineOffsetDeg { get; set; } = 0.0;

        /// <summary>Degrees of tolerance around 0 (after baseline correction)
        /// considered "aligned" — no correction needed.</summary>
        public double ToleranceDeg { get; set; } = 3.0;

        // ---- Region of interest where the pallet's near corner is expected ----
        // ASSUMPTION: these are normalized (0-1) fractions of the WORKING image
        // width/height, calibrated against the sample image you shared, where
        // the real corner sat roughly between x=650..900 out of a 1400px-wide
        // working frame (~46%..64%), spanning most of the frame height.
        // Verify and adjust these for your actual camera framing/mounting —
        // if your rotator sits at a different position in frame than the
        // sample photo, narrow or shift this box accordingly.
        public double RoiXMinFrac { get; set; } = 0.40;
        public double RoiXMaxFrac { get; set; } = 0.70;
        public double RoiYMinFrac { get; set; } = 0.02;
        public double RoiYMaxFrac { get; set; } = 0.85;

        // ---- Processing parameters (validated against the sample image) ----
        public int WorkingWidth { get; set; } = 1400; // resize target for speed/consistency
        public double ClaheClipLimit { get; set; } = 3.0;
        public int ClaheTileGridSize { get; set; } = 8;
        public int CannyThreshold1 { get; set; } = 15;
        public int CannyThreshold2 { get; set; } = 50;
        public int HoughThreshold { get; set; } = 60;
        public double MinLineLengthFracOfHeight { get; set; } = 0.15;
        public int MaxLineGap { get; set; } = 15;
        public double MaxAngleFromVerticalToConsiderDeg { get; set; } = 12.0;

        /// <summary>Optional logging hook.</summary>
        public Action<string> Log { get; set; } = _ => { };

        /// <summary>
        /// Simple yes/no entry point: pass a frame, get back whether the pallet
        /// is aligned. This wraps DetectSkew() — use that instead if you need
        /// the full angle/direction/debug-drawing details.
        /// </summary>
        /// <param name="frame">Camera frame (color or grayscale Mat).</param>
        /// <param name="angleDeg">Detected angle from vertical, in degrees
        /// (after baseline correction). 0 if no edge was found.</param>
        /// <param name="direction">"ALIGNED", "LEFT", "RIGHT", or "UNKNOWN"
        /// (UNKNOWN means no usable edge was detected — e.g. pallet not in
        /// frame yet, or lighting too poor to find the corner).</param>
        /// <returns>True only if a corner edge was found AND it's within
        /// ToleranceDeg of vertical. Returns false if misaligned OR if no
        /// edge could be detected at all — check `direction == "UNKNOWN"`
        /// to tell those two cases apart.</returns>
        public bool IsPalletAligned(Mat frame, out double angleDeg, out string direction)
        {
            PalletSkewResult result = DetectSkew(frame);

            if (!result.EdgeFound)
            {
                angleDeg = 0;
                direction = "UNKNOWN";
                return false;
            }

            angleDeg = result.AngleFromVerticalDeg;
            direction = result.CorrectionDirection;

            return result.CorrectionDirection == "ALIGNED";
        }

        /// <summary>
        /// Same as IsPalletAligned(Mat, out double, out string) but for callers
        /// who just want the bool and don't care about angle/direction.
        /// </summary>
        public bool IsPalletAligned(Mat frame)
        {
            return IsPalletAligned(frame, out _, out _);
        }

        /// <summary>
        /// Runs the full detection pipeline on a grayscale or color frame and
        /// returns the detected corner skew, or EdgeFound=false if nothing
        /// usable was found (e.g. pallet not yet in frame, lighting too poor).
        /// </summary>
        public PalletSkewResult DetectSkew(Mat frame)
        {
            using var gray = ToGrayscale(frame);
            using var resized = ResizeToWorkingWidth(gray);

            int h = resized.Rows;
            int w = resized.Cols;

            using var enhanced = new Mat();
            using var clahe = Cv2.CreateCLAHE(ClaheClipLimit, new Size(ClaheTileGridSize, ClaheTileGridSize));
            clahe.Apply(resized, enhanced);

            using var blurred = new Mat();
            Cv2.GaussianBlur(enhanced, blurred, new Size(5, 5), 0);

            using var edges = new Mat();
            Cv2.Canny(blurred, edges, CannyThreshold1, CannyThreshold2);

            int minLineLength = (int)(h * MinLineLengthFracOfHeight);
            LineSegmentPoint[] lines = Cv2.HoughLinesP(
                edges,
                rho: 1,
                theta: Math.PI / 180,
                threshold: HoughThreshold,
                minLineLength: minLineLength,
                maxLineGap: MaxLineGap
            );

            if (lines == null || lines.Length == 0)
            {
                Log("[PalletAngleDetector] No lines found at all — check lighting/contrast.");
                return new PalletSkewResult { EdgeFound = false };
            }

            // Restrict to the expected corner region and near-vertical lines only.
            double roiXMin = w * RoiXMinFrac;
            double roiXMax = w * RoiXMaxFrac;
            double roiYMin = h * RoiYMinFrac;
            double roiYMax = h * RoiYMaxFrac;

            var candidates = new List<(double x1, double y1, double x2, double y2, double angle, double length)>();

            foreach (var line in lines)
            {
                double x1 = line.P1.X, y1 = line.P1.Y;
                double x2 = line.P2.X, y2 = line.P2.Y;
                double dx = x2 - x1;
                double dy = y2 - y1;
                double length = Math.Sqrt(dx * dx + dy * dy);
                double angle = Math.Atan2(dx, dy) * 180.0 / Math.PI; // angle from vertical

                double midX = (x1 + x2) / 2.0;
                double midY = (y1 + y2) / 2.0;

                bool inRoi = midX >= roiXMin && midX <= roiXMax && midY >= roiYMin && midY <= roiYMax;
                bool nearVertical = Math.Abs(angle) <= MaxAngleFromVerticalToConsiderDeg;

                if (inRoi && nearVertical)
                {
                    candidates.Add((x1, y1, x2, y2, angle, length));
                }
            }

            if (candidates.Count == 0)
            {
                Log("[PalletAngleDetector] Lines found, but none matched the corner ROI/verticality filter.");
                return new PalletSkewResult { EdgeFound = false };
            }

            // Longest segments first — these are the most reliable structural edges
            // (as opposed to short noisy segments from wrap-film reflections).
            candidates = candidates.OrderByDescending(c => c.length).ToList();

            // Least-squares fit: x = m*y + b (fitting x as a function of y is more
            // numerically stable than y=mx+b for near-vertical lines).
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var c in candidates)
            {
                xs.Add(c.x1); ys.Add(c.y1);
                xs.Add(c.x2); ys.Add(c.y2);
            }

            (double m, double b) = FitLineXAsFunctionOfY(xs, ys);

            double yTop = ys.Min();
            double yBot = ys.Max();
            double xTop = m * yTop + b;
            double xBot = m * yBot + b;

            double rawAngleFromVertical = Math.Atan(m) * 180.0 / Math.PI;
            double correctedAngle = rawAngleFromVertical - BaselineOffsetDeg;

            string direction = DetermineDirection(correctedAngle);

            Log($"[PalletAngleDetector] Raw angle={rawAngleFromVertical:F1} deg, " +
                $"baseline-corrected={correctedAngle:F1} deg, direction={direction}, " +
                $"supportingSegments={candidates.Count}");

            return new PalletSkewResult
            {
                EdgeFound = true,
                AngleFromVerticalDeg = correctedAngle,
                CorrectionDirection = direction,
                TopPoint = new Point((int)xTop, (int)yTop),
                BottomPoint = new Point((int)xBot, (int)yBot),
                SupportingSegments = candidates.Count
            };
        }

        /// <summary>
        /// Draws the detected edge (red), the straight target reference (green),
        /// and a correction-direction arrow (yellow) on a copy of the frame —
        /// matching exactly what was validated against the sample image.
        /// Returns a new color Mat; caller is responsible for disposing it.
        /// </summary>
        public Mat DrawDebugOverlay(Mat frame, PalletSkewResult result)
        {
            using var gray = ToGrayscale(frame);
            var resized = ResizeToWorkingWidth(gray);
            var color = new Mat();
            Cv2.CvtColor(resized, color, ColorConversionCodes.GRAY2BGR);
            resized.Dispose();

            if (!result.EdgeFound)
            {
                Cv2.PutText(color, "No edge detected", new Point(20, 40),
                    HersheyFonts.HersheySimplex, 0.8, new Scalar(0, 0, 255), 2);
                return color;
            }

            // Detected edge in red
            Cv2.Line(color, result.TopPoint, result.BottomPoint, new Scalar(0, 0, 255), 4);

            // Target vertical reference in green, anchored at the line's midpoint
            int midY = (result.TopPoint.Y + result.BottomPoint.Y) / 2;
            double frac = (midY - result.TopPoint.Y) / (double)(result.BottomPoint.Y - result.TopPoint.Y);
            int midX = (int)(result.TopPoint.X + frac * (result.BottomPoint.X - result.TopPoint.X));

            Cv2.Line(color, new Point(midX, result.TopPoint.Y), new Point(midX, result.BottomPoint.Y),
                new Scalar(0, 255, 0), 4);

            Cv2.PutText(color, $"Detected edge ({result.AngleFromVerticalDeg:F1} deg off vertical)",
                new Point(Math.Max(0, result.TopPoint.X - 250), Math.Max(20, result.TopPoint.Y - 10)),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 0, 255), 2);

            Cv2.PutText(color, "Target (straight)", new Point(midX + 15, midY),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);

            // Correction arrow
            int arrowY = result.BottomPoint.Y - 30;
            var from = new Point(result.BottomPoint.X, arrowY);
            var to = new Point(midX, arrowY);
            Cv2.ArrowedLine(color, from, to, new Scalar(0, 255, 255), 3, tipLength: 0.3);

            Cv2.PutText(color, $"Rotate pallet {result.CorrectionDirection}",
                new Point(Math.Min(from.X, to.X) - 20, arrowY - 15),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 255), 2);

            return color;
        }

        // -------------------------
        // Helpers
        // -------------------------

        private Mat ToGrayscale(Mat frame)
        {
            if (frame.Channels() == 1)
                return frame.Clone();

            var gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            return gray;
        }

        private Mat ResizeToWorkingWidth(Mat grayFrame)
        {
            double scale = WorkingWidth / (double)grayFrame.Width;
            var resized = new Mat();
            Cv2.Resize(grayFrame, resized, new Size(), scale, scale, InterpolationFlags.Area);
            return resized;
        }

        /// <summary>
        /// Determines rotator correction direction from the (baseline-corrected)
        /// angle from vertical. Matches the convention validated on the sample
        /// image: a positive angle (edge leans toward +x going down) means the
        /// pallet's near corner needs to rotate LEFT to straighten.
        /// ⚠️ ASSUMPTION: verify this LEFT/RIGHT mapping against your actual
        /// rotator's physical rotation direction (CW/CCW) and VFD wiring —
        /// the sign convention here matches the 2D image math, not necessarily
        /// "left" and "right" as your VFD/motor calls them. Confirm with one
        /// real test before relying on it to auto-correct hardware.
        /// </summary>
        private string DetermineDirection(double correctedAngleDeg)
        {
            if (Math.Abs(correctedAngleDeg) <= ToleranceDeg)
                return "ALIGNED";

            return correctedAngleDeg > 0 ? "LEFT" : "RIGHT";
        }

        private static (double m, double b) FitLineXAsFunctionOfY(List<double> xs, List<double> ys)
        {
            // Simple least-squares fit: x = m*y + b
            int n = xs.Count;
            double sumY = ys.Sum();
            double sumX = xs.Sum();
            double sumYY = ys.Select(y => y * y).Sum();
            double sumXY = 0;
            for (int i = 0; i < n; i++)
                sumXY += xs[i] * ys[i];

            double denom = n * sumYY - sumY * sumY;
            if (Math.Abs(denom) < 1e-9)
            {
                // Degenerate case (all points have identical Y) — fall back to
                // a vertical line through the mean X.
                return (0, xs.Average());
            }

            double m = (n * sumXY - sumY * sumX) / denom;
            double b = (sumX - m * sumY) / n;
            return (m, b);
        }
    }
}
