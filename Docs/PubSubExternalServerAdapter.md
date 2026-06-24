# PubSub external server adapter

`Opc.Ua.PubSub.Adapter` connects the Part 14 PubSub runtime to an external OPC UA server by using `Opc.Ua.Client.ManagedSession`. It is a client-session binding package: the PubSub process remains a publisher, subscriber, or Action responder, while the source variables, target variables, or methods live in another OPC UA server.

Use this package when you need to bridge an existing server into PubSub without hosting that server in the same process. Use `Opc.Ua.PubSub.Server` for the in-process server address-space integration that exposes the standard Part 14 `PublishSubscribe` Object and binds to node managers directly.

## Package and namespaces

| Item | Value |
| ---- | ----- |
| Assembly | `Opc.Ua.PubSub.Adapter` |
| NuGet package | `OPCFoundation.NetStandard.Opc.Ua.PubSub.Adapter` |
| Main namespaces | `Opc.Ua.PubSub.Adapter`, `Opc.Ua.PubSub.Adapter.Session`, `Opc.Ua.PubSub.Adapter.Actions`, `Opc.Ua.PubSub.Adapter.DependencyInjection` |
| DI entry points | `AddExternalServerPublisher`, `AddExternalServerSubscriber`, `AddExternalServerActionResponder` on `IPubSubBuilder` |

The adapter implements Part 14 DataSet and Action seams rather than a new transport. You still register UDP, MQTT, encoders, security key providers, and the PubSub configuration through the normal `AddPubSub` builder.

## Architecture

The DI extensions create one `IExternalServerSession` per adapter registration. `ExternalServerSession` wraps a lazily connected `ManagedSession`; `Read`, `Write`, `Call`, and client data-change Subscriptions all go through that managed session. `ManagedSession` owns keep-alive and reconnect behavior, so adapter components do not expose reconnect handlers or custom retry APIs.

| Direction | Configuration source | Adapter seam | Managed session service |
| --------- | -------------------- | ------------ | ----------------------- |
| External server → PubSub | `PublishedDataSetDataType` with `PublishedDataItemsDataType` | `ExternalServerPublishedDataSetSource : IPublishedDataSetSource` | `Read` or client `Subscription` data changes |
| PubSub → external server | `DataSetReaderDataType.SubscribedDataSet` as `TargetVariablesDataType` | `ExternalServerSubscribedDataSetSink` and `ExternalServerTargetVariableWriter : ITargetVariableWriter` | `Write` |
| PubSub Action → external server method | `PubSubActionTarget` plus `ExternalActionMethodMap` | `ExternalServerActionHandler : IPubSubActionHandler` | `Call` |

The PubSub configuration must be supplied before an `AddExternalServer*` extension runs. The extensions enumerate configured PublishedDataSets, DataSetWriters, DataSetReaders, TargetVariables, and action targets during application composition and then register the appropriate sources, sinks, or handlers.

```csharp
builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddPublisher()
        .AddUdpTransport()
        .UseConfigurationFile("publisher.xml")
        .AddExternalServerPublisher(options =>
        {
            options.Connection.EndpointUrl = "opc.tcp://localhost:4840";
        }));
```

## Connection options

`ExternalServerConnectionOptions` describes the client session to the external OPC UA server.

| Option | Type | Default | Notes |
| ------ | ---- | ------- | ----- |
| `EndpointUrl` | `string` | empty | Required endpoint or discovery URL, for example `opc.tcp://localhost:4840`. The session selects an advertised endpoint whose URL scheme matches this URI. |
| `SecurityMode` | `MessageSecurityMode` | `SignAndEncrypt` | Requested client/server message security mode. |
| `SecurityPolicyUri` | `string?` | `null` | Requested security policy URI. When `null`, the adapter chooses the highest-security endpoint advertised for the requested `SecurityMode`. |
| `UserIdentity` | `IUserIdentity?` | `null` | Explicit user identity. Takes precedence over `UserName` and `Password`. |
| `UserName` | `string?` | `null` | User name for username/password activation. Empty means anonymous unless `UserIdentity` is supplied. |
| `Password` | `string?` | `null` | Password used with `UserName`. |
| `SessionName` | `string` | `Opc.Ua.PubSub.Adapter` | Session name reported to the server. |
| `SessionTimeout` | `uint` | `60000` | Requested session timeout in milliseconds. |
| `ApplicationConfiguration` | `ApplicationConfiguration?` | `null` | Client application configuration used to create the session. Supply a configuration with a valid application instance certificate for secured connections. |
| `ApplicationName` | `string` | `Opc.Ua.PubSub.Adapter` | Used only when the adapter builds a minimal client configuration automatically. |

