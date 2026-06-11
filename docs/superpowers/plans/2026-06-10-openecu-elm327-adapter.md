# OpenECU ELM327 / Bluetooth Adapter — Implementation Plan (Plan 14)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Support ELM327-class adapters (OBDLink LX over Bluetooth) by adding an `Elm327ObdSession : IObdSession` (AT command set + OBD requests over a serial transport, parsing the ASCII hex replies and reusing the existing decoders) and an **adapter picker** (Cable vs ELM327) in the connection bar.

**Architecture:** A paired Bluetooth ELM327 surfaces as a COM port, so the transport is the existing `SerialPortTransport`. `Elm327ObdSession` drives the adapter with AT commands, parses responses via a testable `Elm327Response.TryParse`, and decodes with the existing `SupportedPids`/`PidDecoder`/`DtcDecoder`. `ConnectionFactory` builds either a `KLineObdSession` (FTDI cable) or an `Elm327ObdSession` (ELM327) based on the chosen adapter — both implement `IObdSession`, so `LiveDataService` and the whole UI are unchanged.

**Tech Stack:** .NET 8, Avalonia 11.0.10, xUnit, **AwesomeAssertions**. Builds on plans 1–13.

**Prerequisite:** Plans 1–13 on `main` (`IObdSession`, `SupportedPids`, `PidDecoder`, `DtcDecoder`, `SimulatedTransport`, `ConnectionFactory`, `MainViewModel`, `AppSettings`).

**Note:** the bike uses ISO9141-2 OBD-II, which ELM327 handles via auto-protocol. Real validation is on the human's OBDLink LX (paired → COM port) through the diagnostic-port adapter.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.Core/Obd/Elm327Response.cs` | Parse ELM327 ASCII replies → bytes (strip noise/errors) |
| `src/OpenEcu.Core/Obd/Elm327ObdSession.cs` | `IObdSession` over an ELM327 adapter |
| `src/OpenEcu.App/Services/ConnectionFactory.cs` | **Modify:** `AdapterKind` + build Cable or ELM327 |
| `src/OpenEcu.App/Model/AppSettings.cs` | **Modify:** persist `Adapter` |
| `src/OpenEcu.App/ViewModels/MainViewModel.cs` | **Modify:** `SelectedAdapter` (persisted) |
| `src/OpenEcu.Desktop/Views/MainWindow.axaml` | **Modify:** adapter dropdown |
| tests | new + updated |

---

### Task 1: Elm327Response parser

**Files:**
- Create: `src/OpenEcu.Core/Obd/Elm327Response.cs`
- Test: `tests/OpenEcu.Core.Tests/Obd/Elm327ResponseTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Obd/Elm327ResponseTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class Elm327ResponseTests
{
    [Fact]
    public void Parses_hex_data_line()
    {
        Elm327Response.TryParse("4100BE1E9011\r", out byte[] bytes).Should().BeTrue();
        bytes.Should().Equal(0x41, 0x00, 0xBE, 0x1E, 0x90, 0x11);
    }

    [Fact]
    public void Strips_spaces_and_the_searching_line()
    {
        Elm327Response.TryParse("SEARCHING...\r41 0C 1A F8\r", out byte[] bytes).Should().BeTrue();
        bytes.Should().Equal(0x41, 0x0C, 0x1A, 0xF8);
    }

    [Fact]
    public void No_data_is_an_error()
    {
        Elm327Response.TryParse("NO DATA\r", out _).Should().BeFalse();
    }

    [Fact]
    public void Question_mark_and_unable_to_connect_are_errors()
    {
        Elm327Response.TryParse("?\r", out _).Should().BeFalse();
        Elm327Response.TryParse("UNABLE TO CONNECT\r", out _).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter Elm327ResponseTests`
Expected: FAIL — `Elm327Response` does not exist.

- [ ] **Step 3: Write the parser**

