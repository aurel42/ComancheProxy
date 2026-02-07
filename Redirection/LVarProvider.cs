namespace ComancheProxy.Redirection;

/// <summary>
/// Provides high-fidelity aircraft variable values.
/// </summary>
public interface ILVarProvider
{
    /// <summary>
    /// Gets the current value of an L-Var.
    /// </summary>
    /// <param name="name">L-Var name (e.g., "L:Eng1_RPM").</param>
    /// <returns>The current value.</returns>
    double GetValue(string name);
    
    /// <summary>
    /// Checks if a variable is available.
    /// </summary>
    bool Contains(string name);
}

/// <summary>
/// Mock provider for testing without a live MSFS/WASM connection.
/// </summary>
public sealed class MockLVarProvider : ILVarProvider
{
    private readonly Random _rng = new();
    
    public double GetValue(string name) => name switch
    {
        "L:Eng1_RPM" => 2400 + _rng.NextDouble() * 10,
        "L:AirspeedIndicated" => 120 + _rng.NextDouble(),
        "L:ApMaster" => 1.0,
        _ => 0.0
    };

    public bool Contains(string name) => name.StartsWith("L:");
}
