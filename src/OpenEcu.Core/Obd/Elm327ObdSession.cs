using System.Text;
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Obd;

/// <summary>
/// An OBD session over an ELM327-class adapter (e.g. OBDLink LX). Sends the AT command set
/// and OBD-mode hex requests over a serial/Bluetooth transport and decodes the ASCII replies.
/// </summary>
public sealed class Elm327ObdSession : IObdSession
{
    private readonly IEcuTransport _transport;

    public Elm327ObdSession(IEcuTransport transport) =>
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await CommandAsync("ATZ", ct);   // reset
        await CommandAsync("ATE0", ct);  // echo off
        await CommandAsync("ATL0", ct);  // no linefeeds
        await CommandAsync("ATS0", ct);  // no spaces
        await CommandAsync("ATH0", ct);  // headers off
        await CommandAsync("ATSP0", ct); // auto protocol

        string r = await CommandAsync("0100", ct);
        if (!Elm327Response.TryParse(r, out _))
            throw new EcuConnectionException($"ELM327 got no OBD response to 0100: '{r.Trim()}'.");
        IsConnected = true;
    }

    public async Task<IReadOnlyList<byte>> ReadSupportedPidsAsync(CancellationToken ct = default)
    {
        var all = new List<byte>();
        foreach (byte basePid in new byte[] { 0x00, 0x20, 0x40 })
        {
            string raw = await CommandAsync($"01{basePid:X2}", ct);
            if (!Elm327Response.TryParse(raw, out byte[] b) || b.Length < 6 || b[0] != 0x41)
                break;
            IReadOnlyList<byte> pids = SupportedPids.Parse(basePid, b.AsSpan(2, 4));
            all.AddRange(pids);
            if (!pids.Contains((byte)(basePid + 0x20)))
                break;
        }
        return all;
    }

    public async Task<PidReading> ReadPidAsync(byte pid, CancellationToken ct = default)
    {
        string raw = await CommandAsync($"01{pid:X2}", ct);
        if (!Elm327Response.TryParse(raw, out byte[] b) || b.Length < 2 || b[0] != 0x41 || b[1] != pid)
            return new PidReading(pid, $"PID {pid:X2}", null, "", Array.Empty<byte>());
        return PidDecoder.Decode(pid, b.AsSpan(2));
    }

    public async Task<IReadOnlyList<string>> ReadDtcsAsync(CancellationToken ct = default)
    {
        string raw = await CommandAsync("03", ct);
        if (!Elm327Response.TryParse(raw, out byte[] b) || b.Length < 1 || b[0] != 0x43)
            return Array.Empty<string>();
        return DtcDecoder.Decode(b.AsSpan(1));
    }

    public async Task ClearDtcsAsync(CancellationToken ct = default) => await CommandAsync("04", ct);

    public ValueTask DisposeAsync() => _transport.DisposeAsync();

    private async Task<string> CommandAsync(string command, CancellationToken ct)
    {
        await _transport.WriteAsync(Encoding.ASCII.GetBytes(command + "\r"), ct);
        return await ReadUntilPromptAsync(ct);
    }

    private async Task<string> ReadUntilPromptAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new byte[1];
        while (true)
        {
            int n = await _transport.ReadAsync(buffer, ct);
            if (n == 0) break; // idle/timeout
            char ch = (char)buffer[0];
            if (ch == '>') return sb.ToString();
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
