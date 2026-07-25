# Developing WoT protocol bindings

This guide explains how to add a protocol binding to the WoT Connectivity runtime from form identification through live value exchange, registration, diagnostics, tests, packaging, and NativeAOT validation. The current worked implementation is [`MemoryWotBinding.cs`](../src/Opc.Ua.WotCon.Bindings/Samples/MemoryWotBinding.cs), and the production HTTP, Modbus TCP, OPC UA, and MQTT implementations provide protocol-specific examples.

## Package and project layout

The plural `Bindings` name is part of every current artifact and namespace. Do not add new references to the retired singular `Opc.Ua.WotCon.Binding*` names.

| Project or namespace | Package or assembly | Availability | Purpose |
| --- | --- | --- | --- |
| `src/Opc.Ua.WotCon.Bindings` | `OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings` / `Opc.Ua.WotCon.Bindings` | `net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0` | Binding contracts, plan model, form extraction, diagnostics, codecs, credential abstractions, all built-in planners, and the registry. |
| `Opc.Ua.WotCon.Bindings.Http` | Included in the base Bindings package | `net8.0`, `net9.0`, `net10.0` only | HTTP executor and options. |
| `Opc.Ua.WotCon.Bindings.Modbus` | Included in the base Bindings package | `net8.0`, `net9.0`, `net10.0` only | Modbus TCP executor, client, addressing, and conversion. |
| `Opc.Ua.WotCon.Bindings.OpcUa` | Included in the base Bindings package | `net8.0`, `net9.0`, `net10.0` only | OPC UA-to-OPC UA executor and options. |
| `src/Opc.Ua.WotCon.Bindings.Mqtt` | `OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings.Mqtt` / `Opc.Ua.WotCon.Bindings.Mqtt` | `net8.0`, `net9.0`, `net10.0` | MQTT executor kept separate because it depends on MQTTnet. |
| `src/Opc.Ua.WotCon.Server/Materialization` | `OPCFoundation.NetStandard.Opc.Ua.WotCon.Server` | Full library TFM matrix | Generic projection runtime that wires compiled forms to runtime-loaded OPC UA variables. |

The base Bindings package keeps its full TFM matrix. Its planner and abstraction APIs are available on every target, but the concrete HTTP, Modbus, and OPC UA executor namespaces are compiled only for `net8.0`, `net9.0`, and `net10.0`. MQTT remains a separate package and also targets `net8.0`, `net9.0`, and `net10.0`.

## Architecture and lifecycle

The binding pipeline separates pure document processing from transport I/O:

1. `WotFormExtractor` parses property, action, and event forms into immutable `WotAffordanceForm` values. It applies default WoT `op` values, inherits Thing-level security when a form has no override, clones the form and affordance JSON, and records RFC 6901 JSON Pointers.
2. Every `IWotProtocolBinder` exposes a stable `WotBindingIdentity`, a version-pinned `WotBindingCapability`, deterministic `IWotBindingIdentification`, and an `IWotBindingPlanner`.
3. `WotProtocolBinderRegistry.Prepare` validates protocol-neutral target mapping, selects one binder for each form, and calls its planner without performing transport I/O.
4. The planner validates protocol vocabulary and addressing, then emits one immutable `WotCompiledForm` per supported operation. A compiled form carries endpoint, addressing, operation, payload, secret-free credential references, target mapping, and executability.
5. The materialization coordinator converts a dependency closure to runtime NodeSet2 content and passes its plans in `WotProjectionDocument.BindingPlans`.
6. After the NodeSet is imported, `WotProjectionBindingRuntimeFactory` wires the compiled forms to target variables. Wiring is synchronous and performs no transport I/O.
7. On first read or write, `WotBindingChannelSlot` asks `IWotBindingChannelFactory.OpenChannelAsync` for a live channel. The registry resolves the matching `IWotBindingExecutor` and creates a `WotExecutorContext` containing credentials, codecs, and bounds.
8. The runtime NodeSet generation owns the resulting `IAsyncDisposable` binding runtime. The runtime owns every lazily opened channel and disposes them when that generation drains and is removed.

