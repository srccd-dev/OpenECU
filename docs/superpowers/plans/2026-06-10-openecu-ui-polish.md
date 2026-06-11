# OpenECU UI Polish (theme-aware tach + static flicker) — Implementation Plan (Plan 12)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two polish fixes found in the running app's screenshots: make the `AnalogTachometer` text/needle **theme-aware** (currently hard-coded near-white → faint on the light theme), and eliminate the **static-flag flicker** on hero PIDs when the engine is off (they flash "static" mid-cycle before being cleared).

**Architecture:** The static fix moves the `IsStatic` decision out of `MetricViewModel.Update` (which set-then-cleared, visible during the poll loop's awaits) into a single end-of-cycle computation in `LiveDataService` (`IsStatic = repeated && engineRunning`). The tach gains a `Foreground` styled property bound to a theme brush.

**Tech Stack:** .NET 8, Avalonia 11.0.10, xUnit, **AwesomeAssertions**. Builds on plans 1–11.

**Prerequisite:** Plans 1–11 on `main`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.App/ViewModels/MetricViewModel.cs` | **Modify:** `Update` sets `Repeated`, not `IsStatic` |
| `src/OpenEcu.App/Services/LiveDataService.cs` | **Modify:** compute `IsStatic` once per cycle (repeated && running) |
| `tests/OpenEcu.App.Tests/MetricViewModelTests.cs` | **Modify:** the two static tests now assert `Repeated` |
| `src/OpenEcu.Desktop/Controls/AnalogTachometer.cs` | **Modify:** theme-aware `Foreground` for text/needle |
| `src/OpenEcu.Desktop/Views/RacingDashboardView.axaml` | **Modify:** bind the tach `Foreground` to a theme brush |

---

### Task 1: Fix the static-flag flicker (compute IsStatic once per cycle)

**Files:**
- Modify: `src/OpenEcu.App/ViewModels/MetricViewModel.cs`
- Modify: `src/OpenEcu.App/Services/LiveDataService.cs`
- Modify: `tests/OpenEcu.App.Tests/MetricViewModelTests.cs`

- [ ] **Step 1: Update the MetricViewModel tests to assert `Repeated`**

In `tests/OpenEcu.App.Tests/MetricViewModelTests.cs`, replace the two tests `Repeated_identical_readings_flag_static` and `A_changed_reading_clears_static` with:
```csharp
    [Fact]
    public void Repeated_identical_readings_set_Repeated()
    {
        var vm = ForRpm();
        var reading = new OpenEcu.Core.Obd.PidReading(0x0C, "Engine RPM", 1000, "rpm", new byte[] { 0x0F, 0xA0 });

        for (int i = 0; i < 8; i++) vm.Update(reading);

        vm.Repeated.Should().BeTrue();
    }

    [Fact]
    public void A_changed_reading_clears_Repeated()
    {
        var vm = ForRpm();
        var a = new OpenEcu.Core.Obd.PidReading(0x0C, "Engine RPM", 1000, "rpm", new byte[] { 0x0F, 0xA0 });
        for (int i = 0; i < 8; i++) vm.Update(a);
        vm.Repeated.Should().BeTrue();

        vm.Update(new OpenEcu.Core.Obd.PidReading(0x0C, "Engine RPM", 1200, "rpm", new byte[] { 0x12, 0xC0 }));

        vm.Repeated.Should().BeFalse();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter MetricViewModelTests`
Expected: FAIL — `Repeated` does not exist.

- [ ] **Step 3: Make `Update` set `Repeated` instead of `IsStatic`**

In `src/OpenEcu.App/ViewModels/MetricViewModel.cs`, replace the `Update` method with:
```csharp
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
```
(`IsStatic` remains an `[ObservableProperty]`, now set by `LiveDataService` — keep the `_isStatic` field.)

- [ ] **Step 4: Compute `IsStatic` once per cycle in LiveDataService**

In `src/OpenEcu.App/Services/LiveDataService.cs`, replace the `SuppressStaticUnlessEngineRunning` method with:
```csharp
    private const double EngineRunningRpm = 400;

    // Compute the static flag once at the end of the cycle so it never flickers mid-poll.
    // A PID is "static" only if it has repeated AND the engine is running (else everything
    // looks static at idle/off, which is meaningless).
    private void RefreshStaticFlags()
    {
        bool running = _byPid.TryGetValue(0x0C, out var rpm) && rpm.Value is double v && v >= EngineRunningRpm;
        foreach (MetricViewModel m in Metrics)
            m.IsStatic = m.Repeated && running;
    }
```
And in `PollOnceAsync`, replace the call `SuppressStaticUnlessEngineRunning();` with:
```csharp
        RefreshStaticFlags();
```
(If the previous plan's constant `EngineRunningRpm` already exists, keep a single copy — do not declare it twice.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "MetricViewModelTests|LiveDataServiceTests"`
Expected: PASS — the MetricViewModel `Repeated` tests pass, and the existing LiveDataService static tests (`Static_is_suppressed_when_engine_is_off`, `Static_applies_when_engine_running`) still pass.

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.App/ViewModels/MetricViewModel.cs src/OpenEcu.App/Services/LiveDataService.cs tests/OpenEcu.App.Tests/MetricViewModelTests.cs
git commit -m "fix: compute IsStatic once per cycle (no mid-poll flicker)"
```

---

### Task 2: Theme-aware AnalogTachometer text + needle

**Files:**
- Modify: `src/OpenEcu.Desktop/Controls/AnalogTachometer.cs`
- Modify: `src/OpenEcu.Desktop/Views/RacingDashboardView.axaml`

- [ ] **Step 1: Add a `Foreground` property and use it for text/needle/hub**

In `src/OpenEcu.Desktop/Controls/AnalogTachometer.cs`, add a `Foreground` styled property next to `AccentProperty`:
```csharp
    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<AnalogTachometer, IBrush>(nameof(Foreground), Brushes.White);
```
Add it to the `AffectsRender` call and add the CLR property:
```csharp
    public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
```
(So the static ctor becomes `AffectsRender<AnalogTachometer>(ValueProperty, MaxRpmProperty, RedlineRpmProperty, AccentProperty, ForegroundProperty);`.)

Then in `Render`, replace the three hard-coded white usages with `Foreground`:
- the needle pen: `new Pen(Foreground, 4) { LineCap = PenLineCap.Round }`
- the hub dot: `ctx.DrawEllipse(Foreground, null, c, 6, 6);`
- the digital rpm text brush: pass `Foreground` instead of `new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7))`

The tick marks, labels, and the small "rpm" caption keep their existing mid-gray (readable in both themes); the redline stays red and the value arc stays `Accent`.

- [ ] **Step 2: Bind the tach Foreground to a theme brush**

In `src/OpenEcu.Desktop/Views/RacingDashboardView.axaml`, add `Foreground` to the `AnalogTachometer` element:
```xml
    <c:AnalogTachometer Grid.Column="0" Width="320" Height="320"
                        Value="{Binding Rpm.Value, FallbackValue=0}"
                        MaxRpm="{Binding Tach.MaxRpm}" RedlineRpm="{Binding Tach.RedlineRpm}"
                        Accent="{DynamicResource AppAccentBrush}"
                        Foreground="{DynamicResource SystemControlForegroundBaseHighBrush}" />
