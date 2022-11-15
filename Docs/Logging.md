

# Improvements for Tracing and Logging #

## The existing Tracing model in the UA .NET Standard stack  ##

The existing codebase up until version 1.4.367 uses the following primitives for logging:

* `Utils.Trace` functions to log messages in the codebase with message format strings and arguments as format parameters, along with optional `TraceMasks` and `Exceptions`. 
* Logging configuration uses the `ApplicationConfiguration` section for  `TraceConfiguration` to specify the `OutputFilePath` and `TraceMasks`.
* The built in file logging is really slowing the whole system down, so logging and investigating of events with frequent output (10s of messages per second) is not possible without changing system behavior and performance.
* The current model allows to set a `TraceEvent` callback event, which allows an application to capture all messages and filter on its behalf. With some wrapper code it is possible to map the `TraceEvent` messages to existing loggers like Serilog and others. Again, the TraceEvent callback is not suitable for high performance logging, but so far the best solution using the .NET UA stack.
* A sample how to map `TraceEvent` to Serilog is provided in the .NET Framework Reference Server sample application.

## The wishlist for the future logging model

- At least for a transition period backward compatibility for existing applications. Support for `TraceEvents`.
- Reasonable flow of messages at the `Information` level to allow production applications to run with logging always enabled.
- High performance logging using  `EventSource` for development, which is available for Etw on Windows and in a different flavor on Linux with a `dotnet-trace` command.
- Support for a modern pluggable `ILogger` interface as replacement for the `Utils.Trace` functions to log leveled messages in the codebase with message format strings and arguments as format parameters, similar to the existing approach.
- Do not force users to use a specific logging framework. But simplify the use of third party logging frameworks like Serilog, Log4Net, OpenTelemetry etc.
- Support for structured logging including activities (`System.Diagnostics.Activity`) and scopes (`BeginScope`).
- Support for correlation IDs with OpenTelemetry, Zipkin etc.

## The logging solution in Version 1.4.368

- The proposed solution uses the `ILogger` interface which is defined in `Microsoft.Extensions.Logging.Abstractions` as core abstraction for logging functions. This dependency leads to include a new Nuget package with abstractions, but only to the core library.
- This `ILogger` interface is the proposed logging interface for .NET with OpenTelemetry. It  is also supported with prebuilt libraries by all popular logging frameworks. Including the UA stack logger in existing applications becomes a lot easier.
- As a design decision, to avoid change of API signatures in the codebase, the logger in `Opc.Ua.Core` remains a singleton which is used by all depending libraries. Also due to some C# naming convention limitations the singleton for `ILogger` remains in the `Opc.Ua.Utils` class.
- By default, a backward compatibility `TraceEventLogger` is initialized to support the existing `TraceEvent` callback. Once the logger strings are converted to semantic logging, the trace event callback gets the semantic version only, which may cause a format exception if directly passed to `String.Format`. Typically logging frameworks handle the semantic logging strings, but if not it is recommended to switch to the new `ILogger` based interface. The `TraceEvent` interface will be marked as deprecated after a transistion period.
- In order to efficiently support the existing logging all the `LoggerExtensions` functions are integrated in `Opc.Ua.Utils` as static functions instead of the Logging extension functions. This approach also allows to call the `EventSource` logger if enabled.
- For high frequency logging calls, e.g. to log monitored items data, the `EventSource` supports dedicated methods in the `Utils.EventLog` implementation class, to keep the overhead of such logging calls as low as possible and to avoid boxing. These methods may route logging calls back to the `Utils.LogXXX` methods if no EventListener is enabled. 
- The `EventSource` singleton, called `EventLog`, has also seperate implementations in the `Opc.Ua.Client` and `Opc.Ua.Server` libraries. The rational for seperate event source implementations is to seperate the library implementation from the core library and to allow for hooking up an event listener to specifically log client or server or core events. The implementation in the client and server library is available from the `ClientUtils` and `ServerUtils` class.
- The existing `Utils.Trace` logging functions are mapped in a best effort to the new `ILogger` interface. Existing application should still work and log information as previously, even after updating to the latest `Opc.Ua.Core` library with the new logging support.  The `Utils.Trace` methods will be marked as deprecated after a transistion period.
- The new `ILogger` interface is available directly as a singleton `Opc.Ua.Utils.Logger` interface. But using this interface is quite cumbersome, the `Utils.LogXXX` functions should rather be used. 
- In order to support `EventSource` logging there is another singleton with the high performance logging interface exposed as  `Opc.Ua.Utils.EventLog`. There are also   seperate `EventLog` singletons for the `Opc.Ua.Client` and `Opc.Ua.Server` libraries, to allow for specific logging in these libraries.
- An application can override the `ILogger` with its own implementation by calling the new `Opc.Ua.Utils.SetLogger` method. Then the existing logging with `TraceEvents` is disabled. However tracing with `EventSource` is still possible, even after the `ILogger` is replaced. For best performance, once logging through a `EventListener` starts, all logging through the externally set `ILogger` interface is disconnected.
- To update existing applications to use the new logging functions, the following changes have to be made:
  - `Utils.Trace` is replaced by `Utils.LogXXX`,  where `XXX` corresponds to the log level. 
    The supported log levels are: `Critical`, `Error`, `Warning`, `Information`, `Debug` and `Trace`.
  - If a tracemask was used, the tracemask can be passed to `Utils.LogXXX` as the `EventId`. The tracemask is still used to filter events at the Trace level.
  - Other parameters can be passed in as required, there is support for exceptions, format strings and format parameters.
  - Using the built in file logging or TraceEvent callback is not recommended anymore, choose a logging framework which supports the `Microsoft.Extensions.Logging.Abstractions.ILogger` interface as a source or even the `Microsoft.Extensions.Logging` framwork as application logger.
  - Many frameworks already supply Nuget packages for the `ILogger` interface as a logger source. Instantiate an `ILogger` instance and call the `Utils.SetLogger` method to hook up the logging framework.

