# Base Network Model

The Part 22 runtime publishes provider-supplied network state through the
standard `Server/Resources/Communication` entry points. Applications can expose
physical and layered interfaces, priority mappings, Time Sensitive Networking
(TSN) streams and Link Layer Discovery Protocol (LLDP) observations without
coupling the SDK to an operating-system driver or switch.

`ReferenceNetworkModelProvider` is an in-memory simulator. Its mapping methods
change its own state only; an accepted method call does not configure the host,
reserve bandwidth or establish a real TSN stream. Production adapters implement
`INetworkModelProvider` and supply the deployment's acquisition, persistence and
network-control behavior.

## Host the model

Use the existing `Opc.Ua.Server` and `Opc.Ua.Client` packages; no additional
network-model package or packet-capture library is required.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Server.Network;

var provider = new NetworkModelBuilder()
    .AddMappingTable(new NetworkMappingTableSnapshot("default")
    {
        CanEdit = true,
        Entries = []
    })
    .AddInterface(new NetworkInterfaceSnapshot("physical", "eth0")
    {
        AdminStatus = InterfaceAdminStatus.Up,
        OperStatus = InterfaceOperStatus.Up,
        Speed = 1_000_000_000,
        Ethernet = new NetworkEthernetSnapshot(1000, Duplex.Full, 1522),
        MappingTableId = "default"
    })
    .AddInterface(new NetworkInterfaceSnapshot("vlan", "eth0.2")
    {
        AdminStatus = InterfaceAdminStatus.Up,
        OperStatus = InterfaceOperStatus.Up,
        Speed = 1_000_000_000,
        VlanId = 2,
        LowerLayerIds = ["physical"]
    })
    .Build(DateTime.UtcNow, StatusCodes.Good);

services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "NetworkModelServer";
        options.EndpointUrls.Add("opc.tcp://localhost:62588/NetworkModelServer");
    })
    .AddNetworkModel(provider);
```

Mapping changes are denied by default. Pass an explicit
`authorizeMappingEdits` policy to `AddNetworkModel` to permit them for the
appropriate caller. The server evaluates that policy for each invocation,
independently of the table's `CanEdit` capability. A production policy must
check the session's authenticated identity and the application's authorization
rules; configuring an editable table does not grant permission.

For direct server construction, register a
`NetworkModelNodeManagerFactory(provider, authorizeMappingEdits)` with
`StandardServer.AddNodeManager`. Both registration paths use the same manager.
The manager references the namespace-zero Resources tree rather than creating
an application-specific replacement. DiagnosticsNodeManager retains ownership of
these entry points, including LLDP's existing local-system and port container
nodes; the network manager updates local-system values and attaches provider
instances beneath them.

To resolve a deployment provider through dependency injection, register
`INetworkModelProvider` with the application's lifetime and pass
`services => services.GetRequiredService<INetworkModelProvider>()` to
`AddNetworkModel`. The factory runs when the server starts, after its service
container is available.

## Supply acquisitions and own their lifetime

`INetworkModelProvider.ReadAsync` returns a coherent acquisition.
`WatchAsync` yields an initial acquisition and subsequent complete revisions;
it may coalesce intermediate revisions. `EditPriorityMappingAsync` reports
success only after the provider has accepted the mutation. Providers must
serialize acquisition and edits so that an older read cannot overwrite an
accepted edit.

The application owns the provider and its persistence. Stop the server before
disposing provider-owned drivers or connections. The server cancels its
observation on shutdown. The reference provider retains no state across process
restart unless the application explicitly saves a snapshot and supplies it to
the next instance.

Snapshots use stable application-owned IDs for interfaces, tables, streams,
ports and neighbors. `IfName` becomes the interface BrowseName. Removing an
instance from a subsequent acquisition removes its published nodes; clients
must rediscover removed instances rather than keeping their old NodeIds.
Adding or removing an optional capability preserves retained nodes and their
monitored items. Removing a capability deletes only its nodes and references;
restoring it does not require clients to recreate monitored items on the
interface's unchanged mandatory parameters.
Mapping and lower-layer relationships remain browsable in both directions as
acquisitions change.

The reference provider copies mutable generated structures and backing arrays
on input and output. It assigns increasing revisions during its own lifetime,
resets the revision on construction, and rejects an acquisition timestamp
earlier than its current timestamp. Mapping edits retain the latest acquisition
timestamp because they do not represent a new hardware acquisition.

Supply UTC acquisition timestamps and meaningful quality. A failed acquisition
must carry a bad status, rather than `Good` values copied from a healthy default.
Snapshot construction requires explicit quality, acquisition time, interface
states and speed, and stream state and identity; omitted fields cannot silently
become an operational interface or stream.
Omit unsupported optional capabilities. The model distinguishes IETF interface
speed in bit/s from IEEE Ethernet-port speed in Mbit/s. Stream identifiers have
eight bytes, MAC-address arrays have six bytes, and traffic intervals have a
nonzero denominator.

If provider observation throws, the server retains the last values and their
acquisition timestamps but publishes bad quality and retries observation. It
retains those values internally; a read with Bad quality returns a null payload
under the stack's standard DataValue read behavior. Clients must use the status
and timestamp to identify unavailable acquisitions rather than expecting the
last payload on the wire. The server
uses the provider's bad `ServiceResultException` status, or
`BadCommunicationError` for other exceptions. A newer valid revision restores
the acquisition's supplied quality; reconnecting alone does not turn retained
data into a new acquisition. The server rejects a higher revision with an older
timestamp before changing the address space.
If observation ends before cancellation, the server marks retained values
`BadCommunicationError` and starts observation again. An initial read failure
also marks the standard LLDP local-system values bad rather than leaving their
startup quality unchanged.

## Discover and use the typed client

The client borrows its session and subscription. It does not dispose them or
replay mapping methods after reconnecting.

```csharp
using Opc.Ua.Client.Network;

