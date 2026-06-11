using OpenEcu.Core.Obd;
using OpenEcu.Core.Security;
using OpenEcu.Core.Transport;
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

string mode = args.Length > 1 ? args[1] : "scan";
if (mode == "securityaccess")
{
    var logging = new LoggingTransport(transport);
    logging.BytesWritten += b => Console.WriteLine($"  TX {BitConverter.ToString(b)}");
    logging.BytesRead    += b => Console.WriteLine($"  RX {BitConverter.ToString(b)}");
    await using var sagem = new SagemSession(logging, transport);
    try
    {
        Console.WriteLine("Connecting (5-baud init + keyword handshake)...");
        await sagem.ConnectAsync();
        Console.WriteLine("Connected. StartDiagnosticSession (31 90 11)...");
        ObdResponse diag = await sagem.StartDiagnosticAsync();
        Console.WriteLine($"  start-diag reply: SID 0x{diag.ServiceId:X2} [{BitConverter.ToString(diag.Payload)}]");
        Console.WriteLine("SecurityAccess: request seed + send computed key (27 03 02)...");
        await sagem.UnlockAsync(SecurityLevel.Read);
        Console.WriteLine("\n*** ACCESS GRANTED — ECU unlocked. ***");
    }
    catch (SecurityAccessException ex)
    {
        Console.WriteLine($"\n*** Unlock rejected — NRC 0x{ex.Nrc:X2}. {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Security-access error: {ex.GetType().Name}: {ex.Message}");
    }
    return;
}

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
