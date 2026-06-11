using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenEcu.Desktop.Controls;

/// <summary>A 180° radial gauge: track arc + value arc + centered value and label text.</summary>
public sealed class RadialGauge : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<RadialGauge, double>(nameof(Value));
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<RadialGauge, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<RadialGauge, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<RadialGauge, string?>(nameof(Label));
    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<RadialGauge, string?>(nameof(ValueText));
    public static readonly StyledProperty<IBrush> AccentProperty =
        AvaloniaProperty.Register<RadialGauge, IBrush>(nameof(Accent), Brushes.Teal);

    static RadialGauge()
    {
        AffectsRender<RadialGauge>(ValueProperty, MinimumProperty, MaximumProperty,
            LabelProperty, ValueTextProperty, AccentProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? ValueText { get => GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }
    public IBrush Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 10 || h < 10) return;

        double thickness = System.Math.Max(6, w * 0.07);
        double radius = System.Math.Min(w / 2, h) - thickness;
        var center = new Point(w / 2, h - thickness);
        var trackPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), thickness)
            { LineCap = PenLineCap.Round };
        var valuePen = new Pen(Accent, thickness) { LineCap = PenLineCap.Round };

        context.DrawGeometry(null, trackPen, Arc(center, radius, 1.0));

        double range = Maximum - Minimum;
        double frac = range <= 0 ? 0 : System.Math.Clamp((Value - Minimum) / range, 0, 1);
        if (frac > 0)
            context.DrawGeometry(null, valuePen, Arc(center, radius, frac));

        var fg = new SolidColorBrush(Color.FromArgb(220, 130, 130, 130));
        DrawCenteredText(context, ValueText ?? "", center.X, center.Y - radius * 0.45, radius * 0.42, Accent);
        DrawCenteredText(context, Label ?? "", center.X, center.Y - radius * 0.12, radius * 0.20, fg);
    }

    // 180° arc from left (180°) sweeping clockwise by `frac` of a semicircle.
    private static Geometry Arc(Point center, double r, double frac)
    {
        double startAngle = System.Math.PI;                 // left
        double endAngle = System.Math.PI - System.Math.PI * frac;
        var start = new Point(center.X + r * System.Math.Cos(startAngle), center.Y - r * System.Math.Sin(startAngle));
        var end = new Point(center.X + r * System.Math.Cos(endAngle), center.Y - r * System.Math.Sin(endAngle));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(r, r), 0, isLargeArc: false, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        return geo;
    }

    private static void DrawCenteredText(DrawingContext ctx, string text, double cx, double cy, double size, IBrush brush)
    {
        if (string.IsNullOrEmpty(text) || size < 6) return;
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, size, brush);
        ctx.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }
}
