namespace OpenEcu.App.Model;

/// <summary>Tachometer range. Configurable per model so the redline/sweep can adjust.</summary>
public sealed record TachConfig(double MaxRpm, double RedlineRpm)
{
    /// <summary>Default for the Triumph Speed Triple 955i.</summary>
    public static TachConfig Default { get; } = new(MaxRpm: 11000, RedlineRpm: 9500);
}
