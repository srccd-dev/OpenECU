# OpenECU Diagnostics + Console + Settings — Implementation Plan (Plan 10)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the v1 app UI: a **Diagnostics** view (full PID table, with static-value flagging), a **Console** view (raw Tx/Rx hex log), a **view switcher**, and the **theme (light/dark) + accent** picker that persists — finishing the Dashboard/Diagnostics/Console scope.

**Architecture:** Testable additions to `OpenEcu.App` (static-PID detection on `MetricViewModel`, `DiagnosticsViewModel`, `ConsoleViewModel`, `AccentPalette`, theme/accent on `MainViewModel`); Avalonia views + a `TabControl` switcher + settings UI + theme/accent application in `OpenEcu.Desktop`. The Console binds to the existing `LoggingTransport` Tx/Rx events.

**Tech Stack:** .NET 8, Avalonia 11.0.10 (+ Avalonia.Controls.DataGrid), `CommunityToolkit.Mvvm`, xUnit, **AwesomeAssertions**. Builds on plans 1–9.

**Spec:** `docs/superpowers/specs/2026-06-10-openecu-ui-design.md` (§6 Diagnostics/Console/Settings, §3 theme/accent). **Field-driven refinement:** live testing on a running engine showed this ECU returns *static placeholder* values for PID 06/07 (fuel trims) — so the Diagnostics view flags PIDs whose value hasn't changed across many reads, so users aren't misled. **Deferred:** Racing Dashboard → plan 11.

**Avalonia note:** tasks 6–8 are verified by `dotnet build src/OpenEcu.Desktop`. Resolve any 11.0.10 API/XAML mismatch by reading the build error and fixing the API surface only (not behavior).

