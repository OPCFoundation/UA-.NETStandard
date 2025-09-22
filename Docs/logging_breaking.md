# Breaking Changes - Telemetry Context and ILogger Integration

This document outlines the breaking changes introduced in the telemetry context and ILogger integration feature. These changes enable comprehensive observability and proper structured logging throughout the OPC UA stack.

## Overview

The changes introduce a new `ITelemetryContext` interface that provides access to logging, metrics, and tracing capabilities. Many core classes now require this telemetry context in their constructors or methods, which constitutes breaking changes for applications using the OPC UA .NET Standard library.

## 1. Core Infrastructure Classes - Constructor Changes

### `Opc.Ua.ServiceMessageContext`
**File:** `Stack/Opc.Ua.Core/Types/Utils/ServiceMessageContext.cs`

- **REMOVED:** `public ServiceMessageContext()` - Parameterless constructor
- **ADDED:** `public ServiceMessageContext(ITelemetryContext telemetry)` - Now requires telemetry context
- **ADDED:** `public ServiceMessageContext(IServiceMessageContext context, ITelemetryContext telemetry)` - Copy constructor with telemetry

### `Opc.Ua.EncodeableFactory`
**File:** `Stack/Opc.Ua.Core/Types/Encoders/EncodeableFactory.cs`

- **REMOVED:** `public EncodeableFactory()` - Parameterless constructor  
- **ADDED:** `public EncodeableFactory(ITelemetryContext telemetry)` - Now requires telemetry context
- **ADDED:** `public EncodeableFactory(IEncodeableFactory factory, ITelemetryContext telemetry)` - Copy constructor with telemetry

### `Opc.Ua.ApplicationConfiguration`
**File:** `Stack/Opc.Ua.Core/Schema/ApplicationConfiguration.cs`

- **MODIFIED:** `public ApplicationConfiguration()` - Still exists but behavior may have changed
- **ADDED:** `public ApplicationConfiguration(ITelemetryContext telemetry)` - New constructor with telemetry context

## 2. Interface Changes

### `IServiceMessageContext`
**File:** `Stack/Opc.Ua.Core/Types/Utils/IServiceMessageContext.cs`

- **ADDED:** `ITelemetryContext Telemetry { get; }` - New property that implementations must provide

### `ITransportBindingFactory<T>`
**File:** `Stack/Opc.Ua.Core/Stack/Bindings/ITransportBindings.cs`

- **MODIFIED:** `T Create(ITelemetryContext telemetry)` - Method signature changed to require telemetry parameter

## 3. System Context Classes

### `Opc.Ua.SystemContext`
**File:** `Stack/Opc.Ua.Core/Stack/State/ISystemContext.cs`

- **ADDED:** `public SystemContext(ITelemetryContext telemetry)` - New constructor
- **ADDED:** `public SystemContext(IOperationContext context, ITelemetryContext telemetry)` - New constructor with telemetry

## 4. Certificate and Security Classes

### `Opc.Ua.CertificateValidator`
**File:** `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidator.cs`

- **ADDED:** `public CertificateValidator(ITelemetryContext telemetry)` - Constructor now requires telemetry

### `Opc.Ua.CertificateStoreIdentifier`
**File:** `Stack/Opc.Ua.Core/Security/Certificates/CertificateStoreIdentifier.cs`

- **MODIFIED:** `public static ICertificateStore CreateStore(string storeTypeName, ITelemetryContext telemetry)` - Added telemetry parameter
- **MODIFIED:** `public virtual ICertificateStore OpenStore(ITelemetryContext telemetry)` - Added telemetry parameter

### `Opc.Ua.CertificateIdentifier`
**File:** `Stack/Opc.Ua.Core/Security/Certificates/CertificateIdentifier.cs`

- **MODIFIED:** `public ICertificateStore OpenStore(ITelemetryContext telemetry)` - Added telemetry parameter

### Certificate Store Classes

#### `DirectoryCertificateStore`
**File:** `Stack/Opc.Ua.Core/Security/Certificates/DirectoryCertificateStore.cs`

- **ADDED:** `public DirectoryCertificateStore(ITelemetryContext telemetry)`
- **ADDED:** `public DirectoryCertificateStore(bool noSubDirs, ITelemetryContext telemetry)`

#### `X509CertificateStore`
**File:** `Stack/Opc.Ua.Core/Security/Certificates/X509CertificateStore/X509CertificateStore.cs`

- **ADDED:** `public X509CertificateStore(ITelemetryContext telemetry)`

## 5. Transport Layer Breaking Changes

### Transport Channel Factories

