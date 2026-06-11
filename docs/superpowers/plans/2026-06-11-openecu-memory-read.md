# OpenECU v2 Sub-project 2 — ECU Memory Read Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Read ECU memory by address over the unlocked K-line session (KWP ReadMemoryByAddress `0x23`), assemble the bytes, and model them as an addressable `MemoryImage` — validated on the 2004 Speed Triple 955i by reading real memory.

**Architecture:** Mirrors sub-project 1. `SagemMemoryReader` is pure protocol logic over an injected `IObdRequestChannel` (looping `0x23` block reads); `MemoryImage` is an addressable byte buffer; `SagemSession` gains a thin `ReadMemoryAsync` that delegates to a composed reader and returns a `MemoryImage`. A capture-first `readmem` probe mode runs connect → unlock → start-diag → read on the live bike.

**Tech Stack:** .NET 8, xUnit, **AwesomeAssertions**. Builds on SP1 (`IObdRequestChannel`, `SagemSession`, `ObdResponse`, `FakeEcu`, `LoggingTransport`).

**Provenance (clean-room):** The `0x23`/`0x63` read format is standard KWP2000; the decompiled `SendReadData` (`{ 0x23, addr>>16, addr>>8, addr, sBloc, 0 }`) is a **cross-check only**.

**Spec:** `docs/superpowers/specs/2026-06-11-openecu-memory-read-design.md`

**Read-only — zero brick risk.** No write/flash service is touched.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.Core/Memory/MemoryReadException.cs` | **Create:** read-rejection exception (NRC + address) |
| `src/OpenEcu.Core/Memory/SagemMemoryReader.cs` | **Create:** `0x23` block-read loop over `IObdRequestChannel` |
| `src/OpenEcu.Core/Memory/MemoryImage.cs` | **Create:** addressable byte buffer (indexer + `Slice`) |
| `src/OpenEcu.Core/Obd/SagemSession.cs` | **Modify:** add `ReadMemoryAsync` → `MemoryImage` |
| `src/OpenEcu.Probe/Program.cs` | **Modify:** add `readmem` capture-first mode |
| `tests/OpenEcu.Core.Tests/Memory/SagemMemoryReaderTests.cs` | **Create:** scripted-channel read tests |
| `tests/OpenEcu.Core.Tests/Memory/MemoryImageTests.cs` | **Create:** addressing tests |
| `tests/OpenEcu.Core.Tests/Obd/SagemSessionMemoryTests.cs` | **Create:** integration read vs `FakeEcu` |

---

### Task 1: `SagemMemoryReader` + `MemoryReadException`

**Files:**
- Create: `src/OpenEcu.Core/Memory/MemoryReadException.cs`
- Create: `src/OpenEcu.Core/Memory/SagemMemoryReader.cs`
- Test: `tests/OpenEcu.Core.Tests/Memory/SagemMemoryReaderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Memory/SagemMemoryReaderTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Memory;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Memory;

