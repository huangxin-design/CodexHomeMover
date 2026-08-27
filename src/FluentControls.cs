using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexHomeMover
{
    internal static class FluentDrawing
    {
        internal static Color Blend(Color first, Color second, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                (int)Math.Round(first.A + (second.A - first.A) * amount),
                (int)Math.Round(first.R + (second.R - first.R) * amount),
                (int)Math.Round(first.G + (second.G - first.G) * amount),
                (int)Math.Round(first.B + (second.B - first.B) * amount));
        }

        internal static Color SurfaceBehind(Control control)
        {
            Control current = control == null ? null : control.Parent;
            while (current != null)
            {
                FluentPanel fluentParent = current as FluentPanel;
                if (fluentParent != null)
                {
                    return fluentParent.FillColor;
                }
                if (current.BackColor.A == 255)
                {
                    return current.BackColor;
                }
                current = current.Parent;
            }
            return Color.FromArgb(23, 22, 20);
        }

        internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(1, radius * 2);
            Rectangle arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class FluentPanel : Panel
    {
        internal Color FillColor { get; set; }
        internal Color GradientColor { get; set; }
        internal Color BorderColor { get; set; }
        internal int CornerRadius { get; set; }

        internal FluentPanel()
        {
            FillColor = Color.FromArgb(45, 45, 45);
            GradientColor = Color.Transparent;
            BorderColor = Color.FromArgb(63, 63, 63);
            CornerRadius = 10;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(FluentDrawing.SurfaceBehind(this));
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = FluentDrawing.RoundedRectangle(bounds, CornerRadius))
            using (Pen border = new Pen(BorderColor))
            {
                Brush fill = GradientColor.A == 0
                    ? (Brush)new SolidBrush(FillColor)
                    : new LinearGradientBrush(bounds, GradientColor, FillColor, LinearGradientMode.Vertical);
                using (fill)
                {
                    e.Graphics.FillPath(fill, path);
                }
                if (BorderColor.A > 0)
                {
                    e.Graphics.DrawPath(border, path);
                }
            }
        }
    }

    internal enum FluentIcon
    {
        None,
        ZoomOut,
        ZoomIn,
        Folder,
        Sparkles,
        CheckCircle,
        ArrowRight,
        Undo,
        Trash,
        Close,
        Document
    }

    internal sealed class FluentButton : Button
    {
        private bool pointerOver;
        private bool pointerDown;

        internal Color NormalColor { get; set; }
        internal Color HoverColor { get; set; }
        internal Color PressedColor { get; set; }
        internal Color BorderColor { get; set; }
        internal Color DisabledColor { get; set; }
        internal int CornerRadius { get; set; }
        internal FluentIcon Icon { get; set; }

        internal FluentButton()
        {
            NormalColor = Color.FromArgb(50, 50, 50);
            HoverColor = Color.FromArgb(61, 61, 61);
            PressedColor = Color.FromArgb(71, 71, 71);
            BorderColor = Color.FromArgb(72, 72, 72);
            DisabledColor = Color.FromArgb(43, 43, 43);
            CornerRadius = 7;
            Icon = FluentIcon.None;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            pointerOver = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            pointerOver = false;
            pointerDown = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pointerDown = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pointerDown = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(FluentDrawing.SurfaceBehind(this));
            Color background = !Enabled ? DisabledColor : pointerDown ? PressedColor : pointerOver ? HoverColor : NormalColor;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = FluentDrawing.RoundedRectangle(bounds, CornerRadius))
            using (Pen border = new Pen(Enabled ? BorderColor : DisabledColor))
            {
                Color top = FluentDrawing.Blend(background, Color.White, Enabled ? 0.08F : 0.03F);
                Color bottom = FluentDrawing.Blend(background, Color.Black, 0.05F);
                using (LinearGradientBrush fill = new LinearGradientBrush(
                    bounds, top, bottom, LinearGradientMode.Vertical))
                {
                    e.Graphics.FillPath(fill, path);
                }
                if (BorderColor.A > 0)
                {
                    e.Graphics.DrawPath(border, path);
                }
            }

            Color textColor = Enabled ? ForeColor : Color.FromArgb(102, 102, 102);
            if (Icon == FluentIcon.None)
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                return;
            }

            int iconSize = Math.Max(12, Math.Min(22, Font.Height));
            Size textSize = string.IsNullOrWhiteSpace(Text)
                ? Size.Empty
                : TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int spacing = textSize.Width == 0 ? 0 : Math.Max(5, iconSize / 3);
            int totalWidth = iconSize + spacing + textSize.Width;
            int startX = Math.Max(4, (Width - totalWidth) / 2);
            Rectangle iconBounds = new Rectangle(startX, (Height - iconSize) / 2, iconSize, iconSize);
            DrawIcon(e.Graphics, iconBounds, textColor);
            if (textSize.Width > 0)
            {
                Rectangle textBounds = new Rectangle(iconBounds.Right + spacing, 0,
                    Math.Max(1, Width - iconBounds.Right - spacing - 4), Height);
                TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        private void DrawIcon(Graphics graphics, Rectangle bounds, Color color)
        {
            float left = bounds.Left + 1F;
            float top = bounds.Top + 1F;
            float right = bounds.Right - 2F;
            float bottom = bounds.Bottom - 2F;
            float width = right - left;
            float height = bottom - top;
            float stroke = Math.Max(1.35F, bounds.Width / 11F);
            using (Pen pen = new Pen(color, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (Icon)
                {
                    case FluentIcon.ZoomOut:
                    case FluentIcon.ZoomIn:
                        graphics.DrawEllipse(pen, left, top, width * 0.68F, height * 0.68F);
                        graphics.DrawLine(pen, left + width * 0.57F, top + height * 0.57F, right, bottom);
                        graphics.DrawLine(pen, left + width * 0.17F, top + height * 0.34F,
                            left + width * 0.51F, top + height * 0.34F);
                        if (Icon == FluentIcon.ZoomIn)
                        {
                            graphics.DrawLine(pen, left + width * 0.34F, top + height * 0.17F,
                                left + width * 0.34F, top + height * 0.51F);
                        }
                        break;

                    case FluentIcon.Folder:
                        using (GraphicsPath folder = new GraphicsPath())
                        {
                            folder.AddLines(new[]
                            {
                                new PointF(left, top + height * 0.28F),
                                new PointF(left + width * 0.34F, top + height * 0.28F),
                                new PointF(left + width * 0.45F, top + height * 0.40F),
                                new PointF(right, top + height * 0.40F),
                                new PointF(right, bottom),
                                new PointF(left, bottom),
                                new PointF(left, top + height * 0.28F)
                            });
                            graphics.DrawPath(pen, folder);
                        }
                        break;

                    case FluentIcon.Sparkles:
                        DrawSparkle(graphics, pen, left + width * 0.38F, top + height * 0.42F,
                            width * 0.33F, height * 0.42F);
                        DrawSparkle(graphics, pen, left + width * 0.78F, top + height * 0.22F,
                            width * 0.15F, height * 0.18F);
                        DrawSparkle(graphics, pen, left + width * 0.76F, top + height * 0.76F,
                            width * 0.13F, height * 0.16F);
                        break;

                    case FluentIcon.CheckCircle:
                        graphics.DrawEllipse(pen, left, top, width, height);
                        graphics.DrawLines(pen, new[]
                        {
                            new PointF(left + width * 0.22F, top + height * 0.53F),
                            new PointF(left + width * 0.43F, top + height * 0.72F),
                            new PointF(left + width * 0.78F, top + height * 0.32F)
                        });
                        break;

                    case FluentIcon.ArrowRight:
                        graphics.DrawLine(pen, left, top + height * 0.5F, right, top + height * 0.5F);
                        graphics.DrawLines(pen, new[]
                        {
                            new PointF(left + width * 0.62F, top + height * 0.18F),
                            new PointF(right, top + height * 0.5F),
                            new PointF(left + width * 0.62F, top + height * 0.82F)
                        });
                        break;

                    case FluentIcon.Undo:
                        graphics.DrawArc(pen, left + width * 0.12F, top + height * 0.18F,
                            width * 0.78F, height * 0.68F, 205F, 260F);
                        graphics.DrawLines(pen, new[]
                        {
                            new PointF(left + width * 0.10F, top + height * 0.18F),
                            new PointF(left + width * 0.10F, top + height * 0.55F),
                            new PointF(left + width * 0.43F, top + height * 0.38F)
                        });
                        break;

                    case FluentIcon.Trash:
                        graphics.DrawRectangle(pen, left + width * 0.20F, top + height * 0.30F,
                            width * 0.60F, height * 0.65F);
                        graphics.DrawLine(pen, left + width * 0.10F, top + height * 0.24F,
                            right - width * 0.10F, top + height * 0.24F);
                        graphics.DrawLine(pen, left + width * 0.38F, top + height * 0.12F,
                            left + width * 0.62F, top + height * 0.12F);
                        break;

                    case FluentIcon.Close:
                        graphics.DrawLine(pen, left + width * 0.15F, top + height * 0.15F,
                            right - width * 0.15F, bottom - height * 0.15F);
                        graphics.DrawLine(pen, right - width * 0.15F, top + height * 0.15F,
                            left + width * 0.15F, bottom - height * 0.15F);
                        break;

                    case FluentIcon.Document:
                        using (GraphicsPath document = new GraphicsPath())
                        {
                            document.AddLines(new[]
                            {
                                new PointF(left + width * 0.18F, top),
                                new PointF(left + width * 0.66F, top),
                                new PointF(right - width * 0.05F, top + height * 0.28F),
                                new PointF(right - width * 0.05F, bottom),
                                new PointF(left + width * 0.18F, bottom),
                                new PointF(left + width * 0.18F, top)
                            });
                            graphics.DrawPath(pen, document);
                        }
                        graphics.DrawLine(pen, left + width * 0.34F, top + height * 0.52F,
                            right - width * 0.20F, top + height * 0.52F);
                        graphics.DrawLine(pen, left + width * 0.34F, top + height * 0.70F,
                            right - width * 0.28F, top + height * 0.70F);
                        break;
                }
            }
        }

        private static void DrawSparkle(Graphics graphics, Pen pen, float centerX, float centerY,
            float width, float height)
        {
            graphics.DrawLine(pen, centerX, centerY - height / 2F, centerX, centerY + height / 2F);
            graphics.DrawLine(pen, centerX - width / 2F, centerY, centerX + width / 2F, centerY);
        }
    }

    internal sealed class FluentCheckBox : CheckBox
    {
        internal Color AccentColor { get; set; }
        internal Color BoxColor { get; set; }
        internal Color BorderColor { get; set; }

        internal FluentCheckBox()
        {
            AccentColor = Color.FromArgb(255, 199, 39);
            BoxColor = Color.FromArgb(39, 37, 33);
            BorderColor = Color.FromArgb(112, 103, 84);
            AutoSize = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size textSize = TextRenderer.MeasureText(Text, Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return new Size(textSize.Width + 25, Math.Max(20, textSize.Height + 2));
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Invalidate();
            base.OnCheckedChanged(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(FluentDrawing.SurfaceBehind(this));
            int boxSize = Math.Max(14, Math.Min(17, Height - 3));
            Rectangle box = new Rectangle(1, (Height - boxSize) / 2, boxSize, boxSize);
            Color fillColor = Checked ? AccentColor : BoxColor;
            Color outlineColor = Enabled ? (Checked ? AccentColor : BorderColor) : Color.FromArgb(70, 68, 62);
            using (GraphicsPath path = FluentDrawing.RoundedRectangle(box, 3))
            using (SolidBrush fill = new SolidBrush(fillColor))
            using (Pen outline = new Pen(outlineColor, 1F))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(outline, path);
            }

            if (Checked)
            {
                Color checkColor = Enabled ? Color.FromArgb(39, 31, 8) : Color.FromArgb(94, 83, 48);
                using (Pen check = new Pen(checkColor, Math.Max(1.5F, boxSize / 8F)))
                {
                    check.StartCap = LineCap.Round;
                    check.EndCap = LineCap.Round;
                    check.LineJoin = LineJoin.Round;
                    e.Graphics.DrawLines(check, new[]
                    {
                        new PointF(box.Left + box.Width * 0.23F, box.Top + box.Height * 0.52F),
                        new PointF(box.Left + box.Width * 0.43F, box.Top + box.Height * 0.72F),
                        new PointF(box.Left + box.Width * 0.78F, box.Top + box.Height * 0.30F)
                    });
                }
            }

            Rectangle textBounds = new Rectangle(box.Right + 7, 0,
                Math.Max(1, Width - box.Right - 7), Height);
            Color textColor = Enabled ? ForeColor : Color.FromArgb(104, 101, 93);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, textBounds, textColor,
                    FluentDrawing.SurfaceBehind(this));
            }
        }
    }

    internal sealed class FluentProgressBar : Control
    {
        private int currentValue;

        internal Color TrackColor { get; set; }
        internal Color ProgressColor { get; set; }
        internal Color ProgressEndColor { get; set; }

        internal int Value
        {
            get { return currentValue; }
            set
            {
                currentValue = Math.Max(0, Math.Min(100, value));
                Invalidate();
            }
        }

        internal int Minimum { get { return 0; } }
        internal int Maximum { get { return 100; } }

        internal FluentProgressBar()
        {
            TrackColor = Color.FromArgb(59, 59, 59);
            ProgressColor = Color.FromArgb(96, 205, 255);
            ProgressEndColor = Color.FromArgb(38, 185, 243);
            BackColor = Color.FromArgb(43, 43, 43);
            Height = 8;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle trackBounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            int radius = Math.Max(1, Height / 2);
            using (GraphicsPath track = FluentDrawing.RoundedRectangle(trackBounds, radius))
            using (SolidBrush trackBrush = new SolidBrush(TrackColor))
            {
                e.Graphics.FillPath(trackBrush, track);
            }

            if (currentValue <= 0)
            {
                return;
            }

            int progressWidth = Math.Max(Height, (int)Math.Round(Width * currentValue / 100.0));
            progressWidth = Math.Min(Width, progressWidth);
            Rectangle progressBounds = new Rectangle(0, 0, Math.Max(1, progressWidth - 1), Math.Max(1, Height - 1));
            using (GraphicsPath progress = FluentDrawing.RoundedRectangle(progressBounds, radius))
            using (LinearGradientBrush progressBrush = new LinearGradientBrush(
                progressBounds, ProgressColor, ProgressEndColor, LinearGradientMode.Horizontal))
            {
                e.Graphics.FillPath(progressBrush, progress);
            }
        }
    }

    internal sealed class MigrationAnimationControl : Control
    {
        private readonly Timer animationTimer;
        private int currentValue;
        private float animationPhase;
        private bool active;

        internal Color TrackColor { get; set; }
        internal Color ProgressColor { get; set; }
        internal Color ProgressEndColor { get; set; }
        internal Image MascotImage { get; set; }

        internal int Value
        {
            get { return currentValue; }
            set
            {
                currentValue = Math.Max(0, Math.Min(100, value));
                Invalidate();
            }
        }

        internal int Minimum { get { return 0; } }
        internal int Maximum { get { return 100; } }

        internal bool Active
        {
            get { return active; }
            set
            {
                if (active == value)
                {
                    return;
                }
                active = value;
                if (active)
                {
                    animationTimer.Start();
                }
                else
                {
                    animationTimer.Stop();
                    animationPhase = 0F;
                }
                Invalidate();
            }
        }

        internal MigrationAnimationControl()
        {
            TrackColor = Color.FromArgb(66, 62, 54);
            ProgressColor = Color.FromArgb(255, 199, 39);
            ProgressEndColor = Color.FromArgb(255, 166, 25);
            animationTimer = new Timer();
            animationTimer.Interval = 40;
            animationTimer.Tick += delegate
            {
                animationPhase += 0.018F;
                if (animationPhase >= 1F)
                {
                    animationPhase -= 1F;
                }
                Invalidate();
            };
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animationTimer.Stop();
                animationTimer.Dispose();
                if (MascotImage != null)
                {
                    MascotImage.Dispose();
                    MascotImage = null;
                }
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.Clear(FluentDrawing.SurfaceBehind(this));

            int trackHeight = Math.Max(6, Height / 6);
            int trackTop = Height - trackHeight - 2;
            Rectangle trackBounds = new Rectangle(0, trackTop, Math.Max(1, Width - 1), trackHeight);
            int radius = Math.Max(1, trackHeight / 2);
            using (GraphicsPath track = FluentDrawing.RoundedRectangle(trackBounds, radius))
            using (SolidBrush trackBrush = new SolidBrush(TrackColor))
            {
                e.Graphics.FillPath(trackBrush, track);
            }

            int progressWidth = currentValue <= 0
                ? 0
                : Math.Min(Width, Math.Max(trackHeight, (int)Math.Round(Width * currentValue / 100.0)));
            if (progressWidth > 0)
            {
                Rectangle progressBounds = new Rectangle(0, trackTop,
                    Math.Max(1, progressWidth - 1), trackHeight);
                using (GraphicsPath progress = FluentDrawing.RoundedRectangle(progressBounds, radius))
                using (LinearGradientBrush progressBrush = new LinearGradientBrush(
                    progressBounds, ProgressColor, ProgressEndColor, LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillPath(progressBrush, progress);
                }
            }

            int mascotSize = Math.Max(22, Math.Min(34, Height - trackHeight + 3));
            int available = Math.Max(0, Width - mascotSize);
            int realFrontier = Math.Max(0, Math.Min(available,
                (int)Math.Round(available * currentValue / 100.0)));
            int travelFrontier = realFrontier;
            float easedPhase = animationPhase * animationPhase * (3F - 2F * animationPhase);
            int mascotX = active ? (int)Math.Round(travelFrontier * easedPhase) : realFrontier;
            int mascotY = Math.Max(0, trackTop - mascotSize + 4);
            if (active)
            {
                mascotY += (int)Math.Round(Math.Sin(animationPhase * Math.PI * 4.0) * 1.5);
            }

            if (MascotImage != null)
            {
                e.Graphics.DrawImage(MascotImage,
                    new Rectangle(mascotX, mascotY, mascotSize, mascotSize));
            }
            else
            {
                using (SolidBrush fallback = new SolidBrush(ProgressColor))
                {
                    e.Graphics.FillEllipse(fallback, mascotX, mascotY, mascotSize, mascotSize);
                }
            }

            DrawDestination(e.Graphics, Width - 24, Math.Max(1, trackTop - 17));
        }

        private void DrawDestination(Graphics graphics, int left, int top)
        {
            Rectangle box = new Rectangle(left, top + 5, 20, 15);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(90, 73, 27)))
            using (Pen outline = new Pen(ProgressColor, 1.4F))
            {
                outline.LineJoin = LineJoin.Round;
                graphics.FillRectangle(fill, box);
                graphics.DrawRectangle(outline, box);
                graphics.DrawLine(outline, box.Left, box.Top + 5, box.Right, box.Top + 5);
                graphics.DrawLine(outline, box.Left + box.Width / 2, box.Top + 5,
                    box.Left + box.Width / 2, box.Bottom);
                graphics.DrawLine(outline, box.Left, box.Top + 5, box.Left + 5, box.Top);
                graphics.DrawLine(outline, box.Right, box.Top + 5, box.Right - 5, box.Top);
            }
        }
    }
}
