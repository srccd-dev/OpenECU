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

    /// <summary>True once this PID has returned the same bytes for several reads in a row.</summary>
    public bool Repeated { get; private set; }

    /// <summary>Apply a fresh reading from the ECU.</summary>
    public void Update(PidReading reading)
    {
        bool same = Raw.AsSpan().SequenceEqual(reading.Raw); // compare to the previous reading before reassigning
        _unchanged = same ? _unchanged + 1 : 0;
        Repeated = _unchanged >= StaticThreshold;

        Raw = reading.Raw;
        Value = reading.Value;
        IsStale = reading.Value is null;
    }
}
