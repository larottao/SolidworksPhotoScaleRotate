using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace PhotoScaleRotate
{
    public partial class MainForm : Form
    {
        private enum ClickMode { XAxis, YAxis, Ruler }

        private Bitmap? _original;
        private string _originalPath = string.Empty;
        private ProcessedImage? _processed;

        // Axis and ruler points are ALWAYS stored in ORIGINAL image pixel coordinates.
        private PointF? _x1, _x2, _y1, _y2;
        private readonly List<(PointF Start, PointF End)> _rulers = new();
        private PointF? _rulerStart;
        private PointF? _rulerHover;

        private ClickMode _mode = ClickMode.XAxis;

        private bool ViewingResult => buttonShowResult.Checked && _processed != null;

        private readonly WheelRedirectFilter _wheelFilter;

        public MainForm()
        {
            InitializeComponent();
            WireUpEvents();
            comboMode.SelectedIndex = 0;

            // Route wheel messages to the canvas whenever the cursor is over it, regardless of focus.
            _wheelFilter = new WheelRedirectFilter(canvas);
            Application.AddMessageFilter(_wheelFilter);

            SetMessage("Open an image to start. Ctrl+wheel = zoom, wheel = pan vertically, Shift+wheel = pan horizontally, right-drag = pan.", isError: false);
        }

        /// <summary>
        /// Windows delivers WM_MOUSEWHEEL to the focused control, not the one under the cursor.
        /// This filter forwards wheel messages to the canvas when the cursor is over it, so
        /// zooming works while a toolbar textbox has focus.
        /// </summary>
        private sealed class WheelRedirectFilter : IMessageFilter
        {
            private const int WM_MOUSEWHEEL = 0x020A;
            private readonly ImageCanvas _target;

            public WheelRedirectFilter(ImageCanvas target) => _target = target;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_MOUSEWHEEL) return false;
                if (_target.IsDisposed || !_target.Visible || !_target.IsHandleCreated) return false;

                Point clientPos = _target.PointToClient(Cursor.Position);
                if (!_target.ClientRectangle.Contains(clientPos)) return false;
                if (m.HWnd == _target.Handle) return false; // already going to the canvas

                NativeMethods.SendMessage(_target.Handle, m.Msg, m.WParam, m.LParam);
                return true; // suppress delivery to the focused control
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        }

        private void WireUpEvents()
        {
            buttonOpen.Click += (_, _) => OpenImage();
            buttonSaveAs.Click += (_, _) => SaveAs();
            buttonProcess.Click += (_, _) => ProcessAndSave();
            buttonClearMarks.Click += (_, _) => ClearMarks();
            buttonFit.Click += (_, _) => canvas.ZoomToFit();
            buttonShowResult.CheckedChanged += (_, _) => SwitchView();
            comboMode.SelectedIndexChanged += (_, _) => ModeChanged();

            canvas.ImagePointClicked += Canvas_ImagePointClicked;
            canvas.ImageCursorMoved += Canvas_ImageCursorMoved;
            canvas.OverlayPaint += Canvas_OverlayPaint;
            canvas.ViewChanged += (_, _) => statusZoom.Text = $"{canvas.Zoom * 100f:F0}%";

            KeyDown += Form1_KeyDown;
        }

        #region File open / save

        private void OpenImage()
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All files|*.*"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            (Bitmap? bmp, string? error) = LoadBitmapCopy(ofd.FileName);
            if (bmp == null)
            {
                SetMessage(error ?? "Failed to load image.", isError: true);
                return;
            }

            buttonShowResult.Checked = false;
            _processed?.Dispose();
            _processed = null;
            buttonShowResult.Enabled = false;
            buttonSaveAs.Enabled = false;

            _original?.Dispose();
            _original = bmp;
            _originalPath = ofd.FileName;

            _x1 = _x2 = _y1 = _y2 = null;
            _rulers.Clear();
            _rulerStart = null;
            _rulerHover = null;

            canvas.Image = _original;
            canvas.ZoomToFit();
            SetMessage($"Loaded {Path.GetFileName(_originalPath)} ({_original.Width} x {_original.Height} px). Click two points for the X axis.", isError: false);
        }

        /// <summary>Loads a copy of the file (so the file is not locked) and applies EXIF orientation.</summary>
        private static (Bitmap? Bmp, string? Error) LoadBitmapCopy(string path)
        {
            try
            {
                using var loaded = new Bitmap(path);
                var copy = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(copy))
                {
                    g.DrawImage(loaded, new Rectangle(0, 0, loaded.Width, loaded.Height));
                }
                ApplyExifOrientation(loaded, copy);
                return (copy, null);
            }
            catch (Exception ex)
            {
                return (null, $"Failed to load image: {ex.Message}");
            }
        }

        /// <summary>Camera photos often store rotation only as EXIF tag 274; bake it into the pixels.</summary>
        private static void ApplyExifOrientation(Bitmap source, Bitmap target)
        {
            const int OrientationTag = 0x0112;
            if (Array.IndexOf(source.PropertyIdList, OrientationTag) < 0) return;

            PropertyItem? item = source.GetPropertyItem(OrientationTag);
            if (item?.Value == null || item.Value.Length < 2) return;

            ushort orientation = BitConverter.ToUInt16(item.Value, 0);
            RotateFlipType flip = orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.RotateNoneFlipY,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };
            if (flip != RotateFlipType.RotateNoneFlipNone)
                target.RotateFlip(flip);
        }

        private void SaveAs()
        {
            if (_processed == null)
            {
                SetMessage("Nothing to save yet. Click Process + Save first.", isError: true);
                return;
            }

            string dir = Path.GetDirectoryName(_originalPath) ?? ".";
            string name = Path.GetFileNameWithoutExtension(_originalPath);
            string suffix = Math.Abs(_processed.TargetPxPerMm - 1f) < 0.0001f
                ? "_1to1"
                : $"_{_processed.TargetPxPerMm.ToString("0.##", CultureInfo.InvariantCulture)}pxmm";

            using var sfd = new SaveFileDialog
            {
                Filter = "PNG|*.png|JPEG|*.jpg|TIFF|*.tif|BMP|*.bmp",
                FilterIndex = 1,
                InitialDirectory = dir,
                FileName = $"{name}{suffix}.png",
                OverwritePrompt = true
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            // Hard rule: never overwrite the original photo.
            if (string.Equals(Path.GetFullPath(sfd.FileName), Path.GetFullPath(_originalPath), StringComparison.OrdinalIgnoreCase))
            {
                SetMessage("Refusing to overwrite the original photo. Choose a different file name.", isError: true);
                return;
            }

            string ext = Path.GetExtension(sfd.FileName);
            try
            {
                SaveBitmap(_processed.Bitmap, sfd.FileName, ext);
                WriteMetadataJson(sfd.FileName);
            }
            catch (Exception ex)
            {
                SetMessage($"Save failed: {ex.Message}", isError: true);
                return;
            }

            SetMessage($"Saved {sfd.FileName} (+ metadata .json). Insert it in SolidWorks as a sketch picture" +
                       (Math.Abs(_processed.TargetPxPerMm - 1f) < 0.0001f
                           ? " - it will be at true scale."
                           : $" and set its width to {_processed.WidthMm:F2} mm."),
                isError: false);
        }

        private static void SaveBitmap(Bitmap bmp, string path, string extension)
        {
            if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                ImageCodecInfo? codec = ImageCodecInfo.GetImageEncoders()
                    .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                if (codec != null)
                {
                    using var ep = new EncoderParameters(1);
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                    bmp.Save(path, codec, ep);
                    return;
                }
                bmp.Save(path, ImageFormat.Jpeg);
            }
            else if (extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
            {
                bmp.Save(path, ImageFormat.Tiff);
            }
            else if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                bmp.Save(path, ImageFormat.Bmp);
            }
            else
            {
                bmp.Save(path, ImageFormat.Png);
            }
        }

        #endregion

        #region JSON metadata

        private sealed record PointDto(float X, float Y);
        private sealed record AxisDto(PointDto P1, PointDto P2, float LengthMm);
        private sealed record RulerDto(PointDto Start, PointDto End);

        private sealed record MetadataDto(
            string OriginalImage,
            string SavedImage,
            int SavedWidthPx,
            int SavedHeightPx,
            float WidthMm,
            float HeightMm,
            float TargetPxPerMm,
            float EmbeddedDpi,
            float RotationAppliedDeg,
            float SourcePxPerMmX,
            float SourcePxPerMmY,
            float PerpendicularityDeviationDeg,
            AxisDto? XAxisOriginalPx,
            AxisDto? YAxisOriginalPx,
            IReadOnlyList<RulerDto> RulersOriginalPx,
            string TimestampUtc);

        private void WriteMetadataJson(string savedImagePath)
        {
            if (_processed == null) return;

            TryParseLength(textXmm.Text, out float xMm);
            TryParseLength(textYmm.Text, out float yMm);

            var meta = new MetadataDto(
                OriginalImage: Path.GetFileName(_originalPath),
                SavedImage: Path.GetFileName(savedImagePath),
                SavedWidthPx: _processed.Bitmap.Width,
                SavedHeightPx: _processed.Bitmap.Height,
                WidthMm: _processed.WidthMm,
                HeightMm: _processed.HeightMm,
                TargetPxPerMm: _processed.TargetPxPerMm,
                EmbeddedDpi: _processed.TargetPxPerMm * 25.4f,
                RotationAppliedDeg: _processed.RotationAppliedDeg,
                SourcePxPerMmX: _processed.SourcePxPerMmX,
                SourcePxPerMmY: _processed.SourcePxPerMmY,
                PerpendicularityDeviationDeg: _processed.PerpendicularityDeviationDeg,
                XAxisOriginalPx: _x1 != null && _x2 != null
                    ? new AxisDto(ToDto(_x1.Value), ToDto(_x2.Value), xMm)
                    : null,
                YAxisOriginalPx: _y1 != null && _y2 != null
                    ? new AxisDto(ToDto(_y1.Value), ToDto(_y2.Value), yMm)
                    : null,
                RulersOriginalPx: _rulers.Select(r => new RulerDto(ToDto(r.Start), ToDto(r.End))).ToList(),
                TimestampUtc: DateTime.UtcNow.ToString("o"));

            string json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(savedImagePath + ".json", json);

            static PointDto ToDto(PointF p) => new(p.X, p.Y);
        }

        #endregion

        #region Calibration workflow

        private void ModeChanged()
        {
            _mode = comboMode.SelectedIndex switch
            {
                1 => ClickMode.YAxis,
                2 => ClickMode.Ruler,
                _ => ClickMode.XAxis
            };
            _rulerStart = null;
            _rulerHover = null;
            canvas.Invalidate();
        }

        private void Canvas_ImagePointClicked(object? sender, CanvasClickEventArgs e)
        {
            if (_original == null) return;

            PointF pt = e.ImagePoint;

            if (ViewingResult)
            {
                if (_mode != ClickMode.Ruler)
                {
                    SetMessage("Axes are edited on the original photo. Toggle 'Result view' off first.", isError: false);
                    return;
                }
                // Convert the click on the processed image back into original coordinates.
                (bool ok, PointF originalPt) = MapProcessedToOriginal(pt);
                if (!ok) return;
                pt = originalPt;
            }

            switch (_mode)
            {
                case ClickMode.XAxis:
                    if (_x1 == null) { _x1 = pt; SetMessage("X axis: click the second point.", isError: false); }
                    else if (_x2 == null)
                    {
                        _x2 = pt;
                        InvalidateProcessed();
                        if (_y1 == null || _y2 == null)
                        {
                            comboMode.SelectedIndex = 1; // auto-advance to the Y axis
                            SetMessage("X axis set. Now click two points for the Y axis.", isError: false);
                        }
                        else
                        {
                            SetMessage("X axis redefined. Enter lengths and click Process + Save.", isError: false);
                        }
                    }
                    else
                    {
                        _x1 = pt; _x2 = null; // third click restarts the axis at the new point
                        InvalidateProcessed();
                        SetMessage("X axis restarted: click the second point.", isError: false);
                    }
                    break;

                case ClickMode.YAxis:
                    if (_y1 == null) { _y1 = pt; SetMessage("Y axis: click the second point.", isError: false); }
                    else if (_y2 == null)
                    {
                        _y2 = pt;
                        InvalidateProcessed();
                        SetMessage(_x1 != null && _x2 != null
                            ? "Both axes set. Enter their real lengths in mm and click Process + Save."
                            : "Y axis set. Now define the X axis.", isError: false);
                        if (_x1 == null || _x2 == null) comboMode.SelectedIndex = 0;
                    }
                    else
                    {
                        _y1 = pt; _y2 = null;
                        InvalidateProcessed();
                        SetMessage("Y axis restarted: click the second point.", isError: false);
                    }
                    break;

                case ClickMode.Ruler:
                    if (_rulerStart == null)
                    {
                        _rulerStart = pt;
                        _rulerHover = pt;
                    }
                    else
                    {
                        _rulers.Add((_rulerStart.Value, pt));
                        _rulerStart = null;
                        _rulerHover = null;
                    }
                    break;
            }

            canvas.Invalidate();
        }

        private (bool Ok, PointF Point) MapProcessedToOriginal(PointF processedPt)
        {
            if (_processed == null) return (false, PointF.Empty);
            using Matrix inverse = _processed.OriginalToProcessed.Clone();
            if (!inverse.IsInvertible) return (false, PointF.Empty);
            inverse.Invert();
            PointF[] pts = { processedPt };
            inverse.TransformPoints(pts);
            return (true, pts[0]);
        }

        /// <summary>Any change to the axes makes a previously applied result stale.</summary>
        private void InvalidateProcessed()
        {
            if (_processed == null) return;
            buttonShowResult.Checked = false; // triggers SwitchView back to the original
            _processed.Dispose();
            _processed = null;
            buttonShowResult.Enabled = false;
            buttonSaveAs.Enabled = false;
            canvas.Image = _original;
        }

        /// <summary>
        /// One-click workflow: computes the photo's native px/mm from the axes (rotation-only
        /// resample, no detail loss), processes, auto-saves next to the original, copies the
        /// SolidWorks width to the clipboard, and shows the values to enter in SolidWorks.
        /// </summary>
        private void ProcessAndSave()
        {
            if (_original == null)
            {
                SetMessage("Load an image first.", isError: true);
                return;
            }
            if (_x1 == null || _x2 == null)
            {
                SetMessage("Define the X axis by clicking two points on the image.", isError: true);
                return;
            }
            if (_y1 == null || _y2 == null)
            {
                SetMessage("Define the Y axis by clicking two points on the image.", isError: true);
                return;
            }
            if (!TryParseLength(textXmm.Text, out float xMm))
            {
                SetMessage("Enter a positive X length in mm.", isError: true);
                return;
            }
            if (!TryParseLength(textYmm.Text, out float yMm))
            {
                SetMessage("Enter a positive Y length in mm.", isError: true);
                return;
            }

            // Native resolution: the photo keeps its own pixel density, so nothing is lost.
            float pxPerMmX = new AxisLine(_x1.Value, _x2.Value).LengthPx / xMm;
            float pxPerMmY = new AxisLine(_y1.Value, _y2.Value).LengthPx / yMm;
            float native = (pxPerMmX + pxPerMmY) / 2f;

            var input = new CalibrationInput(
                new AxisLine(_x1.Value, _x2.Value),
                new AxisLine(_y1.Value, _y2.Value),
                xMm, yMm, native);

            ProcessOutcome outcome = ImageProcessor.Process(_original, input);
            if (outcome.Result == null)
            {
                SetMessage(outcome.Error ?? "Processing failed.", isError: true);
                return;
            }

            _processed?.Dispose();
            _processed = outcome.Result;

            buttonShowResult.Enabled = true;
            buttonSaveAs.Enabled = true;
            buttonShowResult.Checked = true;
            SwitchView(); // rebind explicitly: CheckedChanged does not fire if it was already checked

            // Auto-save next to the original, never overwriting anything.
            string dir = Path.GetDirectoryName(_originalPath) ?? ".";
            string name = Path.GetFileNameWithoutExtension(_originalPath);
            string savePath = Path.Combine(dir, $"{name}_traced.png");
            int counter = 2;
            while (File.Exists(savePath))
                savePath = Path.Combine(dir, $"{name}_traced_{counter++}.png");

            try
            {
                SaveBitmap(_processed.Bitmap, savePath, ".png");
                WriteMetadataJson(savePath);
            }
            catch (Exception ex)
            {
                SetMessage($"Processed, but saving failed: {ex.Message}", isError: true);
                return;
            }

            // Format with the current Windows locale so the value pastes cleanly into SolidWorks.
            string widthText = _processed.WidthMm.ToString("0.##", CultureInfo.CurrentCulture);
            string heightText = _processed.HeightMm.ToString("0.##", CultureInfo.CurrentCulture);

            bool clipboardOk = true;
            try { Clipboard.SetText(widthText); }
            catch (Exception) { clipboardOk = false; } // clipboard can be locked by another process

            string warn = _processed.PerpendicularityDeviationDeg > 2f
                ? $"\r\n\r\nWARNING: the Y axis is {_processed.PerpendicularityDeviationDeg:F1} deg away from perpendicular to X - the photo may have perspective distortion. Consider re-shooting more head-on."
                : string.Empty;

            SetMessage($"Saved {savePath}. SolidWorks width: {widthText} mm (on clipboard).", isError: false);

            MessageBox.Show(this,
                $"Saved:\r\n{savePath}\r\n\r\n" +
                "In SolidWorks: insert the file as a sketch picture, double-click it, keep the aspect-ratio lock checked, and enter:\r\n\r\n" +
                $"        Width:   {widthText} mm" + (clipboardOk ? "   (already on your clipboard - just paste)" : "") + "\r\n" +
                $"        Height:  {heightText} mm   (fills in by itself with the lock on)\r\n" +
                "        Angle:   0\r\n\r\n" +
                $"Resolution kept at native {native:0.###} px/mm (X: {pxPerMmX:0.###}, Y: {pxPerMmY:0.###}) - no detail loss.\r\n" +
                $"Rotation applied: {_processed.RotationAppliedDeg:F2} deg." + warn,
                "Ready for SolidWorks",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static bool TryParseLength(string? text, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text)) return false;
            // Accept both decimal comma and decimal point regardless of Windows locale.
            string normalized = text.Trim().Replace(',', '.');
            if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return false;
            if (!float.IsFinite(v) || v <= 0f) return false;
            value = v;
            return true;
        }

        private void ClearMarks()
        {
            _x1 = _x2 = _y1 = _y2 = null;
            _rulers.Clear();
            _rulerStart = null;
            _rulerHover = null;
            InvalidateProcessed();
            canvas.Invalidate();
            SetMessage("Marks cleared.", isError: false);
        }

        private void SwitchView()
        {
            if (_original == null) return;
            canvas.Image = ViewingResult ? _processed!.Bitmap : _original;
            canvas.ZoomToFit();
            canvas.Invalidate();
        }

        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape) return;

            bool changed = false;
            if (_rulerStart != null) { _rulerStart = null; _rulerHover = null; changed = true; }
            else if (_x2 == null && _x1 != null) { _x1 = null; changed = true; }
            else if (_y2 == null && _y1 != null) { _y1 = null; changed = true; }

            if (changed)
            {
                canvas.Invalidate();
                SetMessage("Cancelled.", isError: false);
            }
        }

        #endregion

        #region Overlay drawing and status

        private void Canvas_ImageCursorMoved(object? sender, CanvasCursorEventArgs e)
        {
            if (e.ImagePoint is PointF p)
            {
                string text = $"{p.X:F0}, {p.Y:F0} px";
                if (ViewingResult && _processed != null)
                    text += $"  |  {p.X / _processed.TargetPxPerMm:F2}, {p.Y / _processed.TargetPxPerMm:F2} mm";
                statusCursor.Text = text;
            }
            else
            {
                statusCursor.Text = string.Empty;
            }

            if (_rulerStart != null)
            {
                if (e.ImagePoint is PointF hover)
                {
                    if (ViewingResult)
                    {
                        (bool ok, PointF orig) = MapProcessedToOriginal(hover);
                        if (ok) _rulerHover = orig;
                    }
                    else
                    {
                        _rulerHover = hover;
                    }
                }
                canvas.Invalidate();
            }
        }

        /// <summary>Maps an original-space point to the current view's screen coordinates.</summary>
        private PointF MapToScreen(PointF originalPt)
        {
            PointF viewPt = ViewingResult && _processed != null ? _processed.MapPoint(originalPt) : originalPt;
            return canvas.ImageToScreen(viewPt);
        }

        private void Canvas_OverlayPaint(object? sender, PaintEventArgs e)
        {
            if (_original == null) return;
            Graphics g = e.Graphics;

            // Fixed rulers (orange) with live length labels.
            using (var penRuler = new Pen(Color.Orange, 2f))
            {
                foreach ((PointF start, PointF end) in _rulers)
                    DrawMeasuredLine(g, penRuler, Color.Orange, start, end, RulerLengthText(start, end));

                if (_rulerStart != null && _rulerHover != null)
                {
                    using var penTemp = new Pen(Color.Orange, 2f) { DashStyle = DashStyle.Dash };
                    DrawMeasuredLine(g, penTemp, Color.Orange, _rulerStart.Value, _rulerHover.Value,
                        RulerLengthText(_rulerStart.Value, _rulerHover.Value));
                }
            }

            // X axis (red)
            using (var penX = new Pen(Color.Red, 2f))
            {
                string label = TryParseLength(textXmm.Text, out float mm) ? $"X = {mm:0.##} mm" : "X";
                DrawAxis(g, penX, Color.Red, _x1, _x2, label);
            }

            // Y axis (blue)
            using (var penY = new Pen(Color.DodgerBlue, 2f))
            {
                string label = TryParseLength(textYmm.Text, out float mm) ? $"Y = {mm:0.##} mm" : "Y";
                DrawAxis(g, penY, Color.DodgerBlue, _y1, _y2, label);
            }
        }

        private void DrawAxis(Graphics g, Pen pen, Color color, PointF? p1, PointF? p2, string label)
        {
            if (p1 == null) return;
            PointF s1 = MapToScreen(p1.Value);
            if (p2 != null)
            {
                PointF s2 = MapToScreen(p2.Value);
                g.DrawLine(pen, s1, s2);
                DrawMarker(g, s2, color);
                DrawLabel(g, new PointF((s1.X + s2.X) / 2f, (s1.Y + s2.Y) / 2f), label, color);
            }
            DrawMarker(g, s1, color);
        }

        private void DrawMeasuredLine(Graphics g, Pen pen, Color color, PointF start, PointF end, string label)
        {
            PointF s1 = MapToScreen(start);
            PointF s2 = MapToScreen(end);
            g.DrawLine(pen, s1, s2);
            DrawMarker(g, s1, color);
            DrawMarker(g, s2, color);
            if (!string.IsNullOrEmpty(label))
                DrawLabel(g, new PointF((s1.X + s2.X) / 2f, (s1.Y + s2.Y) / 2f), label, color);
        }

        private string RulerLengthText(PointF start, PointF end)
        {
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;
            float px = MathF.Sqrt(dx * dx + dy * dy);
            string text = $"{px:F0} px";

            if (_processed != null)
            {
                // Exact: measure in the calibrated output space.
                PointF a = _processed.MapPoint(start);
                PointF b = _processed.MapPoint(end);
                float mm = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) / _processed.TargetPxPerMm;
                text += $" = {mm:F2} mm";
            }
            else if (_x1 != null && _x2 != null && _y1 != null && _y2 != null &&
                     TryParseLength(textXmm.Text, out float xMm) && TryParseLength(textYmm.Text, out float yMm))
            {
                // Estimate from the current calibration (per-axis scales may differ).
                float pxPerMmX = new AxisLine(_x1.Value, _x2.Value).LengthPx / xMm;
                float pxPerMmY = new AxisLine(_y1.Value, _y2.Value).LengthPx / yMm;
                if (pxPerMmX > 0f && pxPerMmY > 0f)
                {
                    float mm = MathF.Sqrt((dx / pxPerMmX) * (dx / pxPerMmX) + (dy / pxPerMmY) * (dy / pxPerMmY));
                    text += $" ~ {mm:F2} mm";
                }
            }

            return text;
        }

        private static void DrawMarker(Graphics g, PointF pt, Color color)
        {
            const float size = 9f;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, pt.X - size / 2f, pt.Y - size / 2f, size, size);
            using var pen = new Pen(Color.White, 1.5f);
            g.DrawEllipse(pen, pt.X - size / 2f, pt.Y - size / 2f, size, size);
        }

        private void DrawLabel(Graphics g, PointF at, string text, Color color)
        {
            SizeF size = g.MeasureString(text, Font);
            var rect = new RectangleF(at.X + 6f, at.Y - size.Height - 4f, size.Width + 6f, size.Height + 2f);
            using var back = new SolidBrush(Color.FromArgb(210, Color.White));
            g.FillRectangle(back, rect);
            using var fore = new SolidBrush(color);
            g.DrawString(text, Font, fore, rect.X + 3f, rect.Y + 1f);
        }

        private void SetMessage(string text, bool isError)
        {
            statusMessage.Text = text;
            statusMessage.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            Application.RemoveMessageFilter(_wheelFilter);
            canvas.Image = null;
            _processed?.Dispose();
            _original?.Dispose();
        }
    }
}
