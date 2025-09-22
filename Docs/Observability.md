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

## Telemetry context design introduced in 1.4.378

The new and more flexible observability design introduced in 1.4.378 deprecates and in part removes the previous 
model with the intend of supporting dependency injection, structured logging and OpenTelemetry based [observability 
(metrics, traces and logs)](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/). `ITelemetryContext` 
standardizes how logging, tracing and metrics are accessed, removes prior limitations of a singleton logger, 
and aligns the stack with current .NET and OpenTelemetry practices. The logger design follows common 
[guidance for library authors](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging-library-authors).

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
caller. Documentation can be found [here](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging).

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
var connectCounter = meter.CreateCounter<long>("opcua.client.session.connects");
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
- Seperate "service" classes that manage large number of objects can be created and passed the telemetry context
  via constructor. The service class can then create loggers and meters as needed. Example: a NodeStateManager
  that manages a large number of NodeState instances and handles logging for them.  We intend to follow this 
  pattern for certificate handling, storage, and general configuration management (vs. today's data contract
  model)

In some cases the ILogger or Meter can be created in a class and passed to other classes that are created in the 
context of the out class. One example is the message objects in the PubSub stack. 

If that is not possble it is best to initialize the field with a `Telemetry.NullLogger` which will avoid null 
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

* Existing static `Utils.Trace` and `Utils.Log` methods are marked deprecated but continue to work using the old
  tracing model. They are however not tested anymore and usage is advised against.
* The static ILogger model via `Utils.SetLogger` API has been **completely** removed. Callers should create a new 
  `ApplicationInstance` with a default telemetry context, or custom telemetry context for their application.
* See [Obtaining a telemetry context](#obtaining-a-telemetry-context) for how to get a telemetry context in existing
  code and guidelines above.
* See [Using the telemetry context](#using-the-telemetry-context) for how to use the telemetry context.
* See the official documentation for ILoggerFactory and ILogger for more details on logging.
* Use Github Copilot to make the changes in your codebase by pointing to this document.