#### `TcpTransportChannel`
**File:** `Stack/Opc.Ua.Core/Stack/Tcp/TcpMessageSocket.cs`

- **ADDED:** `public TcpTransportChannel(ITelemetryContext telemetry)`
- **MODIFIED:** `public ITransportChannel Create(ITelemetryContext telemetry)` - Added telemetry parameter

### Transport Listeners

#### `TcpTransportListener`
**File:** `Stack/Opc.Ua.Core/Stack/Tcp/TcpTransportListener.cs`

- **ADDED:** `public TcpTransportListener(ITelemetryContext telemetry)`

#### `HttpsTransportListener`
**File:** `Stack/Opc.Ua.Bindings.Https/Stack/Https/HttpsTransportListener.cs`

- **ADDED:** `public HttpsTransportListener(string uriScheme, ITelemetryContext telemetry)`

### Transport Bindings

#### `TransportBindings`
**File:** `Stack/Opc.Ua.Core/Stack/Bindings/TransportBindings.cs`

- **MODIFIED:** `public ITransportChannel GetChannel(string uriScheme, ITelemetryContext telemetry)` - Added telemetry parameter
- **MODIFIED:** `public ITransportListener GetListener(string uriScheme, ITelemetryContext telemetry)` - Added telemetry parameter

## 6. Client Library Breaking Changes

### `Opc.Ua.Client.SessionClient`
**File:** `Stack/Opc.Ua.Core/Stack/Client/SessionClient.cs`

- **ADDED:** `public SessionClient(ITransportChannel channel, ITelemetryContext telemetry)` - Constructor requires telemetry

### `Opc.Ua.Client.Subscription`
**File:** `Libraries/Opc.Ua.Client/Subscription/Subscription.cs`

- **ADDED:** `public Subscription(ITelemetryContext telemetry)` - Constructor requires telemetry

### `Opc.Ua.Client.Browser`
**File:** `Libraries/Opc.Ua.Client/Browser.cs`

- **ADDED:** `public Browser(ISession session, ITelemetryContext telemetry)` - Constructor requires telemetry

### `Opc.Ua.Client.ReverseConnectManager`
**File:** `Libraries/Opc.Ua.Client/ReverseConnectManager.cs`

- **ADDED:** `public ReverseConnectManager(ITelemetryContext telemetry)` - Constructor requires telemetry

### Session Factories

#### `DefaultSessionFactory`
**File:** `Libraries/Opc.Ua.Client/Session/Factory/DefaultSessionFactory.cs`

- **ADDED:** `public DefaultSessionFactory(ITelemetryContext telemetry)`

#### `TraceableSessionFactory`
**File:** `Libraries/Opc.Ua.Client/Session/Factory/TraceableSessionFactory.cs`

- **ADDED:** `public TraceableSessionFactory(ITelemetryContext telemetry)`

### `TraceableSession`
**File:** `Libraries/Opc.Ua.Client/Session/TraceableSession.cs`

- **ADDED:** `public TraceableSession(ISession session, ITelemetryContext telemetry)` - Constructor requires telemetry

### `ComplexTypeSystem`
**File:** `Libraries/Opc.Ua.Client.ComplexTypes/ComplexTypeSystem.cs`

- **ADDED:** `public ComplexTypeSystem(ISession session, ITelemetryContext telemetry)` - Constructor requires telemetry

### `SessionReconnectHandler`
**File:** `Libraries/Opc.Ua.Client/Session/SessionReconnectHandler.cs`

- **ADDED:** `public SessionReconnectHandler(ITelemetryContext telemetry, bool reconnectAbort = false, int maxReconnectPeriod = -1)` - Constructor requires telemetry

## 7. Configuration Library Breaking Changes

### `Opc.Ua.Configuration.ApplicationInstance`
**File:** `Libraries/Opc.Ua.Configuration/ApplicationInstance.cs`

- **ADDED:** `public ApplicationInstance(ITelemetryContext telemetry)` - Constructor requires telemetry

### `ConfiguredEndpointCollection`
**File:** `Stack/Opc.Ua.Core/Stack/Configuration/ConfiguredEndpoints.cs`

- **MODIFIED:** `public static ConfiguredEndpointCollection Load(string filePath, ITelemetryContext telemetry)` - Added telemetry parameter
- **MODIFIED:** `public static ConfiguredEndpointCollection Load(Stream istrm, ITelemetryContext telemetry)` - Added telemetry parameter

### `ConfiguredEndpoint`
**File:** `Stack/Opc.Ua.Core/Stack/Configuration/ConfiguredEndpoints.cs`

