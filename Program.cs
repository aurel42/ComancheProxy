using System.Net;
using System.Net.Sockets;
using ComancheProxy;
using ComancheProxy.Logging;
using ComancheProxy.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using ComancheProxy.Redirection;

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

// Load Configuration
string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
ProxyConfig config;
try
{
    string json = File.ReadAllText(configPath);
    config = JsonSerializer.Deserialize<ProxyConfig>(json, new JsonSerializerOptions 
    { 
        PropertyNameCaseInsensitive = true 
    }) ?? new ProxyConfig();
}
catch (Exception ex)
{
    Console.WriteLine($"CRITICAL: Failed to load config.json: {ex.Message}");
    return;
}
services.AddSingleton(config);
services.AddSingleton<ProxyLogger>(sp => 
{
    var raw = sp.GetRequiredService<ILogger<ProxyBridge>>();
    return new ProxyLogger(raw);
});
services.AddSingleton<StateTracker>();
services.AddSingleton<SidecarInjector>();
services.AddSingleton<TransformationEngine>();

var serviceProvider = services.BuildServiceProvider();

// Resolve components from DI
var logger = serviceProvider.GetRequiredService<ProxyLogger>();
var stateTracker = serviceProvider.GetRequiredService<StateTracker>();
var sidecarInjector = serviceProvider.GetRequiredService<SidecarInjector>();
var transformationEngine = serviceProvider.GetRequiredService<TransformationEngine>();
var resolvedConfig = serviceProvider.GetRequiredService<ProxyConfig>();

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
logger.LogInfo("ComancheProxy v0.1.6");
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

            var bridge = new ProxyBridge(clientSocket, simSocket, logger, stateTracker, transformationEngine, sidecarInjector, resolvedConfig);
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
