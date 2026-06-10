# OpenECU Live K-line OBD Session — Implementation Plan (Plan 7)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wrap every hardware-validated behavior into one clean, tested API — `KLineObdSession` — that does 5-baud init → keyword handshake → echo-locked transmit → read-until-idle → decode, exposing `ConnectAsync`, `ReadSupportedPidsAsync`, `ReadPidAsync`, and `ReadDtcsAsync`. Then rewire `OpenEcu.Probe` onto it and confirm on the bike.

**Architecture:** `KLineObdSession` (in `OpenEcu.Core.Obd`) composes the existing pieces: `KLineFiveBaudInitializer` (init), the plan-6 `ObdMessage`/`PidDecoder`/`DtcDecoder` (framing + decode), and an `IEcuTransport` + `IBreakLine` for I/O. It reproduces exactly what we proved on the 2004 Speed Triple: the OBD-II `68 6A F1` header, echo-locked byte-by-byte send, and read-until-idle. A `FakeEcu` test double (echoes writes, serves scripted responses, models the handshake) makes the whole thing unit-testable with no hardware. The read loop ends on a zero-length read — on real hardware that's the serial `ReadTimeout` idle gap; on the fake it's an empty queue — so identical logic works in both.

**Tech Stack:** .NET 8 (C# 12), xUnit, **AwesomeAssertions** (MIT — `using AwesomeAssertions;`). Builds on plans 1–6.

**Hardware-validated facts this encodes (2026-06-10):** init `0x33`; sync `0x55` ~200 ms after init (preceded by `00` noise to skip); keywords `08 08`; reply `~KW2` within W4 (~30 ms); `~addr` = `0xCC`; requests use OBD header `68 6A F1`; cable echoes every TX byte; reads delimited by the idle gap.

**Scope note:** Plan 7 of several. It builds the live session + rewires the probe. It does NOT add the Avalonia UI or the ELM327/Bluetooth adapter (later plans). Engine-off reads were validated; engine-on dynamic values are a future manual check.

**Prerequisite:** Plans 1–6 on `main`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.Transport.Serial/SerialPortTransport.cs` | **Modify:** also implement `IBreakLine` |
| `src/OpenEcu.Core/Obd/KLineObdSession.cs` | The live session orchestrator |
| `src/OpenEcu.Probe/Program.cs` | **Replace:** drive the real session API |
| `tests/OpenEcu.Transport.Serial.Tests/SerialPortTransportTests.cs` | **Modify:** add a SetBreak test |
| `tests/OpenEcu.Core.Tests/Obd/FakeEcu.cs` | Test double: echoing OBD bike emulator |
| `tests/OpenEcu.Core.Tests/Obd/KLineObdSessionTests.cs` | Session tests (request, connect, high-level reads) |

---

### Task 1: SerialPortTransport also implements IBreakLine

So one object can serve the session as both the byte transport and the break line for 5-baud init.

**Files:**
- Modify: `src/OpenEcu.Transport.Serial/SerialPortTransport.cs`
- Modify: `tests/OpenEcu.Transport.Serial.Tests/SerialPortTransportTests.cs`

- [ ] **Step 1: Write the failing test**

Add to the `SerialPortTransportTests` class in `tests/OpenEcu.Transport.Serial.Tests/SerialPortTransportTests.cs` (before the closing brace). Also add `using OpenEcu.Core.Adapters;` to the file's usings if not present:
```csharp
    [Fact]
    public void SetBreak_delegates_to_the_port_and_is_an_IBreakLine()
    {
        var fake = new FakeSerialPort();
        var transport = new SerialPortTransport(fake);

        ((IBreakLine)transport).SetBreak(true);
        ((IBreakLine)transport).SetBreak(false);

        fake.BreakToggles.Should().Equal(true, false);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SerialPortTransportTests`
Expected: FAIL — `SerialPortTransport` does not implement `IBreakLine` (cast fails / won't compile).

- [ ] **Step 3: Implement it**

In `src/OpenEcu.Transport.Serial/SerialPortTransport.cs`: add `using OpenEcu.Core.Adapters;` to the usings, change the class declaration to implement `IBreakLine`, and add the method.

Change:
```csharp
public sealed class SerialPortTransport : IEcuTransport
```
to:
```csharp
public sealed class SerialPortTransport : IEcuTransport, IBreakLine
```
and add this method inside the class (e.g. after `CloseAsync`):
```csharp
    public void SetBreak(bool on) => _port.SetBreak(on);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter SerialPortTransportTests`
Expected: PASS (5 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Transport.Serial/SerialPortTransport.cs tests/OpenEcu.Transport.Serial.Tests/SerialPortTransportTests.cs
git commit -m "feat: SerialPortTransport implements IBreakLine"
```

---

### Task 2: FakeEcu + KLineObdSession.RequestAsync

**Files:**
- Create: `tests/OpenEcu.Core.Tests/Obd/FakeEcu.cs`
- Create: `src/OpenEcu.Core/Obd/KLineObdSession.cs`
- Test: `tests/OpenEcu.Core.Tests/Obd/KLineObdSessionTests.cs`

- [ ] **Step 1: Write the test double**

Create `tests/OpenEcu.Core.Tests/Obd/FakeEcu.cs`:
```csharp
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Tests.Obd;

/// <summary>
/// In-memory ISO9141-2 OBD bike emulator. Echoes every written byte (single-wire K-line),
/// answers complete OBD request frames from a scripted table, and (when not pre-connected)
/// serves the 5-baud handshake: noise + sync + keywords, then the inverted address after
/// the first write (the tester's ~KW2). A zero-length read signals the idle gap.
/// </summary>
public sealed class FakeEcu : IEcuTransport, IBreakLine
{
    private readonly Queue<byte> _rx = new();
    private readonly List<byte> _frame = new();
    private readonly Dictionary<string, byte[]> _responses;
    private bool _handshakeReplied;

    public List<bool> BreakToggles { get; } = new();
    public bool IsOpen { get; private set; }

    /// <param name="responses">payload (hex, e.g. "010C") -> full response frame bytes.</param>
    /// <param name="connected">true to skip the handshake (test RequestAsync directly).</param>
    public FakeEcu(Dictionary<string, byte[]> responses, bool connected = false)
    {
        _responses = responses;
        _handshakeReplied = connected;
        if (!connected)
            foreach (byte b in new byte[] { 0x00, 0x00, 0x55, 0x08, 0x08 })
                _rx.Enqueue(b);
    }

    public void SetBreak(bool on) => BreakToggles.Add(on);
    public Task OpenAsync(CancellationToken ct = default) { IsOpen = true; return Task.CompletedTask; }
    public Task CloseAsync() { IsOpen = false; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        foreach (byte b in data.ToArray())
        {
            _rx.Enqueue(b); // K-line echoes the transmitted byte
            if (!_handshakeReplied)
            {
                _rx.Enqueue(0xCC); // ~addr reply to the tester's ~KW2
                _handshakeReplied = true;
                continue;
            }
            _frame.Add(b);
            if (IsCompleteObdFrame(_frame))
            {
                byte[] payload = _frame.GetRange(3, _frame.Count - 4).ToArray(); // after 68 6A F1, before checksum
                if (_responses.TryGetValue(Convert.ToHexString(payload), out byte[]? resp))
                    foreach (byte rb in resp) _rx.Enqueue(rb);
                _frame.Clear();
            }
        }
        return Task.CompletedTask;
    }

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_rx.Count == 0) return Task.FromResult(0);
        buffer.Span[0] = _rx.Dequeue();
        return Task.FromResult(1);
    }

    private static bool IsCompleteObdFrame(List<byte> f)
    {
        if (f.Count < 5 || f[0] != 0x68) return false;
        int sum = 0;
        for (int i = 0; i < f.Count - 1; i++) sum += f[i];
        return (byte)sum == f[^1];
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Obd/KLineObdSessionTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class KLineObdSessionTests
{
    private static readonly Func<TimeSpan, Task> NoDelay = _ => Task.CompletedTask;

    [Fact]
    public async Task RequestAsync_sends_obd_frame_and_parses_response()
    {
        // RPM request 01 0C -> real response 48 6B D1 41 0C 00 00 D1
        var ecu = new FakeEcu(new()
        {
            ["010C"] = new byte[] { 0x48, 0x6B, 0xD1, 0x41, 0x0C, 0x00, 0x00, 0xD1 },
        }, connected: true);
        await ecu.OpenAsync();
        var session = new KLineObdSession(ecu, ecu, delay: NoDelay);

        ObdResponse resp = await session.RequestAsync(new byte[] { 0x01, 0x0C });

        resp.ServiceId.Should().Be(0x41);
        resp.Payload.Should().Equal(0x0C, 0x00, 0x00);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter KLineObdSessionTests`
Expected: FAIL — `KLineObdSession` does not exist.

- [ ] **Step 4: Write the session (Connect + high-level reads stubbed for later tasks)**

Create `src/OpenEcu.Core/Obd/KLineObdSession.cs`:
```csharp
using System.IO;
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Obd;

/// <summary>
/// A live ISO9141-2 OBD-II session over a K-line cable: 5-baud init, keyword handshake,
/// echo-locked transmit, read-until-idle, and OBD decode. Caller opens the transport first.
/// </summary>
public sealed class KLineObdSession : IAsyncDisposable
{
    private readonly IEcuTransport _transport;
    private readonly IBreakLine _breakLine;
    private readonly byte _initAddress;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly KLineFiveBaudInitializer _initializer;

    public KLineObdSession(IEcuTransport transport, IBreakLine breakLine,
        byte initAddress = 0x33, Func<TimeSpan, Task>? delay = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _breakLine = breakLine ?? throw new ArgumentNullException(nameof(breakLine));
        _initAddress = initAddress;
        _delay = delay ?? Task.Delay;
        _initializer = new KLineFiveBaudInitializer(delay: _delay);
    }

    public bool IsConnected { get; private set; }

    public async Task<ObdResponse> RequestAsync(byte[] payload, CancellationToken ct = default)
    {
        byte[] frame = ObdMessage.BuildRequest(payload);
        await SendEchoLockedAsync(frame, ct);
        byte[] responseBytes = await ReadUntilIdleAsync(ct);
        if (!ObdMessage.TryParseResponse(responseBytes, out ObdResponse response))
            throw new InvalidDataException(
                $"Invalid OBD response: {BitConverter.ToString(responseBytes)}");
        return response;
    }

    public Task ConnectAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<byte>> ReadSupportedPidsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<PidReading> ReadPidAsync(byte pid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<string>> ReadDtcsAsync(CancellationToken ct = default) => throw new NotImplementedException();

    public ValueTask DisposeAsync() => _transport.DisposeAsync();

    // Sends each byte then waits for its echo (the single-wire K-line echoes TX).
    private async Task SendEchoLockedAsync(byte[] frame, CancellationToken ct)
    {
        foreach (byte tx in frame)
        {
            await _transport.WriteAsync(new[] { tx }, ct);
            int echo = await ReadByteAsync(ct);
            if (echo != tx)
                throw new InvalidDataException($"Echo mismatch: sent 0x{tx:X2}, read 0x{echo:X2}.");
        }
    }

    // Reads bytes until the idle gap (a zero-length read).
    private async Task<byte[]> ReadUntilIdleAsync(CancellationToken ct)
    {
        var bytes = new List<byte>();
        while (true)
        {
            int b = await ReadByteAsync(ct);
            if (b < 0) break;
            bytes.Add((byte)b);
        }
        return bytes.ToArray();
    }

    private async Task<int> ReadByteAsync(CancellationToken ct)
    {
        var buffer = new byte[1];
        int n = await _transport.ReadAsync(buffer, ct);
        return n > 0 ? buffer[0] : -1;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter KLineObdSessionTests`
Expected: PASS (1 passed).

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Obd/KLineObdSession.cs tests/OpenEcu.Core.Tests/Obd/FakeEcu.cs tests/OpenEcu.Core.Tests/Obd/KLineObdSessionTests.cs
git commit -m "feat: KLineObdSession.RequestAsync (echo-locked send + read-until-idle)"
```

---

### Task 3: KLineObdSession.ConnectAsync

**Files:**
- Modify: `src/OpenEcu.Core/Obd/KLineObdSession.cs` (replace the `ConnectAsync` stub + add helpers)
- Modify: `tests/OpenEcu.Core.Tests/Obd/KLineObdSessionTests.cs` (append tests)

- [ ] **Step 1: Write the failing tests (append to the test class)**

Add inside `KLineObdSessionTests`, before the closing brace:
```csharp
    [Fact]
    public async Task ConnectAsync_runs_init_then_completes_the_keyword_handshake()
    {
        var ecu = new FakeEcu(new(), connected: false); // serves 00 00 55 08 08, then CC after ~KW2
        await ecu.OpenAsync();
        var session = new KLineObdSession(ecu, ecu, delay: NoDelay);

        await session.ConnectAsync();

        session.IsConnected.Should().BeTrue();
        ecu.BreakToggles.Should().HaveCount(11); // 5-baud init drove 11 bit-periods
    }

    [Fact]
    public async Task ConnectAsync_throws_if_no_sync_byte_arrives()
    {
        // No 0x55 in the stream: emulate by pre-connecting (rx empty) so reads return idle.
        var ecu = new FakeEcu(new(), connected: true);
        await ecu.OpenAsync();
        var session = new KLineObdSession(ecu, ecu, delay: NoDelay);

        var act = async () => await session.ConnectAsync();

        await act.Should().ThrowAsync<EcuConnectionException>();
    }
```
Add `using OpenEcu.Core.Adapters;` to the test file's usings (for `EcuConnectionException`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter KLineObdSessionTests`
Expected: FAIL — `ConnectAsync` throws `NotImplementedException`.

- [ ] **Step 3: Implement ConnectAsync**

In `src/OpenEcu.Core/Obd/KLineObdSession.cs`, replace this line:
```csharp
    public Task ConnectAsync(CancellationToken ct = default) => throw new NotImplementedException();
```
with:
```csharp
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _initializer.InitializeAsync(_breakLine, _initAddress, ct);

        // After init the ECU sends some line-settling noise, then 0x55 sync + 2 keyword bytes.
        int sync = await ReadUntilAsync(0x55, ct);
        if (sync < 0)
            throw new EcuConnectionException("No 0x55 sync byte after 5-baud init.");

        int kw1 = await ReadByteAsync(ct);
        int kw2 = await ReadByteAsync(ct);
        if (kw1 < 0 || kw2 < 0)
            throw new EcuConnectionException("Missing keyword bytes after sync.");

        await _delay(TimeSpan.FromMilliseconds(30)); // W4
        byte invKw2 = (byte)(kw2 ^ 0xFF);
        await _transport.WriteAsync(new[] { invKw2 }, ct);

        // Skip the echo of our ~KW2; the next distinct byte is the inverted address (~addr).
        int invAddr = await ReadSkippingAsync(invKw2, ct);
        if (invAddr < 0)
            throw new EcuConnectionException("No inverted-address reply; handshake not accepted.");

        IsConnected = true;
    }
```
Then add these two helpers inside the class (e.g. after `ReadByteAsync`):
```csharp
    // Reads (discarding) until the target byte appears; -1 if the stream goes idle first.
    private async Task<int> ReadUntilAsync(byte target, CancellationToken ct)
    {
        while (true)
        {
            int b = await ReadByteAsync(ct);
            if (b < 0) return -1;
            if (b == target) return b;
        }
    }

    // Reads until a byte that is NOT skip; -1 if the stream goes idle first.
    private async Task<int> ReadSkippingAsync(byte skip, CancellationToken ct)
    {
        while (true)
        {
            int b = await ReadByteAsync(ct);
            if (b < 0) return -1;
            if (b != skip) return b;
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter KLineObdSessionTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Obd/KLineObdSession.cs tests/OpenEcu.Core.Tests/Obd/KLineObdSessionTests.cs
git commit -m "feat: KLineObdSession.ConnectAsync (5-baud init + keyword handshake)"
```

---

### Task 4: High-level reads (supported PIDs, PID value, DTCs)

**Files:**
- Modify: `src/OpenEcu.Core/Obd/KLineObdSession.cs` (replace the three stubs)
- Modify: `tests/OpenEcu.Core.Tests/Obd/KLineObdSessionTests.cs` (append tests)

- [ ] **Step 1: Write the failing tests (append to the test class)**

Add inside `KLineObdSessionTests`, before the closing brace:
```csharp
    [Fact]
    public async Task ReadPidAsync_decodes_a_value()
    {
        var ecu = new FakeEcu(new()
        {
            ["0105"] = new byte[] { 0x48, 0x6B, 0xD1, 0x41, 0x05, 0x44, 0x0E }, // coolant 0x44 -> 28 C
        }, connected: true);
        await ecu.OpenAsync();
        var session = new KLineObdSession(ecu, ecu, delay: NoDelay);

        PidReading r = await session.ReadPidAsync(0x05);

        r.Value.Should().Be(28);
        r.Unit.Should().Be("C");
    }

    [Fact]
    public async Task ReadSupportedPidsAsync_walks_the_bitmask_chain()
    {
        var ecu = new FakeEcu(new()
        {
            ["0100"] = new byte[] { 0x48, 0x6B, 0xD1, 0x41, 0x00, 0xBE, 0x1E, 0x90, 0x11, 0x42 },
            ["0120"] = new byte[] { 0x48, 0x6B, 0xD1, 0x41, 0x20, 0x00, 0x00, 0x00, 0x01, 0xE6 },
            ["0140"] = new byte[] { 0x48, 0x6B, 0xD1, 0x41, 0x40, 0x00, 0x00, 0x00, 0x00, 0x05 },
        }, connected: true);
        await ecu.OpenAsync();
        var session = new KLineObdSession(ecu, ecu, delay: NoDelay);

        var pids = await session.ReadSupportedPidsAsync();

        pids.Should().Equal(0x01, 0x03, 0x04, 0x05, 0x06, 0x07, 0x0C, 0x0D, 0x0E, 0x0F, 0x11, 0x14, 0x1C, 0x20, 0x40);
    }

    [Fact]
    public async Task ReadDtcsAsync_returns_stored_codes()
    {
        var ecu = new FakeEcu(new()
        {
            ["03"] = new byte[] { 0x48, 0x6B, 0xD1, 0x43, 0x15, 0x02, 0x00, 0x00, 0x00, 0x00, 0xDE },
        }, connected: true);
        await ecu.OpenAsync();
        var session = new KLineObdSession(ecu, ecu, delay: NoDelay);

        var dtcs = await session.ReadDtcsAsync();

        dtcs.Should().Equal("P1502");
    }
```

(Checksum note for the coolant vector: `0x48+0x6B+0xD1+0x41+0x05+0x44 = 0x20E` → `0x0E`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter KLineObdSessionTests`
Expected: FAIL — the three methods throw `NotImplementedException`.

- [ ] **Step 3: Implement the three methods**

In `src/OpenEcu.Core/Obd/KLineObdSession.cs`, replace these three lines:
```csharp
    public Task<IReadOnlyList<byte>> ReadSupportedPidsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<PidReading> ReadPidAsync(byte pid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<string>> ReadDtcsAsync(CancellationToken ct = default) => throw new NotImplementedException();
```
with:
```csharp
    public async Task<IReadOnlyList<byte>> ReadSupportedPidsAsync(CancellationToken ct = default)
    {
        var all = new List<byte>();
        foreach (byte basePid in new byte[] { 0x00, 0x20, 0x40 })
        {
            ObdResponse resp = await RequestAsync(new byte[] { 0x01, basePid }, ct);
            // Payload = [echoed pid, 4 bitmask bytes]
            if (resp.ServiceId != 0x41 || resp.Payload.Length < 5 || resp.Payload[0] != basePid)
                break;
            IReadOnlyList<byte> pids = SupportedPids.Parse(basePid, resp.Payload.AsSpan(1, 4));
            all.AddRange(pids);
            if (!pids.Contains((byte)(basePid + 0x20)))
                break; // next 32-PID range not advertised
        }
        return all;
    }

    public async Task<PidReading> ReadPidAsync(byte pid, CancellationToken ct = default)
    {
        ObdResponse resp = await RequestAsync(new byte[] { 0x01, pid }, ct);
        if (resp.ServiceId != 0x41 || resp.Payload.Length < 1 || resp.Payload[0] != pid)
            throw new InvalidDataException($"Unexpected response to PID 0x{pid:X2}.");
        return PidDecoder.Decode(pid, resp.Payload.AsSpan(1));
    }

    public async Task<IReadOnlyList<string>> ReadDtcsAsync(CancellationToken ct = default)
    {
        ObdResponse resp = await RequestAsync(new byte[] { 0x03 }, ct);
        if (resp.ServiceId != 0x43)
            throw new InvalidDataException("Unexpected Mode 03 response.");
        return DtcDecoder.Decode(resp.Payload);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter KLineObdSessionTests`
Expected: PASS (6 passed).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS — plans 1–6 (64) + Task 1 (1) + Tasks 2–4 session tests (6) = 71 passed, 1 skipped.

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Obd/KLineObdSession.cs tests/OpenEcu.Core.Tests/Obd/KLineObdSessionTests.cs
git commit -m "feat: KLineObdSession high-level reads (supported PIDs, PID value, DTCs)"
```

---

### Task 5: Rewire the probe onto the session

Replace the ad-hoc probe logic with the real Core API. No unit tests (manual hardware tool); verified by build + the no-hardware smoke check + the manual run.

**Files:**
- Modify: `src/OpenEcu.Probe/Program.cs` (replace entire contents)

- [ ] **Step 1: Replace the probe program**

Replace `src/OpenEcu.Probe/Program.cs` with:
```csharp
using OpenEcu.Core.Obd;
using OpenEcu.Transport.Serial;

// Usage: dotnet run --project src/OpenEcu.Probe -- [COMx]
string portName = args.Length > 0 ? args[0] : "COM8";

Console.WriteLine($"OpenECU probe — port={portName}");
Console.WriteLine("Bike must be powered (ignition on / battery tender), cable connected.\n");

// ReadTimeout 300 ms comfortably covers the ~200 ms post-init sync wait and the response idle gap.
await using var port = new SystemSerialPort(portName, baudRate: 10400, readTimeoutMs: 300, writeTimeoutMs: 1000);
try { port.Open(); }
catch (Exception ex) { Console.WriteLine($"Could not open {portName}: {ex.GetType().Name}: {ex.Message}"); return; }

var transport = new SerialPortTransport(port);
var session = new KLineObdSession(transport, transport); // same object is transport + break line

try
{
    Console.WriteLine("Connecting (5-baud init + keyword handshake)...");
    await session.ConnectAsync();
    Console.WriteLine("Connected.\n");

    var supported = await session.ReadSupportedPidsAsync();
    Console.WriteLine($"Supported PIDs: {string.Join(" ", supported.Select(p => p.ToString("X2")))}\n");

    foreach (byte pid in supported)
    {
        if (pid == 0x20 || pid == 0x40) continue; // bitmask chain PIDs
        try
        {
            PidReading r = await session.ReadPidAsync(pid);
            string value = r.Value is null ? $"[{string.Join(" ", r.Raw.Select(b => b.ToString("X2")))}]"
                                            : $"{r.Value:0.##} {r.Unit}";
            Console.WriteLine($"  PID {pid:X2}  {r.Name,-26} {value}");
        }
        catch (Exception ex) { Console.WriteLine($"  PID {pid:X2}  read failed: {ex.Message}"); }
    }

    var dtcs = await session.ReadDtcsAsync();
    Console.WriteLine($"\nStored DTCs: {(dtcs.Count == 0 ? "none" : string.Join(", ", dtcs))}");
}
catch (Exception ex)
{
    Console.WriteLine($"Session error: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine("\nDone.");
```

- [ ] **Step 2: Build and smoke-check (no hardware)**

Run: `dotnet build src/OpenEcu.Probe`
Expected: build succeeds.

Run: `dotnet run --project src/OpenEcu.Probe -- NOPORT`
Expected: prints the banner then `Could not open NOPORT: ...` and exits cleanly.

- [ ] **Step 3: Commit**

```bash
git add src/OpenEcu.Probe/Program.cs
git commit -m "refactor: probe drives the KLineObdSession Core API"
```

---

## Manual Hardware Run (the human, on the bike)

With the cable on COM8 and the bike powered:

```bash
dotnet run --project src/OpenEcu.Probe
```

Expected: `Connected.`, the supported-PID list, a decoded value per PID (coolant/intake in °C, throttle %, RPM, etc.), and `Stored DTCs: P1502` — the same results as the ad-hoc probe, now through the clean session API. If anything regresses, the captured bytes from 2026-06-10 are the reference.

---

## Self-Review

**Spec coverage:**
- One object serves as transport + break line → Task 1 ✅
- Echo-locked transmit + read-until-idle + OBD parse (`RequestAsync`) → Task 2 ✅
- 5-baud init + keyword handshake (`ConnectAsync`) → Task 3 ✅
- High-level reads (supported PIDs, PID value, DTCs) → Task 4 ✅
- Probe rewired onto the Core API → Task 5 ✅
- **Deliberately deferred:** Avalonia UI; ELM327/Bluetooth adapter; engine-on dynamic-value verification.

**Placeholder scan:** No TBD/TODO. The `NotImplementedException` stubs in Task 2 are fully replaced in Tasks 3–4 (not left dangling). The probe (Task 5) has no unit tests by design (manual tool) and is verified by build + smoke + manual run.

**Type consistency:** `KLineObdSession(IEcuTransport, IBreakLine, byte, Func<TimeSpan,Task>?)`, `RequestAsync(byte[])→ObdResponse`, `ConnectAsync()`, `ReadSupportedPidsAsync()→IReadOnlyList<byte>`, `ReadPidAsync(byte)→PidReading`, `ReadDtcsAsync()→IReadOnlyList<string>`; reuse of `KLineFiveBaudInitializer.InitializeAsync(IBreakLine, byte, ct)`, `IBreakLine.SetBreak`, `ObdMessage`, `SupportedPids.Parse`, `PidDecoder.Decode`, `DtcDecoder.Decode`, `ObdResponse(ServiceId, Payload)`, `EcuConnectionException`, and `SerialPortTransport` (now `IEcuTransport, IBreakLine`) are consistent across tasks and with plans 1–6. Tests use `using AwesomeAssertions;`.
