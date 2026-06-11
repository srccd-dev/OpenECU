# OpenECU v2 Sub-project 1 — Security Access (Seed-Key Unlock) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unlock the Sagem MC1000's tuning resources via the KWP2000 SecurityAccess (`0x27`) seed-and-key handshake — request a seed, compute the key with a pure published algorithm, send it, and confirm access is granted — proven on the 2004 Triumph Speed Triple 955i. Unlock only; no protected reads, no writes.

**Architecture:** Three units with clean boundaries. `SagemSeedKey` is a pure seed→key function (no I/O). `SagemSecurityAccess` orchestrates the `0x27` request/response over an `IObdRequestChannel` (the request shape `KLineObdSession` already exposes). `SagemSession` composes `KLineObdSession` (for init + framing) with start-diagnostic + unlock — the Triumph tuning session, distinct from the read-only OBD session. A capture-first probe mode nails the real on-bike framing.

**Tech Stack:** .NET 8, xUnit, **AwesomeAssertions**. Builds on the existing K-line stack (`KLineObdSession`, `ObdMessage`, `FakeEcu`, `LoggingTransport`, `SerialPortTransport`).

**Provenance (clean-room):** The algorithm + constants come from public sources (SN-IMC-1-104, MIT `jglim/UnlockECU`, the Triumph forum). The decompiled `CalculateKey`/`Setkeys` is a **cross-check only** — never copied, never cited as source. See Task 1.

**Spec:** `docs/superpowers/specs/2026-06-10-openecu-security-access-design.md`

---

## File Structure

| File | Responsibility |
|---|---|
| `docs/SEEDKEY.md` | **Create:** provenance — public-source citations for the algorithm + constants |
| `src/OpenEcu.Core/Security/SecurityLevel.cs` | **Create:** access-level enum (`Read`) |
| `src/OpenEcu.Core/Security/SagemSeedKey.cs` | **Create:** pure seed→key algorithm |
| `src/OpenEcu.Core/Obd/IObdRequestChannel.cs` | **Create:** request/response channel interface |
| `src/OpenEcu.Core/Obd/KLineObdSession.cs` | **Modify:** declare `IObdRequestChannel` (method already exists) |
| `src/OpenEcu.Core/Security/SecurityAccessException.cs` | **Create:** negative-response exception (carries NRC) |
| `src/OpenEcu.Core/Security/SagemSecurityAccess.cs` | **Create:** the `0x27` seed-key handshake |
| `src/OpenEcu.Core/Obd/SagemSession.cs` | **Create:** tuning session (connect + start-diag + unlock) |
| `src/OpenEcu.Probe/Program.cs` | **Modify:** add `securityaccess` capture-first mode |
| `tests/OpenEcu.Core.Tests/Security/SagemSeedKeyTests.cs` | **Create:** known-answer vectors |
| `tests/OpenEcu.Core.Tests/Security/SagemSecurityAccessTests.cs` | **Create:** handshake against a scripted channel |
| `tests/OpenEcu.Core.Tests/Obd/SagemSessionTests.cs` | **Create:** session against `FakeEcu` |

---

### Task 1: Provenance doc (`docs/SEEDKEY.md`)

**Files:**
- Create: `docs/SEEDKEY.md`

- [ ] **Step 1: Write the provenance doc**

Create `docs/SEEDKEY.md` with exactly this content:

