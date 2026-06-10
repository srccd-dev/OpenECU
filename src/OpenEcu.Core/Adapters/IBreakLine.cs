namespace OpenEcu.Core.Adapters;

/// <summary>Something whose break (line-low) condition can be toggled — used for 5-baud init.</summary>
public interface IBreakLine
{
    void SetBreak(bool on);
}
