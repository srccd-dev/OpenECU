using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenEcu.Desktop.Views;

public partial class ConsoleView : UserControl
{
    public ConsoleView() => AvaloniaXamlLoader.Load(this);
}
