# OpenECU v2 — Sub-project 2: ECU Memory Read — Design

> **Status:** Approved design (brainstorming). Next: implementation plan via writing-plans.
> **Date:** 2026-06-11
> **Epic:** v2 (full read + write/flash). This is **sub-project 2 of 6**.

## v2 Roadmap (context + reorder)

Sub-project 1 (Security Access / seed-key unlock) is **done, merged, and validated on the bike**. While designing SP2 we found that **map layout is not self-describing in the ECU** — where a map lives, its dimensions, and its RPM/throttle axes all come from the TuneLibrary `.Tune` definition (community map files carry it, XOR-obfuscated via `codecMap`). So *reading* ECU memory is cleanly separable from *interpreting* it as labeled maps. The roadmap is reordered accordingly:

| # | Sub-project | This spec |
|---|---|---|
| 1 | Security Access (seed-key unlock) | done ✓ |
| **2** | **ECU memory read (read-by-address + memory image)** | ← **this doc** |
| 3 | `.Tune` map file format **+ labeled-map interpretation** (definitions → maps over read memory) | next |
| 4 | Heat-map visualization | later |
| 5 | Map editing + difference reporting | later |
| 6 | Write / flash (checksum, verify, recovery) | later |

## Purpose

Add the ability to **read ECU memory by address** over the unlocked K-line session, and model the result as an addressable image. This is the primitive every later piece needs: a single map region or the whole flash is read the same way. Delivered **read-only** (no writes), and **validated on the 2004 Speed Triple 955i** by reading real memory bytes off the ECU.

This sub-project does **not** know what a "map" is — where maps live and their axes is `.Tune` definition work (SP3). SP2 stops at "I can reliably read bytes from address A for length N, and hold them in an addressable image."

## The read-memory protocol (grounded in decompiled cross-check only)

The original uses standard KWP **ReadMemoryByAddress (`0x23`)** over the OBD `68 6A F1` header (the Init=247 path, same header as our OBD reads and the SP1 unlock):

```
Request:  23 A2 A1 A0 LEN 00      ; 3-byte big-endian address, 1-byte block length, trailing 00
Response: 63 <LEN data bytes>     ; positive response SID = 0x23 + 0x40 = 0x63
          7F 23 <nrc>             ; negative response (address unreadable, etc.)
```

Bulk reads loop this in `LEN`-sized blocks (Sagem block size = 32), incrementing the address each block and concatenating the data. The decompiled `SendReadData` builds exactly `{ 0x23, addr>>16, addr>>8, addr, sBloc, 0 }`.

## On-bike sequence

```
connect (5-baud init + keyword handshake)
  -> UnlockAsync(Read)             ; SP1 — required first
  -> StartDiagnosticAsync (31 90 11) ; expected to SUCCEED now (post-unlock); closes SP1's open question
  -> ReadMemoryAsync(addr, len)    ; loops 0x23 block reads
  -> MemoryImage
```

SP1 found `StartDiagnosticSession` returns NRC `0x33` (securityAccessDenied) *before* unlock. SP2 verifies it succeeds *after* unlock, and treats a successful start-diag as the gate into the read session.

## Architecture — three units

Each unit has one purpose, a clear interface, and is testable in isolation. The design mirrors SP1 (`SagemSecurityAccess` takes an `IObdRequestChannel`; `SagemSession` delegates).

### Unit 1 — `SagemMemoryReader` · `OpenEcu.Core/Memory/SagemMemoryReader.cs`

Pure read-protocol logic over an injected request channel. No serial knowledge.

```
SagemMemoryReader(IObdRequestChannel channel)
Task<byte[]> ReadMemoryAsync(int address, int length, int blockSize = 32, CancellationToken ct = default)
```

Loops from `address` to `address + length` in `blockSize` chunks (last block may be shorter). For each chunk it sends `23 A2 A1 A0 LEN 00`, requires response SID `0x63` with exactly `LEN` payload bytes, and appends them. Returns the assembled `byte[]`. On a `0x7F` response it throws `MemoryReadException(nrc, address)`.

### Unit 2 — `MemoryImage` · `OpenEcu.Core/Memory/MemoryImage.cs`

