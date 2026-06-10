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

// 4) Probe the gate. The original sends SecurityAccess (0x27) FIRST, before any data
//    request, so try that first and capture whether the ECU returns a seed (67 03 02 ...).
var probes = new (string Name, byte[] Payload)[]
{
    ("SecurityAccess request seed (27 03 02)", new byte[] { 0x27, 0x03, 0x02 }),
    ("TesterPresent (3E)",                     new byte[] { 0x3E }),
    ("Mode 01 PID 00 (control)",               new byte[] { 0x01, 0x00 }),
};

foreach (var (name, payload) in probes)
{
    byte[] frame = KLineFrameBuilder.BuildRequest(payload, KLineMode.Iso9141);
    Console.WriteLine($"\n== {name}: TX {Hex(frame)} ==");
    await sp.WriteAsync(frame);
    await Capture("   RX (may include a TX echo first):", 800);
}

Console.WriteLine("\nDone. Copy ALL output above and send it back.");

static string Hex(ReadOnlySpan<byte> data)
{
    var sb = new System.Text.StringBuilder();
    foreach (byte b in data) sb.Append(b.ToString("X2")).Append(' ');
    return sb.ToString().TrimEnd();
}

// Adapts a serial port's SetBreak to the Core IBreakLine abstraction.
file sealed class BreakLineAdapter(ISerialPort port) : IBreakLine
{
    public void SetBreak(bool on) => port.SetBreak(on);
}
