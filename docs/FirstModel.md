# Your first information model

This tutorial continues
[Route B of Getting started](GettingStarted.md#route-b-build-applications-from-nuget-packages).
You add a small information model to the server, give it behavior, and use it
from the client. The model describes a room thermostat:

- A `ThermostatType` object type with a measured `Temperature`, a writable
  `Setpoint`, and a `Boost` method that raises the setpoint.
- A `Thermostat` object of that type in the server's `Objects` folder.

The server accepts setpoints from 5 to 30 degrees Celsius. The client finds the
thermostat by name, reads and writes its variables, and calls its method. It
also shows how the server reports an operation that it rejects.

## Contents

- [Before you begin](#before-you-begin)
- [1. Describe the model](#1-describe-the-model)
- [2. Add the model to the server](#2-add-the-model-to-the-server)
  - [Generate the code](#generate-the-code)
  - [Implement the behavior](#implement-the-behavior)
  - [Register the node manager](#register-the-node-manager)
- [3. Use the model from the client](#3-use-the-model-from-the-client)
  - [Share the model](#share-the-model)
  - [Replace the client code](#replace-the-client-code)
- [4. Check the result](#4-check-the-result)
- [What the code does](#what-the-code-does)
- [Troubleshooting](#troubleshooting)
- [Next steps](#next-steps)

## Before you begin

Complete steps B1 to B4 of
[Getting started](GettingStarted.md#route-b-build-applications-from-nuget-packages).
This tutorial changes the `GettingStartedServer` and `GettingStartedClient`
projects from those steps. The applications keep their certificates and already
trust each other, so you do not repeat the trust steps.

The tutorial was verified with package version `2.0.0-preview.5`, the version
that Route B installs.

If you ran only the repository samples, create the Route B projects first. The
[MinimalCalcServer sample](../samples/MinimalApi/MinimalCalcServer) uses the
same server-side pattern as this tutorial.

## 1. Describe the model

A model file declares types and instances. At build time, the SDK source
generator reads the file and generates C# code for it.

In the `GettingStartedServer` folder, create a `Model` folder and in it a file
named `Heating.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<opc:ModelDesign
  xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
  xmlns:ua="http://opcfoundation.org/UA/"
  xmlns="http://example.com/Heating/"
  TargetNamespace="http://example.com/Heating/"
  TargetXmlNamespace="http://example.com/Heating/Types.xsd"
  TargetVersion="1.0.0"
  TargetPublicationDate="2026-01-01T00:00:00Z">

  <opc:Namespaces>
    <opc:Namespace Name="Heating" Prefix="Heating"
      XmlNamespace="http://example.com/Heating/Types.xsd"
      XmlPrefix="Heating">http://example.com/Heating/</opc:Namespace>
    <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
      XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
      XmlPrefix="OpcUa">http://opcfoundation.org/UA/</opc:Namespace>
  </opc:Namespaces>

  <opc:Method SymbolicName="BoostMethodType">
    <opc:Description>Raises the setpoint and returns the new setpoint.</opc:Description>
    <opc:InputArguments>
      <opc:Argument Name="Degrees" DataType="ua:Double">
        <opc:Description>The number of degrees to add to the setpoint.</opc:Description>
      </opc:Argument>
    </opc:InputArguments>
    <opc:OutputArguments>
      <opc:Argument Name="NewSetpoint" DataType="ua:Double" />
    </opc:OutputArguments>
  </opc:Method>

  <opc:ObjectType SymbolicName="ThermostatType" BaseType="ua:BaseObjectType">
    <opc:Description>A thermostat that controls the temperature of a room.</opc:Description>
    <opc:Children>
      <opc:Variable SymbolicName="Temperature" DataType="ua:Double"
        AccessLevel="Read" ModellingRule="Mandatory">
        <opc:Description>The measured temperature in degrees Celsius.</opc:Description>
      </opc:Variable>
      <opc:Variable SymbolicName="Setpoint" DataType="ua:Double"
        AccessLevel="ReadWrite" ModellingRule="Mandatory">
        <opc:Description>The target temperature in degrees Celsius, from 5 to 30.</opc:Description>
      </opc:Variable>
      <opc:Method SymbolicName="Boost" TypeDefinition="BoostMethodType"
        ModellingRule="Mandatory" />
    </opc:Children>
  </opc:ObjectType>

  <opc:Object SymbolicName="Thermostat" TypeDefinition="ThermostatType">
    <opc:Description>The living-room thermostat.</opc:Description>
    <opc:References>
      <opc:Reference IsInverse="true">
        <opc:ReferenceType>ua:Organizes</opc:ReferenceType>
        <opc:TargetId>ua:ObjectsFolder</opc:TargetId>
      </opc:Reference>
    </opc:References>
  </opc:Object>
</opc:ModelDesign>
```

| Part | Meaning |
| --- | --- |
| `TargetNamespace` | The namespace URI that identifies the model. When the server starts, it assigns each namespace URI a number, the *namespace index*. Clients look up the index by the URI. |
| `Prefix="Heating"` | The C# namespace of the generated code. |
| `BoostMethodType` | Declares the arguments of the `Boost` method: one `Double` input and one `Double` output. |
| `ThermostatType` | The object type. Every instance has the `Mandatory` children. `AccessLevel` decides whether clients can write a variable. |
| `Thermostat` | An instance of `ThermostatType`. The inverse `Organizes` reference places it in the `Objects` folder, where clients start browsing. |

The file uses the ModelDesign format, which is compact to write by hand. The
generator also reads NodeSet2 files: the standard exchange format of modeling
tools and companion specifications. See
[Source-generated node managers](NodeManagers.md#source-generated-node-managers).

> Declare method arguments in a method type such as `BoostMethodType`, and
> reference it with `TypeDefinition`. With version `2.0.0-preview.5`, arguments
> declared on a method inside an object type produce code that does not compile.

## 2. Add the model to the server

Stop the server if it is still running from Getting started.

### Generate the code

From the `GettingStartedServer` folder, add the source generator package. It
runs at build time and adds no assembly to your application.

```bash
dotnet add package OPCFoundation.NetStandard.Opc.Ua.SourceGeneration --prerelease
```

Open `GettingStartedServer.csproj`, and add the model file inside the
`<Project>` element:

```xml
<ItemGroup>
  <AdditionalFiles Include="Model/Heating.xml" />
</ItemGroup>
```

### Implement the behavior

Create `HeatingNodeManager.cs` in the `GettingStartedServer` folder:

```csharp
using Opc.Ua;
using Opc.Ua.Server.Fluent;

namespace Heating;

[NodeManager(NamespaceUri = "http://example.com/Heating/")]
public partial class HeatingNodeManager
{
    private const double MinimumSetpoint = 5.0;
    private const double MaximumSetpoint = 30.0;

    private readonly Lock m_lock = new();
    private double m_setpoint = 21.0;

    partial void Configure(IHeatingNodeManagerBuilder builder)
    {
        builder.Thermostat.Temperature
            .PollEvery(TimeSpan.FromSeconds(1), SampleTemperature);
        builder.Thermostat.Setpoint
            .OnRead(ReadSetpoint)
            .OnWrite(WriteSetpoint);
        builder.Thermostat.Boost
            .OnCall(Boost);
    }

    // Simulates a measurement within half a degree of the setpoint.
    private double SampleTemperature()
    {
        return Math.Round(ReadSetpoint() - 0.5 + Random.Shared.NextDouble(), 1);
    }

    private double ReadSetpoint()
    {
        lock (m_lock)
        {
            return m_setpoint;
        }
    }

    private void WriteSetpoint(double value)
    {
        CheckSetpoint(value);
        lock (m_lock)
        {
            m_setpoint = value;
        }
    }

    private double Boost(double degrees)
    {
        lock (m_lock)
        {
            double newSetpoint = m_setpoint + degrees;
            CheckSetpoint(newSetpoint);
            m_setpoint = newSetpoint;
            return newSetpoint;
        }
    }

    // The client receives the status code of an exception that a callback throws.
    private static void CheckSetpoint(double setpoint)
    {
        if (setpoint is not (>= MinimumSetpoint and <= MaximumSetpoint))
        {
            throw new ServiceResultException(
                StatusCodes.BadOutOfRange,
                $"The setpoint must be from {MinimumSetpoint} to {MaximumSetpoint}.");
        }
    }
}
```

The `[NodeManager]` attribute asks the generator to complete this partial class.
The generated part loads the model's nodes into the server's address space. The
generator also creates a `HeatingNodeManagerFactory` class and an
`IHeatingNodeManagerBuilder` interface. Set `NamespaceUri` to the model's
namespace URI as a string literal. The generated constant for it,
`Heating.Namespaces.Heating`, cannot be used here, because the attribute is read
before that constant is generated.

In `Configure`, the builder has a property for each node of the model:
`builder.Thermostat.Setpoint` is the `Setpoint` variable of the `Thermostat`
object. A misspelled name is a compile error. The callbacks connect the nodes to
the state that the node manager holds:

- `PollEvery` samples `Temperature` every second and updates the node when the
  value changes. Reads return the latest sample, and subscriptions receive each
  change.
- `OnRead` and `OnWrite` handle each read and write of `Setpoint`. A value that
  is returned only by an `OnRead` callback does not reach subscriptions; see
  [pushing value changes to subscribers](NodeManagers.md#pushing-runtime-value-changes-to-subscribers).
- `OnCall` receives the method's input as a `double` and returns its output.

To reject an operation, throw a `ServiceResultException` with a Bad status code.
The server returns that status code to the client. The server checks some rules
itself before it calls your callback: it rejects a value of the wrong data type
and a write to the read-only `Temperature`.

The server runs callbacks for several requests at the same time, and `PollEvery`
runs in the background, so a lock guards the setpoint.

### Register the node manager

Replace the contents of `Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "GettingStartedServer";
        options.ApplicationUri = "urn:localhost:GettingStartedServer";
        options.ProductUri = "urn:example.com:GettingStartedServer";
        options.EndpointUrls.Add("opc.tcp://localhost:62560/GettingStartedServer");
        options.PkiRoot = Path.Combine(builder.Environment.ContentRootPath, "pki");
    })
    .AddNodeManager<Heating.HeatingNodeManagerFactory>();

await builder.Build().RunAsync();
```

Only the `AddNodeManager` line is new. It adds the generated factory, which
creates the node manager when the server starts.

Start the server:

```bash
dotnet run
```

The server is ready when it logs:

```text
OPC UA server listening at opc.tcp://localhost:62560/GettingStartedServer.
```

Leave the server running.

## 3. Use the model from the client

### Share the model

The client generates code from the same model file: constants for the namespace
URI and the browse names, and a typed client for `ThermostatType`.

In the second terminal, from the `GettingStartedClient` folder, add the source
generator package:

```bash
dotnet add package OPCFoundation.NetStandard.Opc.Ua.SourceGeneration --prerelease
```

Open `GettingStartedClient.csproj`, and add the following inside the `<Project>`
element:

```xml
<PropertyGroup>
  <ModelSourceGeneratorOmitFluentApi>true</ModelSourceGeneratorOmitFluentApi>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="../GettingStartedServer/Model/Heating.xml" />
</ItemGroup>
```

`ModelSourceGeneratorOmitFluentApi` turns off the server-side helpers, which
need the server package.

### Replace the client code

Replace the contents of `Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services
    .AddOpcUa()
    .AddClient(options =>
    {
        options.ApplicationName = "GettingStartedClient";
        options.ApplicationUri = "urn:localhost:GettingStartedClient";
        options.ProductUri = "urn:example.com:GettingStartedClient";
        options.PkiRoot = Path.Combine(builder.Environment.ContentRootPath, "pki");
        options.Session = new ManagedSessionOptions
        {
            ReconnectPolicy = new ReconnectPolicyOptions { MaxRetries = 2 },
        };
    })
    .AddDiscoveryAndConnect(options =>
    {
        options.DiscoveryUrl = "opc.tcp://localhost:62560/GettingStartedServer";
        options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
        options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
    });

using IHost host = builder.Build();
await host.StartAsync();

using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
CancellationToken ct = timeout.Token;

try
{
    var connect = host.Services
        .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
    ManagedSession session = await connect(ct);
    await using (session)
    {
        // The server assigns the namespace index at run time. Look it up
        // from the namespace URI that the model defines.
        int namespaceIndex = session.NamespaceUris.GetIndex(Heating.Namespaces.Heating);
        if (namespaceIndex < 0)
        {
            throw new ServiceResultException(
                StatusCodes.BadNotFound,
                "The server does not expose the Heating model.");
        }
        var ns = (ushort)namespaceIndex;
        Console.WriteLine($"Heating namespace index: {ns}");

        // Translate three browse paths, starting at the Objects folder, in one request.
        TranslateBrowsePathsToNodeIdsResponse translated =
            await session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                [
                    PathFromObjects(ns, Heating.BrowseNames.Thermostat),
                    PathFromObjects(ns, Heating.BrowseNames.Thermostat,
                        Heating.BrowseNames.Temperature),
                    PathFromObjects(ns, Heating.BrowseNames.Thermostat,
                        Heating.BrowseNames.Setpoint),
                ],
                ct);
        NodeId thermostatId = GetTarget(session, translated.Results[0]);
        NodeId temperatureId = GetTarget(session, translated.Results[1]);
        NodeId setpointId = GetTarget(session, translated.Results[2]);

        // Read both variables in one request. Each value has its own status.
        (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) =
            await session.ReadValuesAsync([temperatureId, setpointId], ct);
        PrintValue("Temperature", values[0], errors[0]);
        PrintValue("Setpoint", values[1], errors[1]);

        // Write a valid setpoint, then one outside the range the server accepts.
        foreach (double setpoint in new[] { 22.5, 35.0 })
        {
            WriteResponse response = await session.WriteAsync(
                null,
                [
                    new WriteValue
                    {
                        NodeId = setpointId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(setpoint)),
                    },
                ],
                ct);

            // The Write service succeeds as a whole; each write has its own result.
            Console.WriteLine($"Write Setpoint {setpoint}: {response.Results[0]}");
        }

        // Call the Boost method through the client generated from the model.
        var thermostat = new Heating.ThermostatTypeClient(
            session,
            thermostatId,
            host.Services.GetRequiredService<ITelemetryContext>());
        double newSetpoint = await thermostat.BoostAsync(2.0, ct);
        Console.WriteLine($"Boost 2: new setpoint {newSetpoint}");
        try
        {
            await thermostat.BoostAsync(10.0, ct);
        }
        catch (ServiceResultException ex)
        {
            Console.WriteLine($"Boost 10: {ex.StatusCode}");
        }
    }
}
catch (ServiceResultException ex)
{
    Console.Error.WriteLine($"OPC UA error {ex.StatusCode}: {ex.Message}");
    Environment.ExitCode = 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("The client did not finish within one minute.");
    Environment.ExitCode = 1;
}
finally
{
    await host.StopAsync();
}

static BrowsePath PathFromObjects(ushort ns, params string[] browseNames)
{
    return new BrowsePath
    {
        StartingNode = ObjectIds.ObjectsFolder,
        RelativePath = new RelativePath
        {
            Elements = browseNames
                .Select(name => new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName(name, ns),
                })
                .ToArray(),
        },
    };
}

static NodeId GetTarget(ManagedSession session, BrowsePathResult result)
{
    if (StatusCode.IsBad(result.StatusCode) || result.Targets.Count == 0)
    {
        throw new ServiceResultException(
            result.StatusCode,
            "A browse path was not found.");
    }
    return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, session.NamespaceUris);
}

static void PrintValue(string name, DataValue value, ServiceResult error)
{
    if (ServiceResult.IsBad(error) || StatusCode.IsBad(value.StatusCode))
    {
        Console.WriteLine($"{name}: failed with {value.StatusCode}");
    }
    else if (value.WrappedValue.TryGetValue(out double number))
    {
        Console.WriteLine($"{name}: {number} ({value.StatusCode})");
    }
    else
    {
        Console.WriteLine($"{name}: unexpected value {value.WrappedValue}");
    }
}
```

## 4. Check the result

Run the client:

```bash
dotnet run
```

The client prints output like this and exits with exit code 0:

```text
Heating namespace index: 2
Temperature: 21.1 (Good [0x00000000])
Setpoint: 21 (Good [0x00000000])
Write Setpoint 22.5: Good [0x00000000]
Write Setpoint 35: BadOutOfRange [0x803C0000]
Boost 2: new setpoint 24.5
Boost 10: BadOutOfRange [0x803C0000]
```

The temperature varies, and the number format follows your system's culture.
The server rejects the setpoint 35 and the boost to 34.5, because both are above
30.

The server keeps its state between client runs: if you run the client again, the
setpoint starts at 24.5. Restart the server to start again from 21. When you are
done, press Ctrl+C in the server terminal.

## What the code does

| Step | Code | Learn more |
| --- | --- | --- |
| Find the namespace | `session.NamespaceUris.GetIndex(Heating.Namespaces.Heating)` returns the index that this server assigned to the model's namespace URI. `Heating.Namespaces.Heating` is a generated constant. Do not hard-code namespace indexes: they can differ between servers. | [Concepts: node identity and namespaces](Concepts.md#node-identity-and-namespaces) |
| Find the nodes | `TranslateBrowsePathsToNodeIdsAsync` follows browse names from the `Objects` folder. It returns the `NodeId` of each path, or a Bad status such as `BadNoMatch`. The server chooses the `NodeId`s of instances; browse paths are the same on every server that implements the model. | [Concepts: the address space](Concepts.md#the-address-space) |
| Read | `ReadValuesAsync` reads several values in one request, and returns a `DataValue` and a `ServiceResult` for each. | [Concepts: values, status codes, and timestamps](Concepts.md#values-status-codes-and-timestamps) |
| Write | `WriteAsync` returns one status code for each value in the request. The request succeeds even when the server rejects a value. The value must have the variable's data type: `22.0` is a `Double`, but `22` is an `Int32`, which fails with `BadTypeMismatch`. | [Concepts: service results and errors](Concepts.md#service-results-and-errors) |
| Call | `ThermostatTypeClient` is generated from `ThermostatType`. Its `BoostAsync` method calls `Boost` with typed arguments, and throws a `ServiceResultException` when the call fails. | [Methods with arguments](NodeManagers.md#methods-with-arguments--typed-oncall-overloads) (server side) |
| Implement nodes | The generated typed builder attaches `PollEvery`, `OnRead`, `OnWrite`, and `OnCall` callbacks to the model's nodes. | [Typed model traversal](NodeManagers.md#typed-model-traversal--the-configureimanagernodemanagerbuilder-partial) |

## Troubleshooting

| Symptom | Cause | What to do |
| --- | --- | --- |
| The client build fails with `error CS0234: The type or namespace name 'Server' does not exist in the namespace 'Opc.Ua'`. | The generator creates server-side helpers, which need the server package. | Set `ModelSourceGeneratorOmitFluentApi` to `true` in the client project. |
| The server build fails with `error CS0234: The type or namespace name 'BoostMethodState' does not exist in the namespace 'Heating'`. | With `2.0.0-preview.5`, the model declares the method arguments on the method inside `ThermostatType`. | Declare the arguments in a method type, as in [step 1](#1-describe-the-model). |
| The server build fails with `error MODELGEN035: The NamespaceUri expression ... could not be resolved during source generation`. | `[NodeManager]` uses a generated constant, such as `Namespaces.Heating`. | Use the namespace URI as a string literal. |
| `The server does not expose the Heating model.` | The server does not register the node manager, or the model uses another namespace URI. | Check the `AddNodeManager` call in the server, and the `TargetNamespace` in the model. |
| `OPC UA error BadNoMatch [0x806F0000]: A browse path was not found.` | A browse name in the client does not match the model. Browse names are case-sensitive. | Use the generated `Heating.BrowseNames` constants. |
| A write fails with `BadTypeMismatch`. | The value's data type differs from the variable's data type. | Write a `Double`, such as `22.0`. |
| `BadSecurityChecksFailed` or `BadCertificateUntrusted` | The applications do not trust each other. | Repeat [step B3 of Getting started](GettingStarted.md#b3-trust-each-application). |

## Next steps

- Monitor the temperature: pass `temperatureId` to `SubscribeDataChangesAsync`,
  as the Getting started client does for the server clock. See
  [Subscriptions](Subscriptions.md#streaming-subscriptions).
- Learn more about generated node managers: NodeSet2 models, instances created
  at run time, events, and alarms. See
  [Source-generated node managers](NodeManagers.md#source-generated-node-managers).
- Load models into a running server without generated code. See
  [Runtime NodeSets](RuntimeNodeSets.md#quick-start-examples).
- Define your own structures. See
  [Source-generated data types](SourceGeneratedDataTypes.md).
- Use a standard model, such as Device Integration. See
  [Use companion models](README.md#4-use-companion-models).
- Before you deploy, work through the
  [production readiness checklist](ProductionChecklist.md).
