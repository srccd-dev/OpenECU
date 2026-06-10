# OpenECU Keyword Handshake + OBD-II Read Probe — Implementation Plan (Plan 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the probe to complete the ISO9141-2 keyword handshake (reply with inverted KW2 inside the W4 timing window) and then fire standard OBD-II read requests, **capturing every raw byte** — so we empirically learn (a) whether this cable echoes our transmissions and (b) whether the 2004 Sagem answers OBD-II reads WITHOUT the proprietary seed-key.

**Architecture:** This is a **capture-first, probe-only** plan — no new library abstractions, no baked-in assumptions about echo behavior. The probe reads the three handshake bytes (`55 KW1 KW2`) as fast as they arrive, waits W4 (~30 ms), writes `~KW2`, and then raw-logs everything (which reveals any TX echo plus the inverted-address confirmation). It then sends framed OBD-II requests (via the existing `KLineFrameBuilder`) and raw-logs the replies. Structured parsing and the real session abstraction (with correctly-configured echo handling) come in plan 6, designed from what this captures.

**Tech Stack:** .NET 8 (C# 12). Reuses `KLineFiveBaudInitializer`, `KLineFrameBuilder`, `SystemSerialPort` from plans 1–4. No new tests (a hardware-discovery tool); verified by build + a no-hardware smoke check + the manual bike run.

**Confirmed on the real bike (plan 4):** 5-baud init at `0x33` works; ECU returns `55 08 08` (ISO9141-2, `KW1=KW2=0x08`) then waits for the tester to complete the handshake.

**Why no structured handshake class yet:** the handshake's correctness depends on whether the cable echoes UART writes (the init's `00 00 00` noise hints it loops back, but that's unconfirmed). Capturing raw bytes settles it; plan 6 then builds the session with the right echo handling.

**Reverse-engineering / standards basis:**
- ISO9141-2: after `0x55 KW1 KW2`, the tester waits **W4 (25–50 ms)** then sends **`~KW2`**; the ECU replies with **`~address`** (`~0x33 = 0xCC`). Public spec.
- Triumph OBD requests use standard service IDs over the existing framing (`0x80|len, 0xD5, 0xF5, …, checksum`), produced by `KLineFrameBuilder` in `KLineMode.Iso9141`. (Decompiled `SendPidQuery [01 00]`, `SendActiveCodeQuery [03]`, `SendIDQuery [21 80]`.)

**Prerequisite:** Plans 1–4 on `main`. Cable on COM8, bike powered.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.Probe/Program.cs` | **Replace:** init → tight keyword handshake → raw OBD-II capture |

---

### Task 1: Rewrite the probe (handshake + raw OBD capture)

**Files:**
- Modify: `src/OpenEcu.Probe/Program.cs` (replace its entire contents)

- [ ] **Step 1: Replace the probe program**

Replace `src/OpenEcu.Probe/Program.cs` with:
```csharp
using System.Diagnostics;
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Protocol;
using OpenEcu.Transport.Serial;

// Usage: dotnet run --project src/OpenEcu.Probe -- [COMx] [addrHex]
// Defaults: COM8, address 33
string portName = args.Length > 0 ? args[0] : "COM8";
byte address = Convert.ToByte(args.Length > 1 ? args[1] : "33", 16);

Console.WriteLine($"OpenECU probe — port={portName}, init address=0x{address:X2}");
Console.WriteLine("Bike must be powered (ignition on / battery tender), cable connected.\n");

await using var sp = new SystemSerialPort(portName, baudRate: 10400, readTimeoutMs: 200, writeTimeoutMs: 1000);
try { sp.Open(); }
catch (Exception ex) { Console.WriteLine($"Could not open {portName}: {ex.GetType().Name}: {ex.Message}"); return; }

var overall = Stopwatch.StartNew();

// Reads a single byte with a short timeout; returns -1 if none arrived.
async Task<int> ReadByte()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));
    var b = new byte[1];
    try { int n = await sp.ReadAsync(b, cts.Token); return n > 0 ? b[0] : -1; }
    catch (OperationCanceledException) { return -1; }
    catch (TimeoutException) { return -1; }
}

// Raw-captures and logs bytes for a time window.
async Task Capture(string label, int windowMs)
{
    Console.WriteLine(label);
    var sw = Stopwatch.StartNew();
    int total = 0;
    while (sw.ElapsedMilliseconds < windowMs)
    {
        int b = await ReadByte();
        if (b >= 0) { total++; Console.WriteLine($"     [{overall.ElapsedMilliseconds,6} ms] RX {b:X2}"); }
    }
    if (total == 0) Console.WriteLine("     (nothing)");
}

// 1) 5-baud init on the break line.
Console.WriteLine($"== 5-baud init at 0x{address:X2} ==");
await new KLineFiveBaudInitializer().InitializeAsync(new BreakLineAdapter(sp), address);

// 2) Read the three handshake bytes (sync, KW1, KW2) as fast as they arrive.
Console.WriteLine("-- reading handshake bytes (expect 55 08 08):");
var hs = new List<int>();
var hsClock = Stopwatch.StartNew();
while (hs.Count < 3 && hsClock.ElapsedMilliseconds < 600)
{
    int b = await ReadByte();
    if (b >= 0) { hs.Add(b); Console.WriteLine($"     [{overall.ElapsedMilliseconds,6} ms] RX {b:X2}"); }
}

