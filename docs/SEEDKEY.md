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
