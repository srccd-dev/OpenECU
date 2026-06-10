# OpenECU Desktop App + Standard Dashboard — Implementation Plan (Plan 9)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A runnable Avalonia desktop app that connects to the ECU and shows a live **Standard Dashboard** (hero `RadialGauge`s + tiles + a fault-code strip), with a connection bar and a light/dark theme + accent picker that persists.

**Architecture:** The testable MVVM/application layer (`AppSettings`, `ConnectionFactory`, `MainViewModel`, `DashboardViewModel`) lives in the existing `OpenEcu.App` class library (TDD, no Avalonia). A new `OpenEcu.Desktop` Avalonia executable hosts the views, the custom `RadialGauge` control, theming, and app bootstrap. The poll loop is started from the UI thread so `LiveDataService` updates marshal back automatically (its blocking serial I/O already runs on the thread pool inside `SystemSerialPort`).

**Tech Stack:** .NET 8, Avalonia 11.0.10 (+ Avalonia.Desktop, Avalonia.Themes.Fluent), `CommunityToolkit.Mvvm`, xUnit, **AwesomeAssertions** (`using AwesomeAssertions;`). Builds on plans 1–8.

**Spec:** `docs/superpowers/specs/2026-06-10-openecu-ui-design.md` (§4 connection/data flow, §6 dashboard + settings, §8 polling, §3 theme/accent). **Plan scope:** the runnable app + Standard Dashboard. **Deferred:** Diagnostics + Console views → plan 10; optional Racing Dashboard → plan 11.

