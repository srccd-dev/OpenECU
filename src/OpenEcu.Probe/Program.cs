using OpenEcu.Core.Adapters;
using OpenEcu.Core.Memory;
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

if (mode == "readmem")
{
    int addr = args.Length > 2 ? Convert.ToInt32(args[2], 16) : 0x000000;
    int len  = args.Length > 3 ? int.Parse(args[3]) : 64;
    var logging = new LoggingTransport(transport);
    logging.BytesWritten += b => Console.WriteLine($"  TX {BitConverter.ToString(b)}");
    logging.BytesRead    += b => Console.WriteLine($"  RX {BitConverter.ToString(b)}");
    await using var sagem = new SagemSession(logging, transport);
    try
    {
        Console.WriteLine("Connecting (5-baud init + keyword handshake)...");
        await sagem.ConnectAsync();
        Console.WriteLine("Unlocking (SecurityAccess 27 03 02)...");
        await sagem.UnlockAsync(SecurityLevel.Read);
        Console.WriteLine("StartDiagnosticSession (31 90 11) [informational, non-fatal]...");
        try
        {
            ObdResponse diag = await sagem.StartDiagnosticAsync();
            Console.WriteLine($"  start-diag reply: SID 0x{diag.ServiceId:X2} [{BitConverter.ToString(diag.Payload)}]");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  start-diag no/invalid reply ({ex.GetType().Name}) — continuing straight to read (matches decompiled flow).");
        }
        Console.WriteLine($"Reading {len} bytes @ 0x{addr:X6} (ReadMemoryByAddress 0x23)...");
        MemoryImage image = await sagem.ReadMemoryAsync(addr, len);
        Console.WriteLine($"\n  {BitConverter.ToString(image.Slice(addr, len).ToArray())}");
        Console.WriteLine("\n*** READ OK ***");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"readmem error: {ex.GetType().Name}: {ex.Message}");
    }
    return;
}

if (mode == "initd5")
{
    var logging = new LoggingTransport(transport);
    logging.BytesWritten += b => Console.WriteLine($"  TX {BitConverter.ToString(b)}");
    logging.BytesRead    += b => Console.WriteLine($"  RX {BitConverter.ToString(b)}");
    await using var sagem = new SagemSession(logging, transport);
    try
    {
        Console.WriteLine("Connecting (0x33 OBD) + unlocking (SecurityAccess)...");
        await sagem.ConnectAsync();
        await sagem.UnlockAsync(SecurityLevel.Read);
        Console.WriteLine("Unlocked. Re-initializing the K-line at 0xD5 (5-baud slow init, ~2.2s)...");
        var initr = new KLineFiveBaudInitializer();
        await initr.InitializeAsync(transport, 0xD5);
        Console.WriteLine("0xD5 init complete. Capturing response bytes (keywords reveal the session header)...");
        var buf = new byte[1];
        int idle = 0;
        while (idle < 10)
        {
            int n = await logging.ReadAsync(buf);
            if (n == 0) { idle++; await Task.Delay(40); } else { idle = 0; }
        }
        Console.WriteLine("\n*** 0xD5 capture done ***");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"initd5 error: {ex.GetType().Name}: {ex.Message}");
    }
    return;
}

if (mode == "readd5")
{
    int addr = args.Length > 2 ? Convert.ToInt32(args[2], 16) : 0x000000;
    int len  = args.Length > 3 ? int.Parse(args[3]) : 32;
    var logging = new LoggingTransport(transport);
    logging.BytesWritten += b => Console.WriteLine($"  TX {BitConverter.ToString(b)}");
    logging.BytesRead    += b => Console.WriteLine($"  RX {BitConverter.ToString(b)}");
    await using var sagem = new SagemSession(logging, transport);

    async Task<int> ReadByte() { var b = new byte[1]; int n = await logging.ReadAsync(b); return n > 0 ? b[0] : -1; }

    try
    {
        Console.WriteLine("Connect 0x33 + unlock...");
        await sagem.ConnectAsync();
        await sagem.UnlockAsync(SecurityLevel.Read);
        Console.WriteLine("Re-init at 0xD5...");
        await new KLineFiveBaudInitializer().InitializeAsync(transport, 0xD5);

        int sync = -1;
        for (int i = 0; i < 48 && sync != 0x55; i++) sync = await ReadByte();
        int kw1 = await ReadByte();
        int kw2 = await ReadByte();
        Console.WriteLine($"  sync=0x{sync:X2} kw1=0x{kw1:X2} kw2=0x{kw2:X2}");

        await Task.Delay(30); // W4
        int invKw2 = kw2 ^ 0xFF;
        await logging.WriteAsync(new[] { (byte)invKw2 });
        int echo = await ReadByte();
        int invAddr = await ReadByte();
        Console.WriteLine($"  sent ~kw2=0x{invKw2:X2}, echo=0x{echo:X2}, ~addr=0x{invAddr:X2} (expect 0x2A)");

        byte[] payload = { 0x23, (byte)(addr >> 16), (byte)(addr >> 8), (byte)addr, (byte)len, 0x00 };
        var frame = new byte[3 + payload.Length + 1];
        frame[0] = (byte)(0x80 + payload.Length); // KWP=false: length folded into the format byte
        frame[1] = 0xD5;
        frame[2] = 0xF5;
        Array.Copy(payload, 0, frame, 3, payload.Length);
        int sum = 0; for (int i = 0; i < frame.Length - 1; i++) sum += frame[i];
        frame[^1] = (byte)sum;

        Console.WriteLine($"Read {len} bytes @ 0x{addr:X6} over D5/F5 frame {BitConverter.ToString(frame)}...");
        foreach (byte tx in frame) // echo-locked send
        {
            await logging.WriteAsync(new[] { tx });
            int e = await ReadByte();
            if (e != tx) Console.WriteLine($"  (echo mismatch: sent 0x{tx:X2} got 0x{e:X2})");
        }
        Console.WriteLine("Response:");
        int idle = 0;
        while (idle < 12) { int b = await ReadByte(); if (b < 0) { idle++; await Task.Delay(40); } else idle = 0; }
        Console.WriteLine("\n*** readd5 done ***");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"readd5 error: {ex.GetType().Name}: {ex.Message}");
    }
    return;
}

