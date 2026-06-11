using OpenEcu.Core.Obd;
using OpenEcu.Core.Transport;
using OpenEcu.Transport.Serial;

namespace OpenEcu.App.Services;

/// <summary>A live connection: the data service plus the logging transport (open via Log).</summary>
public sealed record LiveConnection(LiveDataService Service, LoggingTransport Log);

public enum AdapterKind { Cable, Elm327 }

public interface IConnectionFactory
{
    LiveConnection Create(string portName, AdapterKind kind = AdapterKind.Cable);
}

/// <summary>Builds either a K-line (FTDI cable) or an ELM327 session for the chosen adapter.</summary>
public sealed class ConnectionFactory : IConnectionFactory
{
    public LiveConnection Create(string portName, AdapterKind kind = AdapterKind.Cable)
    {
        if (kind == AdapterKind.Elm327)
        {
            var btPort = new SystemSerialPort(portName, baudRate: 115200, readTimeoutMs: 2000, writeTimeoutMs: 1000);
            var btSerial = new SerialPortTransport(btPort);
            var btLog = new LoggingTransport(btSerial);
            return new LiveConnection(new LiveDataService(new Elm327ObdSession(btLog)), btLog);
        }

        var port = new SystemSerialPort(portName, baudRate: 10400, readTimeoutMs: 300, writeTimeoutMs: 1000);
        var serial = new SerialPortTransport(port);
        var log = new LoggingTransport(serial);
        return new LiveConnection(new LiveDataService(new KLineObdSession(log, serial)), log);
    }
}
