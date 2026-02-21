using Microsoft.Extensions.Logging;

namespace ComancheProxy;

/// <summary>
/// High-performance logger for the proxy.
/// </summary>
public partial class ProxyLogger(ILogger logger)
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Proxy started. Listening on {Endpoint}.")]
    public partial void LogStarted(string endpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for connection... {Context}")]
    public partial void LogWaiting(string context);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client connected: {Endpoint}.")]
    public partial void LogClientConnected(string endpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to MSFS: {Endpoint}.")]
    public partial void LogSimConnected(string endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection lost: {Reason}.")]
    public partial void LogConnectionLost(string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Proxy error: {Error}.")]
    public partial void LogError(string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bridge session started: {Context}")]
    public partial void LogBridgeStarted(string context);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bridge session stopped: {Reason}")]
    public partial void LogBridgeStopped(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Comanche Detect: {Status}")]
    public partial void LogComancheMode(string status);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client -> Sim: ID={Id} ({IdName}), Size={Size}.")]
    public partial void LogClientPacket(uint id, string idName, uint size);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sim -> Client: ID={Id} ({IdName}), Size={Size}.")]
    public partial void LogServerPacket(uint id, string idName, uint size);



    [LoggerMessage(Level = LogLevel.Debug, Message = "Definition Update: ID={Id}, Size={Size}.")]
    public partial void LogDefinitionTrace(uint id, uint size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sidecar: autopilot is {State}")]
    public partial void LogSidecarApState(string state);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sidecar: injected {ByteCount} bytes into MSFS stream")]
    public partial void LogSidecarInjected(int byteCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sidecar: cleaned up subscription")]
    public partial void LogSidecarCleanup();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sidecar values: AP={ApState} RPM={Rpm} Thrust={Thrust}")]
    public partial void LogSidecarValues(string apState, double rpm, double thrust);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Message}")]
    public partial void LogInfo(string message);

    public bool IsEnabled(LogLevel level) => logger.IsEnabled(level);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Message}")]
    public partial void LogStringDebug(string message);
}