`IWotBinderRegistry.ActivateAsync` is called only after the new projection becomes active. On replacement, the shadow switch succeeds before the coordinator deactivates the old plans and activates the new plans. The old runtime NodeSet generation can continue serving its existing monitored items until they drain; its generation-owned channels are not disposed until that old generation is removed. If conversion, wiring, or shadow activation fails, the previous active generation remains available.

## Identification and capability

Use a stable binder id and a version that identifies the planner behavior. `WotBindingIdentity.Key` is `id@version`, and multiple versions can coexist. Executor lookup first uses the exact key and then the id-level default.

`WotBindingCapability` must accurately describe the version-pinned source document, operations, content types, and whether the binding has a runtime implementation. The capability is projected to `WoTBindingCapabilityDataType`, advertised by the registry, and included in unchanged-generation decisions.

Identification must be deterministic. `WotProtocolBinderBase.MatchStandard` implements the normal precedence: an explicit resource pin is stronger than a vocabulary match, which is stronger than a URI-scheme match. The registry evaluates binders in ordinal `id@version` order and uses that order to break equal-priority matches. Override `Match` directly when the protocol also requires a subprotocol or a pinned shape rule.

Do not claim a form merely because its URI scheme is vaguely related to the protocol. A false positive prevents a better binder from compiling the form and turns a protocol-selection problem into misleading planner diagnostics.

## Form extraction and vocabulary terms

`WotAffordanceForm.FormElement` contains the form object and is where protocol-specific form vocabulary normally belongs. `AffordanceElement` contains the owning property, action, or event. Use `TryGetString`, `TryGetBoolean`, `TryGetInt32`, and `TryGetStringArray` instead of deserializing arbitrary objects or using reflection.

The planner should validate every term it consumes, reject contradictory terms, enforce `WotBindingBounds`, and report diagnostics at `form.Pointer("term")`. Use `form.AffordancePointer("term")` only for terms defined on the owning affordance. Unknown terms from a pinned vocabulary should produce `UnknownVocabularyTerm` when accepting them could change behavior.

`WotFormExtractor` emits a formless descriptor for an affordance with no `forms` array. This intentionally makes strict materialization reject an affordance that has no executable route instead of silently ignoring it.

## OPC 10101 target mapping