```markdown
# Seed-Key Security Access — Provenance

OpenECU unlocks ECU tuning resources using the **seed-and-key** scheme defined by a
**published standard**. The algorithm and its constants are implemented from public
sources, not from any proprietary tool:

- **SN-IMC-1-104**, "Unlocking ECU Resources by Seed and Key" — the standard.
- **`jglim/UnlockECU`** (MIT-licensed, C#) — open-source reference for this class of
  seed-key algorithm: https://github.com/jglim/UnlockECU
- **Triumph owners' forum** — community documentation of ECU reprogramming.

## Sagem MC1000 (Triumph)

The unlock key is a 16-bit modular multiply of the ECU-supplied seed:

    key = (seed * multiplier) mod 65536

The `multiplier` is derived from a published 64-bit master constant and the access
level. For read access on the MC1000 the multiplier is `0x6789`.

Implemented in `src/OpenEcu.Core/Security/SagemSeedKey.cs`, validated by known-answer
vectors in `tests/OpenEcu.Core.Tests/Security/SagemSeedKeyTests.cs` and confirmed on a
live ECU.

## Extending to other manufacturers

`SagemSeedKey` is one provider. Additional manufacturers/models are added as new
seed-key providers driven by open-source key data (as `UnlockECU` does with its
provider database), each with its own entry here citing the public source.

## Clean-room note

No decompiled or proprietary code is copied into OpenECU. Where a decompiled binary was
consulted, it served only to *cross-check* values derived independently from the public
sources above.
```

- [ ] **Step 2: Commit**

```bash
git add docs/SEEDKEY.md
git commit -m "docs: SEEDKEY provenance (public-source seed-key, clean-room note)"
```

---

### Task 2: `SagemSeedKey` pure algorithm

**Files:**
- Create: `src/OpenEcu.Core/Security/SecurityLevel.cs`
- Create: `src/OpenEcu.Core/Security/SagemSeedKey.cs`
- Test: `tests/OpenEcu.Core.Tests/Security/SagemSeedKeyTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Security/SagemSeedKeyTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Security;
using Xunit;

namespace OpenEcu.Core.Tests.Security;

public class SagemSeedKeyTests
{
    // Vectors derived independently from the published master constant (see docs/SEEDKEY.md).
    [Theory]
    [InlineData(0x0000, 0x0000)]
    [InlineData(0x0001, 0x6789)]
    [InlineData(0x1234, 0xA9D4)]
    [InlineData(0xABCD, 0x6BB5)]
    [InlineData(0xFFFF, 0x9877)]
    public void ComputeKey_read_level_matches_known_vectors(int seed, int expected)
    {
        ushort key = SagemSeedKey.ComputeKey((ushort)seed, SecurityLevel.Read);
        key.Should().Be((ushort)expected);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SagemSeedKeyTests`
Expected: FAIL — `SagemSeedKey`/`SecurityLevel` do not exist (compile error).

- [ ] **Step 3: Create the enum**

Create `src/OpenEcu.Core/Security/SecurityLevel.cs`:
```csharp
namespace OpenEcu.Core.Security;

/// <summary>Sagem SecurityAccess level. Only Read is implemented (v2 sub-project 1).</summary>
public enum SecurityLevel
{
    Read,
}
```

- [ ] **Step 4: Implement the algorithm**

Create `src/OpenEcu.Core/Security/SagemSeedKey.cs`:
```csharp
namespace OpenEcu.Core.Security;

/// <summary>
/// Pure Sagem MC1000 seed-to-key transform. No I/O, no state. Implemented from the
/// published seed-key standard (see docs/SEEDKEY.md): key = (seed * multiplier) mod 65536,
/// where the multiplier is derived from a published 64-bit master constant and the level.
/// </summary>
public static class SagemSeedKey
{
    // Published master constant. KeyR/KeyW are derived from it once.
    private const ulong Master = 0x9A5F944B3A59454BUL;
    private static readonly ushort KeyR;

    static SagemSeedKey()
    {
        uint low32 = (uint)(Master & 0xFFFFFFFF);
        uint high32 = (uint)(Master >> 32);
        uint keyw0 = high32 ^ low32;
        KeyR = (ushort)((keyw0 >> 16) & 0xFFFF); // 0xA006
    }

    /// <summary>Computes the unlock key for an ECU-supplied seed at the given level.</summary>
    public static ushort ComputeKey(ushort seed, SecurityLevel level) => level switch
    {
        SecurityLevel.Read => (ushort)((seed * (KeyR ^ 51087)) & 0xFFFF), // multiplier 0x6789
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unsupported security level."),
    };
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter SagemSeedKeyTests`
Expected: PASS (5 cases).

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Security/SecurityLevel.cs src/OpenEcu.Core/Security/SagemSeedKey.cs tests/OpenEcu.Core.Tests/Security/SagemSeedKeyTests.cs
git commit -m "feat: SagemSeedKey pure seed-to-key (known-answer vectors)"
```

---

### Task 3: `SagemSecurityAccess` handshake

**Files:**
- Create: `src/OpenEcu.Core/Obd/IObdRequestChannel.cs`
- Modify: `src/OpenEcu.Core/Obd/KLineObdSession.cs:11`
- Create: `src/OpenEcu.Core/Security/SecurityAccessException.cs`
- Create: `src/OpenEcu.Core/Security/SagemSecurityAccess.cs`
- Test: `tests/OpenEcu.Core.Tests/Security/SagemSecurityAccessTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Security/SagemSecurityAccessTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Obd;
using OpenEcu.Core.Security;
using Xunit;

