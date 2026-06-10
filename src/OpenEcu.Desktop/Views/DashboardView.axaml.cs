using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenEcu.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => AvaloniaXamlLoader.Load(this);
}