## Known Issues

- Full support for structured logging has been added to the core library, however, the format strings have not been decorated yet with semantic information. Structured logging requires to replace the format parameters like `{0}` by a name, e.g. `{ChannelId}`. Currently the `TraceEvent` callback does not convert the structured logging format strings because it will be deprecated. Once the semantic information is added a TraceEvent based logger may run into `FormatExceptions` calling `String.Format`. If the Trace Events are consumed in the application by engines like Serilog, which support semantic logging, no issues should occur.
- There is no attempt made to support `Activities` or `Scopes` from within the UA stack core codebase yet. How to achieve the goal to support correlation ids is still under investigation and we are happy to receive feedback.  
- The best use of the `EventId` parameter is still under investigation. Currently it is only used to support the tracemask used by existing logging calls. The tracemask is passed to `TraceEvent` for filtering and for backward compatibility. 
- Not all log functions with frequent log output have been ported yet to support the `EventSource` model. Please expect more improvements in this area once we get more feedback.
- Samples are provided for Serilog and the Microsoft Extensions logging framework, but there is no sample yet for OpenTelemetry, Nlog and other popular logging frameworks.

## Sample code

The `ConsoleReferenceClient` and the `ConsoleReferenceServer` have been modified to show how to use a Serilog logging provider with new `ILogger` interface. 

The file logging provider uses the `Information` or `Verbose` level depending on the chosen `Tracemasks` and the file location used in the configuration file.

The console logger uses the `Information` level by default and is enabled using the command line option `-c`. App output can be redirected to log using the `-l` option.

A command file for windows called `dotnettrace.cmd` is provided to capture .NET traces using the `EventSource` of the `OPC-UA-*` providers. 

The trace tool is installed using:
	 `dotnet tool install --global dotnet-trace`,  

The trace is captured from a running application with the following command: 
	`dotnet-trace collect --name consolereferenceserver --providers OPC-UA-Core,OPC-UA-Client,OPC-UA-Server`

`Perfview` on Windows shows the traces captured in the `.nettrace` files.
