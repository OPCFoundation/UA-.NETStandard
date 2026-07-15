# OPC Foundation UA .NET Standard Minimal Client

## Introduction

The minimal console client demonstrates a clean, lightweight OPC UA client implementation using
the modern fluent API and dependency injection. It is designed as an educational reference and
starting point for building OPC UA client applications.

## Key Features

- **Fluent API**: Uses the modern `IManagedSessionFactory` for simplified session management
- **Dependency Injection**: Leverages Microsoft.Extensions.DependencyInjection for service composition
- **Service-Provided Telemetry**: Uses the DI-integrated `ITelemetryContext` for logging, metrics, and activity tracking
- **Console Logging**: Integrated console logging for visibility into client operations
- **Native AOT Compatible**: Supports ahead-of-time compilation with .NET

## Getting Started

### Prerequisites

- .NET 10.0 SDK or later
- An OPC UA server accessible at the specified endpoint

### Running the Client

To connect to the default MinimalBoilerServer:

```bash
dotnet run
```

To connect to a different server:

```bash
dotnet run "opc.tcp://localhost:62542/MinimalCalcServer"
```

## Example Usage

The minimal client demonstrates the following operations:

1. **Session Connection**: Establishes a managed session using the DI-resolved session factory
2. **Browsing**: Browses the server's address space (ObjectsFolder)
3. **Reading**: Reads the server's current time from the StandardServer
4. **Clean Shutdown**: Properly closes the session and service provider

## Architecture

### Program.cs

The application demonstrates:

- Creating an `ApplicationConfiguration` with security settings
- Setting up a `ServiceCollection` with OPC UA services via `AddOpcUa()` and `AddClient()`
- Building a `ServiceProvider` and resolving `IManagedSessionFactory`
- Connecting to a server with `sessionFactory.ConnectAsync(endpoint)`
- Performing basic OPC UA operations (Browse, Read)
- Proper resource cleanup with `IDisposable` session and `DisposeAsync()` on the service provider

## Educational Value

This minimal client serves as:

- A clean reference implementation for building OPC UA clients with dependency injection
- A learning resource for understanding the modern OPC UA client stack and DI patterns
- A starting template for custom client applications

## For More Information

- See [Sessions.md](../../Docs/Sessions.md) for detailed session and subscription documentation
- See [DependencyInjection.md](../../Docs/DependencyInjection.md) for DI patterns and service composition
- See [Minimal Servers](../MinimalBoilerServer) and [Minimal Calc Server](../MinimalCalcServer) for server examples

