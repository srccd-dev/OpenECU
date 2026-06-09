# OpenECU — Cross-Platform Rebuild — Design Spec

> **OpenECU** is a clean-room, cross-platform successor to the discontinued **TuneECU**
> Windows app. Throughout this document, "OpenECU" is the new project we are building and
> "TuneECU" refers to the original proprietary application we are replacing.

- **Date:** 2026-06-09
- **Status:** Approved (design); pending implementation plan
- **Author:** Michael Neumann (with Claude)
- **Repo:** <https://github.com/srccd-dev/OpenECU> (public)
- **License:** MIT

---

## 1. Background & problem

The original **TuneECU** Windows desktop application (last build v2.5.8, files dated
2024-06-30) is a motorcycle ECU **diagnostics and remapping** tool. The maintainer has
discontinued the Windows version; only the Android app is still maintained.

What we actually have on disk is **the compiled binary only** — there is no source code:

| File | Detail |
|---|---|
| `TuneECU.exe` | 1.3 MB, .NET assembly v2.5.8.0 |
| `TuneLibrary.dll` | 282 KB, .NET assembly v2.5.8.9 |

Reverse-engineering of the binaries established:

- **.NET Framework**, compiled against **CLR 2.0** (`v2.0.50727`) — needs the legacy
  .NET 3.5 runtime, which is *not* enabled by default on modern Windows.
- **WinForms** UI (`System.Windows.Forms`, `System.Drawing`), written in **C#**.
- **Not obfuscated** — decompiled C# (via ILSpy) is clean and readable.
- Comms layer talks to the bike over **ISO9141 / KWP2000 K-line**, using the native
  **FTDI `FTD2XX.dll`** D2XX driver (24 P/Invokes) plus a `System.IO.Ports.SerialPort`
  fallback; uses WMI (`System.Management`) to enumerate ports.
- Architecture is a deep WinForms **inheritance "god object"**: everything derives from
  `ISOMain : Form` (`ISOFT` comms, `ISORead` protocol, `ISensor` live data, `IMap` map
  editor, `IDraw` graphing). `TuneLibrary.Tune` holds the tune-file format + embedded ECU
  definition tables.

**Goal:** Build a **new, clean-room, cross-platform** application that preserves and
extends this capability for the community, hosted on the user's GitHub.

## 2. Goals / non-goals

**Goals**
- A modern, maintainable, **cross-platform** (Windows/macOS/Linux) rebuild.
- A **protocol-agnostic core** with **pluggable ECU definitions**; first target the
  **Sagem MC1000** ECU (validated against a **2004 Triumph Speed Triple 955i**).
- Support the adapters the community actually uses (see §6).
- **Clean-room provenance**: decompiled code referenced for *behavior only*; no decompiled
  source or extracted proprietary data ever published.

**Non-goals (for v1)**
- Writing/flashing maps to the ECU (deferred to a later, hardened phase — see §10).
- BLE adapter support (architected-for, implemented later — see §6).
- Supporting every brand/ECU at once. Architecture is generic; only Sagem MC1000 is wired
  up first.
- Bundling any ECU maps. Maps are **user-supplied** from the community database.

## 3. Locked decisions (from brainstorming)

