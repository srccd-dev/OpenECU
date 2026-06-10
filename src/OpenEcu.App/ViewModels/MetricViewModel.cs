using CommunityToolkit.Mvvm.ComponentModel;
using OpenEcu.App.Model;
using OpenEcu.Core.Obd;

namespace OpenEcu.App.ViewModels;

/// <summary>One live OBD reading bound to a gauge or tile.</summary>
public sealed partial class MetricViewModel : ObservableObject
{
    public MetricViewModel(MetricDescriptor descriptor) => Descriptor = descriptor;

    public MetricDescriptor Descriptor { get; }

    public byte Pid => Descriptor.Pid;
    public string Name => Descriptor.Name;
    public string Unit => Descriptor.Unit;
    public double Minimum => Descriptor.Min;
    public double Maximum => Descriptor.Max;
    public string Accent => Descriptor.Accent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private double? _value;

    [ObservableProperty]
    private bool _isStale;

    [ObservableProperty]
    private bool _isStatic;

    [ObservableProperty]
    private byte[] _raw = Array.Empty<byte>();

    public string Display => Value is null ? "—" : $"{Value:0.##} {Unit}".Trim();

    private int _unchanged;
    private const int StaticThreshold = 5; // identical reads in a row before flagging static

    /// <summary>Apply a fresh reading from the ECU.</summary>
    public void Update(PidReading reading)
    {
        bool same = _raw.AsSpan().SequenceEqual(reading.Raw);
        _unchanged = same ? _unchanged + 1 : 0;
        IsStatic = _unchanged >= StaticThreshold;

        Raw = reading.Raw;
        Value = reading.Value;
        IsStale = reading.Value is null;
    }
}