**Prerequisite:** Plans 1–9 on `main` (`MetricViewModel`, `LiveDataService`, `LoggingTransport`, `MainViewModel`, `AppSettings`, `DashboardView`, `RadialGauge`).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.App/ViewModels/MetricViewModel.cs` | **Modify:** add `IsStatic` detection |
| `src/OpenEcu.App/Model/AccentPalette.cs` | Accent name → RGB |
| `src/OpenEcu.App/ViewModels/MainViewModel.cs` | **Modify:** theme/accent + persistence |
| `src/OpenEcu.App/ViewModels/DiagnosticsViewModel.cs` | Full PID table + DTCs |
| `src/OpenEcu.App/ViewModels/ConsoleViewModel.cs` | Raw Tx/Rx log + pause/clear |
| `src/OpenEcu.Desktop/OpenEcu.Desktop.csproj` | **Modify:** add DataGrid package |
| `src/OpenEcu.Desktop/App.axaml` | **Modify:** include DataGrid theme |
| `src/OpenEcu.Desktop/Views/DiagnosticsView.axaml(.cs)` | PID DataGrid + DTC panel |
| `src/OpenEcu.Desktop/Views/ConsoleView.axaml(.cs)` | Log list + pause/clear |
| `src/OpenEcu.Desktop/Views/MainWindow.axaml(.cs)` | **Modify:** view switcher + settings UI + wiring |
| `tests/OpenEcu.App.Tests/*` | Tests for each testable unit |

---

### Task 1: Static-value flagging on MetricViewModel

**Files:**
- Modify: `src/OpenEcu.App/ViewModels/MetricViewModel.cs`
- Modify: `tests/OpenEcu.App.Tests/MetricViewModelTests.cs`

- [ ] **Step 1: Write the failing test (append to MetricViewModelTests)**

Add inside the `MetricViewModelTests` class, before the closing brace:
```csharp
    [Fact]
    public void Repeated_identical_readings_flag_static()
    {
        var vm = ForRpm();
        var reading = new OpenEcu.Core.Obd.PidReading(0x0C, "Engine RPM", 1000, "rpm", new byte[] { 0x0F, 0xA0 });

        for (int i = 0; i < 8; i++) vm.Update(reading);

        vm.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void A_changed_reading_clears_static()
    {
        var vm = ForRpm();
        var a = new OpenEcu.Core.Obd.PidReading(0x0C, "Engine RPM", 1000, "rpm", new byte[] { 0x0F, 0xA0 });
        for (int i = 0; i < 8; i++) vm.Update(a);
        vm.IsStatic.Should().BeTrue();

        vm.Update(new OpenEcu.Core.Obd.PidReading(0x0C, "Engine RPM", 1200, "rpm", new byte[] { 0x12, 0xC0 }));

        vm.IsStatic.Should().BeFalse();
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter MetricViewModelTests`
Expected: FAIL — `IsStatic` does not exist.

- [ ] **Step 3: Add the detection**

In `src/OpenEcu.App/ViewModels/MetricViewModel.cs`, add an `[ObservableProperty] private bool _isStatic;` field next to `_isStale`, add a private counter, and update `Update`. Replace the existing `Update` method with:
```csharp
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
```
And add the observable property (next to `_isStale`):
```csharp
    [ObservableProperty]
    private bool _isStatic;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter MetricViewModelTests`
Expected: PASS (5 passed — 3 prior + 2 new).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/ViewModels/MetricViewModel.cs tests/OpenEcu.App.Tests/MetricViewModelTests.cs
git commit -m "feat: flag PIDs that return static (unchanging) values"
```

---

### Task 2: AccentPalette

**Files:**
- Create: `src/OpenEcu.App/Model/AccentPalette.cs`
- Test: `tests/OpenEcu.App.Tests/AccentPaletteTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/AccentPaletteTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Model;
using Xunit;

namespace OpenEcu.App.Tests;

public class AccentPaletteTests
{
    [Fact]
    public void Known_accents_map_to_rgb()
    {
        AccentPalette.Rgb("teal").Should().Be((29, 158, 117));
        AccentPalette.Rgb("red").Should().Be((226, 75, 74));
    }

    [Fact]
    public void Unknown_accent_falls_back_to_teal()
    {
        AccentPalette.Rgb("chartreuse").Should().Be(AccentPalette.Rgb("teal"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter AccentPaletteTests`
Expected: FAIL — `AccentPalette` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/Model/AccentPalette.cs`:
```csharp
namespace OpenEcu.App.Model;

/// <summary>Maps an accent name to an RGB triple. UI-framework-agnostic (no Avalonia types).</summary>
public static class AccentPalette
{
    public static (byte R, byte G, byte B) Rgb(string accent) => accent switch
    {
        "white" => (245, 245, 245),
        "teal" => (29, 158, 117),
        "blue" => (55, 138, 221),
        "green" => (99, 153, 34),
        "yellow" => (234, 179, 8),
        "red" => (226, 75, 74),
        "black" => (40, 40, 40),
        _ => (29, 158, 117),
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter AccentPaletteTests`
Expected: PASS (2 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/Model/AccentPalette.cs tests/OpenEcu.App.Tests/AccentPaletteTests.cs
git commit -m "feat: AccentPalette (accent name -> rgb)"
```

---

### Task 3: Theme + accent on MainViewModel (persisted)

**Files:**
- Modify: `src/OpenEcu.App/ViewModels/MainViewModel.cs`
- Test: `tests/OpenEcu.App.Tests/MainViewModelSettingsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/MainViewModelSettingsTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.App.Tests;

public class MainViewModelSettingsTests
{
    private sealed class NullFactory : IConnectionFactory
    {
        public LiveConnection Create(string portName) =>
            new(new LiveDataService(new FakeObdSession()), new LoggingTransport(new SimulatedTransport()));
    }

    [Fact]
    public void Defaults_load_light_and_teal()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oe-{Guid.NewGuid():N}.json");
        var vm = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
        vm.DarkMode.Should().BeFalse();
        vm.Accent.Should().Be("teal");
    }

    [Fact]
    public void Changing_theme_and_accent_persists_and_reloads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oe-{Guid.NewGuid():N}.json");
        try
        {
            var vm = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            vm.DarkMode = true;
            vm.Accent = "red";

            var reloaded = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            reloaded.DarkMode.Should().BeTrue();
            reloaded.Accent.Should().Be("red");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter MainViewModelSettingsTests`
Expected: FAIL — `MainViewModel` has no settings-path constructor / `DarkMode` / `Accent`.

- [ ] **Step 3: Add settings to MainViewModel**

In `src/OpenEcu.App/ViewModels/MainViewModel.cs`: add `using OpenEcu.App.Model;`, two fields, a settings-aware constructor, the observable properties, and the persistence hooks.

Add fields (next to the other private fields):
```csharp
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
```

Replace the existing constructor with this (adds an optional `settingsPath`):
```csharp
    public MainViewModel(IConnectionFactory factory, Func<IReadOnlyList<string>>? portProvider = null, string? settingsPath = null)
    {
        _factory = factory;
        _portProvider = portProvider ?? (() => SerialPortEnumerator.GetPortNames());
        _settingsPath = settingsPath ?? AppSettings.DefaultPath;
        _settings = AppSettings.Load(_settingsPath);
        _darkMode = _settings.DarkMode;
        _accent = _settings.Accent;
        RefreshPorts();
    }
```

Add the properties + persistence (next to the other `[ObservableProperty]` fields):
```csharp
    [ObservableProperty] private bool _darkMode;
    [ObservableProperty] private string _accent = "teal";

    public IReadOnlyList<string> Accents => AppSettings.Accents;

    partial void OnDarkModeChanged(bool value) { _settings.DarkMode = value; _settings.Save(_settingsPath); }
    partial void OnAccentChanged(string value) { _settings.Accent = value; _settings.Save(_settingsPath); }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter MainViewModelSettingsTests`
Expected: PASS (2 passed). Confirm the existing `MainViewModelTests` still pass too: `dotnet test --filter MainViewModelTests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/ViewModels/MainViewModel.cs tests/OpenEcu.App.Tests/MainViewModelSettingsTests.cs
git commit -m "feat: persisted theme + accent on MainViewModel"
```

---

### Task 4: DiagnosticsViewModel

**Files:**
- Create: `src/OpenEcu.App/ViewModels/DiagnosticsViewModel.cs`
- Test: `tests/OpenEcu.App.Tests/DiagnosticsViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/DiagnosticsViewModelTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using Xunit;

namespace OpenEcu.App.Tests;

public class DiagnosticsViewModelTests
{
    private static async Task<LiveDataService> Connected(params byte[] supported)
    {
        var ecu = new FakeObdSession();
        ecu.Supported.AddRange(supported);
        var svc = new LiveDataService(ecu);
        await svc.ConnectAsync();
        return svc;
    }

    [Fact]
    public async Task Exposes_all_metrics_and_dtcs()
    {
        var svc = await Connected(0x0C, 0x05, 0x11);
        var vm = new DiagnosticsViewModel(svc);

        vm.Metrics.Select(m => m.Pid).Should().Equal((byte)0x0C, (byte)0x05, (byte)0x11);
        vm.Dtcs.Should().BeSameAs(svc.Dtcs);
    }

    [Fact]
    public async Task Clear_codes_is_disabled_in_v1()
    {
        var svc = await Connected(0x0C);
        var vm = new DiagnosticsViewModel(svc);
        vm.CanClearCodes.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DiagnosticsViewModelTests`
Expected: FAIL — `DiagnosticsViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/ViewModels/DiagnosticsViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using OpenEcu.App.Services;

namespace OpenEcu.App.ViewModels;

/// <summary>The full live PID table + fault codes. Clear-codes (Mode 04) is a v2 stub.</summary>
public sealed class DiagnosticsViewModel
{
    private readonly LiveDataService _live;

    public DiagnosticsViewModel(LiveDataService live) => _live = live;

    public ObservableCollection<MetricViewModel> Metrics => _live.Metrics;
    public IReadOnlyList<string> Dtcs => _live.Dtcs;

    /// <summary>Mode 04 clear-DTCs is not implemented in v1 (read-only).</summary>
    public bool CanClearCodes => false;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter DiagnosticsViewModelTests`
Expected: PASS (2 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/ViewModels/DiagnosticsViewModel.cs tests/OpenEcu.App.Tests/DiagnosticsViewModelTests.cs
git commit -m "feat: DiagnosticsViewModel (full PID table + DTCs)"
```

---

### Task 5: ConsoleViewModel

**Files:**
- Create: `src/OpenEcu.App/ViewModels/ConsoleViewModel.cs`
- Test: `tests/OpenEcu.App.Tests/ConsoleViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/ConsoleViewModelTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.ViewModels;
using Xunit;

namespace OpenEcu.App.Tests;

public class ConsoleViewModelTests
{
    [Fact]
    public void Rx_and_tx_append_lines_with_direction_and_hex()
    {
        var vm = new ConsoleViewModel();
        vm.OnTx(new byte[] { 0x68, 0x6A });
        vm.OnRx(new byte[] { 0x48, 0x6B });

        vm.Lines.Should().HaveCount(2);
        vm.Lines[0].Should().Contain("TX").And.Contain("686A");
        vm.Lines[1].Should().Contain("RX").And.Contain("486B");
    }

    [Fact]
    public void Paused_stops_appending()
    {
        var vm = new ConsoleViewModel { Paused = true };
        vm.OnRx(new byte[] { 0x01 });
        vm.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Clear_empties_the_log()
    {
        var vm = new ConsoleViewModel();
        vm.OnRx(new byte[] { 0x01 });
        vm.ClearCommand.Execute(null);
        vm.Lines.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ConsoleViewModelTests`
Expected: FAIL — `ConsoleViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/ViewModels/ConsoleViewModel.cs`:
```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ConsoleViewModelTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS — plans 1–9 (101) + Task1 (2) + Task2 (2) + Task3 (2) + Task4 (2) + Task5 (3) = 112 passed, 1 skipped.

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.App/ViewModels/ConsoleViewModel.cs tests/OpenEcu.App.Tests/ConsoleViewModelTests.cs
git commit -m "feat: ConsoleViewModel (raw Tx/Rx log, pause, clear)"
```

---

### Task 6: Add the DataGrid package + theme

**Files:**
- Modify: `src/OpenEcu.Desktop/OpenEcu.Desktop.csproj`
- Modify: `src/OpenEcu.Desktop/App.axaml`

- [ ] **Step 1: Add the package**

```bash
dotnet add src/OpenEcu.Desktop package Avalonia.Controls.DataGrid --version 11.0.10
```

- [ ] **Step 2: Include the DataGrid theme in App.axaml**

In `src/OpenEcu.Desktop/App.axaml`, add the DataGrid styles inside `<Application.Styles>` (after `<FluentTheme />`):
```xml
    <StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml" />
```

- [ ] **Step 3: Build**

Run: `dotnet build src/OpenEcu.Desktop`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/OpenEcu.Desktop/OpenEcu.Desktop.csproj src/OpenEcu.Desktop/App.axaml
git commit -m "chore: add Avalonia DataGrid package + theme"
```

---

### Task 7: Diagnostics + Console views

**Files:**
- Create: `src/OpenEcu.Desktop/Views/DiagnosticsView.axaml(.cs)`, `Views/ConsoleView.axaml(.cs)`

- [ ] **Step 1: DiagnosticsView**

Create `src/OpenEcu.Desktop/Views/DiagnosticsView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:OpenEcu.App.ViewModels;assembly=OpenEcu.App"
             x:Class="OpenEcu.Desktop.Views.DiagnosticsView"
             x:DataType="vm:DiagnosticsViewModel">
  <DockPanel Margin="12">
    <Border DockPanel.Dock="Bottom" Margin="0,12,0,0" Padding="10" CornerRadius="8"
            Background="{DynamicResource SystemControlBackgroundListLowBrush}">
      <StackPanel>
        <TextBlock Text="Fault codes" FontSize="13" Opacity="0.7" />
        <ItemsControl ItemsSource="{Binding Dtcs}">
          <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="x:String"><TextBlock Text="{Binding}" /></DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
        <TextBlock Text="(no stored codes)" Opacity="0.5" IsVisible="{Binding !Dtcs.Count}" />
        <Button Content="Clear codes" IsEnabled="{Binding CanClearCodes}" Margin="0,8,0,0" />
      </StackPanel>
    </Border>

    <DataGrid ItemsSource="{Binding Metrics}" IsReadOnly="True" GridLinesVisibility="Horizontal"
              CanUserReorderColumns="False" CanUserResizeColumns="True">
      <DataGrid.Columns>
        <DataGridTextColumn Header="Sensor" Binding="{Binding Name}" Width="2*" />
        <DataGridTextColumn Header="Value" Binding="{Binding Display}" Width="*" />
        <DataGridTemplateColumn Header="" Width="80">
          <DataGridTemplateColumn.CellTemplate>
            <DataTemplate x:DataType="vm:MetricViewModel">
              <TextBlock Text="static" FontSize="11" Opacity="0.6" VerticalAlignment="Center"
                         IsVisible="{Binding IsStatic}" />
            </DataTemplate>
          </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>
      </DataGrid.Columns>
    </DataGrid>
  </DockPanel>
</UserControl>
```

Create `src/OpenEcu.Desktop/Views/DiagnosticsView.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenEcu.Desktop.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 2: ConsoleView**

Create `src/OpenEcu.Desktop/Views/ConsoleView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:OpenEcu.App.ViewModels;assembly=OpenEcu.App"
             x:Class="OpenEcu.Desktop.Views.ConsoleView"
             x:DataType="vm:ConsoleViewModel">
  <DockPanel Margin="12">
    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,8">
      <ToggleButton Content="Pause" IsChecked="{Binding Paused}" />
      <Button Content="Clear" Command="{Binding ClearCommand}" />
    </StackPanel>
    <Border CornerRadius="8" Background="{DynamicResource SystemControlBackgroundListLowBrush}">
      <ScrollViewer>
        <ItemsControl ItemsSource="{Binding Lines}" Margin="8">
          <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="x:String">
              <TextBlock Text="{Binding}" FontFamily="Cascadia Code,Consolas,monospace" FontSize="12" />
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>
    </Border>
  </DockPanel>
</UserControl>
```

Create `src/OpenEcu.Desktop/Views/ConsoleView.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenEcu.Desktop.Views;

public partial class ConsoleView : UserControl
{
    public ConsoleView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/OpenEcu.Desktop`
Expected: build succeeds. (Fix any 11.0.10 API/XAML mismatch by build error; do not change behavior.)

- [ ] **Step 4: Commit**

```bash
git add src/OpenEcu.Desktop/Views/DiagnosticsView.axaml src/OpenEcu.Desktop/Views/DiagnosticsView.axaml.cs src/OpenEcu.Desktop/Views/ConsoleView.axaml src/OpenEcu.Desktop/Views/ConsoleView.axaml.cs
git commit -m "feat: Diagnostics (PID table + static flag) and Console views"
```

---

### Task 8: View switcher + settings + wiring in MainWindow

**Files:**
- Modify: `src/OpenEcu.Desktop/Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`

- [ ] **Step 1: MainWindow XAML — tabs + settings bar**

Replace `src/OpenEcu.Desktop/Views/MainWindow.axaml` with:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:OpenEcu.App.ViewModels;assembly=OpenEcu.App"
        x:Class="OpenEcu.Desktop.Views.MainWindow"
        x:DataType="vm:MainViewModel"
        Width="960" Height="640" Title="OpenECU">
  <DockPanel>
    <Border DockPanel.Dock="Top" Padding="10" Background="{DynamicResource SystemControlBackgroundListLowBrush}">
      <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
        <ComboBox ItemsSource="{Binding AvailablePorts}" SelectedItem="{Binding SelectedPort}" MinWidth="120" />
        <Button Content="Refresh" Command="{Binding RefreshPorts_Command}" />
        <Button Content="Connect" Command="{Binding ConnectCommand}" />
        <Button Content="Disconnect" Command="{Binding DisconnectCommand}" />
        <TextBlock Text="{Binding Status}" VerticalAlignment="Center" Margin="12,0,0,0" />

        <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
          <ToggleButton Content="Dark" IsChecked="{Binding DarkMode}" />
          <ComboBox ItemsSource="{Binding Accents}" SelectedItem="{Binding Accent}" MinWidth="90" />
        </StackPanel>
      </StackPanel>
    </Border>

    <TabControl x:Name="Tabs">
      <TabItem Header="Dashboard"><ContentControl x:Name="DashboardHost" /></TabItem>
      <TabItem Header="Diagnostics"><ContentControl x:Name="DiagnosticsHost" /></TabItem>
      <TabItem Header="Console"><ContentControl x:Name="ConsoleHost" /></TabItem>
    </TabControl>
  </DockPanel>
</Window>
```

- [ ] **Step 2: MainWindow code-behind — wire views, console, theme/accent**

Replace `src/OpenEcu.Desktop/Views/MainWindow.axaml.cs` with:
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using OpenEcu.App.Model;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;

namespace OpenEcu.Desktop.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private ConsoleViewModel? _console;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => OnVmAttached(DataContext as MainViewModel);
        OnVmAttached(DataContext as MainViewModel);
    }

    private void OnVmAttached(MainViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmChanged;
        ApplyTheme();
        ApplyAccent();
        UpdateViews();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Live): UpdateViews(); break;
            case nameof(MainViewModel.DarkMode): ApplyTheme(); break;
            case nameof(MainViewModel.Accent): ApplyAccent(); break;
        }
    }

    private void ApplyTheme()
    {
        if (Application.Current is { } app && _vm is not null)
            app.RequestedThemeVariant = _vm.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void ApplyAccent()
    {
        if (Application.Current is not { } app || _vm is null) return;
        var (r, g, b) = AccentPalette.Rgb(_vm.Accent);
        app.Resources["AppAccentBrush"] = new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    private void UpdateViews()
    {
        var dash = this.FindControl<ContentControl>("DashboardHost");
        var diag = this.FindControl<ContentControl>("DiagnosticsHost");
        var con = this.FindControl<ContentControl>("ConsoleHost");

        if (_vm?.Live is LiveDataService live)
        {
            if (dash is not null) dash.Content = new DashboardView { DataContext = new DashboardViewModel(live) };
            if (diag is not null) diag.Content = new DiagnosticsView { DataContext = new DiagnosticsViewModel(live) };

            _console = new ConsoleViewModel();
            if (_vm.Log is { } log)
            {
                log.BytesWritten += _console.OnTx;
                log.BytesRead += _console.OnRx;
            }
            if (con is not null) con.Content = new ConsoleView { DataContext = _console };
        }
        else
        {
            if (dash is not null) dash.Content = NotConnected();
            if (diag is not null) diag.Content = NotConnected();
            if (con is not null) con.Content = NotConnected();
        }
    }

    private static TextBlock NotConnected() => new()
    {
        Text = "Not connected — pick a port and Connect.",
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        Opacity = 0.6,
    };
}
```

- [ ] **Step 3: Make the dashboard gauges use the app accent**

In `src/OpenEcu.Desktop/Views/DashboardView.axaml`, change the hero `RadialGauge` element to bind its accent (add the `Accent` attribute):
```xml
              <c:RadialGauge Value="{Binding Value, FallbackValue=0}"
                             Minimum="{Binding Minimum}" Maximum="{Binding Maximum}"
                             ValueText="{Binding Display}" Label="{Binding Name}"
                             Accent="{DynamicResource AppAccentBrush}" />
```

- [ ] **Step 4: Build + full suite**

Run: `dotnet build src/OpenEcu.Desktop`
Expected: build succeeds.

Run: `dotnet test`
Expected: 112 passed, 1 skipped (unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Desktop/Views/MainWindow.axaml src/OpenEcu.Desktop/Views/MainWindow.axaml.cs src/OpenEcu.Desktop/Views/DashboardView.axaml
git commit -m "feat: view switcher (Dashboard/Diagnostics/Console) + theme/accent settings"
```

---

### Task 9: Manual hardware verification (the human, on the bike)

```bash
dotnet run --project src/OpenEcu.Desktop
```

- [ ] Connect to COM8. The **Dashboard** tab shows live gauges + tiles (as before).
- [ ] Switch to **Diagnostics**: a table of every PID with live values; the fuel-trim PIDs (06/07) show a **"static"** marker once they've repeated; DTC panel shows `P1502`.
- [ ] Switch to **Console**: a live scrolling log of `TX …` / `RX …` hex frames; **Pause** freezes it, **Clear** empties it.
- [ ] Toggle **Dark** → the whole app switches to dark; change the **accent** dropdown → the hero gauges recolor. Close and relaunch → theme + accent are remembered.

---

## Self-Review

**Spec coverage:**
- Diagnostics view: full PID table + DTC panel + clear-codes stub (spec §6) → Tasks 4, 7 ✅
- Console view: raw Tx/Rx log + pause/clear, fed by `LoggingTransport` (spec §6) → Tasks 5, 7, 8 ✅
- View switcher (Dashboard/Diagnostics/Console) (spec §6) → Task 8 ✅
- Theme (light/dark) toggle + accent picker, persisted (spec §3) → Tasks 2, 3, 8 ✅
- Static-PID flagging (field-driven refinement) → Tasks 1, 7 ✅
- **Deferred:** Racing Dashboard → plan 11.

**Placeholder scan:** No TBD/TODO. The "Clear codes" button is an intentional disabled v1 stub (Mode 04 is v2), not a placeholder. Avalonia tasks carry complete files and are build-verified; Task 9 is human-only.

**Type consistency:** `MetricViewModel.IsStatic`; `AccentPalette.Rgb`; `MainViewModel` (`DarkMode`, `Accent`, `Accents`, settings-path ctor); `DiagnosticsViewModel` (`Metrics`, `Dtcs`, `CanClearCodes`); `ConsoleViewModel` (`Lines`, `Paused`, `OnTx`, `OnRx`, `ClearCommand`) are referenced consistently across tasks and XAML bindings. Reuses `LiveDataService`, `LoggingTransport` (`BytesWritten`/`BytesRead`), `MetricViewModel` (`Name`/`Display`/`IsStatic`), `AppSettings` from plans 1–9. Tests use `using AwesomeAssertions;`.