```

- [ ] **Step 3: Build**

Run: `dotnet build src/OpenEcu.Desktop`
Expected: build succeeds. (Visual: the digital rpm + needle are now dark on the light theme, light on the dark theme.)

- [ ] **Step 4: Run the full suite + commit**

Run: `dotnet test`
Expected: 119 passed, 1 skipped (unchanged count — Task 1 modified existing tests rather than adding net-new).

```bash
git add src/OpenEcu.Desktop/Controls/AnalogTachometer.cs src/OpenEcu.Desktop/Views/RacingDashboardView.axaml
git commit -m "fix: theme-aware AnalogTachometer text and needle"
```

---

### Task 3: Manual verification (the human)

```bash
dotnet run --project src/OpenEcu.Desktop
```

- [ ] Racing tab, **light** theme: the digital rpm and needle are now crisp/dark (no longer washed out).
- [ ] Diagnostics, engine **off**: no "static" markers flash on RPM/Coolant.
- [ ] Diagnostics, engine **running**: the frozen fuel-trim PIDs are flagged "static" steadily (no flicker).

---

## Self-Review

**Coverage:** theme-aware tach (Task 2); static-flag flicker removed by once-per-cycle computation gated on engine-running (Task 1). Both are the exact issues seen in the screenshots.

**Placeholder scan:** No TBD/TODO. Test count stays 119 + 1 skipped because Task 1 rewrites two existing tests in place (asserting `Repeated`) and the LiveDataService static tests are unchanged.

**Type consistency:** `MetricViewModel.Repeated` (new, set in `Update`) + `IsStatic` (now set by `LiveDataService.RefreshStaticFlags`); `AnalogTachometer.Foreground` (new styled property) used in `Render` and bound in `RacingDashboardView`. Reuses `EngineRunningRpm`, `_byPid`, `Metrics` from plan 11. Tests use `using AwesomeAssertions;`.
