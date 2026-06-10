using System.Diagnostics;
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Protocol;
using OpenEcu.Transport.Serial;

// Usage: dotnet run --project src/OpenEcu.Probe -- [COMx] [addrHex]
// Defaults: COM8, address 33
string portName = args.Length > 0 ? args[0] : "COM8";
byte address = Convert.ToByte(args.Length > 1 ? args[1] : "33", 16);

Console.WriteLine($"OpenECU probe — port={portName}, init address=0x{address:X2}");
Console.WriteLine("Bike must be powered (ignition on / battery tender), cable connected.\n");

await using var sp = new SystemSerialPort(portName, baudRate: 10400, readTimeoutMs: 200, writeTimeoutMs: 1000);
try { sp.Open(); }
catch (Exception ex) { Console.WriteLine($"Could not open {portName}: {ex.GetType().Name}: {ex.Message}"); return; }

var overall = Stopwatch.StartNew();

// Reads a single byte with a short timeout; returns -1 if none arrived.
async Task<int> ReadByte()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));
    var b = new byte[1];
    try { int n = await sp.ReadAsync(b, cts.Token); return n > 0 ? b[0] : -1; }
    catch (OperationCanceledException) { return -1; }
    catch (TimeoutException) { return -1; }
}

// Raw-captures and logs bytes for a time window.
async Task Capture(string label, int windowMs)
{
    Console.WriteLine(label);
    var sw = Stopwatch.StartNew();
    int total = 0;
    while (sw.ElapsedMilliseconds < windowMs)
    {
        int b = await ReadByte();
        if (b >= 0) { total++; Console.WriteLine($"     [{overall.ElapsedMilliseconds,6} ms] RX {b:X2}"); }
    }
    if (total == 0) Console.WriteLine("     (nothing)");
}

// Sends a frame the way the original does: one byte at a time, waiting for each byte's
// echo before sending the next. On the single-wire K-line that echo round-trip provides
// the inter-byte gap (P4) the ECU needs. Returns false if an echo never came back.
async Task<bool> SendPaced(byte[] frame, int interByteMs)
{
    foreach (byte tx in frame)
    {
        await sp.WriteAsync(new byte[] { tx });
        var sw = Stopwatch.StartNew();
        bool echoed = false;
        while (sw.ElapsedMilliseconds < 150)
        {
            int e = await ReadByte();
            if (e == tx) { echoed = true; break; }
            if (e >= 0) Console.WriteLine($"     (unexpected {e:X2} while pacing {tx:X2})");
        }
        if (!echoed) { Console.WriteLine($"     (no echo for {tx:X2} — aborting frame)"); return false; }
        BusyWaitMs(interByteMs); // explicit inter-byte gap (P4), independent of latency timer
    }
    return true;
}

// Reads a full response: collects bytes until a short idle gap (or the window expires).
async Task<int[]> ReadResponse(int windowMs)
{
    var bytes = new List<int>();
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < windowMs)
    {
        int b = await ReadByte();
        if (b >= 0) bytes.Add(b);
        else if (bytes.Count > 0) break; // got the response, then a gap -> done
    }
    return bytes.ToArray();
}

// Sends an OBD request (echo-locked) and returns the raw response bytes.
async Task<int[]> QueryObd(byte[] payload)
{
    byte[] frame = BuildObd(payload);
    if (!await SendPaced(frame, 0)) return Array.Empty<int>();
    return await ReadResponse(700);
}

// 1) 5-baud init on the break line.
Console.WriteLine($"== 5-baud init at 0x{address:X2} ==");
await new KLineFiveBaudInitializer().InitializeAsync(new BreakLineAdapter(sp), address);

// 2) Read the handshake bytes. The break-toggle at the end of init produces a few
//    line-settling noise bytes (e.g. 00) BEFORE the real sync arrives ~200 ms later, so
//    skip everything until we see the 0x55 sync, then take the next two keyword bytes.
Console.WriteLine("-- reading handshake bytes (skipping init noise, expect 55 08 08):");
var hs = new List<int>();
var hsClock = Stopwatch.StartNew();
while (hsClock.ElapsedMilliseconds < 800)
{
    int b = await ReadByte();
    if (b < 0) continue;
    Console.WriteLine($"     [{overall.ElapsedMilliseconds,6} ms] RX {b:X2}");
    if (hs.Count == 0)
    {
        if (b == 0x55) hs.Add(b); // sync found; ignore any noise before it
    }
    else
    {
        hs.Add(b);
        if (hs.Count >= 3) break; // 55, KW1, KW2
    }
}

// 3) If we got a valid sync + keywords, complete the handshake within the W4 window.
if (hs.Count >= 3 && hs[0] == 0x55)
{
    byte kw2 = (byte)hs[2];
    byte invKw2 = (byte)(kw2 ^ 0xFF);
    await Task.Delay(30); // W4 (25-50 ms)
    Console.WriteLine($"-- TX ~KW2 = {invKw2:X2}");
    await sp.WriteAsync(new byte[] { invKw2 });
    // Raw-log whatever follows: a possible echo of invKw2, then the inverted address (~0x33 = CC).
    await Capture("   (expect [echo?] then invAddr CC):", 400);
}
else
{
    Console.WriteLine($"-- did not get a clean 55 + keywords (got: {string.Join(" ", hs.Select(x => x.ToString("X2")))}). Stopping.");
    return;
}

