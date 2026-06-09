# OpenECU Core Foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the OpenECU .NET solution and a headless, fully unit-tested K-line message foundation (checksum, request framing, response parsing) that round-trips through a simulated transport — no hardware or UI required.

**Architecture:** A core-first layered solution. This plan builds only the bottom of `OpenEcu.Core`: the wire-level K-line message codec (`Protocol/`) plus the transport abstraction (`Transport/`) with an in-memory `SimulatedTransport` for tests. Everything is derived clean-room from documented protocol behavior (see Task 2), written as fresh original code. Real transports (FTDI/serial/Bluetooth), the ISO/KWP init handshake, diagnostics, maps, and the Avalonia UI are later plans.

**Tech Stack:** .NET 8 (C# 12), xUnit, FluentAssertions.

**Scope note:** This is plan 1 of several. It produces independently-testable software (a green test suite proving the message codec works). It deliberately does **not** implement the connection handshake or any real adapter — those depend on this foundation and get their own plans.

---

## File Structure

| File | Responsibility |
|---|---|
| `OpenEcu.sln` | Solution at repo root |
| `src/OpenEcu.Core/OpenEcu.Core.csproj` | Headless core library (net8.0, nullable on) |
| `src/OpenEcu.Core/Protocol/KLineMode.cs` | Enum: `Iso9141` vs `Kwp2000` framing |
| `src/OpenEcu.Core/Protocol/KLineChecksum.cs` | Additive mod-256 checksum |
| `src/OpenEcu.Core/Protocol/KLineFrameBuilder.cs` | Build a request frame from a payload |
| `src/OpenEcu.Core/Protocol/KLineFrameParser.cs` | Validate + extract payload from a response frame |
| `src/OpenEcu.Core/Transport/IEcuTransport.cs` | Async byte-stream transport abstraction |
| `src/OpenEcu.Core/Transport/SimulatedTransport.cs` | In-memory scriptable transport for tests |
| `tests/OpenEcu.Core.Tests/OpenEcu.Core.Tests.csproj` | xUnit test project |
| `tests/OpenEcu.Core.Tests/Protocol/KLineChecksumTests.cs` | Checksum tests |
| `tests/OpenEcu.Core.Tests/Protocol/KLineFrameBuilderTests.cs` | Builder tests (exact vectors) |
| `tests/OpenEcu.Core.Tests/Protocol/KLineFrameParserTests.cs` | Parser tests |
| `tests/OpenEcu.Core.Tests/Transport/SimulatedTransportTests.cs` | Transport + round-trip tests |
| `docs/protocol/kline.md` | Clean-room protocol notes (behavioral reference) |

**Addressing constants** (used by builder/parser): tester→ECU target `0xD5`, source `0xF5`. These are the Triumph/Sagem values observed in the original. They are hard-coded named constants in this plan and will move into `EcuDefinition` in a later plan (do not over-engineer now).

---

### Task 1: Scaffold the solution and projects

**Files:**
- Create: `OpenEcu.sln`, `src/OpenEcu.Core/OpenEcu.Core.csproj`, `tests/OpenEcu.Core.Tests/OpenEcu.Core.Tests.csproj`

- [ ] **Step 1: Create solution and class library**

Run from repo root (`D:\Dev\Projects\TuneECUv2.5.8`):
```bash
dotnet new sln -n OpenEcu
dotnet new classlib -n OpenEcu.Core -o src/OpenEcu.Core -f net8.0
dotnet new xunit -n OpenEcu.Core.Tests -o tests/OpenEcu.Core.Tests -f net8.0
```

- [ ] **Step 2: Delete the template placeholder files**

```bash
rm src/OpenEcu.Core/Class1.cs
rm tests/OpenEcu.Core.Tests/UnitTest1.cs
```

- [ ] **Step 3: Wire up references and the solution**

```bash
dotnet sln add src/OpenEcu.Core/OpenEcu.Core.csproj
dotnet sln add tests/OpenEcu.Core.Tests/OpenEcu.Core.Tests.csproj
dotnet add tests/OpenEcu.Core.Tests/OpenEcu.Core.Tests.csproj reference src/OpenEcu.Core/OpenEcu.Core.csproj
dotnet add tests/OpenEcu.Core.Tests/OpenEcu.Core.Tests.csproj package FluentAssertions
```

- [ ] **Step 4: Set library project properties**

Replace `src/OpenEcu.Core/OpenEcu.Core.csproj` contents with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>12</LangVersion>
    <RootNamespace>OpenEcu.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Verify the empty solution builds and tests run**

Run: `dotnet test`
Expected: build succeeds; test run reports `Passed! - Failed: 0, Passed: 0` (no tests yet).

- [ ] **Step 6: Commit**

```bash
git add OpenEcu.sln src/ tests/
git commit -m "chore: scaffold OpenEcu.Core solution and test project"
```

---

### Task 2: Document the K-line protocol (clean-room behavioral notes)

**Files:**
- Create: `docs/protocol/kline.md`

This records the observed behavior the codec implements, so later contributors never need to look at decompiled code. No production code in this task.

- [ ] **Step 1: Write the protocol notes**

Create `docs/protocol/kline.md` with exactly this content:
````markdown
# K-line message format (ISO9141 / KWP2000)

Clean-room behavioral notes for the Triumph/Sagem K-line dialect OpenECU targets first.
Derived from observed request/response framing of the original tool. No source was copied.

## Checksum
Trailing byte of every frame = `(sum of all preceding bytes) mod 256`.

## Request frame, ISO9141 mode
```
[0x80 | len] [0xD5] [0xF5] [payload bytes...] [checksum]
```
- byte0 = `0x80 | len`, where `len` = payload length
- byte1 = `0xD5` (target = ECU)
- byte2 = `0xF5` (source = tester)
- then `len` payload bytes
- then checksum over every prior byte

Example: payload `81` → `81 D5 F5 81 CC`  (0x81+0xD5+0xF5+0x81 = 0x2CC → low byte 0xCC)

## Request frame, KWP2000 mode
```
[0x80] [0xD5] [0xF5] [len] [payload bytes...] [checksum]
```
- byte0 = `0x80` (format byte; separate length byte follows)
- byte1 = `0xD5`, byte2 = `0xF5`
- byte3 = `len`
- then `len` payload bytes, then checksum

Example: payload `81` → `80 D5 F5 01 81 CC`

## Response frame
Same shape, with target/source swapped (ECU→tester) and the same trailing checksum.
A response is valid iff its last byte equals the checksum of all preceding bytes.
Payload is extracted by stripping the header (3 bytes ISO, 4 bytes KWP) and the checksum.
````

- [ ] **Step 2: Commit**

```bash
git add docs/protocol/kline.md
git commit -m "docs: clean-room K-line protocol notes"
```

---

### Task 3: KLineChecksum

**Files:**
- Create: `src/OpenEcu.Core/Protocol/KLineChecksum.cs`
- Test: `tests/OpenEcu.Core.Tests/Protocol/KLineChecksumTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Protocol/KLineChecksumTests.cs`:
```csharp
using FluentAssertions;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class KLineChecksumTests
{
    [Fact]
    public void Calculate_sums_bytes_modulo_256()
    {
        // 0x81 + 0xD5 + 0xF5 + 0x81 = 0x2CC -> low byte 0xCC
        byte[] data = { 0x81, 0xD5, 0xF5, 0x81 };
        KLineChecksum.Calculate(data).Should().Be(0xCC);
    }

    [Fact]
    public void Calculate_of_empty_is_zero()
    {
        KLineChecksum.Calculate(ReadOnlySpan<byte>.Empty).Should().Be(0x00);
    }

    [Fact]
    public void Calculate_wraps_past_256()
    {
        byte[] data = { 0xFF, 0x02 }; // 0x101 -> 0x01
        KLineChecksum.Calculate(data).Should().Be(0x01);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter KLineChecksumTests`
Expected: FAIL — build error, `KLineChecksum` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/OpenEcu.Core/Protocol/KLineChecksum.cs`:
```csharp
namespace OpenEcu.Core.Protocol;

/// <summary>Additive mod-256 checksum used by the K-line frame format.</summary>
public static class KLineChecksum
{
    public static byte Calculate(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        foreach (byte b in data)
            sum += b;
        return (byte)sum;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter KLineChecksumTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Protocol/KLineChecksum.cs tests/OpenEcu.Core.Tests/Protocol/KLineChecksumTests.cs
git commit -m "feat: K-line additive checksum"
```

---

### Task 4: KLineMode + KLineFrameBuilder

**Files:**
- Create: `src/OpenEcu.Core/Protocol/KLineMode.cs`
- Create: `src/OpenEcu.Core/Protocol/KLineFrameBuilder.cs`
- Test: `tests/OpenEcu.Core.Tests/Protocol/KLineFrameBuilderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Protocol/KLineFrameBuilderTests.cs`:
```csharp
using FluentAssertions;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class KLineFrameBuilderTests
{
    [Fact]
    public void Builds_iso9141_request_with_length_in_format_byte()
    {
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0x81 }, KLineMode.Iso9141);
        frame.Should().Equal(0x81, 0xD5, 0xF5, 0x81, 0xCC);
    }

    [Fact]
    public void Builds_kwp2000_request_with_separate_length_byte()
    {
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0x81 }, KLineMode.Kwp2000);
        frame.Should().Equal(0x80, 0xD5, 0xF5, 0x01, 0x81, 0xCC);
    }

    [Fact]
    public void Iso9141_multibyte_payload_sets_format_byte_to_0x80_or_length()
    {
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0x10, 0x20, 0x30 }, KLineMode.Iso9141);
        // 0x80|3 = 0x83 ; checksum = 0x83+0xD5+0xF5+0x10+0x20+0x30 = 0x2AD -> 0xAD
        frame.Should().Equal(0x83, 0xD5, 0xF5, 0x10, 0x20, 0x30, 0xAD);
    }

    [Fact]
    public void Rejects_payload_longer_than_63_in_iso_mode()
    {
        var tooLong = new byte[64];
        var act = () => KLineFrameBuilder.BuildRequest(tooLong, KLineMode.Iso9141);
        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter KLineFrameBuilderTests`
Expected: FAIL — `KLineMode` / `KLineFrameBuilder` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/OpenEcu.Core/Protocol/KLineMode.cs`:
```csharp
namespace OpenEcu.Core.Protocol;

/// <summary>Framing variant for a K-line message.</summary>
public enum KLineMode
{
    /// <summary>Length encoded in the low bits of the format byte (0x80 | len).</summary>
    Iso9141,
    /// <summary>Format byte fixed at 0x80, with a separate length byte after the header.</summary>
    Kwp2000
}
```

Create `src/OpenEcu.Core/Protocol/KLineFrameBuilder.cs`:
```csharp
namespace OpenEcu.Core.Protocol;

/// <summary>Builds tester→ECU request frames. See docs/protocol/kline.md.</summary>
public static class KLineFrameBuilder
{
    public const byte TargetEcu = 0xD5;
    public const byte SourceTester = 0xF5;
    private const byte FormatBase = 0x80;
    private const int MaxIsoPayload = 63; // 6-bit length field in the format byte

    public static byte[] BuildRequest(ReadOnlySpan<byte> payload, KLineMode mode)
    {
        if (mode == KLineMode.Iso9141 && payload.Length > MaxIsoPayload)
            throw new ArgumentException(
                $"ISO9141 payload must be <= {MaxIsoPayload} bytes, was {payload.Length}.",
                nameof(payload));

        int headerLen = mode == KLineMode.Kwp2000 ? 4 : 3;
        byte[] frame = new byte[headerLen + payload.Length + 1];

        frame[0] = (byte)(mode == KLineMode.Kwp2000 ? FormatBase : FormatBase | payload.Length);
        frame[1] = TargetEcu;
        frame[2] = SourceTester;
        if (mode == KLineMode.Kwp2000)
            frame[3] = (byte)payload.Length;

        payload.CopyTo(frame.AsSpan(headerLen));
        frame[^1] = KLineChecksum.Calculate(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter KLineFrameBuilderTests`
Expected: PASS (4 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Protocol/KLineMode.cs src/OpenEcu.Core/Protocol/KLineFrameBuilder.cs tests/OpenEcu.Core.Tests/Protocol/KLineFrameBuilderTests.cs
git commit -m "feat: K-line request frame builder (ISO9141 + KWP2000)"
```

---

### Task 5: KLineFrameParser

**Files:**
- Create: `src/OpenEcu.Core/Protocol/KLineFrameParser.cs`
- Test: `tests/OpenEcu.Core.Tests/Protocol/KLineFrameParserTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Protocol/KLineFrameParserTests.cs`:
```csharp
using FluentAssertions;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class KLineFrameParserTests
{
    [Fact]
    public void Parses_payload_from_valid_iso_frame()
    {
        // header(3) + payload(0x41,0x42) + checksum
        byte[] frame = { 0x82, 0xF5, 0xD5, 0x41, 0x42, Sum(0x82, 0xF5, 0xD5, 0x41, 0x42) };
        bool ok = KLineFrameParser.TryParse(frame, KLineMode.Iso9141, out var payload);
        ok.Should().BeTrue();
        payload.ToArray().Should().Equal(0x41, 0x42);
    }

    [Fact]
    public void Parses_payload_from_valid_kwp_frame()
    {
        byte[] frame = { 0x80, 0xF5, 0xD5, 0x02, 0x41, 0x42, Sum(0x80, 0xF5, 0xD5, 0x02, 0x41, 0x42) };
        bool ok = KLineFrameParser.TryParse(frame, KLineMode.Kwp2000, out var payload);
        ok.Should().BeTrue();
        payload.ToArray().Should().Equal(0x41, 0x42);
    }

    [Fact]
    public void Rejects_frame_with_bad_checksum()
    {
        byte[] frame = { 0x82, 0xF5, 0xD5, 0x41, 0x42, 0x00 };
        bool ok = KLineFrameParser.TryParse(frame, KLineMode.Iso9141, out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Rejects_frame_too_short_to_contain_header_and_checksum()
    {
        byte[] frame = { 0x82, 0xF5 };
        bool ok = KLineFrameParser.TryParse(frame, KLineMode.Iso9141, out _);
        ok.Should().BeFalse();
    }

    private static byte Sum(params int[] bytes)
    {
        int s = 0;
        foreach (int b in bytes) s += b;
        return (byte)s;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter KLineFrameParserTests`
Expected: FAIL — `KLineFrameParser` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/OpenEcu.Core/Protocol/KLineFrameParser.cs`:
```csharp
namespace OpenEcu.Core.Protocol;

/// <summary>Validates and extracts the payload from an ECU→tester response frame.</summary>
public static class KLineFrameParser
{
    public static bool TryParse(ReadOnlySpan<byte> frame, KLineMode mode, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        int headerLen = mode == KLineMode.Kwp2000 ? 4 : 3;
        int minLen = headerLen + 1; // header + at least the checksum byte
        if (frame.Length < minLen)
            return false;

        byte expected = KLineChecksum.Calculate(frame[..^1]);
        if (frame[^1] != expected)
            return false;

        payload = frame[headerLen..^1];
        return true;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter KLineFrameParserTests`
Expected: PASS (4 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Protocol/KLineFrameParser.cs tests/OpenEcu.Core.Tests/Protocol/KLineFrameParserTests.cs
git commit -m "feat: K-line response frame parser with checksum validation"
```

---

### Task 6: IEcuTransport + SimulatedTransport

**Files:**
- Create: `src/OpenEcu.Core/Transport/IEcuTransport.cs`
- Create: `src/OpenEcu.Core/Transport/SimulatedTransport.cs`
- Test: `tests/OpenEcu.Core.Tests/Transport/SimulatedTransportTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Transport/SimulatedTransportTests.cs`:
```csharp
using FluentAssertions;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Transport;

public class SimulatedTransportTests
{
    [Fact]
    public async Task Open_then_close_toggles_IsOpen()
    {
        var t = new SimulatedTransport();
        t.IsOpen.Should().BeFalse();
        await t.OpenAsync();
        t.IsOpen.Should().BeTrue();
        await t.CloseAsync();
        t.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Write_records_bytes_for_inspection()
    {
        var t = new SimulatedTransport();
        await t.OpenAsync();
        await t.WriteAsync(new byte[] { 0x01, 0x02 });
        t.Written.Should().Equal(0x01, 0x02);
    }

    [Fact]
    public async Task Read_drains_scripted_response_bytes()
    {
        var t = new SimulatedTransport();
        t.EnqueueResponse(new byte[] { 0xAA, 0xBB, 0xCC });
        await t.OpenAsync();

        var buffer = new byte[2];
        int n1 = await t.ReadAsync(buffer);
        n1.Should().Be(2);
        buffer.Should().Equal(0xAA, 0xBB);

        int n2 = await t.ReadAsync(buffer);
        n2.Should().Be(1);
        buffer[0].Should().Be(0xCC);
    }

    [Fact]
    public async Task Write_before_open_throws()
    {
        var t = new SimulatedTransport();
        var act = async () => await t.WriteAsync(new byte[] { 0x01 });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SimulatedTransportTests`
Expected: FAIL — `IEcuTransport` / `SimulatedTransport` do not exist.

- [ ] **Step 3: Write the interface**

Create `src/OpenEcu.Core/Transport/IEcuTransport.cs`:
```csharp
namespace OpenEcu.Core.Transport;

/// <summary>
/// Raw byte-stream link to an ECU adapter (FTDI cable, serial/SPP, Bluetooth, ...).
/// Tier-1 of the two-tier transport/adapter model.
/// </summary>
public interface IEcuTransport : IAsyncDisposable
{
    bool IsOpen { get; }
    Task OpenAsync(CancellationToken ct = default);
    Task CloseAsync();
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>Reads up to buffer.Length bytes; returns the count actually read.</summary>
    Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the simulated implementation**

Create `src/OpenEcu.Core/Transport/SimulatedTransport.cs`:
```csharp
using System.Collections.Generic;

namespace OpenEcu.Core.Transport;

/// <summary>In-memory transport for unit tests: records writes, replays scripted reads.</summary>
public sealed class SimulatedTransport : IEcuTransport
{
    private readonly List<byte> _written = new();
    private readonly Queue<byte> _responses = new();

    public bool IsOpen { get; private set; }

    /// <summary>Bytes written by the code under test, in order.</summary>
    public IReadOnlyList<byte> Written => _written;

    /// <summary>Queue bytes that subsequent ReadAsync calls will return.</summary>
    public void EnqueueResponse(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            _responses.Enqueue(b);
    }

    public Task OpenAsync(CancellationToken ct = default)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        IsOpen = false;
        return Task.CompletedTask;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        EnsureOpen();
        _written.AddRange(data.ToArray());
        return Task.CompletedTask;
    }

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        EnsureOpen();
        int i = 0;
        while (i < buffer.Length && _responses.Count > 0)
            buffer.Span[i++] = _responses.Dequeue();
        return Task.FromResult(i);
    }

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
            throw new InvalidOperationException("Transport is not open.");
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter SimulatedTransportTests`
Expected: PASS (4 passed).

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Transport/ tests/OpenEcu.Core.Tests/Transport/SimulatedTransportTests.cs
git commit -m "feat: IEcuTransport abstraction + in-memory SimulatedTransport"
```

---

### Task 7: End-to-end round-trip test (build → transport → parse)

Proves the codec and transport compose correctly: build a request, write it, have the simulator return a valid response frame, read and parse it back.

**Files:**
- Test: `tests/OpenEcu.Core.Tests/Protocol/KLineRoundTripTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Protocol/KLineRoundTripTests.cs`:
```csharp
using FluentAssertions;
using OpenEcu.Core.Protocol;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class KLineRoundTripTests
{
    [Fact]
    public async Task Request_is_written_and_scripted_response_parses_back()
    {
        var transport = new SimulatedTransport();
        await transport.OpenAsync();

        // Build and send a KWP request with payload 0x81.
        byte[] request = KLineFrameBuilder.BuildRequest(new byte[] { 0x81 }, KLineMode.Kwp2000);
        await transport.WriteAsync(request);
        transport.Written.Should().Equal(0x80, 0xD5, 0xF5, 0x01, 0x81, 0xCC);

        // Script an ECU response carrying payload 0xC1, 0xEA, 0x8F (a plausible positive reply).
        byte[] response = BuildResponse(new byte[] { 0xC1, 0xEA, 0x8F });
        transport.EnqueueResponse(response);

        // Read the whole response back and parse it.
        var buffer = new byte[response.Length];
        int n = await transport.ReadAsync(buffer);
        n.Should().Be(response.Length);

        // NOTE: ReadOnlySpan<byte> cannot be an `out` parameter inside an async method
        // (C# ref-struct restriction), so do the parse in a synchronous helper.
        var (parsed, payloadBytes) = ParseFrame(buffer, n, KLineMode.Kwp2000);
        parsed.Should().BeTrue();
        payloadBytes.Should().Equal(0xC1, 0xEA, 0x8F);
    }

    // Sync helper to keep the ReadOnlySpan<byte> out param out of the async method.
    private static (bool ok, byte[] payload) ParseFrame(byte[] buffer, int length, KLineMode mode)
    {
        bool ok = KLineFrameParser.TryParse(buffer.AsSpan(0, length), mode, out var payload);
        return (ok, payload.ToArray());
    }

    // Helper builds an ECU→tester KWP response frame (target/source swapped vs request).
    private static byte[] BuildResponse(byte[] payload)
    {
        byte[] frame = new byte[4 + payload.Length + 1];
        frame[0] = 0x80;
        frame[1] = 0xF5; // target = tester
        frame[2] = 0xD5; // source = ECU
        frame[3] = (byte)payload.Length;
        payload.CopyTo(frame, 4);
        frame[^1] = KLineChecksum.Calculate(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }
}
```

- [ ] **Step 2: Run test to verify it fails, then passes**

Run: `dotnet test --filter KLineRoundTripTests`
Expected: PASS immediately (all referenced types already exist from Tasks 3–6). If it fails, the failure points to a real composition bug — fix before continuing.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test`
Expected: PASS — all tests from Tasks 3–7 green (15 passed).

- [ ] **Step 4: Commit**

```bash
git add tests/OpenEcu.Core.Tests/Protocol/KLineRoundTripTests.cs
git commit -m "test: end-to-end K-line build/transport/parse round-trip"
```

---

## Self-Review

**Spec coverage (this plan's slice of §5–§7):**
- Solution/layout scaffold → Task 1 ✅
- Clean-room protocol documentation (Phase 0 deliverable) → Task 2 ✅
- K-line framing + checksum (`Protocol/`) → Tasks 3–5 ✅
- `IEcuTransport` abstraction + test double → Task 6 ✅
- Composability proof → Task 7 ✅
- **Deliberately deferred to later plans:** ISO/KWP init handshake, `IEcuAdapter`/`KLineProtocol`/`Elm327Adapter`, `EcuDefinition`/`SagemMc1000Definition`, real transports (FTDI/serial/Bluetooth), diagnostics, maps, Avalonia UI. These are not gaps — they depend on this foundation and are out of this plan's scope.

**Placeholder scan:** No TBD/TODO; every code and test step is complete with exact bytes and commands.

**Type consistency:** `KLineMode`, `KLineChecksum.Calculate`, `KLineFrameBuilder.BuildRequest`, `KLineFrameParser.TryParse`, and `IEcuTransport` members (`IsOpen`, `OpenAsync`, `CloseAsync`, `WriteAsync`, `ReadAsync`) / `SimulatedTransport` extras (`Written`, `EnqueueResponse`) are referenced consistently across all tasks.
