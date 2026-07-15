# OPC Foundation UA .NET Standard Minimal Client

## Introduction

The minimal console client demonstrates a clean, lightweight OPC UA client implementation using
the modern fluent API and dependency injection with HostApplicationBuilder. It is designed as an
educational reference and starting point for building OPC UA client applications.

## Key Features

- **Fluent DI API**: Uses `HostApplicationBuilder` with fluent `.AddOpcUa()`, `.AddClient()`, `ConfigureApplication(...)`, and `.AddAlarms()` configuration
- **Managed Sessions**: Leverages the modern `IManagedSessionFactory` for simplified session management
- **Dependency Injection**: Fully integrated Microsoft.Extensions.DependencyInjection with host-based lifetime management
- **Service-Provided Telemetry**: Uses the DI-integrated `ITelemetryContext` for logging, metrics, and activity tracking
- **Alarms & Conditions**: Includes A&C client support via `.AddAlarms()`
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

1. **Host Setup**: Creates an application host with `Host.CreateApplicationBuilder(args)`
2. **DI Configuration**: Configures OPC UA client services with fluent API (`.AddOpcUa().AddClient(...).AddAlarms()`) and `ConfigureApplication(...)`
3. **Session Connection**: Connects via `IManagedSessionFactory.ConnectAsync(endpoint)`
4. **Browsing**: Browses the server's address space (ObjectsFolder)
5. **Reading**: Reads the server's current time from the StandardServer
6. **Clean Shutdown**: Properly closes the session and host

## Architecture

### Program.cs

The application uses top-level statements and demonstrates:

- Setting up `HostApplicationBuilder` with OPC UA client services
- Building the `ApplicationConfiguration` via `OpcUaClientOptions.ConfigureApplication(...)`
- Configuring services via fluent API: `.AddOpcUa().AddClient(...).AddAlarms()`
- Resolving `IManagedSessionFactory` from the DI container
- Connecting to a server with `sessionFactory.ConnectAsync(endpoint)`
- Performing basic OPC UA operations (Browse, Read)
- Proper resource cleanup with session `DisposeAsync()` and host disposal

### DI Container Integration

The client follows the patterns described in [DependencyInjection.md](../../Docs/DependencyInjection.md):

- **HostApplicationBuilder**: Central entry point for DI and host setup
- **Options Pattern**: `OpcUaClientOptions` with deferred `ApplicationConfiguration` creation
- **Factory Pattern**: `IManagedSessionFactory` for runtime endpoint selection
- **Fluent API**: Chainable `.AddXxx()` methods on `IOpcUaBuilder`

## Educational Value

This minimal client serves as:

- A clean reference implementation for building OPC UA clients with dependency injection and HostApplicationBuilder
- A learning resource for understanding the modern OPC UA client stack and fluent DI patterns
- A template matching the architecture of MinimalBoilerServer for consistency
- A starting point for custom client applications

## For More Information

- See [Sessions.md](../../Docs/Sessions.md) for detailed session and subscription documentation
- See [DependencyInjection.md](../../Docs/DependencyInjection.md) for DI patterns and service composition
- See [MinimalBoilerServer](../MinimalBoilerServer) for server-side HostApplicationBuilder patterns
- See [IdentityProviders.md](../../Docs/IdentityProviders.md) for authentication and authorization patterns