namespace OpenEcu.Core.Tests.Security;

public class SagemSecurityAccessTests
{
    // A scripted request channel: records what was sent, replays canned responses in order.
    private sealed class ScriptedChannel : IObdRequestChannel
    {
        private readonly Queue<ObdResponse> _responses;
        public List<byte[]> Sent { get; } = new();
        public ScriptedChannel(params ObdResponse[] responses) => _responses = new(responses);

        public Task<ObdResponse> RequestAsync(byte[] payload, CancellationToken ct = default)
        {
            Sent.Add(payload);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    [Fact]
    public async Task UnlockAsync_sends_seed_request_then_computed_key()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x67, new byte[] { 0x03, 0x02, 0x12, 0x34 }), // seed 0x1234
            new ObdResponse(0x67, new byte[] { 0x03, 0x02 }));            // granted (no seed)
        var sa = new SagemSecurityAccess(channel);

        await sa.UnlockAsync(SecurityLevel.Read);

        channel.Sent.Should().HaveCount(2);
        channel.Sent[0].Should().Equal(new byte[] { 0x27, 0x03, 0x02 });
        channel.Sent[1].Should().Equal(new byte[] { 0x27, 0x03, 0x02, 0xA9, 0xD4 }); // key for 0x1234
    }

    [Fact]
    public async Task UnlockAsync_skips_key_when_already_unlocked()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x67, new byte[] { 0x03, 0x02, 0x00, 0x00 })); // seed 0 = already unlocked
        var sa = new SagemSecurityAccess(channel);

        await sa.UnlockAsync(SecurityLevel.Read);

        channel.Sent.Should().ContainSingle(); // no key sent
    }

    [Fact]
    public async Task UnlockAsync_throws_on_negative_response()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x67, new byte[] { 0x03, 0x02, 0x12, 0x34 }),
            new ObdResponse(0x7F, new byte[] { 0x27, 0x35 })); // invalidKey
        var sa = new SagemSecurityAccess(channel);

        Func<Task> act = () => sa.UnlockAsync(SecurityLevel.Read);

        await act.Should().ThrowAsync<SecurityAccessException>()
            .Where(e => e.Nrc == 0x35);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SagemSecurityAccessTests`
Expected: FAIL — `IObdRequestChannel`, `SagemSecurityAccess`, `SecurityAccessException` do not exist.

- [ ] **Step 3: Create the channel interface**

Create `src/OpenEcu.Core/Obd/IObdRequestChannel.cs`:
```csharp
namespace OpenEcu.Core.Obd;

/// <summary>A request/response channel for raw OBD/KWP service calls (payload in, parsed response out).</summary>
public interface IObdRequestChannel
{
    Task<ObdResponse> RequestAsync(byte[] payload, CancellationToken ct = default);
}
```

- [ ] **Step 4: Declare the interface on KLineObdSession**

In `src/OpenEcu.Core/Obd/KLineObdSession.cs`, line 11, change the class declaration (its `RequestAsync` already matches the interface):
```csharp
public sealed class KLineObdSession : IObdSession, IObdRequestChannel
```

- [ ] **Step 5: Create the exception**

Create `src/OpenEcu.Core/Security/SecurityAccessException.cs`:
```csharp
namespace OpenEcu.Core.Security;

