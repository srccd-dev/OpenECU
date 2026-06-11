# OpenECU v2 — Sub-project 1: Security Access (Seed-Key Unlock) — Design

> **Status:** Approved design (brainstorming). Next: implementation plan via writing-plans.
> **Date:** 2026-06-10
> **Epic:** v2 (full read + write/flash). This is **sub-project 1 of 6**.

## v2 Roadmap (context)

| # | Sub-project | This spec |
|---|---|---|
| **1** | **Security Access (seed-key unlock)** | ← **this doc** |
| 2 | Map memory read + map data model | later |
| 3 | Map file format (community `.map` load/save) | later |
| 4 | Heat-map visualization | later |
| 5 | Map editing + difference reporting | later |
| 6 | Write / flash (checksum, verify, recovery) | later |

Each sub-project is its own spec → plan → implement cycle. Everything below #1 requires an unlocked ECU, so security access is the foundation built first.

## Purpose

The Sagem MC1000 gates its tuning resources (map memory, programming) behind a KWP2000 **SecurityAccess** (`0x27`) seed-and-key handshake. This sub-project implements that unlock: request a seed from the ECU, compute the correct key, send it, and confirm access is granted — proven on the real 2004 Triumph Speed Triple 955i. It delivers **unlock only** (no protected reads yet, no writes). It is the gate that the rest of v2 passes through.

## Clean-room provenance (non-negotiable)

The seed-key algorithm and its constants are a **published standard**, not a TuneECU secret:

