# OPC Foundation UA .NET Standard Minimal Client

## Introduction

The minimal console client demonstrates a clean, lightweight OPC UA client implementation using the modern fluent API and dependency injection with `HostApplicationBuilder`. It is designed as an educational reference and starting point for building OPC UA client applications.

## Key Features

- **Fluent DI API**: Uses `HostApplicationBuilder` with fluent `.AddOpcUa()`, `.ConfigureApplication()`, `.AddClient()`, `.AddSubscriptions()`, and `.AddAlarms()` configuration
- **Secure by Default**: Discovers a `SignAndEncrypt` / `Basic256Sha256` endpoint unless `--insecure` is explicitly supplied
- **Managed Sessions**: Leverages the modern `IManagedSessionFactory` for simplified session management
- **Fluent Subscriptions**: Creates a V2 subscription and monitored item with `AddSubscription()` and `TryAddMonitoredItem()`
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

The server certificate must already be trusted. For local development only, explicitly opt into automatic trust:

```bash
dotnet run -- --auto-accept
```

To explicitly select an endpoint without message security:

```bash
dotnet run -- --insecure
```

To connect to a different server:

```bash
dotnet run -- "opc.tcp://localhost:62542/MinimalCalcServer"
```

## Example Usage

The minimal client demonstrates the following operations:

1. **Host Setup**: Creates an application host with `Host.CreateApplicationBuilder()`
2. **DI Configuration**: Configures the application and client services with the fluent API
3. **Endpoint Discovery**: Selects the requested secure endpoint through `.AddDiscoveryAndConnect(...)`
4. **Session Connection**: Connects through the DI-provided managed-session factory
5. **Subscriptions**: Monitors `ServerStatus.CurrentTime` with the V2 subscription API
6. **Alarms & Conditions**: Resolves `AlarmClientFactory` and creates an A&C client for the session
7. **Browsing**: Browses the server's address space (`ObjectsFolder`)
8. **Reading**: Reads the server's current time from the StandardServer
9. **Clean Shutdown**: Properly closes the subscription, session, and host

## Architecture

### Program.cs

The application uses top-level statements and demonstrates:

- Configuring application identity and security through `.ConfigureApplication(...)`
- Setting up `HostApplicationBuilder` with OPC UA client services
- Building and validating the `ApplicationConfiguration` inside the DI infrastructure
- Configuring services via fluent API: `.AddOpcUa().ConfigureApplication(...).AddClient(...).AddSubscriptions().AddAlarms()`
- Discovering a secure endpoint and connecting through the DI-provided managed-session delegate
- Creating a V2 subscription and monitored item with the fluent session extensions
- Resolving `AlarmClientFactory` for Part 9 Alarms & Conditions operations
- Performing basic OPC UA operations (Browse, Read)
- Proper resource cleanup for the subscription, session, and host

### DI Container Integration

The client follows the patterns described in [DependencyInjection.md](../../Docs/DependencyInjection.md):

- **HostApplicationBuilder**: Central entry point for DI and host setup
- **Application Options**: `OpcUaApplicationOptions` builds one validated application configuration
- **Client Options**: `OpcUaClientOptions` contains managed-session defaults without requiring explicit configuration construction
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
