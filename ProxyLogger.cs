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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Packet intercepted: ID={Id}, Size={Size}.")]
    public partial void LogPacketTrace(uint id, uint size);
}
