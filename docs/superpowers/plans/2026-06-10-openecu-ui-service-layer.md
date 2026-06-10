# OpenECU UI Service Layer — Implementation Plan (Plan 8)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the UI-agnostic, fully-tested presentation/data layer for the OpenECU app: an `IObdSession` abstraction, a `LoggingTransport` (for the Console), the metric/layout models, the `MetricViewModel`, and the `LiveDataService` polling engine — all with zero Avalonia dependency.

**Architecture:** A new `OpenEcu.App` **class library** (no Avalonia; references `OpenEcu.Core`, `OpenEcu.Transport.Serial`, `CommunityToolkit.Mvvm`) holds the models, view-models, and `LiveDataService`. `LiveDataService` polls an `IObdSession` (implemented by the existing `KLineObdSession`) on a weighted schedule and exposes observable state. The Avalonia executable (`OpenEcu.Desktop`) that renders this is **plan 9**.

**Tech Stack:** .NET 8 (C# 12), `CommunityToolkit.Mvvm` (MIT), xUnit, **AwesomeAssertions** (MIT — `using AwesomeAssertions;`). Builds on plans 1–7.

**Spec:** `docs/superpowers/specs/2026-06-10-openecu-ui-design.md`. This plan implements spec §4 (LiveDataService, LoggingTransport), §6 (MetricDescriptor, MetricViewModel, DashboardLayout), §7 (data-driven layout), and §8 (weighted polling, DTC cadence, failure isolation, heartbeat). Avalonia views/controls/theme/settings are plan 9.

**Structural note (refines spec §5):** the spec showed one `OpenEcu.App` Avalonia project. To keep this layer testable without Avalonia, `OpenEcu.App` is a plain class library here (logic only); the Avalonia executable in plan 9 is `OpenEcu.Desktop`, referencing `OpenEcu.App`.

**Prerequisite:** Plans 1–7 on `main` (`KLineObdSession`, `PidReading`, `IEcuTransport`, `SimulatedTransport`).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.Core/Obd/IObdSession.cs` | Session abstraction `LiveDataService` polls |
| `src/OpenEcu.Core/Obd/KLineObdSession.cs` | **Modify:** implement `IObdSession` |
| `src/OpenEcu.Core/Transport/LoggingTransport.cs` | `IEcuTransport` decorator raising Tx/Rx events |
| `src/OpenEcu.App/OpenEcu.App.csproj` | New class library (no Avalonia) |
| `src/OpenEcu.App/Model/MetricDescriptor.cs` | PID → name, unit, min, max, accent + catalog |
| `src/OpenEcu.App/Model/DashboardLayout.cs` | Hero + tile PID slots (data-driven; default) |
| `src/OpenEcu.App/ViewModels/MetricViewModel.cs` | One live reading (observable) |
| `src/OpenEcu.App/Services/LiveDataService.cs` | Connect + weighted poll loop + state |
| `tests/OpenEcu.Core.Tests/Transport/LoggingTransportTests.cs` | LoggingTransport tests |
| `tests/OpenEcu.App.Tests/OpenEcu.App.Tests.csproj` | New xUnit project |
| `tests/OpenEcu.App.Tests/FakeObdSession.cs` | In-memory `IObdSession` for tests |
| `tests/OpenEcu.App.Tests/MetricCatalogTests.cs` | Catalog tests |
| `tests/OpenEcu.App.Tests/DashboardLayoutTests.cs` | Layout tests |
| `tests/OpenEcu.App.Tests/MetricViewModelTests.cs` | MetricViewModel tests |
| `tests/OpenEcu.App.Tests/LiveDataServiceTests.cs` | Polling-engine tests |

**SDK note:** .NET SDK 10 rejects `dotnet new <template> -f net8.0`. Create with the default template, then overwrite the `.csproj` to target `net8.0` (as in prior plans). Solution is `OpenEcu.slnx`.

---

### Task 1: IObdSession abstraction

**Files:**
- Create: `src/OpenEcu.Core/Obd/IObdSession.cs`
- Modify: `src/OpenEcu.Core/Obd/KLineObdSession.cs` (implement the interface)
- Test: `tests/OpenEcu.Core.Tests/Obd/IObdSessionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Obd/IObdSessionTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class IObdSessionTests
{
    [Fact]
    public void KLineObdSession_implements_IObdSession()
    {
        typeof(IObdSession).IsAssignableFrom(typeof(KLineObdSession)).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter IObdSessionTests`
Expected: FAIL — `IObdSession` does not exist.

- [ ] **Step 3: Create the interface**

Create `src/OpenEcu.Core/Obd/IObdSession.cs`:
```csharp
namespace OpenEcu.Core.Obd;

/// <summary>A read-only OBD diagnostic session (implemented by KLineObdSession).</summary>
public interface IObdSession : IAsyncDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task<IReadOnlyList<byte>> ReadSupportedPidsAsync(CancellationToken ct = default);
    Task<PidReading> ReadPidAsync(byte pid, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ReadDtcsAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement it on KLineObdSession**

In `src/OpenEcu.Core/Obd/KLineObdSession.cs`, change the class declaration from:
```csharp
public sealed class KLineObdSession : IAsyncDisposable
```
to:
```csharp
public sealed class KLineObdSession : IObdSession
```
(All required members — `IsConnected`, `ConnectAsync`, `ReadSupportedPidsAsync`, `ReadPidAsync`, `ReadDtcsAsync`, `DisposeAsync` — already exist with matching signatures, so no other change is needed.)

- [ ] **Step 5: Run test to verify it passes + full suite still green**

Run: `dotnet test`
Expected: PASS — IObdSessionTests passes; all 71 prior tests still pass (72 passed, 1 skipped).

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Obd/IObdSession.cs src/OpenEcu.Core/Obd/KLineObdSession.cs tests/OpenEcu.Core.Tests/Obd/IObdSessionTests.cs
git commit -m "feat: IObdSession abstraction (implemented by KLineObdSession)"
```

---

### Task 2: LoggingTransport

**Files:**
- Create: `src/OpenEcu.Core/Transport/LoggingTransport.cs`
- Test: `tests/OpenEcu.Core.Tests/Transport/LoggingTransportTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Transport/LoggingTransportTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Transport;

public class LoggingTransportTests
{
    [Fact]
    public async Task Raises_BytesWritten_and_passes_through_to_inner()
    {
        var inner = new SimulatedTransport();
        await inner.OpenAsync();
        var log = new LoggingTransport(inner);
        byte[]? seen = null;
        log.BytesWritten += b => seen = b;

        await log.WriteAsync(new byte[] { 0x01, 0x02 });

        seen.Should().Equal(0x01, 0x02);
        inner.Written.Should().Equal(0x01, 0x02);
    }

    [Fact]
    public async Task Raises_BytesRead_with_only_the_bytes_actually_read()
    {
        var inner = new SimulatedTransport();
        inner.EnqueueResponse(new byte[] { 0xAA, 0xBB });
        await inner.OpenAsync();
        var log = new LoggingTransport(inner);
        byte[]? seen = null;
        log.BytesRead += b => seen = b;

        var buffer = new byte[8];
        int n = await log.ReadAsync(buffer);

        n.Should().Be(2);
        seen.Should().Equal(0xAA, 0xBB); // not the full 8-byte buffer
    }

    [Fact]
    public async Task Does_not_raise_BytesRead_on_empty_read()
    {
        var inner = new SimulatedTransport();
        await inner.OpenAsync();
        var log = new LoggingTransport(inner);
        bool raised = false;
        log.BytesRead += _ => raised = true;

        int n = await log.ReadAsync(new byte[4]);

        n.Should().Be(0);
        raised.Should().BeFalse();
    }

    [Fact]
    public void IsOpen_reflects_inner()
    {
        var inner = new SimulatedTransport();
        new LoggingTransport(inner).IsOpen.Should().Be(inner.IsOpen);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter LoggingTransportTests`
Expected: FAIL — `LoggingTransport` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.Core/Transport/LoggingTransport.cs`:
```csharp
namespace OpenEcu.Core.Transport;

/// <summary>
/// Pass-through IEcuTransport decorator that raises events for every byte block written or
/// read. Used to feed a raw protocol console without coupling it to the session.
/// </summary>
public sealed class LoggingTransport : IEcuTransport
{
    private readonly IEcuTransport _inner;

    public LoggingTransport(IEcuTransport inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public event Action<byte[]>? BytesWritten;
    public event Action<byte[]>? BytesRead;

    public bool IsOpen => _inner.IsOpen;
    public Task OpenAsync(CancellationToken ct = default) => _inner.OpenAsync(ct);
    public Task CloseAsync() => _inner.CloseAsync();
    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        await _inner.WriteAsync(data, ct);
        BytesWritten?.Invoke(data.ToArray());
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int n = await _inner.ReadAsync(buffer, ct);
        if (n > 0)
            BytesRead?.Invoke(buffer.Slice(0, n).ToArray());
        return n;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter LoggingTransportTests`
Expected: PASS (4 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Transport/LoggingTransport.cs tests/OpenEcu.Core.Tests/Transport/LoggingTransportTests.cs
git commit -m "feat: LoggingTransport decorator (Tx/Rx events for the console)"
```

---

### Task 3: Scaffold OpenEcu.App + test project

**Files:**
- Create: `src/OpenEcu.App/OpenEcu.App.csproj`
- Create: `tests/OpenEcu.App.Tests/OpenEcu.App.Tests.csproj`

- [ ] **Step 1: Create the projects**

Run from repo root:
```bash
dotnet new classlib -n OpenEcu.App -o src/OpenEcu.App
dotnet new xunit -n OpenEcu.App.Tests -o tests/OpenEcu.App.Tests
rm src/OpenEcu.App/Class1.cs
rm tests/OpenEcu.App.Tests/UnitTest1.cs
```

- [ ] **Step 2: Overwrite the library csproj**

Replace `src/OpenEcu.App/OpenEcu.App.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>12</LangVersion>
    <RootNamespace>OpenEcu.App</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenEcu.Core\OpenEcu.Core.csproj" />
    <ProjectReference Include="..\OpenEcu.Transport.Serial\OpenEcu.Transport.Serial.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Set the test csproj framework and references**

Edit `tests/OpenEcu.App.Tests/OpenEcu.App.Tests.csproj`: set `<TargetFramework>net8.0</TargetFramework>` (replace whatever the template generated). Then run:
```bash
dotnet add tests/OpenEcu.App.Tests reference src/OpenEcu.App/OpenEcu.App.csproj
dotnet add tests/OpenEcu.App.Tests reference src/OpenEcu.Core/OpenEcu.Core.csproj
dotnet add tests/OpenEcu.App.Tests package AwesomeAssertions
```

- [ ] **Step 4: Add both to the solution**

```bash
dotnet sln add src/OpenEcu.App/OpenEcu.App.csproj
dotnet sln add tests/OpenEcu.App.Tests/OpenEcu.App.Tests.csproj
```

- [ ] **Step 5: Verify the solution builds and all tests still pass**

Run: `dotnet test`
Expected: build succeeds; prior tests green; the new test project contributes 0 tests so far.

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.App tests/OpenEcu.App.Tests OpenEcu.slnx
git commit -m "chore: scaffold OpenEcu.App logic library + tests"
```

---

### Task 4: MetricDescriptor + MetricCatalog

**Files:**
- Create: `src/OpenEcu.App/Model/MetricDescriptor.cs`
- Test: `tests/OpenEcu.App.Tests/MetricCatalogTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/MetricCatalogTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Model;
using Xunit;

namespace OpenEcu.App.Tests;

public class MetricCatalogTests
{
    [Fact]
    public void Known_pid_has_display_metadata()
    {
        var d = MetricCatalog.For(0x0C); // RPM
        d.Name.Should().Be("Engine RPM");
        d.Unit.Should().Be("rpm");
        d.Min.Should().Be(0);
        d.Max.Should().Be(12000);
    }

    [Fact]
    public void Coolant_range_supports_below_zero()
    {
        var d = MetricCatalog.For(0x05);
        d.Min.Should().Be(-40);
        d.Max.Should().Be(150);
    }

    [Fact]
    public void Unknown_pid_returns_a_safe_fallback()
    {
        var d = MetricCatalog.For(0xAB);
        d.Name.Should().Be("PID AB");
        d.Min.Should().Be(0);
        d.Max.Should().Be(255);
        MetricCatalog.IsKnown(0xAB).Should().BeFalse();
        MetricCatalog.IsKnown(0x0C).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter MetricCatalogTests`
Expected: FAIL — `MetricCatalog` / `MetricDescriptor` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/Model/MetricDescriptor.cs`:
```csharp
namespace OpenEcu.App.Model;

/// <summary>Display metadata for one OBD metric: how to label, scale, and color it.</summary>
public sealed record MetricDescriptor(byte Pid, string Name, string Unit, double Min, double Max, string Accent);

/// <summary>Catalog of known PIDs with gauge metadata; safe fallback for unknown PIDs.</summary>
public static class MetricCatalog
{
    private static readonly IReadOnlyDictionary<byte, MetricDescriptor> Map = new Dictionary<byte, MetricDescriptor>
    {
        [0x04] = new(0x04, "Engine load", "%", 0, 100, "teal"),
        [0x05] = new(0x05, "Coolant temperature", "°C", -40, 150, "teal"),
        [0x06] = new(0x06, "Short-term fuel trim", "%", -100, 100, "teal"),
        [0x07] = new(0x07, "Long-term fuel trim", "%", -100, 100, "teal"),
        [0x0B] = new(0x0B, "Intake manifold pressure", "kPa", 0, 255, "teal"),
        [0x0C] = new(0x0C, "Engine RPM", "rpm", 0, 12000, "blue"),
        [0x0D] = new(0x0D, "Vehicle speed", "km/h", 0, 300, "blue"),
        [0x0E] = new(0x0E, "Timing advance", "°", -64, 64, "teal"),
        [0x0F] = new(0x0F, "Intake air temperature", "°C", -40, 150, "teal"),
        [0x11] = new(0x11, "Throttle position", "%", 0, 100, "teal"),
        [0x14] = new(0x14, "O2 sensor voltage", "V", 0, 1.275, "teal"),
    };

    public static MetricDescriptor For(byte pid) =>
        Map.TryGetValue(pid, out var d) ? d : new MetricDescriptor(pid, $"PID {pid:X2}", "", 0, 255, "teal");

    public static bool IsKnown(byte pid) => Map.ContainsKey(pid);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter MetricCatalogTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/Model/MetricDescriptor.cs tests/OpenEcu.App.Tests/MetricCatalogTests.cs
git commit -m "feat: MetricDescriptor + catalog (gauge metadata per PID)"
```

---

### Task 5: DashboardLayout

**Files:**
- Create: `src/OpenEcu.App/Model/DashboardLayout.cs`
- Test: `tests/OpenEcu.App.Tests/DashboardLayoutTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/DashboardLayoutTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Model;
using Xunit;

namespace OpenEcu.App.Tests;

public class DashboardLayoutTests
{
    [Fact]
    public void Default_heroes_are_rpm_then_coolant()
    {
        DashboardLayout.Default.HeroPids.Should().Equal((byte)0x0C, (byte)0x05);
    }

    [Fact]
    public void Default_tiles_cover_the_other_common_sensors()
    {
        DashboardLayout.Default.TilePids.Should()
            .Contain(new byte[] { 0x11, 0x0F, 0x04, 0x0E, 0x14, 0x0D });
    }

    [Fact]
    public void Heroes_and_tiles_do_not_overlap()
    {
        var layout = DashboardLayout.Default;
        layout.HeroPids.Should().NotIntersectWith(layout.TilePids);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DashboardLayoutTests`
Expected: FAIL — `DashboardLayout` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/Model/DashboardLayout.cs`:
```csharp
namespace OpenEcu.App.Model;

/// <summary>
/// Which PIDs appear as hero gauges vs. tiles. Data-driven so v2 can let users edit + persist
/// it without touching the views.
/// </summary>
public sealed record DashboardLayout(IReadOnlyList<byte> HeroPids, IReadOnlyList<byte> TilePids)
{
    public static DashboardLayout Default { get; } = new(
        HeroPids: new byte[] { 0x0C, 0x05 },                               // RPM, coolant
        TilePids: new byte[] { 0x11, 0x0F, 0x04, 0x0E, 0x14, 0x0D });      // throttle, intake, load, timing, O2, speed
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter DashboardLayoutTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/Model/DashboardLayout.cs tests/OpenEcu.App.Tests/DashboardLayoutTests.cs
git commit -m "feat: data-driven DashboardLayout with default hero/tile slots"
```

---

### Task 6: MetricViewModel

**Files:**
- Create: `src/OpenEcu.App/ViewModels/MetricViewModel.cs`
- Test: `tests/OpenEcu.App.Tests/MetricViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.App.Tests/MetricViewModelTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Model;
using OpenEcu.App.ViewModels;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.App.Tests;

public class MetricViewModelTests
{
    private static MetricViewModel ForRpm() => new(MetricCatalog.For(0x0C));

    [Fact]
    public void Exposes_descriptor_metadata()
    {
        var vm = ForRpm();
        vm.Pid.Should().Be(0x0C);
        vm.Name.Should().Be("Engine RPM");
        vm.Unit.Should().Be("rpm");
        vm.Maximum.Should().Be(12000);
    }

    [Fact]
    public void Update_sets_value_display_and_clears_stale()
    {
        var vm = ForRpm();
        vm.Update(new PidReading(0x0C, "Engine RPM", 1080, "rpm", new byte[] { 0x10, 0xE0 }));

        vm.Value.Should().Be(1080);
        vm.IsStale.Should().BeFalse();
        vm.Display.Should().Be("1080 rpm");
        vm.Raw.Should().Equal(0x10, 0xE0);
    }

    [Fact]
    public void Null_value_shows_dash_and_marks_stale()
    {
        var vm = ForRpm();
        vm.Update(new PidReading(0x0C, "Engine RPM", null, "rpm", Array.Empty<byte>()));

        vm.Value.Should().BeNull();
        vm.IsStale.Should().BeTrue();
        vm.Display.Should().Be("—");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter MetricViewModelTests`
Expected: FAIL — `MetricViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.App/ViewModels/MetricViewModel.cs`:
```csharp
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
    private byte[] _raw = Array.Empty<byte>();

    public string Display => Value is null ? "—" : $"{Value:0.##} {Unit}".Trim();

    /// <summary>Apply a fresh reading from the ECU.</summary>
    public void Update(PidReading reading)
    {
        Raw = reading.Raw;
        Value = reading.Value;
        IsStale = reading.Value is null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter MetricViewModelTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.App/ViewModels/MetricViewModel.cs tests/OpenEcu.App.Tests/MetricViewModelTests.cs
git commit -m "feat: MetricViewModel (observable live reading)"
```

---

### Task 7: LiveDataService + FakeObdSession

**Files:**
- Create: `tests/OpenEcu.App.Tests/FakeObdSession.cs`
- Create: `src/OpenEcu.App/Services/LiveDataService.cs`
- Test: `tests/OpenEcu.App.Tests/LiveDataServiceTests.cs`

- [ ] **Step 1: Write the test double**

Create `tests/OpenEcu.App.Tests/FakeObdSession.cs`:
```csharp
using OpenEcu.Core.Obd;

namespace OpenEcu.App.Tests;

/// <summary>In-memory IObdSession for testing LiveDataService.</summary>
public sealed class FakeObdSession : IObdSession
{
    public bool IsConnected { get; private set; }
    public bool ThrowOnConnect { get; set; }
    public List<byte> Supported { get; } = new();
    public Dictionary<byte, PidReading> Readings { get; } = new();
    public HashSet<byte> FailingPids { get; } = new();
    public IReadOnlyList<string> Dtcs { get; set; } = Array.Empty<string>();
    public int DtcCalls { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (ThrowOnConnect) throw new InvalidOperationException("connect failed");
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<byte>> ReadSupportedPidsAsync(CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<byte>)Supported);

    public Task<PidReading> ReadPidAsync(byte pid, CancellationToken ct = default)
    {
        if (FailingPids.Contains(pid))
            throw new IOException($"PID {pid:X2} read failed");
        var reading = Readings.TryGetValue(pid, out var r)
            ? r
            : new PidReading(pid, $"PID {pid:X2}", 0, "", Array.Empty<byte>());
        return Task.FromResult(reading);
    }

    public Task<IReadOnlyList<string>> ReadDtcsAsync(CancellationToken ct = default)
    {
        DtcCalls++;
        return Task.FromResult(Dtcs);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/OpenEcu.App.Tests/LiveDataServiceTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.App.Model;
using OpenEcu.App.Services;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.App.Tests;

public class LiveDataServiceTests
{
    private static PidReading Rpm(int v) => new(0x0C, "Engine RPM", v, "rpm", new byte[] { 0, 0 });

    [Fact]
    public async Task ConnectAsync_builds_metrics_for_supported_known_pids()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05, 0x11, 0x20 }); // 0x20 is a chain bit
        var svc = new LiveDataService(fake);

        await svc.ConnectAsync();

        svc.State.Should().Be(ConnectionState.Connected);
        svc.Metrics.Select(m => m.Pid).Should().Equal((byte)0x0C, (byte)0x05, (byte)0x11); // no 0x20
    }

    [Fact]
    public async Task ConnectAsync_failure_sets_error_state_and_rethrows()
    {
        var fake = new FakeObdSession { ThrowOnConnect = true };
        var svc = new LiveDataService(fake);

        var act = async () => await svc.ConnectAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        svc.State.Should().Be(ConnectionState.Error);
    }

    [Fact]
    public async Task PollOnceAsync_updates_metric_values_and_heartbeat()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05 });
        fake.Readings[0x0C] = Rpm(1080);
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();

        await svc.PollOnceAsync();

        svc.Metrics.First(m => m.Pid == 0x0C).Value.Should().Be(1080);
        svc.LastUpdate.Should().BeAfter(DateTime.MinValue);
    }

    [Fact]
    public async Task Hero_pids_are_polled_every_cycle()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05, 0x11, 0x0F, 0x04 }); // 2 heroes + 3 tiles
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();

        // Heroes (RPM 0x0C, coolant 0x05) get a fresh value on each of two cycles even though
        // only one tile is polled per cycle.
        fake.Readings[0x0C] = Rpm(1000);
        await svc.PollOnceAsync();
        fake.Readings[0x0C] = Rpm(2000);
        await svc.PollOnceAsync();

        svc.Metrics.First(m => m.Pid == 0x0C).Value.Should().Be(2000);
    }

    [Fact]
    public async Task A_failing_pid_is_marked_stale_without_stalling_others()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05 });
        fake.Readings[0x0C] = Rpm(900);
        fake.FailingPids.Add(0x05);
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();

        await svc.PollOnceAsync(); // must not throw

        svc.Metrics.First(m => m.Pid == 0x05).IsStale.Should().BeTrue();
        svc.Metrics.First(m => m.Pid == 0x0C).Value.Should().Be(900);
    }

    [Fact]
    public async Task Dtcs_refresh_on_first_cycle_then_respect_the_interval()
    {
        var fake = new FakeObdSession();
        fake.Supported.Add(0x0C);
        fake.Dtcs = new[] { "P1502" };
        var svc = new LiveDataService(fake, dtcInterval: TimeSpan.FromSeconds(30));
        await svc.ConnectAsync();

        await svc.PollOnceAsync(); // first cycle: reads DTCs
        await svc.PollOnceAsync(); // within interval: does NOT read again

        fake.DtcCalls.Should().Be(1);
        svc.Dtcs.Should().Equal("P1502");
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter LiveDataServiceTests`
Expected: FAIL — `LiveDataService` / `ConnectionState` do not exist.

- [ ] **Step 4: Write the implementation**

Create `src/OpenEcu.App/Services/LiveDataService.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenEcu.App.Model;
using OpenEcu.App.ViewModels;
using OpenEcu.Core.Obd;

namespace OpenEcu.App.Services;

public enum ConnectionState { Disconnected, Connecting, Connected, Error }

/// <summary>
/// Owns an IObdSession, connects, and runs a weighted polling loop that keeps the metric
/// view-models live. Hero PIDs are polled every cycle; the rest are interleaved one per cycle.
/// DTCs refresh on a fixed cadence. UI-agnostic: callers marshal updates to their UI thread.
/// </summary>
public sealed partial class LiveDataService : ObservableObject, IAsyncDisposable
{
    private readonly IObdSession _session;
    private readonly DashboardLayout _layout;
    private readonly TimeSpan _dtcInterval;
    private readonly Dictionary<byte, MetricViewModel> _byPid = new();
    private int _tileCursor;
    private DateTime _lastDtc = DateTime.MinValue;

    public LiveDataService(IObdSession session, DashboardLayout? layout = null, TimeSpan? dtcInterval = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _layout = layout ?? DashboardLayout.Default;
        _dtcInterval = dtcInterval ?? TimeSpan.FromSeconds(5);
    }

    public ObservableCollection<MetricViewModel> Metrics { get; } = new();

    [ObservableProperty] private ConnectionState _state = ConnectionState.Disconnected;
    [ObservableProperty] private IReadOnlyList<string> _dtcs = Array.Empty<string>();
    [ObservableProperty] private DateTime _lastUpdate;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        State = ConnectionState.Connecting;
        try
        {
            await _session.ConnectAsync(ct);
            IReadOnlyList<byte> supported = await _session.ReadSupportedPidsAsync(ct);

            Metrics.Clear();
            _byPid.Clear();
            _tileCursor = 0;
            foreach (byte pid in supported)
            {
                if (pid is 0x20 or 0x40) continue; // bitmask chain PIDs, not data
                var vm = new MetricViewModel(MetricCatalog.For(pid));
                Metrics.Add(vm);
                _byPid[pid] = vm;
            }
            State = ConnectionState.Connected;
        }
        catch
        {
            State = ConnectionState.Error;
            throw;
        }
    }

    /// <summary>Runs one weighted poll cycle. Call repeatedly (see RunAsync).</summary>
    public async Task PollOnceAsync(CancellationToken ct = default)
    {
        foreach (byte pid in _layout.HeroPids)
            await PollPidAsync(pid, ct);

        if (NextTilePid() is byte tile)
            await PollPidAsync(tile, ct);

        if (DateTime.UtcNow - _lastDtc >= _dtcInterval)
        {
            try
            {
                Dtcs = await _session.ReadDtcsAsync(ct);
                _lastDtc = DateTime.UtcNow;
            }
            catch { /* transient; retry next cadence */ }
        }

        LastUpdate = DateTime.UtcNow;
    }

    /// <summary>Continuous loop until cancelled. Callers run this on a background task.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await PollOnceAsync(ct);
            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private byte? NextTilePid()
    {
        var heroes = _layout.HeroPids;
        var tiles = _byPid.Keys.Where(p => !heroes.Contains(p)).OrderBy(p => p).ToList();
        if (tiles.Count == 0) return null;
        byte pid = tiles[_tileCursor % tiles.Count];
        _tileCursor++;
        return pid;
    }

    private async Task PollPidAsync(byte pid, CancellationToken ct)
    {
        if (!_byPid.TryGetValue(pid, out var vm)) return;
        try { vm.Update(await _session.ReadPidAsync(pid, ct)); }
        catch { vm.IsStale = true; }
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter LiveDataServiceTests`
Expected: PASS (6 passed).

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: PASS — plans 1–7 (71) + Task 1 (1) + Task 2 (4) + Task 4 (3) + Task 5 (3) + Task 6 (3) + Task 7 (6) = 91 passed, 1 skipped.

- [ ] **Step 7: Commit**

```bash
git add src/OpenEcu.App/Services/LiveDataService.cs tests/OpenEcu.App.Tests/FakeObdSession.cs tests/OpenEcu.App.Tests/LiveDataServiceTests.cs
git commit -m "feat: LiveDataService weighted polling engine (connect, poll, DTC cadence, failure isolation)"
```

---

## Self-Review

**Spec coverage:**
- `IObdSession` (decouples polling from the concrete session) → Task 1 ✅
- `LoggingTransport` (spec §4, console feed) → Task 2 ✅
- `MetricDescriptor` catalog (spec §6) → Task 4 ✅
- Data-driven `DashboardLayout` (spec §6/§7) → Task 5 ✅
- `MetricViewModel` (spec §6) → Task 6 ✅
- `LiveDataService` — connect, weighted round-robin (heroes every cycle), DTC cadence, per-PID failure isolation, heartbeat (`LastUpdate`) (spec §4/§8) → Task 7 ✅
- **Deliberately deferred to plan 9:** the `OpenEcu.Desktop` Avalonia executable — `RadialGauge` control, `MainWindow`/Dashboard/Diagnostics/Console views, light/dark theme + accent picker + settings persistence, `DashboardViewModel`/`DiagnosticsViewModel`/`ConsoleViewModel`/`MainViewModel`, UI-thread marshalling of the poll loop, and on-bike verification. (Heat maps + difference reporting remain v2 per the spec.)

**Placeholder scan:** No TBD/TODO. Every step has complete code and exact expected counts.

**Type consistency:** `IObdSession` members match `KLineObdSession`'s existing signatures; `MetricDescriptor(Pid,Name,Unit,Min,Max,Accent)`, `MetricCatalog.For/IsKnown`, `DashboardLayout(HeroPids,TilePids)/.Default`, `MetricViewModel(MetricDescriptor)` with `.Update(PidReading)`/`.Value`/`.IsStale`/`.Display`/`.Raw`, and `LiveDataService(IObdSession, DashboardLayout?, TimeSpan?)` with `.ConnectAsync`/`.PollOnceAsync`/`.RunAsync`/`.Metrics`/`.State`/`.Dtcs`/`.LastUpdate` are used consistently across tasks. `PidReading(Pid,Name,Value,Unit,Raw)` matches plan 6. Tests use `using AwesomeAssertions;`.
