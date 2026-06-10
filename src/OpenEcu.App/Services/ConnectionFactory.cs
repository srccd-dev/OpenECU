using OpenEcu.Core.Obd;
using OpenEcu.Core.Transport;
using OpenEcu.Transport.Serial;

namespace OpenEcu.App.Services;

/// <summary>A live connection: the data service plus the logging transport (open via Log).</summary>
public sealed record LiveConnection(LiveDataService Service, LoggingTransport Log);

public interface IConnectionFactory
{
    LiveConnection Create(string portName);
}

/// <summary>Wires SystemSerialPort → SerialPortTransport → LoggingTransport → KLineObdSession → LiveDataService.</summary>
public sealed class ConnectionFactory : IConnectionFactory
{
    public LiveConnection Create(string portName)
    {
        var port = new SystemSerialPort(portName, baudRate: 10400, readTimeoutMs: 300, writeTimeoutMs: 1000);
        var serial = new SerialPortTransport(port);   // IEcuTransport + IBreakLine
        var log = new LoggingTransport(serial);        // logs the comms bytes
        var session = new KLineObdSession(log, serial); // transport = logged; break line = serial
        return new LiveConnection(new LiveDataService(session), log);
    }
}
