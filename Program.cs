using System.Net;
using System.Net.Sockets;
using ComancheProxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Setup Dependency Injection and Logging
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
var serviceProvider = services.BuildServiceProvider();

var rawLogger = serviceProvider.GetRequiredService<ILogger<ProxyBridge>>();
var logger = new ProxyLogger(rawLogger);
var stateTracker = new StateTracker(logger);

// Redirection Components
var lVarProvider = new ComancheProxy.Redirection.MockLVarProvider();
var variableMapper = new ComancheProxy.Redirection.VariableMapper();
var transformationEngine = new ComancheProxy.Redirection.TransformationEngine(lVarProvider, variableMapper, logger);

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
            using var simSocket = new TcpClient();
            await simSocket.ConnectAsync(simAddress, simPort, cts.Token);
            
            var bridge = new ProxyBridge(clientSocket, simSocket, logger, stateTracker, transformationEngine);
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
