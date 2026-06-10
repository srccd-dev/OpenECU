# OpenECU K-line 5-Baud Init + Capture Probe — Implementation Plan (Plan 4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the ISO9141 **5-baud slow-init** wake sequence over the serial transport (break-line control + the exact bit pattern + timing), all unit-tested, plus a console **probe** that runs the init against the real bike and hex-logs whatever the ECU returns — so we learn empirically what the Sagem MC1000 does before committing to the (proprietary, legally sensitive) seed-key handshake.

**Architecture:** Adds break-line control to the `ISerialPort` seam (deferred from plan 3) and a pure `FiveBaudInitPattern` that reproduces the original's `address*4 + 1025` bit math, driven by a `KLineFiveBaudInitializer` with an injectable bit-period delay (so it's testable without real 196 ms waits). A new `OpenEcu.Probe` console app wires the real `SystemSerialPort` + initializer + a timed read loop to capture the ECU's response. The pure pieces are unit-tested; the probe is run manually on the bike.

**Tech Stack:** .NET 8 (C# 12), `System.IO.Ports`, xUnit, **AwesomeAssertions** (MIT — NOT FluentAssertions). Builds on plans 1–3.

**Scope note:** Plan 4 of several. It deliberately does NOT implement the seed-key security handshake (`CalculateKey`/`0x27`) — that proprietary algorithm is a deliberate, legally-sensitive decision deferred to plan 5, to be made after we see the captured data. It also does not parse the ECU response yet (capture-first); parsing is plan 5.

**Reverse-engineering basis (from decompiled `ISORead.cs` `Initialization(int c)` + `ISOFT.SetBreak`):**
- 5-baud init builds `pattern = address*4 + 1025`, then for 11 bit-periods sleeps `pTiming` ms and drives the line per the LSB.
- **Polarity (from `ISOFT.SetBreak(v)`): `v == 0` → break ON → line LOW; `v != 0` → break OFF → line HIGH.** So a pattern bit of **`0` drives the line LOW**, a bit of **`1` drives it HIGH**. The line idles high; the 11-bit frame is `[lead-in HIGH(1), START LOW(0), 8 data bits LSB-first, STOP HIGH(1)]`.
- `pTiming` default = **196 ms** (clamped 180–219).
- Init addresses observed: **`0x33`** (51, standard ISO9141) and **`0xD5`** (213, Sagem path).

**Prerequisite:** Plans 1–3 on `main`. The FTDI cable is on **COM8**; the bike is on a battery tender with the cable on its diagnostic port.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.Transport.Serial/ISerialPort.cs` | **Modify:** add `void SetBreak(bool on)` |
| `src/OpenEcu.Transport.Serial/SystemSerialPort.cs` | **Modify:** implement `SetBreak` via `SerialPort.BreakState` |
| `src/OpenEcu.Core/Protocol/FiveBaudInitPattern.cs` | Pure: ordered break states for an address byte |
| `src/OpenEcu.Core/Adapters/KLineFiveBaudInitializer.cs` | Drives the pattern over an `ISerialPort`-like sink with timing |
| `src/OpenEcu.Core/Adapters/IBreakLine.cs` | Tiny abstraction the initializer drives (`SetBreak`) |
| `src/OpenEcu.Probe/OpenEcu.Probe.csproj` | New console app (manual hardware tool) |
| `src/OpenEcu.Probe/Program.cs` | Runs init + captures raw ECU response |
| `tests/OpenEcu.Core.Tests/Protocol/FiveBaudInitPatternTests.cs` | Exact bit-pattern vectors |
| `tests/OpenEcu.Core.Tests/Adapters/KLineFiveBaudInitializerTests.cs` | Toggle-sequence + timing tests |
| `tests/OpenEcu.Transport.Serial.Tests/FakeSerialPort.cs` | **Modify:** record `SetBreak` calls |

**Design note — why `IBreakLine`:** the initializer lives in `OpenEcu.Core` (pure, no `System.IO.Ports` dependency), so it drives a tiny `IBreakLine { void SetBreak(bool on); }` abstraction rather than `ISerialPort` (which lives in the serial project). `SystemSerialPort` and `FakeSerialPort` both already expose `SetBreak`; the probe adapts the port to `IBreakLine` with a one-line lambda wrapper. This keeps Core free of native deps while remaining testable.

---

### Task 1: Add break-line control to the serial seam

**Files:**
- Modify: `src/OpenEcu.Transport.Serial/ISerialPort.cs`
- Modify: `src/OpenEcu.Transport.Serial/SystemSerialPort.cs`
- Modify: `tests/OpenEcu.Transport.Serial.Tests/FakeSerialPort.cs`

- [ ] **Step 1: Extend the interface**

In `src/OpenEcu.Transport.Serial/ISerialPort.cs`, add this member inside the interface (after `ReadAsync`):
```csharp
    /// <summary>Sets the break (line-low) condition. true = line held low; false = idle high.</summary>
    void SetBreak(bool on);
```

- [ ] **Step 2: Implement it in SystemSerialPort**

In `src/OpenEcu.Transport.Serial/SystemSerialPort.cs`, add this method (after `Close`):
```csharp
    public void SetBreak(bool on) => _port.BreakState = on;
```

- [ ] **Step 3: Record it in the test double**

In `tests/OpenEcu.Transport.Serial.Tests/FakeSerialPort.cs`, add a field and the method, and expose the recorded toggles:
```csharp
    private readonly List<bool> _breakToggles = new();
    public IReadOnlyList<bool> BreakToggles => _breakToggles;
    public void SetBreak(bool on) => _breakToggles.Add(on);
```
Add them inside the `FakeSerialPort` class body (e.g. just after the `Written` property).

- [ ] **Step 4: Verify the solution still builds and tests pass**

Run: `dotnet test`
Expected: build succeeds; all existing tests still green (39 passed, 1 skipped).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Transport.Serial/ISerialPort.cs src/OpenEcu.Transport.Serial/SystemSerialPort.cs tests/OpenEcu.Transport.Serial.Tests/FakeSerialPort.cs
git commit -m "feat: break-line control (SetBreak) on the serial seam"
```

---

### Task 2: FiveBaudInitPattern

**Files:**
- Create: `src/OpenEcu.Core/Protocol/FiveBaudInitPattern.cs`
- Test: `tests/OpenEcu.Core.Tests/Protocol/FiveBaudInitPatternTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Protocol/FiveBaudInitPatternTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class FiveBaudInitPatternTests
{
    // pattern = address*4 + 1025. Each of the 11 entries is the BREAK-ON state for that
    // bit-period: break is ON (line low) when the pattern bit is 0 (per ISOFT.SetBreak).
    // 0x33: pattern = 51*4 + 1025 = 1229; LSB-first bits 1,0,1,1,0,0,1,1,0,0,1
    //       break-on (bit==0):       F,T,F,F,T,T,F,F,T,T,F
    [Fact]
    public void Address_0x33_produces_the_expected_break_on_states()
    {
        bool[] states = FiveBaudInitPattern.BreakStatesFor(0x33);
        states.Should().Equal(false, true, false, false, true, true, false, false, true, true, false);
    }

    // 0xD5: pattern = 213*4 + 1025 = 1877; LSB-first bits 1,0,1,0,1,0,1,0,1,1,1
    //       break-on (bit==0):        F,T,F,T,F,T,F,T,F,F,F
    [Fact]
    public void Address_0xD5_produces_the_expected_break_on_states()
    {
        bool[] states = FiveBaudInitPattern.BreakStatesFor(0xD5);
        states.Should().Equal(false, true, false, true, false, true, false, true, false, false, false);
    }

    [Fact]
    public void First_state_is_idle_high_and_second_is_the_low_start_bit()
    {
        // The frame leads in HIGH (break off) then drops LOW (break on) for the start bit.
        bool[] states = FiveBaudInitPattern.BreakStatesFor(0x33);
        states[0].Should().BeFalse(); // lead-in: line high
        states[1].Should().BeTrue();  // start bit: line low
    }

    [Fact]
    public void Always_returns_eleven_states()
    {
        FiveBaudInitPattern.BreakStatesFor(0x00).Should().HaveCount(11);
        FiveBaudInitPattern.BreakStatesFor(0xFF).Should().HaveCount(11);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FiveBaudInitPatternTests`
Expected: FAIL — `FiveBaudInitPattern` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/OpenEcu.Core/Protocol/FiveBaudInitPattern.cs`:
```csharp
namespace OpenEcu.Core.Protocol;

/// <summary>
/// ISO9141 5-baud slow-init bit pattern. Reproduces the original tool's encoding:
/// pattern = address*4 + 1025, taken LSB-first over 11 bit-periods. Each returned bool is
/// the BREAK-ON state for that period: break is ON (line held LOW) when the pattern bit is
/// 0, OFF (line HIGH) when the bit is 1 — matching ISOFT.SetBreak (v==0 => break on).
/// The frame therefore reads: lead-in HIGH, START LOW, 8 data bits LSB-first, STOP HIGH.
/// </summary>
public static class FiveBaudInitPattern
{
    public const int BitCount = 11;

    public static bool[] BreakStatesFor(byte address)
    {
        int pattern = address * 4 + 1025;
        var states = new bool[BitCount];
        for (int i = 0; i < BitCount; i++)
        {
            states[i] = (pattern & 1) == 0; // break ON (line low) when the bit is 0
            pattern >>= 1;
        }
        return states;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FiveBaudInitPatternTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Protocol/FiveBaudInitPattern.cs tests/OpenEcu.Core.Tests/Protocol/FiveBaudInitPatternTests.cs
git commit -m "feat: ISO9141 5-baud init bit pattern"
```

---

### Task 3: IBreakLine + KLineFiveBaudInitializer

**Files:**
- Create: `src/OpenEcu.Core/Adapters/IBreakLine.cs`
- Create: `src/OpenEcu.Core/Adapters/KLineFiveBaudInitializer.cs`
- Test: `tests/OpenEcu.Core.Tests/Adapters/KLineFiveBaudInitializerTests.cs`

The initializer walks the 11 break states, driving the line for each bit-period (waiting one period each). It sets the level every bit (re-asserting the same level is a harmless no-op), so the recorded sequence equals the pattern exactly. The frame's own lead-in/stop bits leave the line idle-high — no separate release needed.

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Adapters/KLineFiveBaudInitializerTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Adapters;

public class KLineFiveBaudInitializerTests
{
    private sealed class RecordingBreakLine : IBreakLine
    {
        public List<bool> Toggles { get; } = new();
        public void SetBreak(bool on) => Toggles.Add(on);
    }

    [Fact]
    public async Task Drives_the_line_with_the_exact_break_pattern_for_0x33()
    {
        var line = new RecordingBreakLine();
        var init = new KLineFiveBaudInitializer(delay: _ => Task.CompletedTask);

        await init.InitializeAsync(line, 0x33);

        line.Toggles.Should().Equal(FiveBaudInitPattern.BreakStatesFor(0x33));
    }

    [Fact]
    public async Task Waits_one_bit_period_per_bit()
    {
        var line = new RecordingBreakLine();
        int delays = 0;
        var init = new KLineFiveBaudInitializer(delay: _ => { delays++; return Task.CompletedTask; });

        await init.InitializeAsync(line, 0x33);

        delays.Should().Be(11); // one wait per bit-period
    }

    [Fact]
    public async Task Ends_with_the_line_idle_high()
    {
        var line = new RecordingBreakLine();
        var init = new KLineFiveBaudInitializer(delay: _ => Task.CompletedTask);

        await init.InitializeAsync(line, 0x33);

        line.Toggles.Last().Should().BeFalse(); // stop bit: break off, line high
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter KLineFiveBaudInitializerTests`
Expected: FAIL — `IBreakLine` / `KLineFiveBaudInitializer` do not exist.

- [ ] **Step 3: Write the abstraction**

Create `src/OpenEcu.Core/Adapters/IBreakLine.cs`:
```csharp
namespace OpenEcu.Core.Adapters;

/// <summary>Something whose break (line-low) condition can be toggled — used for 5-baud init.</summary>
public interface IBreakLine
{
    void SetBreak(bool on);
}
```

- [ ] **Step 4: Write the initializer**

Create `src/OpenEcu.Core/Adapters/KLineFiveBaudInitializer.cs`:
```csharp
using OpenEcu.Core.Protocol;

namespace OpenEcu.Core.Adapters;

/// <summary>
/// Performs an ISO9141 5-baud slow init: bit-bangs an address byte on the break line,
/// one bit per bit-period. The line idles high (break off); a 1 bit drives it low.
/// </summary>
public sealed class KLineFiveBaudInitializer
{
    private readonly TimeSpan _bitPeriod;
    private readonly Func<TimeSpan, Task> _delay;

    /// <param name="bitPeriod">5-baud bit time; the original uses ~196 ms.</param>
    /// <param name="delay">Override the wait (for tests). Defaults to Task.Delay.</param>
    public KLineFiveBaudInitializer(TimeSpan? bitPeriod = null, Func<TimeSpan, Task>? delay = null)
    {
        _bitPeriod = bitPeriod ?? TimeSpan.FromMilliseconds(196);
        _delay = delay ?? Task.Delay;
    }

    public async Task InitializeAsync(IBreakLine line, byte address, CancellationToken ct = default)
    {
        bool[] states = FiveBaudInitPattern.BreakStatesFor(address);
        foreach (bool breakOn in states)
        {
            ct.ThrowIfCancellationRequested();
            await _delay(_bitPeriod);
            line.SetBreak(breakOn);
        }
        // The final (stop) bit is break-off, so the line is already idle-high here.
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter KLineFiveBaudInitializerTests`
Expected: PASS (3 passed).

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Adapters/IBreakLine.cs src/OpenEcu.Core/Adapters/KLineFiveBaudInitializer.cs tests/OpenEcu.Core.Tests/Adapters/KLineFiveBaudInitializerTests.cs
git commit -m "feat: KLineFiveBaudInitializer (ISO9141 slow-init driver)"
```

---

### Task 4: OpenEcu.Probe console app

**Files:**
- Create: `src/OpenEcu.Probe/OpenEcu.Probe.csproj`
- Create: `src/OpenEcu.Probe/Program.cs`

A manual hardware tool (not unit-tested): it opens the cable, runs 5-baud init at the given address(es), and hex-logs every byte the ECU returns, with elapsed-ms timestamps.

- [ ] **Step 1: Create the project**

Run from repo root:
```bash
dotnet new console -n OpenEcu.Probe -o src/OpenEcu.Probe
rm src/OpenEcu.Probe/Program.cs
```

- [ ] **Step 2: Overwrite the csproj**

Replace `src/OpenEcu.Probe/OpenEcu.Probe.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>12</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenEcu.Core\OpenEcu.Core.csproj" />
    <ProjectReference Include="..\OpenEcu.Transport.Serial\OpenEcu.Transport.Serial.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the probe**

Create `src/OpenEcu.Probe/Program.cs`:
```csharp
using System.Diagnostics;
using OpenEcu.Core.Adapters;
using OpenEcu.Transport.Serial;

// Usage: dotnet run --project src/OpenEcu.Probe -- [COMx] [addrHex,addrHex,...]
// Defaults: COM8, addresses 33,D5
string port = args.Length > 0 ? args[0] : "COM8";
byte[] addresses = (args.Length > 1 ? args[1] : "33,D5")
    .Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(s => Convert.ToByte(s.Trim(), 16))
    .ToArray();

Console.WriteLine($"OpenECU probe — port={port}, addresses={string.Join(",", addresses.Select(a => $"0x{a:X2}"))}");
Console.WriteLine("Make sure the bike is powered (ignition on / battery tender) and the cable is connected.\n");

foreach (byte address in addresses)
{
    Console.WriteLine($"=== 5-baud init at address 0x{address:X2} ===");
    await using var sp = new SystemSerialPort(port, baudRate: 10400, readTimeoutMs: 300, writeTimeoutMs: 1000);
    try
    {
        sp.Open();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Could not open {port}: {ex.GetType().Name}: {ex.Message}");
        return;
    }

    // Drive the 5-baud init on the break line.
    var initializer = new KLineFiveBaudInitializer();
    IBreakLine line = new BreakLineAdapter(sp);
    var sw = Stopwatch.StartNew();
    await initializer.InitializeAsync(line, address);
    Console.WriteLine($"  init sent in {sw.ElapsedMilliseconds} ms; listening 3s for the ECU...");

    // Capture whatever comes back for ~3 seconds.
    sw.Restart();
    var buffer = new byte[64];
    int total = 0;
    while (sw.ElapsedMilliseconds < 3000)
    {
        int n;
        using var readCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        try { n = await sp.ReadAsync(buffer, readCts.Token); }
        catch (OperationCanceledException) { n = 0; }
        catch (TimeoutException) { n = 0; }
        if (n > 0)
        {
            total += n;
            string hex = string.Join(" ", buffer.Take(n).Select(b => b.ToString("X2")));
            Console.WriteLine($"  [{sw.ElapsedMilliseconds,5} ms] RX {n,2}: {hex}");
        }
    }
    Console.WriteLine(total == 0
        ? "  (no bytes received)\n"
        : $"  total {total} bytes received\n");

    sp.Close();
}

Console.WriteLine("Done. Copy ALL output above and send it back for analysis.");

// Adapts a serial port's SetBreak to the Core IBreakLine abstraction.
file sealed class BreakLineAdapter(ISerialPort port) : IBreakLine
{
    public void SetBreak(bool on) => port.SetBreak(on);
}
```

- [ ] **Step 4: Add to the solution and build**

```bash
dotnet sln add src/OpenEcu.Probe/OpenEcu.Probe.csproj
dotnet build src/OpenEcu.Probe
```
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Probe OpenEcu.slnx
git commit -m "feat: OpenEcu.Probe console tool for 5-baud init + raw capture"
```

---

### Task 5: Full suite + final commit

- [ ] **Step 1: Run the full suite**

Run: `dotnet test`
Expected: PASS — plans 1–3 (39) + Task 2 (3) + Task 3 (3) = 45 passed, 1 skipped.

- [ ] **Step 2: Confirm the probe builds and shows usage (no hardware needed for this check)**

Run: `dotnet run --project src/OpenEcu.Probe -- NOPORT 33`
Expected: it prints the banner then "Could not open NOPORT: ..." and exits cleanly (proves the wiring without needing the cable).

- [ ] **Step 3: Commit anything outstanding (if the build produced no changes, skip)**

```bash
git status --short
```

---

## Manual Hardware Run (the human, on the bike)

With the cable on **COM8** and the bike powered (battery tender / ignition on):

```bash
dotnet run --project src/OpenEcu.Probe
```

This runs 5-baud init at `0x33` then `0xD5` and logs everything the ECU returns. **Copy the entire output and send it back.** What we're looking for:
- **A `55` sync byte** after init → the ECU woke up and our timing/break control works.
- **Bytes after `55`** (key bytes KW1/KW2) → tells us the keyword protocol the Sagem speaks.
- **Nothing** → we'll adjust the init address/timing or check the cable wiring/port.

That capture drives **plan 5** (parsing the handshake and deciding the seed-key approach).

To target a specific port/address: `dotnet run --project src/OpenEcu.Probe -- COM8 33`

---

## Self-Review

**Spec coverage (this plan's slice of design §6–§7 + roadmap Phase “wake/init”):**
- Break-line control on the transport seam → Task 1 ✅
- ISO9141 5-baud init bit pattern (exact RE of `address*4+1025`) → Task 2 ✅
- Init driver with correct toggle-on-change + 196 ms timing → Task 3 ✅
- Real-hardware capture path → Task 4 + Manual Run ✅
- **Deliberately deferred (noted in scope):** seed-key security handshake (`CalculateKey`/`0x27`) and response parsing → plan 5, decided after capture; ELM327 adapter; maps; UI.

**Placeholder scan:** No TBD/TODO. Task 3 Step 5 includes a contingency for the third test's last-toggle assertion (explicit, not a placeholder).

**Type consistency:** `FiveBaudInitPattern.BreakStatesFor`, `IBreakLine.SetBreak`, `KLineFiveBaudInitializer.InitializeAsync(IBreakLine, byte, ct)`, the new `ISerialPort.SetBreak`/`SystemSerialPort.SetBreak`/`FakeSerialPort.SetBreak`+`BreakToggles`, and the probe's `BreakLineAdapter` are referenced consistently. Tests use `using AwesomeAssertions;` (not FluentAssertions).
