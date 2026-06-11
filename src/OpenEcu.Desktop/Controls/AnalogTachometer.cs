using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenEcu.Desktop.Controls;

/// <summary>Analog tachometer: 240° sweep, ticks/labels per 1000 rpm, redline zone, needle, digital rpm.</summary>
public sealed class AnalogTachometer : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<AnalogTachometer, double>(nameof(Value));
    public static readonly StyledProperty<double> MaxRpmProperty =
        AvaloniaProperty.Register<AnalogTachometer, double>(nameof(MaxRpm), 11000);
    public static readonly StyledProperty<double> RedlineRpmProperty =
        AvaloniaProperty.Register<AnalogTachometer, double>(nameof(RedlineRpm), 9500);
    public static readonly StyledProperty<IBrush> AccentProperty =
        AvaloniaProperty.Register<AnalogTachometer, IBrush>(nameof(Accent), Brushes.Teal);
    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<AnalogTachometer, IBrush>(nameof(Foreground), Brushes.White);

    static AnalogTachometer() =>
        AffectsRender<AnalogTachometer>(ValueProperty, MaxRpmProperty, RedlineRpmProperty, AccentProperty, ForegroundProperty);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double MaxRpm { get => GetValue(MaxRpmProperty); set => SetValue(MaxRpmProperty, value); }
    public double RedlineRpm { get => GetValue(RedlineRpmProperty); set => SetValue(RedlineRpmProperty, value); }
    public IBrush Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private const double A0 = 210, A1 = -30;            // sweep, degrees
    private static readonly Color Red = Color.FromRgb(0xE2, 0x4B, 0x4A);
    private static readonly Color Track = Color.FromArgb(60, 128, 128, 128);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 40 || h < 40) return;
        double radius = Math.Min(w, h) / 2 - 18;
        var c = new Point(w / 2, h / 2);
        double max = MaxRpm <= 0 ? 1 : MaxRpm;
        double redFrac = Math.Clamp(RedlineRpm / max, 0, 1);
        double valFrac = Math.Clamp(Value / max, 0, 1);

        var trackPen = new Pen(new SolidColorBrush(Track), 12) { LineCap = PenLineCap.Round };
        var redPen = new Pen(new SolidColorBrush(Red), 12) { LineCap = PenLineCap.Round };
        var valPen = new Pen(Accent, 12) { LineCap = PenLineCap.Round };

        ctx.DrawGeometry(null, trackPen, Arc(c, radius, 0, 1));
        ctx.DrawGeometry(null, redPen, Arc(c, radius, redFrac, 1));
        if (valFrac > 0)
            ctx.DrawGeometry(null, valPen, Arc(c, radius, 0, Math.Min(valFrac, redFrac)));
        if (valFrac > redFrac)
            ctx.DrawGeometry(null, redPen, Arc(c, radius, redFrac, valFrac));

        int thousands = (int)Math.Round(max / 1000);
        var tickGray = new SolidColorBrush(Color.FromArgb(200, 150, 150, 150));
        for (int t = 0; t <= thousands; t++)
        {
            double f = t / (double)thousands;
            bool red = f >= redFrac - 1e-6;
            var pen = new Pen(red ? new SolidColorBrush(Red) : tickGray, 2);
            ctx.DrawLine(pen, PointAt(c, radius - 14, f), PointAt(c, radius - 2, f));
            DrawText(ctx, t.ToString(), PointAt(c, radius - 30, f), 13, red ? new SolidColorBrush(Red) : tickGray);
        }

        var needle = new Pen(Foreground, 4) { LineCap = PenLineCap.Round };
        ctx.DrawLine(needle, c, PointAt(c, radius - 20, valFrac));
        ctx.DrawEllipse(Foreground, null, c, 6, 6);

        DrawText(ctx, ((int)Math.Round(Value)).ToString(), new Point(c.X, c.Y + radius * 0.42), radius * 0.30,
            Foreground, center: true);
        DrawText(ctx, "rpm", new Point(c.X, c.Y + radius * 0.66), 12,
            new SolidColorBrush(Color.FromArgb(200, 130, 140, 155)), center: true);
    }

    private static Point PointAt(Point c, double r, double frac)
    {
        double deg = A0 - (A0 - A1) * frac;
        double rad = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y - r * Math.Sin(rad));
    }

    private static Geometry Arc(Point c, double r, double f0, double f1)
    {
        var start = PointAt(c, r, f0);
        var end = PointAt(c, r, f1);
        bool large = (f1 - f0) * (A0 - A1) > 180;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(start, false);
            g.ArcTo(end, new Size(r, r), 0, large, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        return geo;
    }

    private static void DrawText(DrawingContext ctx, string text, Point at, double size, IBrush brush, bool center = false)
    {
        if (size < 6) return;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        var p = center ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2);
        ctx.DrawText(ft, p);
    }
}