if (mode == "tuned5")
{
    int addr = args.Length > 2 ? Convert.ToInt32(args[2], 16) : 0x000000;
    int len  = args.Length > 3 ? int.Parse(args[3]) : 32;
    var logging = new LoggingTransport(transport);
    logging.BytesWritten += b => Console.WriteLine($"  TX {BitConverter.ToString(b)}");
    logging.BytesRead    += b => Console.WriteLine($"  RX {BitConverter.ToString(b)}");
    await using var sagem = new SagemSession(logging, transport);

    async Task<int> RB() { var b = new byte[1]; int n = await logging.ReadAsync(b); return n > 0 ? b[0] : -1; }

    // Send a payload over the D5/F5 KWP frame (echo-locked), return the response payload (header+checksum stripped).
    async Task<byte[]> SendD5(byte[] payload)
    {
        var frame = new byte[3 + payload.Length + 1];
        frame[0] = (byte)(0x80 + payload.Length);
        frame[1] = 0xD5; frame[2] = 0xF5;
        Array.Copy(payload, 0, frame, 3, payload.Length);
        int s = 0; for (int i = 0; i < frame.Length - 1; i++) s += frame[i]; frame[^1] = (byte)s;
        foreach (byte tx in frame) { await logging.WriteAsync(new[] { tx }); await RB(); }
        var resp = new List<byte>();
        int idle = 0; while (idle < 6) { int b = await RB(); if (b < 0) { idle++; } else { resp.Add((byte)b); idle = 0; } }
        return resp.Count >= 5 ? resp.GetRange(3, resp.Count - 4).ToArray() : resp.ToArray();
    }

    try
    {
        Console.WriteLine("Connect 0x33 + unlock (OBD)...");
        await sagem.ConnectAsync();
        await sagem.UnlockAsync(SecurityLevel.Read);
        Console.WriteLine("Re-init 0xD5 + handshake...");
        await new KLineFiveBaudInitializer().InitializeAsync(transport, 0xD5);
        int sync = -1; for (int i = 0; i < 48 && sync != 0x55; i++) sync = await RB();
        int kw1 = await RB(), kw2 = await RB();
        Console.WriteLine($"  KW 0x{kw1:X2} 0x{kw2:X2}");
        await Task.Delay(30);
        await logging.WriteAsync(new[] { (byte)(kw2 ^ 0xFF) });
        await RB(); int invAddr = await RB();
        Console.WriteLine($"  ~addr=0x{invAddr:X2}");

        Console.WriteLine("0xD5 seed request (27 05)...");
        byte[] sresp = await SendD5(new byte[] { 0x27, 0x05 });
        Console.WriteLine($"  seed resp: {BitConverter.ToString(sresp)}");
        if (sresp.Length >= 3 && sresp[0] == 0x67)
        {
            int seed = (sresp[^2] << 8) | sresp[^1]; // seed = trailing 2 bytes of the response payload
            int key = (seed * 0xFA52) & 0xFFFF;
            Console.WriteLine($"  seed=0x{seed:X4} -> key (x0xFA52)=0x{key:X4}; submitting key (27 06, single attempt)...");
            byte[] kresp = await SendD5(new byte[] { 0x27, 0x06, (byte)(key >> 8), (byte)key });
            Console.WriteLine($"  key resp: {BitConverter.ToString(kresp)}");
            if (kresp.Length >= 1 && kresp[0] == 0x67)
            {
                Console.WriteLine($"  *** 0xD5 UNLOCKED *** reading {len} bytes @ 0x{addr:X6}...");
                byte[] rresp = await SendD5(new byte[] { 0x23, (byte)(addr >> 16), (byte)(addr >> 8), (byte)addr, (byte)len, 0x00 });
                Console.WriteLine($"  READ RESP: {BitConverter.ToString(rresp)}");
            }
        }
        Console.WriteLine("\n*** tuned5 done ***");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"tuned5 error: {ex.GetType().Name}: {ex.Message}");
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
