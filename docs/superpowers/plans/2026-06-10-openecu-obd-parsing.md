# OpenECU OBD-II Framing & Decoding — Implementation Plan (Plan 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the clean, fully-tested OBD-II layer in `OpenEcu.Core`: request/response framing for the `68 6A F1` / `48 6B D1` format, supported-PID bitmask parsing, Mode 01 PID decoding into physical values, and Mode 03 DTC decoding — all verified against the **real bytes captured from the bike** on 2026-06-10.

**Architecture:** A new dependency-free `OpenEcu.Core.Obd` namespace with small, focused, pure units: `ObdMessage` (build/parse frames), `SupportedPids` (bitmask → PID list), `PidDecoder` (PID + data → reading), `DtcDecoder` (Mode 03 data → DTC strings). These are the reusable heart of the read stack and are trivially unit-testable. The live wiring that drives them over the cable (5-baud init + keyword handshake + echo-locked transmit) is **plan 7**.

**Tech Stack:** .NET 8 (C# 12), xUnit, **AwesomeAssertions** (MIT — `using AwesomeAssertions;`, NOT FluentAssertions). Builds on plans 1–5.

**Golden data (captured live, key-on/engine-off, 2026-06-10):**
- Request `68 6A F1 01 0C D0` → response `48 6B D1 41 0C 00 00 D1` (RPM = 0).
- Supported PIDs 01–20 bitmask = `BE 1E 90 11`; 21–40 = `00 00 00 01`; 41–60 = `00 00 00 00`.
- PID 05 data `44` → 28 °C; PID 0F `49` → 33 °C; PID 11 `1C` → 10.98 %; PID 0E `44` → −30.0°; PID 14 `5D 80` → 0.465 V.
- Mode 03 response `48 6B D1 43 15 02 00 00 00 00 DE` → one DTC `P1502`.
- Response header is `48 6B D1` (ECU source `D1`); checksum = sum of all preceding bytes mod 256.

**Scope note:** Plan 6 of several. Pure parsing/decoding only. It does NOT do the live session (init/handshake/transmit) — that's plan 7, which consumes these units. No hardware needed to implement or test this plan.

**Prerequisite:** Plans 1–5 on `main`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.Core/Obd/ObdResponse.cs` | Parsed response (service id + payload) |
| `src/OpenEcu.Core/Obd/ObdMessage.cs` | Build requests / parse responses (`68 6A F1` framing) |
| `src/OpenEcu.Core/Obd/SupportedPids.cs` | Supported-PID bitmask → PID numbers |
| `src/OpenEcu.Core/Obd/PidReading.cs` | One decoded PID value |
| `src/OpenEcu.Core/Obd/PidDecoder.cs` | Mode 01 PID + data → `PidReading` |
| `src/OpenEcu.Core/Obd/DtcDecoder.cs` | Mode 03 data → DTC strings |
| `tests/OpenEcu.Core.Tests/Obd/ObdMessageTests.cs` | Framing tests |
| `tests/OpenEcu.Core.Tests/Obd/SupportedPidsTests.cs` | Bitmask tests |
| `tests/OpenEcu.Core.Tests/Obd/PidDecoderTests.cs` | Decoding tests (real values) |
| `tests/OpenEcu.Core.Tests/Obd/DtcDecoderTests.cs` | DTC tests |

---

### Task 1: ObdResponse + ObdMessage (framing)

**Files:**
- Create: `src/OpenEcu.Core/Obd/ObdResponse.cs`
- Create: `src/OpenEcu.Core/Obd/ObdMessage.cs`
- Test: `tests/OpenEcu.Core.Tests/Obd/ObdMessageTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Obd/ObdMessageTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class ObdMessageTests
{
    [Fact]
    public void BuildRequest_prepends_68_6A_F1_and_appends_checksum()
    {
        // Real request captured from the bike for Mode 01 PID 0C.
        byte[] frame = ObdMessage.BuildRequest(new byte[] { 0x01, 0x0C });
        frame.Should().Equal(0x68, 0x6A, 0xF1, 0x01, 0x0C, 0xD0);
    }

    [Fact]
    public void TryParseResponse_extracts_service_and_payload()
    {
        // Real response: 48 6B D1 | 41 | 0C 00 00 | D1
        byte[] frame = { 0x48, 0x6B, 0xD1, 0x41, 0x0C, 0x00, 0x00, 0xD1 };
        ObdMessage.TryParseResponse(frame, out ObdResponse resp).Should().BeTrue();
        resp.ServiceId.Should().Be(0x41);
        resp.Payload.Should().Equal(0x0C, 0x00, 0x00);
    }

    [Fact]
    public void TryParseResponse_rejects_bad_checksum()
    {
        byte[] frame = { 0x48, 0x6B, 0xD1, 0x41, 0x0C, 0x00, 0x00, 0x00 };
        ObdMessage.TryParseResponse(frame, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseResponse_rejects_too_short()
    {
        byte[] frame = { 0x48, 0x6B, 0xD1 };
        ObdMessage.TryParseResponse(frame, out _).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ObdMessageTests`
Expected: FAIL — `ObdMessage` / `ObdResponse` do not exist.

- [ ] **Step 3: Write the response type**

Create `src/OpenEcu.Core/Obd/ObdResponse.cs`:
```csharp
namespace OpenEcu.Core.Obd;

/// <summary>A parsed OBD-II response: the response service id and the bytes after it.</summary>
public sealed record ObdResponse(byte ServiceId, byte[] Payload);
```

- [ ] **Step 4: Write the framing**

Create `src/OpenEcu.Core/Obd/ObdMessage.cs`:
```csharp
namespace OpenEcu.Core.Obd;

/// <summary>
/// Builds and parses ISO9141-2 OBD-II K-line messages for an ECU in generic OBD mode.
/// Request header is 68 6A F1; response header is 48 6B &lt;ecu&gt;. The trailing byte of
/// every frame is the sum of all preceding bytes, mod 256.
/// </summary>
public static class ObdMessage
{
    private const byte ReqFormat = 0x68;  // functional OBD request
    private const byte ReqTarget = 0x6A;  // ECU
    private const byte ReqSource = 0xF1;  // tester
    private const int ResponseHeaderLength = 3; // fmt, target, source

    public static byte[] BuildRequest(ReadOnlySpan<byte> payload)
    {
        byte[] frame = new byte[3 + payload.Length + 1];
        frame[0] = ReqFormat;
        frame[1] = ReqTarget;
        frame[2] = ReqSource;
        payload.CopyTo(frame.AsSpan(3));
        frame[^1] = Checksum(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }

    public static bool TryParseResponse(ReadOnlySpan<byte> frame, out ObdResponse response)
    {
        response = null!;
        // header(3) + service id(1) + checksum(1) minimum
        if (frame.Length < ResponseHeaderLength + 2)
            return false;
        if (frame[^1] != Checksum(frame[..^1]))
            return false;

        byte serviceId = frame[ResponseHeaderLength];
        byte[] payload = frame[(ResponseHeaderLength + 1)..^1].ToArray();
        response = new ObdResponse(serviceId, payload);
        return true;
    }

    private static byte Checksum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        foreach (byte b in data) sum += b;
        return (byte)sum;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter ObdMessageTests`
Expected: PASS (4 passed).

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Obd/ObdResponse.cs src/OpenEcu.Core/Obd/ObdMessage.cs tests/OpenEcu.Core.Tests/Obd/ObdMessageTests.cs
git commit -m "feat: OBD-II message framing (68 6A F1 / 48 6B xx)"
```

---

### Task 2: SupportedPids

**Files:**
- Create: `src/OpenEcu.Core/Obd/SupportedPids.cs`
- Test: `tests/OpenEcu.Core.Tests/Obd/SupportedPidsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Obd/SupportedPidsTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class SupportedPidsTests
{
    [Fact]
    public void Parses_real_bitmask_from_the_bike()
    {
        // PID 00 response data BE 1E 90 11 advertises PIDs 01-20.
        var pids = SupportedPids.Parse(0x00, new byte[] { 0xBE, 0x1E, 0x90, 0x11 });
        pids.Should().Equal(0x01, 0x03, 0x04, 0x05, 0x06, 0x07, 0x0C, 0x0D, 0x0E, 0x0F, 0x11, 0x14, 0x1C, 0x20);
    }

    [Fact]
    public void Applies_base_offset_for_the_21_40_range()
    {
        // 21-40 bitmask 00 00 00 01 advertises only PID 40 (the next-range chain bit).
        var pids = SupportedPids.Parse(0x20, new byte[] { 0x00, 0x00, 0x00, 0x01 });
        pids.Should().Equal(0x40);
    }

    [Fact]
    public void Empty_range_yields_nothing()
    {
        var pids = SupportedPids.Parse(0x40, new byte[] { 0x00, 0x00, 0x00, 0x00 });
        pids.Should().BeEmpty();
    }

    [Fact]
    public void Throws_when_bitmask_is_not_four_bytes()
    {
        var act = () => SupportedPids.Parse(0x00, new byte[] { 0x00 });
        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SupportedPidsTests`
Expected: FAIL — `SupportedPids` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenEcu.Core/Obd/SupportedPids.cs`:
```csharp
namespace OpenEcu.Core.Obd;

/// <summary>Decodes a "PIDs supported" bitmask (Mode 01 PID 00/20/40 data) into PID numbers.</summary>
public static class SupportedPids
{
    /// <param name="basePid">The query PID (0x00, 0x20, 0x40). Results are offset by it.</param>
    /// <param name="bitmask">The 4 data bytes; MSB of byte 0 is basePid+1.</param>
    public static IReadOnlyList<byte> Parse(byte basePid, ReadOnlySpan<byte> bitmask)
    {
        if (bitmask.Length != 4)
            throw new ArgumentException("Supported-PID bitmask must be exactly 4 bytes.", nameof(bitmask));

        var pids = new List<byte>();
        for (int i = 0; i < 32; i++)
        {
            bool supported = (bitmask[i / 8] & (0x80 >> (i % 8))) != 0;
            if (supported)
                pids.Add((byte)(basePid + i + 1));
        }
        return pids;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter SupportedPidsTests`
Expected: PASS (4 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Obd/SupportedPids.cs tests/OpenEcu.Core.Tests/Obd/SupportedPidsTests.cs
git commit -m "feat: supported-PID bitmask decoding"
```

---

### Task 3: PidReading + PidDecoder

**Files:**
- Create: `src/OpenEcu.Core/Obd/PidReading.cs`
- Create: `src/OpenEcu.Core/Obd/PidDecoder.cs`
- Test: `tests/OpenEcu.Core.Tests/Obd/PidDecoderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Obd/PidDecoderTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class PidDecoderTests
{
    [Fact]
    public void Decodes_coolant_temp_from_real_bytes()
    {
        var r = PidDecoder.Decode(0x05, new byte[] { 0x44 }); // 0x44 = 68 -> 28 C
        r.Name.Should().Be("Coolant temperature");
        r.Value.Should().Be(28);
        r.Unit.Should().Be("C");
    }

    [Fact]
    public void Decodes_rpm()
    {
        PidDecoder.Decode(0x0C, new byte[] { 0x00, 0x00 }).Value.Should().Be(0);
        PidDecoder.Decode(0x0C, new byte[] { 0x0B, 0xB8 }).Value.Should().Be(750); // (0x0BB8)/4
    }

    [Fact]
    public void Decodes_throttle_percent()
    {
        var r = PidDecoder.Decode(0x11, new byte[] { 0x1C }); // 28*100/255
        r.Value.Should().BeApproximately(10.98, 0.01);
        r.Unit.Should().Be("%");
    }

    [Fact]
    public void Decodes_intake_air_temp()
    {
        PidDecoder.Decode(0x0F, new byte[] { 0x49 }).Value.Should().Be(33); // 73-40
    }

    [Fact]
    public void Decodes_timing_advance()
    {
        PidDecoder.Decode(0x0E, new byte[] { 0x44 }).Value.Should().Be(-30); // 68/2-64
    }

    [Fact]
    public void Decodes_o2_sensor_voltage()
    {
        var r = PidDecoder.Decode(0x14, new byte[] { 0x5D, 0x80 }); // 0x5D=93 -> 0.465 V
        r.Value.Should().BeApproximately(0.465, 0.0001);
        r.Unit.Should().Be("V");
    }

    [Fact]
    public void Unknown_pid_returns_raw_with_null_value()
    {
        var r = PidDecoder.Decode(0x1C, new byte[] { 0x05 });
        r.Value.Should().BeNull();
        r.Raw.Should().Equal(0x05);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter PidDecoderTests`
Expected: FAIL — `PidDecoder` / `PidReading` do not exist.

- [ ] **Step 3: Write the reading type**

Create `src/OpenEcu.Core/Obd/PidReading.cs`:
```csharp
namespace OpenEcu.Core.Obd;

/// <summary>One decoded Mode 01 PID value. Value is null for PIDs we don't decode.</summary>
public sealed record PidReading(byte Pid, string Name, double? Value, string Unit, byte[] Raw);
```

- [ ] **Step 4: Write the decoder**

Create `src/OpenEcu.Core/Obd/PidDecoder.cs`:
```csharp
namespace OpenEcu.Core.Obd;

/// <summary>Decodes standard OBD-II Mode 01 PIDs into physical values.</summary>
public static class PidDecoder
{
    public static PidReading Decode(byte pid, ReadOnlySpan<byte> data)
    {
        byte[] raw = data.ToArray();
        int A = data.Length > 0 ? data[0] : 0;
        int B = data.Length > 1 ? data[1] : 0;

        return pid switch
        {
            0x04 => new PidReading(pid, "Calculated engine load", A * 100.0 / 255, "%", raw),
            0x05 => new PidReading(pid, "Coolant temperature", A - 40, "C", raw),
            0x06 => new PidReading(pid, "Short-term fuel trim", (A - 128) * 100.0 / 128, "%", raw),
            0x07 => new PidReading(pid, "Long-term fuel trim", (A - 128) * 100.0 / 128, "%", raw),
            0x0B => new PidReading(pid, "Intake manifold pressure", A, "kPa", raw),
            0x0C => new PidReading(pid, "Engine RPM", (A * 256 + B) / 4.0, "rpm", raw),
            0x0D => new PidReading(pid, "Vehicle speed", A, "km/h", raw),
            0x0E => new PidReading(pid, "Timing advance", A / 2.0 - 64, "deg", raw),
            0x0F => new PidReading(pid, "Intake air temperature", A - 40, "C", raw),
            0x11 => new PidReading(pid, "Throttle position", A * 100.0 / 255, "%", raw),
            0x14 => new PidReading(pid, "O2 sensor voltage", A * 0.005, "V", raw),
            _ => new PidReading(pid, $"PID {pid:X2}", null, "", raw),
        };
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter PidDecoderTests`
Expected: PASS (7 passed).

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Obd/PidReading.cs src/OpenEcu.Core/Obd/PidDecoder.cs tests/OpenEcu.Core.Tests/Obd/PidDecoderTests.cs
git commit -m "feat: Mode 01 PID decoding to physical values"
```

---

### Task 4: DtcDecoder

**Files:**
- Create: `src/OpenEcu.Core/Obd/DtcDecoder.cs`
- Test: `tests/OpenEcu.Core.Tests/Obd/DtcDecoderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Obd/DtcDecoderTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class DtcDecoderTests
{
    [Fact]
    public void Decodes_the_real_stored_dtc()
    {
        // Mode 03 payload (after service id 0x43) captured from the bike.
        var codes = DtcDecoder.Decode(new byte[] { 0x15, 0x02, 0x00, 0x00, 0x00, 0x00 });
        codes.Should().Equal("P1502");
    }

    [Fact]
    public void Decodes_each_prefix()
    {
        DtcDecoder.Decode(new byte[] { 0x01, 0x33 }).Should().Equal("P0133");
        DtcDecoder.Decode(new byte[] { 0x41, 0x23 }).Should().Equal("C0123");
        DtcDecoder.Decode(new byte[] { 0x81, 0x45 }).Should().Equal("B0145");
        DtcDecoder.Decode(new byte[] { 0xC1, 0x67 }).Should().Equal("U0167");
    }

    [Fact]
    public void Skips_empty_pairs_and_handles_no_codes()
    {
        DtcDecoder.Decode(new byte[] { 0x00, 0x00, 0x00, 0x00 }).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DtcDecoderTests`
Expected: FAIL — `DtcDecoder` does not exist.

- [ ] **Step 3: Write the decoder**

Create `src/OpenEcu.Core/Obd/DtcDecoder.cs`:
```csharp
namespace OpenEcu.Core.Obd;

/// <summary>Decodes Mode 03/07 trouble-code byte pairs into DTC strings (e.g. "P1502").</summary>
public static class DtcDecoder
{
    private const string SystemLetters = "PCBU";

    /// <param name="payload">DTC byte pairs (the Mode 03 data after service id 0x43).</param>
    public static IReadOnlyList<string> Decode(ReadOnlySpan<byte> payload)
    {
        var codes = new List<string>();
        for (int i = 0; i + 1 < payload.Length; i += 2)
        {
            int a = payload[i];
            int b = payload[i + 1];
            if (a == 0 && b == 0)
                continue; // empty slot

            char system = SystemLetters[(a >> 6) & 0x3];
            codes.Add($"{system}{(a >> 4) & 0x3}{a & 0xF:X}{b >> 4:X}{b & 0xF:X}");
        }
        return codes;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter DtcDecoderTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS — plans 1–5 (46) + Task 1 (4) + Task 2 (4) + Task 3 (7) + Task 4 (3) = 64 passed, 1 skipped.

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Obd/DtcDecoder.cs tests/OpenEcu.Core.Tests/Obd/DtcDecoderTests.cs
git commit -m "feat: Mode 03 DTC decoding"
```

---

## Self-Review

**Spec coverage:**
- OBD-II request/response framing (`68 6A F1` / `48 6B xx`, checksum) → Task 1 ✅
- Supported-PID bitmask decoding → Task 2 ✅
- Mode 01 PID → physical value decoding (validated against captured bytes) → Task 3 ✅
- Mode 03 DTC decoding (validated against the real `P1502`) → Task 4 ✅
- **Deliberately deferred to plan 7:** the live `KLineObdSession` (5-baud init + keyword handshake + echo-locked transmit + read-until-idle) that drives these decoders over the cable; rewiring `OpenEcu.Probe` onto the Core API; the Avalonia UI.

**Placeholder scan:** No TBD/TODO. Every step has complete code and real expected values.

**Type consistency:** `ObdMessage.BuildRequest`/`TryParseResponse`, `ObdResponse(ServiceId, Payload)`, `SupportedPids.Parse(byte, ReadOnlySpan<byte>)`, `PidDecoder.Decode(byte, ReadOnlySpan<byte>)`, `PidReading(Pid, Name, Value, Unit, Raw)`, and `DtcDecoder.Decode(ReadOnlySpan<byte>)` are referenced consistently across tasks. All tests use `using AwesomeAssertions;`.
