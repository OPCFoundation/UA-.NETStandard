# UaLens guided stack workflows

UaLens is an engineering workspace, not a conformance certificate or a real-time
delivery guarantee. Its tool catalog keeps ordinary browse/read/monitor work close
and puts advanced operations in their document or connection settings.

There are seventeen tool kinds. The original monitoring, event, history, bench,
performance, file, certificate, discovery, GDS and user/role workflows remain.
Alarms, Models, Continuity Lab, PubSub and Companion Tasks add focused experiences
over existing stack modules.

## Choosing a workflow

| Goal | Open or configure | Starts work |
|---|---|---|
| Inspect retained conditions and act on a selected event | Alarms | Explicit observation/refresh and operator commands |
| Inspect or edit a custom value | Models, or the existing Write/Call dialog | Explicit Read, metadata refresh, Write or Call |
| Explain recovery or transfer | Continuity Lab | Start observation, then the selected step |
| Receive a published dataset | PubSub | Explicit Start with interface/broker/security configuration |
| Inspect an industry model | Companion Tasks | Discover, Inspect, then an offered task |
| Use X.509, issued tokens or hardware keys | Connection identity selection | Connect after provider selection |
| Accept a configured reverse connection | Connection setup | Explicit listener/wait and Connect |

Loading a workspace restores intent, not active workloads. It does not acknowledge
conditions, call methods, write values, start a publisher, bind a listener, restart
a server, or arm a sample task.

## Capability evidence

The catalog uses five states: Supported, Unsupported, Unknown, Requires
configuration, and Denied. They describe available evidence, not unconditional
permission or full server support. For example, an EventNotifier attribute can
advertise events without proving that a particular alarm method is authorized.
Write and Call probes inspect permissions; they never perform a trial mutation.

Checks are bounded and tied to the current session and namespace mapping. A
reconnect or explicit refresh invalidates stale evidence. Transport failures remain
retryable, rather than being cached as an absent feature. The UI continues to allow
offline document configuration.

## Alarms

Use an event notifier that exposes real conditions. Alarms keeps the condition
identity separate from the source node, distinguishes branches, and uses the
selected event's current EventId for acknowledgement and confirmation.

Refresh reconciles retained state using the server's refresh markers. A completed
service request without the expected event sequence is not presented as a complete
refresh. Recreating a subscription and refreshing is distinct from transferring one.
Missing events and bounded retention are visible.

Acknowledgement, confirmation, comments and applicable advanced condition
operations are explicit. A disconnected or restored document never repeats them.
Authorization errors and unsupported operations remain failures; the UI does not
optimistically mark a rejected acknowledgement successful.

The implementation reuses the stack's typed alarm operations, generated event
records and V2 subscription facilities. See [Alarms and Conditions](AlarmsAndConditions.md).

## Models and structured values

Select a variable, DataType or method and open Models. Read the value or definition
explicitly. The same structured editing module serves Models and the existing
Write/Call dialogs; it supports generated encodeables and the stack's default
NativeAOT-friendly runtime type adapters without Reflection.Emit.

Edits use a separate draft. A rejected or canceled draft cannot modify the original
value or write to the server. Nested fields, optional presence, unions and supported
arrays retain their type semantics. Existing matrix dimensions are preserved;
creating new matrix shapes requires suitable typed input. OptionSets are not
presented as a complete bit-field editor.

Schema preview/export uses the existing schema provider. Missing definitions are
reported explicitly; denied reads, canceled resolution and connection failures
are not treated as authoritative absence. Metadata refresh and session-generation
changes discard stale definitions. Unknown opaque values remain read-only.

See [Complex Types](ComplexTypes.md) and [Schema Generation](SchemaGeneration.md).

## Continuity Lab and diagnostics

Continuity Lab owns its observation subscription and any auxiliary sessions used
by its selected scenario. It does not replace the primary connection owner or
implement another reconnect engine. Other documents retain their configuration.

The timeline distinguishes reconnect/recovery, transfer, recreation, missing
messages and republish activity. It records actual server subscription/partition
IDs where available; client correlation IDs are not relabeled as server IDs.
Requested and revised settings and server operation limits provide context for
throughput and failures.

The available scenarios include observation of a user-managed outage, recreation
of the lab's own subscription, transfer or recreation from its own snapshot, and
configured durable restore or redundant takeover. A snapshot used by a running
lab is separate from ordinary workspace JSON.

Durability is configured before monitored items are added, and the server may
revise its requested lifetime. Same-user transfer rules, server queues, partitioning
and triggering relationships matter. The repository's Quickstarts durable store
persists subscriptions on graceful shutdown and does not persist issued-token
subscriptions. It does not establish arbitrary crash recovery.

UaLens does not terminate arbitrary server processes. For a graceful-restart
scenario, save/close the lab-owned session, restart the explicitly selected sample
yourself, and then request restore. Redundancy requires an already configured
set/provider. Missing prerequisites are shown as setup requirements.

Evidence export is bounded and omits credentials. Sampling time, server publishing,
network receipt and UI rendering are different measurements. Unsynchronized
source/client clocks cannot establish one-way latency. A missing diagnostics
permission is not a zero counter, and republish attempts do not prove every missing
sample was recovered. Privileged packet capture is not required for the ordinary
counter view and is not automatically provisioned.

See [Subscriptions](Subscriptions.md), [Transfer](TransferSubscription.md),
[Durable Subscriptions](DurableSubscription.md), [High Availability](HighAvailability.md)
and [Diagnostics](Diagnostics.md).

