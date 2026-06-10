using OpenEcu.Core.Obd;
using OpenEcu.Transport.Serial;

// Usage: dotnet run --project src/OpenEcu.Probe -- [COMx]
string portName = args.Length > 0 ? args[0] : "COM8";

Console.WriteLine($"OpenECU probe — port={portName}");
Console.WriteLine("Bike must be powered (ignition on / battery tender), cable connected.\n");

// ReadTimeout 300 ms comfortably covers the ~200 ms post-init sync wait and the response idle gap.
await using var port = new SystemSerialPort(portName, baudRate: 10400, readTimeoutMs: 300, writeTimeoutMs: 1000);
try { port.Open(); }
catch (Exception ex) { Console.WriteLine($"Could not open {portName}: {ex.GetType().Name}: {ex.Message}"); return; }

var transport = new SerialPortTransport(port);
var session = new KLineObdSession(transport, transport); // same object is transport + break line

try
{
    Console.WriteLine("Connecting (5-baud init + keyword handshake)...");
    await session.ConnectAsync();
    Console.WriteLine("Connected.\n");

    var supported = await session.ReadSupportedPidsAsync();
    Console.WriteLine($"Supported PIDs: {string.Join(" ", supported.Select(p => p.ToString("X2")))}\n");

    foreach (byte pid in supported)
    {
        if (pid == 0x20 || pid == 0x40) continue; // bitmask chain PIDs
        try
        {
            PidReading r = await session.ReadPidAsync(pid);
            string value = r.Value is null ? $"[{string.Join(" ", r.Raw.Select(b => b.ToString("X2")))}]"
                                            : $"{r.Value:0.##} {r.Unit}";
            Console.WriteLine($"  PID {pid:X2}  {r.Name,-26} {value}");
        }
        catch (Exception ex) { Console.WriteLine($"  PID {pid:X2}  read failed: {ex.Message}"); }
    }

    var dtcs = await session.ReadDtcsAsync();
    Console.WriteLine($"\nStored DTCs: {(dtcs.Count == 0 ? "none" : string.Join(", ", dtcs))}");
}
catch (Exception ex)
{
    Console.WriteLine($"Session error: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine("\nDone.");