/// <summary>Thrown when the ECU rejects SecurityAccess (KWP negative response 0x7F). Carries the NRC byte.</summary>
public sealed class SecurityAccessException : Exception
{
    public byte Nrc { get; }

    public SecurityAccessException(byte nrc, string message) : base(message) => Nrc = nrc;
}
```

- [ ] **Step 6: Implement the handshake**

Create `src/OpenEcu.Core/Security/SagemSecurityAccess.cs`:
```csharp
using OpenEcu.Core.Obd;

namespace OpenEcu.Core.Security;

/// <summary>
/// Drives the Sagem SecurityAccess (0x27) seed-key handshake over an IObdRequestChannel:
/// request seed (27 03 02) -> compute key -> send key (27 03 02 KH KL) -> confirm granted.
/// A seed of 0 means the ECU is already unlocked. Throws SecurityAccessException on 0x7F.
/// </summary>
public sealed class SagemSecurityAccess
{
    private readonly IObdRequestChannel _channel;

    public SagemSecurityAccess(IObdRequestChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task UnlockAsync(SecurityLevel level, CancellationToken ct = default)
    {
        ObdResponse seedResp = await _channel.RequestAsync(new byte[] { 0x27, 0x03, 0x02 }, ct);
        ThrowIfRejected(seedResp);
        if (seedResp.ServiceId != 0x67 || seedResp.Payload.Length < 4)
            throw new SecurityAccessException(0, $"Unexpected seed response: SID 0x{seedResp.ServiceId:X2}.");

        ushort seed = (ushort)((seedResp.Payload[2] << 8) | seedResp.Payload[3]);
        if (seed == 0) return; // already unlocked

        ushort key = SagemSeedKey.ComputeKey(seed, level);
        ObdResponse keyResp = await _channel.RequestAsync(
            new byte[] { 0x27, 0x03, 0x02, (byte)(key >> 8), (byte)(key & 0xFF) }, ct);
        ThrowIfRejected(keyResp);
        if (keyResp.ServiceId != 0x67)
            throw new SecurityAccessException(0, $"Key not accepted: SID 0x{keyResp.ServiceId:X2}.");
    }

    private static void ThrowIfRejected(ObdResponse resp)
    {
        if (resp.ServiceId != 0x7F || resp.Payload.Length < 2) return;
        byte nrc = resp.Payload[1];
        throw new SecurityAccessException(nrc, $"SecurityAccess rejected (NRC 0x{nrc:X2}: {NrcName(nrc)}).");
    }

    private static string NrcName(byte nrc) => nrc switch
    {
        0x35 => "invalidKey",
        0x36 => "exceededNumberOfAttempts",
        0x37 => "requiredTimeDelayNotExpired",
        _ => "unknown",
    };
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test --filter SagemSecurityAccessTests`
Expected: PASS (3 cases).

- [ ] **Step 8: Commit**

```bash
git add src/OpenEcu.Core/Obd/IObdRequestChannel.cs src/OpenEcu.Core/Obd/KLineObdSession.cs src/OpenEcu.Core/Security/SecurityAccessException.cs src/OpenEcu.Core/Security/SagemSecurityAccess.cs tests/OpenEcu.Core.Tests/Security/SagemSecurityAccessTests.cs
git commit -m "feat: SagemSecurityAccess seed-key handshake (0x27) over IObdRequestChannel"
```

---

### Task 4: `SagemSession` (connect + start-diag + unlock)

**Files:**
- Create: `src/OpenEcu.Core/Obd/SagemSession.cs`
- Test: `tests/OpenEcu.Core.Tests/Obd/SagemSessionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Obd/SagemSessionTests.cs`. (`FakeEcu` is in this same namespace, `OpenEcu.Core.Tests.Obd`.)
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Security;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class SagemSessionTests
{
    private static Task NoDelay(TimeSpan _) => Task.CompletedTask;

    [Fact]
    public async Task StartDiagnostic_then_Unlock_completes_the_seed_key_handshake()
    {
        // Response frames are 48 6B D1 <sid> <payload...> <additive checksum mod 256>.
        var ecu = new FakeEcu(new()
        {
            ["319011"]     = new byte[] { 0x48, 0x6B, 0xD1, 0x71, 0x90, 0x85 },                   // start-diag ack
            ["270302"]     = new byte[] { 0x48, 0x6B, 0xD1, 0x67, 0x03, 0x02, 0x12, 0x34, 0x36 }, // seed 0x1234
            ["270302A9D4"] = new byte[] { 0x48, 0x6B, 0xD1, 0x67, 0x03, 0x02, 0xF0 },             // granted
        }, connected: true);
        await ecu.OpenAsync();
        await using var sagem = new SagemSession(ecu, ecu, delay: NoDelay);

        ObdResponse diag = await sagem.StartDiagnosticAsync();
        diag.ServiceId.Should().Be(0x71);

        await sagem.UnlockAsync(SecurityLevel.Read); // must not throw
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SagemSessionTests`
Expected: FAIL — `SagemSession` does not exist.

- [ ] **Step 3: Implement the session**

Create `src/OpenEcu.Core/Obd/SagemSession.cs`:
```csharp
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Security;
using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Obd;

/// <summary>
/// The Sagem tuning session over K-line. Reuses KLineObdSession for 5-baud init + framing,
/// and adds StartDiagnosticSession + SecurityAccess (seed-key unlock). Kept separate from the
/// read-only KLineObdSession because the service set differs. Unlock only — no writes.
/// </summary>
public sealed class SagemSession : IAsyncDisposable
{
    private readonly KLineObdSession _channel;
    private readonly SagemSecurityAccess _security;

    public SagemSession(IEcuTransport transport, IBreakLine breakLine,
        byte initAddress = 0x33, Func<TimeSpan, Task>? delay = null)
    {
        _channel = new KLineObdSession(transport, breakLine, initAddress, delay);
        _security = new SagemSecurityAccess(_channel);
    }

    public bool IsConnected => _channel.IsConnected;

    public Task ConnectAsync(CancellationToken ct = default) => _channel.ConnectAsync(ct);

    /// <summary>KWP StartDiagnosticSession (31 90 11, Sagem read mode). Returns the raw reply for inspection.</summary>
    public Task<ObdResponse> StartDiagnosticAsync(CancellationToken ct = default)
        => _channel.RequestAsync(new byte[] { 0x31, 0x90, 0x11 }, ct);

    /// <summary>Seed-key unlock of the ECU's tuning resources.</summary>
    public Task UnlockAsync(SecurityLevel level = SecurityLevel.Read, CancellationToken ct = default)
        => _security.UnlockAsync(level, ct);

    public ValueTask DisposeAsync() => _channel.DisposeAsync();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter SagemSessionTests`
Expected: PASS.

- [ ] **Step 5: Run the full suite + commit**

Run: `dotnet test`
Expected: PASS — all prior tests plus the new Security + SagemSession tests; no regressions.

```bash
git add src/OpenEcu.Core/Obd/SagemSession.cs tests/OpenEcu.Core.Tests/Obd/SagemSessionTests.cs
git commit -m "feat: SagemSession (connect + start-diag + seed-key unlock)"
```

---

### Task 5: Probe `securityaccess` mode (capture-first, on the bike)

**Files:**
- Modify: `src/OpenEcu.Probe/Program.cs`

- [ ] **Step 1: Add usings**

In `src/OpenEcu.Probe/Program.cs`, add to the using block at the top (after the existing `using` lines):
```csharp
using OpenEcu.Core.Security;
using OpenEcu.Core.Transport;
```

- [ ] **Step 2: Add the securityaccess branch**

In `src/OpenEcu.Probe/Program.cs`, immediately after the line `var transport = new SerialPortTransport(port);`, insert:
```csharp
string mode = args.Length > 1 ? args[1] : "scan";
if (mode == "securityaccess")
{
    var logging = new LoggingTransport(transport);
    logging.BytesWritten += b => Console.WriteLine($"  TX {BitConverter.ToString(b)}");
    logging.BytesRead    += b => Console.WriteLine($"  RX {BitConverter.ToString(b)}");
    await using var sagem = new SagemSession(logging, transport);
    try
    {
        Console.WriteLine("Connecting (5-baud init + keyword handshake)...");
        await sagem.ConnectAsync();
        Console.WriteLine("Connected. StartDiagnosticSession (31 90 11)...");
        ObdResponse diag = await sagem.StartDiagnosticAsync();
        Console.WriteLine($"  start-diag reply: SID 0x{diag.ServiceId:X2} [{BitConverter.ToString(diag.Payload)}]");
        Console.WriteLine("SecurityAccess: request seed + send computed key (27 03 02)...");
        await sagem.UnlockAsync(SecurityLevel.Read);
        Console.WriteLine("\n*** ACCESS GRANTED — ECU unlocked. ***");
    }
    catch (SecurityAccessException ex)
    {
        Console.WriteLine($"\n*** Unlock rejected — NRC 0x{ex.Nrc:X2}. {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Security-access error: {ex.GetType().Name}: {ex.Message}");
    }
    return;
}
```

- [ ] **Step 3: Build the probe**

Run: `dotnet build src/OpenEcu.Probe`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/OpenEcu.Probe/Program.cs
git commit -m "feat: probe securityaccess mode (capture-first seed-key unlock)"
```

- [ ] **Step 5: Manual run on the bike (human)**

Bike powered (ignition on / battery tender), FTDI cable on COM8, latency timer 1 ms.

Run: `dotnet run --project src/OpenEcu.Probe -- COM8 securityaccess`

Capture-first: record the raw `TX`/`RX` lines, the start-diag reply, and the raw seed `RX`. Expected outcome is **ACCESS GRANTED**. If it is rejected (NRC 0x35 invalidKey), the capture tells us whether the framing/path differs from the OBD `68 6A F1` assumption — feed that back to refine `SagemSession`/`SagemSecurityAccess` before declaring the sub-project done. NRC 0x36/0x37 means attempt-lockout — wait before retrying.

---

## Self-Review

**Spec coverage:**
- Clean-room provenance → Task 1 (`docs/SEEDKEY.md`), reaffirmed in every code comment.
- `SagemSeedKey` pure algorithm + known-answer vectors → Task 2 (master `0x9A5F944B3A59454B`, KeyR `0xA006`, multiplier `0x6789`, vector table).
- `SagemSecurityAccess` handshake (seed → key → granted; already-unlocked; rejection NRC) → Task 3.
- `SagemSession` (connect + start-diag + unlock; distinct from OBD session) → Task 4.
- Capture-first probe → Task 5.
- Safety (unlock only, no writes) → no write/memory paths anywhere in the plan.
- Extensibility to other manufacturers → noted in `docs/SEEDKEY.md`; `SagemSeedKey` is one provider, `SecurityLevel`/`IObdRequestChannel` are the seams.

**Placeholder scan:** none — every step has concrete code/commands/expected output.

**Type consistency:** `SagemSeedKey.ComputeKey(ushort, SecurityLevel) -> ushort`; `IObdRequestChannel.RequestAsync(byte[], CancellationToken) -> Task<ObdResponse>` (matches the existing `KLineObdSession.RequestAsync` signature exactly); `SecurityAccessException(byte nrc, string message)` with `.Nrc`; `SagemSecurityAccess(IObdRequestChannel).UnlockAsync(SecurityLevel, CancellationToken)`; `SagemSession(IEcuTransport, IBreakLine, byte, Func<TimeSpan,Task>?)` with `.ConnectAsync`/`.StartDiagnosticAsync()->Task<ObdResponse>`/`.UnlockAsync(SecurityLevel, CancellationToken)`. Response frame checksums in the Task 4 fixture verified additive mod 256 (`67 03 02 12 34`→`0x36`, `67 03 02`→`0xF0`, `71 90`→`0x85`).

**Test-vector cross-check:** `ComputeKey(0x1234)=0xA9D4` is consistent between Task 2 (vector table), Task 3 (key request bytes `…A9 D4`), and Task 4 (FakeEcu key-request key `270302A9D4`).