- SN-IMC-1-104 "Unlocking ECU Resources by Seed and Key" (the standard).
- `jglim/UnlockECU` (MIT-licensed, C#) — the open-source reference for this class of algorithm.
- The Triumph forum thread documenting ECU reprogramming.

OpenECU implements the algorithm **from these public sources**, documented with citations in a new `docs/SEEDKEY.md`. The decompiled `CalculateKey`/`Setkeys` is used **only as a correctness cross-check** of values we derive independently — never copied, never published, never cited as the source. The constants live in OpenECU's own code with public-source attribution. This keeps the clean-room boundary intact.

## Architecture

Three units, each independently understandable and testable:

```
SagemSeedKey (pure)          SagemSecurityAccess              SagemSession
  seed -> key                  orchestrates the 0x27           connect + start-diag
  no I/O, no state             request/response exchange       + Unlock(); owns the
  fully unit-testable          over an IObdSession-like         K-line transport for
                               request channel                  the Triumph tuning path
```

### Unit 1 — `SagemSeedKey` (pure algorithm) · `OpenEcu.Core/Security/SagemSeedKey.cs`

A pure, stateless function. No transport, no async, no I/O.

```
ushort ComputeKey(ushort seed, SecurityLevel level)
enum SecurityLevel { Read }   // Flash and others added in their own sub-projects
```

Key derivation from the published 64-bit master constant `0x9A5F944B3A59454B`:

```
KEYR, KEYW derived once from the master (the published Setkeys derivation):
    low32  = master & 0xFFFFFFFF
    high32 = master >> 32
    keyw0  = (high32 ^ low32) & 0xFFFFFFFF
    KEYR   = (keyw0 >> 16) & 0xFFFF   -> 0xA006
    KEYW   =  keyw0        & 0xFFFF   -> 0xD100

Read level (Sagem, our OBD/Init=247 path):
    key = (seed * (KEYR ^ 51087)) & 0xFFFF      // multiplier = 0x6789
```

**Known-answer vectors** (computed independently; embedded as unit tests):

| seed | key |
|------|-----|
| `0x0001` | `0x6789` |
| `0x1234` | `0xA9D4` |
| `0xABCD` | `0x6BB5` |
| `0xFFFF` | `0x9877` |

The clean `0x6789` multiplier is itself a sanity signal the derivation is correct. (The Flash/KWP-path multipliers — `KEYW ^ {40014, 48689, …}` — are documented for later sub-projects but **not implemented now**; YAGNI.)

### Unit 2 — `SagemSecurityAccess` · `OpenEcu.Core/Security/SagemSecurityAccess.cs`

Orchestrates the handshake over a request channel (the same `RequestAsync(byte[])` shape `KLineObdSession` already exposes), using `SagemSeedKey` for the math:

```
UnlockAsync(level):
  1. send  27 03 02                       (request seed, Sagem sub-function)
  2. recv  67 03 02 <seedHi> <seedLo>      -> seed = (seedHi<<8)|seedLo
  3. key   = SagemSeedKey.ComputeKey(seed, level)
  4. send  27 03 02 <keyHi> <keyLo>        (send key)
  5. recv  67 03 02                         -> access granted (no seed payload = unlocked)
     recv  7F 27 <nrc>                      -> rejected, throw SecurityAccessException(nrc)
  • seed == 0x0000 -> already unlocked, succeed without sending a key
```

Returns success/failure; raises `SecurityAccessException` on a negative response (`0x7F`). No knowledge of serial ports — it talks to an injected request channel, so it tests against a scripted fake.

### Unit 3 — `SagemSession` · `OpenEcu.Core/Obd/SagemSession.cs`

The Triumph **tuning** session, distinct from the OBD-only `KLineObdSession`. It owns the K-line transport for the proprietary diagnostic path and exposes the foundation this sub-project needs: `ConnectAsync` (init), `StartDiagnosticAsync` (`StartDiagnosticSession`), and `UnlockAsync` (delegates to `SagemSecurityAccess`). Map-memory reads are **not** added here yet — that is sub-project 2. We keep it separate from `KLineObdSession` because the framing, init, and service set differ; overloading the read-only OBD session would blur a clean boundary.

## The protocol is partly unknown → capture-first (as in v1)

The seed-key **math** is certain (above). What is **not** certain without the bike: the exact session framing for `0x27` on this ECU (OBD `68 6A F1` path vs. the Triumph `D5 F5` path), the precise init sequence the tuning session needs, and the exact positive-response shape. v1 resolved exactly this kind of uncertainty by capturing real traffic first. We do the same:

**`OpenEcu.Probe`** gains a `securityaccess` mode that, against the live ECU: sends `27 03 02`, prints the raw seed response, computes the key with `SagemSeedKey`, sends it, and prints whether access was granted. We read the real framing off the wire, lock it into `SagemSession`, and only then finalize the session code. **No map data is touched — unlock only.**

## Data flow

```
SagemSession.UnlockAsync(Read)
  └─ SagemSecurityAccess.UnlockAsync(Read)
       ├─ request channel: 27 03 02            ──► ECU
       │                    67 03 02 SH SL      ◄── ECU   (seed)
       ├─ SagemSeedKey.ComputeKey(seed, Read)  =  key
       ├─ request channel: 27 03 02 KH KL       ──► ECU
       │                    67 03 02            ◄── ECU   (granted)
       └─ return Unlocked
```

## Error handling

- **Negative response** (`7F 27 nrc`): `SecurityAccessException` carrying the NRC byte (e.g. `0x35` invalidKey, `0x36` exceededAttempts, `0x37` requiredTimeDelay). The message names the NRC where known.
- **Wrong key** (`0x35`): surfaced clearly — this is the signal our derivation/path is off, and the cue to re-check against the capture.
- **Attempt lockout** (`0x36`/`0x37`): documented; the ECU enforces a delay after repeated bad keys. The probe warns before retrying.
- **Timeout / malformed frame**: existing `IncompleteFrameException`/transport timeout paths; `UnlockAsync` surfaces them, leaves the ECU untouched.

## Testing strategy

1. **`SagemSeedKey`** — pure unit tests against the known-answer vector table above (and `seed 0 -> key 0`). The provenance anchor.
2. **`SagemSecurityAccess`** — against a scripted fake request channel: happy path (seed → key → granted), already-unlocked (`seed 0`), and rejection (`7F 27 35` → `SecurityAccessException`). Verifies the exact bytes sent.
3. **`SagemSession`** — connect → start-diag → unlock against the existing `FakeEcu`/`SimulatedTransport`, scripted with the seed/key exchange.
4. **Bike validation (human, capture-first):** run the probe `securityaccess` mode on the 955i; confirm the ECU returns *granted*. This is the real proof and the source of truth for the framing.

Stack: .NET 8, xUnit, **AwesomeAssertions**. Async transport methods use the established sync-helper pattern (no `ReadOnlySpan<byte>` out-params in async).

## Safety / scope

- **Unlock only.** No memory reads, no writes, **zero brick risk.** Writing is sub-project 6, gated behind much more safety work.
- Unlock is **non-destructive and session-scoped** — it grants access for the current session; it does not alter ECU contents.
- **Out of scope (YAGNI):** Flash/other security levels, map memory reads, any UI. UI exposure of "unlock" waits until there is something unlocked to *do* (sub-project 2+).

## Deliverables

- `docs/SEEDKEY.md` — algorithm + public-source citations (provenance).
- `OpenEcu.Core/Security/SagemSeedKey.cs` (+ `SecurityLevel`) and tests.
- `OpenEcu.Core/Security/SagemSecurityAccess.cs` (+ `SecurityAccessException`) and tests.
- `OpenEcu.Core/Obd/SagemSession.cs` and tests.
- `OpenEcu.Probe` `securityaccess` mode (capture-first bike tool).

## Validation checklist (definition of done)

- [ ] `SagemSeedKey` passes the known-answer vectors.
- [ ] `SagemSecurityAccess` passes happy-path, already-unlocked, and rejection tests.
- [ ] `SagemSession` connect → start-diag → unlock passes against the fake.
- [ ] Full suite green; clean build.
- [ ] **On the bike:** probe `securityaccess` returns *access granted*.
- [ ] `docs/SEEDKEY.md` committed with public-source citations; no decompiled code copied.
