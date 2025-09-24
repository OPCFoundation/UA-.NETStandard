# Observability model of the OPC UA .NET Standard Stack

## Context: Historic approach of logging inside the UA .NET and UA .NET Standard stack

### 1.4.367 and earlier

The codebase up until version 1.4.367 used static tracing to support logging. The `Utils.Trace` function was used to log
messages in the codebase with message format strings and arguments as format parameters, along with optional
`TraceMasks` and `Exceptions`. The logging system was configured via the `ApplicationConfiguration` section for
`TraceConfiguration` to specify the `OutputFilePath` and `TraceMasks`.

### Changes introduced in 1.4.368

In 1.4.368 we introduced a global `ILogger` interface which is defined in `Microsoft.Extensions.Logging.Abstractions`
as core abstraction for logging functions. To avoid change of API signatures in the codebase the logger in `Opc.Ua.Core`
remained a singleton used by all depending libraries.

By default, a backward compatibility `TraceEventLogger` was initialized to support the existing `TraceEvent` callback.
An application can override the `ILogger` with its own implementation by calling the new `Opc.Ua.Utils.SetLogger`
method. Once the logger strings are converted to semantic logging, the trace event callback gets the semantic version
only, which may cause a format exception if directly passed to `String.Format`. Typically logging frameworks handle the
semantic logging strings, but if not it is recommended to switch to the new `ILogger` based interface. The `TraceEvent`
interface will be marked as deprecated after a transistion period.

In order to efficiently support the existing logging all the `LoggerExtensions` functions are integrated in
`Opc.Ua.Utils` as static functions instead of the Logging extension functions. `Utils.Trace` is replaced by
`Utils.LogXXX`, where `XXX` corresponds to the log level. The supported log levels are: `Critical`, `Error`, `Warning`,
`Information`, `Debug` and `Trace`. This approach also allows to call the `EventSource` logger if enabled.

This logging approach does not support structured logging and does not fit well with the overall `ILoggerProvider`
model. `Activities` or `Scopes` are also not supported. The `EventId` parameter is used to plumb through trace masks
which means it cannot be used to replace EventSource.  The approach also does not allow support for source generated
logging, but worse, it requires a single logging context in the same process, prohibiting you from running server
and clients with different logging mechanism.

## Telemetry context design introduced in 1.5.378

