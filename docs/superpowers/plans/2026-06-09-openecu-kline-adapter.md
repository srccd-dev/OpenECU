# OpenECU K-line Adapter & Handshake — Implementation Plan (Plan 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the message-level K-line adapter (`IEcuAdapter` / `KLineProtocol`) on top of plan 1's codec + transport: a request/response exchange, a frame reader that assembles whole frames from a byte stream, the KWP2000 connect/disconnect handshake (StartCommunication / StopCommunication), and a TesterPresent keep-alive — all verified against the in-memory simulator, no hardware.

**Architecture:** Tier-2 of the two-tier transport/adapter model. `KLineProtocol` implements `IEcuAdapter` using the existing `IEcuTransport` (tier-1) plus plan 1's `KLineFrameBuilder`/`KLineFrameParser`. A new `KLineFrameReader` reads exactly one frame from the stream by using the frame's length field. Responses are normalized into an `EcuResponse` value (positive vs. negative/NRC). Service IDs are standard KWP2000/ISO14230 + OBD-II constants (public, not derived from proprietary code).

**Tech Stack:** .NET 8 (C# 12), xUnit, FluentAssertions. Builds on the `OpenEcu.Core` solution from plan 1.

**Scope note:** Plan 2 of several. Independently testable (green suite against the simulator). It deliberately excludes: the physical wake-up/init electrical timing and request-echo stripping (→ plan 3, FTDI transport), SecurityAccess `0x27` and any writing/flashing (→ later phase), and ECU-specific data parsing like sensors/DTCs (→ diagnostics plan).

**Prerequisite:** Plan 1 (`2026-06-09-openecu-core-foundation.md`) is implemented: `KLineMode`, `KLineChecksum`, `KLineFrameBuilder`, `KLineFrameParser`, `IEcuTransport`, `SimulatedTransport` all exist and pass tests.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/OpenEcu.Core/Protocol/KwpServiceId.cs` | Standard service-ID + response constants |
| `src/OpenEcu.Core/Protocol/EcuResponse.cs` | Normalized response (positive vs negative/NRC) |
| `src/OpenEcu.Core/Protocol/IncompleteFrameException.cs` | Thrown when the stream ends mid-frame |
| `src/OpenEcu.Core/Protocol/KLineFrameReader.cs` | Reads exactly one complete frame from an `IEcuTransport` |
| `src/OpenEcu.Core/Adapters/IEcuAdapter.cs` | Tier-2 adapter abstraction |
| `src/OpenEcu.Core/Adapters/EcuConnectionException.cs` | Thrown when the connect handshake fails |
| `src/OpenEcu.Core/Adapters/KLineProtocol.cs` | `IEcuAdapter` impl: request/response + handshake |
| `tests/OpenEcu.Core.Tests/Protocol/EcuResponseTests.cs` | Response parsing tests |
| `tests/OpenEcu.Core.Tests/Protocol/KLineFrameReaderTests.cs` | Frame reader tests |
| `tests/OpenEcu.Core.Tests/Adapters/KLineProtocolTests.cs` | Adapter request + handshake tests |

**Test helper convention:** To fabricate scripted ECU responses in tests, reuse `KLineFrameBuilder.BuildRequest(payload, mode)` — the parser/reader don't care about the target/source address bytes, only the length field and checksum, so a "request" frame is a valid stand-in carrying an arbitrary payload. This keeps tests DRY and avoids hand-computed checksums.

---

### Task 1: KwpServiceId constants

**Files:**
- Create: `src/OpenEcu.Core/Protocol/KwpServiceId.cs`
- Test: `tests/OpenEcu.Core.Tests/Protocol/EcuResponseTests.cs` (constants asserted indirectly in Task 2; this task just adds the file)

This is a constants-only file (no behavior to TDD). It is exercised by Task 2's tests.

- [ ] **Step 1: Create the constants**

Create `src/OpenEcu.Core/Protocol/KwpServiceId.cs`:
```csharp
namespace OpenEcu.Core.Protocol;

/// <summary>
/// Standard KWP2000 / ISO 14230 service identifiers used by the K-line handshake.
/// Public protocol constants (not derived from any proprietary source).
/// </summary>
public static class KwpServiceId
{
    public const byte StartCommunication = 0x81;
    public const byte StopCommunication = 0x82;
    public const byte TesterPresent = 0x3E;

    /// <summary>First byte of a negative response: 0x7F, &lt;requestSid&gt;, &lt;nrc&gt;.</summary>
    public const byte NegativeResponse = 0x7F;

    /// <summary>A positive response SID equals the request SID plus this offset.</summary>
    public const byte PositiveResponseOffset = 0x40;

    /// <summary>Expected positive-response SID for a given request SID.</summary>
    public static byte PositiveResponseFor(byte requestSid) => (byte)(requestSid + PositiveResponseOffset);
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/OpenEcu.Core/Protocol/KwpServiceId.cs
git commit -m "feat: KWP2000 service-id constants"
```

---

### Task 2: EcuResponse

**Files:**
- Create: `src/OpenEcu.Core/Protocol/EcuResponse.cs`
- Test: `tests/OpenEcu.Core.Tests/Protocol/EcuResponseTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Protocol/EcuResponseTests.cs`:
```csharp
using FluentAssertions;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class EcuResponseTests
{
    [Fact]
    public void Positive_response_exposes_sid_and_payload()
    {
        // StartCommunication positive: 0xC1 + two key bytes
        byte[] payload = { 0xC1, 0xEA, 0x8F };
        var r = EcuResponse.FromPayload(payload);

        r.IsPositive.Should().BeTrue();
        r.ServiceId.Should().Be(0xC1);
        r.NegativeResponseCode.Should().Be(0x00);
        r.Data.Should().Equal(0xEA, 0x8F); // payload after the response SID
    }

    [Fact]
    public void Negative_response_exposes_request_sid_and_nrc()
    {
        // 0x7F, <request sid>, <nrc>
        byte[] payload = { 0x7F, 0x81, 0x10 };
        var r = EcuResponse.FromPayload(payload);

        r.IsPositive.Should().BeFalse();
        r.ServiceId.Should().Be(0x81);              // the request that was rejected
        r.NegativeResponseCode.Should().Be(0x10);
        r.Data.Should().BeEmpty();
    }

    [Fact]
    public void Empty_payload_throws()
    {
        var act = () => EcuResponse.FromPayload(ReadOnlySpan<byte>.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Malformed_negative_response_throws()
    {
        byte[] payload = { 0x7F, 0x81 }; // missing NRC byte
        var act = () => EcuResponse.FromPayload(payload);
        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter EcuResponseTests`
Expected: FAIL — `EcuResponse` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/OpenEcu.Core/Protocol/EcuResponse.cs`:
```csharp
namespace OpenEcu.Core.Protocol;

/// <summary>A normalized ECU response: either positive (with data) or negative (with an NRC).</summary>
public sealed class EcuResponse
{
    private EcuResponse(bool isPositive, byte serviceId, byte nrc, byte[] data)
    {
        IsPositive = isPositive;
        ServiceId = serviceId;
        NegativeResponseCode = nrc;
        Data = data;
    }

    /// <summary>True for a positive response, false for a negative (0x7F) response.</summary>
    public bool IsPositive { get; }

    /// <summary>
    /// For a positive response, the response SID (request SID + 0x40).
    /// For a negative response, the rejected request's SID.
    /// </summary>
    public byte ServiceId { get; }

    /// <summary>Negative response code; 0 for a positive response.</summary>
    public byte NegativeResponseCode { get; }

    /// <summary>Positive-response data after the response SID; empty for a negative response.</summary>
    public IReadOnlyList<byte> Data { get; }

    public static EcuResponse FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
            throw new ArgumentException("Response payload is empty.", nameof(payload));

        if (payload[0] == KwpServiceId.NegativeResponse)
        {
            if (payload.Length < 3)
                throw new ArgumentException("Negative response must be 0x7F, SID, NRC.", nameof(payload));
            return new EcuResponse(isPositive: false, serviceId: payload[1], nrc: payload[2], data: Array.Empty<byte>());
        }

        return new EcuResponse(isPositive: true, serviceId: payload[0], nrc: 0x00, data: payload[1..].ToArray());
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter EcuResponseTests`
Expected: PASS (4 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Protocol/EcuResponse.cs tests/OpenEcu.Core.Tests/Protocol/EcuResponseTests.cs
git commit -m "feat: normalized EcuResponse (positive/negative)"
```

---

### Task 3: IncompleteFrameException + KLineFrameReader

**Files:**
- Create: `src/OpenEcu.Core/Protocol/IncompleteFrameException.cs`
- Create: `src/OpenEcu.Core/Protocol/KLineFrameReader.cs`
- Test: `tests/OpenEcu.Core.Tests/Protocol/KLineFrameReaderTests.cs`

The reader assembles exactly one frame from a byte stream by reading the header, computing the length (from the format byte's low 6 bits in ISO mode, or the explicit length byte in KWP mode), then reading the remaining payload + checksum.

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Protocol/KLineFrameReaderTests.cs`:
```csharp
using FluentAssertions;
using OpenEcu.Core.Protocol;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class KLineFrameReaderTests
{
    [Fact]
    public async Task Reads_one_complete_kwp_frame()
    {
        // Fabricate a valid KWP frame carrying payload C1 EA 8F.
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0xC1, 0xEA, 0x8F }, KLineMode.Kwp2000);
        var transport = new SimulatedTransport();
        transport.EnqueueResponse(frame);
        await transport.OpenAsync();

        byte[] read = await KLineFrameReader.ReadFrameAsync(transport, KLineMode.Kwp2000);
        read.Should().Equal(frame);
    }

    [Fact]
    public async Task Reads_one_complete_iso_frame()
    {
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0x41, 0x00, 0x01, 0x02 }, KLineMode.Iso9141);
        var transport = new SimulatedTransport();
        transport.EnqueueResponse(frame);
        await transport.OpenAsync();

        byte[] read = await KLineFrameReader.ReadFrameAsync(transport, KLineMode.Iso9141);
        read.Should().Equal(frame);
    }

    [Fact]
    public async Task Reads_only_the_first_frame_when_two_are_queued()
    {
        byte[] first = KLineFrameBuilder.BuildRequest(new byte[] { 0x7E }, KLineMode.Kwp2000);
        byte[] second = KLineFrameBuilder.BuildRequest(new byte[] { 0xC2 }, KLineMode.Kwp2000);
        var transport = new SimulatedTransport();
        transport.EnqueueResponse(first);
        transport.EnqueueResponse(second);
        await transport.OpenAsync();

        byte[] read = await KLineFrameReader.ReadFrameAsync(transport, KLineMode.Kwp2000);
        read.Should().Equal(first);
    }

    [Fact]
    public async Task Throws_when_stream_ends_mid_frame()
    {
        // Header claims 3 payload bytes but only 1 is provided.
        var transport = new SimulatedTransport();
        transport.EnqueueResponse(new byte[] { 0x80, 0xF5, 0xD5, 0x03, 0xC1 });
        await transport.OpenAsync();

        var act = async () => await KLineFrameReader.ReadFrameAsync(transport, KLineMode.Kwp2000);
        await act.Should().ThrowAsync<IncompleteFrameException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter KLineFrameReaderTests`
Expected: FAIL — `KLineFrameReader` / `IncompleteFrameException` do not exist.

- [ ] **Step 3: Write the exception**

Create `src/OpenEcu.Core/Protocol/IncompleteFrameException.cs`:
```csharp
namespace OpenEcu.Core.Protocol;

/// <summary>Thrown when the transport stream ends before a complete frame was read.</summary>
public sealed class IncompleteFrameException : Exception
{
    public IncompleteFrameException(string message) : base(message) { }
}
```

- [ ] **Step 4: Write the reader**

Create `src/OpenEcu.Core/Protocol/KLineFrameReader.cs`:
```csharp
using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Protocol;

/// <summary>Reads exactly one complete K-line frame from a transport, using its length field.</summary>
public static class KLineFrameReader
{
    public static async Task<byte[]> ReadFrameAsync(
        IEcuTransport transport, KLineMode mode, CancellationToken ct = default)
    {
        int headerLen = mode == KLineMode.Kwp2000 ? 4 : 3;
        byte[] header = await ReadExactAsync(transport, headerLen, ct);

        int payloadLen = mode == KLineMode.Kwp2000 ? header[3] : (header[0] & 0x3F);

        byte[] rest = await ReadExactAsync(transport, payloadLen + 1, ct); // payload + checksum

        byte[] frame = new byte[headerLen + payloadLen + 1];
        header.CopyTo(frame.AsSpan(0));
        rest.CopyTo(frame.AsSpan(headerLen));
        return frame;
    }

    private static async Task<byte[]> ReadExactAsync(IEcuTransport transport, int count, CancellationToken ct)
    {
        byte[] buffer = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n = await transport.ReadAsync(buffer.AsMemory(got, count - got), ct);
            if (n == 0)
                throw new IncompleteFrameException($"Stream ended after {got} of {count} expected bytes.");
            got += n;
        }
        return buffer;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter KLineFrameReaderTests`
Expected: PASS (4 passed).

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Protocol/IncompleteFrameException.cs src/OpenEcu.Core/Protocol/KLineFrameReader.cs tests/OpenEcu.Core.Tests/Protocol/KLineFrameReaderTests.cs
git commit -m "feat: K-line frame reader (length-aware stream assembly)"
```

---

### Task 4: IEcuAdapter + EcuConnectionException

**Files:**
- Create: `src/OpenEcu.Core/Adapters/IEcuAdapter.cs`
- Create: `src/OpenEcu.Core/Adapters/EcuConnectionException.cs`

Interface + exception only; behavior is tested via `KLineProtocol` in Task 5.

- [ ] **Step 1: Create the interface**

Create `src/OpenEcu.Core/Adapters/IEcuAdapter.cs`:
```csharp
using OpenEcu.Core.Protocol;

namespace OpenEcu.Core.Adapters;

/// <summary>
/// Tier-2 adapter: a request/response conversation with an ECU over some transport.
/// Implementations: KLineProtocol (dumb cable) and, later, Elm327Adapter (smart adapters).
/// </summary>
public interface IEcuAdapter : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>Performs the protocol handshake (e.g. StartCommunication). Throws on failure.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Cleanly ends the session (e.g. StopCommunication).</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>Sends a service request payload and returns the ECU's response.</summary>
    Task<EcuResponse> RequestAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default);

    /// <summary>Sends a TesterPresent keep-alive.</summary>
    Task TesterPresentAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Create the exception**

Create `src/OpenEcu.Core/Adapters/EcuConnectionException.cs`:
```csharp
namespace OpenEcu.Core.Adapters;

/// <summary>Thrown when the ECU handshake does not complete successfully.</summary>
public sealed class EcuConnectionException : Exception
{
    public EcuConnectionException(string message) : base(message) { }
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/OpenEcu.Core/Adapters/IEcuAdapter.cs src/OpenEcu.Core/Adapters/EcuConnectionException.cs
git commit -m "feat: IEcuAdapter abstraction + EcuConnectionException"
```

---

### Task 5: KLineProtocol — RequestAsync

**Files:**
- Create: `src/OpenEcu.Core/Adapters/KLineProtocol.cs`
- Test: `tests/OpenEcu.Core.Tests/Adapters/KLineProtocolTests.cs`

Implement the adapter incrementally. This task does the constructor + `RequestAsync` (build → write → read → parse → normalize). Connect/disconnect/testerpresent come in Task 6. The class is created here with `NotImplementedException` stubs for the Task 6 members so it compiles.

- [ ] **Step 1: Write the failing test**

Create `tests/OpenEcu.Core.Tests/Adapters/KLineProtocolTests.cs`:
```csharp
using System.IO;
using FluentAssertions;
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Protocol;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Adapters;

public class KLineProtocolTests
{
    private const KLineMode Mode = KLineMode.Kwp2000;

    private static (KLineProtocol adapter, SimulatedTransport transport) NewAdapter()
    {
        var transport = new SimulatedTransport();
        transport.OpenAsync().GetAwaiter().GetResult();
        return (new KLineProtocol(transport, Mode), transport);
    }

    [Fact]
    public async Task RequestAsync_writes_framed_request_and_returns_positive_response()
    {
        var (adapter, transport) = NewAdapter();
        // Script a positive response carrying SID 0x61 + data.
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0x61, 0xAB }, Mode));

        EcuResponse response = await adapter.RequestAsync(new byte[] { 0x21, 0x80 });

        transport.Written.Should().Equal(KLineFrameBuilder.BuildRequest(new byte[] { 0x21, 0x80 }, Mode));
        response.IsPositive.Should().BeTrue();
        response.ServiceId.Should().Be(0x61);
        response.Data.Should().Equal(0xAB);
    }

    [Fact]
    public async Task RequestAsync_returns_negative_response_without_throwing()
    {
        var (adapter, transport) = NewAdapter();
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0x7F, 0x21, 0x11 }, Mode));

        EcuResponse response = await adapter.RequestAsync(new byte[] { 0x21, 0x80 });

        response.IsPositive.Should().BeFalse();
        response.ServiceId.Should().Be(0x21);
        response.NegativeResponseCode.Should().Be(0x11);
    }

    [Fact]
    public async Task RequestAsync_throws_on_bad_checksum()
    {
        var (adapter, transport) = NewAdapter();
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0x61 }, Mode);
        frame[^1] ^= 0xFF; // corrupt the checksum
        transport.EnqueueResponse(frame);

        var act = async () => await adapter.RequestAsync(new byte[] { 0x21, 0x80 });
        await act.Should().ThrowAsync<InvalidDataException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter KLineProtocolTests`
Expected: FAIL — `KLineProtocol` does not exist.

- [ ] **Step 3: Write the implementation (with Task-6 members stubbed)**

Create `src/OpenEcu.Core/Adapters/KLineProtocol.cs`:
```csharp
using System.IO;
using OpenEcu.Core.Protocol;
using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Adapters;

/// <summary>
/// Tier-2 adapter for the "dumb" K-line cable: the host performs the ISO9141/KWP2000
/// message exchange itself over a raw byte-stream transport.
/// </summary>
public sealed class KLineProtocol : IEcuAdapter
{
    private readonly IEcuTransport _transport;
    private readonly KLineMode _mode;

    public KLineProtocol(IEcuTransport transport, KLineMode mode)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _mode = mode;
    }

    public bool IsConnected { get; private set; }

    public async Task<EcuResponse> RequestAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        byte[] request = KLineFrameBuilder.BuildRequest(payload.Span, _mode);
        await _transport.WriteAsync(request, ct);

        byte[] responseFrame = await KLineFrameReader.ReadFrameAsync(_transport, _mode, ct);
        return ParseResponse(responseFrame);
    }

    // NOTE: TryParse has a ReadOnlySpan<byte> out param, which cannot live in an async
    // method under C# 12 (net8.0 default). Keep it in a synchronous helper.
    private EcuResponse ParseResponse(byte[] responseFrame)
    {
        if (!KLineFrameParser.TryParse(responseFrame, _mode, out var responsePayload))
            throw new InvalidDataException("ECU response failed checksum validation.");
        return EcuResponse.FromPayload(responsePayload);
    }

    public Task ConnectAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task DisconnectAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task TesterPresentAsync(CancellationToken ct = default) => throw new NotImplementedException();

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter KLineProtocolTests`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/OpenEcu.Core/Adapters/KLineProtocol.cs tests/OpenEcu.Core.Tests/Adapters/KLineProtocolTests.cs
git commit -m "feat: KLineProtocol.RequestAsync (build/write/read/parse)"
```

---

### Task 6: KLineProtocol — Connect / Disconnect / TesterPresent

**Files:**
- Modify: `src/OpenEcu.Core/Adapters/KLineProtocol.cs` (replace the three stubbed methods)
- Modify: `tests/OpenEcu.Core.Tests/Adapters/KLineProtocolTests.cs` (append tests)

- [ ] **Step 1: Write the failing tests (append to the existing test class)**

Add these methods inside the `KLineProtocolTests` class in `tests/OpenEcu.Core.Tests/Adapters/KLineProtocolTests.cs`, before the closing brace:
```csharp
    [Fact]
    public async Task ConnectAsync_sends_StartCommunication_and_sets_connected()
    {
        var (adapter, transport) = NewAdapter();
        // Positive StartCommunication response: 0xC1 + two key bytes.
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0xC1, 0xEA, 0x8F }, Mode));

        await adapter.ConnectAsync();

        adapter.IsConnected.Should().BeTrue();
        transport.Written.Should().Equal(KLineFrameBuilder.BuildRequest(new byte[] { 0x81 }, Mode));
    }

    [Fact]
    public async Task ConnectAsync_throws_and_stays_disconnected_on_negative_response()
    {
        var (adapter, transport) = NewAdapter();
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0x7F, 0x81, 0x10 }, Mode));

        var act = async () => await adapter.ConnectAsync();

        await act.Should().ThrowAsync<EcuConnectionException>();
        adapter.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task DisconnectAsync_sends_StopCommunication_and_clears_connected()
    {
        var (adapter, transport) = NewAdapter();
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0xC1, 0xEA, 0x8F }, Mode));
        await adapter.ConnectAsync();

        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0xC2 }, Mode));
        await adapter.DisconnectAsync();

        adapter.IsConnected.Should().BeFalse();
        // Second write (index after the connect frame) is the StopCommunication request.
        var stopFrame = KLineFrameBuilder.BuildRequest(new byte[] { 0x82 }, Mode);
        transport.Written.Skip(KLineFrameBuilder.BuildRequest(new byte[] { 0x81 }, Mode).Length)
                  .Should().Equal(stopFrame);
    }

    [Fact]
    public async Task TesterPresentAsync_sends_3E_request()
    {
        var (adapter, transport) = NewAdapter();
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0x7E }, Mode));

        await adapter.TesterPresentAsync();

        transport.Written.Should().Equal(KLineFrameBuilder.BuildRequest(new byte[] { 0x3E }, Mode));
    }
