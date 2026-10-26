# UAFX information models

`OPCFoundation.NetStandard.Opc.Ua.Fx` supplies generated OPC 10000-81 model
contracts for the FX Data, AutomationComponent and ConnectionManager
namespaces. It uses the existing Device Integration model and the stack's
source generator; it adds no native runtime or third-party package.

## Register the models

Register dependencies before their consumers. The same registration order
applies to encodeable factories, node-state factories and predefined nodes.
The namespace table may already contain application namespaces.

```csharp
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Fx.Data;
using Opc.Ua.Fx.Ac;
using Opc.Ua.Fx.Cm;

messageContext.Factory.Builder
    .AddOpcUaDi()
    .AddOpcUaFxData()
    .AddOpcUaFxAc()
    .AddOpcUaFxCm()
    .Commit();

nodes.AddOpcUaDi(systemContext)
    .AddOpcUaFxData(systemContext)
    .AddOpcUaFxAc(systemContext)
    .AddOpcUaFxCm(systemContext);
```

The generated types include asynchronous `EstablishConnections` and
`CloseConnections` Method handlers and client proxies, endpoint configuration
and result structures, and PubSub communication-identifier reservation
structures. `PubSubReserveCommunicationIds2DataType` and
`PubSubReserveCommunicationIdsResult2DataType` preserve the standard
`RequestTransportSpecificInfo` and `TransportSpecificInfo` fields.

## Bind verification and reservation

`Opc.Ua.PubSub.Server` provides the experimental `PubSubFxReservationMethods`
binding for the `VerifyAssetCmd`, `VerifyFunctionalEntityCmd`, and
`ReserveCommunicationIdsCmd` commands of `EstablishConnections`. It can also
bind `CloseConnections` to a host-owned `IPubSubFxConnectionLifecycle`
transaction. The host implements that capability because it owns the endpoint
nodes, ConnectionManager authorization, and shared-resource ownership. The
binding delegates
asset verification to each addressed `IAssetRevisionType.VerifyAsset` method,
delegates functional-entity verification to each related
`FunctionalEntityType.Verify` method, and uses the application's existing
[Session-owned communication-ID transaction](PubSubCommunicationIds.md) for
reservations. The host owns the AutomationComponent instance and its NodeId;
model loading does not create or bind one automatically. Functional entities
are accepted only when their `IsHostedBy` Asset is in the bound component's
in-memory parent hierarchy; an address-space-wide type match is not sufficient.

Register the binding with the same PubSub server application:

```csharp
services.AddOpcUa()
    .AddPubSubServer(
        server => { },
        pubsub => { },
        options => options.ExposeFxModel = true)
    .WithFxConnectionLifecycle(hostConnectionLifecycle)
    .WithFxCommunicationIdReservations();

// In the host's node manager, after creating and initializing its local component:
serviceProvider.GetRequiredService<PubSubFxReservationMethods>()
    .Bind(automationComponent, ResolveActivePubSubUascListenerAsync);
```

Explicit construction is also supported:

```csharp
var reservations = new PubSubFxReservationMethods(application, pubSubServerOptions);
reservations.Bind(automationComponent);
```

Both paths require the application to implement `IPubSubCommunicationIdApplication`.
The server's PubSub node manager forwards Session closure to the reservation
provider. A standalone host must perform that forwarding itself, as described
in [PubSub communication-ID reservations](PubSubCommunicationIds.md).
Registration does not enable configuration methods, expose the model, replace
an existing handler, or implement endpoint creation/configuration/enablement.
Registering a lifecycle capability causes `Bind` to install the generated
`CloseConnections` Method handler only when that Method has no existing handler.

Calls require an actual Session with the `ConfigureAdmin` role and
`ExposeConfigurationMethods = true`. The accepted command masks are any
non-empty combination of `VerifyAssetCmd`, `VerifyFunctionalEntityCmd`, and
`ReserveCommunicationIdsCmd`; other command bits fail explicitly. Each set
command requires a non-empty input array, and each unset command requires an
empty array. `VerifyFunctionalEntityCmd` requires at least one non-empty
`ExpectedVerificationVariables` array. Its endpoint entries must select the
`ConnectionEndpointDefinitionDataType.Node` member; control groups,
configuration data, and communication links must remain unset because their
commands are not implemented by this binding. The other endpoint command
inputs and communication-configuration inputs must also remain empty. A call
is limited to 128 combined asset-verification, endpoint-verification, and
reservation requests.

