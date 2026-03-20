using System.Collections.Concurrent;
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

var serviceProvider = services.BuildServiceProvider();

var logger = serviceProvider.GetRequiredService<ProxyLogger>();
var resolvedConfig = serviceProvider.GetRequiredService<ProxyConfig>();

int cls2SimPort = resolvedConfig.CLS2SimPort;
const int simPort = 500;
const string simAddress = "127.0.0.1";
int fsePort = resolvedConfig.FsePort;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    cts.Cancel();
};

logger.LogInfo("ComancheProxy v0.1.10");

// Start CLS2Sim listener
var cls2SimListener = new TcpListener(IPAddress.Loopback, cls2SimPort);
cls2SimListener.Start();
logger.LogStarted($"CLS2Sim on 127.0.0.1:{cls2SimPort}");

// Start FSEconomy listener if configured
TcpListener? fseListener = null;
if (fsePort > 0)
{
    fseListener = new TcpListener(IPAddress.Loopback, fsePort);
    fseListener.Start();
    logger.LogStarted($"FSEconomy on 127.0.0.1:{fsePort}");
}

var activeBridges = new ConcurrentDictionary<int, Task>();
int bridgeIdCounter = 0;

try
{
    var acceptTasks = new List<Task>
    {
        AcceptLoopAsync(cls2SimListener, ClientProfile.CLS2Sim, "CLS2Sim", cts.Token)
    };

    if (fseListener != null)
    {
        acceptTasks.Add(AcceptLoopAsync(fseListener, ClientProfile.FSEconomy, "FSEconomy", cts.Token));
    }

    await Task.WhenAll(acceptTasks);
}
catch (OperationCanceledException)
{
    // Shutting down
}
finally
{
    cls2SimListener.Stop();
    fseListener?.Stop();

    // Wait for active bridges to finish
    try { await Task.WhenAll(activeBridges.Values); }
    catch { /* bridges already logging their own errors */ }

    logger.LogConnectionLost("Proxy listeners stopped.");
}

async Task AcceptLoopAsync(TcpListener listener, ClientProfile profile, string profileName, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        logger.LogWaiting($"Waiting for {profileName} connection...");
        TcpClient clientSocket;
        try
        {
            clientSocket = await listener.AcceptTcpClientAsync(ct);
        }
        catch (OperationCanceledException) { break; }

        logger.LogClientConnected($"{profileName}: {clientSocket.Client.RemoteEndPoint?.ToString() ?? "Unknown"}");

        int bridgeId = Interlocked.Increment(ref bridgeIdCounter);
        var bridgeTask = Task.Run(async () =>
        {
            using (clientSocket)
            {
                try
                {
                    using var simSocket = new TcpClient();
                    await simSocket.ConnectAsync(simAddress, simPort, ct);

                    // Per-connection state instances
                    var stateTracker = new StateTracker(logger);
                    var sidecarInjector = new SidecarInjector();
                    var transformationEngine = new TransformationEngine(sidecarInjector, stateTracker);

                    var bridge = new ProxyBridge(clientSocket, simSocket, logger, stateTracker,
                        transformationEngine, sidecarInjector, resolvedConfig, profile);
                    await bridge.RunAsync(ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    logger.LogError($"Failed to bridge {profileName} to MSFS: {ex.Message}");
                }
                finally
                {
                    activeBridges.TryRemove(bridgeId, out _);
                }
            }
        }, ct);

        activeBridges.TryAdd(bridgeId, bridgeTask);
    }
}
