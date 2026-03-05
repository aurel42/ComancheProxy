using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;
using System.Text.Json;
using ComancheProxy.Models;

namespace ComancheDiag;

// Concrete enums for SimConnect SDK compliance
public enum DEFINITIONS { Main = 1 }
public enum REQUESTS { Main = 1 }

class Program
{
    const int WM_USER_SIMCONNECT = 0x0402;

    static async Task Main(string[] args)
    {
        Console.WriteLine("Comanche Diagnostic Tool (Official SDK)");
        
        using var serviceProvider = new ServiceCollection()
            .AddLogging(builder => builder.AddConsole())
            .BuildServiceProvider();

        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var config = LoadConfig();
        
        if (config == null)
        {
            Console.WriteLine("Error: Could not load ../config.json");
            return;
        }

        SimConnect? simconnect = null;
        var cts = new CancellationTokenSource();
        
        try
        {
            // hWnd is null, UserEvent is 0 since we poll via ReceiveMessage
            simconnect = new SimConnect("ComancheDiag", IntPtr.Zero, 0, null, 0);
            
            Console.WriteLine("Connected to SimConnect via Official SDK.");

            var stateVars = new HashSet<string> { "AUTOPILOT MASTER", "ELEVATOR TRIM PCT", "AILERON TRIM PCT", "RUDDER TRIM PCT" };
            var variablesToMonitor = new List<string>(stateVars);
            
            foreach (var profile in config.AircraftProfiles)
            {
                if (profile.Features.Autopilot != null && profile.Features.Autopilot.Enabled)
                {
                    foreach (var lvar in profile.Features.Autopilot.SourceLVars)
                        if (!variablesToMonitor.Contains(lvar)) variablesToMonitor.Add(lvar);
                }
                if (profile.Features.PropellerThrust != null && profile.Features.PropellerThrust.Enabled)
                {
                    var lvar = profile.Features.PropellerThrust.SourceLVar;
                    if (!string.IsNullOrEmpty(lvar) && !variablesToMonitor.Contains(lvar)) variablesToMonitor.Add(lvar);
                }
            }

            // We use one definition/request pair per variable to avoid complex struct marshaling
            for (int i = 0; i < variablesToMonitor.Count; i++)
            {
                var varName = variablesToMonitor[i];
                var defId = (DEFINITIONS)(i + 1);
                var reqId = (REQUESTS)(i + 1);

                // Using "Number" instead of "percent" to avoid the 0..1 clamping
                // and allow the full signed range (-1 to 1) expected by the user.
                string units = "Number";
                if (varName == "AUTOPILOT MASTER") units = "Bool";

                simconnect.AddToDataDefinition(defId, varName, units, SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                // In managed SDK, we register the definition as returning a single double
                simconnect.RegisterDataDefineStruct<double>(defId);
            }

            var lastValues = new Dictionary<string, double>();
            var lastReportTimes = new Dictionary<string, DateTime>();

            simconnect.OnRecvSimobjectData += (SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data) =>
            {
                int index = (int)data.dwRequestID - 1;
                if (index >= 0 && index < variablesToMonitor.Count)
                {
                    string name = variablesToMonitor[index];
                    double val = (double)data.dwData[0];
                    DateTime now = DateTime.Now;

                    if (name == "ELEVATOR TRIM PCT")
                    {
                        if (!lastReportTimes.TryGetValue(name, out DateTime lastTime) || (now - lastTime).TotalSeconds >= 5)
                        {
                            Console.WriteLine($"{now:HH:mm:ss} [TRIM] {name,-25} = {val:F4}");
                            lastReportTimes[name] = now;
                        }
                    }
                    else
                    {
                        if (!lastValues.TryGetValue(name, out double oldVal) || Math.Abs(oldVal - val) > 0.001)
                        {
                            string prefix = name.StartsWith("L:") ? "[LV]" : "[ST]";
                            Console.WriteLine($"{now:HH:mm:ss} {prefix}   {name,-25} = {val:F4}");
                            lastValues[name] = val;
                        }
                    }
                }
            };

            // Start requests
            for (int i = 0; i < variablesToMonitor.Count; i++)
            {
                var defId = (DEFINITIONS)(i + 1);
                var reqId = (REQUESTS)(i + 1);
                simconnect.RequestDataOnSimObject(reqId, defId, 0, SIMCONNECT_PERIOD.VISUAL_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }

            Console.WriteLine($"Monitoring {variablesToMonitor.Count} variables. Press Ctrl+C to exit.");

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    simconnect.ReceiveMessage();
                }
                catch (COMException ex) when ((uint)ex.ErrorCode == 0xC000013C)
                {
                    Console.WriteLine("Simulator disconnected.");
                    break;
                }
                await Task.Delay(16);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical Error: {ex.Message}");
            if (ex.InnerException != null) Console.WriteLine($"Inner: {ex.InnerException.Message}");
        }
        finally
        {
            simconnect?.Dispose();
        }
    }

    static ProxyConfig? LoadConfig()
    {
        try
        {
            string path = "../config.json";
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<ProxyConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch { }
        return null;
    }
}