services.AddOpcUa()
    .AddClient(options => options.ApplicationName = "NetworkModelClient")
    .AddNetworkModelClient();

// session is an already-connected, caller-owned ISessionClient.
NetworkModelClient client =
    serviceProvider.GetRequiredService<NetworkModelClientFactory>().Create(session);

ArrayOf<NodeId> interfaces = await client.DiscoverInterfacesAsync(ct);
for (int index = 0; index < interfaces.Count; index++)
{
    NetworkInterfaceValues values = await client.ReadInterfaceAsync(interfaces[index], ct);
    if (StatusCode.IsGood(values.Speed.DataValue.StatusCode) &&
        values.Speed.DataValue.WrappedValue.TryGetValue(out ulong bitsPerSecond))
    {
        Console.WriteLine(bitsPerSecond);
    }

    ArrayOf<NodeId> tables =
        await client.GetEffectiveMappingTablesAsync(interfaces[index], ct);
}
```

Direct construction uses `new NetworkModelClient(session, telemetry)` or
`session.GetNetworkModelClient(telemetry)`. `GetInterface`, `GetMappingTable`
and `GetLldp` return the source-generated standard model proxies.

Discovery matches standard types, vendor subtypes and `HasInterface`
references. It resolves namespace URIs through the connected session, rather
than assuming an instance namespace index. Mapping lookup follows
`HasLowerLayerInterface` only when the current interface has no direct
`UsesPriorityMappingTable` reference. Aggregated lower layers can yield several
tables; cycles, remote targets and ambiguous direct mappings produce errors.
Traversal and browse-result limits bound malformed graphs.

`ReadInterfaceAsync`, `ReadStreamAsync`, `ReadMappingTableAsync` and
`ReadParametersAsync` retain value quality and acquisition timestamps. Check
each status before interpreting its payload. Missing mandatory parameters
produce `BadNotFound`; omitted optional parameters produce `BadNotSupported`.
`ObserveAsync(variableId, streamingSubscription, ...)` forwards data changes
through an existing streaming subscription without taking ownership of it.

### Mapping results

The table key is the combination of `MappingUri` and `PriorityLabel`.
`AddPriorityMappingEntryAsync` and `DeletePriorityMappingEntryAsync` return
the complete `CallMethodResult`, including input-argument diagnostics.
Service-level failures throw `ServiceResultException`.

| Condition | Result |
|---|---|
| Caller rejected by the server policy | `BadUserAccessDenied` |
| Reference provider rejects an edit to a read-only table | `BadNotSupported` |
| Duplicate mapping key on add | `BadIndexRangeInvalid` |
| Missing mapping key on delete | `BadBrowseNameInvalid` |
| PCP outside 0-7, except sentinel 255 | `BadInvalidArgument` |
| DSCP outside 0-63, except sentinel `UInt32.MaxValue` | `BadInvalidArgument` |
| Unavailable reference-provider acquisition | Acquisition's bad status |

PCP 255 and DSCP `UInt32.MaxValue` mean that the field is unused. The standard
variable BrowseName is spelled `PriorityMapppingEntries`; clients and servers
retain that spelling for interoperability.
The server publishes the optional add/delete methods only for editable tables.
An omitted method is not an available configuration operation.

`FailureSystemIdentifier` uses a Byte matrix with eight columns. When no
identifiers exist, the server retains the `[0,8]` shape in memory and declares
the variable's dimensions, but the binary Variant carries an empty Byte array:
OPC 10000-6 omits matrix dimensions when any dimension is zero. Nonempty
acquisitions carry the matrix dimensions and identifier bytes again.

## Run the simulated server/client scenario

```powershell
dotnet run --project samples\Network\NetworkModelSample -- 62588
```

The sample starts a local server, connects a separate client over a secured
OPC TCP endpoint, and discovers a physical interface and a layered VLAN
interface. Both resolve to the physical interface's mapping table. It exercises
an unauthorized edit, a permitted edit and an invalid edit, then observes
stream configuration, operation, bandwidth failure and recovery through a
subscription while an LLDP neighbor appears and disappears.

Its authorization switch is controlled by the local scenario, and its
certificate configuration automatically accepts certificates for the local
demonstration. Neither is a production authorization or trust policy. The sample
stores generated certificates under the process temporary directory and never
changes host networking.

Replace `ReferenceNetworkModelProvider` with an injected
`INetworkModelProvider` implementation to connect an application-owned adapter.
The snapshot records and standard wire model do not require a particular driver
or switch API.

## Specification and implementation coverage

The contract follows [OPC 10000-22 version 1.05.04](https://reference.opcfoundation.org/specs/OPC-10000-22/v1.05.04)
and the namespace-zero declarations generated from
`tools/Opc.Ua.SourceGeneration.Core/Design/StandardTypes.xml`.
The published document names the following conformance units; this list does
not invent corresponding profile URIs or establish product certification.

| Published conformance units | Runtime surface |
|---|---|
| BNM Entry Points; BNM Mapping Entry Points; BNM TSN Entry Points | Standard Resources, Communication, interfaces, tables and stream entry points |
| BNM IETF Interface Base Info; BNM Ethernet Base Info | IETF parameters and distinct IEEE Ethernet-port parameters with engineering units |
| BNM AutoNeg; BNM VLAN Capabilities; BNM IETF Interface Vlan Info | Optional negotiation, VLAN capability and VLAN identity interfaces |
| BNM TSN Base Info; BNM TSN Config | Talker/listener identity, state, traffic, status, MAC/VLAN and interface configuration |
| BNM Priority Mapping; BNM Priority Mapping2 | Mapping references, lower-layer inheritance, entries and authorized table methods |
| BNM IEEE LLDP Info | Local system, ports and dynamically acquired remote systems |

Provider invariants are exercised in
`ReferenceNetworkModelProviderTests`, client service behavior in
`NetworkModelClientServiceTests`, and real connections in
`NetworkModelIntegrationTests`. The sample's deterministic acquisition source
is compiled into the integration tests so its topology and transitions are
exercised without a physical network dependency.

Production operating-system and switch adapters, physical TSN reservation and
packet capture are outside this runtime. Optional MCP and UA Lens integrations
are not included.