```

Add `using System.Linq;` to the top of the test file if not already present (the `.Skip` call needs it).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter KLineProtocolTests`
Expected: FAIL — the three methods throw `NotImplementedException`.

- [ ] **Step 3: Replace the stubbed methods**

In `src/OpenEcu.Core/Adapters/KLineProtocol.cs`, replace these three lines:
```csharp
    public Task ConnectAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task DisconnectAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task TesterPresentAsync(CancellationToken ct = default) => throw new NotImplementedException();
```
with:
```csharp
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        EcuResponse response = await RequestAsync(new byte[] { KwpServiceId.StartCommunication }, ct);
        if (!response.IsPositive ||
            response.ServiceId != KwpServiceId.PositiveResponseFor(KwpServiceId.StartCommunication))
        {
            throw new EcuConnectionException(
                $"StartCommunication failed (positive={response.IsPositive}, sid=0x{response.ServiceId:X2}).");
        }
        IsConnected = true;
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        try
        {
            await RequestAsync(new byte[] { KwpServiceId.StopCommunication }, ct);
        }
        finally
        {
            IsConnected = false;
        }
    }

    public async Task TesterPresentAsync(CancellationToken ct = default)
    {
        await RequestAsync(new byte[] { KwpServiceId.TesterPresent }, ct);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter KLineProtocolTests`
