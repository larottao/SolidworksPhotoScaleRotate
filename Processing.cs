using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PhotoScaleRotate
{
    /// <summary>A calibration line defined by two points, in ORIGINAL image pixel coordinates.</summary>
    public readonly record struct AxisLine(PointF P1, PointF P2)
    {
        public float LengthPx
        {
            get
            {
                float dx = P2.X - P1.X;
                float dy = P2.Y - P1.Y;
                return MathF.Sqrt(dx * dx + dy * dy);
            }
        }

        public PointF Vector => new(P2.X - P1.X, P2.Y - P1.Y);
    }

    /// <summary>Immutable input for one processing run.</summary>
    public sealed record CalibrationInput(
        AxisLine XAxis,
        AxisLine YAxis,
        float XLengthMm,
        float YLengthMm,
        float TargetPxPerMm);

    /// <summary>
    /// Result of a processing run. Owns the output bitmap and the transform that maps
    /// ORIGINAL image pixel coordinates to PROCESSED image pixel coordinates
    /// (used to redraw the axis/ruler overlays on top of the result).
    /// </summary>
    public sealed class ProcessedImage : IDisposable
    {
        public Bitmap Bitmap { get; }
        public Matrix OriginalToProcessed { get; }
        public float RotationAppliedDeg { get; }
        public float SourcePxPerMmX { get; }
        public float SourcePxPerMmY { get; }
        public float TargetPxPerMm { get; }
        /// <summary>Deviation of the clicked Y axis from being perpendicular to the clicked X axis, in degrees.</summary>
        public float PerpendicularityDeviationDeg { get; }

        public float WidthMm => Bitmap.Width / TargetPxPerMm;
        public float HeightMm => Bitmap.Height / TargetPxPerMm;

        private bool _disposed;

        public ProcessedImage(
            Bitmap bitmap,
            Matrix originalToProcessed,
            float rotationAppliedDeg,
            float sourcePxPerMmX,
            float sourcePxPerMmY,
            float targetPxPerMm,
            float perpendicularityDeviationDeg)
        {
            Bitmap = bitmap;
            OriginalToProcessed = originalToProcessed;
            RotationAppliedDeg = rotationAppliedDeg;
            SourcePxPerMmX = sourcePxPerMmX;
            SourcePxPerMmY = sourcePxPerMmY;
            TargetPxPerMm = targetPxPerMm;
            PerpendicularityDeviationDeg = perpendicularityDeviationDeg;
        }

        /// <summary>Maps a point from original image coordinates into processed image coordinates.</summary>
        public PointF MapPoint(PointF original)
        {
            PointF[] pts = { original };
            OriginalToProcessed.TransformPoints(pts);
            return pts[0];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Bitmap.Dispose();
            OriginalToProcessed.Dispose();
        }
    }

    /// <summary>Structured outcome instead of exceptions for expected failures.</summary>
    public sealed record ProcessOutcome(ProcessedImage? Result, string? Error)
    {
        public static ProcessOutcome Ok(ProcessedImage r) => new(r, null);
        public static ProcessOutcome Fail(string error) => new(null, error);
    }

    public static class ImageProcessor
    {
        // Refuse to allocate absurd outputs (protects against a typo in the mm boxes).
        private const long MaxOutputPixels = 120_000_000;

        /// <summary>
        /// Rotates the source so the clicked X axis becomes horizontal, then scales X and Y
        /// independently so the output has exactly <c>TargetPxPerMm</c> pixels per millimetre.
        /// SolidWorks inserts sketch pictures at 1 px = 1 mm and ignores embedded DPI, so a
        /// TargetPxPerMm of 1.0 imports at true scale with no adjustment.
        /// The canvas is expanded (white fill), never cropped.
        /// </summary>
        public static ProcessOutcome Process(Bitmap source, CalibrationInput input)
        {
            float xPx = input.XAxis.LengthPx;
            float yPx = input.YAxis.LengthPx;

            if (xPx < 2f || yPx < 2f)
                return ProcessOutcome.Fail("Axis points are too close together.");
            if (input.XLengthMm <= 0f || input.YLengthMm <= 0f || input.TargetPxPerMm <= 0f)
                return ProcessOutcome.Fail("Lengths and output px/mm must be positive numbers.");

            float srcPxPerMmX = xPx / input.XLengthMm;
            float srcPxPerMmY = yPx / input.YLengthMm;

            // Rotation that makes the clicked X axis horizontal (image Y grows downward).
            PointF xv = input.XAxis.Vector;
            float angleDeg = (float)(Math.Atan2(xv.Y, xv.X) * 180.0 / Math.PI);
            float rotationDeg = -angleDeg;

            // How far the clicked Y axis is from perpendicular to the clicked X axis.
            PointF yv = input.YAxis.Vector;
            double dot = xv.X * yv.X + xv.Y * yv.Y;
            double cos = dot / (xPx * yPx);
            cos = Math.Clamp(cos, -1.0, 1.0);
            float between = (float)(Math.Acos(cos) * 180.0 / Math.PI); // 0..180
            float perpDeviation = MathF.Abs(between - 90f);

            // Rotation preserves lengths, so per-axis scale factors can be computed directly.
            float scaleX = input.TargetPxPerMm / srcPxPerMmX;
            float scaleY = input.TargetPxPerMm / srcPxPerMmY;

            // Combined transform: rotate first, then per-axis scale. M = S * R (Prepend appends on the right).
            var m = new Matrix();
            m.Scale(scaleX, scaleY);
            m.Rotate(rotationDeg, MatrixOrder.Prepend);

            // Transform corners to find the expanded output bounds.
            int w = source.Width;
            int h = source.Height;
            PointF[] corners = { new(0, 0), new(w, 0), new(w, h), new(0, h) };
            m.TransformPoints(corners);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (PointF c in corners)
            {
                minX = MathF.Min(minX, c.X);
                minY = MathF.Min(minY, c.Y);
                maxX = MathF.Max(maxX, c.X);
                maxY = MathF.Max(maxY, c.Y);
            }

            int outW = Math.Max(1, (int)MathF.Ceiling(maxX - minX));
            int outH = Math.Max(1, (int)MathF.Ceiling(maxY - minY));

            if ((long)outW * outH > MaxOutputPixels)
            {
                m.Dispose();
                return ProcessOutcome.Fail(
                    $"Output would be {outW} x {outH} px. Check the mm lengths and the output px/mm value.");
            }

            // Shift so the image starts at (0,0). M = T * S * R.
            m.Translate(-minX, -minY, MatrixOrder.Append);

            Bitmap? bmp = null;
            try
            {
                bmp = new Bitmap(outW, outH, PixelFormat.Format24bppRgb);
                // Embed matching DPI so DPI-aware software also sees the true size.
                bmp.SetResolution(input.TargetPxPerMm * 25.4f, input.TargetPxPerMm * 25.4f);

                using Graphics g = Graphics.FromImage(bmp);
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Transform = m;

                // TileFlipXY avoids the translucent seam GDI+ draws at bitmap edges under a transform.
                using var attrs = new ImageAttributes();
                attrs.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(
                    source,
                    new Rectangle(0, 0, w, h),
                    0, 0, w, h,
                    GraphicsUnit.Pixel,
                    attrs);
                g.ResetTransform();
            }
            catch (Exception ex)
            {
                bmp?.Dispose();
                m.Dispose();
                return ProcessOutcome.Fail($"Image processing failed: {ex.Message}");
            }

            var result = new ProcessedImage(
                bmp,
                m,
                rotationDeg,
                srcPxPerMmX,
                srcPxPerMmY,
                input.TargetPxPerMm,
                perpDeviation);

            return ProcessOutcome.Ok(result);
        }
    }
}
