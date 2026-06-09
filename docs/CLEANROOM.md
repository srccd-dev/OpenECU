# Clean-Room Provenance Statement

This project is a **clean-room reimplementation**. None of the original TuneECU source code
exists publicly; we only ever possessed the compiled binaries (`TuneECU.exe`,
`TuneLibrary.dll`).

## What we did

- Decompiled the binaries **for behavioral reference only** — to understand the K-line /
  ISO9141 / KWP2000 wire protocol, the tune-file format, and the ECU definition structure.
- Wrote **all shipped code fresh**, as original work. No decompiled source is copied into
  the published codebase.

## What is NEVER published

The following are **git-ignored** and must never be committed or distributed:

- `_reference/`, `_decompiled/` — decompiled C# from the original binaries.
- `TuneECU v2.5.8/`, `*.exe`, `*.dll` — the original proprietary binaries.
- Any ECU map/definition data tables extracted from the original `TuneLibrary.dll`.

## Maps & data

ECU maps are **user-supplied** from the community database
(<https://tuneecu.net/Map_Database.html>). No maps or proprietary data tables are bundled
with this software.

## Disclaimer

This software is provided for use **at your own risk**. It has **no affiliation with, and no
endorsement from, any motorcycle manufacturer** or the original TuneECU author. Modifying or
diagnosing an engine ECU can damage hardware; you assume all responsibility.