Expected: PASS (7 passed — 3 from Task 5 + 4 new).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS — all plan 1 + plan 2 tests green (16 from plan 1 + 15 from plan 2 = 31 passed).

- [ ] **Step 6: Commit**

```bash
git add src/OpenEcu.Core/Adapters/KLineProtocol.cs tests/OpenEcu.Core.Tests/Adapters/KLineProtocolTests.cs
git commit -m "feat: KLineProtocol connect/disconnect/testerpresent handshake"
```

---

## Self-Review

**Spec coverage (this plan's slice of design §6–§7):**
- Tier-2 `IEcuAdapter` abstraction → Task 4 ✅
- `KLineProtocol` (dumb-cable adapter: framing + request/response) → Tasks 5–6 ✅
- KWP2000 connect handshake (StartCommunication + key-byte response) → Task 6 ✅
- TesterPresent keep-alive → Task 6 ✅
- Negative-response handling → Tasks 2, 5 ✅
- Length-aware stream framing → Task 3 ✅
- **Deliberately deferred (noted in scope):** physical init/wake timing + echo stripping (plan 3, FTDI transport); `Elm327Adapter` (transport plan); SecurityAccess 0x27 + writing/flashing (later phase); sensor/DTC decoding (diagnostics plan); real `EcuDefinition`/`SagemMc1000Definition` wiring.

**Placeholder scan:** No TBD/TODO. The `NotImplementedException` stubs in Task 5 are explicitly and fully replaced in Task 6 Step 3 — not left dangling.

**Type consistency:** `EcuResponse` members (`IsPositive`, `ServiceId`, `NegativeResponseCode`, `Data`, `FromPayload`); `KwpServiceId` members (`StartCommunication`, `StopCommunication`, `TesterPresent`, `NegativeResponse`, `PositiveResponseOffset`, `PositiveResponseFor`); `IEcuAdapter` members (`IsConnected`, `ConnectAsync`, `DisconnectAsync`, `RequestAsync`, `TesterPresentAsync`); `KLineFrameReader.ReadFrameAsync`; and plan 1's `KLineFrameBuilder.BuildRequest`/`KLineFrameParser.TryParse`/`KLineMode`/`SimulatedTransport` (`EnqueueResponse`, `Written`, `OpenAsync`) are referenced consistently across all tasks.
