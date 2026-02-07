Antigravity C# Coding Rules (.antigravity_rules)
These rules are mandatory for the agent when generating or refactoring code for ComancheProxy.
1. Performance & Latency (The "Critical Path")
Zero-Allocation Buffers: Use Span<byte> and ReadOnlySpan<byte> for all packet parsing logic. Avoid Encoding.ASCII.GetString() on high-frequency loops; use System.Buffers.Text.Utf8Parser where possible.
Avoid Boxing: Use generics with where T : struct for transformation math to prevent heap allocations.
Channels over Queues: Use System.Threading.Channels (Unbounded or Bounded with DropOldest) for passing data between the SimConnect client and the Mock Server.
NativeAOT Compatibility: Do not use heavy reflection or dynamic code generation (Emit). The project will be compiled with PublishAot=true.
2. High-Performance Logging
Source Generators: Use [LoggerMessage] source generators for all high-frequency log calls (Trace/Debug) to avoid string interpolation and allocation when the log level is disabled.
Log Level Check: Always check _logger.IsEnabled(LogLevel.Debug) before performing complex data serialization for logs.
3. Asynchronous Patterns
ValueTask: Prefer ValueTask or ValueTask<T> for high-frequency methods that usually complete synchronously to reduce Task object overhead.
CancellationToken: Every async method must accept and respect a CancellationToken.
No Async Void: Never use async void except for top-level event handlers.
4. SimConnect Specifics
Struct Alignment: All structs mapped to SimConnect data must use [StructLayout(LayoutKind.Sequential, Pack = 1)].
Safe Handles: Use SafeHandle for pipe/socket management to ensure clean resource disposal during reconnection cycles.
5. Modern C# Syntax
File-scoped Namespaces: Use namespace MyProject; to reduce indentation levels.
Primary Constructors: Use primary constructors for dependency injection in services and managers.
Collection Expressions: Use [1, 2, 3] syntax for array/list initialization.
Pattern Matching: Favor switch expressions and property patterns for packet ID identification.
6. Documentation & Units
XML Documentation: Every public member must have <summary> tags.
Units: Variable names for physical quantities MUST include the unit (e.g., airspeedKnots, altitudeFeet, deflectionRadians).