**Avalonia note for the implementer:** Avalonia is XAML-heavy and version-sensitive. Tasks 5–8 are verified by `dotnet build` (the GUI can't be launched headless here; the human launches it). If a specific Avalonia 11.0.10 API name differs (rare), resolve it by building and reading the compiler error — do **not** change behavior, just the API call. Report if genuinely stuck.

**Prerequisite:** Plans 1–8 on `main` (`LiveDataService`, `MetricViewModel`, `DashboardLayout`, `LoggingTransport`, `KLineObdSession`, `SerialPortTransport`, `SystemSerialPort`, `SerialPortEnumerator`).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.App/Model/AppSettings.cs` | Theme + accent settings + JSON load/save |
| `src/OpenEcu.App/Services/ConnectionFactory.cs` | Builds the live data stack for a port (`IConnectionFactory`, `LiveConnection`) |
| `src/OpenEcu.App/ViewModels/MainViewModel.cs` | Ports, connect/disconnect, state, settings |
| `src/OpenEcu.App/ViewModels/DashboardViewModel.cs` | Hero + tile metric VMs from the layout |
| `src/OpenEcu.Desktop/OpenEcu.Desktop.csproj` | New Avalonia executable |
| `src/OpenEcu.Desktop/Program.cs` · `App.axaml(.cs)` | Avalonia bootstrap + theme |
| `src/OpenEcu.Desktop/Controls/RadialGauge.cs` | Custom arc gauge control |
| `src/OpenEcu.Desktop/Views/MainWindow.axaml(.cs)` | Connection bar + settings + dashboard host |
| `src/OpenEcu.Desktop/Views/DashboardView.axaml(.cs)` | Hero gauges + tile grid + fault strip |
| `tests/OpenEcu.App.Tests/AppSettingsTests.cs` | Settings round-trip |
| `tests/OpenEcu.App.Tests/ConnectionFactoryTests.cs` | Factory build |
| `tests/OpenEcu.App.Tests/MainViewModelTests.cs` | Connect/disconnect/port logic |
| `tests/OpenEcu.App.Tests/DashboardViewModelTests.cs` | Hero/tile composition |

**SDK note:** SDK 10 rejects `dotnet new -f net8.0`; create default then edit the csproj (as before). Solution is `OpenEcu.slnx`.

---

### Task 1: AppSettings (theme + accent persistence)

**Files:**
- Create: `src/OpenEcu.App/Model/AppSettings.cs`
- Test: `tests/OpenEcu.App.Tests/AppSettingsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/AppSettingsTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Model;
using Xunit;

namespace OpenEcu.App.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_are_light_theme_and_teal_accent()
    {
        var s = new AppSettings();
        s.DarkMode.Should().BeFalse();
        s.Accent.Should().Be("teal");
    }

    [Fact]
    public void Round_trips_through_a_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"openecu-settings-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { DarkMode = true, Accent = "red" }.Save(path);
            var loaded = AppSettings.Load(path);
            loaded.DarkMode.Should().BeTrue();
            loaded.Accent.Should().Be("red");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Load_returns_defaults_when_file_is_missing()
    {
        var s = AppSettings.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
        s.DarkMode.Should().BeFalse();
        s.Accent.Should().Be("teal");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter AppSettingsTests`
Expected: FAIL — `AppSettings` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/Model/AppSettings.cs`:
```csharp
using System.Text.Json;

namespace OpenEcu.App.Model;

/// <summary>Persisted UI preferences: theme + accent. Default light + teal.</summary>
public sealed class AppSettings
{
    public bool DarkMode { get; set; }
    public string Accent { get; set; } = "teal";

    /// <summary>The accent colors offered in the picker.</summary>
    public static IReadOnlyList<string> Accents { get; } =
        new[] { "white", "teal", "blue", "green", "yellow", "red", "black" };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenECU", "settings.json");

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this));
    }

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { /* corrupt file -> defaults */ }
        return new AppSettings();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter AppSettingsTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/Model/AppSettings.cs tests/OpenEcu.App.Tests/AppSettingsTests.cs
git commit -m "feat: AppSettings (theme + accent persistence)"
```

---

### Task 2: ConnectionFactory

**Files:**
- Create: `src/OpenEcu.App/Services/ConnectionFactory.cs`
- Test: `tests/OpenEcu.App.Tests/ConnectionFactoryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/ConnectionFactoryTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Services;
using Xunit;

namespace OpenEcu.App.Tests;

public class ConnectionFactoryTests
{
    [Fact]
    public void Create_builds_a_connection_without_opening_the_port()
    {
        // Constructing the stack must not throw or open hardware — just wire it up.
        var conn = new ConnectionFactory().Create("COM_NONEXISTENT");
        conn.Service.Should().NotBeNull();
        conn.Log.Should().NotBeNull();
        conn.Log.IsOpen.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ConnectionFactoryTests`
Expected: FAIL — `ConnectionFactory` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/Services/ConnectionFactory.cs`:
```csharp
using OpenEcu.Core.Obd;
using OpenEcu.Core.Transport;
using OpenEcu.Transport.Serial;

namespace OpenEcu.App.Services;

/// <summary>A live connection: the data service plus the logging transport (open via Log).</summary>
public sealed record LiveConnection(LiveDataService Service, LoggingTransport Log);

public interface IConnectionFactory
{
    LiveConnection Create(string portName);
}

/// <summary>Wires SystemSerialPort → SerialPortTransport → LoggingTransport → KLineObdSession → LiveDataService.</summary>
public sealed class ConnectionFactory : IConnectionFactory
{
    public LiveConnection Create(string portName)
    {
        var port = new SystemSerialPort(portName, baudRate: 10400, readTimeoutMs: 300, writeTimeoutMs: 1000);
        var serial = new SerialPortTransport(port);   // IEcuTransport + IBreakLine
        var log = new LoggingTransport(serial);        // logs the comms bytes
        var session = new KLineObdSession(log, serial); // transport = logged; break line = serial
        return new LiveConnection(new LiveDataService(session), log);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ConnectionFactoryTests`
Expected: PASS (1 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/Services/ConnectionFactory.cs tests/OpenEcu.App.Tests/ConnectionFactoryTests.cs
git commit -m "feat: ConnectionFactory (composition root for a live connection)"
```

---

### Task 3: MainViewModel

**Files:**
- Create: `src/OpenEcu.App/ViewModels/MainViewModel.cs`
- Test: `tests/OpenEcu.App.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/MainViewModelTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.App.Tests;

public class MainViewModelTests
{
    private sealed class FakeFactory : IConnectionFactory
    {
        public FakeObdSession Ecu { get; } = new();
        public LiveConnection Create(string portName)
        {
            var log = new LoggingTransport(new SimulatedTransport());
            return new LiveConnection(new LiveDataService(Ecu), log);
        }
    }

    private static MainViewModel New(FakeFactory f, params string[] ports) =>
        new(f, () => ports);

    [Fact]
    public void RefreshPorts_populates_and_selects_first()
    {
        var vm = New(new FakeFactory(), "COM3", "COM8");
        vm.AvailablePorts.Should().Equal("COM3", "COM8");
        vm.SelectedPort.Should().Be("COM3");
    }

    [Fact]
    public async Task Connect_then_disconnect_transitions_state()
    {
        var f = new FakeFactory();
        f.Ecu.Supported.AddRange(new byte[] { 0x0C, 0x05 });
        var vm = New(f, "COM8");

        await vm.ConnectCommand.ExecuteAsync(null);
        vm.State.Should().Be(ConnectionState.Connected);
        vm.Live.Should().NotBeNull();

        await vm.DisconnectCommand.ExecuteAsync(null);
        vm.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task Connect_failure_sets_error_state()
    {
        var f = new FakeFactory { };
        f.Ecu.ThrowOnConnect = true;
        var vm = New(f, "COM8");

        await vm.ConnectCommand.ExecuteAsync(null);

        vm.State.Should().Be(ConnectionState.Error);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter MainViewModelTests`
Expected: FAIL — `MainViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/ViewModels/MainViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenEcu.App.Model;
using OpenEcu.App.Services;
using OpenEcu.Transport.Serial;

namespace OpenEcu.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IConnectionFactory _factory;
    private readonly Func<IReadOnlyList<string>> _portProvider;
    private LiveConnection? _connection;
    private CancellationTokenSource? _loopCts;

    public MainViewModel(IConnectionFactory factory, Func<IReadOnlyList<string>>? portProvider = null)
    {
        _factory = factory;
        _portProvider = portProvider ?? (() => SerialPortEnumerator.GetPortNames());
        RefreshPorts();
    }

    public ObservableCollection<string> AvailablePorts { get; } = new();

    [ObservableProperty] private string? _selectedPort;
    [ObservableProperty] private ConnectionState _state = ConnectionState.Disconnected;
    [ObservableProperty] private string _status = "Disconnected";

    /// <summary>The connected live data service (null until connected). Views bind metrics from it.</summary>
    public LiveDataService? Live => _connection?.Service;

    /// <summary>The logging transport for the console (null until connected).</summary>
    public LoggingTransport? Log => _connection?.Log;

    public void RefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (string p in _portProvider()) AvailablePorts.Add(p);
        SelectedPort ??= AvailablePorts.FirstOrDefault();
    }

    [RelayCommand]
    private void RefreshPorts_() => RefreshPorts();

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrEmpty(SelectedPort)) return;
        State = ConnectionState.Connecting;
        Status = $"Connecting to {SelectedPort}…";
        try
        {
            _connection = _factory.Create(SelectedPort);
            await _connection.Log.OpenAsync();
            await _connection.Service.ConnectAsync();
            OnPropertyChanged(nameof(Live));
            OnPropertyChanged(nameof(Log));
            State = ConnectionState.Connected;
            Status = "Connected";
            _loopCts = new CancellationTokenSource();
            _ = _connection.Service.RunAsync(_loopCts.Token);
        }
        catch (Exception ex)
        {
            State = ConnectionState.Error;
            Status = $"Connect failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        _loopCts?.Cancel();
        if (_connection is not null)
        {
            try { await _connection.Log.CloseAsync(); await _connection.Service.DisposeAsync(); }
            catch { /* ignore on teardown */ }
        }
        _connection = null;
        OnPropertyChanged(nameof(Live));
        OnPropertyChanged(nameof(Log));
        State = ConnectionState.Disconnected;
        Status = "Disconnected";
    }
}
```
(Note: `RefreshPorts_` is named with a trailing underscore so its generated `RefreshPorts_Command` doesn't collide with the public `RefreshPorts()` method.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter MainViewModelTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/ViewModels/MainViewModel.cs tests/OpenEcu.App.Tests/MainViewModelTests.cs
git commit -m "feat: MainViewModel (ports, connect/disconnect, state)"
```

---

### Task 4: DashboardViewModel

**Files:**
- Create: `src/OpenEcu.App/ViewModels/DashboardViewModel.cs`
- Test: `tests/OpenEcu.App.Tests/DashboardViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/DashboardViewModelTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using Xunit;

namespace OpenEcu.App.Tests;

public class DashboardViewModelTests
{
    private static async Task<LiveDataService> ConnectedService(params byte[] supported)
    {
        var ecu = new FakeObdSession();
        ecu.Supported.AddRange(supported);
        var svc = new LiveDataService(ecu);
        await svc.ConnectAsync();
        return svc;
    }

    [Fact]
    public async Task Heroes_are_the_layout_hero_pids_in_order()
    {
        var svc = await ConnectedService(0x0C, 0x05, 0x11);
        var vm = new DashboardViewModel(svc);

        vm.Heroes.Select(m => m.Pid).Should().Equal((byte)0x0C, (byte)0x05);
    }

    [Fact]
    public async Task Tiles_are_the_supported_non_hero_metrics()
    {
        var svc = await ConnectedService(0x0C, 0x05, 0x11, 0x0F);
        var vm = new DashboardViewModel(svc);

        vm.Tiles.Select(m => m.Pid).Should().Contain(new byte[] { 0x11, 0x0F });
        vm.Tiles.Select(m => m.Pid).Should().NotContain(new byte[] { 0x0C, 0x05 });
    }

    [Fact]
    public async Task Dtcs_passes_through_from_the_service()
    {
        var svc = await ConnectedService(0x0C);
        var vm = new DashboardViewModel(svc);
        vm.Dtcs.Should().BeSameAs(svc.Dtcs);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DashboardViewModelTests`
Expected: FAIL — `DashboardViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/ViewModels/DashboardViewModel.cs`:
```csharp
using OpenEcu.App.Model;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;

namespace OpenEcu.App.ViewModels;

/// <summary>Composes the dashboard's hero gauges and tiles from the layout + the live metrics.</summary>
public sealed class DashboardViewModel
{
    private readonly LiveDataService _live;
    private readonly DashboardLayout _layout;

    public DashboardViewModel(LiveDataService live, DashboardLayout? layout = null)
    {
        _live = live;
        _layout = layout ?? DashboardLayout.Default;
    }

    public IReadOnlyList<MetricViewModel> Heroes =>
        _layout.HeroPids
            .Select(pid => _live.Metrics.FirstOrDefault(m => m.Pid == pid))
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();

    public IReadOnlyList<MetricViewModel> Tiles =>
        _live.Metrics.Where(m => !_layout.HeroPids.Contains(m.Pid)).ToList();

    public IReadOnlyList<string> Dtcs => _live.Dtcs;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter DashboardViewModelTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Run full suite**

Run: `dotnet test`
Expected: PASS — plans 1–8 (91) + Task1 (3) + Task2 (1) + Task3 (3) + Task4 (3) = 101 passed, 1 skipped.

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.App/ViewModels/DashboardViewModel.cs tests/OpenEcu.App.Tests/DashboardViewModelTests.cs
git commit -m "feat: DashboardViewModel (hero + tile composition)"
```

---

### Task 5: Scaffold the Avalonia executable

**Files:**
- Create: `src/OpenEcu.Desktop/OpenEcu.Desktop.csproj`, `Program.cs`, `App.axaml`, `App.axaml.cs`, `Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`

- [ ] **Step 1: Create the project**

```bash
dotnet new console -n OpenEcu.Desktop -o src/OpenEcu.Desktop
rm src/OpenEcu.Desktop/Program.cs
```

- [ ] **Step 2: Overwrite the csproj**

Replace `src/OpenEcu.Desktop/OpenEcu.Desktop.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.0.10" />
    <PackageReference Include="Avalonia.Desktop" Version="11.0.10" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.0.10" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.0.10" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenEcu.App\OpenEcu.App.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add an app manifest (DPI awareness)**

Create `src/OpenEcu.Desktop/app.manifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">permonitorv2,permonitor</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 4: Program.cs**

Create `src/OpenEcu.Desktop/Program.cs`:
```csharp
using Avalonia;

namespace OpenEcu.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

- [ ] **Step 5: App.axaml + code-behind**

Create `src/OpenEcu.Desktop/App.axaml`:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="OpenEcu.Desktop.App"
             RequestedThemeVariant="Light">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

Create `src/OpenEcu.Desktop/App.axaml.cs`:
```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using OpenEcu.Desktop.Views;

namespace OpenEcu.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(new ConnectionFactory())
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
```

- [ ] **Step 6: A minimal MainWindow (placeholder, fleshed out in Task 7)**

Create `src/OpenEcu.Desktop/Views/MainWindow.axaml`:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="OpenEcu.Desktop.Views.MainWindow"
        Width="900" Height="560" Title="OpenECU">
  <TextBlock Text="OpenECU" HorizontalAlignment="Center" VerticalAlignment="Center" FontSize="24" />
</Window>
```

Create `src/OpenEcu.Desktop/Views/MainWindow.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenEcu.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 7: Add to solution and build**

```bash
dotnet sln add src/OpenEcu.Desktop/OpenEcu.Desktop.csproj
dotnet build src/OpenEcu.Desktop
```
Expected: build succeeds. (The human can `dotnet run --project src/OpenEcu.Desktop` to see an empty "OpenECU" window.)

- [ ] **Step 8: Commit**

```bash
git add src/OpenEcu.Desktop OpenEcu.slnx
git commit -m "chore: scaffold OpenEcu.Desktop Avalonia app shell"
```

---

### Task 6: RadialGauge control

**Files:**
- Create: `src/OpenEcu.Desktop/Controls/RadialGauge.cs`

A code-only custom control: a 180° track arc + a value arc + centered value/label text. Driven by styled properties; redraws on change.

- [ ] **Step 1: Write the control**

Create `src/OpenEcu.Desktop/Controls/RadialGauge.cs`:
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenEcu.Desktop.Controls;

/// <summary>A 180° radial gauge: track arc + value arc + centered value and label text.</summary>
public sealed class RadialGauge : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<RadialGauge, double>(nameof(Value));
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<RadialGauge, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<RadialGauge, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<RadialGauge, string?>(nameof(Label));
    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<RadialGauge, string?>(nameof(ValueText));
    public static readonly StyledProperty<IBrush> AccentProperty =
        AvaloniaProperty.Register<RadialGauge, IBrush>(nameof(Accent), Brushes.Teal);

    static RadialGauge()
    {
        AffectsRender<RadialGauge>(ValueProperty, MinimumProperty, MaximumProperty,
            LabelProperty, ValueTextProperty, AccentProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? ValueText { get => GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }
    public IBrush Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 10 || h < 10) return;

        double thickness = System.Math.Max(6, w * 0.07);
        double radius = System.Math.Min(w / 2, h) - thickness;
        var center = new Point(w / 2, h - thickness);
        var trackPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), thickness)
            { LineCap = PenLineCap.Round };
        var valuePen = new Pen(Accent, thickness) { LineCap = PenLineCap.Round };

        context.DrawGeometry(null, trackPen, Arc(center, radius, 1.0));

        double range = Maximum - Minimum;
        double frac = range <= 0 ? 0 : System.Math.Clamp((Value - Minimum) / range, 0, 1);
        if (frac > 0)
            context.DrawGeometry(null, valuePen, Arc(center, radius, frac));

        var fg = new SolidColorBrush(Color.FromArgb(220, 130, 130, 130));
        DrawCenteredText(context, ValueText ?? "", center.X, center.Y - radius * 0.45, radius * 0.42, Accent);
        DrawCenteredText(context, Label ?? "", center.X, center.Y - radius * 0.12, radius * 0.20, fg);
    }

    // 180° arc from left (180°) sweeping clockwise by `frac` of a semicircle.
    private static Geometry Arc(Point center, double r, double frac)
    {
        double startAngle = System.Math.PI;                 // left
        double endAngle = System.Math.PI - System.Math.PI * frac;
        var start = new Point(center.X + r * System.Math.Cos(startAngle), center.Y - r * System.Math.Sin(startAngle));
        var end = new Point(center.X + r * System.Math.Cos(endAngle), center.Y - r * System.Math.Sin(endAngle));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        return geo;
    }

    private static void DrawCenteredText(DrawingContext ctx, string text, double cx, double cy, double size, IBrush brush)
    {
        if (string.IsNullOrEmpty(text) || size < 6) return;
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, size, brush);
        ctx.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/OpenEcu.Desktop`
Expected: build succeeds. (If an Avalonia 11.0.10 API name differs — e.g. `ArcTo` overload — adjust the call to the correct signature without changing behavior, then rebuild.)

- [ ] **Step 3: Commit**

```bash
git add src/OpenEcu.Desktop/Controls/RadialGauge.cs
git commit -m "feat: RadialGauge custom control (180-degree arc gauge)"
```

---

### Task 7: Dashboard view + full MainWindow (connection bar, theme/accent, live dashboard)

**Files:**
- Create: `src/OpenEcu.Desktop/Views/DashboardView.axaml`, `Views/DashboardView.axaml.cs`
- Modify: `src/OpenEcu.Desktop/Views/MainWindow.axaml` (replace placeholder)

- [ ] **Step 1: DashboardView**

Create `src/OpenEcu.Desktop/Views/DashboardView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:c="clr-namespace:OpenEcu.Desktop.Controls"
             x:Class="OpenEcu.Desktop.Views.DashboardView">
  <ScrollViewer Padding="16">
    <StackPanel Spacing="16">
      <ItemsControl ItemsSource="{Binding Heroes}">
        <ItemsControl.ItemsPanel>
          <ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Margin="8" Width="220" Height="150">
              <c:RadialGauge Value="{Binding Value, FallbackValue=0}"
                             Minimum="{Binding Minimum}" Maximum="{Binding Maximum}"
                             ValueText="{Binding Display}" Label="{Binding Name}" />
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>

      <ItemsControl ItemsSource="{Binding Tiles}">
        <ItemsControl.ItemsPanel>
          <ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Margin="6" Width="150" Padding="12"
                    Background="{DynamicResource SystemControlBackgroundListLowBrush}"
                    CornerRadius="8">
              <StackPanel>
                <TextBlock Text="{Binding Name}" FontSize="12" Opacity="0.7" />
                <TextBlock Text="{Binding Display}" FontSize="22" />
              </StackPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>

      <Border Background="#33CC8800" CornerRadius="8" Padding="10"
              IsVisible="{Binding Dtcs.Count}">
        <ItemsControl ItemsSource="{Binding Dtcs}">
          <ItemsControl.ItemTemplate>
            <DataTemplate><TextBlock Text="{Binding}" /></DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </Border>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

Create `src/OpenEcu.Desktop/Views/DashboardView.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenEcu.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 2: Full MainWindow with connection bar + accent/theme + dashboard host**

Replace `src/OpenEcu.Desktop/Views/MainWindow.axaml` with:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:v="clr-namespace:OpenEcu.Desktop.Views"
        x:Class="OpenEcu.Desktop.Views.MainWindow"
        Width="940" Height="600" Title="OpenECU">
  <DockPanel>
    <Border DockPanel.Dock="Top" Padding="10" Background="{DynamicResource SystemControlBackgroundListLowBrush}">
      <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
        <ComboBox ItemsSource="{Binding AvailablePorts}" SelectedItem="{Binding SelectedPort}"
                  MinWidth="120" />
        <Button Content="Refresh" Command="{Binding RefreshPorts_Command}" />
        <Button Content="Connect" Command="{Binding ConnectCommand}" />
        <Button Content="Disconnect" Command="{Binding DisconnectCommand}" />
        <TextBlock Text="{Binding Status}" VerticalAlignment="Center" Margin="12,0,0,0" />
      </StackPanel>
    </Border>

    <ContentControl Content="{Binding Live}">
      <ContentControl.DataTemplates>
        <DataTemplate DataType="x:Null">
          <TextBlock Text="Not connected — pick a port and Connect."
                     HorizontalAlignment="Center" VerticalAlignment="Center" Opacity="0.6" />
        </DataTemplate>
      </ContentControl.DataTemplates>
    </ContentControl>
  </DockPanel>
</Window>
```

(Binding the dashboard: the `ContentControl.Content` is the `LiveDataService`; a `DataTemplate` for it hosts the `DashboardView` whose `DataContext` is a `DashboardViewModel`. To keep wiring explicit, set it in code-behind instead — see Step 3.)

- [ ] **Step 3: Wire the dashboard in code-behind**

Replace `src/OpenEcu.Desktop/Views/MainWindow.axaml.cs` with:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;

namespace OpenEcu.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.GetObservable(DataContextProperty).Subscribe(OnDataContextChanged);
    }

    private MainViewModel? _vm;

    private void OnDataContextChanged(object? dc)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = dc as MainViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        UpdateContent();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Live)) UpdateContent();
    }

    private void UpdateContent()
    {
        var host = this.FindControl<ContentControl>("Host");
        if (host is null) return;
        host.Content = _vm?.Live is LiveDataService live
            ? new DashboardView { DataContext = new DashboardViewModel(live) }
            : null;
    }
}
```

And give the `ContentControl` the name `Host` in the XAML (replace the `<ContentControl ...>` element from Step 2 with):
```xml
    <ContentControl x:Name="Host">
      <TextBlock Text="Not connected — pick a port and Connect."
                 HorizontalAlignment="Center" VerticalAlignment="Center" Opacity="0.6" />
    </ContentControl>
```

- [ ] **Step 4: Build**

Run: `dotnet build src/OpenEcu.Desktop`
Expected: build succeeds. Resolve any Avalonia API/XAML mismatch by reading the build error (do not change behavior).

- [ ] **Step 5: Run the full test suite (unchanged) + commit**

Run: `dotnet test`
Expected: 101 passed, 1 skipped (unchanged — this task adds no unit tests).

```bash
git add src/OpenEcu.Desktop/Views/
git commit -m "feat: live Standard Dashboard view + connection bar in MainWindow"
```

---

### Task 8: Manual hardware verification (the human, on the bike)

Not automated. With the cable on COM8 and the bike powered (key on):

```bash
dotnet run --project src/OpenEcu.Desktop
```

- [ ] The window opens; pick **COM8** in the dropdown and click **Connect**.
- [ ] Status shows "Connected"; the dashboard appears.
- [ ] The two hero gauges (RPM, Coolant) and the tiles show live values; coolant/intake read real ambient temps; values refresh continuously.
- [ ] If a stored DTC exists, the fault strip shows `P1502`.
- [ ] Click **Disconnect** → returns to "Not connected".

If connect fails: confirm the FTDI latency timer is 1 ms (Device Manager) and that COM8 is the cable. The status bar shows the error message.

---

## Self-Review

**Spec coverage (this plan's slice):**
- Connection bar (port list, connect/disconnect, status) (spec §6) → Tasks 3, 7 ✅
- `ConnectionFactory` composition + UI-thread-marshalled polling (spec §4/§8) → Tasks 2, 3 ✅
- `RadialGauge` custom control (spec §6) → Task 6 ✅
- Live Standard Dashboard: hero gauges + tiles + fault strip, data-driven from `DashboardLayout` (spec §6/§7) → Tasks 4, 7 ✅
- Theme/accent settings persistence model (spec §3) → Task 1 ✅ (the theme/accent *picker UI* + applying the accent brush to gauges is a small follow-up wired in plan 10 alongside Diagnostics/Console; the persistence model + defaults ship here)
- Avalonia app bootstrap, light-default theme → Task 5 ✅
- **Deferred to plan 10:** Diagnostics view, Console view (+ wiring `Log` events), the theme-toggle/accent-picker UI controls. **Plan 11:** Racing Dashboard.

**Placeholder scan:** No TBD/TODO. Avalonia tasks (5–7) carry complete file contents and are build-verified; the manual run (Task 8) is explicitly human-only, as the UI can't be launched headless.

**Type consistency:** `AppSettings` (`DarkMode`, `Accent`, `Accents`, `Load`/`Save`), `IConnectionFactory.Create→LiveConnection(Service, Log)`, `MainViewModel` (`AvailablePorts`, `SelectedPort`, `State`, `Status`, `Live`, `Log`, `ConnectCommand`/`DisconnectCommand`/`RefreshPorts_Command`), `DashboardViewModel` (`Heroes`, `Tiles`, `Dtcs`), and `RadialGauge` (`Value`/`Minimum`/`Maximum`/`Label`/`ValueText`/`Accent`) are referenced consistently across tasks and bindings. Reuses `LiveDataService`, `MetricViewModel` (`Pid`/`Name`/`Value`/`Display`/`Minimum`/`Maximum`), `DashboardLayout`, `LoggingTransport`, `SerialPortEnumerator` from plans 1–8. Tests use `using AwesomeAssertions;`.