For secured connections, provide an `ApplicationConfiguration` that uses the stack certificate manager and normal trusted issuer, trusted peer, and rejected certificate stores. The automatic fallback configuration is useful for simple hosting scenarios, but production deployments should manage the client application certificate and trust lists explicitly.

## Publisher adapter

`AddExternalServerPublisher` registers an `IPublishedDataSetSource` for each configured PublishedDataSet that has a name. The PublishedDataSet must use `PublishedDataItemsDataType`; each `PublishedVariableDataType` becomes a `ReadValueId` using `PublishedVariable`, `AttributeId` (defaulting to `Attributes.Value`), and `IndexRange`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter;
using Opc.Ua.PubSub.Adapter.Session;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
ApplicationConfiguration clientConfiguration = await LoadClientConfigurationAsync();

builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddPublisher()
        .AddUdpTransport()
        .UseConfigurationFile("publisher.xml")
        .AddExternalServerPublisher(options =>
        {
            options.Connection = new ExternalServerConnectionOptions
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                ApplicationConfiguration = clientConfiguration,
                SessionName = "PubSub external publisher"
            };
            options.ReadMode = ExternalReadMode.Cyclic;
        }));

await builder.Build().RunAsync();
```

### Read modes

| Mode | Behavior | Trade-offs |
| ---- | -------- | ---------- |
| `ExternalReadMode.Cyclic` | The publish cycle issues a `Read` service call for the current PublishedDataSet variables. | Simple and predictable; every cycle requests fresh values. Network and server load scale with the publish cadence and field count. |
| `ExternalReadMode.Subscription` | The adapter creates client Subscriptions, adds monitored items for the referenced PublishedDataSet variables, maintains a latest-value cache from data-change notifications, primes that cache with one initial `Read`, and samples the cache during publish cycles. | Lower publish-path latency and server-driven updates. More lifecycle state: Subscriptions and monitored items must be created, applied, primed, and kept alive by the managed session. |

Cyclic mode is the default and is a good fit when the publish interval is modest, field counts are small, or the external server should only be sampled at the PubSub cadence. Subscription mode is a better fit when values change independently of the publish cadence, lower latency matters, or the external server can serve monitored items more efficiently than repeated Read calls.

### Subscription affinity

`ExternalSubscriptionAffinity` controls how subscription-mode monitored items are grouped.

| Affinity | Behavior | Guidance |
| -------- | -------- | -------- |
| `WriterGroup` | One client Subscription per WriterGroup. The subscription publishing interval is the WriterGroup publishing interval, or 1000 ms when the WriterGroup interval is not set. This is the default. | Prefer this for most deployments because it aligns the client/server sampling group with the Part 14 WriterGroup cadence and reduces subscription count. |
| `DataSetWriter` | One client Subscription per DataSetWriter, using the owning WriterGroup publishing interval. | Use this when writers need isolation, when a server applies per-subscription limits or diagnostics that should map to one writer, or when you want to contain noisy datasets. |

For each affinity group, the coordinator de-duplicates monitored items by node and attribute, uses `PublishedVariableDataType.SamplingIntervalHint` when set, otherwise uses the group publishing interval, applies the monitored items server-side, and then primes the cache with a one-shot `Read`. Until a value is primed or a data change arrives, the cache returns `UncertainInitialValue` for that field.

## Subscriber adapter

`AddExternalServerSubscriber` registers a sink for every configured DataSetReader whose `SubscribedDataSet` is `TargetVariablesDataType`. The normal PubSub subscriber resolves incoming DataSet fields to `FieldTargetDataType` entries; the adapter writes each resolved field to the configured external node, attribute, and write index range.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter.Session;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
ApplicationConfiguration clientConfiguration = await LoadClientConfigurationAsync();

builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddSubscriber()
        .AddMqttTransport()
        .UseConfigurationFile("subscriber.xml")
        .AddExternalServerSubscriber(options =>
        {
            options.Connection.EndpointUrl = "opc.tcp://localhost:4840";
            options.Connection.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            options.Connection.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            options.Connection.ApplicationConfiguration = clientConfiguration;
            options.Connection.SessionName = "PubSub external subscriber";
        }));

await builder.Build().RunAsync();
```

