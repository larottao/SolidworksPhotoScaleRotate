using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhotoScaleRotate
{
    public sealed class CanvasClickEventArgs : EventArgs
    {
        public PointF ImagePoint { get; }
        public CanvasClickEventArgs(PointF imagePoint) => ImagePoint = imagePoint;
    }

    public sealed class CanvasCursorEventArgs : EventArgs
    {
        /// <summary>Cursor position in image pixel coordinates, or null when outside the image.</summary>
        public PointF? ImagePoint { get; }
        public CanvasCursorEventArgs(PointF? imagePoint) => ImagePoint = imagePoint;
    }

    /// <summary>
    /// Image viewport that replaces the old PictureBox-inside-Panel setup.
    /// - Ctrl + wheel: zoom centered on the cursor
    /// - Wheel: vertical pan; Shift + wheel: horizontal pan
    /// - Right or middle mouse drag: free pan
    /// The view state (zoom/pan) is never reset behind the user's back.
    /// </summary>
    public sealed class ImageCanvas : Control
    {
        private Bitmap? _image;
        private float _zoom = 1f;
        private PointF _pan = PointF.Empty; // screen position of image pixel (0,0)
        private bool _panning;
        private Point _lastMouse;

        private const float MinZoom = 0.02f;
        private const float MaxZoom = 40f;
        private const float ZoomStep = 1.15f;
        private const float PanWheelStep = 60f;

        /// <summary>Raised on left click inside the image; point is in image pixel coordinates.</summary>
        public event EventHandler<CanvasClickEventArgs>? ImagePointClicked;
        /// <summary>Raised on mouse move; reports the image-space cursor position (null outside).</summary>
        public event EventHandler<CanvasCursorEventArgs>? ImageCursorMoved;
        /// <summary>Raised after the image content is drawn; subscribers draw overlays in SCREEN coordinates.</summary>
        public event EventHandler<PaintEventArgs>? OverlayPaint;
        /// <summary>Raised whenever zoom or pan changes.</summary>
        public event EventHandler? ViewChanged;

        public ImageCanvas()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);
            BackColor = Color.FromArgb(60, 60, 64);
            Cursor = Cursors.Cross;
            TabStop = true;
        }

        /// <summary>The displayed bitmap. The canvas does NOT own it and never disposes it.</summary>
        public Bitmap? Image
        {
            get => _image;
            set
            {
                _image = value;
                Invalidate();
            }
        }

        public float Zoom => _zoom;

        public PointF ScreenToImage(PointF screen) =>
            new((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

        public PointF ImageToScreen(PointF image) =>
            new(image.X * _zoom + _pan.X, image.Y * _zoom + _pan.Y);

        /// <summary>Fits and centers the current image in the viewport.</summary>
        public void ZoomToFit()
        {
            if (_image == null || ClientSize.Width < 2 || ClientSize.Height < 2) return;

            float zx = (ClientSize.Width - 20f) / _image.Width;
            float zy = (ClientSize.Height - 20f) / _image.Height;
            _zoom = Math.Clamp(MathF.Min(zx, zy), MinZoom, MaxZoom);
            _pan = new PointF(
                (ClientSize.Width - _image.Width * _zoom) / 2f,
                (ClientSize.Height - _image.Height * _zoom) / 2f);
            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ZoomAt(Point screenPoint, float factor)
        {
            float newZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - _zoom) < float.Epsilon) return;

            // Keep the image point under the cursor stationary.
            PointF imgPt = ScreenToImage(screenPoint);
            _zoom = newZoom;
            _pan = new PointF(screenPoint.X - imgPt.X * _zoom, screenPoint.Y - imgPt.Y * _zoom);
            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            // ModifierKeys is read live, so Ctrl works regardless of who had focus before.
            float notches = e.Delta / 120f;
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                ZoomAt(e.Location, MathF.Pow(ZoomStep, notches));
            }
            else if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                _pan = new PointF(_pan.X + notches * PanWheelStep, _pan.Y);
                Invalidate();
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _pan = new PointF(_pan.X, _pan.Y + notches * PanWheelStep);
                Invalidate();
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Focused) Focus();

            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                _panning = true;
                _lastMouse = e.Location;
                Cursor = Cursors.SizeAll;
                return;
            }

            if (e.Button == MouseButtons.Left && _image != null)
            {
                PointF imgPt = ScreenToImage(e.Location);
                if (imgPt.X >= 0 && imgPt.Y >= 0 && imgPt.X <= _image.Width && imgPt.Y <= _image.Height)
                {
                    // Clamp to valid pixel range.
                    imgPt.X = Math.Clamp(imgPt.X, 0f, _image.Width - 1);
                    imgPt.Y = Math.Clamp(imgPt.Y, 0f, _image.Height - 1);
                    ImagePointClicked?.Invoke(this, new CanvasClickEventArgs(imgPt));
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_panning)
            {
                _pan = new PointF(_pan.X + (e.X - _lastMouse.X), _pan.Y + (e.Y - _lastMouse.Y));
                _lastMouse = e.Location;
                Invalidate();
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }

            PointF? imgPt = null;
            if (_image != null)
            {
                PointF p = ScreenToImage(e.Location);
                if (p.X >= 0 && p.Y >= 0 && p.X <= _image.Width && p.Y <= _image.Height)
                    imgPt = p;
            }
            ImageCursorMoved?.Invoke(this, new CanvasCursorEventArgs(imgPt));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_panning && (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle))
            {
                _panning = false;
                Cursor = Cursors.Cross;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            if (_image != null)
            {
                // Nearest neighbor when zoomed in so individual pixels are visible for precise clicking.
                g.InterpolationMode = _zoom >= 2f
                    ? InterpolationMode.NearestNeighbor
                    : InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                var dest = new RectangleF(_pan.X, _pan.Y, _image.Width * _zoom, _image.Height * _zoom);
                var src = new RectangleF(0, 0, _image.Width, _image.Height);
                g.DrawImage(_image, dest, src, GraphicsUnit.Pixel);

                using var border = new Pen(Color.FromArgb(120, 120, 130), 1f);
                g.DrawRectangle(border, dest.X, dest.Y, dest.Width, dest.Height);

                g.PixelOffsetMode = PixelOffsetMode.Default;
                g.InterpolationMode = InterpolationMode.Default;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            OverlayPaint?.Invoke(this, e);
        }
    }
}
