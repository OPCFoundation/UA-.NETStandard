# PubSub communication-ID reservations

`IPubSubCommunicationIdReservations` implements the communication-ID reservation
boundary used by the Part 14 `PubSubConfiguration.ReserveIds` Method. It is separate
from `IPubSubIdAllocator`, which allocates configuration identifiers and file handles.
Neither service allocates SecureChannel identifiers, connection ContextIds, or ports.

## Configure the default PublisherId

The host must explicitly configure each supported transport profile and its default
PublisherId. A default is not inferred from the first configured connection. The
PublisherId must be a nonzero Byte, UInt16, UInt32, or UInt64, or a nonempty String.
Types remain distinct: UInt32 `7`, UInt64 `7`, and String `"7"` are different publishers.

Register the policy before resolving the PubSub application:

```csharp
services.AddSingleton<IPubSubDefaultPublisherIdPolicy>(
    new PubSubDefaultPublisherIdPolicy(
    [
        new PubSubDefaultPublisherId(
            Profiles.PubSubUdpUadpTransport,
            Variant.From(42UL))
    ]));
services.AddOpcUa().AddPubSub();
```

The example value is an explicit host assignment, not an allocation algorithm.
Choose the default according to the deployment's PublisherId assignment policy.
Datagram/UADP deployments normally use UInt64; broker/JSON deployments normally
use String. An unsupported or unconfigured transport scope returns `BadNotSupported`;
an invalid value returned by a custom policy returns `BadConfigurationError`.

The DI registration injects the same reservation provider into the application
transaction. A custom provider can replace the default with an
`IPubSubCommunicationIdReservations` registration. Standalone applications can use
the same boundary without DI:

```csharp
using var reservations = new InMemoryPubSubCommunicationIdReservations(policy);
await using IPubSubApplication application = new PubSubApplicationBuilder(telemetry)
    .UseConfiguration(configuration)
    .UseAllStandardEncoders()
    .AddTransportFactory(transportFactory)
    .WithCommunicationIdReservations(reservations)
    .Build();

var reservingApplication = (IPubSubCommunicationIdApplication)application;
PubSubCommunicationIdReservation ids =
    await reservingApplication.ReserveCommunicationIdsAsync(
        actualSessionId, transportProfileUri, 1, 1, cancellationToken);

// Populate connection.WriterGroups using ids and ids.DefaultPublisherId.
NodeId connectionNodeId = await PubSubConfigurationSession.ExecuteAsync(
    actualSessionId,
    ct => application.AddConnectionAsync(connection, ct),
    cancellationToken);
```

The standalone host must authorize the caller, supply the actual Session identifier,
and forward Session closure to `reservations.CloseSession(actualSessionId)`.
The server node manager performs these steps for its bindings. Its reservation and
asynchronous consuming methods require a Session with the `ConfigureAdmin` role and
`ExposeConfigurationMethods = true`.

## Reservation and mutation semantics

- WriterGroupIds and DataSetWriterIds have independent domains, scoped by the exact
  transport-profile URI and typed default PublisherId.
- Each domain has exactly 32,768 reservable values, `0x8000` through `0xFFFF`.
  Configured identifiers, including disabled components, and outstanding reservations
  are excluded. A request either reserves both complete arrays or reserves nothing.
  Exhaustion returns `BadResourceUnavailable`.
- A reservation belongs to its actual Session. Foreign and sessionless mutations
  cannot consume it and return `BadUserAccessDenied`. Direct sessionless mutations
  that do not consume a reservation remain supported.
- `PubSubApplication` serializes reservations with its central configuration mutation
  transaction. Application-level replacement, adding a connection with nested groups/writers,
  adding a WriterGroup, and adding a DataSetWriter all pass through the same
  ownership check and configuration persistence operation. This is not a separate
  address-space preflight.
- A successful configuration save consumes the identifiers it uses. Successful
  deletion makes identifiers available again. Session closure releases unused
  reservations, but never releases identifiers that an in-flight save could still
  commit to a competing Session.
- Closure is recorded before waiting for an asynchronous policy or save. An
  in-flight reservation cannot recreate reservations after closure. Closed Session
  identifiers are not reusable for the provider's lifetime.
- Cancellation or a failed configuration save does not consume a reservation.
  Providers must not report a failed or cancelled save after silently committing it
  without a reconciliation contract.