The writer is fail-soft for service and transport faults: it logs the failure and returns a Bad status for that field so the receive loop can continue. Cancellation still propagates.

## Action responder adapter

`AddExternalServerActionResponder` maps inbound PubSub Actions to external OPC UA Method Calls. `Targets` lists the `PubSubActionTarget` values that should be handled. `MethodMap` resolves each target to an external object and method, either by `(DataSetWriterId, ActionTargetId)` or by `ActionName`. Action input fields are converted to method input arguments in order. Method output arguments are converted back to Action response fields using the configured output field names; positions without names become `Output0`, `Output1`, and so on.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
ApplicationConfiguration clientConfiguration = await LoadClientConfigurationAsync();

var target = new PubSubActionTarget
{
    DataSetWriterId = 1001,
    ActionTargetId = 1,
    ActionName = "ResetMachine"
};

builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddSubscriber()
        .AddMqttTransport()
        .UseConfigurationFile("actions.xml")
        .AddExternalServerActionResponder(options =>
        {
            options.Connection.EndpointUrl = "opc.tcp://localhost:4840";
            options.Connection.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            options.Connection.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            options.Connection.ApplicationConfiguration = clientConfiguration;
            options.Targets.Add(target);
            options.MethodMap.Add(
                dataSetWriterId: 1001,
                actionTargetId: 1,
                objectId: new NodeId("ns=2;s=Machine1"),
                methodId: new NodeId("ns=2;s=Machine1.Reset"),
                outputFieldNames: new[] { "Accepted" }.ToArrayOf());
            options.AllowUnsecured = false;
        }));

await builder.Build().RunAsync();
```

Action responders honor the Part 14 security posture of the Action exchange. `AllowUnsecured` defaults to `false`; keep it false unless the deployment explicitly accepts unsecured Action requests and responses. With the default, the responder fails closed for unsecured action paths.

## Metadata behavior

Publisher metadata is configuration-first and server-fallback. The adapter builds the field set, order, and names from the configured `PublishedDataSetDataType`, its `PublishedDataItemsDataType.PublishedData`, and any declared `DataSetMetaDataType`. If a field does not declare type information, `ExternalDataSetMetaDataBuilder` reads `DataType`, `ValueRank`, and `ArrayDimensions` from the external server. If the fallback read fails, the field remains conservative: `BaseDataType`, `Variant`, scalar.

This behavior keeps Part 14 metadata stable when the configuration is complete and still lets a bridge infer missing type details from the source server during startup or the first publish sample.

## Lifecycle and resilience

The adapter registrations add `ExternalServerAdapterRuntime` and `ExternalServerAdapterHostedService`. The runtime owns sessions and subscription coordinators. On host start, subscription-mode publisher coordinators connect the session, create the client Subscriptions, add monitored items, apply changes, and prime caches. Cyclic publishers, subscribers, and action responders connect lazily on first service call. On host shutdown, coordinators and sessions are disposed.

`ManagedSession` handles keep-alive and reconnect for the underlying client session. Adapter read, write, and call components are fail-soft for ordinary service or transport faults: publisher fields become Bad-quality values, subscriber writes return Bad field status, and action failures return Bad action status. Cancellation and disposal still propagate normally.

## Security notes

The external client session uses the same stack security configuration model as other OPC UA clients. Use the certificate manager and trust stores described in [Certificates](Certificates.md) for application instance certificates, issuers, trusted peers, and rejected certificates. Prefer `MessageSecurityMode.SignAndEncrypt` with a SHA-2 security policy such as `SecurityPolicies.Basic256Sha256` or stronger policies supported by the server.

For Actions, leave `ExternalServerActionResponderOptions.AllowUnsecured` at its default `false` unless an application-specific risk assessment requires otherwise. That gate is intentionally fail-closed.

## Sample

See `Applications\ConsoleReferencePubSub` (the `external` mode) for a complete host that wires PubSub configuration, transport registration, external session options, publisher/subscriber binding, and Action-to-Call mapping in one process.

## See also

- [PubSub (Part 14)](PubSub.md)
- [Dependency Injection](DependencyInjection.md)
- [Sessions, Reconnection, and Subscription Engines](Sessions.md)
- [Certificates](Certificates.md)
- [OPC UA Part 14](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/)
