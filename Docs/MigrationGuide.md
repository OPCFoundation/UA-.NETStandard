# Migration Guide

- [Migration Guide](#migration-guide)
  - [Migrating from 1.5.378 to 2.0.x](#migrating-from-15378-to-20x)
    - [Telemetry and Logging](#telemetry-and-logging)
    - [Source Generation](#source-generation)
      - [Default value of boolean properties in source-generated data types is now false](#default-value-of-boolean-properties-in-source-generated-data-types-is-now-false)
      - [Project Structure](#project-structure)
    - [Package, Target Framework and Dependency Changes](#package-target-framework-and-dependency-changes)
      - [New published packages](#new-published-packages)
      - [Target Frameworks (only Opc.Ua.Types changes)](#target-frameworks-only-opcuatypes-changes)
      - [NuGet dependency additions and removals](#nuget-dependency-additions-and-removals)
      - [Newtonsoft.Json - what really changed](#newtonsoftjson---what-really-changed)
    - [Improved Type safety](#improved-type-safety)
      - [Several built in types are now immutable value types](#several-built-in-types-are-now-immutable-value-types)
      - [ByteString](#bytestring)
      - [ArrayOf and MatrixOf](#arrayof-and-matrixof)
        - [Configuration collection types removed](#configuration-collection-types-removed)
        - [Generated data type fields with ValueRank=OneOrMoreDimensions](#generated-data-type-fields-with-valuerankoneormoredimensions)
      - [DateTimeUtc](#datetimeutc)
      - [QualifiedName and LocalizedText](#qualifiedname-and-localizedtext)
      - [StatusCode](#statuscode)
      - [NodeId/ExpandedNodeId](#nodeidexpandednodeid)
      - [Variant, DataValue and ExtensionObject](#variant-datavalue-and-extensionobject)
        - [Deprecated boxing behavior](#deprecated-boxing-behavior)
        - [Replacement of all use of System.Object in generated code and API](#replacement-of-all-use-of-systemobject-in-generated-code-and-api)
      - [DataValue](#datavalue)
      - [XmlElement](#xmlelement)
      - [EnumValue to represent the enumeration built in type](#enumvalue-to-represent-the-enumeration-built-in-type)
      - [ExtensionObject array helpers changed](#extensionobject-array-helpers-changed)
      - [Other Data Types](#other-data-types)
      - [Obsoleted APIs and replacements](#obsoleted-apis-and-replacements)
      - [APIs permanently removed](#apis-permanently-removed)
    - [Encodeable Factory and Complex Type System](#encodeable-factory-and-complex-type-system)
      - [IType hierarchy](#itype-hierarchy)
      - [IEncodeableTypeLookup changes](#iencodeabletypelookup-changes)
      - [IEncodeableFactoryBuilder changes](#iencodeablefactorybuilder-changes)
      - [EncodeableFactory.GlobalFactory removed](#encodeablefactoryglobalfactory-removed)
      - [ExtensionObject array helpers changed](#extensionobject-array-helpers-changed)
      - [IJsonEncodeable interface removed](#ijsonencodeable-interface-removed)
    - [Complex Types](#complex-types)
      - [ComplexTypes moved to Opc.Ua.Client assembly](#complextypes-moved-to-opcuaclient-assembly)
      - [OptionSet DataType support](#optionset-datatype-support)
    - [Encoders and Decoders](#encoders-and-decoders)
    - [Node States](#node-states)
      - [Generics and Typed BaseVariableState and BaseVariableTypeState](#generics-and-typed-basevariablestate-and-basevariabletypestate)
      - [Predefined node processing](#predefined-node-processing)
      - [NodeState Cloning and Lifecycle](#nodestate-cloning-and-lifecycle)
        - [Clone() replaced with CreateCopy()](#clone-replaced-with-createcopy)
        - [BaseVariableState Read/Write helpers removed](#basevariablestate-readwrite-helpers-removed)
        - [OnAfterCreate gains CancellationToken](#onaftercreate-gains-cancellationtoken)
      - [INodeManager3 - new role-permission and method-resolution hooks](#inodemanager3---new-role-permission-and-method-resolution-hooks)
    - [User Identity Token Handlers](#user-identity-token-handlers)
    - [Configuration](#configuration)
      - [Data Contract Serializer support removed](#data-contract-serializer-support-removed)
      - [Newtonsoft.Json removed from Opc.Ua.Core](#newtonsoftjson-removed-from-opcuacore)
      - [ParseExtension/UpdateExtension signature changed](#parseextensionupdateextension-signature-changed)
      - [Session and Browser State Persistence](#session-and-browser-state-persistence)
    - [Certificate Management](#certificate-management)
      - [Certificate and CertificateCollection wrapper types](#certificate-and-certificatecollection-wrapper-types)
      - [CertificateManager and segregated interfaces](#certificatemanager-and-segregated-interfaces)
      - [Obsoleted certificate APIs](#obsoleted-certificate-apis)
    - [GDS Client API modernization](#gds-client-api-modernization)
      - [`Task` → `ValueTask` on GDS client interfaces](#task--valuetask-on-gds-client-interfaces)
      - [Removal of obsolete GDS APIs](#removal-of-obsolete-gds-apis)
    - [ManagedSession and Automatic Reconnection](#managedsession-and-automatic-reconnection)
    - [Alarms and Conditions](#alarms-and-conditions)
      - [`AlarmConditionState` state-transition behavior](#alarmconditionstate-state-transition-behavior)
      - [Auto-emit `GeneralModelChangeEvent` from `CustomNodeManager`](#auto-emit-generalmodelchangeevent-from-customnodemanager)
    - [Address-space model change tracking](#address-space-model-change-tracking)
      - [New `INodeCache.InvalidateNode` member](#new-inodecacheinvalidatenode-member)
    - [Time and Timer abstraction (`TimeProvider`)](#time-and-timer-abstraction-timeprovider)
    - [Subscriptions and Transports](#subscriptions-and-transports)
      - [Durable subscriptions and reshaped Subscription tree](#durable-subscriptions-and-reshaped-subscription-tree)
      - [PubSub](#pubsub)
      - [Reverse connect](#reverse-connect)
  - [Migrating from 1.05.377 to 1.05.378](#migrating-from-105377-to-105378)
    - [Asynchronous as default](#asynchronous-as-default)
    - [Observability](#observability)
  - [Migrating from 1.04 to 1.05](#migrating-from-104-to-105)
  - [Support](#support)

This document outlines the breaking changes introduced from version to version.  General principles we follow:

1. All API that is replaced with newer API is marked as [Obsolete] and code should compile and work albeit of the warnings which can be suppressed.  [Obsolete] API will be cleaned up in the next "minor" version increment. Therefore we recommend to upgrade from minor version to minor version and fixing all [Obsolete] warnings as you go along.
2. API that "cannot" be supported anymore will be removed in a minor version and migration steps documented below. We are trying to keep this to an absolute minimum.
3. Bugs or issues found in Obsoleted API are not supported.
4. We now follow semver, but do not use the major version indicator to denote breaking changes like (1) or (2) as we should if we followed related conventions. We are a small team and cannot afford to maintain previous major versions, therefore we are trying to keep cases of (2) to a minimum and expect you to upgrade to the next minor version within 6 months of release.

> Pro TIP: Point your favorite coding agent at this doc and let them take care of the migration work!

## Migrating from 1.5.378 to 2.0.x

> **Automate the migration.** Add the `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer` analyzer package to your projects to receive analyzer warnings and one-click fixes for the patterns in this guide. Rule IDs `UA0001`-`UA0020` map directly to the sections below.

Version 2.0 introduces a major architectural change from pre-generated code files to runtime source generation and more efficient memory use with a several major Breaking Changes requiring changes to your applications.

### Transports: WSS and HTTPS-JSON, `IMessageSocket` removed

Version 2.0 adds the two transport profiles defined in OPC UA Part 6 that 1.5.378 did not support: **WebSocket Secure** (`opc.wss://` / `wss://`, Part 6 §7.5) and **HTTPS JSON** (`application/opcua+uajson`, Part 6 §7.4.5). The WSS sub-protocols `opcua+uacp` (binary + UASC SecureChannel, all security modes) and `opcua+uajson` (compact JSON, Security Mode None only) are both supported on the same `wss://` listener. See [`Docs/Profiles.md`](Profiles.md) for the full transport matrix.

Internally the runtime transport boundary moved from `IMessageSocket` to the new public `IUaSCByteTransport` (`Opc.Ua.Bindings`). This let us share one UASC pipeline across raw TCP and WebSocket connections and let JSON profiles bypass UASC entirely. As part of this change the entire `IMessageSocket` family was **removed** from the public API surface — this is a breaking change versus 1.5.378.

| Removed type | Replacement |
|---|---|
| `IMessageSocket`, `IMessageSocketAsyncEventArgs`, `IMessageSink`, `IMessageSocketChannel` | `IUaSCByteTransport` (chunk-level send / receive); typical consumers use `ITransportChannel` instead |
| `IMessageSocketFactory` | `IUaSCByteTransportFactory` |
| `MessageSocketExtensions` (e.g. `BeginConnect`) | `IUaSCByteTransport.ConnectAsync` |
| `TcpMessageSocket`, `TcpMessageSocketFactory`, `TcpMessageSocketAsyncEventArgs` | `TcpByteTransport` (sealed; `TcpByteTransportFactory` for client-side construction). The public `TcpTransportChannel` / `TcpTransportChannelFactory` shapes are unchanged. |
| `UaSCUaBinaryTransportChannel.Socket` (`IMessageSocket?`) | `UaSCUaBinaryTransportChannel.Transport` (`IUaSCByteTransport?`) |
| `UaSCUaBinaryClientChannel(..., IMessageSocketFactory, ...)` ctor | `UaSCUaBinaryClientChannel(..., IUaSCByteTransportFactory, ...)` ctor |
| `ITcpChannelListener.ReconnectToExistingChannel(IMessageSocket, ...)` | `ITcpChannelListener.ReconnectToExistingChannel(IUaSCByteTransport, ...)` |

**If you previously implemented a custom `IMessageSocket`** (rare in practice — almost no consumer subclasses `TcpMessageSocket`): the recommended migration path is to implement [`IUaSCByteTransport`](../Stack/Opc.Ua.Core/Stack/Tcp/IUaSCByteTransport.cs) directly. See [`Docs/Transports.md`](Transports.md) § "Implementing a custom byte transport" for the contract, an implementation checklist, and a worked example (the public [`InProcessTransport`](../Stack/Opc.Ua.Core/Stack/Tcp/InProcessTransport.cs) reference implementation that consumes only the public surface). The new abstraction is chunk-oriented (one Send / Receive per UASC `MessageChunk`) and exposes only `ValueTask`-based async; it is intentionally narrower than the old SAEA-based `IMessageSocket` and most legacy implementations collapse to ~150 lines.

### Telemetry and Logging

Observability in 2.0 is plumbed through `ITelemetryContext`. Loggers are resolved from the telemetry context via `telemetry.CreateLogger<T>()` rather than from `Utils.Trace` / `Utils.LogX`. The static logging helpers remain compilable but are `[Obsolete]`; consumers should resolve `ILogger` from `ITelemetryContext` instead.

Constructor injection across the public API is not uniform - the parameter is required positionally on most types, optional on `ApplicationInstance`, and absent on `Session` / `CustomNodeManager2`. The table below summarises the precise shape per type:

| Type | Telemetry parameter | Notes |
|---|---|---|
| `ApplicationInstance(ITelemetryContext? telemetry)` | Nullable | Also `ApplicationInstance(ApplicationConfiguration, ITelemetryContext?)`. Passing `null` falls back to a default telemetry context. |
| `ServerBase(ITelemetryContext telemetry)` | Required positional | The only public ctor. |
| `CertificateManagerFactory.Create(SecurityConfiguration, ITelemetryContext, Action<CertificateManagerOptions>?)` | Required positional (2nd parameter) | Factory entry point for `CertificateManager`. |
| `DefaultSessionFactory()` / `DefaultSessionFactory(ITelemetryContext telemetry)` | Both ctors exist | The parameterless ctor is `[Obsolete]`; use the telemetry-aware overload. |
| `ManagedSessionFactory(ITelemetryContext telemetry)` | Required positional | The only public ctor. |
| `Session` ctors | **None** | Telemetry flows in via `ApplicationConfiguration` or `ISubscriptionEngineFactory`. Do not look for a `Session(... ITelemetryContext)` overload - none exists. |
| `CustomNodeManager2(IServerInternal, ApplicationConfiguration?, bool, ILogger, params string[])` | **None directly** | Obtain a logger via `server.Telemetry.CreateLogger<T>()` and pass it to the ctor. |

```csharp
// Server side - log via the server's telemetry context
public sealed class MyNodeManager : CustomNodeManager2
{
    public MyNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, useSamplingGroups: false,
               server.Telemetry.CreateLogger<MyNodeManager>(),
               "urn:example:my-namespace")
    {
    }
}

// Client side - construct the factory with telemetry
var factory = new ManagedSessionFactory(telemetry);
ISession session = await factory.CreateAsync(/* ... */);
```

### Source Generation

Instead of generating code for OPC UA design files using the [ModelCompiler](https://github.com/OPCFoundation/UA-ModelCompiler), this version of the stack uses [Source Generators](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/#source-generators) to generate code behind for your project. Input into the source generator can be NodeSet2.xml files or ModelDesign.xml files (the same that ModelCompiler consumes). Example projects are provided in the Applications folder. Source generators are Roslyn analyzers, that are called by the Roslyn compiler and emit code during the build process.

**Model compiler generated csharp code is not supported in this version!**

To migrate remove all your generated files (ending in `*.Classes.cs`, `*.Constants.cs`, etc.) and only leave the design file(s) (.xml and .csv files) in your project. Add an entry into your `csproj` file similar to the following to provide the location of the design files to the source generation process:

```xml
  <PropertyGroup>
    <!-- Optional: to configure whether to allow sub types - see model compiler documentation -->
    <ModelSourceGeneratorUseAllowSubtypes>true</ModelSourceGeneratorUseAllowSubtypes>
  </PropertyGroup>
  <ItemGroup>
    <!-- Generate code behind for the following design or nodeset2.xml files during build-->
    <AdditionalFiles Include="Boiler\Generated\BoilerDesign.csv" />
    <AdditionalFiles Include="Boiler\Generated\BoilerDesign.xml" />
    <AdditionalFiles Include="MemoryBuffer\Generated\MemoryBufferDesign.csv" />
    <AdditionalFiles Include="MemoryBuffer\Generated\MemoryBufferDesign.xml" />
    <AdditionalFiles Include="TestData\Generated\TestDataDesign.csv" />
    <AdditionalFiles Include="TestData\Generated\TestDataDesign.xml" />
  </ItemGroup>
```

The [source generator model](https://devblogs.microsoft.com/dotnet/introducing-c-source-generators/) has several benefits that go beyond custom `msbuild` targets: Among the most important is that the generator ships with the stack and therefore code that is generated conforms to the stack version that ships the analyzer (the source generator will be part of `Opc.Ua.Core` nuget package). Therefore when updating to a newer version the code generated automatically takes advantage of the improvements made across the entire stack.

Code generation during compilation also allows not just emitting code ahead of time, but also to generate code while you are developing. We now take advantage of this feature to generate `IEncodeable` implementations for partial POCO types on the fly using the `[DataType]` and `[DataTypeField]` attributes as annotation (similar to `DataContract`/`DataMember`).

The stack itself uses source generators to generate the core opc ua code. Therefore all pre-generated code files (`Generated/` folders) have been removed and are now generated at build time. As a result of using source generators to generate the stack code all `*.nodeset2.xml` files previously included as embedded zip have been removed. Also, all `*.Types.xsd` and `*.Types.bsd` files are now included as string resource instead of embedded resources. If you need access to these, use the new `Schemas.XmlAsStream` and `Schemas.BinaryAsStream` APIs in the node manager namespace which produce a utf8 stream. Alternatively you can use the existing ModelCompiler tool to generate these files.

When you encounter slower build times use incremental compilation and avoid changes to code in Opc.Ua and Opc.Ua.Core project. In addition you can change your builds to only build for your target framework using the dotnet `-f <tfm>` command line option, e.g. `-f net10`.

#### Default value of boolean properties in source-generated data types is now false

**Breaking Change**: Boolean properties on source-generated data types now correctly default to `false` instead of `true`.

Generated code produced by the model compiler contained a bug because it inverted the default value for boolean fields in generated data types. Boolean fields without an explicit `<DefaultValue>` in the model design XML were initialized to `true` instead of `false` as expected and defined in Part 6. This has been fixed.

**Impact**: Any code that creates instances of source-generated data types and relies on boolean properties being `true` by default must now explicitly set those properties to `true`. This primarily affects PubSub configuration types:

| Type | Property | Old Default | New Default |
|---|---|---|---|
| `PubSubConfigurationDataType` | `Enabled` | `true` | `false` |
| `PubSubConnectionDataType` | `Enabled` | `true` | `false` |
| `WriterGroupDataType` | `Enabled` | `true` | `false` |
| `ReaderGroupDataType` | `Enabled` | `true` | `false` |
| `DataSetWriterDataType` | `Enabled` | `true` | `false` |
| `DataSetReaderDataType` | `Enabled` | `true` | `false` |
| `PublishedDataSetCustomSourceDataType` | `CyclicDataSet` | `true` | `false` |

Other affected types include all source-generated structures with boolean fields (e.g., `AggregateConfiguration.TreatUncertainAsBad`, `MonitoringParameters.DiscardOldest`, `CreateSubscriptionRequest.PublishingEnabled`) as well as
some hand-written types in `Opc.Ua.Types` (such as `BrowseDescription`, `RelativePathElement`).

**Migration**: Add explicit initialization where your code depends on `true` as the default:

```csharp
// Before (relied on incorrect true default)
var connection = new PubSubConnectionDataType
{
    Name = "MyConnection"
};

// After (explicitly set Enabled)
var connection = new PubSubConnectionDataType
{
    Enabled = true,
    Name = "MyConnection"
};
```

#### Server default Aggregate configuration now treats Uncertain as Bad (Part 13)

**Behavioral Change (Part 13 compliance)**: The server-side default aggregate configuration returned by
`AggregateManager.GetDefaultConfiguration(...)` — used when a `ReadProcessedDetails` request sets
`AggregateConfiguration.UseServerCapabilitiesDefaults = true` — now sets `TreatUncertainAsBad = true`,
matching the default mandated by OPC 10000-13 (Aggregates) v1.05.07 §4.2.1.2. Previously it defaulted to
`false`.

**Impact**: Processed (aggregate) history reads that rely on the server-capabilities defaults now treat
Uncertain-quality samples as Bad when computing aggregate `StatusCode`s (unless a specific aggregate
definition states otherwise). Clients that require the previous behavior should send an explicit
`AggregateConfiguration` with `TreatUncertainAsBad = false` instead of `UseServerCapabilitiesDefaults = true`.

#### Project Structure

New `Opc.Ua` project as an intermediate project. Impact:

- Most applications using NuGet packages are not affected. Continue linking to Opc.Ua.Core project as it includes the Opc.Ua intermediate assembly
- Assembly loading order *may* change

### Package, Target Framework and Dependency Changes

#### New published packages

Two assemblies that previously shipped only as transitive content inside `Opc.Ua.Core` are now published as standalone NuGet packages. Add an explicit `<PackageReference>` only if your project depends on these types without also depending on `Opc.Ua.Core` (which still includes them transitively).

**`OPCFoundation.NetStandard.Opc.Ua.Core.Types`** (project `Stack/Opc.Ua.Core.Types/Opc.Ua.Core.Types.csproj`, `IsPackable=true`, target frameworks `$(LibCoreTargetFrameworks)`). Owns the framework-neutral built-in type and node-state contracts. Headline public types include `IServiceRequest`, `IServiceResponse`, `BaseEventState`, `EventSeverity`, `InstanceStateSnapshot`, `FolderState`, `FolderTypeState`, `LimitAlarmStates`, `ContentFilter` (including `Result` / `ElementResult`), and `MonitoringFilter` / `MonitoringFilterResult`.

```xml
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Core.Types" Version="2.0.*" />
```

**`OPCFoundation.NetStandard.Opc.Ua.Security.Certificates`** (project `Stack/Opc.Ua.Security.Certificates/Opc.Ua.Security.Certificates.csproj`, `IsPackable=true`, target frameworks `$(LibCoreTargetFrameworks)`). Owns the wrapper certificate type system. Headline public types: `Certificate`, `CertificateCollection`, `IX509Certificate`, `ICertificateFactory`, `ICertificateIssuer`, `CertificateChangeKind`, `X509AuthorityKeyIdentifierExtension`, `X509CrlNumberExtension`, `X509SubjectAltNameExtension`, `CRLReason`.

```xml
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Security.Certificates" Version="2.0.*" />
```

#### Target Frameworks (only Opc.Ua.Types changes)

The TFM matrix for the main libraries (Core, Client, Server, Configuration, etc.) is unchanged from 1.5.378: `net472;net48;netstandard2.1;net8.0;net9.0;net10.0`. The only consumer-visible change is the `Opc.Ua.Types` assembly: on 1.5.378 it tracked the dedicated `LibTypesTargetFrameworks` variable (`net472;net48;netstandard2.0;netstandard2.1;net8.0;net9.0;net10.0`); on 2.0 the variable is removed and `Opc.Ua.Types` tracks `LibCoreTargetFrameworks`, the same matrix as every other library. The net effect is that `netstandard2.0` is no longer offered for `Opc.Ua.Types`.

The minimum SDK is the **.NET 10 SDK**, and projects compile with **`LangVersion 14.0`**. Projects that target `netstandard2.0` and pull in `Opc.Ua.Types` will fail to restore with `NU1202` ("package is not compatible") - retarget to `netstandard2.1` or one of the .NET / .NET Framework TFMs above.

#### NuGet dependency additions and removals

| Package | Status in 2.0 | First introduced in |
|---|---|---|
| `DotNext` 5.26.3 | Added | `Libraries/Opc.Ua.Lds.Server/Opc.Ua.Lds.Server.csproj` |
| `Makaretu.Dns.Multicast` 0.27.0 | Added (pinned) | Centralised pin; previously vendored in-tree, no direct reference yet |
| `Microsoft.Bcl.TimeProvider` 10.0.8 | Added (pinned) | Centralised pin; transitive use for `TimeProvider` on net472/net48 |
| `Microsoft.CodeAnalysis.Analyzers` 4.14.0 | Added | `Stack/Opc.Ua.Core/Opc.Ua.Core.csproj` (runtime source-generation surface) |
| `Microsoft.CodeAnalysis.Common` 4.14.0 | Added | `Stack/Opc.Ua.Core/Opc.Ua.Core.csproj` |
| `Microsoft.CodeAnalysis.CSharp` 5.3.0 | Added | `Stack/Opc.Ua.Core/Opc.Ua.Core.csproj` |
| `Microsoft.Extensions.Configuration.Abstractions` 10.0.8 | Added (pinned) | Used by dependency injection integration |
| `Microsoft.Extensions.Diagnostics` 10.0.8 | Added (pinned) | Centralised pin |
| `Microsoft.Extensions.Hosting` 10.0.8 | Added (pinned) | Centralised pin |
| `Microsoft.Extensions.Hosting.Abstractions` 10.0.8 | Added (pinned) | Centralised pin |
| `Microsoft.Extensions.Options` 10.0.8 | Added (pinned) | Centralised pin |
| `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.8 | Added (pinned) | Centralised pin |
| `ModelContextProtocol` 1.3.0 | Added | `Applications/McpServer/Opc.Ua.Mcp.csproj` |
| `ModelContextProtocol.AspNetCore` 1.3.0 | Added | `Applications/McpServer/Opc.Ua.Mcp.csproj` |
| `SourceGenerator.Foundations` 2.0.14 | Added | `Tools/Opc.Ua.SourceGeneration.Stack/Opc.Ua.SourceGeneration.Stack.csproj` |
| `System.CommandLine` 2.0.8 | Added | `Applications/McpServer/Opc.Ua.Mcp.csproj` |
| `System.Threading.Channels` 10.0.8 | Added | `Libraries/Opc.Ua.Lds.Server/Opc.Ua.Lds.Server.csproj` |
| `TUnit` 1.45.8 | Added (test-only) | `Tests/Opc.Ua.Server.Tests/Opc.Ua.Server.Tests.csproj` |
| `NUnit.Analyzers` 4.13.0 | Added (test-only) | Test projects |
| `ObjectLayoutInspector` 0.2.0 | Added (test-only) | Test projects |
| `System.Reflection.Metadata` 10.0.6 | Added (test-only) | Test projects |
| `Mono.Options` 6.12.0.148 | Removed | Previously referenced by `Applications/ConsoleReferenceServer/MonoReferenceServer.csproj` |

#### Newtonsoft.Json - what really changed

`Newtonsoft.Json` was removed as a direct dependency of `Stack/Opc.Ua.Core/Opc.Ua.Core.csproj` in 2.0. The only direct `<PackageReference Include="Newtonsoft.Json" ... />` remaining anywhere under `Libraries/` and `Stack/` is in `Libraries/Opc.Ua.PubSub/Opc.Ua.PubSub.csproj`. Consequences:

- Consumers that reached `Newtonsoft.Json` only transitively through `Opc.Ua.Core` now need to add their own explicit reference.
- Consumers of `Opc.Ua.PubSub` continue to receive `Newtonsoft.Json` transitively and are unaffected.

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
```

Use `Version="13.0.4"` or any compatible later `13.x` release.

### Improved Type safety

#### Several built in types are now immutable value types

The `Variant` and `TypeInfo`, `NodeId`, `ExpandedNodeId`, `ExtensionObject`, `LocalizedText` and `QualifiedName` are now `readonly struct`s. This is a large breaking change and affects existing usage:

1. You cannot compare any of these types against `null`. Use the instance properties: `NodeId.IsNull`, `ExpandedNodeId.IsNull`, `QualifiedName.IsNull`, `LocalizedText.IsNullOrEmpty`, `ExtensionObject.IsNull`. In case of `ArrayOf`/`MatrixOf`/`ByteString`, you can most often just check against `IsEmpty` which checks null and emptiness.
2. The default item can be created by assigning `default`, e.g. producing `NodeId.Null` for NodeId and `QualifiedName.Null` for QualifiedName. It is recommended to use the `Null` property on these types for readability and per your coding conventions.
3. Any API that mutated an instance of one of these built in types must be replaced with methods that return a new value of the type, e.g. `NodeId.WithNamespaceIndex(ushort)` as setters were removed.

#### ByteString

Previously the OPC UA built-in type *ByteString* was represented as `byte[]`. This caused ambiguities with regards to it and the byte *array* type. This has changed and `ByteString` is now a type in the Opc.Ua namespace. It is a wrapper around `ReadOnlyMemory<byte>` and while `Variant` handles both still interchangeably, the generated API now simplifies mixing of byte arrays and `ByteString` without confusion.

Note that equality operation compare the content of the byte string. A `ByteString` is a value type while `System.Byte[]` is not. It cannot be compared against `null`. However, it supports checking for empty `IsEmpty` and `IsNull` whereby the first checks whether the ByteString is effectively a `ByteString.Empty` amd the second checks whether `ByteString` was initialized using `default`.

While it was tempting to make `ByteString` implicitly convertible from `byte[]`, an explicit cast is needed to strictly distinguish against `ArrayOf<byte>` which implicit converts to `byte[]`. Prefer the `ByteString.From` or `ToByteString()` calls to cast operators to make your code's intentions explicit. Note that a `byte[]` implicitly converts to `ReadOnlyMemory<byte>` in .net therefore any conversion from `ByteString` is explicit.

To migrate, perform the following general replacements in your code:

**Change code as follows:**

- Replace `byte[]` with `ByteString` in areas flagged as errors, e.g. wherever casting a `Variant` to a `byte[]` change it to `ByteString` or to `ArrayOf<byte>` if it is a byte array.
- When a `ByteString` is required as input and you have any form of enumerable bytes, try appending `.ToByteString()` to convert.
- Use `ByteString.Combine` in lieu of `Utils.Append`.
- Indexing and enumeration of bytes is only supported via the `Span` property. Change your code to replace `[i]` with `.Span[i]` to fix errors.
- If your code tried to set a byte in the ByteString, create a buffer `byte[]` and after changing convert to `ByteString` using `ByteString.From(buffer)` or `.ToByteString()` extension method
- Perform changes only where you encounter build breaks. This should be enough to get into a working state. Later adjust the code as needed.

#### ArrayOf and MatrixOf

Similar to `ByteString`, `ArrayOf<T>` and `MatrixOf<T>` are new type safe and sliceable generic value types representing non-scalar values. They are immutable meaning the values at an index inside them cannot be "set" unless they are converted to a `Span<T>` (and then reconverted to a `ArrayOf`/`MatrixOf`).

In addition to slicing and range based access, both types provide the ability to apply a NumericIndex to them.  They are efficiently stored inside a Variant as well and can be used to allocate efficiently from `ArrayPool` providing the ability to built object pooling support at the array level. `ArrayOf<T>` implicitly converts to `List<T>` but not vice versa. For API that is taking `ArrayOf<T>` as input convert any list using `ToArrayOf`. `IsEmpty` returns true if `IsNull` is true but not necessarily vice versa.

Internally an `ArrayOf`/`MatrixOf` stores a reference to "memory" and a offset and length integer. They have the same layout as `ReadOnlyMemory<T>` although this is not guaranteed to stay so in the future. All generated collection types implicitly convert to and from `ArrayOf<T>` whereby `T` is the member type of the collection type.  E.g. `VariantCollection` is effectively `ArrayOf<Variant>`.

`ArrayOf<T>` provides helper methods e.g. to  `AddItem` an item or `AddItems` of items in another `ArrayOf<T>`. Both return a new `ArrayOf<T>`, very similar to the .net ImmutableCollection classes or the `Append` or `Concat` extension methods in the `System.Linq`.

`Contains`, `IndexOf`, `Filter`, `Find`, `FindIndex` and `ConvertAll` methods mimic the Linq `Where`, `Any`, `FirstOrDefault`, `Select` or the respective methods on the `List<T>` type. Use `SafeSlice` instead of `Take` to slice up to the length and which returns an empty array instead of throwing which is what the regular Slice/range operators do.  You cannot use more advanced Linq expressions (e.g. order by or group by) without converting to a list (`ToList`) or array (`ToArray`) first. Linq is slow, so using the methods on the array type where possible will provide a performance improvement.

All generated APIs, Encoders/decoders, and the Variant type now use `ArrayOf`/`MatrixOf` instead of the previously generated/built-in non-generic collection types which have been removed.

Note that equality operators and methods now compare the content of the Array and Matrix, not just reference equality as with `T[]`. It supports checking for an empty array or matrix via `IsEmpty` and `IsNull` whereby the first checks whether the array is effectively a `ArrayOf.Empty<T>` amd the second is just a check against `ArrayOf<T>` initialized using `default` (since it is not a reference type anymore). `IsEmpty` returns true if `IsNull` is true but not necessarily vice versa.

**Change code as follows:**

> ℹ **Tip — install `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`** before touching collection sites. Its source generator emits an `internal sealed [Obsolete] class <Name>Collection : List<TElement>` shim per consumer compilation for every `<Type>Collection` the consumer references (including model-compiled `<UserType>Collection` patterns), so `CS0246: type or namespace 'XxxCollection' not found` is replaced with `[Obsolete]` warnings + `UA0002` analyzer guidance you can iterate through.

- Replace any `T[]` with `ArrayOf<T>` where T is the type of the element in the array. Do this where errors are flagged, e.g. wherever casting a Variant to a `T[]` change it to `ArrayOf<T>` if it is a T array.
- Change all use of `<Type>Collection` or `IList<Type>` to `List<Type>` (add a `using System.Collections.Generic` directive if needed). When the collection is never mutated (items added, inserted or removed), use `ArrayOf<Type>`.
- In case of `error CS4007: Instance of type 'System.ReadOnlySpan<T>.Enumerator' cannot be preserved across 'await' or 'yield' boundary` convert the enumerated `ArrayOf<T>` to a list using `ToList()` and enumerate the list.
- When trying to set a value in the previous array, create a buffer `T[]` and after mutating convert to `ArrayOf<T>` using `buffer.ToArrayOf()`.
- To add items to an `ArrayOf` use the new `AddItem`/`AddItems` methods where you would have used `Add` or `AddRange` before. Note that ArrayOf is immutable so the result needs to be assigned to the variable to which you want to add. You can also use the `+=` operator for less verbose code.
- In performance intensive code or where items are added in a loop it is best to first create a `List<T>` and then assign the list later (e.g. after the loop) to a variable of `ArrayOf<T>` type.
- Perform changes only where you encounter build breaks. This should be enough to get into a working state. Later adjust the code if needed.
- Remove any use of `Matrix` which is deprecated and replace with `MatrixOf<T>` which is type safe.

``` csharp
    // Some examples
    VariantCollection c = new VariantCollection();
    // if (c != null) if c is passed from outside
    c.Add(new Variant(1))
    var first = c.FirstOrDefault();
    Int32Collection i = c.Select(v => (int)v).ToList();

    // need to change to
    ArrayOf<Variant> c = [new Variant(1)]; // or
    ArrayOf<Variant> c = default; c = c.Add(new Variant(1)); // or
    ArrayOf<Variant> c = default; c += new Variant(1);
    var first = !c.IsEmpty ? c[0] : default;
    ArrayOf<int> i = c.ConvertAll(v => (int)v);
```

##### Configuration collection types removed

All `List<T>`-based collection wrappers for configuration types have been removed and replaced with `ArrayOf<T>`: `ServerSecurityPolicyCollection`, `TransportConfigurationCollection`, `SamplingRateGroupCollection`, `ReverseConnectClientCollection`, `ReverseConnectClientEndpointCollection`, `ServerRegistrationCollection`, `CertificateIdentifierCollection`, `CertificateGroupConfigurationCollection`, `OAuth2ServerSettingsCollection`, `OAuth2CredentialCollection`.

##### Generated data type fields with ValueRank=OneOrMoreDimensions

Previously, every structure field declared with `ValueRank="OneOrMoreDimensions"` in a model design was generated as `global::Opc.Ua.Variant`. The property is now typed as `global::Opc.Ua.MatrixOf<T>` (mirroring the `ArrayOf<T>` treatment already used for `ValueRank="Array"`). Encoding/decoding still flows through `Variant`, but the boxing/unboxing happens inside the encoder calls so consumers see the typed surface.

The element type follows the field's `DataType`:

| Field `DataType`                                  | Generated property type                    | Encode call                                                       | Decode call                                                                 |
| ------------------------------------------------- | ------------------------------------------ | ----------------------------------------------------------------- | --------------------------------------------------------------------------- |
| primitive (e.g. `Boolean`, `Int32`, `String`)     | `MatrixOf<bool>` etc.                      | `encoder.WriteVariant(name, Variant.From(field));`                | `field = decoder.ReadVariant(name).GetBooleanMatrix();` (etc.)              |
| `Structure` / abstract structure parent           | `MatrixOf<ExtensionObject>`                | `encoder.WriteVariant(name, Variant.From(field));`                | `field = decoder.ReadVariant(name).GetExtensionObjectMatrix();`             |
| concrete `IEncodeable` (e.g. `Vector`)            | `MatrixOf<Vector>`                         | `encoder.WriteEncodeableMatrix(name, field);`                     | `field = decoder.ReadEncodeableMatrix<Vector>(name);`                       |
| typed enum (`MyEnum`)                             | `MatrixOf<MyEnum>`                         | `encoder.WriteVariant(name, Variant.From(field));`                | `field = decoder.ReadVariant(name).GetEnumerationMatrix<MyEnum>();`         |
| `BaseDataType` / `Number` / `Integer` / `UInteger`| `MatrixOf<Variant>`                        | `encoder.WriteVariant(name, Variant.From(field));`                | `field = decoder.ReadVariant(name).GetVariantMatrix();`                     |

`Variant` round-trip APIs are available for every `BasicDataType` value except `DiagnosticInfo`. For a `DiagnosticInfo` matrix field — which is not a valid structure field per OPC UA Part 5 in any case — the legacy `Variant` property surface is retained.

**Change code as follows:**

- Direct access on the property is now typed; replace the
  `new Variant(new Matrix(...))` wrapping / `Variant.Value` cast you
  needed in 1.5.378 with the typed `MatrixOf<T>` assignment:

  ``` csharp
      // Before (1.5.378) — field was object/Variant; wrap a Matrix
      myStruct.MyMatrix = new Variant(new Matrix(
          new int[] { 1, 2, 3, 4 },
          BuiltInType.Int32,
          new int[] { 2, 2 }));
      var back = (int[,])((Matrix)myStruct.MyMatrix.Value).ToArray();

      // After — field is MatrixOf<int>; assign / read directly
      myStruct.MyMatrix = new int[,] { { 1, 2 }, { 3, 4 } }.ToMatrixOf();
      MatrixOf<int> back = myStruct.MyMatrix;
  ```

- `IDecoder` gained a parameterless `ReadEncodeableMatrix<T>(string? fieldName) where T : IEncodeable, new()` overload that mirrors the existing `ReadEncodeableArray<T>(string? fieldName)` shape. Custom `IDecoder` implementations should add this overload alongside the existing encoding-id variant.

###### VariableType State classes, PropertyState instances, and service parameters

The same `MatrixOf<T>` opt-in now extends beyond structure data type fields to three sibling sites in the source generator:

- **VariableType State classes** — `VariableType` designs that restrict both the `DataType` and the `ValueRank` to a concrete matrix shape (e.g. `XYArrayItemType` with `DataType="XVType" ValueRank="OneOrMoreDimensions"`) now inherit the generic chain with a typed parameter. Previously: `XYArrayItemState : ArrayItemState<Variant>.Implementation<VariantBuilder>`. Now: `XYArrayItemState : ArrayItemState<MatrixOf<XVType>>.Implementation<StructureBuilder<XVType>>`. Consumers reading or writing `.Value` get a typed `MatrixOf<XVType>` directly.
- **PropertyState / BaseDataVariableState instances** — instance variables that *narrow* a generic variable type (e.g. `PropertyType` → `EnumDictionaryEntries` with `DataType="NodeId" ValueRank="OneOrMoreDimensions"`) now declare typed `PropertyState<MatrixOf<NodeId>>` instead of falling back to the simple `PropertyState` name and losing type information. Same for `FailureSystemIdentifier` → `BaseDataVariableState<MatrixOf<byte>>`.
- **Service method parameters** — Client/Server API generators now type matrix-rank arguments as `MatrixOf<T>` instead of `Variant`. No OPC UA standard service declares matrix arguments today, so this is forward-looking for custom service models.

**Change code as follows:**

For abstract base variable types (`ArrayItemType`, `CubeItemType`, `ImageItemType`, `NDimensionArrayItemType`, all of which declare `DataType="BaseDataType"`) the State class still uses the generic `<T>` parameter — consumers continue to instantiate with whatever element type matches their data.

For *concrete* matrix variable types (today only `XYArrayItemType`) and matrix-rank property/variable instances, the `Value` setter and getter are now typed. Replace the 1.5.378 `new Variant(new Matrix(...))` pattern with a typed `MatrixOf<T>` assignment:

``` csharp
    // Before (1.5.378) — Value was object; wrap a Matrix of XVType
    variable.Value = new Variant(new Matrix(
        new XVType[]
        {
            new XVType { X = 0.0, Value = 0.0f },
            new XVType { X = 1.0, Value = 1.0f }
        },
        BuiltInType.ExtensionObject,
        new int[] { 2 }));

    // After — Value is MatrixOf<XVType>; use a typed constructor
    variable.Value = new[]
    {
        new XVType { X = 0.0, Value = 0.0f },
        new XVType { X = 1.0, Value = 1.0f }
    }.ToMatrixOf(2);
```

#### DateTimeUtc

Previously the **DateTime** built in type was represented by the `System.DateTime` type. It is now represented by the `Opc.Ua.DateTimeUtc` type. This new type complies with the details of the spec without requiring external helper methods to be used. It's Value property returns the ticks, bounded by the information in Part 6 of the spec, and its time is always UTC. There are conversion operations to and from `DateTime`, but also `DateTimeOffset` and `long` and a minimal subset of `System.DateTime` API to allow for simpler porting. `DateTime` implicitly converts to `DateTimeUtc`, but not vice versa to force use of the new type.

**Change code as follows:**

- Replace `DateTime` with `DateTimeUtc` where appropriate, especially in places where comparing with `DateTime.MinValue`.
- Replace `DateTime.UtcNow` with `DateTimeUtc.Now` for UTC time "right now". `DateTime.Now` or `DateTime.Today` can be cast or replaced with its Utc variant, which is likely intended anyway as all date/time values in OPC UA are UTC.
- When assigning a `DateTime` value to a `DateTimeUtc` variable, add a cast, or use the corresponding `DateTimeUtc` constructor.

#### QualifiedName and LocalizedText

There is no implicit conversion from `string` to `QualifiedName` or `LocalizedText` anymore. For one, it flags areas where null assignment is happening implicitly, and secondly, it makes the API more explicit. E.g. previously it was possible to assign a string to a browse name which landed the browse name accidentally in namespace 0 instead of the owning namespace. If you know what you are doing you can explicitly cast the string, but it is suggested to use the new static `From` API instead.

#### StatusCode

`StatusCode` contains now not only a uint code, but also a symbol.  Symbols are interned strings and using the `StatusCodes` constants therefore come with the symbol string. This removes the need to look up the symbolic id, however, when receiving a uint code it needs to be translated to a StatusCode constant to retain the Symbol. Older API has been obsoleted with proper instructions. Since types are immutable it is important to replace mutation calls with the proper replacement method and store the returned value.

#### NodeId/ExpandedNodeId

`NodeId`s with integer identifiers (the most common case) now do not box the integer identifier anymore into an object, making the entire NodeId heap allocation free (*).  ExpandedNodeId with integer identifiers only contain an allocated namespace Uri, which is mostly a const (interned) string, reducing small allocations across both types. Because both types are now immutable, they must be mutated using the provided `With<X>`. Access to the identifier in boxed form (object) is deprecated. Instead use the `TryGetValue(out uint/string/Guid/byte[])` API. If you need to get the identifier only to "stringify" it, use the `IdentifierAsText` property which avoids boxing integer identifiers.

There is no implicit conversion from `uint`/`Guid`/`string`/`byte[]` to `NodeId`/`ExpandedNodeId` to ensure assignment of null reference types (byte array and string) is not happening implicitly and to prevent accidental conversion of these identifiers into namespace 0. It also removes  hidden behavior such as parsing during assignments and flags areas where a proper Null/default NodeId should be inserted/returned. Use the explicit cast (e.g. `(NodeId)[(byte)3, 2]`) instead. For the previous implicit conversion from `string` to `NodeId` conversion use `NodeId.Parse` and `ExpandedNodeId.Parse`. On the same note, the constructor taking a string and no namespace index has been deprecated as it required a string to parse. Use Parse/TryParse instead.

> (*) Note that NodeId leverages the new `uint` field to cache the HashCode of a "non-uint" "Identifier", which provides faster lookup using NodeId/ExpandedNodeId as key.

#### Variant, DataValue and ExtensionObject

Previously the `Variant` was a *mutable* struct containing a `TypeInfo` and `Value` property allowing setting the inner state and returning `object`.  All value types thus were implicitly boxed to object and landing on the heap. The new `Variant` only boxes value types > 8 bytes in size (*), and stores the rest in a union.  `TypeInfo`, previously a class, also now is stored as a 4 byte type (with padding).

The `ExtensionObject` was a reference type wrapping a `NodeId` and a body as a reference type of `object`. The `ExtensionObject` is now an immutable value type with type-safe access to its body.

`Session.Call` / `Session.CallAsync` previously took `params object[]` and silently boxed every argument. The new signature takes `params Variant[]`, so each call argument must be wrapped explicitly:

```csharp
// Before
var output = session.Call(objectId, methodId, 1, "two", DateTime.UtcNow);

// After
var output = session.Call(objectId, methodId,
    Variant.From(1),
    Variant.From("two"),
    Variant.From(new DateTimeUtc(DateTime.UtcNow)));
```

`null` arguments must be passed as `Variant.Null` (a literal `null` will not bind to the `params Variant[]` overload).

##### Deprecated boxing behavior

Access to the `Value` property of `Variant` is marked as [Obsolete] to discourage use in favor of casting to `<Type>` or `Get<Type>()` (both throw) or preferably `bool TryGetValue(out <Type> value)` calls. The same applies to the `Value` property of `DataValue`. The APIs perform any required conversion between `BuiltInType.Int32` and `BuiltInType.Enumeration` as well as arrays of `BuiltInType.Byte` and `BuiltInType.ByteString`. This also applies to the `Body` property of `ExtensionObject`. Here prefer the use of `TryGetValue<T>` and `TryGetBinary, TryGetJson, TryGetXml`.

Creating a `Variant` or `ExtensionObject` via the constructor taking a `object` parameter is also marked [Obsolete] to encourage using type safe API to create a Variant (and thus not storing the wrong value in the inner `object` variable that cannot be converted out again or makes the Variant a null variant unexpectedly).

In some cases it is desirable to gain access to what was returned from the now obsoleted `Value` property. To make the fact that the returned value is likely boxed, the new API is named `AsBoxedObject()`. While the Variant has conversion operators from all supported types and corresponding `From(<Type> value)` APIs, it is sometimes necessary to convert from `System.Object`. Note that `AsBoxedObject()` does not return .net array types but `ArrayOf<T>`, and `ByteString` for - yes - ByteString. `Value` property converts to the old style type expectations.

To perform conversion from `<T>` to a Variant, helper methods are available in `VariantHelper` static class. These helper methods are split into ones that use reflection and ones that do not. Overall, use of these helper methods is not recommended in favor of switching on the type information in the Variant.

> `DateTimeUtc` and `EnumValue` are always stored unboxed inside a Variant. However, converting a enum (`System.Enum`) to an EnumValue requires boxing on .net standard and .net framework.
> All other built in value types (`ExtensionObject`, `NodeId`, `QualifiedName`, `LocalizedText`, `Uuid`, etc.) are > 8 bytes in size and are therefore boxed when stored inside a Variant.
> Future improvements will make certain types like `ArrayOf` be stored *spliced* inside the Variant (where the array pointer is stored in the object, and length/offset inside the union).

##### Replacement of all use of System.Object in generated code and API

`Variant` is now the type reflecting the OPC UA Variant type in all API. That means all generated API now uses Variant instead of `System.Object` and all `Value` Properties are `Variant` too.  This provides type safety and removes the need for Reflection via `GetType()` when the underlying type already is `Variant`.

**System.Object and Variant comparable operations:**

- *Casting*: Casting from Variant to built in system type "will just work" the same way as casting from the object, e.g. `object a; uint b = (uint)a;` is equivalent to `Variant a; uint b = (uint)a;`. Both throw `InvalidCastException` if the cast is not possible.
- *Pattern matching*: If you use is pattern matching use the new `TryGetValue/TryGetStructure` calls. If you cast using as, use the same or if you prefer a default value in case the Variant has a different type, the `Get<BuiltInType>` or `GetStructure<T>` or equivalent array returning methods ending in `Array`. They do not throw, but return the default value.
- *Reflection*: Use `TypeInfo` property on Variant to obtain metadata for for example switching.
- *Conversion*: Previously TypeInfo had support to Cast an object aligned with Variant behavior. These API have been removed in favor of the `ConvertTo[<]BuiltInType]()` members or `ConvertTo(BuiltInType target)`. NOTE: Under the hood `IConvertible` is used, which means integer values are boxed.

To migrate, perform the following general replacements in your code:

- If you are setting the `Value` property of Variant, change the code to create a `new Variant` with the value via constructor or `Variant.From` or by casting to `Variant`.
- Generally replace all `IList<object>` with `IList<Variant>`
- Generally replace all `ref object` with `ref Variant`.
- In addition: for all callbacks registered in `BaseVariableState` change the callback signature to use `Variant` instead of `object` and `Variant[]` instead of `object[]`.
- For all remaining `object[]` instances, replace with `ArrayOf<Variant>` or `IList<Variant>` judiciously and depending on context.
- Keep all *casts* from **Variant** (not from its Value property) to the concrete type if you intend to preserve throw behavior. For any pattern matching (is/as) use `TryGetValue` if you need to check the result, or `Get<BuiltInType>` if you do not want to throw but are happy with the default value.

> IMPORTANT: Care must be taken to not accidentally box a `Variant` value into an `object`.  E.g. current code like `object f = state.Value` will not be flagged by the compiler but must be replaced with `Variant f = state.Value` to remain type safe. Here it is best to use `var` for locals which requires no code changes.

**Remaining work:**

- Assignments to Variants and casting from variant to type should be dealt with via implicit conversion except for Structures. Here change code from `Value = <structure>` to `Value = Variant.FromStructure(<structure>)` and `<structure> = Value` to `Value.TryGetStructure(out <structure>)`.
- Any pattern matching conversion used must be replaced with the TryGetValue/TryGetStructure pattern of Variant for checked conversions, e.g. `a = Value as uint?` must be replaced with `Value.TryGetValue(out uint a)` which most often produces more concise code and avoids the check for nullable result of the conversion. The same applies to `is` matching.
- For Variable and VariableType node state classes that provide a narrowed "Value" via generic `<T>` any access to `T Value` incurs a heavy type check.  It is recommended to use `WrappedValue` instead when possible for assignment and access.
- While most assignments work implicitly, use `TypeInfo.GetDefaultVariantValue` instead of `TypeInfo.GetDefaultValue` to initialize a variant value to a default that is `!= Variant.Null`.

#### DataValue

`DataValue` has been converted from a reference type (class) to a `readonly struct` to relieve GC pressure on hot subscription/encoder paths. The semantics are aligned with the other immutable built-in types (`NodeId`, `ExtensionObject`, etc.).

**What changed:**

1. **You cannot compare a `DataValue` against `null` anymore.** Use the `DataValue.IsNull` instance property, or the `DataValue.Null` static field (equivalent to `default(DataValue)`).
2. **Property setters were removed.** Use the new `With<Property>()` fluent mutators — each returns a *new* `DataValue` with that field replaced, e.g. `dv = dv.WithStatus(StatusCodes.BadInternalError)`. Chaining a `default` value with `With*` calls is folded by the JIT into a single constructor call.
3. **`IsGood` / `IsBad` / `IsUncertain` / `IsNotGood` / `IsNotBad` / `IsNotUncertain` are instance properties** on `DataValue` now. The previous static `DataValue.IsGood(dv)` style helpers were removed; they remain as `[Obsolete]` extension methods on `DataValueExtensions` so existing source still compiles, but new code should prefer `dv.IsGood`.
4. **`Nullable<DataValue>` (`DataValue?`) is redundant** and should be removed from your code. Because `DataValue` is itself nullable via `IsNull`, wrapping it in `Nullable<>` doubles the storage and adds boxing on the `HasValue`/`Value` access pattern. Replace `DataValue?` fields/parameters/locals with `DataValue` and use `dv.IsNull` / `DataValue.Null` instead of `dv == null` / `null`. The compiler will not flag this automatically.
5. **`IsNull` has sentinel semantics**: `default(DataValue)` reports `IsNull == true`, while any *explicitly* constructed `DataValue` (e.g. `new DataValue(Variant.Null)` with all-default fields) reports `IsNull == false`. This preserves the distinction between "absent" and "explicitly empty" on the wire — the binary, JSON and XML encoders now round-trip both forms without conflation. If you currently rely on "all fields are at default" semantics, replace your check with explicit field comparisons instead of `IsNull`.
6. **Decoders use the sentinel.** `IDecoder.ReadDataValue` (Binary, Xml, Json) returns `DataValue.Null` when the field is absent (or, for the binary encoder, when the encoding byte is `0`), allowing callers to distinguish "missing" from "present but empty".
7. **Prefer `in DataValue` for synchronous method parameters.** The struct is large (~64 bytes after the IsNull sentinel) and copying it on every call is wasteful. The server `IDataChangeMonitoredItem.QueueValue(in DataValue, ...)` API has been updated accordingly. Async methods cannot use `in`/`ref` parameters, so leave those by-value.
8. **`object? GetValue(Type)` and `T? GetValueOrDefault<T>()` are now `[Obsolete]`.** Use `WrappedValue.TryGetValue<T>(out T value)` or `WrappedValue.TryGetStructure<T>(out T value)` for type-safe extraction without throwing. `GetValue<T>(T defaultValue)` remains supported.
9. **`DataValue.FromStatusCode(StatusCode)` and `FromStatusCode(StatusCode, DateTimeUtc serverTimestamp)`** are the preferred way to construct a `DataValue` that conveys only a status. The `DataValue(StatusCode)` and `DataValue(StatusCode, DateTimeUtc)` constructors are `[Obsolete]` because they conflict with overload resolution against the numeric `Variant` types (`uint`/`int`/`StatusCode` all implicitly convert in different directions).

**Change code as follows:**

```csharp
// Before
DataValue dv = ReadValue();
if (dv == null) { ... }
dv.Value = 42;                                     // mutating setter — gone
dv.StatusCode = StatusCodes.Bad;                   // mutating setter — gone
if (DataValue.IsGood(dv)) { ... }                  // static helper — moved to Obsolete extension

// After
DataValue dv = ReadValue();
if (dv.IsNull) { ... }
dv = dv.WithWrappedValue(new Variant(42));         // returns a new DataValue
dv = dv.WithStatus(StatusCodes.Bad);
if (dv.IsGood) { ... }                             // instance property

// And to convey only a status (no value):
DataValue bad = DataValue.FromStatusCode(StatusCodes.BadInternalError);

// Drop redundant Nullable<DataValue>:
//   private DataValue? m_lastValue;       ->  private DataValue m_lastValue;
//   m_lastValue = null;                   ->  m_lastValue = DataValue.Null;
//   if (m_lastValue != null) { ... }      ->  if (!m_lastValue.IsNull) { ... }
//   m_lastValue.Value.StatusCode          ->  m_lastValue.StatusCode

// Pass by 'in' on hot paths:
public void QueueValue(in DataValue value, ServiceResult? error) { ... }
```

Async methods cannot accept `in` / `ref` parameters. When an async caller needs to forward a `DataValue` into an `in` API, copy it to a local first so the local owns the storage that gets captured by the state machine:

```csharp
// In async code, copy DataValue to a local before passing in.
async Task EnqueueAsync(DataValue dv)
{
    var snapshot = dv;
    queue.QueueValue(in snapshot, error: default);
    await Task.Yield();
}
```

#### XmlElement

Previously the `XmlElement` built in type was represented by the `System.Xml.XmlElement` system type. While officially a deprecated, there is now a value type `XmlElement` that merely wraps a string but provides conversion operations to `System.Xml.XmlElement` and `System.Linq.Xml.XNode` as well as validation and equality/hashing operations. Normally you just need to remove `using System.Xml` and code continues working as is.  If you need to have access to the `System.Xml.XmlElement` cast or use the `ToXmlElement` method.

> `XmlElement` types are compared via a normalized version of the XML `string` contained, which removes all whitespace before comparing. This can result in some ambiguity, but operates well enough for test operations. For complete equality, cast to XNode and use `DeepEquals`.

#### EnumValue to represent the enumeration built in type

`EnumValue` bundles a symbol with a integer value (same as `StatusCode`). While most API works with standard .net `enum` types, these do not work in scenarios where the enum value is the result of a `EnumDefinition`. For these
cases the `EnumValue` overloads provide a similar experience to using `enum`. In addition, the `EnumValue` type
allows more efficient storage inside `Variant`. For this case, `Variant(Enum)` constructor, `IEquatable<Enum>`, and `operator ==/!=(Variant, Enum)` do not exist anymore.

Change code as follows:

```csharp
// Before
Variant v = new Variant(MyEnum.Value);
// After
Variant v = EnumValue.From(MyEnum.Value); // or
Variant v = new Variant(EnumValue.From(MyEnum.Value)); // or
Variant v = Variant.From(MyEnum.Value);
```

#### ExtensionObject array helpers changed

`ExtensionObject.ToArray(object, Type)` and `ToList<T>(object)` removed. Use `extensionObjects.GetStructuresOf<T>()` or `ExtensionObject.ToArray<T>(ArrayOf<ExtensionObject>)`.

#### Other Data Types

All generated data types implementing `IEncodeable` are now equality comparable using `==` and `!=` and implement `IEquatable<T>`. Equality defaults to the `IsEqual` implementation of the `IEncodeable` interface. In addition `ToString()` and `GetHashCode()` are implemented making all generated data types effectively equivalent to `record` classes with the exception of supporting `with` expressions.

**Change code as follows:**

No changes are required, however there can be subtle bugs exposed, e.g.:

- When comparing data type instances for reference equality, use `ReferenceEquals`, instead of `==` or `!=` operators. You can use the `RefEqualityComparer<T>` helper when creating Dictionaries that use the type as key and require reference equality semantics for it.
- When testing for `null`, use `is null` for more performant code.

#### Obsoleted APIs and replacements

- `NodeId(string text)` -> `NodeId.Parse(string)`
- `NodeId(object identifier, ushort namespaceIndex)` -> typed constructors: `new NodeId(uint, ushort)`, `new NodeId(Guid, ushort)`, `new NodeId(string, ushort)`, `new NodeId(ByteString, ushort)`
- `NodeId.Create(object identifier, string namespaceUri, NamespaceTable namespaceTable)` -> typed overloads: `NodeId.Create(string|uint|Guid|ByteString, string, NamespaceTable)`
- `NodeId.Identifier` -> `TryGetValue(out uint|string|Guid|ByteString)` or `IdentifierAsString`
- `NodeId.SetNamespaceIndex(ushort)` -> `WithNamespaceIndex(ushort)` (store the return value)
- `NodeId.SetIdentifier(IdType, object)` -> `WithIdentifier(uint|string|Guid|ByteString)` or typed constructors
- `ExpandedNodeId(string text)` -> `ExpandedNodeId.Parse(string)`
- `ExpandedNodeId(object identifier, ushort namespaceIndex, string namespaceUri, uint serverIndex)` -> typed constructors: `new ExpandedNodeId(uint|Guid|string|ByteString, ushort, string, uint)`
- `ExpandedNodeId.Identifier` -> `TryGetValue(out uint|string|Guid|ByteString)` or `IdentifierAsString`
- `NodeIdExtensions.IsNull(NodeId)` -> `NodeId.IsNull`
- `NodeIdExtensions.IsNull(ExpandedNodeId)` -> `ExpandedNodeId.IsNull`
- `QualifiedNameExtensions.IsNull(QualifiedName)` -> `QualifiedName.IsNull`
- `LocalizedTextExtensions.IsNullOrEmpty(LocalizedText)` -> `LocalizedText.IsNullOrEmpty`
- `QualifiedName.IsNull(QualifiedName)` -> use `QualifiedName.IsNull`
- `ExtensionObject.IsNull(ExtensionObject)` -> use `ExtensionObject.IsNull`
- Implicit cast from `string` or `byte[]` to `NodeId`/`ExpandedNodeId` -> use explicit cast or `From()` API
- Implicit cast from `string` to `LocalizedText`/`QualifiedName` -> use explicit cast or `From()` API
- `Format` and `ToString` APIs return `string.Empty` instead of `null` for `NodeId`, `QualifiedName`, `ExpandedNodeId`, `LocalizedText` to prevent NullReferenceExceptions
- `Matrix` class -> use `MatrixOf<T>`
- `<T>Collection` classes -> use `ArrayOf<T>` or `List<T>`
- `new Variant(object)` -> use `Variant.From(T)`
- `Variant.Value` -> use `Variant.TryGetValue`, cast, or `AsBoxedObject` if absolutely necessary.
- `DataValue.GetValue`, `DataValue.GetValueOrDefault`, ,`DataValue.Value` -> use `DataValue.WrappedValue` and the new API on Variant (e.g. `Get[Type]`,  `TryGetValue`)
- `new DataValue(StatusCode)` and `new DataValue(StatusCode, DateTimeUtc)` -> use `DataValue.FromStatusCode(StatusCode)` and `DataValue.FromStatusCode(StatusCode, DateTimeUtc)`. The constructors suffered from a C# overload resolution bug where `new DataValue(42)` silently resolved to `DataValue(StatusCode)` instead of `DataValue(Variant)`, losing the value.
- `SessionManager.ImpersonateUser` -> register `IUserTokenAuthenticator` instances via `services.AddIdentityAuthenticator<T>()` or `server.CurrentInstance.IdentityRegistry.Register(...)`. The event remains functional as a fallback, but is now `[Obsolete]`; the in-box ReferenceServer, GlobalDiscoverySampleServer, and ConsoleReferenceClient samples use the provider model.

#### APIs permanently removed

- All `<Type>Collection` classes, e.g. Int32Collection or ArgumentCollection -> use `List<Type>` or `ArrayOf<T>`
- `ICloneable`/`Clone()`/`MemberwiseClone()` on the immutable built-in types -> use assignment for copies
- Creating `NodeId` or `ExpandedNodeId` using `byte[]` -> use `ByteString` and type safe constructor.
- Setters removed from immutable types:
  - `QualifiedName.Name`/`QualifiedName.NamespaceIndex` -> `WithName(string)`/`WithNamespaceIndex(ushort)`
  - `LocalizedText.Translations`/`LocalizedText.TranslationInfo` -> `WithTranslations(...)`/`WithTranslationInfo(...)`
  - `ExtensionObject.Body`/`ExtensionObject.TypeId` -> constructors and `WithTypeId(...)`
  - `NodeId.NamespaceIndex`/`NodeId.IdType`/`NodeId.Identifier` setters -> use constructors or `WithIdentifier(...)`
- Implicit cast operator of type string to NodeId/ExpandedNodeId -> use Parse/TryParse
- `WriteGuid(string, Guid)` -> use `WriteGuid(string, Uuid)` and - `WriteGuidArray(string, IList<Guid>)` -> use `WriteGuidArray(string, ArrayOf<Uuid>)`
- `WriteDateTime(string, DateTime)` -> use `WriteDateTime(string, DateTimeUtc)` and - `WriteDateTimeArray(string, IList<DateTime>)` -> use `WriteDateTimeArray(string, ArrayOf<DateTimeUtc>)`
- `WriteByteString(string, byte[])` -> use `WriteByteString(string, ByteString)` and - `WriteByteStringArray(string, IList<byte[]>)` -> use `WriteByteStringArray(string, ArrayOf<ByteString>)`
- new `Variant(Guid)` -> use `Variant.From(Uuid)` or `new Variant(Uuid)`
- new `Variant(DateTime)` -> use `Variant.From(DateTimeUtc)` or `new Variant(DateTimeUtc)`
- new `Variant(byte[])` -> use `Variant.From(ByteString)` or `new Variant(ByteString)` or `Variant.From(ArrayOf<byte>)` or `new Variant(ArrayOf<byte>)`
- Session `Call/CallAsync(param object[])` -> use `Call/CallAsync(param Variant[])`
- `byte[]` as ByteString -> use `ByteString`
- `new DataValue(DataValue)` copy constructor -> use `DataValue.Copy()` instance method or `Clone()`

### Encodeable Factory and Complex Type System

#### IType hierarchy

New type abstraction layer: `IType` (base) with `IBuiltInType`, `IEnumeratedType` (new), and `IEncodeableType` (now extends `IType`). Many APIs return `IType` instead of `Type`:

- `TypeInfo.GetSystemType(ExpandedNodeId, IEncodeableTypeLookup)` → returns `IType` (was `Type`). Use `.Type` property to get the CLR `Type`.
- The overload `TypeInfo.GetSystemType(BuiltInType, int valueRank)` was removed.

#### IEncodeableTypeLookup changes

- `TryGetEncodeableType<T>()` removed.
- Added: `TryGetEnumeratedType(ExpandedNodeId, out IEnumeratedType?)`, `TryGetType(XmlQualifiedName, out IType?)`.

#### IEncodeableFactoryBuilder changes

- `AddEncodeableType(ExpandedNodeId, Type)` → renamed to `AddType(ExpandedNodeId, Type)`.
- Added: `AddEnumeratedType(IEnumeratedType)`, `AddEnumeratedType(ExpandedNodeId, IEnumeratedType)`.
- `AddEncodeableType(Type)` and `AddEncodeableTypes(Assembly)` now have AOT annotations (`[DynamicallyAccessedMembers]`, `[RequiresUnreferencedCode]`).

#### EncodeableFactory.GlobalFactory removed

The `[Obsolete]` static `EncodeableFactory.GlobalFactory` was removed. `EncodeableFactory.Create()` renamed to `Fork()`. Use `ServiceMessageContext.Factory` instead.

#### ComplexTypes moved to Opc.Ua.Client assembly

Core complex type interfaces and default (non-reflection-emit) implementations moved from `Opc.Ua.Client.ComplexTypes` to `Libraries/Opc.Ua.Client/ComplexTypes/`.
Namespace remains `Opc.Ua.Client.ComplexTypes`. If you used the default constructors without specifying the builder, and want to use the Reflection.Emit based type builders,
you need to change your code to call `ComplexTypeSystem.Create(...)` instead of `new ComplexTypeSystem(...)` which now uses the new default builder not supporting Reflection.Emit.

#### OptionSet DataType support

Concrete Structure-backed sub-types of the abstract `OptionSet` DataType (`i=12755`) are now automatically registered by the default `ComplexTypeSystem` builder with a new runtime class `Opc.Ua.Encoders.OptionSet` (in `Stack/Opc.Ua.Types`). Bit-field metadata is resolved from `DataTypeDefinition` (`EnumDefinition`) or, as a fallback, synthesized from the `OptionSetValues` property (`LocalizedText[]`).

Impact on existing code:

- **Source-breaking for custom `IComplexTypeBuilder` implementations**: a new member `AddOptionSetType(QualifiedName, ExpandedNodeId, ExpandedNodeId, ExpandedNodeId, ExpandedNodeId, EnumDefinition)` was added to `IComplexTypeBuilder`. Custom implementations must provide it.
- The Reflection.Emit builder in `Opc.Ua.Client.ComplexTypes` throws `NotSupportedException` from `AddOptionSetType`; callers relying on the Reflection.Emit path for OptionSet sub-types should switch to the default builder (`new ComplexTypeSystem(session)`).
- No wire-format changes: encoders/decoders continue to route through `IEncodeableFactory` → `IEncodeableType.CreateInstance`, which now yields `Opc.Ua.Encoders.OptionSet` for registered sub-types.
- UInteger-backed OptionSet DataTypes remain treated as their underlying unsigned integer in a `Variant` (unchanged).

### Encoders and Decoders

The `IEncoder` and `IDecoder` interfaces have changed to use `ArrayOf<T>` instead of Collection and `System.Array`. Also generic versions of `ReadEncodeable`/`WriteEncodeable` and `ReadEnumerated`/`WriteEnumerated` were added with the ones taking a `System.Type` paramter removed. There are 2 versions of `ReadEncodeable<T>` and `WriteEncodeable<T>`, one with a `new()` constraint bypassing `EncodeableFactory` lookups, and one with a `ExpandedNodeId` used to look up the concrete type and allowing to use `IEncodeable` as `T` constraint.

Furthermore, `ReadArray`/`WriteArray` methods have been removed. A new `ReadVariantValue` and `WriteVariantValue` method has been added to write "only" the content (Value) of a Variant, or read the value using `TypeInfo` information. Neither supports `DiagnosticInfo` but also supports writing and reading scalar values. The return type is Variant. To read a `TypeInfo.Scalars.Variant` use ReadVariant instead because a Variant cannot contain a scalar Variant.

In addition to the generic Write/ReadEnumerated, the non-generic `EnumValue` variants were also added.

- `IEncoder`: `WriteEnumerated(string, EnumValue)`, `WriteEnumeratedArray(string, ArrayOf<EnumValue>)`
- `IDecoder`: `ReadEnumerated(string)` returning `EnumValue`, `ReadEnumeratedArray(string)` returning `ArrayOf<EnumValue>`

Custom encoder/decoder implementations must adjust to comply with the new interfaces.

**Change code as follows:**

- Change all `ReadEncodeable`/`WriteEncodeable` calls to use the type as part of the generic expression. E.g. `ReadEncodeable("field", typeof(T))` to `ReadEncodeable<T>("field")` and `WriteEncodeable("field", value, typeof(T))` to `WriteEncodeable("field", value)`. If value is a type that cannot be created using a parameterless constructor, pass the type id as last argument.
- Change all `ReadEnumerated` calls to use the enumeration type as part of the generic expression. E.g. `ReadEnumerated("field", typeof(T))` to `ReadEnumerated<T>("field")`.
- Change calls to `ReadArray`/`WriteArray` to use `ReadVariantValue` and `WriteVariantValue` and extract the value from the returned `Variant` based on the type you intended to read. A good example can be found in `BaseComplexType` `EncodeProperty` and `DecodeProperty`.

### Node States

#### Generics and Typed BaseVariableState and BaseVariableTypeState

With the changes to Variant, the generic node state classes reflecting the inner value of the variant "value" have been changed to not rely on "casting" from object to T. The conversion is "baked in" when creating an instance of a typed state using a "builder" struct. Whether the value is scalar, array or matrix is irrelevant to which builder to use. There are 3 situations and the respective builder struct to use:

1. T is a built in type -> use `VariantBuilder`
2. T is a instance of `IEncodeable` (a complex structure) -> Use `StructureBuilder<T>` where T is the name of the structure.
3. T is an instance of Enum (an enumeration) -> Use `EnumBuilder<T>` where T is the name fo the enumeration type.

E.g. to create an instance of a `PropertyState<T>` where T is `ArrayOf<ExtensionObject>` use

``` csharp
    var state = new PropertyState<ArrayOf<ExtensionObject>>.Implementation<VariantBuilder>(parent)
    // or
    var state = PropertyState<ArrayOf<ExtensionObject>>.With<VariantBuilder>(parent)
```

To create an instance of a `PropertyState<T>` where T is `Argument` (an IEncodeable type) use

``` csharp
    var state = new PropertyState<Argument>.Implementation<StructureBuilder<Argument>>(parent)
    // or
    var state = PropertyState<Argument>.With<StructureBuilder<Argument>>(parent)
```

To create an instance of a `PropertyState<T>` where T is `MatrixOf<ComplexType>` (an IEncodeable type) use

``` csharp
    var state = new PropertyState<MatrixOf<ComplexType>>.Implementation<StructureBuilder<ComplexType>>(parent)
    // or
    var state = PropertyState<MatrixOf<ComplexType>>.With<StructureBuilder<ComplexType>>(parent)
```

Note: While this looks clunky, it does not use reflection and comes with 0 allocation including any allocations for `Func` or `Action` delegates and works around .net limitations regarding overload resolution for generic arguments (which also required the use of `FromStructure` or `FromEnumeration` on the Variant type instead of using `From`). In future versions it is possible the source generator could generate away some of the redundancies in the above expressions.

#### Predefined node processing

Filling the predefined node state list is now generated as source code.  This means the predefined Variable and Object instance states are the generated classes, not the root node states. This has an
impact on the AddBehaviorToPredefinedNode implementations which should use the received node state as "activeNode" and attach functionality to it instead of creating a active node.

Example guidance (mirrors BoilerNodeManager): the node passed to `AddBehaviorToPredefinedNode` is already the generated instance state, so attach behavior directly to it instead of creating a new state. This ensures the predefined list stays consistent and the generated type-specific fields are available.

``` csharp
    protected override void AddBehaviorToPredefinedNode(
        ISystemContext context,
        NodeState node)
    {
        if (node is BoilerTypeState boiler)
        {
            var activeNode = boiler;
            activeNode.Temperature.OnSimpleWriteValue = OnTemperatureWrite;
            activeNode.FlowRate.OnSimpleWriteValue = OnFlowRateWrite;
        }

        // Add callbacks to the node here if necessary
        // If not needed you do not need to implement this call at all.
    }
```

See [NodeStates](./../Stack/Opc.Ua.Types/State/readme.md) document for more information.

#### NodeState Cloning and Lifecycle

##### Node state does not implement IDisposable anymore.

Node states do not manage resources, they access resources. Therefore the management of resources must be done in a node manager.
If you are overriding Dispose() on a NodeState to manage the node state, make the method public instead of protected, and maintain
a list of node states on which you must call the Dispose() method when the Node Manager is disposed.  Better, associated node states
only via an identifier with a backend "system" that manages all state centrally and in your control.

##### Clone() replaced with CreateCopy()

`NodeState.Clone()` is now a concrete method that calls `CreateCopy()` + `CopyTo()`. The new `protected abstract NodeState CreateCopy()` must be overridden by all direct NodeState subclasses.

```csharp
// Before
public override object Clone()
{
    var clone = new MyNodeState(Parent);
    CopyTo(clone);
    return clone;
}

// After
protected override NodeState CreateCopy()
{
    return new MyNodeState(Parent);
}
```

If you had custom deep-copy logic beyond what `CopyTo()` does, override `CopyTo()` instead.

##### BaseVariableState Read/Write helpers removed

The `protected ServiceResult Read(object, ref object)` and `protected object Write(object)` methods were removed.
Use the `CopyPolicy` property or the new `CopyOnWrite` bool directly with `CoreUtils.Clone()` for copy-on-read/write semantics.

##### OnAfterCreate gains CancellationToken

`OnAfterCreate(ISystemContext, NodeState)` now has an optional `CancellationToken ct = default` parameter.

> **⚠ Silent regression.** Source-compatible, but **binary-incompatible**. Pre-compiled assemblies whose overrides still target the old `OnAfterCreate(ISystemContext, NodeState)` signature will silently no-op at runtime against 2.0 - the CLR resolves virtual overrides by exact signature, finds no match, and falls back to the base implementation. **No runtime exception is thrown** to alert the developer. The only fix is to **recompile** the consuming assembly against 2.0 so the override binds to the new three-argument signature.

```csharp
protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
{
    base.OnAfterCreate(context, node, ct);
}
```

#### INodeManager3 - new role-permission and method-resolution hooks

2.0 introduces `INodeManager3`, an extension of `INodeManager2` that surfaces explicit hooks for per-role permission evaluation and for resolving the target of a `Call` request. `CustomNodeManager2` implements the new members with safe defaults that mirror the previous behavior, so node managers that already derive from `CustomNodeManager2` need no changes.

Custom node managers that implement `INodeManager` / `INodeManager2` **directly** (not via `CustomNodeManager2`) silently lose the new behavior: the server probes for `INodeManager3` at the call site, and node managers that do not implement it fall through to the legacy code path. This is not a build break - it is a silent feature-availability regression. Either derive from `CustomNodeManager2` or implement `INodeManager3` explicitly to participate in role-permission evaluation and the new method-resolution contract.

### User Identity Token Handlers

**Breaking Change**: Identity tokens no longer perform cryptographic
operations directly. The handler pattern introduced earlier is now
**fully asynchronous** and **non-disposable**, and the
`Certificate`-taking ctors of `UserIdentity` and
`X509IdentityTokenHandler` have been removed in favour of a
`CertificateIdentifier` + `ICertificateProvider` model that resolves
the private-key cert on demand.

**Before**:

```csharp
    var token = new X509IdentityToken();
    using var handler = token.AsTokenHandler();
    handler.Encrypt(certificate, nonce, securityPolicy, context);
    handler.Decrypt(certificate, nonce, securityPolicy, context);
    var signature = handler.Sign(data, securityPolicy);
    bool isValid = handler.Verify(data, signature, securityPolicy);

    using var userIdentity = new UserIdentity(certificate);   // legacy ctor
```

**After**:

```csharp
    var token = new X509IdentityToken();
    var handler = token.AsTokenHandler();                      // not IDisposable
    await handler.EncryptAsync(certificate, nonce, securityPolicy, context, ct: ct);
    await handler.DecryptAsync(certificate, nonce, securityPolicy, context, ct: ct);
    SignatureData signature = await handler.SignAsync(data, securityPolicy, ct);
    bool isValid = await handler.VerifyAsync(data, signature, securityPolicy, ct);

    // New cert-based UserIdentity: identifier + cache-aware provider.
    UserIdentity userIdentity = await UserIdentity.CreateAsync(
        certificateIdentifier,
        passwordProvider,
        configuration.CertificateManager.CertificateProvider,
        ct);
```

**New interface shape**:

```csharp
    public interface IUserIdentityTokenHandler :
        ICloneable, IEquatable<IUserIdentityTokenHandler>
    {
        UserIdentityToken Token { get; }
        string DisplayName { get; }
        UserTokenType TokenType { get; }

        void UpdatePolicy(UserTokenPolicy userTokenPolicy);

        ValueTask EncryptAsync(
            Certificate receiverCertificate, byte[] receiverNonce,
            string securityPolicyUri, IServiceMessageContext context,
            ..., CancellationToken ct = default);
        ValueTask DecryptAsync(
            Certificate certificate, Nonce receiverNonce,
            string securityPolicyUri, IServiceMessageContext context,
            ..., CancellationToken ct = default);
        ValueTask<SignatureData> SignAsync(
            byte[] dataToSign, string securityPolicyUri,
            CancellationToken ct = default);
        ValueTask<bool> VerifyAsync(
            byte[] dataToVerify, SignatureData signatureData,
            string securityPolicyUri, CancellationToken ct = default);
    }
```

**Migration required**:

| Removed | Replacement |
| ------- | ----------- |
| `IUserIdentityTokenHandler : IDisposable` | `IUserIdentityTokenHandler` (no `IDisposable`). Drop `using` on handler instances. Sensitive byte buffers (`UserNameIdentityTokenHandler.DecryptedPassword`, `IssuedIdentityTokenHandler.DecryptedTokenData`) are no longer cleared on disposal — secure-memory management is the secret store's responsibility (deferred to a future revision). |
| `UserIdentity : IDisposable`, `UserIdentity.Dispose()` | `UserIdentity` (no `IDisposable`). Drop `using` on `new UserIdentity(...)`. |
| `handler.Encrypt(...)` (sync) | `await handler.EncryptAsync(..., ct)` |
| `handler.Decrypt(...)` (sync) | `await handler.DecryptAsync(..., ct)` |
| `SignatureData handler.Sign(...)` (sync) | `await handler.SignAsync(..., ct)` |
| `bool handler.Verify(...)` (sync) | `await handler.VerifyAsync(..., ct)` |
| `new UserIdentity(Certificate)` (legacy ctor) | `await UserIdentity.CreateAsync(certificateIdentifier, passwordProvider, certificateProvider, ct)` — the new ctor stores the identifier; the cert is materialised on demand by the provider. |
| `new X509IdentityTokenHandler(Certificate)` | `new X509IdentityTokenHandler(CertificateIdentifier, ICertificatePasswordProvider, ICertificateProvider)` — handler holds no live Certificate; on `SignAsync` the provider's cache is consulted (`TryGetPrivateKeyCertificate`) then the store (`GetPrivateKeyCertificateAsync`). |
| `[Obsolete] new UserIdentity(CertificateIdentifier, CertificatePasswordProvider)` | `await UserIdentity.CreateAsync(certificateIdentifier, passwordProvider, certificateProvider, ct)` — the obsolete ctor blocked on async; the new factory does not pre-resolve. |
| `await UserIdentity.CreateAsync(certId, passwordProvider, telemetry, ct)` | `await UserIdentity.CreateAsync(certId, passwordProvider, certificateProvider, ct)` — `ICertificateProvider` (typically `configuration.CertificateManager.CertificateProvider`) replaces the telemetry-only argument list. |

**Available token handlers** (all non-disposable):
   - `AnonymousIdentityTokenHandler`
   - `UserNameIdentityTokenHandler`
   - `X509IdentityTokenHandler`
   - `IssuedIdentityTokenHandler`

**Note on secure-memory management**: with `IDisposable` gone, the
sync `Array.Clear` of decrypted password / issued-token bytes that
used to happen in `Dispose()` no longer fires. Bytes live in plain
fields until GC. A follow-up revision will route inbound decrypted
secrets through the new `ISecretStore` abstraction (see *Secrets*
below) so secure clearing becomes the store's responsibility, with no
public surface change.

### User Identity Providers

The identity-provider redesign is a source-level migration only. The OPC UA
wire token types and `ActivateSession` service behavior are unchanged, so
servers and clients can roll forward independently. Obsolete members remain
functional while you migrate to the provider model.

| Obsolete API | Replacement |
|---|---|
| `ISessionManager.ImpersonateUser` | Implement `IUserTokenAuthenticator` and register it with `services.AddIdentityAuthenticator<T>()` or `server.CurrentInstance.IdentityRegistry.Register(...)`. |
| `SessionManager.ImpersonateUser` | Same replacement; the event remains a fallback after the registry declines a token. SelfAdmin elevation logic should move to `IIdentityAugmenter`. |
| SelfAdmin logic in an `ImpersonateUser` subscriber | Implement `IIdentityAugmenter` and register it with `services.AddIdentityAugmenter<T>()` or `IdentityRegistry.RegisterAugmenter(...)`. GDS hosts can use `AddGdsApplicationSelfAdminProvider()`. |
| `ManagedSessionOptions.Identity` | Set `ManagedSessionOptions.IdentityProvider` so long-lived sessions can reacquire expiring identities. |
| `AuthorizationServiceClient.RequestAccessTokenAsync` | Use `StartRequestTokenAsync` followed by `FinishRequestTokenAsync`. |
| `Opc.Ua.Gds.Server.IAccessTokenProvider.RequestAccessTokenAsync` | Implement `StartRequestTokenAsync` and `FinishRequestTokenAsync`; keep the legacy method as a compatibility shim if you serve v1.04 clients. |

- Custom `IAccessTokenProvider` implementations now have a default `EnableRefreshTokens = true`
  behavior on the in-memory provider. Implementers who do not support refresh tokens can override
  `RefreshTokenAsync` to throw `Bad_NotSupported` or set
  `AuthorizationServiceOptions.EnableRefreshTokens = false`.

#### `SessionManager.ImpersonateUser` → registry authenticators

Legacy event wiring:

```csharp
server.CurrentInstance.SessionManager.ImpersonateUser +=
    SessionManager_ImpersonateUser;

private void SessionManager_ImpersonateUser(
    Session session, ImpersonateEventArgs args)
{
    if (args.NewIdentity is UserNameIdentityToken token &&
        ValidatePassword(token.UserName, token.DecryptedPassword))
    {
        args.Identity = new UserIdentity(token);
    }
}
```

Modern authenticator plus dependency injection registration:

```csharp
public sealed class MyUserNameAuthenticator : IUserTokenAuthenticator
{
    public UserTokenType TokenType => UserTokenType.UserName;
    public string? IssuedTokenProfileUri => null;

    public ValueTask<AuthenticationResult> AuthenticateAsync(
        AuthenticationContext context, CancellationToken ct = default)
    {
        if (context.TokenHandler is not UserNameIdentityTokenHandler userName)
        {
            return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
        }

        return new ValueTask<AuthenticationResult>(
            ValidatePassword(userName.UserName, userName.DecryptedPassword)
                ? AuthenticationResult.Accept(new UserIdentity(userName))
                : AuthenticationResult.Reject(new ServiceResult(StatusCodes.BadUserAccessDenied)));
    }
}

services.AddOpcUa()
    .AddServer(o => o.ApplicationUri = "urn:example:server")
    .AddIdentityAuthenticator<MyUserNameAuthenticator>();

// Manual host alternative:
server.CurrentInstance.IdentityRegistry.Register(new MyUserNameAuthenticator());
```

Repeat the pattern per token type: `UserTokenType.UserName`,
`UserTokenType.Certificate`, `UserTokenType.IssuedToken` with
`IssuedTokenProfileUri = Profiles.JwtUserToken`, or a vendor profile such
as the experimental KeyCredential bridge.

- SelfAdmin elevation now runs through `IIdentityAugmenter` after an authenticator accepts. Register an
  augmenter via `services.AddIdentityAugmenter<T>()` or `IdentityRegistry.RegisterAugmenter(...)`.
- GDS hosts get `GdsApplicationSelfAdminProvider` automatically via `AddDefaultIdentityAuthenticators(...)`
  on the GDS builder — opt out with `DisableGdsApplicationSelfAdminProvider()` (see GDS docs).
- Legacy `ImpersonateUser` subscribers that only layered SelfAdmin should drop the subscription; the
  augmenter sees the secure-channel `ChannelCertificate` + `ChannelApplicationUri` through
  `AuthenticationContext`.

#### `ManagedSessionOptions.Identity` → `IdentityProvider`

Before, an eager identity was fixed for the lifetime of the managed session:

```csharp
var options = new ManagedSessionOptions
{
    Endpoint = endpoint,
    Identity = new UserIdentity("alice", passwordBytes)
};
```

After, use a lazy provider. `ManagedSession` refreshes by calling
`Session.UpdateIdentityAsync` before `provider.ExpiresAt` where possible:

```csharp
IClientIdentityProvider provider = new CompositeClientIdentityProvider(
    new UserNamePasswordIdentityProvider(
        "alice",
        secretRegistry,
        new SecretIdentifier("alice-password", "InMemory")),
    new IssuedTokenIdentityProvider(accessTokenProvider));

var options = new ManagedSessionOptions
{
    Endpoint = endpoint,
    IdentityProvider = provider
};
```

### Secrets — caller-supplied passwords go through a secret registry

A new low-level abstraction layer carries caller-supplied secrets
(currently the password held by `CertificatePasswordProvider`) without
forcing a `byte[] DecryptedPassword`-style field to live on the
identity object.

```csharp
public sealed record SecretIdentifier(string Name, string StoreType, string? StorePath = null);
public interface ISecret : IDisposable { ReadOnlySpan<byte> Bytes { get; } }
public interface ISecretStore { ISecret? TryGet(SecretIdentifier id); /* + async Get/Set/Remove */ }
public interface ISecretRegistry { void RegisterStore(ISecretStore store); /* + Get/TryGet */ }
```

The default `InMemorySecretStore` keeps bytes in a `ConcurrentDictionary`
keyed by `SecretIdentifier.Name`. Every `TryGet`/`GetAsync` returns a
fresh `ISecret` view; the receiver disposes it when done. The
implementation chooses what disposal does — no-op for `InMemorySecret`
in this revision, future stores (DPAPI, Kubernetes secret, Azure Key
Vault) can implement clear-on-dispose, lease-return, or watch-handle
release.

`CertificatePasswordProvider` is reimplemented over this registry.
**The existing public ctors stay BC** — they internally create a
per-instance `InMemorySecretStore` and register the password under an
opaque identifier:

```csharp
new CertificatePasswordProvider();                                  // empty
new CertificatePasswordProvider("password");                        // string
new CertificatePasswordProvider(passwordBytes, isUtf8String: true); // bytes
new CertificatePasswordProvider(passwordSpan);                      // ReadOnlySpan<char>

// New advanced ctor for callers who want to plug in a custom store:
new CertificatePasswordProvider(secretRegistry, secretIdentifier);
```

`ICertificatePasswordProvider.GetPassword(CertificateIdentifier)` still
returns `char[]` for backward compatibility — internally it resolves
the secret bytes from the registry and decodes UTF-8 on every call.

### Centralised certificate cache via `ICertificateProvider`

A new public `ICertificateProvider` interface exposes the existing
`CertificateCache` for resolving private-key certs on demand:

```csharp
public interface ICertificateProvider
{
    Certificate? TryGetPrivateKeyCertificate(string thumbprint);          // sync
    ValueTask<Certificate?> GetPrivateKeyCertificateAsync(
        CertificateIdentifier identifier,
        ICertificatePasswordProvider? passwordProvider = null,
        string? applicationUri = null,
        CancellationToken ct = default);
}
```

`CertificateManager` exposes one via the new `CertificateProvider`
property; `ICertificateManager` likewise. The provider follows the
**TryGet → async ValueTask** pattern: cache hits complete
synchronously without allocations; misses fall through to
`CertificateIdentifierResolver.LoadPrivateKeyAsync` and write the
loaded cert back into the cache.

Wire it through to the new `X509IdentityTokenHandler` /
`UserIdentity.CreateAsync` overloads:

```csharp
UserIdentity userIdentity = await UserIdentity.CreateAsync(
    certificateIdentifier,
    passwordProvider,
    configuration.CertificateManager.CertificateProvider,
    ct);
```



### Configuration

#### Data Contract Serializer support removed

Because **Data Contract serialization** is not AOT compliant and does not support trimming, all use of `DataContract` in the configuration has been removed. Instead, the source generator enables generating *IEncodeable* implementations using the `DataType` and `DataTypeField` attributes which are now consequently used for all configuration. Because the configuration is now `IEncodeable` the existing encoders and decoders (in particular the new `XmlParser` which parses Xml and allows out of order fields) compliant with Part 6 can be used to serialize and deserialize all configuration and configuration extensions.

> Generated Data types still support DataContract based serialization, however, consider this a deprecated feature.

All configuration DTO classes (`ApplicationConfiguration`, `ServerConfiguration`, `TraceConfiguration`, `TransportConfiguration`, `ServerSecurityPolicy`, `OAuth2ServerSettings`, `OAuth2Credential`, `GlobalDiscoveryServerConfiguration`, `CertificateGroupConfiguration`, `BrowserOptions`, etc.) migrated from `[DataContract]`/`[DataMember]` to source-generated `[DataType]`/`[DataTypeField]` attributes and are now `partial` classes.

- `ApplicationConfiguration.LoadWithNoValidation` uses `XmlParser`/`IEncodeable.Decode()`. Existing XML config files should remain loadable.
- Browser and session state persistence switched from XML to OPC UA Binary encoding. **Old persisted files cannot be loaded** — delete and re-save.
- `SecuredApplication` uses `SecuredApplicationEncoding` helpers instead of `DataContractSerializer`.

**Change code as follows:**

- Replace `[DataContract(Namespace = ...)]` with `[DataType(Namespace = ...)]` and `[DataMember(...)]` with `[DataTypeField(...)]` on custom configuration subtypes.
- Add the `partial` keyword to any subclass of these configuration types.
- Custom configuration extension types must implement `IEncodeable` (the `[DataType]` source generator handles this automatically for `partial` classes).
- Code using reflection to inspect `[DataContract]`/`[DataMember]` attributes must switch to `[DataType]`/`[DataTypeField]`.

#### Newtonsoft.Json removed from Opc.Ua.Core

`Newtonsoft.Json` is no longer a dependency of `Opc.Ua.Core`. Projects relying on its transitive availability must add an explicit reference:

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
```

#### ParseExtension/UpdateExtension signature changed

`ParseExtension<T>()` and `UpdateExtension<T>()` now require `T` to implement `IEncodeable`. New delegate-based overloads were added for custom decoding:

```csharp
// Generic overload (T must implement IEncodeable)
var config = configuration.ParseExtension<MyConfig>();

// Delegate overload for custom decoding
var config = configuration.ParseExtension<MyConfig>(
    new XmlQualifiedName("MyConfig", myNamespace),
    decoder => { var c = new MyConfig(); c.Decode(decoder); return c; });
```

#### ExtensionObject array helpers changed

`ExtensionObject.ToArray(object, Type)` and `ToList<T>(object)` removed. Use `extensionObjects.GetStructuresOf<T>()` or `ExtensionObject.ToArray<T>(ArrayOf<ExtensionObject>)`.

#### IJsonEncodeable interface removed

The `IJsonEncodeable` interface and the entire "Default JSON Encoding" infrastructure have been removed. OPC UA JSON encoding is handled by the `JsonEncoder`/`JsonDecoder` classes which do not require per-type encoding node IDs — those classes are unaffected by this change.

**Migration steps:**

1. Remove `IJsonEncodeable` from any custom class that implements it:

    ```diff
    - public class MyType : IEncodeable, IJsonEncodeable
    + public class MyType : IEncodeable
    ```

2. Remove the `JsonEncodingId` property from those classes:

    ```diff
    - public ExpandedNodeId JsonEncodingId => ...;
    ```

### Complex Types

#### ComplexTypes moved to Opc.Ua.Client assembly

Core complex type interfaces and default (non-reflection-emit) implementations moved from `Opc.Ua.Client.ComplexTypes` to `Libraries/Opc.Ua.Client/ComplexTypes/`.
Namespace remains `Opc.Ua.Client.ComplexTypes`. If you used the default constructors without specifying the builder, and want to use the Reflection.Emit based type builders,
you need to change your code to call `ComplexTypeSystem.Create(...)` instead of `new ComplexTypeSystem(...)` which now uses the new default builder not supporting Reflection.Emit.

#### OptionSet DataType support

Concrete Structure-backed sub-types of the abstract `OptionSet` DataType (`i=12755`) are now automatically registered by the default `ComplexTypeSystem` builder with a new runtime class `Opc.Ua.Encoders.OptionSet` (in `Stack/Opc.Ua.Types`). Bit-field metadata is resolved from `DataTypeDefinition` (`EnumDefinition`) or, as a fallback, synthesized from the `OptionSetValues` property (`LocalizedText[]`).

Impact on existing code:

- **Source-breaking for custom `IComplexTypeBuilder` implementations**: a new member `AddOptionSetType(QualifiedName, ExpandedNodeId, ExpandedNodeId, ExpandedNodeId, ExpandedNodeId, EnumDefinition)` was added to `IComplexTypeBuilder`. Custom implementations must provide it.
- The Reflection.Emit builder in `Opc.Ua.Client.ComplexTypes` throws `NotSupportedException` from `AddOptionSetType`; callers relying on the Reflection.Emit path for OptionSet sub-types should switch to the default builder (`new ComplexTypeSystem(session)`).
- No wire-format changes: encoders/decoders continue to route through `IEncodeableFactory` → `IEncodeableType.CreateInstance`, which now yields `Opc.Ua.Encoders.OptionSet` for registered sub-types.
- UInteger-backed OptionSet DataTypes remain treated as their underlying unsigned integer in a `Variant` (unchanged).

### Session and Browser State Persistence

**Breaking Change**: Persistence switched from `DataContractSerializer` XML to `IEncoder` and `IDecoder`. `BrowserState`, `SessionState`, `SessionOptions`, `SubscriptionState`, and `MonitoredItemState` are annotated with `[DataType]` and use the standard `Encode`/`Decode` methods generated by the source generator.

To register the state types with the encodeable factory:

```csharp
context.Factory.Builder.AddOpcUaClientDataTypes();
```

> The encoding format for session state has changed. Existing persisted session state files **cannot** be loaded by the new `SessionConfiguration.Create()` method. Handle restore failures and re-persist the new session state.

### Certificate Management

#### Certificate and CertificateCollection wrapper types

`X509Certificate2` and `X509Certificate2Collection` are no longer used directly in the public API. They are replaced by `Certificate` and `CertificateCollection` (in `Opc.Ua.Security.Certificates`).

**Migration steps:**

```csharp
// Before:
X509Certificate2 cert = new X509Certificate2(rawData);
X509Certificate2Collection certs = await store.Enumerate();

// After:
Certificate cert = new Certificate(rawData);
CertificateCollection certs = await store.EnumerateAsync();
```

`Certificate` implements reference counting. Call `AddRef()` before sharing a certificate across ownership boundaries, and `Dispose()` to release. The inner `X509Certificate2` is disposed when the last reference is released.

For .NET interop, use `certificate.AsX509Certificate2()` which returns a copy the caller must dispose. The internal `X509Certificate2` is accessible via the `internal X509` property for `InternalsVisibleTo` friends.

`CertificateBuilder.CreateForRSA()` and `CreateForECDsa()` now return `Certificate` instead of `X509Certificate2`.

#### CertificateManager and segregated interfaces

A new centralized `CertificateManager` replaces the scattered certificate handling across `CertificateValidator`, `CertificateIdentifier`, `CertificateTypesProvider`, and `CertificateFactory`. It is composed of focused interfaces:

| Interface | Purpose | Location |
|-----------|---------|----------|
| `ICertificateRegistry` | Read-only access to app certificates | `Opc.Ua` |
| `ICertificateTrustListManager` | Named trust-list management | `Opc.Ua` |
| `ICertificateValidatorEx` | Trust-list-scoped validation | `Opc.Ua` |
| `ICertificateLifecycle` | Change notifications + cert updates | `Opc.Ua` |
| `ICertificateFactory` | Stateless cert creation/parsing | `Opc.Ua.Security.Certificates` |
| `ICertificateIssuer` | CA signing + CRL revocation | `Opc.Ua.Security.Certificates` |
| `ICertificateStoreProvider` | Pluggable store backends | `Opc.Ua` |

The `CertificateManager` is automatically initialized by `ServerBase` and `ApplicationInstance` during startup. Access it via `ServerBase.CertificateManager` or `ApplicationInstance.CertificateManager`.

**Trust-lists are now named and extensible:**

```csharp
// Well-known: TrustListIdentifier.Peers, .Users, .Https, .Rejected
// Custom:
manager.RegisterTrustList(new TrustListIdentifier("MqttBrokers"),
    trustedStorePath: "...", issuerStorePath: "...");

// Validate against any trust-list
var result = await manager.ValidateAsync(cert, TrustListIdentifier.Users);
```

**Subscribe to certificate changes:**

```csharp
manager.CertificateChanges.Subscribe(observer);
```

See [CertificateManager.md](CertificateManager.md) for the full API reference and usage guide.

#### CertificateIdentifier is metadata-only

`CertificateIdentifier` no longer caches a `Certificate`, no longer implements `IDisposable`, and the cert-bearing constructors / instance methods have been removed. Use `CertificateIdentifierResolver` to materialize a `Certificate` from an identifier.

**Removed members:**

* `Certificate` get/set property and the cached `m_certificate` field.
* `IDisposable` declaration, `Dispose()`, `DisposeCertificate()`.
* Constructors `CertificateIdentifier(Certificate)`, `CertificateIdentifier(Certificate, CertificateValidationOptions)`, `CertificateIdentifier(byte[])`.
* Instance methods `FindAsync(...)`, `LoadPrivateKeyAsync(char[], ...)`, `LoadPrivateKeyExAsync(...)`, `OpenStore(...)`.
* `IOpenStore` interface declaration on `CertificateIdentifier`.

**`RawData`** is now backed by an explicit `byte[]` field. The setter still derives `SubjectName` / `Thumbprint` / `CertificateType` from the parsed raw bytes.

**`ICertificateRegistry.GetIssuersAsync`** now returns `IList<CertificateIssuerReference>` (a public sealed record with `Certificate Certificate, CertificateValidationOptions Options`) instead of `IList<CertificateIdentifier>`. Existing callers must update the list type and switch from `CertificateIdentifier.Certificate` to `CertificateIssuerReference.Certificate`.

**Migration patterns:**

| Before (legacy) | After |
|---|---|
| `var id = new CertificateIdentifier(cert);` | `var id = new CertificateIdentifier { Thumbprint = cert.Thumbprint, SubjectName = cert.Subject, CertificateType = CertificateIdentifier.GetCertificateType(cert) };` |
| `var id = new CertificateIdentifier(rawData);` | `var id = new CertificateIdentifier { RawData = rawData };` |
| `id.Certificate` (read) | `await CertificateIdentifierResolver.ResolveAsync(id, registry, needPrivateKey: false, applicationUri, telemetry, ct)` |
| `id.Certificate = cert;` | Drop the assignment. Cert lifecycle is owned by `CertificateManager` (use `ICertificateLifecycle.UpdateApplicationCertificateAsync`) or by a local variable. |
| `await id.FindAsync(true, applicationUri, telemetry, ct)` | `await CertificateIdentifierResolver.LoadPrivateKeyAsync(id, passwordProvider, applicationUri, telemetry, ct)` |
| `await id.LoadPrivateKeyExAsync(passwordProvider, applicationUri, telemetry, ct)` | `await CertificateIdentifierResolver.LoadPrivateKeyAsync(id, passwordProvider, applicationUri, telemetry, ct)` |
| `id.OpenStore(telemetry)` | `CertificateIdentifierResolver.OpenStore(id, telemetry)` |
| `using var id = new CertificateIdentifier(...);` | `var id = new CertificateIdentifier(...);` (no `using`) |
| `IList<CertificateIdentifier> issuers = ...; var cert = issuers[i].Certificate;` | `IList<CertificateIssuerReference> issuers = ...; var cert = issuers[i].Certificate;` |

See [CertificateManager.md](CertificateManager.md#migration-certificateidentifier-is-metadata-only) for the full migration walkthrough.

#### Obsoleted certificate APIs

The following APIs are marked `[Obsolete]` and will be removed in the next minor version. They remain
functional forwarders to the new design for binary-compatibility, but emit `CS0618` warnings when used.

| Obsolete API | Replacement |
|-------------|-------------|
| `CertificateFactory.Create(ReadOnlyMemory<byte>)` | `Certificate.FromRawData(ReadOnlyMemory<byte>)` or `DefaultCertificateFactory.Instance.CreateFromRawData(...)` |
| `CertificateFactory.CreateCertificate(string)` | `DefaultCertificateFactory.Instance.CreateCertificate(string)` |
| `CertificateFactory.CreateCertificate(string, string, string, ArrayOf<string>)` | `DefaultCertificateFactory.Instance.CreateApplicationCertificate(...)` |
| `CertificateFactory.CreateSigningRequest(...)` | `DefaultCertificateFactory.Instance.CreateSigningRequest(...)` |
| `CertificateFactory.RevokeCertificate(...)` | `DefaultCertificateIssuer.Instance.RevokeCertificates(...)` |
| `CertificateFactory.CreateCertificateWithPEMPrivateKey(...)` | `DefaultCertificateFactory.Instance.CreateWithPEMPrivateKey(...)` |
| `CertificateFactory.CreateCertificateWithPrivateKey(...)` | `DefaultCertificateFactory.Instance.CreateWithPrivateKey(...)` |
| `CertificateStoreIdentifier.RegisterCertificateStoreType(...)` | Register `ICertificateStoreProvider` via dependency injection or pass to the `CertificateManager` constructor |
| `CertificateValidator` (class) | `ICertificateManager` (composed of `ICertificateValidatorEx` for validation, `ICertificateRegistry` for app certs, `ICertificateTrustListManager` for trust lists, `ICertificateLifecycle` for change events). Construct via `CertificateManagerFactory.Create(securityConfiguration, telemetry, ...)` |
| `ICertificateValidator` (interface) | `ICertificateValidatorEx` from `ICertificateManager`. The new interface returns a structured `CertificateValidationResult` (`IsValid`, `StatusCode`, `Errors`, `IsBeingTrustedTransiently`) instead of throwing. Per-error accept logic moves from the `CertificateValidation` event to the new `CertificateValidationOptions.AcceptError` callback. |
| `CertificateTypesProvider` (class) | `ICertificateRegistry` (composed in `ICertificateManager`). Use `manager.GetInstanceCertificate(securityPolicyUri)` and `manager.LoadCertificateChainAsync(...)`. |
| `ApplicationConfiguration.CertificateValidator` (property) | `ApplicationConfiguration.CertificateManager` (parallel property — set in `ApplicationInstance.CheckApplicationInstanceCertificatesAsync`) |
| `ServerBase.CertificateValidator` (property) | `ServerBase.CertificateManager` |
| `ServerBase.InstanceCertificateTypesProvider` (property) | `ServerBase.CertificateManager` (use `ICertificateRegistry` surface) |

> **Lifecycle ordering.** `configuration.CertificateManager` is populated *inside* `await applicationInstance.CheckApplicationInstanceCertificatesAsync(...)`. Code that reads it before that call gets `null`. The required ordering is:
>
> 1. Construct `new ApplicationInstance(telemetry)`.
> 2. Load `ApplicationConfiguration` (e.g. via `LoadApplicationConfigurationAsync`).
> 3. `await applicationInstance.CheckApplicationInstanceCertificatesAsync(silent: false, ..., ct);`.
> 4. Read `configuration.CertificateManager` / pass `configuration.CertificateManager.CertificateProvider` to `UserIdentity.CreateAsync(...)`.

##### Migrating the `CertificateValidator.CertificateValidation` event

The legacy event with mutable `e.Accept = true` mutability has been replaced by
the structured `CertificateValidationOptions.AcceptError` callback:

```csharp
// Before:
configuration.CertificateValidator.CertificateValidation += (s, e) =>
{
    if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
    {
        e.Accept = true;
    }
};
await configuration.CertificateValidator.ValidateAsync(cert);

// After:
var options = new CertificateValidationOptions
{
    AcceptError = (cert, error) =>
        error.StatusCode == StatusCodes.BadCertificateUntrusted
};
CertificateValidationResult result =
    await applicationInstance.CertificateManager.ValidateAsync(cert, options: options);
if (!result.IsValid)
{
    throw new ServiceResultException(result.StatusCode);
}
```

##### Endpoint-aware validation helpers

`CertificateValidator.ValidateApplicationUri(...)` and
`CertificateValidator.ValidateDomains(...)` are now exposed as extension
methods on `ICertificateValidatorEx` in the
`Opc.Ua.CertificateValidationExtensions` static class. Existing call sites
that previously used the legacy class continue to work transparently.

> The `CertificateFactory.DefaultKeySize` / `DefaultLifeTime` / `DefaultHashSize` constants are
> intentionally **not** marked obsolete; they remain the canonical default values used across
> configuration sites.

To suppress `CS0618` warnings while migrating, add at the top of affected files:
```csharp
#pragma warning disable CS0618 // Obsolete API usage during migration
```

### GDS Client API modernization

The `Opc.Ua.Gds.Client.Common` package has undergone a significant cleanup. Two breaking changes affect almost every consumer of the GDS / LDS / Server-Push client APIs.

#### `Task` → `ValueTask` on GDS client interfaces

**Breaking Change**: All asynchronous methods on `IGlobalDiscoveryServerClient`, `ILocalDiscoveryServerClient`, and `IServerPushConfigurationClient` (and their concrete implementations) now return `ValueTask` / `ValueTask<T>` instead of `Task` / `Task<T>`.

**Rationale**: Many GDS operations complete synchronously when a session is already established. Returning `ValueTask` avoids the per-call `Task` allocation on those fast paths and keeps the surface consistent with the rest of the modernized client stack.

**Impact**: Pure `await` callers require **no change** — `await` works identically on `Task` and `ValueTask`. However, two patterns require a small adjustment.

| Pattern | Old (`Task`) | New (`ValueTask`) |
|---|---|---|
| `await` on the return value | works | works (no change) |
| Block synchronously via `.Result` / `.Wait()` | works | use `.AsTask().Result` / `.AsTask().Wait()` |
| Combine results with `Task.WhenAll` / `Task.WhenAny` | works | call `.AsTask()` first |
| Await the same return value more than once | works | **not supported** — call `.AsTask()` first |

> **Important**: A `ValueTask` may be awaited only once and the underlying value source must not be observed after the operation has completed. If you need to await a result more than once, fan it out across multiple consumers, or pass it to anything other than a single `await`, materialize it via `.AsTask()` first.

```csharp
// Before
Task<NodeId> registration = gds.RegisterApplicationAsync(application, ct);
NodeId id = await registration;
await Task.WhenAll(registration, otherTask);          // worked

// After
ValueTask<NodeId> registration = gds.RegisterApplicationAsync(application, ct);
NodeId id = await registration;                       // unchanged

// Multi-await / Task.WhenAll: materialize first
Task<NodeId> asTask = gds.RegisterApplicationAsync(application, ct).AsTask();
await Task.WhenAll(asTask, otherTask);
```

#### Removal of obsolete GDS APIs

**Breaking Change**: All `[Obsolete]` synchronous wrappers, APM (`Begin*`/`End*`) methods, and other deprecated members have been removed from the GDS client surface.

**Affected APIs (non-exhaustive)**:

- All synchronous wrappers on `GlobalDiscoveryServerClient` (~25 methods such as `FindApplication`, `RegisterApplication`, `StartNewKeyPairRequest`, …) — use the corresponding `*Async` overload returning `ValueTask`/`ValueTask<T>`.
- All synchronous wrappers on `ServerPushConfigurationClient` (~14 methods such as `UpdateCertificate`, `ReadTrustList`, `ApplyChanges`, …) — use the `*Async` overload.
- APM (`Begin*` / `End*`) overloads on `LocalDiscoveryServerClient` (e.g. `BeginFindServers` / `EndFindServers`) — use the `*Async` overload.
- The capability identifier constants are now source-generated as `Opc.Ua.ServerCapability` (singular, e.g. `ServerCapability.GDS`, `ServerCapability.LDS`, `ServerCapability.DA`). The `[Obsolete] public const string` shims previously exposed on the value-type `ServerCapability` class (now `ServerCapabilityInfo` in `Opc.Ua.Gds.Client`) have been removed. The runtime `ServerCapabilities.csv` parsing path (which never actually loaded — the resource was not embedded) has been replaced by the generated dictionary `ServerCapability.All`. The instance enumerable previously named `ServerCapabilityCatalog` is now `Opc.Ua.Gds.Client.ServerCapabilities` and its `Find` returns `ServerCapabilityInfo`.
- `RegisteredApplication` is now a `sealed record`; the obsolete extension methods that wrapped its property access have been removed — use the record properties directly.
- `CertificateWrapper` is now `sealed` and no longer implements `IEncodeable`; remove any code that treated it as an encodeable.

**Migration**:

The `ServerCapability` identifiers are source-generated from `Tools/Opc.Ua.SourceGeneration.Core/Design/ServerCapabilities.csv`; each capability emits a `public const string` field. The instance type carrying `Id` / `Description` is `ServerCapabilityInfo`, and the registry exposing `IEnumerable<ServerCapabilityInfo>` plus `Find(string?) : ServerCapabilityInfo?` is the static `ServerCapabilities` class in `Opc.Ua.Gds.Client.Common`.

```csharp
// Before
var apps = gds.FindApplication(uri);                       // sync wrapper
var caps = ServerCapability.GlobalDiscoveryServer;         // obsolete shim

// After
var apps = await gds.FindApplicationAsync(uri, ct);
string id = ServerCapability.GDS;                          // const string "GDS"
ServerCapabilityInfo? info = ServerCapabilities.Find(id);  // null if not registered
```

If you currently rely on a `[Obsolete]` member, switch to the `Async` equivalent and apply the `ValueTask` migration notes above. If a particular API has no direct replacement, the migration is described inline in the XML doc comment of the replacement member.

### ManagedSession and Automatic Reconnection

Version 2.0 introduces `ManagedSession`, a wrapper around `Session` that automatically handles connection lifecycle including reconnection and server redundancy failover.

**Key Changes**:

- **`ManagedSessionFactory`** is a **new** factory that creates `ManagedSession` instances which handle reconnection and failover automatically. Use this when you want managed-session behavior.
- **`DefaultSessionFactory`** is **unchanged** — it continues to create raw `Session` instances. Existing code that constructs `DefaultSessionFactory` directly keeps the same behavior in 2.0.
- **`SessionReconnectHandler`** is retained as a supported legacy entry point for callers that already manage raw `Session` instances. The type itself is not removed. Its parameterless legacy constructor remains marked `[Obsolete("Use SessionReconnectHandler(ITelemetryContext, bool, int) instead.")]`; pass an `ITelemetryContext` to the new ctor when adopting it. It now also requires the wrapped `ISession` to be a `Session` (or a derived type) — passing a `ManagedSession` (or any other `ISession` facade) throws `NotSupportedException`. New code should still prefer `ManagedSessionFactory` / `ManagedSession.CreateAsync` (which transparently uses the new `IClientChannelManager` if registered), or wire `IClientChannelManager` into a raw `Session` via `Session.CreateAsync(IClientChannelManager, ...)`.

- **`IClientBase.AttachChannel(ITransportChannel)` and `DetachChannel()`** are now marked **`[Obsolete]`** as of issue [#3288](https://github.com/OPCFoundation/UA-.NETStandard/issues/3288). They remain functional. New code should use `IClientChannelManager` so transport channels are reference-counted, shared across participants on the same endpoint, and reconnected transparently. See [Sessions and reconnect](Sessions.md#4-iclientchannelmanager--centralised-channel-sharing-and-reconnect).

For a deeper architectural picture of how `Session`, `ManagedSession`, `SessionReconnectHandler`, and the subscription engines fit together, see [Sessions, Reconnection, and Subscription Engines](Sessions.md).

**Migration**:

**If you use `DefaultSessionFactory`:**
No code changes are required — `DefaultSessionFactory` still returns raw `Session`. To opt into automatic reconnection and redundancy failover, switch to `ManagedSessionFactory`:

```csharp
// Still supported in 2.0 — DefaultSessionFactory creates raw Session:
var defaultFactory = new DefaultSessionFactory(telemetry);
ISession rawSession = await defaultFactory.CreateAsync(...);

// Opt in to managed reconnect/failover — ManagedSessionFactory creates ManagedSession:
var managedFactory = new ManagedSessionFactory(telemetry);
ISession managedSession = await managedFactory.CreateAsync(...);
```

Both factories implement `ISessionFactory`. `ManagedSessionFactory` internally uses a `DefaultSessionFactory` to create the raw `Session` and then wraps it in a `ManagedSession`; the public surface is unchanged.

**If you use `SessionReconnectHandler`:**

`SessionReconnectHandler` continues to work in 2.0 against `Session` instances. The pattern below is unchanged, but the legacy parameterless ctor remains `[Obsolete]` - prefer the `(ITelemetryContext, bool, int)` overload:

```csharp
ISession session = await new DefaultSessionFactory(telemetry).CreateAsync(...);
using var reconnectHandler = new SessionReconnectHandler(telemetry);
session.KeepAlive += (s, e) =>
{
    if (e.Status != null && ServiceResult.IsNotGood(e.Status))
    {
        reconnectHandler.BeginReconnect(session, 1000, OnReconnectComplete);
    }
};
```

`SessionReconnectHandler.BeginReconnect` only supports the legacy `Session` class (or types derived from it). Passing a `ManagedSession` throws `NotSupportedException`. If you have already migrated to `ManagedSession`, **do not** wrap it with a `SessionReconnectHandler` — `ManagedSession` already runs its own reconnect state machine. Use the `StateChanged` event to observe transitions:

```csharp
ISession session = await ManagedSession.CreateAsync(
    configuration, endpoint,
    reconnectPolicy: new ReconnectPolicy
    {
        Strategy = BackoffStrategy.Exponential,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30)
    });
// Reconnection is automatic — no manual handler needed
((ManagedSession)session).StateMachine.StateChanged += (s, e) =>
{
    Console.WriteLine($"Session state: {e.NewState}");
};
```

Or, equivalently, via the factory:

```csharp
var factory = new ManagedSessionFactory(telemetry);
ISession session = await factory.CreateAsync(...);
```

#### Configuring Reconnection Policy

Two related types ship side-by-side and are not interchangeable. `ReconnectPolicyOptions` is a `public sealed record` with init-only properties - the DTO consumed by dependency injection / `ManagedSessionOptions`. `ReconnectPolicy` is a `public class` (implementing `IReconnectPolicy`) - the runtime policy passed to `ManagedSession.CreateAsync` and `SessionReconnectHandler`. Construct the runtime policy from the options snapshot with `new ReconnectPolicy(options)`; `ManagedSessionBuilder.ConnectAsync` performs this conversion internally.

```csharp
var policy = new ReconnectPolicy
{
    Strategy = BackoffStrategy.Exponential,  // or Linear, Constant
    InitialDelay = TimeSpan.FromSeconds(1),
    MaxDelay = TimeSpan.FromSeconds(30),
    MaxRetries = 0,         // 0 = unlimited
    JitterFactor = 0.1,     // ±10% jitter
    MaxTotalReconnectTime = TimeSpan.FromMinutes(5)
};
```

#### Server Redundancy

`ManagedSession` automatically reads server redundancy information and can failover to backup servers:

```csharp
var session = await ManagedSession.CreateAsync(
    configuration, endpoint,
    redundancyHandler: new DefaultServerRedundancyHandler());
```

#### Service Call Behavior During Reconnect

When the session is reconnecting, service calls (Read, Write, Browse, etc.) automatically wait until the session is reconnected. This is transparent to the caller — no special handling needed. If reconnection fails permanently, calls will throw `ServiceResultException`.

#### Fluent Builder, V2 Subscriptions, and Dependency Injection

Version 2.0 introduces a fluent builder for `ManagedSession`, exposes the new options-based subscription API on the managed session, and adds Microsoft.Extensions.DependencyInjection integration for Azure / ASP.NET Core / generic-host scenarios.

**Fluent builder:**

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .WithSessionName("MyClient")
    .WithSessionTimeout(TimeSpan.FromSeconds(60))
    .WithReconnectPolicy(p => p with
    {
        Strategy = BackoffStrategy.Exponential,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30)
    })
    .WithServerRedundancy()
    .ConnectAsync(ct);
```

`Build()` returns an immutable `ManagedSessionOptions` snapshot; `ConnectAsync()` wraps `Build()` and `ManagedSession.CreateAsync(...)` so most callers can use the builder directly.

**New subscription API on `ManagedSession`:**

`ManagedSession` now exposes an `ISubscriptionManager` (the V2 options-based API) alongside the classic `Subscriptions` property. The V2 engine is the default for `ManagedSession`. Use `UseSubscriptionEngine(ClassicSubscriptionEngineFactory.Instance)` on the builder if you need the legacy classic engine instead — accessing `SubscriptionManager` then throws `InvalidOperationException`.

```csharp
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;

var handler = new MyNotificationHandler();   // : ISubscriptionNotificationHandler

ISubscription subscription = session.AddSubscription(handler,
    new SubscriptionOptions
    {
        PublishingInterval = TimeSpan.FromMilliseconds(500),
        KeepAliveCount = 10,
        LifetimeCount = 100
    });

subscription.TryAddMonitoredItem(
    "ServerStatus_CurrentTime",
    VariableIds.Server_ServerStatus_CurrentTime,
    o => o with
    {
        SamplingInterval = TimeSpan.FromMilliseconds(250),
        QueueSize = 10
    },
    out IMonitoredItem _);
```

The `SubscriptionOptions` and `MonitoredItemOptions` records used by this API live in `Opc.Ua.Client.Subscriptions` and `Opc.Ua.Client.Subscriptions.MonitoredItems`. They are distinct from the classic types of the same names in the `Opc.Ua.Client` namespace; use namespace aliases (or fully-qualified names) when both are visible in the same file. Both records ship in the same assembly (`Opc.Ua.Client.dll`), so a using-alias is sufficient - `extern alias` is **not** required:

```csharp
using ClassicSubscriptionOptions = Opc.Ua.Client.SubscriptionOptions;
using V2SubscriptionOptions      = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
```

The classic `ManagedSession.Subscriptions` collection (V1 `Subscription` objects) remains supported. Mixing classic subscriptions with the V2 manager on the same session is allowed for the time being, but this will change in future releases; classic subscriptions still receive notifications via the internal `SubscriptionBridge` when the V2 engine is active.

**Opt-in V2 notification pooling (`WithPoolNotifications`):**

The V2 subscription engine supports activator-level pooling of decoded
notification payload instances (`DataChangeNotification`,
`MonitoredItemNotification`, `EventNotificationList`, `EventFieldList`) to
reduce GC pressure on high-throughput publish loops. Pooling is **opt-in**
and disabled by default. Enable it on the builder, in
`ManagedSessionOptions`, or directly on the V2 manager:

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .WithPoolNotifications()        // opt in
    .ConnectAsync(ct);
```

When pooling is enabled, the V2 dispatcher walks each decoded notification
after the handler `await` completes and calls
`IPooledEncodeable.Reuse()` on every payload item, returning instances to
their static activator pools. The recorded benchmarks show ~315× fewer
allocations per `MonitoredItemNotification` and a corresponding drop in
gen-0 GC pressure
(see [`Docs/perf/PooledNotificationBenchmarks.md`](perf/PooledNotificationBenchmarks.md)).

**Handler contract change (only when `WithPoolNotifications` is enabled):**
Handlers must **not** retain references to notification objects past the
`await` of the dispatch call. The pool may re-rent those instances to the
next publish immediately after `Reuse()` runs. Handlers that need to keep
values must **copy** them out of the dispatched struct before returning.
The `DataValueChange` / `EventNotification` projection structs are
designed not to surface pooled instances directly — copy-by-value of the
struct itself is safe and is the recommended pattern. See
[`Docs/Sessions.md`](Sessions.md#v2-notification-pooling-opt-in) for full
detail and a code example.

```csharp
// UNSAFE - captures a pooled instance across await
handler.OnDataChange = async (notif, ct) =>
{
    log.Add(notif);     // notif may be re-rented on the next publish
    await Task.Yield();
};

// SAFE - value-copy the projection struct before suspending
handler.OnDataChange = async (notif, ct) =>
{
    var snapshot = notif;
    log.Add(snapshot);
    await Task.Yield();
};
```

This affects only the V2 engine; the classic subscription engine is
unaffected. There is no breaking change to `IEncodeable`,
`IDecoder`, `IServiceMessageContext`, or
`ISubscriptionNotificationHandler` — pooling is opt-in via the new
`IPooledEncodeable` sub-interface, which only the source-generated
publish-payload types implement today.

**Dependency Injection:**

`services.AddOpcUa().AddClient(...)` registers a `ManagedSession` factory delegate that lazily connects on first use:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Client;

services.AddOpcUa().AddClient(opt =>
{
    opt.Configuration = applicationConfiguration;
    opt.Session = new ManagedSessionOptions
    {
        Endpoint = endpoint,
        ReconnectPolicy = new ReconnectPolicyOptions
        {
            Strategy = BackoffStrategy.Exponential
        }
    };
});

// Resolve and connect on first use:
var sessionFactory = serviceProvider
    .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
ManagedSession session = await sessionFactory(ct);
```

The factory caches the connected session — subsequent awaits return the same instance. The registered delegate type is `Func<CancellationToken, Task<ManagedSession>>` (the OPC UA client APIs use `Task` here, not `ValueTask`), so resolving it from dependency injection and `await`-ing the result returns the connected `ManagedSession`. The dependency injection registration also exposes `ITelemetryContext`, `ISessionFactory` (a `DefaultSessionFactory` configured with the V2 engine), `ManagedSessionFactory`, and the top-level `OpcUaClientOptions`.

This iteration uses single-instance options (no named/keyed registrations); the underlying V2 manager consumes options via `IOptionsMonitor<T>` unfiltered. For one-off use, the `AddSubscription`/`TryAddMonitoredItem` extensions adapt plain options snapshots into the required `IOptionsMonitor<T>` automatically. Named-options dependency injection is deferred to a future iteration.

### `INodeCache` changes

Version 2.0 collapses the two parallel node-cache contracts into a single public interface and removes the remaining synchronous wrappers from the cache surface.

**Key changes**:

- **`ILruNodeCache` is removed.** `LruNodeCache` now implements only `INodeCache`. All members previously on `ILruNodeCache` (the   NodeId-keyed `Get*` family and `LoadTypeHierarchyAsync`) are now
  members of `INodeCache`.
- **All async methods on `INodeCache` return `ValueTask` / `ValueTask<T>`** (was `Task<T>` for `FindAsync`, `FetchNodeAsync`, `FetchNodesAsync`, `FetchSuperTypesAsync`, `FindReferencesAsync`).
  Callers that simply `await` these methods need no change. Callers that store the result in a `Task` variable, return the bare task, or re-await the same task must wrap with `.AsTask()` once.
- **`void INodeCache.LoadUaDefinedTypes(ISystemContext)` is removed.** The LRU implementation populates lazily and the prior method body was a no-op. Drop the call from your code; the cache is ready to
  use.
- **`bool ILruNodeCache.IsTypeOf(NodeId, NodeId)` is removed.** Use `IAsyncTypeTable.IsTypeOfAsync(NodeId, NodeId, CancellationToken)` instead — `INodeCache` inherits from `IAsyncTypeTable` so the
  method is reachable on the same instance.
- **`NodeCacheObsolete` synchronous extensions are removed.** The blocking wrappers `Find`, `FetchNode`, `FetchNodes`, `FetchSuperTypes`, `FindReferences`, `GetDisplayText`, `IsKnown`, `FindSuperType`, and
  `Exists` were obsoleted in 1.5.378 and now no longer compile. Switch to the matching async methods (`FindAsync`, `FetchNodeAsync`, …).
- ** Moving of several methods to extension classes**: The following members were moved to extension methods on `NodeCacheExtensions` (in the same `Opc.Ua` namespace, so no `using` changes needed). These methods are thin wrappers around the core `INodeCache` surface and preserve the old signatures where possible.

    | Removed from interface | Replacement |
    |---|---|
    | `GetSuperTypeAsync(NodeId, ct)` | inherited `IAsyncTypeTable.FindSuperTypeAsync(NodeId, ct)` (identical semantics — the interface methods returned the same `NodeId.Null`-on-miss value) |
    | `FindReferencesAsync(ExpandedNodeId, NodeId, bool, bool, ct)` | inherited `IAsyncNodeTable.FindAsync(source, refType, isInverse, includeSubtypes, ct)` (identical signature). A thin extension method preserves the old name for callers that prefer it. |
    | `FindReferencesAsync(ArrayOf<ExpandedNodeId>, ArrayOf<NodeId>, …)` | extension method on `NodeCacheExtensions` (same signature). |
    | `FindAsync(ArrayOf<ExpandedNodeId>, ct)` | extension method on `NodeCacheExtensions` that loops over the inherited `FindAsync(ExpandedNodeId)`. |
    | `FetchSuperTypesAsync(ExpandedNodeId, ct)` | extension method that loops `FindSuperTypeAsync`. |
    | `GetNodeWithBrowsePathAsync(NodeId, ArrayOf<QualifiedName>, ct)` | extension method on `NodeCacheExtensions`. |
    | `GetBuiltInTypeAsync(NodeId, ct)` | extension method on `NodeCacheExtensions`. |
    | `GetDisplayTextAsync(INode | ExpandedNodeId | ReferenceDescription, ct)` | three extension methods on `NodeCacheExtensions`. |

  External implementations of `INodeCache` no longer need to implement these members. Call sites that already used `using Opc.Ua;` keep compiling unchanged because the extensions live in the same namespace.

The new `INodeCache` deliberately keeps two name conventions side by side. The XML doc on `INodeCache` spells this out as well:

| Family | Identity | Result | Behavior |
|---|---|---|---|
| `Find*` / `Fetch*` | `ExpandedNodeId` | nullable | `Find*` consults the cache, then the server; `Fetch*` always re-reads from the server. |
| `Get*` | `NodeId` | non-nullable / throws | LRU-style direct hit; cheaper for in-process callers that already have a local `NodeId`. |

**Migration**:

```csharp
// Before — Task-returning + sync helpers
INodeCache cache = session.NodeCache;
cache.LoadUaDefinedTypes(session.SystemContext); // removed
ArrayOf<INode?> nodes = await cache.FindAsync(nodeIds);
Task<Node?> tn = cache.FetchNodeAsync(nodeId);   // returned Task<T>
bool isType = cache.IsTypeOf(sub, super);        // sync, was on ILruNodeCache
```

```csharp
// After — single INodeCache surface, all async, no sync IsTypeOf
INodeCache cache = session.NodeCache;
ArrayOf<INode?> nodes = await cache.FindAsync(nodeIds);
ValueTask<Node?> tn = cache.FetchNodeAsync(nodeId);
bool isType = await cache.IsTypeOfAsync(sub, super);
```

### Alarms and Conditions

Two changes require attention.

#### `AlarmConditionState` state-transition behavior

The state-machine setters on `AlarmConditionState` previously did not
implement several cross-state spec requirements. 1.6 makes them
compliant:

| Behavior | Spec | Was (≤ 1.5.378) | Is (1.6) |
|---|---|---|---|
| Activating an alarm with `LatchedState` populated | §4.8 | `LatchedState` untouched | `LatchedState.Id = true` automatically |
| Activating an alarm with `SilenceState` populated and silenced | §4.8 | `SilenceState` stayed silenced | `SilenceState.Id = false` (audible again) |
| `SuppressedOrShelved` flag computation | §5.8.2 | considered Suppressed + Shelved only | also considers `OutOfServiceState` |
| `GetRetainState` for latched alarms | §5.5.2 | did not include LatchedState | latched alarms are retained while `LatchedState.Id = true` |
| `EffectiveDisplayName` composition | §5.8.2 | Active + Suppressed + Shelved + Acked + Confirmed | additionally includes OutOfService and Latched |

**Migration:** If you have alarms with `LatchedState`,
`SilenceState`, or `OutOfServiceState` populated and you relied on
the prior behavior, the spec-compliant behavior is what your
operators expected anyway. To restore the old behavior, do not
populate those optional state nodes (leave them `null`).

The quickstart reference server (`Applications/Quickstarts.Servers/
Alarms/AlarmHolders/AlarmConditionTypeHolder.cs`) now creates the
`SilenceState`, `OutOfServiceState`, and `LatchedState` nodes by
default — so the conformance tests exercise the new compliant
behavior end-to-end.

The quickstart `AlarmNodeManager` itself was also modernized:

* it now derives from `AsyncCustomNodeManager` (was
  `CustomNodeManager2`) and uses the async lifecycle overrides
  (`CreateAddressSpaceAsync`, `CallAsync`, `ConditionRefreshAsync`),
  matching the stack-wide pattern used by `WotConnectivityNodeManager`,
  `FluentNodeManagerBase`, etc.;
* it demonstrates the new `AlarmGroup` + `AlarmSuppressionEngine`
  helpers end-to-end with an `/Alarms/AnalogGroup` group and a
  writable `/Alarms/MaintenanceMode` boolean — clients can flip
  MaintenanceMode and watch every member alarm transition into
  `SuppressedState`. See
  [Alarms and Conditions](AlarmsAndConditions.md#alarm-groups-and-first-in-group)
  for the developer guide.

Neither change is breaking for stack consumers — they only affect
the quickstart demo project that ships with the reference server.

#### Auto-emit `GeneralModelChangeEvent` from `CustomNodeManager`

`CustomNodeManager.CreateNode(...)` and `DeleteNode(...)` (and the
async equivalents on `AsyncCustomNodeManager`) now record the change
in a per-instance `ModelChangeAggregator` and emit a
`GeneralModelChangeEvent` at the end of the call. This was required
by Part 5 §6.4.32 but was previously left to derived classes.

If clients were already subscribed to `BaseEventType` on the server
notifier, they will start receiving `GeneralModelChangeEvent`. Existing
clients that filter events by `EventTypeId` (the common case) keep
receiving only the types they asked for. Clients that subscribe to
the broad `BaseEventType` and want to skip model-change traffic should
add a `not OfType GeneralModelChangeEventType` clause to their
`EventFilter`.

```csharp
// To opt out of auto-emit in a derived node manager:
public MyNodeManager(...)
{
    ModelChangeEmissionEnabled = false;
}
```

The aggregator API (`ModelChangeAggregator.RecordNodeAdded/Deleted/
ReferenceAdded/ReferenceDeleted/DataTypeChanged`, `Drain`,
`HasPending`) is also available for manual control — see
[Model Change Tracking](ModelChangeTracking.md).

### Address-space model change tracking

#### New `INodeCache.InvalidateNode` member

`INodeCache` gains a new abstract member in 1.6:

```csharp
void InvalidateNode(NodeId nodeId);
```

The stack's built-in `NodeCache` implements this with true per-node
eviction. The `ModelChangeTracker` uses it to keep the cache in sync
with server-reported address-space changes — see
[Model Change Tracking](ModelChangeTracking.md).

**Migration:** Custom `INodeCache` implementations must add an
implementation. The simplest is to delegate to `Clear()`:

```csharp
public sealed class MyNodeCache : INodeCache
{
    public void Clear() { /* ... */ }

    // Add this:
    public void InvalidateNode(NodeId nodeId) => Clear();

    // ... rest of INodeCache ...
}
```

Implementations that can perform per-node eviction should do so —
the tracker is most efficient when targeted invalidation is
available.

### Time and Timer abstraction (`TimeProvider`)

**Not source-breaking.** The stack now uses
[`System.TimeProvider`](https://learn.microsoft.com/dotnet/api/system.timeprovider) as
its canonical clock and scheduler so that timeouts, intervals, keep-alive loops,
reconnect back-off, publishing pacing, certificate-lifetime checks, and similar
duration-sensitive code paths are mockable in tests and immune to wall-clock changes.

`HiResClock` is still in place but **every public member is now marked
`[Obsolete]`**. The class itself is not obsolete so that existing field references
(`HiResClock.Disabled`) keep round-tripping through configuration; only the static
clock-reading members raise CS0618. The recommended replacements are:

| Legacy API                              | Replacement                                                                          |
| --------------------------------------- | ------------------------------------------------------------------------------------ |
| `HiResClock.UtcNow`                     | `timeProvider.GetUtcNow().UtcDateTime`                                               |
| `HiResClock.TickCount64` / `.Ticks`     | `timeProvider.GetTimestamp()`                                                        |
| `HiResClock.TickCount` (int wraparound) | `timeProvider.GetTickCount()` (internal extension in `Opc.Ua`)                       |
| `HiResClock.UtcTickCount(offsetMs)`     | `timeProvider.GetTimestampMilliseconds() + offsetMs`                                 |
| elapsed-time math via `TickCount`       | `long start = timeProvider.GetTimestamp(); … TimeSpan elapsed = timeProvider.GetElapsedTime(start);` |
| `new Stopwatch()` / `Stopwatch.StartNew()` for duration | `long start = timeProvider.GetTimestamp(); … timeProvider.GetElapsedTime(start);` |
| `new System.Threading.Timer(…)`         | `ITimer timer = timeProvider.CreateTimer(callback, state, dueTime, period);`         |
| `Task.Delay(delay, ct)` in production timing loops | `Task.Delay(delay, timeProvider, ct)`                                       |
| `new CancellationTokenSource(timeout)`  | `new CancellationTokenSource(timeout, timeProvider)`                                 |

**Constructor pattern.** Components that need a clock now take a nullable
`TimeProvider` as the **last** constructor parameter with a default value of `null`.
If `null` is passed, `TimeProvider.System` is used. Example:

```csharp
public sealed class Foo
{
    private readonly TimeProvider m_timeProvider;

    public Foo(/* existing args */, TimeProvider? timeProvider = null)
    {
        // existing initialisation…
        m_timeProvider = timeProvider ?? TimeProvider.System;
    }
}
```

For published public types whose existing constructors must remain
binary-compatible, the original constructor signature is preserved and a new
overload that ends with `TimeProvider?` is added. The legacy constructor delegates
to the new one passing `timeProvider: null`. No existing constructor is marked
`[Obsolete]` in this release.

**Dependency injection.** `AddOpcUaServerBuilder` / `AddOpcUaClientBuilder` register
`TimeProvider.System` via `TryAddSingleton<TimeProvider>` and wire the resolved
provider into every component they construct. To run a server or client against a
fake clock in tests, register a `Microsoft.Extensions.Time.Testing.FakeTimeProvider`
in the service collection before the OPC UA builders.

```csharp
services.AddSingleton<TimeProvider>(new FakeTimeProvider());
services.AddOpcUaServerBuilder(/* … */);
```

Outside DI, pass the `TimeProvider` directly to the type's constructor as the last
argument.

**Migrating off `HiResClock`.** Replace the call with the table above. If the
migration cannot happen immediately, wrap the affected scope with
`#pragma warning disable CS0618` / `#pragma warning restore CS0618`.

```csharp
// before:
long start = HiResClock.TickCount64;
DoWork();
TimeSpan elapsed = TimeSpan.FromTicks(HiResClock.TickCount64 - start);

// after:
long start = m_timeProvider.GetTimestamp();
DoWork();
TimeSpan elapsed = m_timeProvider.GetElapsedTime(start);
```

```csharp
// before:
DateTime utcNow = HiResClock.UtcNow;

// after — when a wall-clock value is required (e.g. for an OPC UA SourceTimestamp):
DateTime utcNow = m_timeProvider.GetUtcNow().UtcDateTime;
```

```csharp
// before:
m_timer = new Timer(OnTick, state: null, dueTime: 1_000, period: Timeout.Infinite);

// after:
m_timer = m_timeProvider.CreateTimer(OnTick, state: null,
    dueTime: TimeSpan.FromMilliseconds(1_000), period: Timeout.InfiniteTimeSpan);
```

The `Timer` field type changes from `System.Threading.Timer` to `ITimer` — both
implement `IDisposable` and the same `Change` / `Dispose` semantics; only the
parameter types on `Change` differ (`TimeSpan` instead of `int`/`uint`/`long`).

#### Monotonic timestamps for duration calculations

`TimeProvider.GetTimestamp()` returns a `long` monotonic timestamp that does not
suffer from the 32-bit wraparound of `Environment.TickCount` / `HiResClock.TickCount`
nor the system-clock drift of `DateTime.UtcNow`. All internal duration math in the
stack now uses `GetTimestamp()` + `GetElapsedTime(start)` instead of `int`-tick
subtraction. The following public surface changes were made:

| Old (removed or `[Obsolete]`)                                            | New                                                                                                  |
| ------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------- |
| `ISession.LastKeepAliveTickCount: int` (was on the interface)            | `ISession.LastKeepAliveTimestamp: long` + `timeProvider.GetElapsedTime(timestamp)` (legacy int now an `[Obsolete]` extension property in `SessionObsolete`) |
| `ChannelToken.Expired`, `ChannelToken.ActivationRequired`, `ChannelToken.CreatedAtTickCount` | Removed. Use `ChannelToken.IsExpired(TimeProvider)` / `ChannelToken.IsActivationRequired(TimeProvider)` (internal). |
| `UaSCUaBinaryChannel.LastActiveTickCount: int` (protected)               | Removed. Use `UaSCUaBinaryChannel.GetElapsedSinceLastActive(): TimeSpan` (internal).                 |

Pattern for new code computing an internal duration:

```csharp
// before:
int startTicks = m_timeProvider.GetTickCount();
// ... do work ...
int elapsedMs = m_timeProvider.GetTickCount() - startTicks;

// after:
long startTimestamp = m_timeProvider.GetTimestamp();
// ... do work ...
TimeSpan elapsed = m_timeProvider.GetElapsedTime(startTimestamp);
```

### Subscriptions and Transports

#### Durable subscriptions and reshaped Subscription tree

**Source-breaking.** Durable subscription support reshapes the subscription tree on both the client and the server. On the client side, the new public surface in `Libraries/Opc.Ua.Client/Subscription/` includes `ISubscription`, `ISubscriptionManager`, `SubscriptionOptions`, and `MonitoredItemOptions` - these are the V2 options-based shapes; the classic `Opc.Ua.Client.Subscription` continues to ship alongside them. On the server side, the new public surface in `Libraries/Opc.Ua.Server/Subscription/...` includes `DataChangeMonitoredItemQueue`, `EventMonitoredItemQueue`, `IDataChangeMonitoredItemQueue`, `IMonitoredItemQueueFactory`, `ISubscriptionStore`, `IStoredSubscription`, `StoredSubscription`, and `StoredMonitoredItem`.

Consumers adopting the new shape may need to add a `using Opc.Ua.Client.Subscriptions;` import alongside the existing `using Opc.Ua.Client;`. Because the V2 records share their type names with the classic records, namespace aliases are required when both are visible in the same file - see [Fluent Builder, V2 Subscriptions, and Dependency Injection](#fluent-builder-v2-subscriptions-and-dependency-injection) for the canonical alias snippet.

#### PubSub

**Not source-breaking.** No public top-level types in `Opc.Ua.PubSub` were removed or renamed in 2.0. Changes are limited to internal modernization, AOT preparation, and diagnostics improvements. `Newtonsoft.Json` remains a direct `<PackageReference>` of `Libraries/Opc.Ua.PubSub/Opc.Ua.PubSub.csproj`, so PubSub consumers keep receiving it transitively (see [Newtonsoft.Json - what really changed](#newtonsoftjson---what-really-changed)).

#### Reverse connect

**Not source-breaking.** `ReverseConnectManager`, `ReverseConnectProperty`, and `ReverseConnectServer` retain the same public shape in 2.0. The previously published `ReverseConnectClientCollection` wrapper has been removed; this is already covered by the broader [Configuration collection types removed](#configuration-collection-types-removed) guidance.

Two additive surface additions for the new WSS reverse-connect path (see [`Docs/ReverseConnect.md`](ReverseConnect.md)):

* **`ReverseConnectManager.AddEndpoint(Uri, ApplicationConfiguration?)`** — new overload. Callers binding `opc.wss://` reverse-connect endpoints should pass the `ApplicationConfiguration` to this overload so the underlying `HttpsTransportListener` receives the `CertificateManager` at bind time. The single-parameter `AddEndpoint(Uri)` is unchanged and remains the right call for `opc.tcp://` endpoints (which don't need TLS state).
* **`Opc.Ua.Bindings.Kestrel.Tcp`** — new opt-in package (net8+) that hosts `opc.tcp` on Kestrel and supports both forward and reverse-connect listener modes. Install via the new DI extension `services.AddOpcUa().AddOpcTcpTransport().AddKestrelOpcTcpTransport()` or, for non-DI consumers, hand a `DefaultTransportBindingRegistry` carrying a `KestrelTcpTransportListenerFactory` to `ServerBase` / `ReverseConnectManager`. The default raw-socket `TcpTransportListener` continues to ship in `Opc.Ua.Core` for deployments that avoid the ASP.NET Core dependency.

Channel-customization hooks (relevant only to consumers subclassing `UaSCBinaryChannel`-derived types):

* **`UaSCBinaryChannel.StartReceiveLoop`** is now `protected internal virtual`. Existing overrides (none expected outside the stack) continue to work. The default implementation is unchanged.
* **`UaSCBinaryChannel.StartReceiveLoopWithBody(Func<IUaSCByteTransport, CancellationToken, Task>)`** — new `protected` helper that sets up the receive-loop CTS/task state and runs a caller-supplied loop body. Used by `TcpReverseConnectChannel` to read a single `ReverseHello` chunk and exit cleanly (required because `WebSocket.ReceiveAsync` cancellation aborts the underlying WebSocket and would otherwise break the reverse-connect handoff).

#### Transport binding registry — `TransportBindings` static API removed

**Source-breaking.** The process-wide `TransportBindings` static class and the `Utils.DefaultBindings` reflection-based assembly auto-load helper have been removed. The new `ITransportBindingRegistry` interface (in `Opc.Ua.Bindings`) is the only public registry surface; it resolves out of the host's `IServiceProvider` so two hosts (e.g. parallel test fixtures or multi-tenant applications) can install different factories without racing on shared global state.

| Removed                                                       | Replacement                                                                                                  |
| ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `TransportBindings.Channels` (static)                         | `ITransportBindingRegistry` (resolved from DI or constructed via `DefaultTransportBindingRegistry.WithDefaultTcp()`) |
| `TransportBindings.Listeners` (static)                        | Same `ITransportBindingRegistry` — listeners and channels share one keyed registry per scheme               |
| `TransportBindings.Channels.SetBinding(...)`                  | `ITransportBindingRegistry.RegisterChannelFactory(...)` or the DI extension `AddCustomTransport<,>()`        |
| `TransportBindings.Listeners.SetBinding(...)`                 | `ITransportBindingRegistry.RegisterListenerFactory(...)` or the DI extension `AddKestrelOpcTcpTransport()`   |
| `TransportBindings.Channels.GetBinding(scheme, telemetry)`    | `ITransportBindingRegistry.GetChannelFactory(scheme)`                                                        |
| `TransportBindings.Listeners.GetBinding(scheme, telemetry)`   | `ITransportBindingRegistry.GetListenerFactory(scheme)`                                                       |
| `TransportBindings.AddBindings(assembly)` (reflection)        | Explicit DI registration via `AddOpcTcpTransport()` / `AddHttpsTransport()` / `AddWssTransport()` / `AddKestrelOpcTcpTransport()` / `AddCustomTransport<,>()` |
| `Utils.DefaultBindings` dictionary                            | Removed; no replacement needed                                                                               |
| `ITransportBindings<T>` interface                             | Removed (folded into `ITransportBindingRegistry` per-facet methods)                                           |
| `TransportBindingsBase` (reflection helper)                   | Removed                                                                                                       |

**`Microsoft.Extensions.DependencyInjection` consumers (recommended path):**

```csharp
// Before (1.5.378):
//   TransportBindings.Listeners.SetBinding(new KestrelTcpTransportListenerFactory());
//   PcapBindings.Install(); // mutated TransportBindings.Channels

// After (2.0):
services
    .AddOpcUa()
    .AddOpcTcpTransport()              // raw-socket opc.tcp default
    .AddHttpsTransport()               // HTTPS + HTTPS-JSON
    .AddWssTransport()                 // WSS + WSS-JSON
    .AddKestrelOpcTcpTransport();      // override opc.tcp with Kestrel (last-writer-wins)
services.AddOpcUaBindingsPcap();       // installs Pcap channel decorator via configurator
```

Every `Add*Transport()` extension installs an `ITransportBindingConfigurator` instance into the `IServiceCollection`. The `DefaultTransportBindingRegistry` singleton runs every registered configurator in registration order at first resolution time, so the **last** registration for a given URI scheme wins — exactly the same semantics `SetBinding` had, but scoped per `IServiceProvider`.

**Non-DI consumers:**

```csharp
// Before (1.5.378):
//   TransportBindings.Listeners.SetBinding(new KestrelTcpTransportListenerFactory());
//   var server = new MyServer();
//   server.Start(config); // pre-2.0 sync path

// After (2.0):
DefaultTransportBindingRegistry registry = DefaultTransportBindingRegistry.WithDefaultTcp();
registry.RegisterListenerFactory(new KestrelTcpTransportListenerFactory());
var server = new MyServer(telemetry);
server.TransportBindings = registry;   // public setter; rejected after StartAsync
await server.StartAsync(config, ct);
```

`ServerBase` exposes a new constructor overload (`ServerBase(ITelemetryContext, ITransportBindingRegistry?)`) and a publicly-settable `TransportBindings` property (with a started-server guard). Non-DI callers that pass nothing get a `DefaultTransportBindingRegistry.WithDefaultTcp()` on first use — exactly the raw-socket TCP listener the 1.5.378 stack defaulted to.

**Custom transport authors:**

```csharp
// Before (1.5.378):
//   TransportBindings.Listeners.SetBinding(new MyCustomListenerFactory());
//   TransportBindings.Channels.SetBinding(new MyCustomChannelFactory());

// After (2.0):
services
    .AddOpcUa()
    .AddCustomTransport<MyCustomListenerFactory, MyCustomChannelFactory>();
```

The DI extension resolves both factory types out of the container (so they may have constructor-injected dependencies), and both are registered into the registry under the `UriScheme` exposed by `MyCustomListenerFactory`.

**`Opc.Ua.Bindings.Pcap` consumers:**

`PcapBindings.Install()` no longer has a parameterless overload. Pass an `ITransportBindingRegistry` explicitly:

```csharp
// Before (1.5.378):
//   IChannelCaptureRegistry captureRegistry = PcapBindings.Install();

// After (2.0):
ITransportBindingRegistry bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
IChannelCaptureRegistry captureRegistry = PcapBindings.Install(bindings);
// or — preferred — let the DI extension wire it through:
services.AddOpcUa().AddOpcUaBindingsPcap();
```



### Security tightening — WoT Connectivity management methods

**Behaviour-breaking, not source-breaking.** The five management methods on the standard `WoTAssetConnectionManagement` object (`CreateAsset`, `DeleteAsset`, `DiscoverAssets`, `CreateAssetForEndpoint`, `ConnectionTest`) now reject anonymous and `None`/`Sign`-only callers by default. The new `WotConnectivityServerOptions.ManagementAccess` (`WotManagementAccessPolicy`) defaults to:

* `MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt`,
* `AllowAnonymous = false`,
* `RequiredRoleId = ObjectIds.WellKnownRole_SecurityAdmin`.

Existing deployments that relied on anonymous management over `None` channels must either configure their clients to use `SignAndEncrypt` and present a `SecurityAdmin`-roled identity, or explicitly opt-in to the legacy behaviour:

```csharp
services.AddOpcUa()
    .AddServer(...)
    .AddWotConServer(opts =>
    {
        opts.ManagementAccess = new WotManagementAccessPolicy
        {
            AllowAnonymous = true,
            MinimumSecurityMode = MessageSecurityMode.None,
            RequiredRoleId = ObjectIds.WellKnownRole_Anonymous
        };
    });
```

Internal callers that invoke `AssetRegistry.*Async` directly (startup restoration of persisted assets, in-process tests) are unaffected — the enforcement runs only against `OperationContext`-bearing address-space calls.

## Migrating from 1.05.377 to 1.05.378

### Asynchronous as default

The server now supports AsyncNodeManagers, see [Server Async (TAP) Support](Docs/AsyncServerSupport.md). The client APIs are async by default and all synchronous and APM
based API has been deprecated. To migrate update your code to use the Async version of all API if possible. Not recommended but for expedience sake you can use the Async
version and make it sync by appending `GetAwaiter().GetResult()` to it.

### Observability

[Observability](Docs/Observability.md) via `ITelemetryContext` in preparation for better dependency injection support. See documentation for breaking changes.

## Migrating from 1.04 to 1.05

- A few features are still missing to fully comply for 1.05, but certification for V1.04 is still possible with the 1.05 release.

## Support

For additional migration support:

- Review sample applications in the repository
- Check unit tests for usage patterns
- Consult the OPC Foundation community forums
- Report issues in the GitHub repository

---
