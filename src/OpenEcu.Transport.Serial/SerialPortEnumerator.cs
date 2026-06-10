using System.IO.Ports;

namespace OpenEcu.Transport.Serial;

/// <summary>Lists serial ports available on this machine.</summary>
public static class SerialPortEnumerator
{
    /// <summary>Returns the names of available serial ports (e.g. "COM8", "/dev/ttyUSB0").</summary>
    public static string[] GetPortNames() => SerialPort.GetPortNames();
}