public class SagemMemoryReaderTests
{
    // Scripted request channel: records sent payloads, replays canned responses in order.
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
    public async Task ReadMemoryAsync_single_block_sends_0x23_and_returns_payload()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x63, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }));
        var reader = new SagemMemoryReader(channel);

        byte[] data = await reader.ReadMemoryAsync(0x123456, 4, blockSize: 32);

        data.Should().Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        channel.Sent.Should().ContainSingle();
        channel.Sent[0].Should().Equal(new byte[] { 0x23, 0x12, 0x34, 0x56, 0x04, 0x00 });
    }

    [Fact]
    public async Task ReadMemoryAsync_multi_block_increments_address_and_assembles()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x63, new byte[] { 0x01, 0x02 }),  // block 0 @ 0x1000, len 2
            new ObdResponse(0x63, new byte[] { 0x03 }));        // block 1 @ 0x1002, len 1 (short final)
        var reader = new SagemMemoryReader(channel);

        byte[] data = await reader.ReadMemoryAsync(0x1000, 3, blockSize: 2);

        data.Should().Equal(new byte[] { 0x01, 0x02, 0x03 });
        channel.Sent.Should().HaveCount(2);
        channel.Sent[0].Should().Equal(new byte[] { 0x23, 0x00, 0x10, 0x00, 0x02, 0x00 });
        channel.Sent[1].Should().Equal(new byte[] { 0x23, 0x00, 0x10, 0x02, 0x01, 0x00 });
    }

    [Fact]
    public async Task ReadMemoryAsync_throws_on_negative_response()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x7F, new byte[] { 0x23, 0x31 })); // requestOutOfRange
        var reader = new SagemMemoryReader(channel);

        Func<Task> act = () => reader.ReadMemoryAsync(0x9999, 16, blockSize: 32);

        await act.Should().ThrowAsync<MemoryReadException>()
            .Where(e => e.Nrc == 0x31 && e.Address == 0x9999);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SagemMemoryReaderTests`
Expected: FAIL — `SagemMemoryReader` / `MemoryReadException` do not exist (compile error).

- [ ] **Step 3: Create the exception**

Create `src/OpenEcu.Core/Memory/MemoryReadException.cs`:
```csharp
namespace OpenEcu.Core.Memory;

/// <summary>Thrown when an ECU memory read fails (KWP negative response, or a malformed block).</summary>
public sealed class MemoryReadException : Exception
{
    public byte Nrc { get; }
    public int Address { get; }

    public MemoryReadException(byte nrc, int address, string message) : base(message)
    {
        Nrc = nrc;
        Address = address;
    }
}
```

- [ ] **Step 4: Implement the reader**

Create `src/OpenEcu.Core/Memory/SagemMemoryReader.cs`:
```csharp
using OpenEcu.Core.Obd;

namespace OpenEcu.Core.Memory;

/// <summary>
/// Reads ECU memory over an IObdRequestChannel using KWP ReadMemoryByAddress (0x23):
/// 23 A2 A1 A0 LEN 00 -> 63 &lt;LEN bytes&gt;. Bulk reads loop in blockSize chunks,
/// incrementing the address, and concatenate. Throws MemoryReadException on 0x7F or a
/// malformed block. Pure protocol logic — no serial knowledge.
/// </summary>
public sealed class SagemMemoryReader
{
    private readonly IObdRequestChannel _channel;

