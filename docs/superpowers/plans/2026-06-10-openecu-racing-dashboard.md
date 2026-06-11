# OpenECU Racing Dashboard (+ polish) — Implementation Plan (Plan 11)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the optional **Racing Dashboard** mode — a custom `AnalogTachometer`, an aligned race-readout layout, a kept-but-greyed gear box, and a **Standard ⇄ Racing** toggle (persisted) — plus two quick polish fixes spotted in the running app: the `RadialGauge` over-full arc, and the static-flag over-firing with the engine off.

**Architecture:** The Racing Dashboard is an alternate *skin* over the same `LiveDataService` data. Testable additions to `OpenEcu.App` (`TachConfig`, `RacingDashboardViewModel`, static-flag suppression on `LiveDataService`, `RacingMode` on `MainViewModel`); a new `AnalogTachometer` Avalonia control + `RacingDashboardView` + the mode toggle in `OpenEcu.Desktop`.

**Tech Stack:** .NET 8, Avalonia 11.0.10, `CommunityToolkit.Mvvm`, xUnit, **AwesomeAssertions**. Builds on plans 1–10.

**Spec:** `docs/superpowers/specs/2026-06-10-openecu-ui-design.md` §7 "Optional Racing Dashboard". Decisions: **aligned** readouts; **gear box kept** (greyed `—/n/a`); **configurable tach** (`TachConfig`, default redline 9,500 / max 11,000); honors light/dark + accent.

**Avalonia note:** tasks 1, 6, 7 are verified by `dotnet build src/OpenEcu.Desktop`. Resolve any 11.0.10 API/XAML mismatch by the build error (API surface only, not behavior). The `AnalogTachometer` arc mirrors the working `RadialGauge`; if a value arc sweeps the wrong way, flip `SweepDirection` — note it.