// 3) If we got a valid sync + keywords, complete the handshake within the W4 window.
if (hs.Count >= 3 && hs[0] == 0x55)
{
    byte kw2 = (byte)hs[2];
    byte invKw2 = (byte)(kw2 ^ 0xFF);
    await Task.Delay(30); // W4 (25-50 ms)
    Console.WriteLine($"-- TX ~KW2 = {invKw2:X2}");
    await sp.WriteAsync(new byte[] { invKw2 });
    // Raw-log whatever follows: a possible echo of 0x{invKw2}, then the inverted address (~0x33 = CC).
    await Capture("   (expect [echo?] then invAddr CC):", 400);
}
else
{
    Console.WriteLine($"-- did not get a clean 55 + keywords (got: {string.Join(" ", hs.Select(x => x.ToString("X2")))}). Stopping.");
    return;
}

// 4) Fire standard OBD-II reads and raw-log the replies (no parsing yet).
var probes = new (string Name, byte[] Payload)[]
{
    ("Mode 01 PID 00 (supported PIDs)", new byte[] { 0x01, 0x00 }),
    ("Mode 01 PID 0C (RPM)",            new byte[] { 0x01, 0x0C }),
    ("Mode 01 PID 05 (coolant temp)",  new byte[] { 0x01, 0x05 }),
    ("Mode 03 (stored DTCs)",          new byte[] { 0x03 }),
    ("Triumph ID (21 80)",             new byte[] { 0x21, 0x80 }),
};

foreach (var (name, payload) in probes)
{
    byte[] frame = KLineFrameBuilder.BuildRequest(payload, KLineMode.Iso9141);
    Console.WriteLine($"\n== {name}: TX {Hex(frame)} ==");
    await sp.WriteAsync(frame);
    await Capture("   RX (may include a TX echo first):", 800);
}

Console.WriteLine("\nDone. Copy ALL output above and send it back.");

static string Hex(ReadOnlySpan<byte> data)
{
    var sb = new System.Text.StringBuilder();
    foreach (byte b in data) sb.Append(b.ToString("X2")).Append(' ');
    return sb.ToString().TrimEnd();
}

// Adapts a serial port's SetBreak to the Core IBreakLine abstraction.
file sealed class BreakLineAdapter(ISerialPort port) : IBreakLine
{
    public void SetBreak(bool on) => port.SetBreak(on);
}
```

- [ ] **Step 2: Build the probe**

Run: `dotnet build src/OpenEcu.Probe`
Expected: build succeeds.

- [ ] **Step 3: Smoke-check wiring without hardware**

Run: `dotnet run --project src/OpenEcu.Probe -- NOPORT 33`
Expected: prints the banner then `Could not open NOPORT: ...` and exits cleanly.

- [ ] **Step 4: Confirm the rest of the suite is unaffected**

Run: `dotnet test`
Expected: 46 passed, 1 skipped (unchanged — this plan adds no tests).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Probe/Program.cs
git commit -m "feat: probe completes ISO9141-2 handshake + raw-captures OBD-II reads"
```

---

## Manual Hardware Run (the human, on the bike)

With the cable on COM8 and the bike powered:

```bash
dotnet run --project src/OpenEcu.Probe
```

**Copy the entire output back.** How to read it:
- **Handshake:** after `TX ~KW2 = F7`, do we see `F7` echoed back (tells us the cable echoes), and/or `CC` (`~0x33`, the session-open confirmation)?
- **OBD reads:** for each request, the `RX` lines show raw bytes. We're distinguishing:
  - A **positive response** (the request SID + 0x40, e.g. Mode 01 → `41 …`) → the ECU answers OBD-II **without** the seed-key → clean-room read-only diagnostics are GO.
  - A **negative `7F xx 33`** (NRC 0x33 = security access denied) → reads need the seed-key; we decide that next, with proof.
  - **(nothing)** → likely a W4/P2 timing miss or an echo-handling quirk we'll see in the raw bytes.

**If the handshake or reads time out — lower the FTDI latency timer:** Device Manager → Ports → USB Serial Port (COM8) → Properties → Port Settings → Advanced → **Latency Timer = 1 ms**, then re-run. (The 16 ms default can blow the 25–50 ms W4 window.)

This capture decides plan 6: the structured `KLineSession` (with correct echo handling) + OBD response parsing, and whether the seed-key is needed at all.

---

## Self-Review

**Spec coverage:**
- Complete the ISO9141-2 keyword handshake within W4 → Task 1 ✅
- Capture-first OBD-II reads over the real cable (raw, assumption-free) → Task 1 + Manual Run ✅
- Reveal echo behavior empirically → Task 1 (raw logging after our TX) ✅
- Empirical "reads without seed-key" test → Manual Run ✅
- **Deliberately deferred:** structured session/echo handling + OBD parsing + the seed-key (`0x27`) decision → plan 6, driven by this capture.

**Placeholder scan:** No TBD/TODO. The probe is complete and self-contained; `BreakLineAdapter` is the only helper and is fully defined.

**Type consistency:** Reuses `KLineFiveBaudInitializer.InitializeAsync(IBreakLine, byte)`, `IBreakLine.SetBreak`, `KLineFrameBuilder.BuildRequest(payload, KLineMode.Iso9141)`, and `SystemSerialPort` (`Open`, `SetBreak`, `WriteAsync`, `ReadAsync`) consistently with plans 1–4. No new types introduced.
