# OpenECU Serial/VCP Transport — Implementation Plan (Plan 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide a real, cross-platform serial (Virtual COM Port) `IEcuTransport` so OpenECU can talk to actual hardware — the user's FTDI KKL cable (live on COM8), the OBDLink LX over Bluetooth SPP, and CH340 cables — plus a K-line echo-suppression decorator and COM-port enumeration. Pure logic is unit-tested; the System.IO.Ports binding is validated manually against the FTDI cable.

**Architecture:** Adds a concrete tier-1 transport. To keep `OpenEcu.Core` free of native dependencies and fully testable, the pure pieces (the `ISerialPort` seam, `SerialPortTransport` that consumes it, and the `KLineEchoSuppressor` decorator) live in `OpenEcu.Core` and are unit-tested with fakes; the actual `System.IO.Ports` implementation (`SystemSerialPort`) and port enumeration live in a new `OpenEcu.Transport.Serial` project that references Core. The single-wire K-line echoes every transmitted byte back on RX, so `KLineEchoSuppressor` drains that echo after each write — resolving the echo concern deferred from plan 2.

**Tech Stack:** .NET 8 (C# 12), `System.IO.Ports` 8.0.0, xUnit, FluentAssertions. Builds on plans 1–2.

**Scope note:** Plan 3 of several. Independently shippable: a unit-tested serial transport plus a documented hardware bring-up. It deliberately excludes the ECU **wake-up / init timing sequence** (5-baud / fast-init, the timing-sensitive part that needs the running bike) — that is **plan 4**, which builds on this transport. It also excludes raw FTDI D2XX (later optional) and the ELM327 adapter (later).

**Prerequisite:** Plans 1–2 implemented and on `main`: `IEcuTransport`, `SimulatedTransport`, `KLineMode`, the codec, `IEcuAdapter`/`KLineProtocol`. The user's FTDI FT232R cable enumerates as **COM8** (this may differ on other machines — the manual test notes how to find the right port).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.Core/Transport/KLineEchoSuppressor.cs` | Decorator: drains the K-line TX echo after each write |
| `src/OpenEcu.Transport.Serial/OpenEcu.Transport.Serial.csproj` | New project (net8.0) referencing Core + System.IO.Ports |
| `src/OpenEcu.Transport.Serial/ISerialPort.cs` | Minimal serial-port seam (for testability) |
| `src/OpenEcu.Transport.Serial/SerialPortTransport.cs` | `IEcuTransport` over an `ISerialPort` |
| `src/OpenEcu.Transport.Serial/SystemSerialPort.cs` | Real `ISerialPort` using `System.IO.Ports.SerialPort` |
| `src/OpenEcu.Transport.Serial/SerialPortEnumerator.cs` | Lists available COM port names |
| `tests/OpenEcu.Core.Tests/Transport/KLineEchoSuppressorTests.cs` | Echo-suppressor unit tests |
| `tests/OpenEcu.Transport.Serial.Tests/OpenEcu.Transport.Serial.Tests.csproj` | New xUnit project |
| `tests/OpenEcu.Transport.Serial.Tests/FakeSerialPort.cs` | Test double implementing `ISerialPort` |
| `tests/OpenEcu.Transport.Serial.Tests/SerialPortTransportTests.cs` | Transport unit tests (via fake) |
| `tests/OpenEcu.Transport.Serial.Tests/ManualHardwareTests.cs` | Skip-by-default test against the real cable |

**Note on placement:** `SerialPortTransport` and `ISerialPort` are pure (no native deps), so they could live in Core. They are placed in `OpenEcu.Transport.Serial` (with `SystemSerialPort`) so all serial concerns live together, per design §5. `KLineEchoSuppressor` is K-line-specific but transport-agnostic and pure, so it lives in Core for reuse by every transport.

**SDK note:** This machine has .NET SDK 10, which (a) writes solutions as `OpenEcu.slnx` and (b) rejects `dotnet new <template> -f net8.0`. Create projects with the default template, then overwrite the `.csproj` with the net8.0 XML shown below. `dotnet sln add`, `dotnet build`, and `dotnet test` all work against `OpenEcu.slnx`.

---

### Task 1: KLineEchoSuppressor (in Core)

**Files:**
- Create: `src/OpenEcu.Core/Transport/KLineEchoSuppressor.cs`
- Test: `tests/OpenEcu.Core.Tests/Transport/KLineEchoSuppressorTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Transport/KLineEchoSuppressorTests.cs`:
```csharp
using FluentAssertions;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Transport;

public class KLineEchoSuppressorTests
{
    [Fact]
    public async Task Write_drains_the_echoed_bytes_so_reads_see_only_the_reply()
    {
        var inner = new SimulatedTransport();
        await inner.OpenAsync();
        // On a K-line, the write is echoed back first, then the ECU reply arrives.
        inner.EnqueueResponse(new byte[] { 0x81, 0xD5, 0xF5, 0x81, 0xCC }); // echo of the request
        inner.EnqueueResponse(new byte[] { 0xC1, 0xEA, 0x8F });             // the reply

        var suppressor = new KLineEchoSuppressor(inner);
        await suppressor.WriteAsync(new byte[] { 0x81, 0xD5, 0xF5, 0x81, 0xCC });

        inner.Written.Should().Equal(0x81, 0xD5, 0xF5, 0x81, 0xCC);

        var buffer = new byte[3];
        int n = await suppressor.ReadAsync(buffer);
        n.Should().Be(3);
        buffer.Should().Equal(0xC1, 0xEA, 0x8F);
    }

    [Fact]
    public async Task Write_throws_when_echo_is_incomplete()
    {
        var inner = new SimulatedTransport();
        await inner.OpenAsync();
        inner.EnqueueResponse(new byte[] { 0x81, 0xD5 }); // only 2 of 5 echo bytes

        var suppressor = new KLineEchoSuppressor(inner);
        var act = async () => await suppressor.WriteAsync(new byte[] { 0x81, 0xD5, 0xF5, 0x81, 0xCC });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task IsOpen_and_lifecycle_delegate_to_inner()
    {
        var inner = new SimulatedTransport();
        var suppressor = new KLineEchoSuppressor(inner);

        suppressor.IsOpen.Should().BeFalse();
        await suppressor.OpenAsync();
        suppressor.IsOpen.Should().BeTrue();
        await suppressor.CloseAsync();
        suppressor.IsOpen.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter KLineEchoSuppressorTests`
Expected: FAIL — `KLineEchoSuppressor` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/OpenEcu.Core/Transport/KLineEchoSuppressor.cs`:
```csharp
namespace OpenEcu.Core.Transport;

/// <summary>
/// Decorates a transport on a single-wire K-line bus, where every transmitted byte is
/// echoed back on RX. After each write it drains exactly that many echoed bytes so the
/// next read returns only the ECU's reply.
/// </summary>
public sealed class KLineEchoSuppressor : IEcuTransport
{
    private readonly IEcuTransport _inner;

    public KLineEchoSuppressor(IEcuTransport inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool IsOpen => _inner.IsOpen;
    public Task OpenAsync(CancellationToken ct = default) => _inner.OpenAsync(ct);
    public Task CloseAsync() => _inner.CloseAsync();
    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _inner.ReadAsync(buffer, ct);
    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        await _inner.WriteAsync(data, ct);
        await DrainEchoAsync(data.Length, ct);
    }

    private async Task DrainEchoAsync(int count, CancellationToken ct)
    {
        byte[] scratch = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n = await _inner.ReadAsync(scratch.AsMemory(got, count - got), ct);
            if (n == 0)
                throw new InvalidOperationException($"Expected a {count}-byte echo but received {got}.");
            got += n;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter KLineEchoSuppressorTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Transport/KLineEchoSuppressor.cs tests/OpenEcu.Core.Tests/Transport/KLineEchoSuppressorTests.cs
git commit -m "feat: K-line echo-suppressor transport decorator"
```

---

### Task 2: Scaffold OpenEcu.Transport.Serial + test project

**Files:**
- Create: `src/OpenEcu.Transport.Serial/OpenEcu.Transport.Serial.csproj`
- Create: `tests/OpenEcu.Transport.Serial.Tests/OpenEcu.Transport.Serial.Tests.csproj`

- [ ] **Step 1: Create the projects**

Run from repo root:
```bash
dotnet new classlib -n OpenEcu.Transport.Serial -o src/OpenEcu.Transport.Serial
dotnet new xunit -n OpenEcu.Transport.Serial.Tests -o tests/OpenEcu.Transport.Serial.Tests
rm src/OpenEcu.Transport.Serial/Class1.cs
rm tests/OpenEcu.Transport.Serial.Tests/UnitTest1.cs
```

- [ ] **Step 2: Overwrite the library csproj**

Replace `src/OpenEcu.Transport.Serial/OpenEcu.Transport.Serial.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>12</LangVersion>
    <RootNamespace>OpenEcu.Transport.Serial</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenEcu.Core\OpenEcu.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Overwrite the test csproj target framework**

Edit `tests/OpenEcu.Transport.Serial.Tests/OpenEcu.Transport.Serial.Tests.csproj` and set `<TargetFramework>net8.0</TargetFramework>` (replace whatever the template generated, e.g. `net10.0`). Leave the rest of the file as generated.

- [ ] **Step 4: Add references, package, and solution entries**

```bash
dotnet add src/OpenEcu.Transport.Serial package System.IO.Ports --version 8.0.0
dotnet add tests/OpenEcu.Transport.Serial.Tests reference src/OpenEcu.Transport.Serial/OpenEcu.Transport.Serial.csproj
dotnet add tests/OpenEcu.Transport.Serial.Tests reference src/OpenEcu.Core/OpenEcu.Core.csproj
dotnet add tests/OpenEcu.Transport.Serial.Tests package FluentAssertions
dotnet sln add src/OpenEcu.Transport.Serial/OpenEcu.Transport.Serial.csproj
dotnet sln add tests/OpenEcu.Transport.Serial.Tests/OpenEcu.Transport.Serial.Tests.csproj
```

- [ ] **Step 5: Verify the solution still builds and all tests pass**

Run: `dotnet test`
Expected: build succeeds; existing tests still pass (plan 1+2 plus Task 1 = 34 passed); the new test project contributes 0 tests so far.

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Transport.Serial tests/OpenEcu.Transport.Serial.Tests OpenEcu.slnx
git commit -m "chore: scaffold OpenEcu.Transport.Serial project + tests"
```

---

### Task 3: ISerialPort seam + SerialPortTransport

**Files:**
- Create: `src/OpenEcu.Transport.Serial/ISerialPort.cs`
- Create: `src/OpenEcu.Transport.Serial/SerialPortTransport.cs`
- Create: `tests/OpenEcu.Transport.Serial.Tests/FakeSerialPort.cs`
- Test: `tests/OpenEcu.Transport.Serial.Tests/SerialPortTransportTests.cs`

- [ ] **Step 1: Write the seam and the test double**

Create `src/OpenEcu.Transport.Serial/ISerialPort.cs`:
```csharp
namespace OpenEcu.Transport.Serial;

/// <summary>
/// Minimal abstraction over a serial port, so SerialPortTransport can be unit-tested
/// without a physical device. The real implementation is SystemSerialPort.
/// </summary>
public interface ISerialPort : IAsyncDisposable
{
    bool IsOpen { get; }
    void Open();
    void Close();
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
    Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
}
```

Create `tests/OpenEcu.Transport.Serial.Tests/FakeSerialPort.cs`:
```csharp
using OpenEcu.Transport.Serial;

namespace OpenEcu.Transport.Serial.Tests;

/// <summary>In-memory ISerialPort: records writes, replays scripted reads, tracks open state.</summary>
public sealed class FakeSerialPort : ISerialPort
{
    private readonly List<byte> _written = new();
    private readonly Queue<byte> _toRead = new();

    public bool IsOpen { get; private set; }
    public IReadOnlyList<byte> Written => _written;

    public void EnqueueRead(params byte[] data)
    {
        foreach (byte b in data) _toRead.Enqueue(b);
    }

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        _written.AddRange(data.ToArray());
        return Task.CompletedTask;
    }

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int i = 0;
        while (i < buffer.Length && _toRead.Count > 0)
            buffer.Span[i++] = _toRead.Dequeue();
        return Task.FromResult(i);
    }

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/OpenEcu.Transport.Serial.Tests/SerialPortTransportTests.cs`:
```csharp
using FluentAssertions;
using OpenEcu.Core.Transport;
using OpenEcu.Transport.Serial;
using Xunit;

namespace OpenEcu.Transport.Serial.Tests;

public class SerialPortTransportTests
{
    [Fact]
    public async Task OpenAsync_opens_underlying_port()
    {
        var fake = new FakeSerialPort();
        IEcuTransport transport = new SerialPortTransport(fake);

        transport.IsOpen.Should().BeFalse();
        await transport.OpenAsync();
        transport.IsOpen.Should().BeTrue();
        fake.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_delegates_to_port()
    {
        var fake = new FakeSerialPort();
        IEcuTransport transport = new SerialPortTransport(fake);
        await transport.OpenAsync();

        await transport.WriteAsync(new byte[] { 0x10, 0x20 });

        fake.Written.Should().Equal(0x10, 0x20);
    }

    [Fact]
    public async Task ReadAsync_returns_bytes_from_port()
    {
        var fake = new FakeSerialPort();
        fake.EnqueueRead(0xAA, 0xBB);
        IEcuTransport transport = new SerialPortTransport(fake);
        await transport.OpenAsync();

        var buffer = new byte[2];
        int n = await transport.ReadAsync(buffer);

        n.Should().Be(2);
        buffer.Should().Equal(0xAA, 0xBB);
    }

    [Fact]
    public async Task WriteAsync_before_open_throws()
    {
        var fake = new FakeSerialPort();
        IEcuTransport transport = new SerialPortTransport(fake);

        var act = async () => await transport.WriteAsync(new byte[] { 0x01 });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter SerialPortTransportTests`
Expected: FAIL — `SerialPortTransport` does not exist.

- [ ] **Step 4: Write the implementation**

Create `src/OpenEcu.Transport.Serial/SerialPortTransport.cs`:
```csharp
using OpenEcu.Core.Transport;

namespace OpenEcu.Transport.Serial;

/// <summary>An IEcuTransport backed by a serial (Virtual COM Port) device.</summary>
public sealed class SerialPortTransport : IEcuTransport
{
    private readonly ISerialPort _port;

    public SerialPortTransport(ISerialPort port)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
    }

    public bool IsOpen => _port.IsOpen;

    public Task OpenAsync(CancellationToken ct = default)
    {
        _port.Open();
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        _port.Close();
        return Task.CompletedTask;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        EnsureOpen();
        await _port.WriteAsync(data, ct);
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        EnsureOpen();
        return await _port.ReadAsync(buffer, ct);
    }

    public ValueTask DisposeAsync() => _port.DisposeAsync();

    private void EnsureOpen()
    {
        if (!_port.IsOpen)
            throw new InvalidOperationException("Serial port is not open.");
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter SerialPortTransportTests`
Expected: PASS (4 passed).

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Transport.Serial/ISerialPort.cs src/OpenEcu.Transport.Serial/SerialPortTransport.cs tests/OpenEcu.Transport.Serial.Tests/FakeSerialPort.cs tests/OpenEcu.Transport.Serial.Tests/SerialPortTransportTests.cs
git commit -m "feat: SerialPortTransport over an ISerialPort seam"
```

---

### Task 4: SystemSerialPort (real System.IO.Ports implementation)

**Files:**
- Create: `src/OpenEcu.Transport.Serial/SystemSerialPort.cs`

This is the thin binding to `System.IO.Ports.SerialPort`. It is validated by the manual hardware test in Task 6 (it cannot be meaningfully unit-tested without a device).

- [ ] **Step 1: Write the implementation**

Create `src/OpenEcu.Transport.Serial/SystemSerialPort.cs`:
```csharp
using System.IO.Ports;

namespace OpenEcu.Transport.Serial;

/// <summary>ISerialPort backed by System.IO.Ports.SerialPort (FTDI VCP, CH340, BT-SPP, ...).</summary>
public sealed class SystemSerialPort : ISerialPort
{
    private readonly SerialPort _port;

    /// <param name="portName">e.g. "COM8" on Windows or "/dev/ttyUSB0" on Linux.</param>
    /// <param name="baudRate">K-line default is 10400 baud.</param>
    public SystemSerialPort(string portName, int baudRate = 10400, int readTimeoutMs = 2000, int writeTimeoutMs = 2000)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = readTimeoutMs,
            WriteTimeout = writeTimeoutMs
        };
    }

    public bool IsOpen => _port.IsOpen;

    public void Open() => _port.Open();

    public void Close()
    {
        if (_port.IsOpen)
            _port.Close();
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        => _port.BaseStream.WriteAsync(data, ct).AsTask();

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        => _port.BaseStream.ReadAsync(buffer, ct).AsTask();

    public ValueTask DisposeAsync()
    {
        _port.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/OpenEcu.Transport.Serial/SystemSerialPort.cs
git commit -m "feat: SystemSerialPort (System.IO.Ports binding)"
```

---

### Task 5: SerialPortEnumerator

**Files:**
- Create: `src/OpenEcu.Transport.Serial/SerialPortEnumerator.cs`
- Test: `tests/OpenEcu.Transport.Serial.Tests/SerialPortEnumeratorTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Transport.Serial.Tests/SerialPortEnumeratorTests.cs`:
```csharp
using FluentAssertions;
using OpenEcu.Transport.Serial;
using Xunit;

namespace OpenEcu.Transport.Serial.Tests;

public class SerialPortEnumeratorTests
{
    [Fact]
    public void GetPortNames_returns_a_non_null_array()
    {
        // We can't assert specific ports (machine-dependent), but it must never return null
        // or throw, and every entry must be a non-empty string.
        string[] ports = SerialPortEnumerator.GetPortNames();

        ports.Should().NotBeNull();
        ports.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SerialPortEnumeratorTests`
Expected: FAIL — `SerialPortEnumerator` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.Transport.Serial/SerialPortEnumerator.cs`:
```csharp
using System.IO.Ports;

namespace OpenEcu.Transport.Serial;

/// <summary>Lists serial ports available on this machine.</summary>
public static class SerialPortEnumerator
{
    /// <summary>Returns the names of available serial ports (e.g. "COM8", "/dev/ttyUSB0").</summary>
    public static string[] GetPortNames() => SerialPort.GetPortNames();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter SerialPortEnumeratorTests`
Expected: PASS (1 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Transport.Serial/SerialPortEnumerator.cs tests/OpenEcu.Transport.Serial.Tests/SerialPortEnumeratorTests.cs
git commit -m "feat: serial port enumeration"
```

---

### Task 6: Manual hardware bring-up test (skip-by-default)

**Files:**
- Create: `tests/OpenEcu.Transport.Serial.Tests/ManualHardwareTests.cs`

A real-device smoke test, skipped in normal runs (so CI/`dotnet test` stays green with no hardware). It confirms the serial stack can open the FTDI cable, write, and close without error. It does NOT require the motorcycle — it only validates the host-side serial path. (Talking to the ECU needs the wake/init sequence from plan 4 and the bike powered on.)

- [ ] **Step 1: Write the manual test**

Create `tests/OpenEcu.Transport.Serial.Tests/ManualHardwareTests.cs`:
```csharp
using FluentAssertions;
using OpenEcu.Core.Transport;
using OpenEcu.Transport.Serial;
using Xunit;

namespace OpenEcu.Transport.Serial.Tests;

public class ManualHardwareTests
{
    // The FTDI cable enumerates as a COM port. Find yours via Device Manager or by calling
    // SerialPortEnumerator.GetPortNames(). On the author's machine it is COM8.
    private const string PortName = "COM8";

    [Fact(Skip = "Manual: requires the FTDI KKL cable plugged in. Set PortName, then remove Skip and run.")]
    public async Task Can_open_write_and_close_the_real_cable()
    {
        await using var port = new SystemSerialPort(PortName, baudRate: 10400, readTimeoutMs: 1000, writeTimeoutMs: 1000);
        IEcuTransport transport = new SerialPortTransport(port);

        await transport.OpenAsync();
        transport.IsOpen.Should().BeTrue();

        // Writing is safe even with nothing on the K-line; this just exercises the path.
        await transport.WriteAsync(new byte[] { 0x00 });

        await transport.CloseAsync();
        transport.IsOpen.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Verify the skipped test is collected but not run**

Run: `dotnet test --filter ManualHardwareTests`
Expected: 1 test found, 0 run / 1 skipped (skipped tests count as passing).

- [ ] **Step 3: Run the full suite**

Run: `dotnet test`
Expected: PASS — plans 1+2 (31) + Task 1 (3) + Task 3 (4) + Task 5 (1) = 39 passed, 1 skipped.

- [ ] **Step 4: Commit**

```bash
git add tests/OpenEcu.Transport.Serial.Tests/ManualHardwareTests.cs
git commit -m "test: manual hardware bring-up smoke test (skip-by-default)"
```

---

## Manual Verification (after merge, with the cable)

These steps are for the human, not the automated suite:

1. Plug in the FTDI KKL cable. Confirm its COM port with `SerialPortEnumerator.GetPortNames()` (or Device Manager → Ports). It was **COM8** during planning.
2. In `ManualHardwareTests.cs`, set `PortName` to the right port and remove the `Skip` argument.
3. Run `dotnet test --filter ManualHardwareTests`. Expected: it opens, writes, and closes the port with no exception — proving the host serial stack works end-to-end with the real cable.
4. Re-add the `Skip` before committing (keep the suite hardware-free).

Talking to the actual ECU (connect + read ECU ID on the running bike) arrives in **plan 4**, which adds the K-line wake/init sequence on top of this transport.

---

## Self-Review

**Spec coverage (this plan's slice of design §6):**
- Cross-platform serial/VCP transport (`SerialPortTransport` + `SystemSerialPort`) → Tasks 3–4 ✅
- Works with FTDI VCP, CH340, and Bluetooth-SPP (all surface as serial ports) → by construction ✅
- K-line echo suppression (deferred from plan 2) → Task 1 ✅
- Port enumeration for the connection UI → Task 5 ✅
- Hardware bring-up path → Task 6 + Manual Verification ✅
- **Deliberately deferred (noted in scope):** ECU wake/init timing (plan 4); raw FTDI D2XX transport (later optional); `BluetoothClassicTransport` discovery convenience and `Elm327Adapter` (later); BLE (later).

**Placeholder scan:** No TBD/TODO. The manual test is intentionally `Skip`-marked (documented), not an unfinished stub.

**Type consistency:** `IEcuTransport` members (`IsOpen`, `OpenAsync`, `CloseAsync`, `WriteAsync`, `ReadAsync`, `DisposeAsync`) are implemented consistently by `KLineEchoSuppressor` and `SerialPortTransport`; `ISerialPort` members (`IsOpen`, `Open`, `Close`, `WriteAsync`, `ReadAsync`, `DisposeAsync`) match between `FakeSerialPort` and `SystemSerialPort` and are consumed correctly by `SerialPortTransport`; `SimulatedTransport` (`EnqueueResponse`, `Written`) and `FakeSerialPort` (`EnqueueRead`, `Written`) helpers are used consistently in their respective tests.