[OPC 10101 section 6.5.4](https://reference.opcfoundation.org/specs/OPC-10101/6.5.4) defines generic OPC UA vocabulary terms for annotating Thing Descriptions. [Section 8.2](https://reference.opcfoundation.org/specs/OPC-10101/8.2) demonstrates that the mapping vocabulary is not limited to OPC UA source forms: its example maps properties from a Modbus energy meter into an OPC UA data model.

The runtime implements the following affordance-level semantics:

* `uav:mapToNodeId` identifies the exact OPC UA target variable.
* `uav:mapToType` identifies the target variable by its OPC UA `DataType`; resolution requires a unique variable of that type.
* When both are present, the exact node is resolved and its `DataType` must equal `uav:mapToType`.
* `uav:mapByFieldPath` maps a property to a field within a structured target and is valid only together with `uav:mapToType`.
* All three terms belong on a property affordance. Authoring them inside an individual form is invalid, and authoring them on an action or event is invalid.
* Values must be non-empty strings. The registry validates these rules before any protocol planner runs and copies one `WotTargetMappingDescriptor` to every compiled operation for that property.

This direct mapping is valid because the target term is a sibling of `forms` on the property affordance:

```json
{
  "properties": {
    "temperature": {
      "type": "number",
      "uav:mapToNodeId": "nsu=urn:example:aggregate;s=Device1.Temperature",
      "forms": [
        {
          "href": "https://sensor.example.test/temperature",
          "op": "readproperty"
        }
      ]
    }
  }
}
```

A structured mapping puts both type and field path on the property:

```json
{
  "properties": {
    "lineVoltage": {
      "type": "number",
      "uav:mapToType": "nsu=urn:example:types;s=EnergyMeasurementsType",
      "uav:mapByFieldPath": "VoltageL1N",
      "forms": [
        {
          "href": "modbus+tcp://meter.example.test",
          "op": "readproperty",
          "modv:entity": "holdingregister",
          "modv:address": 100
        }
      ]
    }
  }
}
```

Moving any `uav:mapTo*` or `uav:mapByFieldPath` member inside the form object is invalid even if the form uses the OPC UA protocol.

Use portable `nsu=` NodeIds whenever documents can move between servers whose namespace indexes differ. `WotTargetVariableResolver` parses `uav:mapToNodeId` and `uav:mapToType` with `ExpandedNodeId.Parse(text, builder.Context.NamespaceUris)`, so `nsu=urn:vendor:model;s=Device1.Value` resolves against the materialized generation's namespace table. A numeric `ns=` identifier is valid only when the author controls the target server's namespace-index assignment.

Target mapping is protocol-neutral. The form can address HTTP, Modbus, MQTT, OPC UA, or a custom protocol while the affordance maps the resulting value to an OPC UA variable. Protocol planners must not parse, reinterpret, or discard `uav:mapToNodeId`, `uav:mapToType`, or `uav:mapByFieldPath`.

## Planner validation and compiled forms

Deriving from `WotProtocolBinderBase` provides helpers for common work:

* `RequireHref` validates presence and `MaxUriLength`.
* `TryParseUri`, `SchemeOf`, `MakeEndpoint`, and `MakeEndpointOrSynthetic` normalize endpoint metadata.
* `ResolveOperations` validates affordance/operation compatibility, filters unsupported operations, and avoids duplicate teardown entries.
* `ResolveCodec` selects a codec and creates `WotPayloadDescriptor`.
* `ResolveSecurity` converts document security definitions into secret-free `WotCredentialReference` values.

Return `WotBindingCompilation.Unsupported(...)` when the binder cannot produce any valid entry. Return `Supported(entries, diagnostics)` only when entries are non-empty and there are no error diagnostics. The registry treats a compilation with errors as unsupported even if entries were returned.

Keep `WotCompiledForm` immutable and transport-neutral. Put protocol additions in the `Metadata` dictionaries of `WotEndpointDescriptor`, `WotAddressingDescriptor`, `WotOperationDescriptor`, or `WotPayloadDescriptor`. Do not store open clients, mutable protocol state, credentials, delegates, or disposable resources in a plan.

A planner can ship without an executor. The registry still validates and compiles its forms but marks its entries non-executable and the non-strict projection degraded. This is the preferred path for landing a validator before the transport runtime is ready.

## Executors, channels, and disposal

`IWotBindingExecutor.CanExecute` should reject compiled forms for another identity. `ActivateAsync` receives one immutable compiled form and a `WotExecutorContext`; it returns a live `IWotBindingChannel`.

The channel implements read, write, invoke, property observation, event subscription, and asynchronous disposal. Unsupported operations return `BadNotSupported` instead of throwing. Transport failures should be translated into deterministic `StatusCode` results; cancellation requested by the caller should normally remain cancellation, while an executor-owned timeout should become `BadTimeout`.

The projection runtime opens channels lazily. One `WotBindingChannelSlot` is shared for each compiled-form object within a generation, concurrent first use opens exactly once, a failed open is evicted for retry, and one caller's cancellation does not cancel the generation-scoped open for other callers. Disposal marks the slot closed before awaiting an in-flight open, then disposes any successfully created channel. Channel disposal must be idempotent, and subscription disposal must stop delivery and release its transport resources.

Do not create transport connections in the planner, binder constructor, or DI registration callback unless the executor itself explicitly owns a long-lived pooled client. Prefer an injectable client/session factory in options, as the built-in executors do.

## Payload codecs

The default `WotPayloadCodecRegistry` contains reflection-free JSON, text, and octet-stream codecs. A planner records only the codec id and payload metadata; a channel selects the codec from `WotExecutorContext.Codecs` when it encodes or decodes.

Custom codecs implement `IWotPayloadCodec` and return `WotEncodeResult` or `WotDecodeResult` rather than throwing for expected malformed input. Register custom codecs ahead of the built-ins with `WotPayloadCodecRegistry.Register`, or provide an `IWotCodecRegistry` through DI. Keep codecs deterministic, bounded, culture-invariant, and free of runtime type discovery.

## Credentials and trust

Thing Descriptions and registry nodes contain only `WotSecurityDefinition` and `WotCredentialReference` data. Actual headers, query values, usernames, passwords, certificates, and trust anchors are resolved at channel activation or request time through `IWotCredentialProvider`.

Register a provider with `AddWotCredentialProvider`. Scope credentials by the reference's scheme name, binding URI, and endpoint. Fail closed when a form declares security but the provider cannot resolve the required material. Never serialize `WotCredential`, cache secret text in `WotCompiledForm`, or include secrets in diagnostics.

## Registration

The direct-construction path is useful in focused tests:

```csharp
var store = new MemoryWotStore();
var registry = new WotProtocolBinderRegistry(
    new IWotProtocolBinder[] { new MemoryWotBinder() },
    new IWotBindingExecutor[] { new MemoryWotBindingExecutor(store) });
```

The normal host path uses `IOpcUaBuilder` extensions:

```csharp
MemoryWotStore store = new();

IOpcUaBuilder opcUa = services
    .AddOpcUa()
    .AddServer(server => { /* server configuration */ })
    .AddWotRegistryServer(options => options.StrictBindings = false);

opcUa
    .AddWotBinder(new MemoryWotBinder())
    .AddWotBindingExecutor(new MemoryWotBindingExecutor(store))
    .AddWotCredentialProvider(NullWotCredentialProvider.Instance);
```

`EnsureWotBinderRegistry` registers one `WotProtocolBinderRegistry` singleton and exposes that same instance as both `IWotBinderRegistry` and `IWotBindingChannelFactory`, independent of registration order. A custom binding package should expose one fluent `Add<Protocol>WotBinding` method that creates options, calls `AddWotProtocolBinders` or `AddWotBinder`, and registers its executor.

## Monitoring and local sampling

For a target-mapped variable, the generic projection runtime wires executable `readproperty` and `writeproperty` forms to async `OnRead` and `OnWrite` handlers. Local OPC UA monitored items sample that same read handler. An `observeproperty` entry does not create a second upstream observe bridge for target mapping, so a binding must provide a reliable and bounded read operation even when its native protocol also supports push observation.

Outside target mapping, callers can use `IWotBindingChannel.ObserveAsync` or `SubscribeEventAsync` directly. The returned `IWotSubscription` owns the native subscription or polling loop and must stop it in `DisposeAsync`.

## Structured target mapping

Direct mapping reads or writes the whole target value. Structured mapping groups forms by target variable and field path. Reads run all mapped field reads concurrently, build nested `IStructure` instances without reflection, and return one `ExtensionObject`. Writes extract each mapped field and run the field writes concurrently.

The runtime rejects a target that mixes direct and field mappings, duplicate read mappings for the same field, duplicate write mappings for the same field, and target-mapped operations other than read, write, or observe. A failed field fails the entire structured operation. A successful structured read preserves a non-default Good status when present and uses the oldest available source timestamp.

Structure type and field-path resolution is delayed until first structured use because runtime NodeSet configuration completes before custom encodeable types are registered in the shared factory. Failed resolution is not cached; later operations retry. Until resolution succeeds, the read or write returns `BadConfigurationError`.

## Status and error mapping

Return a `WotReadResult`, `WotWriteResult`, or `WotInvokeResult` for expected protocol outcomes. Reserve exceptions for invalid API use, cancellation, resource construction failures, and conditions that prevent a channel from being opened.

| Condition | Recommended status |
| --- | --- |
| Unsupported channel operation | `BadNotSupported` |
| Payload encode/decode failure | `BadEncodingError` / `BadDecodingError` |
| Executor-owned timeout | `BadTimeout` |
| Network or broker failure | `BadCommunicationError` |
| Missing protocol target | `BadNodeIdUnknown` or a protocol-specific mapped status |
| Invalid compiled address | `BadNodeIdInvalid` or `BadInvalidArgument` |
| Authentication or authorization rejection | `BadUserAccessDenied` |
| Response exceeds configured bounds | `BadEncodingLimitsExceeded` |
| Invalid runtime mapping or structured configuration | `BadConfigurationError` |

Preserve a source protocol's meaningful OPC UA status and timestamps when the source is OPC UA. Do not expose credentials or stack traces through `Error`; use concise operator-safe text and server-side telemetry for detailed exceptions.

## Complete memory binding

The following is the complete pattern used by the checked-in sample. It supports `mem://` property read, write, and polling-based observation.

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Bindings;

public sealed class MemoryWotBinder : WotProtocolBinderBase
{
    public const string BindingUri = "urn:example:wot:mem";

    private static readonly string[] s_schemes = { "mem" };

    public override WotBindingIdentity Identity { get; } =
        new("example.mem", "1.0", BindingUri, "Sample In-Memory Binding");

    public override WotBindingCapability Capability { get; } = new(
        BindingUri,
        "Sample In-Memory Binding",
        new WotBindingSource(
            "urn:example:wot:mem",
            "1.0",
            WotBindingMaturity.UnofficialDraft,
            note: "A sample custom binding for documentation and tests."),
        new[]
        {
            WoTBindingCapabilityEnum.ReadProperty,
            WoTBindingCapabilityEnum.WriteProperty,
            WoTBindingCapabilityEnum.ObserveProperty
        },
        new[] { "application/json", "text/plain" },
        isExecutable: true);

    protected override IReadOnlyCollection<string> Schemes => s_schemes;

    public override WotBindingMatch Match(
        WotAffordanceForm form,
        WotBindingSelectionContext context)
        => MatchStandard(form, context, "memv:");

    public override WotBindingCompilation Compile(
        WotAffordanceForm form,
        WotBindingPlanContext context)
    {
        var diagnostics = new List<WotBindingDiagnostic>();
        if (!RequireHref(form, context, diagnostics, out string href) ||
            !TryParseUri(href, out Uri uri) ||
            !string.Equals(uri.Scheme, "mem", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(WotBindingDiagnostic.Error(
                WotBindingDiagnosticCode.InvalidHref,
                "The href is not a valid mem:// URI.",
                form.Pointer("href")));
            return WotBindingCompilation.Unsupported(diagnostics.ToArray());
        }

        string key = uri.AbsolutePath.Trim('/');
        ResolveCodec(form, context, out WotPayloadDescriptor payload);
        WotEndpointDescriptor endpoint = MakeEndpoint(uri);
        var addressing = new WotAddressingDescriptor(key);
        var entries = ImmutableArray.CreateBuilder<WotCompiledForm>();

        foreach ((string op, WoTBindingCapabilityEnum capability) in
            ResolveOperations(form, diagnostics))
        {
            var operation = new WotOperationDescriptor(
                capability,
                op,
                capability.ToString());
            entries.Add(new WotCompiledForm(
                Identity,
                form.Kind,
                form.AffordanceName,
                form.JsonPointer,
                capability,
                op,
                endpoint,
                addressing,
                operation,
                payload,
                ImmutableArray<WotCredentialReference>.Empty,
                Capability.IsExecutable));
        }

        return entries.Count == 0
            ? WotBindingCompilation.Unsupported(diagnostics.ToArray())
            : WotBindingCompilation.Supported(
                entries.ToImmutable(),
                diagnostics.ToImmutableArray());
    }
}

public sealed class MemoryWotStore
{
    public DataValue Get(string key)
        => m_values.TryGetValue(key, out DataValue value)
            ? value
            : new DataValue(Variant.Null);

    public void Set(string key, DataValue value)
        => m_values[key] = value;

    private readonly ConcurrentDictionary<string, DataValue> m_values =
        new(StringComparer.Ordinal);
}

public sealed class MemoryWotBindingExecutor : IWotBindingExecutor
{
    public MemoryWotBindingExecutor(MemoryWotStore store)
    {
        m_store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public WotBindingIdentity Identity { get; } =
        new("example.mem", "1.0", MemoryWotBinder.BindingUri, "Sample In-Memory Executor");

    public bool CanExecute(WotCompiledForm form)
        => form is not null &&
            string.Equals(form.Binding.Id, Identity.Id, StringComparison.Ordinal);

    public ValueTask<IWotBindingChannel> ActivateAsync(
        WotCompiledForm form,
        WotExecutorContext context,
        CancellationToken cancellationToken = default)
    {
        if (form is null)
        {
            throw new ArgumentNullException(nameof(form));
        }
        IWotBindingChannel channel = new MemoryWotBindingChannel(m_store, form);
        return new ValueTask<IWotBindingChannel>(channel);
    }

    private readonly MemoryWotStore m_store;
}

internal sealed class MemoryWotBindingChannel : IWotBindingChannel
{
    public MemoryWotBindingChannel(MemoryWotStore store, WotCompiledForm form)
    {
        m_store = store;
        Form = form;
        m_key = form.Addressing.Target;
    }

    public WotCompiledForm Form { get; }

    public ValueTask<WotReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
        => new(new WotReadResult(StatusCodes.Good, m_store.Get(m_key)));

    public ValueTask<WotWriteResult> WriteAsync(
        DataValue value,
        CancellationToken cancellationToken = default)
    {
        m_store.Set(m_key, value);
        return new ValueTask<WotWriteResult>(
            new WotWriteResult(StatusCodes.Good));
    }

    public ValueTask<WotInvokeResult> InvokeAsync(
        IReadOnlyList<Variant> inputs,
        CancellationToken cancellationToken = default)
        => new(new WotInvokeResult(
            StatusCodes.BadNotSupported,
            null,
            "The sample binding has no actions."));

    public ValueTask<IWotSubscription> ObserveAsync(
        Action<WotNotification> onNotification,
        CancellationToken cancellationToken = default)
    {
        if (onNotification is null)
        {
            throw new ArgumentNullException(nameof(onNotification));
        }
        IWotSubscription subscription = new PollingWotSubscription(
            Form,
            token =>
            {
                onNotification(new WotNotification(m_store.Get(m_key)));
                return default;
            },
            TimeSpan.FromMilliseconds(200));
        return new ValueTask<IWotSubscription>(subscription);
    }

    public ValueTask<IWotSubscription> SubscribeEventAsync(
        Action<WotNotification> onEvent,
        CancellationToken cancellationToken = default)
        => ObserveAsync(onEvent, cancellationToken);

    public ValueTask DisposeAsync() => default;

    private readonly MemoryWotStore m_store;
    private readonly string m_key;
}
```

### Memory-binding tests

The positive test compiles a TD, selects the generated read and write entries, opens channels through the registry, and verifies round-trip behavior:

```csharp
[Test]
public async Task SampleBinderCompilesAndExecutesReadWrite()
{
    var store = new MemoryWotStore();
    var registry = new WotProtocolBinderRegistry(
        new IWotProtocolBinder[] { new MemoryWotBinder() },
        new IWotBindingExecutor[] { new MemoryWotBindingExecutor(store) });

    const string td =
        """
        {
          "@context": "https://www.w3.org/2022/wot/td/v1.1",
          "title": "Memory device",
          "properties": {
            "setpoint": {
              "type": "number",
              "forms": [{ "href": "mem://store/setpoint" }]
            }
          }
        }
        """;

    WotBindingPlan plan = registry.Prepare(
        WotBindingPlanRequest.FromDocument(
            "memory-device",
            WoTDocumentKindEnum.ThingDescription,
            Encoding.UTF8.GetBytes(td)));

    Assert.That(plan.FullySupported, Is.True);
    Assert.That(plan.HasExecutableForms, Is.True);

    WotCompiledForm write = plan.CompiledForms.Single(
        form => form.Operation == WoTBindingCapabilityEnum.WriteProperty);
    WotCompiledForm read = plan.CompiledForms.Single(
        form => form.Operation == WoTBindingCapabilityEnum.ReadProperty);

    IWotBindingChannel writeChannel = await registry.OpenChannelAsync(write);
    await using (writeChannel.ConfigureAwait(false))
    {
        WotWriteResult result = await writeChannel.WriteAsync(
            new DataValue(new Variant(42.5)));
        Assert.That(result.Success, Is.True);
    }

    IWotBindingChannel readChannel = await registry.OpenChannelAsync(read);
    await using (readChannel.ConfigureAwait(false))
    {
        WotReadResult result = await readChannel.ReadAsync();
        Assert.That(result.Success, Is.True);
        Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(42.5));
    }
}
```

Add a diagnostic test so malformed input remains actionable:

```csharp
[Test]
public void SampleBinderReportsInvalidHrefAtTheFormPointer()
{
    var registry = new WotProtocolBinderRegistry(
        new IWotProtocolBinder[] { new MemoryWotBinder() });

    const string td =
        """
        {
          "title": "Invalid memory device",
          "properties": {
            "setpoint": {
              "forms": [{ "href": "mem://[invalid" }]
            }
          }
        }
        """;

    WotBindingPlan plan = registry.Prepare(
        WotBindingPlanRequest.FromDocument(
            "invalid-memory-device",
            WoTDocumentKindEnum.ThingDescription,
            Encoding.UTF8.GetBytes(td)));

    Assert.That(plan.FullySupported, Is.False);
    Assert.That(
        plan.Diagnostics.Any(d =>
            d.Code == WotBindingDiagnosticCode.InvalidHref &&
            d.JsonPointer == "/properties/setpoint/forms/0/href"),
        Is.True);
}
```

The checked-in equivalent is [`WotCustomBinderSampleTests.cs`](../Tests/Opc.Ua.WotCon.Tests/Binding/WotCustomBinderSampleTests.cs). Protocol executor tests belong in [`tests/Opc.Ua.WotCon.Bindings.Tests`](../tests/Opc.Ua.WotCon.Bindings.Tests), while planner, registry, target-mapping, and materialization tests belong in [`Tests/Opc.Ua.WotCon.Tests`](../Tests/Opc.Ua.WotCon.Tests).

## NativeAOT and trimming

Binding code must remain compatible with trimming and NativeAOT. Parse form vocabulary with `JsonElement`; do not use runtime assembly scanning, unbounded reflection, `Type.GetType`, dynamic code generation, or serializer overloads that require runtime metadata. Use source-generated JSON contexts when a protocol needs typed JSON beyond the built-in scalar codec.

Keep plan objects data-only and immutable. Inject transport factories and credential providers instead of locating services dynamically. Ensure asynchronous cleanup does not depend on finalizers. If a dependency is not annotated as AOT-compatible, add a NativeAOT smoke path that exercises every used feature.

The base Bindings project sets `IsAotCompatible` for compatible `net10.0` builds, and the aggregation samples publish with `PublishAot` on `net10.0`. Validate a new concrete executor with a `net10.0` build and, when it participates in a sample or app, a real `dotnet publish -f net10.0 -r <rid>`.

## Packaging and TFM decisions

Keep protocol abstractions and planners in the base Bindings project when they can compile across the full library matrix without a transport dependency. Place a concrete executor in the base project only when its dependencies are already suitable for the bundled `net8.0+` build, as with HTTP, Modbus TCP, and OPC UA. Use a separate package when the executor introduces an optional external dependency, as MQTT does.

Conditionally exclude executor source on older TFMs rather than reducing the base package's TFM matrix. Public documentation and package README files must state both facts: the package is available on all library TFMs, and the concrete executor namespaces exist only on `net8.0+`.

## Contributor checklist

- [ ] Use plural `Opc.Ua.WotCon.Bindings` project, package, and namespace names.
- [ ] Pin an authoritative binding source and version in `WotBindingSource`.
- [ ] Choose a stable binder id, version, binding URI, display name, and capability set.
- [ ] Implement deterministic identification and verify tie/pin behavior.
- [ ] Validate required vocabulary, conflicts, bounds, operations, content types, and security references without transport I/O.
- [ ] Emit immutable compiled forms with precise endpoint, address, operation, payload, credential, and JSON Pointer data.
- [ ] Leave OPC 10101 target mapping to the protocol-neutral registry/runtime.
- [ ] Implement an executor only for operations the transport can actually perform.
- [ ] Map expected failures to OPC UA status codes and keep caller cancellation distinct from executor timeout.
- [ ] Resolve credentials out of band and verify that diagnostics never contain secrets.
- [ ] Make channels, subscriptions, and in-flight activation safe under asynchronous disposal.
- [ ] Register direct-construction and DI/fluent paths.
- [ ] Add planner, diagnostics, executor, concurrency, disposal, and security tests.
- [ ] Test local monitored-item sampling when the binding is used through target mapping.
- [ ] Test direct and structured mappings when the protocol is intended for aggregation.
- [ ] Verify all supported TFMs, `net10.0` trimming/AOT behavior, package contents, and README accuracy.

## Testing matrix

| Area | Required cases |
| --- | --- |
| Identification | Scheme match, vocabulary match, explicit pin, no match, deterministic tie, multiple binder versions. |
| Form extraction | Default operations, form operation override, Thing-level security fallback, relative/base URI behavior if supported, formless affordance. |
| Planner validation | Valid form, missing/invalid href, incompatible operation, unsupported content type, missing term, invalid term shape/range, conflicting terms, configured bounds. |
| Diagnostics | Stable code, severity, offending term, exact RFC 6901 form or affordance pointer, no secret leakage. |
| Compiled plan | Endpoint, addressing, operation, payload, security references, target mapping, executable/non-executable state. |
| Codec | Encode/decode round trip, malformed payload, empty payload, maximum payload, culture independence. |
| Credentials | No-security path, missing required credential, correct endpoint scoping, secure transport, explicit rejection of unsafe downgrade. |
| Executor | Read, write, invoke, observe, event, every intentionally unsupported operation, source status/timestamp preservation. |
| Failure mapping | Timeout, cancellation, connection loss, protocol error, encode/decode failure, authentication failure, oversized response. |
| Concurrency | Concurrent first channel use opens once, failed open retries, parallel operations obey transport rules. |
| Disposal | Never-opened channel, successfully opened channel, failed open, in-flight open racing disposal, subscription partial-construction failure, repeated disposal. |
| Target mapping | Affordance-level direct mapping, `nsu=` mapping, forms-level rejection, action/event rejection, field path requires type, direct/field conflict, duplicate field direction. |
| Structured mapping | Nested fields, unknown field, non-structure intermediate, array-valued intermediate, one failed field, status/timestamp aggregation. |
| Materialization | Strict rejection, non-strict degradation, successful activation, failed shadow replacement retaining old generation, old monitored-item drain. |
| Packaging | Full base TFM matrix, executor source absent before `net8.0`, MQTT separate package, package README and dependency graph. |
| AOT/trimming | `net10.0` analyzer-clean build and NativeAOT publish/run smoke test for the concrete executor path. |

## Related documentation

* [WoT Connectivity protocol bindings](WoTProtocolBindings.md)
* [WoT aggregation sample](WoTAggregationSample.md)
* [WoT Connectivity model, server, registry, and client](WoTConnectivity.md)
* [Dependency injection](DependencyInjection.md)
* [Runtime NodeSets](RuntimeNodeSets.md)
