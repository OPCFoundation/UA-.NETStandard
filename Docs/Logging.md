

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

## The proposed solution

- The proposed solution uses the `ILogger` interface which is defined in `Microsoft.Extensions.Logging.Abstractions` as core abstraction for logging functions. This dependency leads to include a new Nuget package with abstractions, but only to the core library.
- This `ILogger` interface is the proposed logging interface for .NET with OpenTelemetry. It  is also supported with prebuilt libraries by all popular logging frameworks. Including the UA stack logger in existing applications becomes a lot easier.
- As a design decision, to avoid change of API signatures in the codebase, the logger in `Opc.Ua.Core` remains a singleton which is used by all depending libraries. Also due to some C# naming convention limitations the singleton for `ILogger` remains in the `Opc.Ua.Utils` class.
- By default, a backward compatibility `TraceEventLogger` is initialized to support the existing `TraceEvent` callback.
- In order to efficiently support the existing logging all the `LoggerExtensions` functions are integrated in `Opc.Ua.Utils` as static functions instead of the Logging extension functions. This approach also allows to call the `EventSource` logger if enabled.
- The existing `Utils.Trace` logging functions are mapped as a best effort to the new `ILogger` interface. Existing application should still work and log information as previously, even after updating to the latest `Opc.Ua.Core` library with the new logging support.
- The new `ILogger`interface is available directly as a singleton `Opc.Ua.Utils.Logger` interface. But using this interface is quite cumbersome, the `Utils.LogXXX` functions should rather be used. 
- In order to support `EventSource` logging there is another singleton with the high performance logging interface exposed as  `Opc.Ua.Utils.EventLog`. There are also   seperate `EventLog` singletons for the `Opc.Ua.Client` and `Opc.Ua.Server` libraries, to allow for specific logging in these libraries.
- An application can override the `ILogger` with its own implementation by calling the `Opc.Ua.Utils.SetLogger` function. Then the existing logging with `TraceEvents` is disabled. However tracing with `EventSource` is still possible, even after the `ILogger` is replaced.
- To update existing applications to use the new logging functions, the following changes have to be made:
  - `Utils.Trace` is replaced by `Utils.LogXXX`,  where `XXX` corresponds to the log level. 
    The supported log levels are: `Critical`, `Error`, `Warning`, `Information`, `Debug` and `Trace`.
  - If a tracemask was used, the tracemask can be passed to `Utils.LogXXX` as the `EventId`. 
  - Other parameters can be passed in as required, there is support for exceptions, format strings and format parameters.

## Caveats and open issues

- At this time there is no support for structured logging. Structured logging requires to replace the format parameters like `{0}` by a name, e.g. `{ChannelId}`. Currently the `EventSource` logger and the existing `TraceEvent` do not support the structured logging format strings. A solution is under investigation.
- There is no attempt made to support `Activities` or `Scopes` from within the UA stack code yet. How to achieve the goal to support correlation ids is still under investigation.  
- The best use of the `EventId` parameter is still under investigation. Currently it is only used to support the tracemask used by existing logging calls. The tracemask is passed to `TraceEvent` for filtering and for backward compatibility. 
- Not all log functions with frequent log output have been ported yet to support the `EventSource` model.
- There is no sample yet for OpenTelemetry.

## Sample code

The `ConsoleReferenceClient` and the `ConsoleReferenceServer` have been modified to show how to use a Serilog logging provider with new `ILogger` interface. 

The file logging provider uses the `Information` or `Verbose` level depending on the chosen `Tracemasks` and the file location used in the configuration file.

The console logger uses the `Information` level and is enabled using the command line option `-c`. App output can be redirected to log using the `-l` option.

A command file for windows called `dotnettrace.cmd` is provided to capture .NET traces using the `EventSource` of the `OPC-UA-*` providers. 

The trace tool is installed using:
	 `dotnet tool install --global dotnet-trace`,  

The trace is captured from a running application with the following command: 
	`dotnet-trace collect --name consolereferenceserver --providers OPC-UA-Core,OPC-UA-Client,OPC-UA-Server`

`Perfview` on Windows shows the traces captured in the `.nettrace` files.