An addressable byte buffer over a read region.

```
MemoryImage(int baseAddress, byte[] bytes)
int BaseAddress { get; }
int Length { get; }
byte this[int address] { get; }          // indexed by absolute address
ReadOnlySpan<byte> Slice(int address, int length)
```

Indexer/`Slice` translate absolute address → offset and throw `ArgumentOutOfRangeException` outside `[BaseAddress, BaseAddress+Length)`. This is the model SP3's map definitions will read regions out of.

### Unit 3 — `SagemSession` (extend) · `OpenEcu.Core/Obd/SagemSession.cs`

Add memory read, delegating to a composed `SagemMemoryReader`:

```
Task<MemoryImage> ReadMemoryAsync(int address, int length, int blockSize = 32, CancellationToken ct = default)
```

Returns a `MemoryImage` with `BaseAddress = address`. Unlock + StartDiagnostic already exist from SP1.

### Supporting — `MemoryReadException` · `OpenEcu.Core/Memory/MemoryReadException.cs`

```
MemoryReadException(byte nrc, int address, string message) : Exception   // .Nrc, .Address
```

## Capture-first probe

`OpenEcu.Probe` gains a `readmem` mode: connect → unlock → start-diag (print the reply) → `ReadMemoryAsync(address, length)` → hex-dump the bytes. Address and length come from args (`dotnet run --project src/OpenEcu.Probe -- COM8 readmem <addrHex> <len>`) so we can probe readable regions live. Wraps the transport in `LoggingTransport` for raw TX/RX capture, as the SP1 probe does. **Read-only — touches no map, writes nothing.** Success = stable, repeatable bytes across reads.

## Error handling

- **Negative response `7F 23 nrc`** → `MemoryReadException` carrying the NRC and the failing address. Some addresses are unreadable; the capture identifies which respond.
- **Short/empty block** (response SID `0x63` but fewer than `LEN` bytes, or a non-`0x63`/non-`0x7F` SID) → `MemoryReadException` describing the mismatch; the partial read is discarded.
- **Timeout / malformed frame** → existing transport/`IncompleteFrameException` paths surface; the ECU is left untouched.

## Testing

1. **`SagemMemoryReader`** against a scripted `IObdRequestChannel` (same `ScriptedChannel` pattern as `SagemSecurityAccessTests`): single block; multi-block assembly with correctly incrementing addresses and a short final block; `0x7F` → `MemoryReadException` with the right NRC/address.
2. **`MemoryImage`** — absolute-address indexing and `Slice`; out-of-range throws.
3. **`SagemSession.ReadMemoryAsync`** — one integration read against `FakeEcu` scripted with `0x23`→`0x63` frames, asserting the returned `MemoryImage` contents and `BaseAddress`.
4. **Bike validation (human, capture-first):** `readmem` returns stable bytes; start-diag succeeds post-unlock.

Stack: .NET 8, xUnit, **AwesomeAssertions**. Async transport methods use the established sync-helper pattern (no `ReadOnlySpan<byte>` out-params in async).

## Safety / scope

- **Read-only. Zero brick risk.** No write/flash service is touched.
- **Out of scope (→ SP3, YAGNI):** map locations, dimensions, axes, `.Tune` decoding, any map/grid model, any UI. SP2 produces raw addressable bytes only.

## Deliverables

- `OpenEcu.Core/Memory/SagemMemoryReader.cs` (+ `MemoryReadException.cs`) and tests.
- `OpenEcu.Core/Memory/MemoryImage.cs` and tests.
- `SagemSession.ReadMemoryAsync` and an integration test.
- `OpenEcu.Probe` `readmem` mode (capture-first bike tool).

## Validation checklist (definition of done)

- [ ] `SagemMemoryReader` passes single-block, multi-block (incrementing address + short final block), and negative-response tests.
- [ ] `MemoryImage` indexing/`Slice`/out-of-range tests pass.
- [ ] `SagemSession.ReadMemoryAsync` integration test passes against `FakeEcu`.
- [ ] Full suite green; clean build.
- [ ] **On the bike:** start-diag succeeds after unlock; `readmem` returns stable, repeatable bytes from a readable region.