Asset verification preserves input order, invokes each asset's
`VerifyAsset` method with the supplied mode and expected-variable arrays, and
returns the method status and outputs in `AssetVerificationResults`. An unknown
asset reports `BadNodeIdUnknown`; a node that is not an `IAssetRevisionType`
reports `BadInvalidArgument`. The binding processes the complete verification
array before deciding whether later commands can run. A bad method status, or
`NotSet` or `Mismatch` result always aborts subsequent commands. A `Compatible`
result also aborts when `ExpectedVerificationResult` is `Match`. Unexecuted
reservation results report `BadNothingToDo` and the `EstablishConnections`
Method returns `Uncertain`.

Functional-entity verification preserves endpoint-configuration order. An
empty `ExpectedVerificationVariables` array returns `Good_NoData`, `NotSet`,
and no variable errors without calling `Verify`. Non-empty arrays are passed
unchanged to the target `FunctionalEntityType.Verify` Method. Missing nodes
report `BadNodeIdUnknown` and `Mismatch`; wrong-type or unrelated nodes report
`BadInvalidArgument` and `Mismatch`. Method status and outputs are copied to
`VerificationStatus`, `VerificationResult`, and
`VerificationVariablesErrors`. A missing/invalid FunctionalEntity, method
error, `Mismatch`, or `NotSet` with populated errors aborts later commands
after all endpoint configurations have been processed. Unexecuted reservation
results report `BadNothingToDo`, and the Method returns `Uncertain`.

Reservation input uses a non-empty array of
`PubSubReserveCommunicationIds2DataType` values when the command is set.
Results use `PubSubReserveCommunicationIdsResult2DataType` with the policy's
typed default PublisherId and UInt16 identifier arrays.

When requested, transport-specific information is supported only for
`DataChannelProfiles.PubSubUasc`. The asynchronous listener resolver must attest
that an active local endpoint supports that capability, and return its
`opc.tcp` or `opc.wss` URL as a String, limited to 4,096 UTF-8 bytes. It must
return null when unavailable. A Reverse Connect-only host omits the resolver;
the binding never fabricates a listening address. Other profiles can reserve
identifiers under their configured policy when transport-specific information
is not requested; requesting information for an unsupported profile fails
before allocation.

Requests execute in input order. A request failure aborts further reservations:
the failing item reports its status, later items report `BadNothingToDo`, and
the Method reports `Uncertain`. Earlier successful reservations remain owned
by the Session; the reservation command does not roll them back. Cancellation
before processing returns `BadRequestCancelledByClient`. Cancellation during
processing reports that status on the interrupted item and preserves earlier
results under the same abort rules. Session closure prevents delayed work from
recreating released reservations.

The optional `CloseConnections` handler passes the actual Method context, bound
host AutomationComponent, ordered endpoint NodeIds, Remove flag, and cancellation
token to one `IPubSubFxConnectionLifecycle.CloseConnectionsAsync` call. The host
capability must authorize the actual ConnectionManager and perform the complete
endpoint list as one transaction. It returns exactly one ordered StatusCode per
requested endpoint; a wrong-length result is rejected as `BadUnexpectedError`.
Host `ServiceResultException` and requested cancellation are returned as
explicit Method failures. The binder never searches for, removes, or disables
host nodes or PubSub resources itself. When no lifecycle capability is
registered, `CloseConnections` remains unbound; an existing host handler is
never replaced.

## Project endpoint health

The experimental `PubSubNodeManager.BindFxEndpointStatus` projects real
Part 14 DataSetReader and DataSetWriter health onto an initialized, host-owned
`PubSubConnectionEndpointState.Status`. The existing injectable node manager
provides this binding; no additional service or transport is created.

```csharp
// The host has already resolved and associated these runtime components.
using IDisposable statusBinding = pubSubNodeManager.BindFxEndpointStatus(
    systemContext, connectionEndpoint, reader, writer);
```

