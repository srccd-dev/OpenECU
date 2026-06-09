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