## PubSub

A PubSub document owns its runtime independently of the primary UA session. Configure
the transport, address/interface or broker, publisher/writer/reader identifiers,
encoding and security requirements, then start it explicitly. Metadata, decoded
fields, state and bounded diagnostic evidence are shown together.

The default subscriber sink observes locally. Receiving a dataset does not write
to a UA server. Publication, Actions, external-server adapters and write-back need
explicit configuration and authorization. Stop/close releases the owned runtime;
primary-session loss affects only configured adapters that depend on that session.

The module reuses `PubSubApplicationBuilder`, `IPubSubApplication`, transport,
metadata, source/sink, security and Action facilities. Configurations save safe
references, not raw broker credentials or SKS key material. No runtime or publisher
is started by application DI registration or workspace restore.

Reaching a sample cap is not the same as completing its final send. The runtime
finishes the final publication before stopping its writer. A synthetic UDP loopback
also gets a bounded two-second local receive drain; expiry is recorded explicitly
and does not imply remote acknowledgement or a lossless network.

UDP/UADP and configured MQTT workflows use the existing stack transports. Other
profiles require their registered transport/provider and platform prerequisites.
Kafka uses the managed backend in the native app; the optional Confluent backend
is not a NativeAOT substitute. DTLS, Ethernet, SKS, brokers and advanced adapter
scenarios do not install their own external infrastructure.

For a self-contained source use the repository's
[Console Reference PubSub Client](../samples/PubSub/ConsoleReferencePubSubClient/README.md)
in publisher mode with matching explicit settings. The full stack guide is
[PubSub](PubSub.md).

## Identities, hardware keys and reverse connect

Anonymous and username selection remain available. X.509 user identity resolves an
existing certificate through a configured source and password/PIN provider; it is
not the same certificate role as the application's secure channel. The selected
token policy must match the key and provider capability.

Issued-token selection resolves a named access-token provider and pinned authority/
profile metadata. The application does not include a general OAuth enrollment UI
or accept a workspace as authority to create a token broker. Reconnect and user
changes reacquire identity material through its owner, never reuse a disposed
session identity or downgrade to Anonymous.

Hardware-backed selection references host-registered certificate/crypto providers.
Keys remain inside their provider; PINs and bearer tokens never go into workspace
JSON. Provider modules, device enrollment, token authorities, and HSM provisioning
are external setup. No hardware or FIPS claim follows merely from selecting a name.

Reverse connect requires an explicit listener, expected ServerUri/endpoint, registered
binding and secure identity/trust configuration. Restoring a profile does not bind
its socket. A received reverse handshake is not sufficient authorization to trust
an arbitrary peer. WSS also requires listener TLS configuration. Forward transport
selection is constrained to the registered bindings and actual platform support.
Ordinary Connect goes directly to endpoint/security selection. Listener and application
identity setup is available from connection settings instead of adding an advanced
setup dialog to every connection.

See [Identity Providers](IdentityProviders.md), [Crypto Provider](CryptoProvider.md),
[Reverse Connect](ReverseConnect.md) and [Transports](Transports.md).

## Companion Tasks

Choose a model, select **Discover**, then **Inspect** a returned instance. Discovery
uses actual typed instances, not just namespace presence. Inspection is read-only.
The task list is populated by the selected instance; an arbitrary operation name
cannot be executed without a current inspection.

Sample mutations additionally require a loopback endpoint and explicit confirmation
that it is a repository sample. Loopback alone does not prove a server is safe to
mutate. Confirmation and operation input are not saved and are cleared when the
connection changes. File exports require an explicit destination.

| Family | Guided scope | Intentional limit |
|---|---|---|
| DI | Device identity/topology, parameters and software-update state; explicit sample preparation | No firmware installation or complete update-state orchestration |
| ISA-95 | Typed V1/V2 resources and job inspection; explicit sample job storage | Jobs are not automatically started |
| WoT / xRegistry | Asset/document/version/model inspection; explicit compatible sample registration/refresh | No overwrite of existing versions; server AutoRefresh policy still applies |
| Robotics | Published device-system/controller/axis topology and telemetry | No physical actuation or Robot Intent commanding |
| Vision | Sensor/frame/pipeline/result inspection through typed clients | No camera provisioning, rendering or automatic inference |
| AI | Model, dataset, deployment and learning-job inspection; fixed synthetic local request where eligible | No backend/vendor SDK hosting or arbitrary prompt egress |
| OpenUSD | Representation/binding inspection, bound-value snapshot, advertised asset verification/export | No renderer, remote federation or arbitrary USD dependency fetching |

The AI sample request is offered only for a loopback backend with egress disabled;
both the UA server and backend restrictions are rechecked before invocation.
Responses and displays are bounded. A response that requires a separate transfer
workflow is not silently downloaded.

OpenUSD export uses a new destination, advertised assets and digest checks with
bounded asset counts and sizes. Metadata inspection alone is not proof that an
asset has been verified. Read-only snapshots do not enable command bindings.

The catalog labels model maturity independently. Published OPC 40010 Robotics is
not the draft Robot Intent extension. Vision, AI, OpenUSD, xRegistry and relevant
WoT extensions retain their own draft/version labels. Generic browsing of an
unrecognized model remains available in the address-space explorer.

Matching servers are listed in [Sample Applications](samples.md). Brokers, token
authorities, redundant sets, hardware and privileged network facilities must be
supplied explicitly; a gated scenario is not reported as demonstrated.
