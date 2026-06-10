using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenEcu.Desktop.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView() => AvaloniaXamlLoader.Load(this);
}