    public SagemMemoryReader(IObdRequestChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<byte[]> ReadMemoryAsync(int address, int length, int blockSize = 32, CancellationToken ct = default)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (blockSize is <= 0 or > 255) throw new ArgumentOutOfRangeException(nameof(blockSize));

        var result = new byte[length];
        int done = 0;
        while (done < length)
        {
            int blockAddr = address + done;
            int blockLen = Math.Min(blockSize, length - done);
            ObdResponse resp = await _channel.RequestAsync(
                new byte[] { 0x23, (byte)(blockAddr >> 16), (byte)(blockAddr >> 8), (byte)blockAddr, (byte)blockLen, 0x00 }, ct);

            if (resp.ServiceId == 0x7F)
            {
                byte nrc = resp.Payload.Length >= 2 ? resp.Payload[1] : (byte)0;
                throw new MemoryReadException(nrc, blockAddr,
                    $"ReadMemoryByAddress rejected at 0x{blockAddr:X6} (NRC 0x{nrc:X2}).");
            }
            if (resp.ServiceId != 0x63 || resp.Payload.Length < blockLen)
                throw new MemoryReadException(0, blockAddr,
                    $"Unexpected read response at 0x{blockAddr:X6}: SID 0x{resp.ServiceId:X2}, {resp.Payload.Length} bytes (wanted {blockLen}).");

            Array.Copy(resp.Payload, 0, result, done, blockLen);
            done += blockLen;
        }
        return result;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter SagemMemoryReaderTests`
Expected: PASS (3 cases).

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Memory/MemoryReadException.cs src/OpenEcu.Core/Memory/SagemMemoryReader.cs tests/OpenEcu.Core.Tests/Memory/SagemMemoryReaderTests.cs
git commit -m "feat: SagemMemoryReader (KWP ReadMemoryByAddress 0x23 block reads)"
```

---

### Task 2: `MemoryImage`

**Files:**
- Create: `src/OpenEcu.Core/Memory/MemoryImage.cs`
- Test: `tests/OpenEcu.Core.Tests/Memory/MemoryImageTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Memory/MemoryImageTests.cs`:
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Memory;
using Xunit;

namespace OpenEcu.Core.Tests.Memory;

public class MemoryImageTests
{
    [Fact]
    public void Indexer_returns_byte_at_absolute_address()
    {
        var image = new MemoryImage(0x1000, new byte[] { 0xAA, 0xBB, 0xCC });

        image.BaseAddress.Should().Be(0x1000);
        image.Length.Should().Be(3);
        image[0x1000].Should().Be((byte)0xAA);
        image[0x1002].Should().Be((byte)0xCC);
    }

    [Fact]
    public void Slice_returns_region_by_absolute_address()
    {
        var image = new MemoryImage(0x1000, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });

        image.Slice(0x1001, 2).ToArray().Should().Equal(new byte[] { 0xBB, 0xCC });
    }

    [Fact]
    public void Out_of_range_access_throws()
    {
        var image = new MemoryImage(0x1000, new byte[] { 0xAA });

        var below = () => image[0x0FFF];
        var above = () => image[0x1001];
        var slicePastEnd = () => image.Slice(0x1000, 2).ToArray();

        below.Should().Throw<ArgumentOutOfRangeException>();
        above.Should().Throw<ArgumentOutOfRangeException>();
        slicePastEnd.Should().Throw<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter MemoryImageTests`
Expected: FAIL — `MemoryImage` does not exist.

- [ ] **Step 3: Implement the image**

Create `src/OpenEcu.Core/Memory/MemoryImage.cs`:
```csharp
namespace OpenEcu.Core.Memory;

/// <summary>An addressable byte buffer over a read region, indexed by absolute address.</summary>
public sealed class MemoryImage
{
    private readonly byte[] _bytes;

    public MemoryImage(int baseAddress, byte[] bytes)
    {
        BaseAddress = baseAddress;
        _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    }

    public int BaseAddress { get; }
    public int Length => _bytes.Length;

    public byte this[int address]
    {
        get
        {
            int offset = address - BaseAddress;
            if (offset < 0 || offset >= _bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(address),
                    $"Address 0x{address:X} outside image [0x{BaseAddress:X}, 0x{BaseAddress + _bytes.Length:X}).");
            return _bytes[offset];
        }
    }

    public ReadOnlySpan<byte> Slice(int address, int length)
    {
        int offset = address - BaseAddress;
        if (offset < 0 || length < 0 || offset + length > _bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(address),
                $"Slice [0x{address:X}, +{length}) outside image [0x{BaseAddress:X}, 0x{BaseAddress + _bytes.Length:X}).");
        return _bytes.AsSpan(offset, length);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter MemoryImageTests`
Expected: PASS (3 cases).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Memory/MemoryImage.cs tests/OpenEcu.Core.Tests/Memory/MemoryImageTests.cs
git commit -m "feat: MemoryImage addressable byte buffer (indexer + Slice)"
```

---

### Task 3: `SagemSession.ReadMemoryAsync`

**Files:**
- Modify: `src/OpenEcu.Core/Obd/SagemSession.cs`
- Test: `tests/OpenEcu.Core.Tests/Obd/SagemSessionMemoryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Obd/SagemSessionMemoryTests.cs`. (`FakeEcu` is in this same namespace `OpenEcu.Core.Tests.Obd`; `SagemSession` and `MemoryImage` are NOT, so they need explicit usings.)
```csharp
using AwesomeAssertions;
using OpenEcu.Core.Memory;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class SagemSessionMemoryTests
{
    private static Task NoDelay(TimeSpan _) => Task.CompletedTask;

    [Fact]
    public async Task ReadMemoryAsync_returns_image_with_base_address_and_bytes()
    {
        // Request 68 6A F1 23 00 10 00 04 00 FA -> payload "230010000400".
        // Response 48 6B D1 63 11 22 33 44 <ck>; ck = (0x48+0x6B+0xD1+0x63+0x11+0x22+0x33+0x44) & 0xFF = 0x91.
        var ecu = new FakeEcu(new()
        {
            ["230010000400"] = new byte[] { 0x48, 0x6B, 0xD1, 0x63, 0x11, 0x22, 0x33, 0x44, 0x91 },
        }, connected: true);
        await ecu.OpenAsync();
        await using var sagem = new SagemSession(ecu, ecu, delay: NoDelay);

        MemoryImage image = await sagem.ReadMemoryAsync(0x001000, 4);

        image.BaseAddress.Should().Be(0x001000);
        image.Length.Should().Be(4);
        image.Slice(0x001000, 4).ToArray().Should().Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SagemSessionMemoryTests`
Expected: FAIL — `SagemSession.ReadMemoryAsync` does not exist.

- [ ] **Step 3: Add the memory reader + method to SagemSession**

In `src/OpenEcu.Core/Obd/SagemSession.cs`:

(a) Add to the using block at the top:
```csharp
using OpenEcu.Core.Memory;
```

(b) Add a field next to `_security` (after the `_security` field declaration):
```csharp
    private readonly SagemMemoryReader _memory;
```

(c) In the constructor body, after `_security = new SagemSecurityAccess(_channel);`, add:
```csharp
        _memory = new SagemMemoryReader(_channel);
```

(d) Add the method after `UnlockAsync` (before `DisposeAsync`):
```csharp
    /// <summary>Reads <paramref name="length"/> bytes from <paramref name="address"/> into an addressable image.</summary>
    public async Task<MemoryImage> ReadMemoryAsync(int address, int length, int blockSize = 32, CancellationToken ct = default)
    {
        byte[] bytes = await _memory.ReadMemoryAsync(address, length, blockSize, ct);
        return new MemoryImage(address, bytes);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter SagemSessionMemoryTests`
Expected: PASS.

- [ ] **Step 5: Run the full suite + commit**

Run: `dotnet test`
Expected: PASS — all prior tests plus the 7 new (3 reader + 3 image + 1 session); no regressions (~147 passed, 1 skipped).

```bash
git add src/OpenEcu.Core/Obd/SagemSession.cs tests/OpenEcu.Core.Tests/Obd/SagemSessionMemoryTests.cs
git commit -m "feat: SagemSession.ReadMemoryAsync -> MemoryImage"
```

---

### Task 4: Probe `readmem` mode (capture-first, on the bike)

**Files:**
- Modify: `src/OpenEcu.Probe/Program.cs`

- [ ] **Step 1: Add the using**

In `src/OpenEcu.Probe/Program.cs`, add to the using block at the top (it already has `OpenEcu.Core.Security`, `OpenEcu.Core.Transport`, `OpenEcu.Core.Obd`):
```csharp
using OpenEcu.Core.Memory;
```

- [ ] **Step 2: Add the readmem branch**

In `src/OpenEcu.Probe/Program.cs`, immediately after the closing brace of the existing `if (mode == "securityaccess") { ... return; }` block, insert:
```csharp
if (mode == "readmem")
{
    int addr = args.Length > 2 ? Convert.ToInt32(args[2], 16) : 0x000000;
    int len  = args.Length > 3 ? int.Parse(args[3]) : 64;
    var logging = new LoggingTransport(transport);
    logging.BytesWritten += b => Console.WriteLine($"  TX {BitConverter.ToString(b)}");
    logging.BytesRead    += b => Console.WriteLine($"  RX {BitConverter.ToString(b)}");
    await using var sagem = new SagemSession(logging, transport);
    try
    {
        Console.WriteLine("Connecting (5-baud init + keyword handshake)...");
        await sagem.ConnectAsync();
        Console.WriteLine("Unlocking (SecurityAccess 27 03 02)...");
        await sagem.UnlockAsync(SecurityLevel.Read);
        Console.WriteLine("StartDiagnosticSession (31 90 11)...");
        ObdResponse diag = await sagem.StartDiagnosticAsync();
        Console.WriteLine($"  start-diag reply: SID 0x{diag.ServiceId:X2} [{BitConverter.ToString(diag.Payload)}]");
        Console.WriteLine($"Reading {len} bytes @ 0x{addr:X6} (ReadMemoryByAddress 0x23)...");
        MemoryImage image = await sagem.ReadMemoryAsync(addr, len);
        Console.WriteLine($"\n  {BitConverter.ToString(image.Slice(addr, len).ToArray())}");
        Console.WriteLine("\n*** READ OK ***");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"readmem error: {ex.GetType().Name}: {ex.Message}");
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
git commit -m "feat: probe readmem mode (capture-first ECU memory read)"
```

- [ ] **Step 5: Manual run on the bike (human)**

Bike powered, FTDI cable on COM8, latency timer 1 ms.

Run (defaults to 64 bytes @ 0x000000): `dotnet run --project src/OpenEcu.Probe -- COM8 readmem`
Or a chosen region: `dotnet run --project src/OpenEcu.Probe -- COM8 readmem 1000 128`  (hex address, decimal length)

Capture-first goals:
1. Confirm **start-diag now succeeds after unlock** (reply SID `0x71`, not `7F 33`) — closes SP1's open question.
2. Confirm `0x23` reads return **`0x63` + data**, and that **two reads of the same region match** (stable bytes).
3. If an address is rejected (`7F 23 <nrc>`, e.g. NRC 0x31 requestOutOfRange), try another address — the capture maps which regions respond. Feed findings back before declaring done.

---

## Self-Review

**Spec coverage:**
- `0x23`/`0x63` read-by-address loop, blockSize 32, incrementing address → Task 1 (`SagemMemoryReader`).
- `MemoryReadException` with NRC + address → Task 1.
- Short/malformed block handling → Task 1 (`SID != 0x63 || Payload.Length < blockLen`).
- `MemoryImage` (BaseAddress, Length, indexer, Slice, out-of-range throw) → Task 2.
- `SagemSession.ReadMemoryAsync` → `MemoryImage` → Task 3.
- On-bike sequence connect → unlock → start-diag → read, capture-first → Task 4 (closes SP1's start-diag-after-unlock question).
- Read-only / zero brick risk → no write service anywhere in the plan.

**Placeholder scan:** none — every step has concrete code/commands/expected output.

**Type consistency:** `SagemMemoryReader(IObdRequestChannel).ReadMemoryAsync(int address, int length, int blockSize = 32, CancellationToken) -> Task<byte[]>`; `MemoryReadException(byte nrc, int address, string message)` with `.Nrc`/`.Address`; `MemoryImage(int baseAddress, byte[]).BaseAddress/.Length/this[int]/Slice(int,int)`; `SagemSession.ReadMemoryAsync(int,int,int,CancellationToken) -> Task<MemoryImage>` (base address = read address). Request bytes `23 A2 A1 A0 LEN 00` match `SagemMemoryReader` and the Task 3 `FakeEcu` fixture (`230010000400`); response SID `0x63`. Test files that live in `OpenEcu.Core.Tests.Obd` but use `OpenEcu.Core.Obd`/`OpenEcu.Core.Memory` types include explicit `using` directives (the namespace-walk gotcha from SP1).

**Checksum cross-check (Task 3 fixture):** request `68 6A F1 23 00 10 00 04 00` → checksum `0xFA`; response `48 6B D1 63 11 22 33 44` → checksum `0x91`. Both additive mod 256, verified.
