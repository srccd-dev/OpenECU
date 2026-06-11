# OpenECU Mode 04 Clear-Codes — Implementation Plan (Plan 13)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add OBD-II **Mode 04 (clear stored DTCs)** end to end: `IObdSession.ClearDtcsAsync` (implemented by `KLineObdSession`), a `LiveDataService.ClearDtcsAsync` that clears then re-reads, and an enabled "Clear codes" button in the Diagnostics view.

**Architecture:** Standard OBD-II service `0x04` (no proprietary data, safe — it just clears stored emissions codes). It rides the existing `KLineObdSession.RequestAsync` framing; the UI wires a command through `LiveDataService`.

**Tech Stack:** .NET 8, Avalonia 11.0.10, xUnit, **AwesomeAssertions**. Builds on plans 1–12.

**Prerequisite:** Plans 1–12 on `main` (`IObdSession`, `KLineObdSession`, `LiveDataService`, `DiagnosticsViewModel`, `FakeObdSession`, `FakeEcu`).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.Core/Obd/IObdSession.cs` | **Modify:** add `ClearDtcsAsync` |
| `src/OpenEcu.Core/Obd/KLineObdSession.cs` | **Modify:** implement `ClearDtcsAsync` (service 0x04) |
| `src/OpenEcu.App/Services/LiveDataService.cs` | **Modify:** `ClearDtcsAsync` (clear + re-read) |
| `src/OpenEcu.App/ViewModels/DiagnosticsViewModel.cs` | **Modify:** enable clear + `ClearCodesCommand` |
| `src/OpenEcu.Desktop/Views/DiagnosticsView.axaml` | **Modify:** wire the button |
| `tests/OpenEcu.App.Tests/FakeObdSession.cs` | **Modify:** implement `ClearDtcsAsync` |
| tests | new tests for each |

---

### Task 1: IObdSession.ClearDtcsAsync + KLineObdSession

**Files:**
- Modify: `src/OpenEcu.Core/Obd/IObdSession.cs`
- Modify: `src/OpenEcu.Core/Obd/KLineObdSession.cs`
- Test: `tests/OpenEcu.Core.Tests/Obd/KLineObdSessionTests.cs`

- [ ] **Step 1: Write the failing test (append to KLineObdSessionTests)**

Add inside `KLineObdSessionTests`, before the closing brace:
```csharp
    [Fact]
    public async Task ClearDtcsAsync_sends_mode_04_and_accepts_the_positive_response()
    {
        // Mode 04 positive response is service id 0x44.
        var ecu = new FakeEcu(new()
        {
            ["04"] = new byte[] { 0x48, 0x6B, 0xD1, 0x44, 0xC8 }, // 0x48+0x6B+0xD1+0x44 = 0x1C8 -> 0xC8
        }, connected: true);
        await ecu.OpenAsync();
        var session = new KLineObdSession(ecu, ecu, delay: NoDelay);

        await session.ClearDtcsAsync(); // must not throw
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter KLineObdSessionTests`
Expected: FAIL — `ClearDtcsAsync` does not exist.

- [ ] **Step 3: Add to the interface**

In `src/OpenEcu.Core/Obd/IObdSession.cs`, add inside the interface:
```csharp
    /// <summary>Clears stored diagnostic trouble codes (OBD-II Mode 04).</summary>
    Task ClearDtcsAsync(CancellationToken ct = default);
```

- [ ] **Step 4: Implement on KLineObdSession**

In `src/OpenEcu.Core/Obd/KLineObdSession.cs`, add the method (e.g. after `ReadDtcsAsync`):
```csharp
    public async Task ClearDtcsAsync(CancellationToken ct = default)
    {
        ObdResponse resp = await RequestAsync(new byte[] { 0x04 }, ct);
        if (resp.ServiceId != 0x44)
            throw new InvalidDataException($"Mode 04 (clear codes) rejected: SID 0x{resp.ServiceId:X2}.");
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter KLineObdSessionTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Obd/IObdSession.cs src/OpenEcu.Core/Obd/KLineObdSession.cs tests/OpenEcu.Core.Tests/Obd/KLineObdSessionTests.cs
git commit -m "feat: ClearDtcsAsync (OBD-II Mode 04) on the session"
```

---

### Task 2: FakeObdSession + LiveDataService.ClearDtcsAsync

**Files:**
- Modify: `tests/OpenEcu.App.Tests/FakeObdSession.cs`
- Modify: `src/OpenEcu.App/Services/LiveDataService.cs`
- Test: `tests/OpenEcu.App.Tests/LiveDataServiceTests.cs`

- [ ] **Step 1: Add ClearDtcsAsync to the fake**

In `tests/OpenEcu.App.Tests/FakeObdSession.cs`, add a counter and the method (the fake clears its DTC list to mimic the ECU):
```csharp
    public int ClearCalls { get; private set; }

    public Task ClearDtcsAsync(CancellationToken ct = default)
    {
        ClearCalls++;
        Dtcs = Array.Empty<string>();
        return Task.CompletedTask;
    }
```

- [ ] **Step 2: Write the failing test (append to LiveDataServiceTests)**

Add inside `LiveDataServiceTests`, before the closing brace:
```csharp
    [Fact]
    public async Task ClearDtcsAsync_clears_on_the_ecu_and_refreshes()
    {
        var fake = new FakeObdSession();
        fake.Supported.Add(0x0C);
        fake.Dtcs = new[] { "P1502" };
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();
        await svc.PollOnceAsync();
        svc.Dtcs.Should().Equal("P1502");

        await svc.ClearDtcsAsync();

        fake.ClearCalls.Should().Be(1);
        svc.Dtcs.Should().BeEmpty();
    }
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "LiveDataServiceTests.ClearDtcsAsync_clears_on_the_ecu_and_refreshes"`
Expected: FAIL — `LiveDataService.ClearDtcsAsync` does not exist.

- [ ] **Step 4: Implement on LiveDataService**

In `src/OpenEcu.App/Services/LiveDataService.cs`, add (e.g. after `PollOnceAsync`):
```csharp
    /// <summary>Clears stored DTCs on the ECU, then re-reads them.</summary>
    public async Task ClearDtcsAsync(CancellationToken ct = default)
    {
        await _session.ClearDtcsAsync(ct);
        Dtcs = await _session.ReadDtcsAsync(ct);
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter LiveDataServiceTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add tests/OpenEcu.App.Tests/FakeObdSession.cs src/OpenEcu.App/Services/LiveDataService.cs tests/OpenEcu.App.Tests/LiveDataServiceTests.cs
git commit -m "feat: LiveDataService.ClearDtcsAsync (clear then re-read)"
```

---

### Task 3: DiagnosticsViewModel clear command + button

**Files:**
- Modify: `src/OpenEcu.App/ViewModels/DiagnosticsViewModel.cs`
- Modify: `src/OpenEcu.Desktop/Views/DiagnosticsView.axaml`
- Test: `tests/OpenEcu.App.Tests/DiagnosticsViewModelTests.cs`

- [ ] **Step 1: Update the test (replace the disabled-stub test)**

In `tests/OpenEcu.App.Tests/DiagnosticsViewModelTests.cs`, replace the test `Clear_codes_is_disabled_in_v1` with:
```csharp
    [Fact]
    public async Task Clear_codes_is_enabled_and_clears_through_the_service()
    {
        var ecu = new FakeObdSession();
        ecu.Supported.Add(0x0C);
        ecu.Dtcs = new[] { "P1502" };
        var svc = new LiveDataService(ecu);
        await svc.ConnectAsync();
        await svc.PollOnceAsync();

        var vm = new DiagnosticsViewModel(svc);
        vm.CanClearCodes.Should().BeTrue();

        await vm.ClearCodesCommand.ExecuteAsync(null);

        ecu.ClearCalls.Should().Be(1);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DiagnosticsViewModelTests`
Expected: FAIL — `ClearCodesCommand` does not exist / `CanClearCodes` is false.

- [ ] **Step 3: Update DiagnosticsViewModel**

Replace the contents of `src/OpenEcu.App/ViewModels/DiagnosticsViewModel.cs` with:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenEcu.App.Services;

namespace OpenEcu.App.ViewModels;

/// <summary>The full live PID table + fault codes, with Mode 04 clear-codes.</summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly LiveDataService _live;

    public DiagnosticsViewModel(LiveDataService live) => _live = live;

    public ObservableCollection<MetricViewModel> Metrics => _live.Metrics;
    public IReadOnlyList<string> Dtcs => _live.Dtcs;

    public bool CanClearCodes => true;

    [RelayCommand]
    private Task ClearCodesAsync() => _live.ClearDtcsAsync();
}
```

- [ ] **Step 4: Wire the button**

In `src/OpenEcu.Desktop/Views/DiagnosticsView.axaml`, change the Clear-codes button to bind the command:
```xml
        <Button Content="Clear codes" Command="{Binding ClearCodesCommand}" IsEnabled="{Binding CanClearCodes}" Margin="0,8,0,0" />
```

- [ ] **Step 5: Run tests + build**

Run: `dotnet test --filter DiagnosticsViewModelTests`
Expected: PASS.

Run: `dotnet build src/OpenEcu.Desktop`
Expected: build succeeds.

- [ ] **Step 6: Run the full suite + commit**

Run: `dotnet test`
Expected: PASS — 122 passed, 1 skipped (119 prior + Task1 1 + Task2 1 + Task3 modified one in place).

```bash
git add src/OpenEcu.App/ViewModels/DiagnosticsViewModel.cs src/OpenEcu.Desktop/Views/DiagnosticsView.axaml tests/OpenEcu.App.Tests/DiagnosticsViewModelTests.cs
git commit -m "feat: enabled Clear-codes button (Mode 04) in Diagnostics"
```

---

### Task 4: Manual verification (the human, on the bike)

```bash
dotnet run --project src/OpenEcu.Desktop
```

- [ ] Connect (cable, COM8). On **Diagnostics**, the **Clear codes** button is now enabled.
- [ ] Click it → the fault list refreshes. (Note: `P1502` is a live sensor fault, so it will likely return on the next drive cycle — that's expected; the clear itself succeeds.)

---

## Self-Review

**Coverage:** Mode 04 on the session (Task 1), service-level clear+refresh (Task 2), enabled UI command (Task 3). Standard OBD-II, no proprietary data.

**Placeholder scan:** No TBD/TODO. The old disabled-stub test is replaced with the enabled-behavior test.

**Type consistency:** `IObdSession.ClearDtcsAsync` implemented by `KLineObdSession` (+ `FakeObdSession`); `LiveDataService.ClearDtcsAsync`; `DiagnosticsViewModel.ClearCodesCommand`/`CanClearCodes`. Reuses `RequestAsync`/`ObdResponse`, `FakeEcu`, `FakeObdSession`. Tests use `using AwesomeAssertions;`.
