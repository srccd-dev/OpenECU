using System.Diagnostics;
using OpenEcu.Core.Adapters;
using OpenEcu.Transport.Serial;

// Usage: dotnet run --project src/OpenEcu.Probe -- [COMx] [addrHex,addrHex,...]
// Defaults: COM8, addresses 33,D5
string port = args.Length > 0 ? args[0] : "COM8";
byte[] addresses = (args.Length > 1 ? args[1] : "33,D5")
    .Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(s => Convert.ToByte(s.Trim(), 16))
    .ToArray();

Console.WriteLine($"OpenECU probe — port={port}, addresses={string.Join(",", addresses.Select(a => $"0x{a:X2}"))}");
Console.WriteLine("Make sure the bike is powered (ignition on / battery tender) and the cable is connected.\n");

foreach (byte address in addresses)
{
    Console.WriteLine($"=== 5-baud init at address 0x{address:X2} ===");
    await using var sp = new SystemSerialPort(port, baudRate: 10400, readTimeoutMs: 300, writeTimeoutMs: 1000);
    try
    {
        sp.Open();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Could not open {port}: {ex.GetType().Name}: {ex.Message}");
        return;
    }

    // Drive the 5-baud init on the break line.
    var initializer = new KLineFiveBaudInitializer();
    IBreakLine line = new BreakLineAdapter(sp);
    var sw = Stopwatch.StartNew();
    await initializer.InitializeAsync(line, address);
    Console.WriteLine($"  init sent in {sw.ElapsedMilliseconds} ms; listening 3s for the ECU...");

    // Capture whatever comes back for ~3 seconds.
    sw.Restart();
    var buffer = new byte[64];
    int total = 0;
    while (sw.ElapsedMilliseconds < 3000)
    {
        int n;
        using var readCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        try { n = await sp.ReadAsync(buffer, readCts.Token); }
        catch (OperationCanceledException) { n = 0; }
        catch (TimeoutException) { n = 0; }
        if (n > 0)
        {
            total += n;
            string hex = string.Join(" ", buffer.Take(n).Select(b => b.ToString("X2")));
            Console.WriteLine($"  [{sw.ElapsedMilliseconds,5} ms] RX {n,2}: {hex}");
        }
    }
    Console.WriteLine(total == 0
        ? "  (no bytes received)\n"
        : $"  total {total} bytes received\n");

    sp.Close();
}

Console.WriteLine("Done. Copy ALL output above and send it back for analysis.");

// Adapts a serial port's SetBreak to the Core IBreakLine abstraction.
file sealed class BreakLineAdapter(ISerialPort port) : IBreakLine
{
    public void SetBreak(bool on) => port.SetBreak(on);
}
