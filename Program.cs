using System.Net;
using System.Net.Sockets;
using ComancheProxy;
using ComancheProxy.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Setup Dependency Injection and Logging
bool debugMode = args.Contains("--debug");

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.SetMinimumLevel(debugMode ? LogLevel.Debug : LogLevel.Information);

    // Console always shows Information+ (the important stuff)
    builder.AddConsole();
    builder.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>(null, LogLevel.Information);

    if (debugMode)
    {
        // File captures everything at Debug level — relative to working directory
        string logPath = Path.Combine(Directory.GetCurrentDirectory(), "logs", "comanche-proxy.log");
        builder.AddProvider(new FileLoggerProvider(logPath, LogLevel.Debug));
        Console.WriteLine($"Debug log: {logPath}");
    }
});
var serviceProvider = services.BuildServiceProvider();

var rawLogger = serviceProvider.GetRequiredService<ILogger<ProxyBridge>>();
var logger = new ProxyLogger(rawLogger);
var stateTracker = new StateTracker(logger);

// Redirection Components
var sidecarInjector = new ComancheProxy.Redirection.SidecarInjector();
var transformationEngine = new ComancheProxy.Redirection.TransformationEngine(sidecarInjector);

const int listenPort = 5001;
const int simPort = 500;
const string simAddress = "127.0.0.1";

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    cts.Cancel();
};

var listener = new TcpListener(IPAddress.Loopback, listenPort);
listener.Start();
logger.LogInfo("ComancheProxy v0.1.5");
logger.LogStarted($"127.0.0.1:{listenPort}");

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        logger.LogWaiting("Waiting for CLS2Sim connection...");
        using var clientSocket = await listener.AcceptTcpClientAsync(cts.Token);

        logger.LogClientConnected(clientSocket.Client.RemoteEndPoint?.ToString() ?? "Unknown");

        try
        {
            // Reset shared state so reconnections start clean
            stateTracker.Reset();
            sidecarInjector.Reset();

            using var simSocket = new TcpClient();
            await simSocket.ConnectAsync(simAddress, simPort, cts.Token);

            var bridge = new ProxyBridge(clientSocket, simSocket, logger, stateTracker, transformationEngine, sidecarInjector);
            await bridge.RunAsync(cts.Token);
        }


        catch (Exception ex)
        {
            logger.LogError($"Failed to bridge to MSFS: {ex.Message}");
        }
    }
}
catch (OperationCanceledException)
{
    // Shutting down
}
finally
{
    listener.Stop();
    logger.LogConnectionLost("Proxy listener stopped.");
}