// 4) Full PID scan. The ECU is in OBD-II mode (68 6A F1 header). Read the supported-PID
//    bitmasks (PID 00/20/40), then query and decode every supported PID, plus DTCs.
var supported = new List<int>();
foreach (byte basePid in new byte[] { 0x00, 0x20, 0x40 })
{
    int[] resp = await QueryObd(new byte[] { 0x01, basePid });
    int[]? bits = ObdData(resp, 0x01, basePid);
    if (bits is null || bits.Length < 4)
    {
        Console.WriteLine($"\nSupported bitmask {basePid:X2}: no valid response — stopping scan.");
        break;
    }
    Console.WriteLine($"\nSupported {basePid + 1:X2}-{basePid + 0x20:X2}: {Hex(bits.Take(4).Select(x => (byte)x).ToArray())}");
    for (int i = 0; i < 32; i++)
        if ((bits[i / 8] & (0x80 >> (i % 8))) != 0) supported.Add(basePid + i + 1);
    if (!supported.Contains(basePid + 0x20)) break; // next 32-PID range not advertised
}

Console.WriteLine($"\n== Reading {supported.Count(p => p != 0x20 && p != 0x40)} live PIDs ==");
foreach (int pid in supported)
{
    if (pid == 0x20 || pid == 0x40) continue; // bitmask PIDs, already used
    int[] resp = await QueryObd(new byte[] { 0x01, (byte)pid });
    int[]? data = ObdData(resp, 0x01, (byte)pid);
    Console.WriteLine(data is null
        ? $"  PID {pid:X2}: no/invalid response [{Hex(resp.Select(x => (byte)x).ToArray())}]"
        : $"  PID {pid:X2} {DecodePid(pid, data)}");
}

int[] dtcResp = await QueryObd(new byte[] { 0x03 });
Console.WriteLine($"\n== Mode 03 stored DTCs: {DecodeDtcs(dtcResp)}");
Console.WriteLine($"   raw [{Hex(dtcResp.Select(x => (byte)x).ToArray())}]");

Console.WriteLine("\nDone. Copy ALL output above and send it back.");

static string Hex(ReadOnlySpan<byte> data)
{
    var sb = new System.Text.StringBuilder();
    foreach (byte b in data) sb.Append(b.ToString("X2")).Append(' ');
    return sb.ToString().TrimEnd();
}

// Builds a standard OBD-II ISO9141-2 request frame: 68 6A F1 <payload> <checksum>.
static byte[] BuildObd(byte[] payload)
{
    var frame = new byte[3 + payload.Length + 1];
    frame[0] = 0x68; // format (functional OBD-II)
    frame[1] = 0x6A; // target = ECU
    frame[2] = 0xF1; // source = tester
    payload.CopyTo(frame, 3);
    int sum = 0;
    for (int i = 0; i < frame.Length - 1; i++) sum += frame[i];
    frame[^1] = (byte)sum;
    return frame;
}

// Strips an OBD Mode-01 response (48 6B D1 <mode+40> <pid> <data...> <cks>) to its data
// bytes, or returns null if it isn't a valid positive response to (mode, pid).
static int[]? ObdData(int[] resp, byte mode, byte pid)
{
    if (resp.Length < 6) return null;
    if (resp[3] != mode + 0x40) return null;
    if (resp[4] != pid) return null;
    return resp[5..^1];
}

// Decodes common OBD-II Mode 01 PIDs into physical values; raw bytes otherwise.
static string DecodePid(int pid, int[] d)
{
    string raw = "[" + string.Join(" ", d.Select(x => x.ToString("X2"))) + "]";
    string? v = pid switch
    {
        0x04 when d.Length >= 1 => $"Engine load   {d[0] * 100 / 255} %",
        0x05 when d.Length >= 1 => $"Coolant       {d[0] - 40} C",
        0x06 when d.Length >= 1 => $"Short fuel trim {(d[0] - 128) * 100 / 128} %",
        0x07 when d.Length >= 1 => $"Long fuel trim  {(d[0] - 128) * 100 / 128} %",
        0x0B when d.Length >= 1 => $"MAP           {d[0]} kPa",
        0x0C when d.Length >= 2 => $"RPM           {(d[0] * 256 + d[1]) / 4}",
        0x0D when d.Length >= 1 => $"Speed         {d[0]} km/h",
        0x0E when d.Length >= 1 => $"Timing adv    {d[0] / 2.0 - 64:0.0} deg",
        0x0F when d.Length >= 1 => $"Intake air    {d[0] - 40} C",
        0x11 when d.Length >= 1 => $"Throttle      {d[0] * 100 / 255} %",
        _ => null
    };
    return v is null ? $"= {raw}" : $"= {v,-22} {raw}";
}

// Decodes a Mode 03 response (48 6B D1 43 <code pairs...> cks) into DTC strings.
static string DecodeDtcs(int[] resp)
{
    if (resp.Length < 5 || resp[3] != 0x43) return "(no/invalid Mode 03 response)";
    var codes = new List<string>();
    for (int i = 4; i + 1 < resp.Length - 1; i += 2)
    {
        int a = resp[i], b = resp[i + 1];
        if (a == 0 && b == 0) continue;
        char system = "PCBU"[(a >> 6) & 3];
        codes.Add($"{system}{(a >> 4) & 3}{(a & 0xF):X}{(b >> 4):X}{(b & 0xF):X}");
    }
    return codes.Count == 0 ? "none" : string.Join(", ", codes);
}

// Precise short delay (Task.Delay has ~15 ms granularity, too coarse for P4 timing).
static void BusyWaitMs(int ms)
{
    if (ms <= 0) return;
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed.TotalMilliseconds < ms) { }
}

// Adapts a serial port's SetBreak to the Core IBreakLine abstraction.
file sealed class BreakLineAdapter(ISerialPort port) : IBreakLine
{
    public void SetBreak(bool on) => port.SetBreak(on);
}