- **MODIFIED:** `public Task UpdateFromServerAsync(ITelemetryContext telemetry, CancellationToken ct = default)` - Added telemetry parameter

## 8. Server Library Breaking Changes

### Server Subscription Components

#### `DataChangeMonitoredItemQueue`
**File:** `Libraries/Opc.Ua.Server/Subscription/MonitoredItem/Queue/DataChangeMonitoredItemQueue.cs`

- **ADDED:** `public DataChangeMonitoredItemQueue(bool createDurable, uint monitoredItemId, ITelemetryContext telemetry)`

#### `EventMonitoredItemQueue`
**File:** `Libraries/Opc.Ua.Server/Subscription/MonitoredItem/Queue/EventMonitoredItemQueue.cs`

- **ADDED:** `public EventMonitoredItemQueue(bool createDurable, uint monitoredItemId, ITelemetryContext telemetry)`

#### `MonitoredItemQueueFactory`
**File:** `Libraries/Opc.Ua.Server/Subscription/MonitoredItem/Queue/MonitoredItemQueueFactory.cs`

- **ADDED:** `public MonitoredItemQueueFactory(ITelemetryContext telemetry)`

## 9. PubSub Library Breaking Changes

### `UaPubSubApplication`
**File:** `Libraries/Opc.Ua.PubSub/UaPubSubApplication.cs`

- **MODIFIED:** `public static UaPubSubApplication Create(IUaPubSubDataStore dataStore, ITelemetryContext telemetry)` - Added telemetry parameter

### `UaPubSubConfigurator`
**File:** `Libraries/Opc.Ua.PubSub/Configuration/UaPubSubConfigurator.cs`

- **ADDED:** `public UaPubSubConfigurator(ITelemetryContext telemetry)` - Constructor requires telemetry

### Transport Components

#### `UdpClientBroadcast`
**File:** `Libraries/Opc.Ua.PubSub/Transport/UdpClientBroadcast.cs`

- **ADDED:** `public UdpClientBroadcast(IPAddress address, int port, UsedInContext pubSubContext, ITelemetryContext telemetry)`

#### `UdpClientUnicast`
**File:** `Libraries/Opc.Ua.PubSub/Transport/UdpClientUnicast.cs`

- **ADDED:** `public UdpClientUnicast(IPAddress localAddress, int port, ITelemetryContext telemetry)`

## 10. Method Signature Changes

### `UserIdentityToken`
**File:** `Stack/Opc.Ua.Core/Stack/Types/UserIdentityToken.cs`

- **MODIFIED:** `public X509Certificate2 GetOrCreateCertificate(ITelemetryContext telemetry)` - Added telemetry parameter

### `ServerSecurityPolicy`
**File:** `Stack/Opc.Ua.Core/Schema/ApplicationConfiguration.cs`

- **MODIFIED:** `public static byte CalculateSecurityLevel(MessageSecurityMode mode, string policyUri, ILogger logger)` - Added ILogger parameter

## 11. Deprecated Static Properties

### Static Global Context Properties (Now Obsolete)

#### `ServiceMessageContext`
**File:** `Stack/Opc.Ua.Core/Types/Utils/ServiceMessageContext.cs`

- **`ServiceMessageContext.GlobalContext`** - Marked `[Obsolete]`
- **`ServiceMessageContext.ThreadContext`** - Marked `[Obsolete]`  

#### `EncodeableFactory`
**File:** `Stack/Opc.Ua.Core/Types/Encoders/EncodeableFactory.cs`

- **`EncodeableFactory.GlobalFactory`** - Marked `[Obsolete]`

## 12. Utility Method Changes

### Multiple `Utils` class methods now marked `[Obsolete]`
**File:** `Stack/Opc.Ua.Core/Types/Utils/UtilsObsolete.cs`

All trace/logging methods marked obsolete with guidance to use `ITelemetryContext` and `ILogger` instead.

## Migration Guide

### High Impact Breaking Changes

1. **Core infrastructure classes** (`ServiceMessageContext`, `EncodeableFactory`) no longer have parameterless constructors
2. **Transport binding factories** require telemetry context in `Create()` methods  
3. **Certificate operations** require telemetry context parameters
4. **Client session and subscription classes** require telemetry in constructors

### Required Migration Steps

1. **Provide ITelemetryContext instances** when creating core OPC UA objects
2. **Update factory patterns** to pass telemetry context parameters
3. **Replace static global context properties** with proper scoped contexts
4. **Update certificate store operations** to include telemetry parameters
5. **Modify transport layer initialization** to provide telemetry context

### Example Migration

**Before:**
```csharp
var messageContext = new ServiceMessageContext();
var factory = new EncodeableFactory();