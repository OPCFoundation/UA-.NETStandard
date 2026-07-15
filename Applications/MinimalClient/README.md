# OPC Foundation UA .NET Standard Minimal Client

## Introduction

The minimal console client demonstrates a clean, lightweight OPC UA client implementation using
the modern fluent API and dependency injection. It is designed as an educational reference and
starting point for building OPC UA client applications.

## Key Features

- **Fluent API**: Uses the modern `ManagedSessionBuilder` fluent API for simplified session management
- **Automatic Reconnection**: Configured with built-in reconnection policy
- **Dependency Injection**: Leverages Microsoft.Extensions.Hosting and DependencyInjection
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
dotnet run --endpoint "opc.tcp://localhost:62542/MinimalCalcServer"
```

## Example Usage

The minimal client demonstrates the following operations:

1. **Endpoint Discovery**: Discovers available endpoints on the OPC UA server
2. **Session Connection**: Establishes a managed session with automatic reconnection
3. **Browsing**: Browses the server's address space (RootFolder and ObjectsFolder)
4. **Reading**: Reads the server's current time from the StandardServer
5. **Clean Shutdown**: Properly closes the session

## Architecture

### Program.cs

The application demonstrates:

- Creating an `ApplicationConfiguration` with security settings
- Using `ManagedSessionBuilder` fluent API to configure and connect to a server
- Performing basic OPC UA operations (Browse, Read)
- Proper resource cleanup with `IDisposable` session

### Key Classes

- **ConsoleLogger**: Implements `ITelemetryContext` for logging integration
- **Program**: Main entry point using System.CommandLine for argument parsing

## Educational Value

This minimal client serves as:

- A clean reference implementation for building OPC UA clients
- A learning resource for understanding the modern OPC UA client stack
- A starting template for custom client applications

## For More Information

- See [Sessions.md](../../Docs/Sessions.md) for detailed session and subscription documentation
- See [DependencyInjection.md](../../Docs/DependencyInjection.md) for DI patterns
- See [Minimal Servers](../MinimalBoilerServer) and [Minimal Calc Server](../MinimalCalcServer) for server examples