Create `src/OpenEcu.Core/Obd/Elm327Response.cs`:
```csharp
namespace OpenEcu.Core.Obd;

/// <summary>Parses an ELM327 ASCII reply into raw bytes, rejecting error/no-data replies.</summary>
public static class Elm327Response
{
    private static readonly string[] Errors =
        { "NO DATA", "UNABLE", "ERROR", "STOPPED", "?", "BUFFER FULL", "CAN ERROR" };

    public static bool TryParse(string raw, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        string upper = raw.ToUpperInvariant();
        foreach (string e in Errors)
            if (upper.Contains(e))
                return false;

        var hex = new System.Text.StringBuilder();
        foreach (string line in raw.Split('\r', '\n'))
        {
            string s = line.Replace(" ", "").Trim();
            if (s.Length > 0 && IsHex(s))
                hex.Append(s);
        }

        if (hex.Length < 2 || hex.Length % 2 != 0)
            return false;

        var result = new byte[hex.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(hex.ToString(i * 2, 2), 16);
        bytes = result;
        return true;
    }

    private static bool IsHex(string s)
    {
        foreach (char ch in s)
            if (!Uri.IsHexDigit(ch))
                return false;
        return true;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter Elm327ResponseTests`
Expected: PASS (4 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Obd/Elm327Response.cs tests/OpenEcu.Core.Tests/Obd/Elm327ResponseTests.cs
git commit -m "feat: ELM327 ASCII response parser"
```

---

### Task 2: Elm327ObdSession

**Files:**
- Create: `src/OpenEcu.Core/Obd/Elm327ObdSession.cs`
- Test: `tests/OpenEcu.Core.Tests/Obd/Elm327ObdSessionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Obd/Elm327ObdSessionTests.cs`:
```csharp
using System.Text;
using AwesomeAssertions;
using OpenEcu.Core.Obd;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class Elm327ObdSessionTests
{
    // Scripts a SimulatedTransport with ELM327 ASCII replies (each ends with the '>' prompt).
    private static async Task<SimulatedTransport> Open(params string[] replies)
    {
        var t = new SimulatedTransport();
        await t.OpenAsync();
        foreach (string r in replies)
            t.EnqueueResponse(Encoding.ASCII.GetBytes(r + "\r>"));
        return t;
    }

    [Fact]
    public async Task ConnectAsync_runs_at_setup_then_confirms_with_0100()
    {
        // 6 AT commands then 0100.
        var t = await Open("ELM327 v1.5", "OK", "OK", "OK", "OK", "OK", "4100BE1E9011");
        var s = new Elm327ObdSession(t);

        await s.ConnectAsync();

        s.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task ReadPidAsync_decodes_rpm()
    {
        var t = await Open("410C0BB8"); // 0x0BB8 / 4 = 750 rpm
        var s = new Elm327ObdSession(t);

        PidReading r = await s.ReadPidAsync(0x0C);

        r.Value.Should().Be(750);
    }

    [Fact]
    public async Task ReadDtcsAsync_decodes_codes()
    {
        var t = await Open("431502");
        var s = new Elm327ObdSession(t);

        var dtcs = await s.ReadDtcsAsync();

        dtcs.Should().Equal("P1502");
    }

    [Fact]
    public async Task ReadSupportedPidsAsync_parses_the_bitmask()
    {
        // 0100 -> supported; 0x20 set so it asks 0120; then stop.
        var t = await Open("4100BE1E9011", "412000000001", "414000000000");
        var s = new Elm327ObdSession(t);

        var pids = await s.ReadSupportedPidsAsync();

        pids.Should().Contain(new byte[] { 0x0C, 0x05, 0x11 });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter Elm327ObdSessionTests`
Expected: FAIL — `Elm327ObdSession` does not exist.

- [ ] **Step 3: Write the session**

Create `src/OpenEcu.Core/Obd/Elm327ObdSession.cs`:
```csharp
using System.Text;
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Obd;

/// <summary>
/// An OBD session over an ELM327-class adapter (e.g. OBDLink LX). Sends the AT command set
/// and OBD-mode hex requests over a serial/Bluetooth transport and decodes the ASCII replies.
/// </summary>
public sealed class Elm327ObdSession : IObdSession
{
    private readonly IEcuTransport _transport;

    public Elm327ObdSession(IEcuTransport transport) =>
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await CommandAsync("ATZ", ct);   // reset
        await CommandAsync("ATE0", ct);  // echo off
        await CommandAsync("ATL0", ct);  // no linefeeds
        await CommandAsync("ATS0", ct);  // no spaces
        await CommandAsync("ATH0", ct);  // headers off
        await CommandAsync("ATSP0", ct); // auto protocol

        string r = await CommandAsync("0100", ct);
        if (!Elm327Response.TryParse(r, out _))
            throw new EcuConnectionException($"ELM327 got no OBD response to 0100: '{r.Trim()}'.");
        IsConnected = true;
    }

    public async Task<IReadOnlyList<byte>> ReadSupportedPidsAsync(CancellationToken ct = default)
    {
        var all = new List<byte>();
        foreach (byte basePid in new byte[] { 0x00, 0x20, 0x40 })
        {
            string raw = await CommandAsync($"01{basePid:X2}", ct);
            if (!Elm327Response.TryParse(raw, out byte[] b) || b.Length < 6 || b[0] != 0x41)
                break;
            IReadOnlyList<byte> pids = SupportedPids.Parse(basePid, b.AsSpan(2, 4));
            all.AddRange(pids);
            if (!pids.Contains((byte)(basePid + 0x20)))
                break;
        }
        return all;
    }

    public async Task<PidReading> ReadPidAsync(byte pid, CancellationToken ct = default)
    {
        string raw = await CommandAsync($"01{pid:X2}", ct);
        if (!Elm327Response.TryParse(raw, out byte[] b) || b.Length < 2 || b[0] != 0x41 || b[1] != pid)
            return new PidReading(pid, $"PID {pid:X2}", null, "", Array.Empty<byte>());
        return PidDecoder.Decode(pid, b.AsSpan(2));
    }

    public async Task<IReadOnlyList<string>> ReadDtcsAsync(CancellationToken ct = default)
    {
        string raw = await CommandAsync("03", ct);
        if (!Elm327Response.TryParse(raw, out byte[] b) || b.Length < 1 || b[0] != 0x43)
            return Array.Empty<string>();
        return DtcDecoder.Decode(b.AsSpan(1));
    }

    public async Task ClearDtcsAsync(CancellationToken ct = default) => await CommandAsync("04", ct);

    public ValueTask DisposeAsync() => _transport.DisposeAsync();

    private async Task<string> CommandAsync(string command, CancellationToken ct)
    {
        await _transport.WriteAsync(Encoding.ASCII.GetBytes(command + "\r"), ct);
        return await ReadUntilPromptAsync(ct);
    }

    private async Task<string> ReadUntilPromptAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new byte[64];
        while (true)
        {
            int n = await _transport.ReadAsync(buffer, ct);
            if (n == 0) break; // idle/timeout
            for (int i = 0; i < n; i++)
            {
                char ch = (char)buffer[i];
                if (ch == '>') return sb.ToString();
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter Elm327ObdSessionTests`
Expected: PASS (4 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Obd/Elm327ObdSession.cs tests/OpenEcu.Core.Tests/Obd/Elm327ObdSessionTests.cs
git commit -m "feat: Elm327ObdSession (OBD over an ELM327 adapter)"
```

---

### Task 3: Adapter selection (factory + settings + view-model)

**Files:**
- Modify: `src/OpenEcu.App/Services/ConnectionFactory.cs`
- Modify: `src/OpenEcu.App/Model/AppSettings.cs`
- Modify: `src/OpenEcu.App/ViewModels/MainViewModel.cs`
- Modify: `tests/OpenEcu.App.Tests/MainViewModelTests.cs`, `MainViewModelSettingsTests.cs`, `ConnectionFactoryTests.cs`
- Test: new assertions in those files

- [ ] **Step 1: Add `AdapterKind` and branch the factory**

In `src/OpenEcu.App/Services/ConnectionFactory.cs`, add the enum and change `Create` to take a kind. Replace the file's `IConnectionFactory` + `ConnectionFactory` with:
```csharp
public enum AdapterKind { Cable, Elm327 }

public interface IConnectionFactory
{
    LiveConnection Create(string portName, AdapterKind kind = AdapterKind.Cable);
}

/// <summary>Builds either a K-line (FTDI cable) or an ELM327 session for the chosen adapter.</summary>
public sealed class ConnectionFactory : IConnectionFactory
{
    public LiveConnection Create(string portName, AdapterKind kind = AdapterKind.Cable)
    {
        if (kind == AdapterKind.Elm327)
        {
            var btPort = new SystemSerialPort(portName, baudRate: 115200, readTimeoutMs: 2000, writeTimeoutMs: 1000);
            var btSerial = new SerialPortTransport(btPort);
            var btLog = new LoggingTransport(btSerial);
            return new LiveConnection(new LiveDataService(new Elm327ObdSession(btLog)), btLog);
        }

        var port = new SystemSerialPort(portName, baudRate: 10400, readTimeoutMs: 300, writeTimeoutMs: 1000);
        var serial = new SerialPortTransport(port);
        var log = new LoggingTransport(serial);
        return new LiveConnection(new LiveDataService(new KLineObdSession(log, serial)), log);
    }
}
```
(Keep the existing `using` directives and add `using OpenEcu.Core.Obd;` if not present — it's needed for `Elm327ObdSession`.)

- [ ] **Step 2: Persist the adapter in AppSettings**

In `src/OpenEcu.App/Model/AppSettings.cs`, add next to `Accent`:
```csharp
    public string Adapter { get; set; } = "Cable";
```

- [ ] **Step 3: Update the test fakes + add coverage**

The `IConnectionFactory.Create` signature changed, so update the in-test fakes:

In `tests/OpenEcu.App.Tests/MainViewModelTests.cs`, change `FakeFactory.Create` to:
```csharp
        public LiveConnection Create(string portName, AdapterKind kind = AdapterKind.Cable)
```

In `tests/OpenEcu.App.Tests/MainViewModelSettingsTests.cs`, change `NullFactory.Create` to:
```csharp
        public LiveConnection Create(string portName, AdapterKind kind = AdapterKind.Cable) =>
```
(keep its body). Add `using OpenEcu.App.Services;` if not already present (for `AdapterKind`).

In `tests/OpenEcu.App.Tests/ConnectionFactoryTests.cs`, add a test:
```csharp
    [Fact]
    public void Create_builds_an_elm327_connection_without_opening()
    {
        var conn = new ConnectionFactory().Create("COM_NONEXISTENT", AdapterKind.Elm327);
        conn.Service.Should().NotBeNull();
        conn.Log.IsOpen.Should().BeFalse();
    }
```

- [ ] **Step 4: Add `SelectedAdapter` to MainViewModel**

In `src/OpenEcu.App/ViewModels/MainViewModel.cs`:
- In the constructor, after `_accent = _settings.Accent;` add:
```csharp
        _selectedAdapter = Enum.TryParse<AdapterKind>(_settings.Adapter, out var a) ? a : AdapterKind.Cable;
```
- Add the property + persistence + list (next to `_accent`):
```csharp
    [ObservableProperty] private AdapterKind _selectedAdapter;

    public IReadOnlyList<AdapterKind> Adapters { get; } = new[] { AdapterKind.Cable, AdapterKind.Elm327 };

    partial void OnSelectedAdapterChanged(AdapterKind value) { _settings.Adapter = value.ToString(); _settings.Save(_settingsPath); }
```
- In `ConnectAsync`, change the factory call from `_factory.Create(SelectedPort)` to:
```csharp
            _connection = _factory.Create(SelectedPort, SelectedAdapter);
```

- [ ] **Step 5: Add a settings-persistence test (append to MainViewModelSettingsTests)**

Add inside `MainViewModelSettingsTests`, before the closing brace:
```csharp
    [Fact]
    public void Adapter_defaults_to_cable_and_persists()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oe-{Guid.NewGuid():N}.json");
        try
        {
            var vm = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            vm.SelectedAdapter.Should().Be(AdapterKind.Cable);

            vm.SelectedAdapter = AdapterKind.Elm327;
            var reloaded = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            reloaded.SelectedAdapter.Should().Be(AdapterKind.Elm327);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
```

- [ ] **Step 6: Run tests to verify**

Run: `dotnet test --filter "ConnectionFactoryTests|MainViewModelTests|MainViewModelSettingsTests"`
Expected: PASS (existing + the new adapter tests).

- [ ] **Step 7: Commit**

```bash
git add src/OpenEcu.App/Services/ConnectionFactory.cs src/OpenEcu.App/Model/AppSettings.cs src/OpenEcu.App/ViewModels/MainViewModel.cs tests/OpenEcu.App.Tests/ConnectionFactoryTests.cs tests/OpenEcu.App.Tests/MainViewModelTests.cs tests/OpenEcu.App.Tests/MainViewModelSettingsTests.cs
git commit -m "feat: adapter selection (Cable vs ELM327), persisted"
```

---

### Task 4: Adapter dropdown in the connection bar

**Files:**
- Modify: `src/OpenEcu.Desktop/Views/MainWindow.axaml`

- [ ] **Step 1: Add the dropdown**

In `src/OpenEcu.Desktop/Views/MainWindow.axaml`, add an adapter `ComboBox` in the connection bar (after the port `ComboBox`, before `Refresh`):
```xml
        <ComboBox ItemsSource="{Binding Adapters}" SelectedItem="{Binding SelectedAdapter}" MinWidth="90" />
```

- [ ] **Step 2: Build + full suite**

Run: `dotnet build src/OpenEcu.Desktop`
Expected: build succeeds.

Run: `dotnet test`
Expected: PASS — 121 (plan 13) + Task1 (4) + Task2 (4) + Task3 (2 net new: the elm327 factory test + the adapter persistence test) = 131 passed, 1 skipped.

- [ ] **Step 3: Commit**

```bash
git add src/OpenEcu.Desktop/Views/MainWindow.axaml
git commit -m "feat: adapter dropdown (Cable/ELM327) in the connection bar"
```

---

### Task 5: Manual verification (the human, with the OBDLink LX)

1. Pair the OBDLink LX in Windows Bluetooth settings → note the **outgoing COM port** it creates.
2. `dotnet run --project src/OpenEcu.Desktop`
3. Pick that COM port, set the adapter dropdown to **Elm327**, click **Connect**.

- [ ] Status shows Connected; the Dashboard/Diagnostics populate with live values (same decoders, via the LX).
- [ ] The Console shows the ELM327 chatter (`ATZ`, `ATE0`, `0100`, the hex replies).
- [ ] If "no OBD response to 0100": confirm the LX is paired to the right COM port and the diagnostic-port adapter is seated; the Console will show what the LX returned.

---

## Self-Review

**Coverage:** ELM327 reply parsing (Task 1); full `Elm327ObdSession` — connect/supported-PIDs/PID/DTC/clear (Task 2); adapter selection wired through factory + settings + VM + UI (Tasks 3–4). Reuses every decoder and the entire `LiveDataService`/UI stack — both sessions are `IObdSession`.

**Placeholder scan:** No TBD/TODO. ELM327 AT setup commands are the standard init sequence.

**Type consistency:** `Elm327Response.TryParse`; `Elm327ObdSession` implements all `IObdSession` members (incl. `ClearDtcsAsync` from plan 13); `AdapterKind` + `IConnectionFactory.Create(string, AdapterKind)` updated in the concrete factory and both test fakes; `MainViewModel.SelectedAdapter`/`Adapters` + `AppSettings.Adapter`. Reuses `SupportedPids.Parse`, `PidDecoder.Decode`, `DtcDecoder.Decode`, `SimulatedTransport`, `LoggingTransport`, `SerialPortTransport`, `SystemSerialPort`. Tests use `using AwesomeAssertions;`.