Configuration saving is the reservation commit point. Version persistence, runtime
replacement, restart, or notification can still fail **after** configuration saving.
Such failures are not a promise of complete rollback. The process-local provider
retains the saved identifier set even before the runtime snapshot is replaced, so
those identifiers are not made available accidentally.

## Wire contracts and configuration files

The standard `ReserveIds` binding and the consuming `AddConnection`,
`AddWriterGroup`, `AddDataSetWriter`, `AddReaderGroup`, and `AddDataSetReader`
bindings use true asynchronous
`MethodState.OnCallMethod2Async` dispatch. Their standard argument metadata is reused
from the generated model. Dynamic standard methods have namespace-zero BrowseNames
and unique method/argument instance NodeIds. `ReserveIds` returns exactly three
outputs: the typed default PublisherId, UInt16 WriterGroupIds, and UInt16
DataSetWriterIds. Delayed providers are awaited, not polled for synchronous completion.

Both the standard and materialized `PubSubConfiguration` objects use generated
FileType properties and method declarations. `Open` accepts only Read (`0x01`),
Write + EraseExisting (`0x06`), and Read + Write (`0x03`). Reading and Read + Write
start with a binary `UABinaryFileDataType` ExtensionObject whose Body contains a
`PubSubConfiguration2DataType` ExtensionObject. Write + EraseExisting starts empty.
Read handles can coexist; a write handle requires exclusive access. `Close`
discards uncommitted changes.

Handles belong to the actual calling Session, not its user name. Another Session
cannot read, write, seek, or close them. Session closure releases its handles and
prevents a delayed allocation from reopening them. `GetPosition` and `SetPosition`
use UInt64 positions; seeking beyond the end clamps to the end. `OpenCount`, `Size`,
and `UserWritable` expose current values, with `UserWritable` evaluated for the
calling Session.

`PubSubServerOptions.MaxConfigurationFileSize` limits each file to 16 MiB by
default. `MaxConfigurationFileHandles` limits concurrent handles to 64 by default
and must be between 1 and 65,535. Exceeding a byte limit returns
`BadEncodingLimitsExceeded` without modifying retained bytes; exhausting the
handle budget returns `BadResourceUnavailable`. The same byte limit bounds binary
decoding and encoding.

The vendor `SetConfiguration` alias has an explicit argument declaration: one
`PubSubConfigurationDataType` input and one StatusCode-array output. It awaits
full replacement through the actual Session's reservation transaction. It is not
the standard reference-level file import protocol.

Standard `CloseAndUpdate` takes **three** inputs: FileHandle,
RequireCompleteUpdate, and ConfigurationReferences. The import path decodes the
binary file, validates its envelope and Body, remaps the file namespace indices to
the Server's registered namespace table, and applies only referenced elements
inside the application's serialized persistence/runtime transaction. It handles
all nine reference categories, parent matching/addition, removal-before-edit,
configuration-properties merge/delete, optional security authorization,
same-Session reservation consumption, and complete versus partial updates. The
four standard outputs are returned in their declared types. Empty references return
`BadNothingToDo`; malformed file bodies and unknown/duplicate namespaces are
rejected without applying configuration changes.

Application-level `ReplaceConfigurationAsync` is functional and protected by the
reservation transaction. It does not itself claim to implement the standard
`CloseAndUpdate` wire protocol. The import processor is transactionally integrated,
but is not a claim of Part 81 lifecycle or full Parts 30–32 conformance.

## Deployment and conformance limits

The default provider is **process-local**, intended for one PubSub application's
configuration domain. It retains closed Session tombstones until disposal.
It is not a distributed reservation service or a crash-recovery journal. HA hosts
must inject a shared provider that coordinates reservations, authoritative
configuration saves, Session closure, and recovery across instances. Merely sharing
the transient `IPubSubIdAllocator` does not provide that behavior.

The opt-in [UAFX binding](UAFX.md#bind-verification-and-reservation) uses this
transaction for the `ReserveCommunicationIdsCmd` of the Part 81
`EstablishConnections` command. It also delegates `VerifyAssetCmd` and
`VerifyFunctionalEntityCmd` to the addressed asset and related functional
entity Methods. It does not implement the remaining connection lifecycle,
transport endpoint negotiation, or complete OPC 10000-30/-31/-32 conformance.

References:

- [Part 14 ReserveIds](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.3.7.5)
- [Part 14 PublisherId](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7.1)
- [Part 14 CloseAndUpdate](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.3.7.6)