The new and more flexible observability design introduced in 1.5.378 deprecates and in part removes the previous
model with the intend of supporting dependency injection, structured logging and OpenTelemetry based [observability
(metrics, traces and logs)](https://learn.microsoft.com/dotnet/core/diagnostics/). `ITelemetryContext`
standardizes how logging, tracing and metrics are accessed, removes prior limitations of a singleton logger,
and aligns the stack with current .NET and OpenTelemetry practices. The logger design follows common
[guidance for library authors](https://learn.microsoft.com/dotnet/core/extensions/logging-library-authors).

The `ITelemetryContext` interface provides access to the `ILoggerFactory`, `Activity` sources and acts as a `Meter`
factory (the latter two are used to create OpenTelemetry compliant traces and metrics).

### `ITelemetryContext` overview

```csharp
public interface ITelemetryContext
{
    // Creates a new Meter for recording metrics (caller disposes).
    Meter CreateMeter();

    // Factory used to create typed ILogger instances.
    ILoggerFactory LoggerFactory { get; }

    // Shared ActivitySource representing the current assembly/component.
    ActivitySource ActivitySource { get; }
}
```

This single abstraction covers logs, traces and metrics. External dependencies (`ILoggerFactory`, `ActivitySource`,
`Meter`) are localized. This promotes dependency injection by enabling passing one context instead of many
singletons.

Lifetime semantics are explicit: callers dispose meters they create while the context owns the long‑lived
activity source. Multiple telemetry context can be hosted in the same process, e.g. running a server and a client
or multiple servers with different logging configurations in a container.

The `ActivitySource` is a context scoped instance representing the current assembly or component and long lived.

A `Meter` is created when a component needs instruments. It is owned by the caller and must be disposed when no
longer needed.

The `LoggerFactory` is long lived and allows creation of typed `ILogger` instances which are maintained by the
caller. [Documentation can be found here](https://learn.microsoft.com/dotnet/core/extensions/logging).

In addition there are several extension methods to simplify usage of telemetry context:

```csharp
public static class TelemetryExtensions
{
    // Creates a logger for the specified category.
    ILogger CreateLogger(this ITelemetryContext context, string categoryName);
    ILogger<T> CreateLogger<T>(this ITelemetryContext context);
    // Starts a new Activity with the shared ActivitySource.
    Activity StartActivity(this ITelemetryContext context, string activityName, ActivityKind kind = ActivityKind.Internal);
}
```

Always use these extension methods! They ensure that null telemetry contexts produce a backwards compatible trace
logger in release builds, and a debug check enabled logger in debug builds. This allows gradual adoption and
verification of the new telemetry model.

### High speed logging and instruments

For high speed logging we are using source generated logging and instruments throughout the code and code will be
gradually enabled to use source generated logging and instruments, enabling us to turn roslyn analzers on to warn
if new logging is added without source generation.

### Obtaining a telemetry context

Telemetry context should be passed (only) via constructors. This enables dependency injection and testability
and the creation of `readonly` logger and meter fields. It also aligns with the lifecycle of the owning class, e.g.
`Dispose` calls disposing the obtained meter.

In addition to being passed as part of a class/service's constructor, existing code can obtain a `ITelemetryContext`
from `IServiceMessageContext.Telemetry`, `ISystemContext.Telemetry` or `IServerInternal.Telemetry`. This allows
gradual adoption but also provides telemetry context to frequently used capabilities, such as encoders, session
users, or filtering.  On the server side, `ISystemContext` is passed to many methods, allowing access to telemetry.
On the client side, IServiceMessageContext is available from `ISession` as a property.

When deriving from `NodeState` and the derived class requires an `ITelemetryContext`, the subclass can override the
`void Initialize(ITelemetryContext)` method and store the context in a private field. Ensure to call the base class
so that it also receives the telemetry context. The Initialize method is called after creation of the NodeState
instance and before any other method is called.

To create a telemetry context you can use the `public ITelemetryContext DefaultTelemetryContext.Create()` static
method.  Do not create new telemetry contexts in cases where you have no access to one but try to wire it from
an area in code where you have to the place you need it.  Best to initialize the telemetry context at the root
of your application, e.g. at time you create a `new ApplicationInstance(...)`.  See [migrating](#migrating)
for more information.

### Using the telemetry context

Code should always use the `ITelemetryContext` extension methods to create loggers, meters and activities.
This ensures proper fallback to tracing if `ITelemetryContext` is null or returns null for any of its properties.
A caller can be assured that the returned logger, meter or activity is never null.

Example usage:

```csharp
// Obtain telemetry context
ITelemetryContext telemetry = xxxxx.Telemetry;

// Logging
var logger = telemetry.CreateLogger("Sample");
// or
var logger = telemetry.CreateLogger<MyClass>();

logger.LogInformation("Connecting to {Endpoint}", endpointUrl);

// Tracing
using var activity = telemetry.StartActivity("ConnectSession");
// Perform OPC UA operation ...

// Metrics
using var meter = telemetry.CreateMeter();
var connectCounter = meter.CreateCounter<long>("my.app.connects");
connectCounter.Add(1);
```

The `ConsoleReferenceClient` and the `ConsoleReferenceServer` have been modified to show how to use ILogger and
ITelemetryContext.

When a logger or meter is created it should always be stored it in a readonly field inside the class inside the
constructor.

Logger instances are cached inside the `ILoggerProvider` Logger provider, therefore obtaining one is relatively
cheap. However, the cost of the size of the reference should be considered when creating loggers for large number
of objects such as NodeState or NodeId instances.

In most cases consider the following refactoring patterns:

- Only pass an ILogger instance via constructor to objects that need it, e.g. NodeState derived classes can
  choose to create a ILogger but do not have to.
- It is better to create a logger in the class using it and handling "Loggable" events by throwing exceptions
  from the "model" object.  Example: parsing a NodeId could throw when it fails with the caller deciding to convert
  the exception to a null node id and logging or throwing the exception on.  A TryParse is typically the better
  api model, with options to handle failures (e.g. today we log a warning, but tomorrow we could add a parsing
  option that determines whether in the warning case we should throw or gracefully handle the parsing.
- Separate "service" classes that manage large number of objects can be created and passed the telemetry context
  via constructor. The service class can then create loggers and meters as needed. Example: a NodeStateManager
  that manages a large number of NodeState instances and handles logging for them.  We intend to follow this
  pattern for certificate handling, storage, and general configuration management (vs. today's data contract
  model)

In some cases the ILogger or Meter can be created in a class and passed to other classes that are created in the
context of the out class. One example is the message objects in the PubSub stack.

If that is not possible it is best to initialize the field with a `Telemetry.NullLogger` which will avoid null
reference exceptions. Note that the `Telemetry.NullLogger` is different from `NullLogger.Instance` which it returns
only in release builds. In debug builds `Telemetry.NullLogger.Instance` is a debug check logger which will
throw if any logging method is called. This allows to verify that a logger is not used before a ITelemetryContext
is available. Such code shall be gradually refactored.

### Other temporary compromises

The current codebase is still relying heavily on static methods and static classes (e.g. CertificateFactory), the
telemetry context is added as argument to these static methods or to methods that belong to classes that are
instantiated via default constructors and are effectively static too (e.g. anything that is DataContract
serializable). The goal is to eventually remove static utilities and pass the context through constructors only.

Meanwhile, any passing of `ITelemetryContext` to "public" static methods was done by adding it as the last optional
argument with a default null value except for async methods, where it comes before the CancellationToken argument.
While it does not promote adoption which a Obsolete tagged method would it makes for smaller/simpler code changes.

`IServiceMessageContext` access is at times needed in places where no context is available. For these situations
an ambient `ServiceMessageContext` is available as an async local, e.g. when using the decoder during
deserialization of extension objects. This is a compromise until the codebase is refactored to pass the telemetry
context through constructors into "service" classes that manage things like parsing, loading, deserialization, etc.
The ambient model is marked as `Experimental` at this point and should not be used in new code.

These issues will be addressed over time by refactoring the code base to be dependency injection friendly.

### Migrating

#### Important context - read first

- Breaking Changes to be aware of upfront. While affecting public signatures, much of the updated API can be
  considered internal and the effect on current code is expected to be low. Generally affected are:
  1. **Core infrastructure classes** (`ServiceMessageContext`, `EncodeableFactory`) no longer have parameter-less constructors.
  2. **Transport binding factories** require telemetry context in `Create()` methods
  3. **Certificate operations** require telemetry context parameters
  4. **Client session and subscription classes** require telemetry in constructors
  Details [below](#non-exhaustive-summary-of-breaking-changes-and-deprecations-as-part-of-the-implementation).

- Existing static `Utils.Trace` and `Utils.Log` methods are marked deprecated but continue to work using the old
  tracing model. They are however not tested anymore and usage is advised against.

- The static ILogger model via `Utils.SetLogger` API has been **completely** removed. Callers should create a new
  `ApplicationInstance` with a default telemetry context, or custom telemetry context for their application.

- See [Obtaining a telemetry context](#obtaining-a-telemetry-context) for how to get a telemetry context in existing
  code and guidelines above.

- See [Using the telemetry context](#using-the-telemetry-context) for how to use the telemetry context.

- See the official documentation for ILoggerFactory and ILogger for more details on logging.

- Use Github Copilot to make the changes in your codebase by pointing to this document.

#### Utilities

To aid migration, the following functionality is provided:

- A default telemetry context (`DefaultTelemetry`) which when created using the default constructor provides trace based
  logging via the `Utils.LoggingProvider` provider.

- A NullLogger (not the one in `Microsoft.Extensions.Logging.Abstraction` nuget package).  This `Utils.Null.Logger` and
  `Utils.Null<T>.Logger` can not be instantiated but obtained via its `static Logger` property.
  This logger can be used to initialize a `m_logger` to prevent null reference exceptions.  In release builds it is a
  dummy, in debug builds it debug checks and crashes the application with stack trace.

- A Logger that can be used to replace the `Utils.LogXXX` calls called `Utils.Fallback.Logger` and can be used in places
  where no telemetry context can be plumbed through (for example static global properties) or in cases where a ILogger
  is required in the method signature but a null was passed by the caller. The logger can be used just
  like the old static `ILogger`, e.g. `Utils.Fallback.Logger.LogInformation(...)`.  However, it must be temporary and a
  proper refactoring of the area is advised as such the Fallback class has been marked as Experimental and can be removed
  at any point in time in future releases.

#### From Static Logger Management

```csharp
// OLD - No longer works
Utils.SetLogger(myLogger);
Utils.SetLogLevel(LogLevel.Information);

// NEW - Use ITelemetryContext
var telemetryContext = DefaultTelemetry.Create(builder => builder.AddConsole());
var applicationInstance = new ApplicationInstance(telemetryContext);
```

#### From Static Logging Methods

```csharp
// OLD - Obsolete
Utils.LogInformation("Connection established to {0}", endpoint);
Utils.LogError(exception, "Failed to connect to {0}", endpoint);

// NEW - Context-based logging
var logger = telemetryContext.CreateLogger<MyClass>();
logger.LogInformation("Connection established to {Endpoint}", endpoint);
logger.LogError(exception, "Failed to connect to {Endpoint}", endpoint);
```

#### From Trace Methods

```csharp
// OLD - Obsolete
Utils.Trace("Processing request {0}", id);
Utils.Trace(TraceMasks.Information, "Request processed successfully");

// NEW - Structured logging with context
var logger = telemetryContext.CreateLogger<MyClass>();
logger.LogInformation("Processing request {RequestId}", id);
logger.LogInformation("Request processed successfully");
```

## Non-exhaustive summary of breaking changes and deprecations as part of the implementation

### 1. Core Infrastructure Classes - Constructor Changes

#### `Opc.Ua.ServiceMessageContext`

**File:** `Stack/Opc.Ua.Core/Types/Utils/ServiceMessageContext.cs`

- **REMOVED:** `public ServiceMessageContext()` - Parameter-less constructor used by stack only.

- **ADDED:** `public ServiceMessageContext(ITelemetryContext telemetry)` - Now requires telemetry context
- **ADDED:** `public ServiceMessageContext(IServiceMessageContext context, ITelemetryContext telemetry)` - Copy constructor with telemetry

#### `Opc.Ua.EncodeableFactory`

**File:** `Stack/Opc.Ua.Core/Types/Encoders/EncodeableFactory.cs`

- **REMOVED:** `public EncodeableFactory()` - Parameter-less constructor used by stack only.
- **ADDED:** `public EncodeableFactory(ITelemetryContext telemetry)` - Now requires telemetry context
- **ADDED:** `public EncodeableFactory(IEncodeableFactory factory, ITelemetryContext telemetry)` - Copy constructor with telemetry

#### `Opc.Ua.ApplicationConfiguration`

**File:** `Stack/Opc.Ua.Core/Schema/ApplicationConfiguration.cs`

- **ADDED:** `public ApplicationConfiguration(ITelemetryContext telemetry)` - New constructor with telemetry context

**File:** `Stack\Opc.Ua.Core\Stack\Types\ContentFilter.cs`

- **REMOVED:** `public ContentFilter.Evaluate(...)` - Moved to new `FilterEvaluator` class which requires telemetry context
- **ADDED:** `FilterEvaluator` class that encapsulates a filter evaluation operation with result returned as `Result` property.

Various other methods in Content filter were modified to pass telemetry context or logger, but those are used mostly internally
to the stack.

### 2. Interface Changes

#### `IServiceMessageContext`

**File:** `Stack/Opc.Ua.Core/Types/Utils/IServiceMessageContext.cs`

- **ADDED:** `ITelemetryContext Telemetry { get; }` - New property that implementations must provide

#### `ITransportBindingFactory<T>`

**File:** `Stack/Opc.Ua.Core/Stack/Bindings/ITransportBindings.cs`

- **MODIFIED:** `T Create(ITelemetryContext telemetry)` - Method signature changed to require telemetry parameter. Used by stack code only.

### 3. System Context Classes

#### `Opc.Ua.SystemContext`

**File:** `Stack/Opc.Ua.Core/Stack/State/ISystemContext.cs`

- **REMOVED:** creation via default constructor. Used by stack code only.
- **ADDED:** `public SystemContext(ITelemetryContext telemetry)` - New constructor
- **ADDED:** `public SystemContext(IOperationContext context, ITelemetryContext telemetry)` - New constructor with telemetry

### 4. Certificate and Security Classes

#### `Opc.Ua.CertificateValidator`

**File:** `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidator.cs`

- **ADDED:** `public CertificateValidator(ITelemetryContext telemetry)` - Constructor now requires telemetry context. Used by stack code only.

#### `Opc.Ua.CertificateStoreIdentifier`

**File:** `Stack/Opc.Ua.Core/Security/Certificates/CertificateStoreIdentifier.cs`

- **MODIFIED:** `public static ICertificateStore CreateStore(string storeTypeName, ITelemetryContext telemetry)` - Added telemetry parameter. Used by stack code only.  However, custom stores need to change their implementation.
- **MODIFIED:** `public virtual ICertificateStore OpenStore(ITelemetryContext telemetry)` - Added telemetry parameter. Used by stack code only.  However, custom stores need to change their implementation.

#### `Opc.Ua.CertificateIdentifier`

**File:** `Stack/Opc.Ua.Core/Security/Certificates/CertificateIdentifier.cs`

- **MODIFIED:** `public ICertificateStore OpenStore(ITelemetryContext telemetry)` - Added telemetry parameter. Used by stack code only.  However, custom stores need to change their implementation.

##### `CertificateIdentiferCollection`

- **REMOVED**: `ICertificateStore` interface removed, pass to `CertificateIdentifierCollectionStore` to make it a **readonly** store as before.

#### Certificate Store Classes

##### `CertificateIdentifierCollectionStore`

**NEW**, replaces `ICertificateStore` interface on CertificateIdentifierCollection, which should not implement the store interface. Used internally only, so no impact.

##### `DirectoryCertificateStore`

**File:** `Stack/Opc.Ua.Core/Security/Certificates/DirectoryCertificateStore.cs`

- **REMOVED:** creation via default constructor. Used by stack code only.
- **ADDED:** `public DirectoryCertificateStore(ITelemetryContext telemetry)`
- **ADDED:** `public DirectoryCertificateStore(bool noSubDirs, ITelemetryContext telemetry)`

##### `X509CertificateStore`

**File:** `Stack/Opc.Ua.Core/Security/Certificates/X509CertificateStore/X509CertificateStore.cs`

- **REMOVED:** creation via default constructor. Used by stack code only.
- **ADDED:** `public X509CertificateStore(ITelemetryContext telemetry)`

### 5. Transport Layer Breaking Changes

#### Transport Channel Factories

##### `TcpTransportChannel`

**File:** `Stack/Opc.Ua.Core/Stack/Tcp/TcpMessageSocket.cs`

- **REMOVED:** creation via default constructor. Used by stack code only.
- **ADDED:** `public TcpTransportChannel(ITelemetryContext telemetry)`
- **MODIFIED:** `public ITransportChannel Create(ITelemetryContext telemetry)` - Added telemetry parameter

#### Transport Listeners

##### `TcpTransportListener`

**File:** `Stack/Opc.Ua.Core/Stack/Tcp/TcpTransportListener.cs`

- **REMOVED:** creation via default constructor. Used by stack code only.
- **ADDED:** `public TcpTransportListener(ITelemetryContext telemetry)`

##### `HttpsTransportListener`

**File:** `Stack/Opc.Ua.Bindings.Https/Stack/Https/HttpsTransportListener.cs`

- **MODIFIED:** `public HttpsTransportListener(string uriScheme, ITelemetryContext telemetry)` - Added telemetry parameter. Used by stack code only.

#### Transport Bindings

##### `TransportBindings`

**File:** `Stack/Opc.Ua.Core/Stack/Bindings/TransportBindings.cs`

- **MODIFIED:** `public ITransportChannel GetChannel(string uriScheme, ITelemetryContext telemetry)` - Added telemetry parameter. Used by stack code only.
- **MODIFIED:** `public ITransportListener GetListener(string uriScheme, ITelemetryContext telemetry)` - Added telemetry parameter. Used by stack code only.

### 6. Client Library Breaking Changes

#### `Opc.Ua.Client.SessionClient`

**File:** `Stack/Opc.Ua.Core/Stack/Client/SessionClient.cs`

- **ADDED:** `public SessionClient(ITransportChannel channel, ITelemetryContext telemetry)` - Constructor requires telemetry. Used by derived types which are stack code only.

#### `Opc.Ua.Client.Subscription`

**File:** `Libraries/Opc.Ua.Client/Subscription/Subscription.cs`

- **ADDED:** `public Subscription(ITelemetryContext telemetry)` - Constructor with telemetry. Otherwise telemetry will be attached to subscription when calling `AddSubscription`.

#### `Opc.Ua.Client.Browser`

**File:** `Libraries/Opc.Ua.Client/Browser.cs`

- **ADDED:** `public Browser(ISession session, ITelemetryContext telemetry)` - Constructor with telemetry. Otherwise set via new Telemetry property.

#### `Opc.Ua.Client.ReverseConnectManager`

**File:** `Libraries/Opc.Ua.Client/ReverseConnectManager.cs`

- **ADDED:** `public ReverseConnectManager(ITelemetryContext telemetry)` - Constructor requires telemetry, default constructor marked as deprecated.

#### Session Factories

##### `DefaultSessionFactory`

**File:** `Libraries/Opc.Ua.Client/Session/Factory/DefaultSessionFactory.cs`

- **ADDED:** `public DefaultSessionFactory(ITelemetryContext telemetry)`- Constructor requires telemetry, default constructor marked as deprecated.

##### `TraceableSessionFactory`

**File:** `Libraries/Opc.Ua.Client/Session/Factory/TraceableSessionFactory.cs`

- **ADDED:** `public TraceableSessionFactory(ITelemetryContext telemetry)`- Constructor requires telemetry, default constructor marked as deprecated.

#### `TraceableSession`

**File:** `Libraries/Opc.Ua.Client/Session/TraceableSession.cs`

- **ADDED:** `public TraceableSession(ISession session, ITelemetryContext telemetry)` - Constructor requires telemetry, non telemetry constructors marked as deprecated.

#### `ComplexTypeSystem`

**File:** `Libraries/Opc.Ua.Client.ComplexTypes/ComplexTypeSystem.cs`

- **ADDED:** `public ComplexTypeSystem(..., ITelemetryContext telemetry)` - Constructors require telemetry, non telemetry constructors marked as deprecated.

#### `SessionReconnectHandler`

**File:** `Libraries/Opc.Ua.Client/Session/SessionReconnectHandler.cs`

- **ADDED:** `public SessionReconnectHandler(ITelemetryContext telemetry, bool reconnectAbort = false, int maxReconnectPeriod = -1)` - Constructor requires telemetry, non telemetry constructors marked as deprecated.

### 7. Configuration Library Breaking Changes

#### `Opc.Ua.Configuration.ApplicationInstance`

**File:** `Libraries/Opc.Ua.Configuration/ApplicationInstance.cs`

- **ADDED:** `public ApplicationInstance(ITelemetryContext telemetry)` - Constructor requires telemetry, non telemetry constructors marked as deprecated.
- **ADDED:** `public ApplicationInstance(ITelemetryContext telemetry, ApplicationConfiguration applicationConfiguration)` - Constructor requires telemetry, non telemetry constructors marked as deprecated.

#### `ConfiguredEndpointCollection`

**File:** `Stack/Opc.Ua.Core/Stack/Configuration/ConfiguredEndpoints.cs`

- **MODIFIED:** `public static ConfiguredEndpointCollection Load(string filePath, ITelemetryContext telemetry)` - Added telemetry parameter
- **MODIFIED:** `public static ConfiguredEndpointCollection Load(Stream istrm, ITelemetryContext telemetry)` - Added telemetry parameter

#### `ConfiguredEndpoint`

**File:** `Stack/Opc.Ua.Core/Stack/Configuration/ConfiguredEndpoints.cs`

- **MODIFIED:** `public Task UpdateFromServerAsync(ITelemetryContext telemetry, CancellationToken ct = default)` - Added telemetry parameter

### 8. Server Library Breaking Changes

#### Server Subscription Components

##### `DataChangeMonitoredItemQueue`

**File:** `Libraries/Opc.Ua.Server/Subscription/MonitoredItem/Queue/DataChangeMonitoredItemQueue.cs`

- **ADDED:** `public DataChangeMonitoredItemQueue(bool createDurable, uint monitoredItemId, ITelemetryContext telemetry)`

##### `EventMonitoredItemQueue`

**File:** `Libraries/Opc.Ua.Server/Subscription/MonitoredItem/Queue/EventMonitoredItemQueue.cs`

- **ADDED:** `public EventMonitoredItemQueue(bool createDurable, uint monitoredItemId, ITelemetryContext telemetry)`

##### `MonitoredItemQueueFactory`

**File:** `Libraries/Opc.Ua.Server/Subscription/MonitoredItem/Queue/MonitoredItemQueueFactory.cs`

- **ADDED:** `public MonitoredItemQueueFactory(ITelemetryContext telemetry)`

### 9. PubSub Library Breaking Changes

#### `UaPubSubApplication`

**File:** `Libraries/Opc.Ua.PubSub/UaPubSubApplication.cs`

- **MODIFIED:** `public static UaPubSubApplication Create(IUaPubSubDataStore dataStore, ITelemetryContext telemetry)` - Added telemetry parameter, must update

#### `UaPubSubConfigurator`

**File:** `Libraries/Opc.Ua.PubSub/Configuration/UaPubSubConfigurator.cs`

- **ADDED:** `public UaPubSubConfigurator(ITelemetryContext telemetry)` - Constructor requires telemetry, must update

#### Transport Components

##### `UdpClientBroadcast`

**File:** `Libraries/Opc.Ua.PubSub/Transport/UdpClientBroadcast.cs`

- **ADDED:** `public UdpClientBroadcast(IPAddress address, int port, UsedInContext pubSubContext, ITelemetryContext telemetry)`, only used internally.

##### `UdpClientUnicast`

**File:** `Libraries/Opc.Ua.PubSub/Transport/UdpClientUnicast.cs`

- **ADDED:** `public UdpClientUnicast(IPAddress localAddress, int port, ITelemetryContext telemetry)`, only used internally.

### 10. Method Signature Changes

#### `UserIdentityToken`

**File:** `Stack/Opc.Ua.Core/Stack/Types/UserIdentityToken.cs`

- **MODIFIED:** `public X509Certificate2 GetOrCreateCertificate(ITelemetryContext telemetry)` - must be called to create cert (previously done implicitly in Certificate getter but not anymore)

#### `ServerSecurityPolicy`

**File:** `Stack/Opc.Ua.Core/Schema/ApplicationConfiguration.cs`

- **ADDED:** `public static byte CalculateSecurityLevel(MessageSecurityMode mode, string policyUri, ILogger logger)` - Added version with ILogger parameter and deprecated non logger API

### 11. Removed and Obsolete APIs from Utils Static Class

#### Static Logger Management

- **`public static void SetLogger(ILogger logger)`** - Static logger assignment completely removed
- **`public static void SetLogLevel(LogLevel logLevel)`** - Static log level control completely removed

These methods now do nothing when called and should be replaced with the `ITelemetryContext` dependency injection pattern.

The following methods in the `Utils` class on the other hand are marked with`[Obsolete]` and will be removed
in future versions:

#### Trace Configuration Methods

- `public static void SetTraceOutput(TraceOutput output)`
- `public static int TraceMask { get; }`
- `public static void SetTraceMask(int masks)`
- `public static Tracing Tracing { get; }`
- `public static void SetTraceLog(string filePath, bool deleteExisting)`
- `public static bool UseTraceEvent { get; set; }`

#### Basic Trace Methods

- `public static void Trace(string message)`
- `public static void Trace(string format, params object[] args)`
- `public static void TraceDebug(string format, params object[] args)` (DEBUG only)
- `public static void Trace(Exception e, string message)`
- `public static void Trace(Exception e, string format, params object[] args)`
- `public static void Trace(Exception e, string format, bool handled, params object[] args)`

#### TraceMask-based Trace Methods

- `public static void Trace(int traceMask, string format, params object[] args)`
- `public static void Trace(int traceMask, string format, bool handled, params object[] args)`
- `public static void Trace<TState>(TState state, Exception exception, int traceMask, Func<TState, Exception, string> formatter)`
- `public static void Trace(Exception e, int traceMask, string format, bool handled, params object[] args)`

#### Debug Log Methods

- `public static void LogDebug(EventId eventId, Exception exception, string message, params object[] args)` (DEBUG only)
- `public static void LogDebug(EventId eventId, string message, params object[] args)` (DEBUG only)
- `public static void LogDebug(Exception exception, string message, params object[] args)` (DEBUG only)
- `public static void LogDebug(string message, params object[] args)` (DEBUG only)

#### Trace Log Methods

- `public static void LogTrace(EventId eventId, Exception exception, string message, params object[] args)`
- `public static void LogTrace(EventId eventId, string message, params object[] args)`
- `public static void LogTrace(Exception exception, string message, params object[] args)`
- `public static void LogTrace(string message, params object[] args)`

#### Information Log Methods

- `public static void LogInformation(EventId eventId, Exception exception, string message, params object[] args)`
- `public static void LogInformation(EventId eventId, string message, params object[] args)`
- `public static void LogInformation(Exception exception, string message, params object[] args)`
- `public static void LogInformation(string message, params object[] args)`

#### Warning Log Methods

- `public static void LogWarning(EventId eventId, Exception exception, string message, params object[] args)`
- `public static void LogWarning(EventId eventId, string message, params object[] args)`
- `public static void LogWarning(Exception exception, string message, params object[] args)`
- `public static void LogWarning(string message, params object[] args)`

#### Error Log Methods

- `public static void LogError(EventId eventId, Exception exception, string message, params object[] args)`
- `public static void LogError(EventId eventId, string message, params object[] args)`
- `public static void LogError(Exception exception, string message, params object[] args)`
- `public static void LogError(string message, params object[] args)`
- `public static void LogError(Exception e, string format, bool handled, params object[] args)`

#### Critical Log Methods

- `public static void LogCritical(EventId eventId, Exception exception, string message, params object[] args)`
- `public static void LogCritical(EventId eventId, string message, params object[] args)`
- `public static void LogCritical(Exception exception, string message, params object[] args)`
- `public static void LogCritical(string message, params object[] args)`

#### Generic Log Methods

- `public static void Log(LogLevel logLevel, string message, params object[] args)`
- `public static void Log(LogLevel logLevel, EventId eventId, string message, params object[] args)`
- `public static void Log(LogLevel logLevel, Exception exception, string message, params object[] args)`
- `public static void Log(LogLevel logLevel, EventId eventId, Exception exception, string message, params object[] args)`
- `public static void Log(int traceMask, string format, params object[] args)`
- `public static void Log(int traceMask, string format, bool handled, params object[] args)`
