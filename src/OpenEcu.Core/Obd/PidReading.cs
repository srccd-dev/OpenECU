namespace OpenEcu.Core.Obd;

/// <summary>One decoded Mode 01 PID value. Value is null for PIDs we don't decode.</summary>
public sealed record PidReading(byte Pid, string Name, double? Value, string Unit, byte[] Raw);