| Decision | Choice |
|---|---|
| Build approach | Clean-room rebuild; decompiled code = reference only |
| Platform / framework | .NET 8/9 + **Avalonia** (cross-platform) |
| Core architecture | Protocol-agnostic core, pluggable ECU definitions |
| First ECU | Sagem MC1000 (2004 Triumph Speed Triple 955i = test rig) |
| Maps | User-supplied from [tuneecu.net/Map_Database.html](https://tuneecu.net/Map_Database.html); never bundled |
| v1 capabilities | **Read-only diagnostics** (no flashing) |
| v1 transports | FTDI K-line **+** ELM327-over-SPP; BLE deferred |
| Solution structure | Approach A — core-first layered solution |
| Project name | **OpenECU** |
| Repository | `srccd-dev/OpenECU` — **public** |
| License | **MIT** |

## 4. Approach (chosen: A — core-first layered solution)

A headless, fully testable `OpenEcu.Core` holds the protocol engine, ECU-definition model,
and map parser. Native comms sit behind a transport interface. The Avalonia UI is a thin
consumer. This isolates the riskiest part (cross-platform comms) behind an interface and
lets it be test-driven against captured/simulated traffic — so we never debug protocol and
UI simultaneously.

*(Rejected: B "UI-first" couples UI to protocol and defers the hard comms work; C
"single-project monolith" reproduces the original's unmaintainable god-object.)*

## 5. Solution / repo layout

```
OpenECU/   (public repo: github.com/srccd-dev/OpenECU)
├─ src/
│  ├─ OpenEcu.Core/              # headless, cross-platform. NO UI, NO native deps
│  │   ├─ Transport/             # IEcuTransport abstraction (byte stream)
│  │   ├─ Adapters/              # IEcuAdapter: KLineProtocol | Elm327Adapter
│  │   ├─ Protocol/              # ISO9141/KWP2000 framing, checksum, init state machine
│  │   ├─ Ecu/                   # EcuDefinition model + SagemMc1000Definition
│  │   ├─ Maps/                  # community map-file parser/model (reverse-engineered)
│  │   └─ Diagnostics/           # sensors, fault codes, services
│  ├─ OpenEcu.Transport.Ftdi/    # FTDI D2XX wrapper (cross-platform libftd2xx)
│  ├─ OpenEcu.Transport.Serial/  # System.IO.Ports VCP (USB-serial + BT-Classic COM)
│  ├─ OpenEcu.Transport.Bluetooth/ # BT-Classic discovery/pairing (32feet.NET on Win)
│  └─ OpenEcu.App/               # Avalonia MVVM UI
├─ tests/OpenEcu.Core.Tests/     # xUnit + simulated/replay ECU
├─ docs/                         # protocol notes, CLEANROOM.md, map-format notes
└─ _reference/   (GIT-IGNORED, NEVER published — decompiled code lives here)
```

## 6. Transport & adapter design (two-tier)

The key insight: adapters split into **"dumb" cables** (the PC performs K-line
signaling/timing itself through the chip) vs **"smart" adapters** (an onboard MCU runs the
OBD protocol; the PC drives it with the **ELM327/STN AT-command set**). These are different
code paths, modelled as two tiers.

**Tier 1 — `IEcuTransport`** (raw byte stream):
- `FtdiD2xxTransport` — USB → FTDI direct (needed for K-line init timing).
- `SerialPortTransport` — `System.IO.Ports`; covers USB-serial **and** Bluetooth-Classic
  (which surfaces as a COM port / `/dev/rfcomm`).
- `BluetoothClassicTransport` — SPP discovery/pairing convenience (32feet.NET on Windows;
  BlueZ on Linux).
- `BleTransport` — **deferred** (see risk in §11).

**Tier 2 — `IEcuAdapter`** (protocol on top of the byte stream):
- `KLineProtocol` — for the dumb FTDI KKL cable; *we* implement ISO9141/KWP2000 init,
  framing, checksum (reverse-engineered from `ISORead.cs`).
- `Elm327Adapter` — speaks AT/STN commands; the adapter handles the bus. Includes
  **firmware-version gating** (UniCarScan ≥ 2.49, vLinker ≥ 4.3.2).

**Supported adapter matrix:**

| Adapter | Tier-1 link | Tier-2 protocol | v1? |
|---|---|---|---|
| USB KKL 409.1 (FTDI) | FTDI D2XX | KLineProtocol | ✅ implemented (955i test) |
| OBDLink LX / MX / MX+ | BT-Classic SPP | Elm327Adapter | ✅ implemented |
| OBD vLinker MC (fw ≥ 4.3.2) | BT-Classic SPP | Elm327Adapter | ✅ implemented |
| UniCarScan UCSI-2100 (fw ≥ 2.49) | **BLE** | Elm327Adapter | 🔭 architected, deferred |

Recommended community USB cables (FTDI KKL 409.1) to document in the README:
- https://www.lonelec.com/product/tune-ecu-kkl-interface-cable-lead
- https://www.obdauto.fr/cable-kkl-special-moto-aprilia-ktm-triumph-moto-guzzi-et-ducati

**Test-bike note:** the 2004 Speed Triple 955i uses the **FTDI K-line cable**, not the
Bluetooth adapters (those serve newer ride-by-wire Triumphs). So v1 hardware-in-the-loop
validation runs on the FTDI path; the ELM327/SPP path is verified against an ELM327
simulator and/or community testers with newer bikes.

## 7. Core protocol & ECU model

- `KLineProtocol`: reverse-engineer message framing (header byte `0x80|len` family),
  additive checksum (`CalcChecksum`), and init (standard ISO slow/fast init + KWP2000)
  from the decompiled `ISORead.cs` — re-implemented as fresh, documented code.
- `EcuDefinition`: describes one ECU — init type, memory map, sensor table (PID → scaling),
  fault-code table, map layout. `SagemMc1000Definition` is the first plugin.
- Map (`OpenEcu.Core/Maps`): reverse-engineer the tune-file format from `TuneLibrary.Tune`;
  implement a fresh loader for community map files. A helper points users at the community
  database; **nothing is bundled.**

## 8. UI (Avalonia MVVM) — v1 read-only screens

- **Connection** — pick transport/adapter, port/device, connect/disconnect, status.
- **ECU Info** — ECU ID, VIN, firmware/cal info read from the ECU.
- **Live Sensors** — real-time gauges/graphs (RPM, TPS, temps, lambda, etc.).
- **Fault Codes** — read + clear DTCs.
- **Map Viewer** — read current map; display as table + 2D/3D plot (read-only).

## 9. GitHub, licensing & clean-room hygiene

- **Name: OpenECU** (deliberately not "TuneECU" — avoids the original's trademark).
- **Repo:** <https://github.com/srccd-dev/OpenECU> — **public**, open source.
- **License: MIT** (already on the repo).
- `docs/CLEANROOM.md` documenting that only observable behavior was referenced; decompiled
  code is git-ignored (`_reference/`, `_decompiled/`) and never published.
- README carries a prominent **"use at your own risk / no manufacturer affiliation"**
  disclaimer (mirrors the community database's terms).
- Original binaries and decompiled output are git-ignored from commit #1 (see `.gitignore`).

## 10. Roadmap / phasing

- **Phase 0 — Reverse-engineering & scaffolding:** decompile (done), document the K-line
  protocol + map-file format, scaffold the solution, define interfaces, build the ECU
  simulator for tests.
- **Phase 1 — v1 read-only (this spec):** FTDI + ELM327/SPP transports; Sagem MC1000
  definition; connect, ECU info, live sensors, fault codes, read-only map viewer; Avalonia
  UI; HIL-validated on the 955i.
- **Phase 2 — Offline map editing:** open/edit/save community map files (table editor +
  graphs); still no flashing.
- **Phase 3 — Hardened flashing ("remap W"):** write maps / flash ECU with checksum
  validation, connection-loss safeguards, recovery. Enables Bluetooth remap of newer
  Triumphs via `Elm327Adapter`.
- **Later:** BLE transport (UniCarScan); additional ECU definitions (KTM/Aprilia/Ducati/
  Guzzi…); enhancements backlog.

## 11. Risks

- **Cross-platform BLE** (UniCarScan) — no unified .NET desktop BLE story; platform-specific
  (WinRT / BlueZ / CoreBluetooth). **Mitigation:** deferred; SPP adapters first.
- **FTDI K-line init timing cross-platform** — slow-init timing via D2XX may differ on
  Linux/macOS. **Mitigation:** D2XX on Windows, serial/VCP fallback elsewhere; validate HIL.
- **Map-file format fidelity** — must round-trip community maps exactly. **Mitigation:**
  byte-exact tests against sample files from the community DB.
- **Legal** — derived from proprietary freeware. **Mitigation:** clean-room process, no
  decompiled artifacts published, new name, disclaimer; revisit author permission before
  public release.
- **ECU safety** — even read operations touch a live ECU. **Mitigation:** read-only v1;
  flashing only after comms hardening (Phase 3).

## 12. Open questions

Resolved: name = **OpenECU**; repo = **public** `srccd-dev/OpenECU`; license = **MIT**.

Remaining (optional, non-blocking):
- Whether to contact the original TuneECU author as a courtesy / for blessing. Not required
  for a clean-room MIT project, but good community etiquette.
