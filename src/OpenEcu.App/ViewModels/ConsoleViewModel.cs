using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OpenEcu.App.ViewModels;

/// <summary>Raw protocol console: timestamped Tx/Rx hex lines, with pause and clear.</summary>
public sealed partial class ConsoleViewModel : ObservableObject
{
    private const int MaxLines = 500;

    public ObservableCollection<string> Lines { get; } = new();

    [ObservableProperty] private bool _paused;

    public void OnTx(byte[] data) => Append("TX", data);
    public void OnRx(byte[] data) => Append("RX", data);

    private void Append(string direction, byte[] data)
    {
        if (Paused) return;
        Lines.Add($"{DateTime.Now:HH:mm:ss.fff}  {direction}  {Convert.ToHexString(data)}");
        while (Lines.Count > MaxLines) Lines.RemoveAt(0);
    }

    [RelayCommand]
    private void Clear() => Lines.Clear();
}
