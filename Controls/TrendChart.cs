using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace XTSPrimeMoverProject.Controls
{
    /// <summary>
    /// Lightweight time-series / SPC chart drawn in OnRender (no per-point visuals, frozen resources).
    /// Optional control limits (UCL/LCL, dashed), spec limits (LSL/USL, red), centre line and
    /// rule-violation markers make it an individuals control chart.
    /// </summary>
    public sealed class TrendChart : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty = Register(nameof(Values), typeof(IReadOnlyList<double>), null);
        public static readonly DependencyProperty MarkersProperty = Register(nameof(Markers), typeof(IReadOnlyList<int>), null);
        public static readonly DependencyProperty StrokeProperty = Register(nameof(Stroke), typeof(Brush), Brushes.DeepSkyBlue);
        public static readonly DependencyProperty FillAreaProperty = Register(nameof(FillArea), typeof(bool), true);
        public static readonly DependencyProperty CenterLineProperty = Register(nameof(CenterLine), typeof(double), double.NaN);
        public static readonly DependencyProperty UpperControlLimitProperty = Register(nameof(UpperControlLimit), typeof(double), double.NaN);
        public static readonly DependencyProperty LowerControlLimitProperty = Register(nameof(LowerControlLimit), typeof(double), double.NaN);
        public static readonly DependencyProperty UpperSpecLimitProperty = Register(nameof(UpperSpecLimit), typeof(double), double.NaN);
        public static readonly DependencyProperty LowerSpecLimitProperty = Register(nameof(LowerSpecLimit), typeof(double), double.NaN);
        public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), typeof(double), double.NaN);
        public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), typeof(double), double.NaN);
        public static readonly DependencyProperty ShowLastValueProperty = Register(nameof(ShowLastValue), typeof(bool), true);
        public static readonly DependencyProperty ValueFormatProperty = Register(nameof(ValueFormat), typeof(string), "0.##");

        private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1));
        private static readonly Pen ControlPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), 1) { DashStyle = DashStyles.Dash });
        private static readonly Pen SpecPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)), 1.2));
        private static readonly Pen CenterPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(120, 0x36, 0xD3, 0x99)), 1) { DashStyle = DashStyles.Dot });
        private static readonly Brush MarkerBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x6D)));
        private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF5)));
        private static readonly Typeface Typeface = new("Segoe UI");

        public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
        public IReadOnlyList<int>? Markers { get => (IReadOnlyList<int>?)GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
        public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        public bool FillArea { get => (bool)GetValue(FillAreaProperty); set => SetValue(FillAreaProperty, value); }
        public double CenterLine { get => (double)GetValue(CenterLineProperty); set => SetValue(CenterLineProperty, value); }
        public double UpperControlLimit { get => (double)GetValue(UpperControlLimitProperty); set => SetValue(UpperControlLimitProperty, value); }
        public double LowerControlLimit { get => (double)GetValue(LowerControlLimitProperty); set => SetValue(LowerControlLimitProperty, value); }
        public double UpperSpecLimit { get => (double)GetValue(UpperSpecLimitProperty); set => SetValue(UpperSpecLimitProperty, value); }
        public double LowerSpecLimit { get => (double)GetValue(LowerSpecLimitProperty); set => SetValue(LowerSpecLimitProperty, value); }
        public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public bool ShowLastValue { get => (bool)GetValue(ShowLastValueProperty); set => SetValue(ShowLastValueProperty, value); }
        public string ValueFormat { get => (string)GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 4 || h < 4)
            {
                return;
            }

            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            for (int i = 1; i < 4; i++)
            {
                double y = Math.Round(h * i / 4) + 0.5;
                dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
            }

            var values = Values;
            if (values == null || values.Count < 2)
            {
                return;
            }

            double min = double.IsNaN(Minimum) ? double.MaxValue : Minimum;
            double max = double.IsNaN(Maximum) ? double.MinValue : Maximum;
            if (double.IsNaN(Minimum) || double.IsNaN(Maximum))
            {
                foreach (double v in values)
                {
                    if (double.IsNaN(Minimum)) min = Math.Min(min, v);
                    if (double.IsNaN(Maximum)) max = Math.Max(max, v);
                }

                foreach (double limit in new[] { UpperControlLimit, LowerControlLimit, UpperSpecLimit, LowerSpecLimit, CenterLine })
                {
                    if (double.IsNaN(limit)) continue;
                    if (double.IsNaN(Minimum)) min = Math.Min(min, limit);
                    if (double.IsNaN(Maximum)) max = Math.Max(max, limit);
                }

                double pad = Math.Max(1e-9, (max - min) * 0.08);
                if (double.IsNaN(Minimum)) min -= pad;
                if (double.IsNaN(Maximum)) max += pad;
            }

            double span = Math.Max(1e-9, max - min);
            double Y(double v) => h - (v - min) / span * h;
            double X(int i) => values.Count == 1 ? w / 2 : i * w / (values.Count - 1);

            void HLine(Pen pen, double v)
            {
                if (double.IsNaN(v)) return;
                double y = Y(v);
                dc.DrawLine(pen, new Point(0, y), new Point(w, y));
            }

            HLine(CenterPen, CenterLine);
            HLine(ControlPen, UpperControlLimit);
            HLine(ControlPen, LowerControlLimit);
            HLine(SpecPen, UpperSpecLimit);
            HLine(SpecPen, LowerSpecLimit);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(X(0), Y(values[0])), false, false);
                for (int i = 1; i < values.Count; i++)
                {
                    ctx.LineTo(new Point(X(i), Y(values[i])), true, true);
                }
            }

            geometry.Freeze();

            if (FillArea)
            {
                var area = new StreamGeometry();
                using (var ctx = area.Open())
                {
                    ctx.BeginFigure(new Point(X(0), h), true, true);
                    for (int i = 0; i < values.Count; i++)
                    {
                        ctx.LineTo(new Point(X(i), Y(values[i])), false, false);
                    }

                    ctx.LineTo(new Point(X(values.Count - 1), h), false, false);
                }

                area.Freeze();
                var color = (Stroke as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
                var fill = new LinearGradientBrush(Color.FromArgb(70, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
                fill.Freeze();
                dc.DrawGeometry(fill, null, area);
            }

            var pen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);

            if (Markers != null)
            {
                foreach (int index in Markers)
                {
                    if (index >= 0 && index < values.Count)
                    {
                        dc.DrawEllipse(MarkerBrush, null, new Point(X(index), Y(values[index])), 3.2, 3.2);
                    }
                }
            }

            if (ShowLastValue)
            {
                double last = values[^1];
                dc.DrawEllipse(Stroke, null, new Point(X(values.Count - 1), Y(last)), 2.6, 2.6);
                var text = new FormattedText(last.ToString(ValueFormat, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface, 10, LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point(Math.Max(0, w - text.Width - 2), 1));
            }
        }

        private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
            DependencyProperty.Register(name, type, typeof(TrendChart), new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