For a Publisher endpoint supply only its writer; for a Subscriber supply only
its reader; for PublisherSubscriber supply both. A missing required component
reports `Initial`. Otherwise, an `Error` component takes precedence; a Disabled
or Paused component reports `Ready`, followed by `PreOperational`, then
`Operational` when every required component is Operational. These are the
Part 81 Figures 31–33 matrices, not a transport connection or management-Call
success indicator. Status reads use the current component states, and state
transitions publish the variable's normal change masks. Reading Status does not
refresh the reader's receive timeout, enable communication, or clear a fault.
The Part 14 per-instance Enable Method likewise leaves a newly enabled reader
PreOperational until a valid DataSetMessage arrives; repeating Enable does not
clear a receive-timeout fault. Per-instance Enable and Disable execute
asynchronously, propagate Call cancellation to the runtime, and report the
resulting component state rather than assuming the requested state was reached.

The host owns membership resolution and the standard ToDataSetReader/Writer
references or paths. It must dispose the binding before changing the endpoint
mode, replacing its Status variable, or removing/replacing an associated
runtime component; then bind the new association, or bind without the missing
component to report `Initial`. The binding never searches by ambiguous names,
replaces an existing Status read handler, or disposes the host's nodes or
runtime components. Disposing the PubSub node manager detaches all remaining
bindings. This status projection does not implement endpoint creation,
configuration, enablement, shared ownership, persistence or `CloseConnections`.

## NativeAOT execution

The native test host exercises both server bindings without reflection-based
mocks. `FxReservationHandlerAndDependencyInjectionAreAotSafeAsync` resolves the
reservation binder from DI and invokes both its generated asynchronous handler
and `MethodState.CallAsync` with generated argument metadata. It checks all five
input argument results, all four typed output arrays, derived reservation
results, listening information, Session closure and access denial. This is
in-process Method dispatch, not a network Call-service or authenticated
live-Session test. `FxEndpointStatusReadNotificationsAndDisposalAreAotSafeAsync`
uses a real DataSetReader to exercise generated endpoint Value reads, change
notifications, fault projection and callback disposal. These checks complement
the generated-model encoding tests; they do not establish complete Part 81
lifecycle or Part 84 profile conformance.

## Source provenance

The three unmodified UAFX 1.00.04 NodeSets in `src/Opc.Ua.Fx/Model` come from
[UA-Nodeset at dd178cf655ed3d74f1d085ef04e0e226a1d3ed9f](https://github.com/OPCFoundation/UA-Nodeset/tree/dd178cf655ed3d74f1d085ef04e0e226a1d3ed9f/UAFX).
Their Git blob identifiers are:

| Input | Git blob |
| --- | --- |
| `opc.ua.fx.data.nodeset2.xml` | `12212843b7499c48059ca081963d349cd06d19a4` |
| `opc.ua.fx.ac.nodeset2.xml` | `224276cd06fb200f7421d5370698fd187b199893` |
| `opc.ua.fx.cm.nodeset2.xml` | `674ed1aa7ec9baad7119eba35ed1b87c10d2f0a8` |

Generated C# is produced during compilation and is not checked in.

## Implementation boundary

These are model contracts, not a complete ConnectionManager or
AutomationComponent lifecycle implementation. The binding above provides only
the documented verification and reservation operations; applications remain
responsible for authorization, lifecycle orchestration, endpoint association,
enablement, status binding, persistence and cleanup according to Part 81 and
their selected Part 84 profiles. Loading a model or completing a Method call is
not evidence of valid PubSub DataSet reception or UAFX conformance.

`Opc.Ua.PubSub.Server` can load these declarations into its node manager when
`PubSubServerOptions.ExposeFxModel` is enabled. This is opt-in and registers
the generated encodeable types and the DI, FX Data, AutomationComponent, and
ConnectionManager namespaces. Model exposure alone does not bind
`EstablishConnections` or `CloseConnections`. The binding above implements
asset and functional-entity verification plus communication-ID reservation;
`CloseConnections` is delegated to the optional host transaction. Endpoint
creation, control, configuration, and enablement remain unimplemented.

The experimental [Data Channel binding](DataChannels.md) provides the
`ConfigureUafx` transport-configuration bridge. It does not install Part 81
configuration or grant a ConnectionManager authority over authenticated data
peers. The `EstablishConnections` binding above supports asset verification,
functional-entity verification, and communication-ID reservation only; it does
not implement endpoint creation, control, configuration, or enablement.