**Prerequisite:** Plans 1–10 on `main`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.Desktop/Controls/RadialGauge.cs` | **Modify:** fix isLargeArc (Task 1) |
| `src/OpenEcu.App/Services/LiveDataService.cs` | **Modify:** suppress static when engine off (Task 2) |
| `src/OpenEcu.App/Model/TachConfig.cs` | Tach range (max + redline), configurable |
| `src/OpenEcu.App/ViewModels/RacingDashboardViewModel.cs` | RPM/speed/gear/readouts for the racing view |
| `src/OpenEcu.App/Model/AppSettings.cs` | **Modify:** add `RacingMode` |
| `src/OpenEcu.App/ViewModels/MainViewModel.cs` | **Modify:** add persisted `RacingMode` |
| `src/OpenEcu.Desktop/Controls/AnalogTachometer.cs` | Custom analog tach control |
| `src/OpenEcu.Desktop/Views/RacingDashboardView.axaml(.cs)` | Racing layout |
| `src/OpenEcu.Desktop/Views/MainWindow.axaml(.cs)` | **Modify:** Racing toggle + content swap |
| `tests/OpenEcu.App.Tests/*` | Tests for the testable units |

---

### Task 1: Fix the RadialGauge over-full arc

**Files:**
- Modify: `src/OpenEcu.Desktop/Controls/RadialGauge.cs`

The `Arc` helper passes `isLargeArc: frac > 0.5`. Since a value arc here is at most a 180° semicircle, `isLargeArc` must always be `false`; the `frac > 0.5` made mid/high gauges draw the major (long-way) arc.

- [ ] **Step 1: Apply the fix**

In `src/OpenEcu.Desktop/Controls/RadialGauge.cs`, in the `Arc` method, change:
```csharp
            ctx.ArcTo(end, new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise);
```
to:
```csharp
            ctx.ArcTo(end, new Size(r, r), 0, isLargeArc: false, SweepDirection.Clockwise);
```

- [ ] **Step 2: Build**

Run: `dotnet build src/OpenEcu.Desktop`
Expected: build succeeds. (Visual: hero gauges now fill proportionally — RPM empty at 0, coolant ~half at mid-range.)

- [ ] **Step 3: Commit**

```bash
git add src/OpenEcu.Desktop/Controls/RadialGauge.cs
git commit -m "fix: RadialGauge value arc must not use the large-arc flag"
```

---

### Task 2: Suppress the static flag when the engine is off

**Files:**
- Modify: `src/OpenEcu.App/Services/LiveDataService.cs`
- Modify: `tests/OpenEcu.App.Tests/LiveDataServiceTests.cs`

With the engine off, every value is unchanging, so the static flag fires on everything. It's only meaningful while the engine runs (then truly-frozen PIDs like the fuel trims stand out). Suppress all static flags unless RPM indicates a running engine.

- [ ] **Step 1: Write the failing tests (append to LiveDataServiceTests)**

Add inside `LiveDataServiceTests`, before the closing brace:
```csharp
    [Fact]
    public async Task Static_is_suppressed_when_engine_is_off()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05 });
        fake.Readings[0x0C] = new PidReading(0x0C, "Engine RPM", 0, "rpm", new byte[] { 0, 0 });
        fake.Readings[0x05] = new PidReading(0x05, "Coolant", 80, "C", new byte[] { 0x78 });
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();

        for (int i = 0; i < 10; i++) await svc.PollOnceAsync();

        svc.Metrics.First(m => m.Pid == 0x05).IsStatic.Should().BeFalse();
    }

    [Fact]
    public async Task Static_applies_when_engine_running()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05 });
        fake.Readings[0x0C] = new PidReading(0x0C, "Engine RPM", 1200, "rpm", new byte[] { 0x12, 0xC0 });
        fake.Readings[0x05] = new PidReading(0x05, "Coolant", 80, "C", new byte[] { 0x78 }); // never changes
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();

        for (int i = 0; i < 10; i++) await svc.PollOnceAsync();

        svc.Metrics.First(m => m.Pid == 0x05).IsStatic.Should().BeTrue();
    }
```
Add `using OpenEcu.Core.Obd;` to the test file's usings (for `PidReading`) if not already present.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter LiveDataServiceTests`
Expected: FAIL — the engine-off test fails (coolant is currently flagged static).

- [ ] **Step 3: Add suppression to PollOnceAsync**

In `src/OpenEcu.App/Services/LiveDataService.cs`, add a constant and a call at the end of `PollOnceAsync`. Insert the suppression call just before `LastUpdate = DateTime.UtcNow;`:
```csharp
        SuppressStaticUnlessEngineRunning();
        LastUpdate = DateTime.UtcNow;
```
And add the helper + constant (near the other private members):
```csharp
    private const double EngineRunningRpm = 400;

    // Static flags are only meaningful with the engine running; with it off everything is
    // unchanging, so clear them to avoid misleading the user.
    private void SuppressStaticUnlessEngineRunning()
    {
        bool running = _byPid.TryGetValue(0x0C, out var rpm) && rpm.Value is double v && v >= EngineRunningRpm;
        if (running) return;
        foreach (MetricViewModel m in Metrics) m.IsStatic = false;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter LiveDataServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/Services/LiveDataService.cs tests/OpenEcu.App.Tests/LiveDataServiceTests.cs
git commit -m "fix: only flag static PIDs while the engine is running"
```

---

### Task 3: TachConfig

**Files:**
- Create: `src/OpenEcu.App/Model/TachConfig.cs`
- Test: `tests/OpenEcu.App.Tests/TachConfigTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/TachConfigTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Model;
using Xunit;

namespace OpenEcu.App.Tests;

public class TachConfigTests
{
    [Fact]
    public void Default_matches_the_955i()
    {
        TachConfig.Default.MaxRpm.Should().Be(11000);
        TachConfig.Default.RedlineRpm.Should().Be(9500);
    }

    [Fact]
    public void Is_configurable_per_model()
    {
        var cfg = new TachConfig(MaxRpm: 14000, RedlineRpm: 12500);
        cfg.MaxRpm.Should().Be(14000);
        cfg.RedlineRpm.Should().Be(12500);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter TachConfigTests`
Expected: FAIL — `TachConfig` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/Model/TachConfig.cs`:
```csharp
namespace OpenEcu.App.Model;

/// <summary>Tachometer range. Configurable per model so the redline/sweep can adjust.</summary>
public sealed record TachConfig(double MaxRpm, double RedlineRpm)
{
    /// <summary>Default for the Triumph Speed Triple 955i.</summary>
    public static TachConfig Default { get; } = new(MaxRpm: 11000, RedlineRpm: 9500);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter TachConfigTests`
Expected: PASS (2 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/Model/TachConfig.cs tests/OpenEcu.App.Tests/TachConfigTests.cs
git commit -m "feat: configurable TachConfig (max + redline rpm)"
```

---

### Task 4: RacingDashboardViewModel

**Files:**
- Create: `src/OpenEcu.App/ViewModels/RacingDashboardViewModel.cs`
- Test: `tests/OpenEcu.App.Tests/RacingDashboardViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/RacingDashboardViewModelTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Model;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using Xunit;

namespace OpenEcu.App.Tests;

public class RacingDashboardViewModelTests
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
    public async Task Exposes_rpm_speed_gear_and_readouts()
    {
        var svc = await Connected(0x0C, 0x0D, 0x11, 0x05, 0x0E, 0x14);
        var vm = new RacingDashboardViewModel(svc);

        vm.Rpm!.Pid.Should().Be(0x0C);
        vm.Speed!.Pid.Should().Be(0x0D);
        vm.Gear.Should().Be("—");
        vm.Readouts.Select(m => m.Pid).Should().Equal((byte)0x11, (byte)0x05, (byte)0x0E, (byte)0x14);
    }

    [Fact]
    public async Task Uses_the_default_tach_config()
    {
        var svc = await Connected(0x0C);
        var vm = new RacingDashboardViewModel(svc);
        vm.Tach.Should().Be(TachConfig.Default);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter RacingDashboardViewModelTests`
Expected: FAIL — `RacingDashboardViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/ViewModels/RacingDashboardViewModel.cs`:
```csharp
using OpenEcu.App.Model;
using OpenEcu.App.Services;

namespace OpenEcu.App.ViewModels;

/// <summary>The racing-mode skin: RPM tach + speed + (n/a) gear + a few race readouts.</summary>
public sealed class RacingDashboardViewModel
{
    private static readonly byte[] ReadoutPids = { 0x11, 0x05, 0x0E, 0x14 }; // throttle, coolant, timing, O2

    private readonly LiveDataService _live;

    public RacingDashboardViewModel(LiveDataService live, TachConfig? tach = null)
    {
        _live = live;
        Tach = tach ?? TachConfig.Default;
    }

    public TachConfig Tach { get; }

    public MetricViewModel? Rpm => Find(0x0C);
    public MetricViewModel? Speed => Find(0x0D);

    /// <summary>OBD-II doesn't expose gear on this bike; shown greyed.</summary>
    public string Gear => "—";

    public IReadOnlyList<MetricViewModel> Readouts =>
        ReadoutPids.Select(Find).Where(m => m is not null).Select(m => m!).ToList();

    private MetricViewModel? Find(byte pid) => _live.Metrics.FirstOrDefault(m => m.Pid == pid);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter RacingDashboardViewModelTests`
Expected: PASS (2 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/ViewModels/RacingDashboardViewModel.cs tests/OpenEcu.App.Tests/RacingDashboardViewModelTests.cs
git commit -m "feat: RacingDashboardViewModel (tach, speed, gear, readouts)"
```

---

### Task 5: RacingMode setting (persisted) on MainViewModel

**Files:**
- Modify: `src/OpenEcu.App/Model/AppSettings.cs`
- Modify: `src/OpenEcu.App/ViewModels/MainViewModel.cs`
- Modify: `tests/OpenEcu.App.Tests/MainViewModelSettingsTests.cs`

- [ ] **Step 1: Write the failing test (append to MainViewModelSettingsTests)**

Add inside `MainViewModelSettingsTests`, before the closing brace:
```csharp
    [Fact]
    public void Racing_mode_defaults_off_and_persists()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oe-{Guid.NewGuid():N}.json");
        try
        {
            var vm = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            vm.RacingMode.Should().BeFalse();

            vm.RacingMode = true;
            var reloaded = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            reloaded.RacingMode.Should().BeTrue();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter MainViewModelSettingsTests`
Expected: FAIL — `RacingMode` does not exist.

- [ ] **Step 3: Add `RacingMode` to AppSettings and MainViewModel**

In `src/OpenEcu.App/Model/AppSettings.cs`, add a property next to `Accent`:
```csharp
    public bool RacingMode { get; set; }
```

In `src/OpenEcu.App/ViewModels/MainViewModel.cs`: in the constructor, after `_accent = _settings.Accent;`, add:
```csharp
        _racingMode = _settings.RacingMode;
```
Add the observable property + persistence (next to `_accent`):
```csharp
    [ObservableProperty] private bool _racingMode;

    partial void OnRacingModeChanged(bool value) { _settings.RacingMode = value; _settings.Save(_settingsPath); }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter MainViewModelSettingsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/Model/AppSettings.cs src/OpenEcu.App/ViewModels/MainViewModel.cs tests/OpenEcu.App.Tests/MainViewModelSettingsTests.cs
git commit -m "feat: persisted RacingMode toggle on MainViewModel"
```

---

### Task 6: AnalogTachometer control

**Files:**
- Create: `src/OpenEcu.Desktop/Controls/AnalogTachometer.cs`

A code-only control: a 240° sweep (0 → MaxRpm), tick marks + labels per 1000 rpm, a red redline zone, an accent value arc, a needle, and the digital RPM in the center.

- [ ] **Step 1: Write the control**

Create `src/OpenEcu.Desktop/Controls/AnalogTachometer.cs`:
```csharp
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenEcu.Desktop.Controls;

/// <summary>Analog tachometer: 240° sweep, ticks/labels per 1000 rpm, redline zone, needle, digital rpm.</summary>
public sealed class AnalogTachometer : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<AnalogTachometer, double>(nameof(Value));
    public static readonly StyledProperty<double> MaxRpmProperty =
        AvaloniaProperty.Register<AnalogTachometer, double>(nameof(MaxRpm), 11000);
    public static readonly StyledProperty<double> RedlineRpmProperty =
        AvaloniaProperty.Register<AnalogTachometer, double>(nameof(RedlineRpm), 9500);
    public static readonly StyledProperty<IBrush> AccentProperty =
        AvaloniaProperty.Register<AnalogTachometer, IBrush>(nameof(Accent), Brushes.Teal);

    static AnalogTachometer() =>
        AffectsRender<AnalogTachometer>(ValueProperty, MaxRpmProperty, RedlineRpmProperty, AccentProperty);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double MaxRpm { get => GetValue(MaxRpmProperty); set => SetValue(MaxRpmProperty, value); }
    public double RedlineRpm { get => GetValue(RedlineRpmProperty); set => SetValue(RedlineRpmProperty, value); }
    public IBrush Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    private const double A0 = 210, A1 = -30;            // sweep, degrees
    private static readonly Color Red = Color.FromRgb(0xE2, 0x4B, 0x4A);
    private static readonly Color Track = Color.FromArgb(60, 128, 128, 128);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 40 || h < 40) return;
        double radius = Math.Min(w, h) / 2 - 18;
        var c = new Point(w / 2, h / 2);
        double max = MaxRpm <= 0 ? 1 : MaxRpm;
        double redFrac = Math.Clamp(RedlineRpm / max, 0, 1);
        double valFrac = Math.Clamp(Value / max, 0, 1);

        var trackPen = new Pen(new SolidColorBrush(Track), 12) { LineCap = PenLineCap.Round };
        var redPen = new Pen(new SolidColorBrush(Red), 12) { LineCap = PenLineCap.Round };
        var valPen = new Pen(Accent, 12) { LineCap = PenLineCap.Round };

        ctx.DrawGeometry(null, trackPen, Arc(c, radius, 0, 1));
        ctx.DrawGeometry(null, redPen, Arc(c, radius, redFrac, 1));
        if (valFrac > 0)
            ctx.DrawGeometry(null, valPen, Arc(c, radius, 0, Math.Min(valFrac, redFrac)));
        if (valFrac > redFrac)
            ctx.DrawGeometry(null, redPen, Arc(c, radius, redFrac, valFrac));

        int thousands = (int)Math.Round(max / 1000);
        var tickGray = new SolidColorBrush(Color.FromArgb(200, 150, 150, 150));
        for (int t = 0; t <= thousands; t++)
        {
            double f = t / (double)thousands;
            bool red = f >= redFrac - 1e-6;
            var pen = new Pen(red ? new SolidColorBrush(Red) : tickGray, 2);
            ctx.DrawLine(pen, PointAt(c, radius - 14, f), PointAt(c, radius - 2, f));
            DrawText(ctx, t.ToString(), PointAt(c, radius - 30, f), 13, red ? new SolidColorBrush(Red) : tickGray);
        }

        var needle = new Pen(new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)), 4) { LineCap = PenLineCap.Round };
        ctx.DrawLine(needle, c, PointAt(c, radius - 20, valFrac));
        ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)), null, c, 6, 6);

        DrawText(ctx, ((int)Math.Round(Value)).ToString(), new Point(c.X, c.Y + radius * 0.42), radius * 0.30,
            new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)), center: true);
        DrawText(ctx, "rpm", new Point(c.X, c.Y + radius * 0.66), 12,
            new SolidColorBrush(Color.FromArgb(200, 130, 140, 155)), center: true);
    }

    private static Point PointAt(Point c, double r, double frac)
    {
        double deg = A0 - (A0 - A1) * frac;
        double rad = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y - r * Math.Sin(rad));
    }

    private static Geometry Arc(Point c, double r, double f0, double f1)
    {
        var start = PointAt(c, r, f0);
        var end = PointAt(c, r, f1);
        bool large = (f1 - f0) * (A0 - A1) > 180;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(start, false);
            g.ArcTo(end, new Size(r, r), 0, large, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        return geo;
    }

    private static void DrawText(DrawingContext ctx, string text, Point at, double size, IBrush brush, bool center = false)
    {
        if (size < 6) return;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        var p = center ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2);
        ctx.DrawText(ft, p);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/OpenEcu.Desktop`
Expected: build succeeds. (If a value arc renders the wrong direction, flip `SweepDirection.Clockwise` → `CounterClockwise` in `Arc`, and note it.)

- [ ] **Step 3: Commit**

```bash
git add src/OpenEcu.Desktop/Controls/AnalogTachometer.cs
git commit -m "feat: AnalogTachometer control (sweep, ticks, redline, needle)"
```

---

### Task 7: RacingDashboardView + Standard/Racing toggle + wiring

**Files:**
- Create: `src/OpenEcu.Desktop/Views/RacingDashboardView.axaml(.cs)`
- Modify: `src/OpenEcu.Desktop/Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`

- [ ] **Step 1: RacingDashboardView**

Create `src/OpenEcu.Desktop/Views/RacingDashboardView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:OpenEcu.App.ViewModels;assembly=OpenEcu.App"
             xmlns:c="clr-namespace:OpenEcu.Desktop.Controls"
             x:Class="OpenEcu.Desktop.Views.RacingDashboardView"
             x:DataType="vm:RacingDashboardViewModel">
  <Grid ColumnDefinitions="Auto,*" Margin="16" ColumnSpacing="20">
    <c:AnalogTachometer Grid.Column="0" Width="320" Height="320"
                        Value="{Binding Rpm.Value, FallbackValue=0}"
                        MaxRpm="{Binding Tach.MaxRpm}" RedlineRpm="{Binding Tach.RedlineRpm}"
                        Accent="{DynamicResource AppAccentBrush}" />

    <StackPanel Grid.Column="1" Spacing="14" VerticalAlignment="Center">
      <Grid ColumnDefinitions="*,Auto">
        <StackPanel Grid.Column="0">
          <TextBlock Text="SPEED" FontSize="12" Opacity="0.6" />
          <TextBlock Text="{Binding Speed.Display, FallbackValue='—'}" FontSize="40" />
        </StackPanel>
        <Border Grid.Column="1" BorderBrush="{DynamicResource SystemControlForegroundBaseMediumLowBrush}"
                BorderThickness="1" CornerRadius="10" Padding="14,6" VerticalAlignment="Top">
          <StackPanel>
            <TextBlock Text="{Binding Gear}" FontSize="28" HorizontalAlignment="Center" />
            <TextBlock Text="GEAR · n/a" FontSize="10" Opacity="0.5" HorizontalAlignment="Center" />
          </StackPanel>
        </Border>
      </Grid>

      <ItemsControl ItemsSource="{Binding Readouts}">
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="vm:MetricViewModel">
            <Grid ColumnDefinitions="110,*,90" Margin="0,4" ColumnSpacing="10">
              <TextBlock Grid.Column="0" Text="{Binding Name}" FontSize="12" Opacity="0.75" VerticalAlignment="Center" />
              <ProgressBar Grid.Column="1" Minimum="{Binding Minimum}" Maximum="{Binding Maximum}"
                           Value="{Binding Value, FallbackValue=0}" Height="8"
                           Foreground="{DynamicResource AppAccentBrush}" VerticalAlignment="Center" />
              <TextBlock Grid.Column="2" Text="{Binding Display}" FontSize="16" TextAlignment="Right" VerticalAlignment="Center" />
            </Grid>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </Grid>
</UserControl>
```

Create `src/OpenEcu.Desktop/Views/RacingDashboardView.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenEcu.Desktop.Views;

public partial class RacingDashboardView : UserControl
{
    public RacingDashboardView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 2: Add the Racing toggle to the MainWindow settings bar**

In `src/OpenEcu.Desktop/Views/MainWindow.axaml`, add a toggle next to the Dark toggle. Change the right-hand settings `StackPanel` to:
```xml
        <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
          <ToggleButton Content="Racing" IsChecked="{Binding RacingMode}" />
          <ToggleButton Content="Dark" IsChecked="{Binding DarkMode}" />
          <ComboBox ItemsSource="{Binding Accents}" SelectedItem="{Binding Accent}" MinWidth="90" />
        </StackPanel>
```

- [ ] **Step 3: Swap dashboard content on RacingMode in code-behind**

In `src/OpenEcu.Desktop/Views/MainWindow.axaml.cs`: handle the `RacingMode` change and choose the dashboard view. In `OnVmChanged`, add a case:
```csharp
            case nameof(MainViewModel.RacingMode): UpdateViews(); break;
```
And in `UpdateViews()`, replace the dashboard-host assignment line:
```csharp
            if (dash is not null) dash.Content = new DashboardView { DataContext = new DashboardViewModel(live) };
```
with:
```csharp
            if (dash is not null)
                dash.Content = _vm!.RacingMode
                    ? new RacingDashboardView { DataContext = new RacingDashboardViewModel(live) }
                    : new DashboardView { DataContext = new DashboardViewModel(live) };
```

- [ ] **Step 4: Build + full suite**

Run: `dotnet build src/OpenEcu.Desktop`
Expected: build succeeds.

Run: `dotnet test`
Expected: PASS — plans 1–10 (112) + Task2 (2) + Task3 (2) + Task4 (2) + Task5 (1) = 119 passed, 1 skipped.

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Desktop/Views/RacingDashboardView.axaml src/OpenEcu.Desktop/Views/RacingDashboardView.axaml.cs src/OpenEcu.Desktop/Views/MainWindow.axaml src/OpenEcu.Desktop/Views/MainWindow.axaml.cs
git commit -m "feat: RacingDashboardView + Standard/Racing toggle"
```

---

### Task 8: Manual hardware verification (the human, on the bike)

```bash
dotnet run --project src/OpenEcu.Desktop
```

- [ ] Connect to COM8. On the **Dashboard** tab, the RPM hero gauge now reads empty at 0 (arc fix), coolant fills proportionally.
- [ ] Flip the **Racing** toggle: the dashboard swaps to the analog tach + speed + greyed gear box + the aligned readout bars. With the engine running, the needle sweeps and the bars move live; redline shows ~9,500.
- [ ] Toggle **Dark** and change **accent** — the racing tach + bars recolor and honor the theme. Close/relaunch → Racing + theme + accent are remembered.
- [ ] On **Diagnostics**, the "static" markers no longer blanket every row with the engine off; with it running, the frozen fuel-trim PIDs stand out.

---

## Self-Review

**Spec coverage (§7 Racing Dashboard + field-driven polish):**
- AnalogTachometer (sweep, ticks, redline, needle, digital rpm), configurable range → Tasks 3, 6 ✅
- Aligned race readouts + kept greyed gear box + speed → Tasks 4, 7 ✅
- Standard ⇄ Racing toggle, persisted; honors theme + accent → Tasks 5, 7 ✅
- Polish: RadialGauge arc fix → Task 1; static-flag only while running → Task 2 ✅
- **Deferred:** angular/skew styling of readout bars (clean aligned bars ship now); per-model tach auto-selection (config exists, default 955i); ELM327/Bluetooth, Mode 04 — later.

**Placeholder scan:** No TBD/TODO. The greyed gear box is an intentional kept-but-n/a element (forward-compatible), not a placeholder.

**Type consistency:** `TachConfig(MaxRpm, RedlineRpm)/.Default`; `RacingDashboardViewModel` (`Rpm`, `Speed`, `Gear`, `Readouts`, `Tach`); `MainViewModel.RacingMode` + `AppSettings.RacingMode`; `AnalogTachometer` (`Value`/`MaxRpm`/`RedlineRpm`/`Accent`); `RadialGauge.Arc` fix; `LiveDataService.SuppressStaticUnlessEngineRunning`. Reuses `MetricViewModel` (`Pid`/`Name`/`Value`/`Display`/`Minimum`/`Maximum`/`IsStatic`), `LiveDataService.Metrics`, `AppAccentBrush`, `MainViewModel` view-swap from plans 9–10. Tests use `using AwesomeAssertions;`.
