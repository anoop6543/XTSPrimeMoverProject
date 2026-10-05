using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace XTSPrimeMoverProject.Controls
{
    /// <summary>Circular KPI gauge (0..1) with centred value and caption, colour-graded by thresholds.</summary>
    public sealed class RingGauge : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = Register(nameof(Value), typeof(double), 0.0);
        public static readonly DependencyProperty CaptionProperty = Register(nameof(Caption), typeof(string), string.Empty);
        public static readonly DependencyProperty GoodThresholdProperty = Register(nameof(GoodThreshold), typeof(double), 0.75);
        public static readonly DependencyProperty WarnThresholdProperty = Register(nameof(WarnThreshold), typeof(double), 0.5);
        public static readonly DependencyProperty ThicknessProperty = Register(nameof(Thickness), typeof(double), 7.0);

        private static readonly Brush TrackBrush = Frozen(Color.FromArgb(45, 255, 255, 255));
        private static readonly Brush GoodBrush = Frozen(Color.FromRgb(0x36, 0xD3, 0x99));
        private static readonly Brush WarnBrush = Frozen(Color.FromRgb(0xFB, 0xBF, 0x24));
        private static readonly Brush BadBrush = Frozen(Color.FromRgb(0xF8, 0x71, 0x71));
        private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xE8, 0xEE, 0xF5));
        private static readonly Brush CaptionBrush = Frozen(Color.FromRgb(0x8E, 0xA0, 0xB4));
        private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface Regular = new("Segoe UI");

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
        public double GoodThreshold { get => (double)GetValue(GoodThresholdProperty); set => SetValue(GoodThresholdProperty, value); }
        public double WarnThreshold { get => (double)GetValue(WarnThresholdProperty); set => SetValue(WarnThresholdProperty, value); }
        public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size < 10)
            {
                return;
            }

            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double radius = size / 2 - Thickness / 2 - 1;
            double value = double.IsNaN(Value) ? 0 : Math.Clamp(Value, 0, 1);
            var brush = value >= GoodThreshold ? GoodBrush : value >= WarnThreshold ? WarnBrush : BadBrush;

            var trackPen = new Pen(TrackBrush, Thickness);
            trackPen.Freeze();
            dc.DrawEllipse(null, trackPen, center, radius, radius);

            if (value > 0.001)
            {
                double start = -Math.PI / 2;
                double end = start + value * 2 * Math.PI * 0.9999;
                var startPoint = new Point(center.X + radius * Math.Cos(start), center.Y + radius * Math.Sin(start));
                var endPoint = new Point(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end));
                var arc = new StreamGeometry();
                using (var ctx = arc.Open())
                {
                    ctx.BeginFigure(startPoint, false, false);
                    ctx.ArcTo(endPoint, new Size(radius, radius), 0, value > 0.5, SweepDirection.Clockwise, true, false);
                }

                arc.Freeze();
                var pen = new Pen(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                pen.Freeze();
                dc.DrawGeometry(null, pen, arc);
            }

            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var valueText = new FormattedText($"{value * 100:0}%", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, size * 0.22, TextBrush, dpi);
            dc.DrawText(valueText, new Point(center.X - valueText.Width / 2, center.Y - valueText.Height / 2 - (string.IsNullOrEmpty(Caption) ? 0 : size * 0.06)));
            if (!string.IsNullOrEmpty(Caption))
            {
                var captionText = new FormattedText(Caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Regular, Math.Max(8, size * 0.11), CaptionBrush, dpi);
                dc.DrawText(captionText, new Point(center.X - captionText.Width / 2, center.Y + size * 0.1));
            }
        }

        private static DependencyProperty Register(string name, Type type, object defaultValue) =>
            DependencyProperty.Register(name, type, typeof(RingGauge), new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
