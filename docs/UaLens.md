# UaLens

UaLens is the stack's Avalonia desktop engineering tool. It combines address-space
exploration, monitoring, history, events, diagnostics, and administration in one
workspace. Its source is in [`tools/Opc.Ua.Lens`](../tools/Opc.Ua.Lens).

The tool catalog provides browse/read/monitor workflows and allows you to perform
advanced operations or control advanced settings, such as connection settings.
UaLens is an engineering workspace, not a conformance certificate or a real-time
delivery guarantee.

There is one primary server connection. GDS tools can use a suitable primary
session or maintain their own secondary connection. Document tabs are working
contexts, not separate primary server sessions.

The screenshots show local examples using the repository's ConsoleReferenceServer.
The live examples use Anonymous/None on a loopback endpoint; this is not a
deployment security recommendation. Select the appropriate security policy,
identity and certificate trust for a deployed server. Sample values and counters
illustrate the interface, not timing or delivery guarantees.

## Getting started

From the repository root:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet build tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0
dotnet run --project tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0 --no-build
```

The application also targets .NET 8 and .NET 9. Its assembly is `UaLens`, its
package is `OPCFoundation.NetStandard.Opc.Ua.Lens`, and its tool command is `ualens`.

Use the connection bar to choose an endpoint and connect. The endpoint picker
shows the server's advertised security and identity policies. The primary state,
security policy, and identity are visible while working.

The standard headless `--smoke` and protocol probes are development diagnostics
for a reference server's explicitly selected Anonymous/None endpoint. They do
not prove secure desktop behavior or authorize accepting an untrusted certificate.

## Workspace

The menu contains **File**, **View**, **Tools**, and **Help**. Add a document
through **Add Tool**, the Tools menu, or an applicable address-space action.
The searchable catalog groups tools into Explore / Connect, Observe, Administer,
and Diagnose. Use document actions and document or connection settings to perform
advanced operations and control their configuration.

The empty workspace offers connection, workspace, discovery, and local-certificate
entry points; it is not a permanent Home or General tab. The address-space explorer
and contextual attributes/references inspector are resizable. Monitor, Write, Call,
Events, History, and Inspect are available according to the selected node.

An unavailable operation does not hide an entire document. Local certificate
management and discovery are available without a primary connection. Session-bound
documents retain their configuration while disconnected.

![UaLens workspace with an expanded address space and live values, quality and source timestamps](Images/UaLens/workspace.png)

*Browse the address space beside a monitor document. The connection bar applies
to the primary session shared by the documents.*

The default appearance follows the operating system. Light and Dark can be
selected explicitly. Saved Light, DarkStandard, and DarkNavy preferences select
an explicit appearance; System follows the operating system. Charts use the
same semantic colors as dialogs and document surfaces.

Standard text-editing shortcuts retain their usual meaning. F2 renames a document;
Ctrl+Tab and Ctrl+Shift+Tab cycle documents. The command registry is the source of
truth for displayed shortcuts and their availability.

Close and Quit await local cleanup, with bounded V2 server-side subscription
deletion even when the server is unavailable. The status bar shows shutdown
progress. A remote cleanup failure is logged rather than presented as confirmed
deletion; the server may retain resources until their lifetime expires.

## Connection and trust

An untrusted server certificate is rejected unless the user makes an explicit
decision. The choices are Reject, Accept Once, and Trust Permanently.

Accept Once applies to the selected endpoint, certificate, and overridable
validation error. It is not global trust and does not authorize a GDS secondary
connection. Disconnecting or selecting a different target clears it. Permanent
trust must be written successfully to the certificate store before retrying.
Invalid or revoked certificates are not made acceptable by the untrusted-certificate
choice.

![Endpoint picker showing security policies, message security modes and user-token choices](Images/UaLens/connection-policies.png)

*Choose an advertised endpoint and its user-token policy. Selecting SignAndEncrypt
does not automatically grant certificate trust.*

The validation callback never blocks on a window. Connection coordination rejects
and captures the validation failure, prompts asynchronously, and makes a bounded
retry using the decision. Cancellation does not apply a late dialog answer.

Changing engines or reconnecting preserves the selected security and identity
profile. Credentials are reacquired when necessary; a disposed session identity
is not reused. Changing the user replaces the session through the connection
owner, updating its profile and retained credentials together; document
configurations are rebound to the new session. Workspaces contain profile intent,
not passwords, private keys, or bearer tokens. The connection flow supports
Anonymous, UserName, X.509 user certificates and issued-token providers registered
by the host. Certificate stores, password/PIN providers, token
authorities, application-key providers, and reverse-connect listeners have explicit
configuration requirements; a saved provider name cannot install a provider or load
an arbitrary native module.

### Identities, hardware keys and reverse connect

X.509 user identity resolves an existing certificate through a configured source
and password/PIN provider; it is not the same certificate role as the application's
secure channel. The selected token policy must match the key and provider capability.

Issued-token selection resolves a named access-token provider and pinned authority/
profile metadata. The application does not include a general OAuth enrollment UI
or accept a workspace as authority to create a token broker. Reconnect and user
changes reacquire identity material through its owner, never reuse a disposed
session identity or downgrade to Anonymous.

For a fresh connection, **Authorize configured token provider...** is available
only when that named provider registers `IIdentityTokenInteraction`. Interaction
and progress belong to the provider; UaLens neither discovers another authority
nor implements its own OAuth exchange. Server-advertised token, authorization and
JWKS endpoint overrides are removed before interaction and normal acquisition.
Authorization returns no token to the dialog. **Use identity** still checks the
exact policy, and Connect acquires fresh credentials. A restored exact reference
cannot launch setup or silently select a different source.
The wait is limited to five minutes and cancels with the dialog even if the
provider does not finish promptly. The registered provider remains responsible
for stopping its own interaction; cancel does not roll back authority-side effects.

A configured `IIdentityEnrollment` factory enables **Enroll / renew configured
user certificate...**. Each dialog owns a fresh adapter and separates local
**Prepare signing request**, remote **Request certificate**, and local
**Add reviewed certificate**. Request and addition each consume a separate
confirmation. Closing cancels and drains the adapter without returning a late
identity selection; an already submitted request or completed store addition is
not rolled back by cancel.

`GdsIdentityEnrollment` supports software-backed user keys through an already
authorized GDS and an explicitly configured application, certificate group and
certificate type. The review shows those identifiers and the GDS endpoint.
The existing certificate must contain the ApplicationUri of that registered
application, as required by
[StartSigningRequest](https://reference.opcfoundation.org/specs/OPC-10000-12/v1.05.07/7.9.3).
Ordinary subject-only user certificates need their identity authority's adapter;
they are not sent to a GDS as if they satisfied that contract. GDS policy may also
assign the application to the requested group.

Preparation creates a CSR for the selected key without changing the store or
submitting a request. The adapter pins the GDS session/user/endpoint/security,
registered application, source store and password provider. Approval polls the
same request on
[BadNothingToDo](https://reference.opcfoundation.org/specs/OPC-10000-12/v1.05.07/7.9.5);
it does not resubmit a lost or rejected request. Each operation has a 30-second
deadline and the review expires after five minutes.

Issued certificates must match the requested key, distinguished name, application
binding, current validity, signature usage and selected token algorithm. Validation
uses the Users trust list with no SHA-1, automatic trust, certificate downloads or
unknown-revocation fallback. Adoption revalidates and adds only to the exact
configured store opened for private keys, clears temporary password characters,
and retains the old certificate. Hardware and application-instance key enrollment
are not performed by this user-key adapter.

For example, trusted host configuration can register the adapter without opening
a store, sending a CSR or connecting during registration:

```csharp
var userSource = new ConfiguredCertificateSource(
    "approved-user", "Approved user identity", approvedStore,
    certificateProvider, approvedPasswordSources,
    createEnrollment: source => new GdsIdentityEnrollment(
        source, configuredGds, approvedApplicationId, approvedGroupId, approvedTypeId,
        userCertificateValidator,
        () =>
        {
            var store = new DirectoryCertificateStore(telemetry);
            store.Open(approvedStore.StorePath, noPrivateKeys: false);
            return store;
        }));
services.AddSingleton(new ConnectionIdentityConfiguration([userSource]));
services.AddUaLens();
```

The example assumes a Directory store and an eligible existing user certificate.
The GDS, validator and certificate/password providers remain owned by the host;
each enrollment owns only its transient certificate handles and returned store.

Hardware-backed selection references host-registered certificate/crypto providers.
Keys stay inside their provider; PINs and bearer tokens never go into workspace
JSON. Provider modules, device enrollment, token authorities, and HSM provisioning
are external setup. No hardware or FIPS claim follows merely from selecting a name.

Reverse connect requires an explicit listener, expected ServerUri/endpoint, registered
binding and secure identity/trust configuration. Restoring a profile does not bind
its socket. A received reverse handshake is not sufficient authorization to trust
an arbitrary peer. WSS also requires listener TLS configuration. Forward transport
selection is constrained to the registered bindings and actual platform support.
Ordinary Connect goes directly to endpoint/security selection. Configure listeners
and application identity through connection settings.

Connection setup shows the selected binding's forward/reverse support, named
application-key and listener-TLS registrations, expected peer and outstanding
network/trust prerequisites. **Discover endpoints** is an explicit forward
operation; reverse mode uses **Start listener**, then **Wait / discover**.
**Use setup** saves the selected intent without creating a channel or listener.
Saved profiles stay pinned to their exact endpoint, application URI, security and
user-token policy; unavailable registrations remain visible instead of selecting
an unrelated default.

Reverse waits and startup can be canceled independently. Stop prevents new
listener leases while existing work drains; the primary reverse session must be
released before its listener is disposed. Discovery and a registered certificate
provider do not prove current certificate trust, private-key access or reachability.

See [Identity Providers](IdentityProviders.md), [Crypto Provider](CryptoProvider.md),
[Reverse Connect](ReverseConnect.md) and [Transports](Transports.md).

## Owned repository samples

Open **Connection settings > Repository samples...** for a modeless setup window.
It provides the Console Reference Server, the opt-in DI pump software-update
simulator and the [Vision fixture inspection cell](../samples/Vision/VisualInspectionCell/README.md)
from an explicitly selected local checkout. These are bounded sample processes,
not a general command launcher or a new document kind.

Choose the trusted checkout, its existing Debug/Release build, managed framework
and build layout. Confirm that you trust the source and build, then select
**Check setup**. UaLens does not build, download or install missing prerequisites.
File existence is not binary attestation; only select a checkout and artifacts
you trust. An installed tool still needs a separately supplied built checkout.
The Vision cell requires .NET 10 and its three checked-in PNG fixtures; select
the current-runtime layout when its build is under `net10.0/<rid>`. It uses the
in-process OnServer backend with automatic certificate acceptance disabled,
without a camera or renderer.

Set the runtime from 1 to 300 seconds, confirm the particular run, and select
**Start sample**. The process receives a private run directory, configuration and
PKI with an allowlisted command and isolated environment. Changing the source,
build, trust decision or runtime revokes the applicable confirmation. Opening the
window or restoring ordinary workspace state never starts a sample or restores
trust in executable files.

The window shows lifecycle state, owned process ID, exit and cleanup evidence,
and bounded redacted output. Readiness requires discovery of the expected
application/endpoint with public-certificate evidence from that run's private
PKI; a successful process start or output line is not sufficient.
**Use advertised endpoint** only fills the primary connection address. Connect,
peer trust, user identity and any sample mutation remain separate explicit steps.

**Stop / retry cleanup**, closing this window, and quitting UaLens drain the owned
run. The current sample CLIs use timed graceful shutdown rather than a portable
immediate-stop IPC; Stop waits for that configured deadline before a bounded
termination fallback limited to the exact owned process tree. A cleanup failure
retains ownership for retry and blocks another launch. It is not reported as a
successful clean stop. Unrelated processes, source files and the host PKI are not
cleanup targets.

## Monitoring

A monitor document starts with values, quality, and source timestamps. Its **View**
selector offers Values, Trend, Timing: dots, Timing: bars, Timing: lines, Histogram,
and Heatmap. Trend plots the sampled signal; choosing a visualization does not
change server publishing.

![Monitor document with two live numeric values and a trend chart with axes and legend](Images/UaLens/monitoring.png)

*Trend combines the latest values and quality with a signal chart. The sample
axis is a display sequence, not a measurement of network latency.*

The requested publishing interval and publishing-enabled control are immediately
available. The server's revised values are displayed separately. Item settings
control sampling, monitoring mode, queues, discard policy, and data-change filtering.
Source and server timestamps have different meanings; neither is replaced with
the time at which the UI happens to render.

![Add monitored item dialog with sampling interval, data-change trigger and deadband controls](Images/UaLens/monitored-item.png)

*The address-space Monitor action lets you confirm sampling and an optional
data-change filter before adding the selected variable.*

Publishing, sampling, and display timing are distinct:

| Setting | Meaning |
|---|---|
| Sampling interval | How often the server evaluates a monitored value, subject to server revision and source behavior |
| Publishing interval | When a subscription has publishing opportunities; it does not force the source to change |
| Monitoring mode | Whether an item is disabled, sampling, or reporting |
| Publishing enabled | Whether the server publishes subscription notifications |
| Display pause or rendering throttle | A client presentation choice, not a server subscription setting |

Publish-worker/request limits belong to the **connection**. They affect every
subscription using that session and are not reapplied by unrelated per-document
settings changes.

Each monitor owns one notification-capture task. Charts have independent playback
cursors, so switching documents or views does not steal notifications from another
reader or reset collected history. Retention and rendering work are bounded;
retired history, dropped notifications, gaps, and republish activity must not be
confused with a lossless delivery guarantee.

The client dropped-notification counter counts notifications actually evicted
from the bounded delivery queue, not merely a full queue. It is separate from
server-side monitored-item overflow and publish-sequence gaps. Event View ignores
callbacks from subscriptions that have been stopped or disconnected.

Raw publish diagnostics use server subscription identifiers where the public
stack interface exposes an unambiguous identifier. Partitioned V2 callbacks can
instead carry a `client:` correlation identifier; this is deliberately not
presented as a server-assigned ID. Display-log buffering is bounded separately
from notification delivery.

## Tools and workflows

The catalog provides seventeen tool kinds:

| Tool | Workflows |
|---|---|
| Monitor | Values, events, quality/timestamps, charts, subscription/item settings, recursive node selection, export |
| Event View | Multiple sources, filters, selected fields, details, bounded event log, display pause/clear |
| Alarms | Retained conditions and branches, refresh reconciliation, explicit acknowledgement/confirmation/comment, contextual operator actions |
| Models | Type definitions, schema preview/export, native-safe structured editing, explicit read/write/call |
| Continuity Lab | Bounded recovery evidence, owned-subscription recreation, transfer/recreate-on-load, configured durable and redundant scenarios |
| PubSub | Explicitly started dataset observation, metadata and diagnostics, controlled publication and configured Action/adapter workflows |
| Companion Tasks | Typed DI, ISA-95, WoT/xRegistry, Robotics, Vision, AI and OpenUSD discovery/inspection, with bounded guided operations |
| Historian | Raw, processed, at-time and modified reads; cancellation, export, and explicitly requested updates/deletion |
| Subscription Bench | Variable pool, both live scaling sliders, aggregate rates/counts, shared defaults, shrinking and Stop cleanup |
| Performance | Explicit write/call workloads, selected baselines, metric/configuration/distribution comparison, bounded history, JSON and legacy CSV |
| File System | File/directory browsing, transfers, creation, rename, and deletion |
| Certificate Manager | Local application/trusted/issuer/rejected store management |
| GDS Discovery | Discovery endpoints and saved favorites |
| GDS Management / Push | Registered applications, issuance/CSR, trust lists, certificate update/apply |
| User / Role Management | Account/password restrictions and role/identity/application/endpoint mappings |

![Searchable Add Tool catalog with grouped workflows and offline configuration and capability descriptions](Images/UaLens/tool-catalog.png)

*The catalog explains configuration and live-operation prerequisites per tool.
An available document or successful browse does not establish mutation permission.*

Subscription Bench is slider-driven; there is no separate Run prerequisite. Shared
settings apply to existing resources and resources added later. Shrinking, Stop,
and document closure release the corresponding server resources.

PubSub and continuity experiments have their own explicit Start/Stop controls.
PubSub can own an independent network runtime without a primary UA session; only
configured primary-session adapters depend on that session.

Administration targets and prerequisites matter. History deletion, file deletion,
account/role changes, certificate application, and write/call workloads are not
automatically executed when a workspace loads. Certificate ApplyChanges can
intentionally terminate a connection.

GDS Discovery keeps endpoint results tied to the selected server; a late response
for an earlier selection cannot replace the current result. Preferred locales
are edited as an ordered list and sent to the active session only on **Apply**.
An unsuccessful locale change leaves the dialog open with the failure visible.

## Guided workflows

### Choosing a workflow

| Goal | Open or configure | Starts work |
|---|---|---|
| Inspect retained conditions and act on a selected event | Alarms | Explicit observation/refresh and operator commands |
| Inspect or edit a custom value | Models, or a Write/Call dialog | Explicit Read, metadata refresh, Write or Call |
| Explain recovery or transfer | Continuity Lab | Start observation, then the selected step |
| Receive a published dataset | PubSub | Explicit Start with interface/broker/security configuration |
| Inspect an industry model | Companion Tasks | Discover, Inspect, Prepare, review, then explicitly Run an offered task |
| Use X.509, issued tokens or hardware keys | Connection identity selection | Connect after provider selection |
| Accept a configured reverse connection | Connection setup | Explicit listener/wait and Connect |

Loading a workspace restores intent, not active workloads. It does not acknowledge
conditions, call methods, write values, start a publisher, bind a listener, restart
a server, or arm a sample task.

### Alarms

Use an event notifier that exposes real conditions. Alarms keeps the condition
identity separate from the source node, distinguishes branches, and uses the
selected event's current EventId for acknowledgement and confirmation.

Refresh reconciles retained state using the server's refresh markers. A completed
service request without the expected event sequence is not presented as a complete
refresh. Recreating a subscription and refreshing is distinct from transferring one.
Missing events and bounded retention are visible.

![Alarms document with retained condition branches, completed refresh markers and bounded event history](Images/UaLens/alarms.png)

*Observe an event notifier and reconcile retained conditions with Condition
refresh. This example uses a bounded reference-server alarm simulation.*

Acknowledgement, confirmation, comments and applicable advanced condition
operations are explicit. A disconnected or restored document never repeats them.
Authorization errors and unsupported operations are reported as failures; the UI
does not optimistically mark a rejected acknowledgement successful.

The implementation reuses the stack's typed alarm operations, generated event
records and V2 subscription facilities. See [Alarms and Conditions](AlarmsAndConditions.md).

### Models and structured values

Select a variable, DataType or method and open Models. Read the value or definition
explicitly. The same structured editing module serves Models and the Write/Call
dialogs; it supports generated encodeables and the stack's default NativeAOT-friendly
runtime type adapters without Reflection.Emit.

![Models document displaying BuildInfo fields, type evidence and a generated compact JSON schema](Images/UaLens/models.png)

*Read a structured value, inspect its definition and preview a schema. Editing
starts from a separate local draft and does not itself write to the server.*

Edits use a separate draft. A rejected or canceled draft cannot modify the original
value or write to the server. Nested fields, optional presence, unions and supported
arrays retain their type semantics.

OptionSet editors expose named bits without discarding unnamed bits. Unsigned
OptionSets retain their Byte, UInt16, UInt32 or UInt64 wire width. Concrete
Structure-backed OptionSets keep separate Value and ValidBits controls; changing
one does not imply changing the other. Null and empty masks remain distinct.
A missing native type adapter or incompatible width is an explicit editing error.

The shared array/matrix editor is available in Models and the Write/Call dialogs,
including for a new value whose declared rank establishes the shape. Enter
comma-separated dimensions, then select **Apply shape / create matrix**.
Reshaping preserves flattened row-major order. Adding or dropping elements requires
**Allow resizing / discarding trailing elements**; new elements receive typed
defaults. Rank, declared dimension maxima, checked products and the session's
encoding limits are validated before commit. The editor supports up to 32 dimensions
and 65,536 elements, subject to lower session limits. Pending or rejected shape
changes cannot silently commit the previous shape.

Null and empty one-dimensional arrays have different wire encodings. Null/empty
matrix fields retain their raw structure encoding; standalone matrix Variants
require a nonempty valid shape. A fresh matrix can be created without importing
an existing value. Metadata refresh, cancellation or session changes invalidate
the draft rather than letting it write through stale type information.

Schema preview/export uses the stack's schema provider. Missing definitions are
reported explicitly; denied reads, canceled resolution and connection failures
are not treated as authoritative absence. Metadata refresh and session-generation
changes discard stale definitions. Unknown opaque values are read-only.

Structure fields declared as `Number` (`i=26`), `Integer` (`i=27`) or `UInteger`
(`i=28`) use the standard Variant wire encoding in JSON, XML and binary schemas.
They do not require a server-side `DataTypeDefinition`. Unresolved custom data
types are reported as unavailable rather than exported as an untyped success.

See [Complex Types](ComplexTypes.md) and [Schema Generation](SchemaGeneration.md).

### Performance comparisons

Configure a Write or Call target, rate or bounded-concurrency burst, generator and
duration, then explicitly **Run**. Settings, target identity and available
endpoint/security evidence are captured at the start; changing later settings
does not rewrite a completed run's evidence. **Stop** cancels scheduling and waits
for issued operations to settle before retaining a partial result. Throughput uses
actual elapsed time, including that drain, rather than the requested duration.

Call targets read the method's typed `InputArguments` metadata, including array
ranks, and display the signature before acceptance. The latency chart includes
the complete range from one microsecond through its labeled ten-second overflow bucket.

Open **Compare runs**, select any retained row and choose **Use as baseline**.
Select another row to compare throughput, completed operations, errors, elapsed
time and latency. Absolute differences are selected minus baseline; relative
differences use the baseline. A zero baseline with a nonzero difference is shown
as unavailable, not infinity. Target, generator, rate, duration, concurrency,
argument signature and security differences are shown separately. Matching
captured settings does not control server load or establish equivalent environments.

Each newly captured run retains 71 fixed latency-bucket counts, not individual
samples. The last bucket contains latencies at or above ten seconds. Distribution
comparisons use each run's observed sample count and show percentage-point
differences; percentile values are bucket estimates. Missing legacy distributions
are labeled missing, never reconstructed from percentiles.

**Save results** writes versioned JSON retaining selections, configuration evidence
and distributions, or legacy aggregate-only CSV when that format is selected.
**Load results** validates the complete bounded file before replacing history; it
does not change workload settings or start a run. Import is limited to 4 Mi
characters and 4,096 records; history retains the newest 64. If a selected baseline
is evicted it is cleared explicitly, not replaced with a different run.
**Highlight latest 3** only highlights rows and is independent of comparison.

### Continuity Lab and diagnostics

Continuity Lab owns its observation subscription and any auxiliary sessions used
by its selected scenario. It uses the primary connection owner and the stack's
reconnect handling. Other documents retain their configuration.

The timeline distinguishes reconnect/recovery, transfer, recreation, missing
messages and republish activity. It records actual server subscription/partition
IDs where available; client correlation IDs are not relabeled as server IDs.
Requested and revised settings and server operation limits provide context for
throughput and failures.

![Continuity Lab showing the own-subscription recreation scenario and retained lifecycle and counter evidence after Stop](Images/UaLens/continuity.png)

*The lab retains bounded evidence after its resources are released. Recreation,
transfer and recovery are distinct observations; discontinuities alone are not
proof of lost source samples.*

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

### PubSub

A PubSub document owns its runtime independently of the primary UA session. Configure
the transport, address/interface or broker, publisher/writer/reader identifiers,
encoding and security requirements, then start it explicitly. Metadata, decoded
fields, state and bounded diagnostic evidence are shown together.

The default subscriber sink observes locally. Receiving a dataset does not write
to a UA server. Publication, Actions, external-server adapters and write-back need
explicit configuration and authorization. Stop/close releases the owned runtime;
primary-session loss affects only configured adapters that depend on that session.

![PubSub document with applied field values, message history and counters after a bounded local UDP loopback](Images/UaLens/pubsub.png)

*A bounded synthetic loopback retains received values and wire-message evidence
after Stop. Local dataset acceptance is separate from an external delivery guarantee.*

The module reuses `PubSubApplicationBuilder`, `IPubSubApplication`, transport,
metadata, source/sink, security and Action facilities. Configurations save safe
references, not raw broker credentials or SKS key material. No runtime or publisher
is started by application DI registration or workspace restore.

Choose an offline preset from the registered transport/profile catalog, then set
the endpoint/interface or broker and review the prerequisites. Presets neither
choose a destination nor supply credentials. The **Dataset / adapters** tab edits
ordered scalar fields, field/dataset UUIDs, metadata versions, portable UA mappings
and supported content masks. UADP retains the selected numeric publisher width;
JSON uses canonical numeric identities and supports UUIDs. Ambiguous numeric or
UUID-looking string identities are rejected for JSON rather than silently remapped.

Received scalar metadata can populate an offline reader configuration. Adoption
clears publication, write-back, responder and UA target mappings. Arrays, custom
types and promoted fields require a shared schema/encoding context and are reported
as unsupported by this scalar commissioning form, not flattened into a different
schema.

**Validate draft** checks the current form; **Apply configuration** updates the
reviewed intent. Field, mask, identity or adapter edits revoke Start authorization.
**Import configuration** validates the entire file before changing the document,
rejects duplicate/unknown properties, and cannot overwrite newer edits made while
the file is being read. **Export applied configuration** replaces a local JSON file
atomically. Files are limited to 65,536 JSON characters and 196,608 UTF-8 bytes;
fields are limited to 32. Import and restore never start network work.

Select either a registered out-of-band key provider or the registered provider's
pinned SKS endpoint and security group. A typed endpoint cannot retarget a provider.
The active token's ID, issuance, expiry and key sizes are checked before runtime
construction; missing or invalid material never falls back to unsecured traffic.
Provider registration is not SKS enrollment or key generation.

Action input forms preserve scalar types and ordering and allow at most 16
transient inputs. Each invocation requires fresh consent and supports explicit
cancellation. Results retain stack-provided request/correlation evidence; rejected,
uncorrelated or malformed responses cannot leave a previous success displayed.
Write-back requires complete, positionally mapped fields with matching names,
types and good quality before calling the configured UA writer.

Reaching a sample cap is not the same as completing its final send. The runtime
finishes the final publication before stopping its writer. A synthetic UDP loopback
also gets a bounded two-second local receive drain; expiry is recorded explicitly
and does not imply remote acknowledgement or a lossless network.

UDP/UADP and configured MQTT workflows use the stack transports. Other profiles
require their registered transport/provider and platform prerequisites. Kafka uses
the managed backend in the native app; the optional Confluent backend is not a
NativeAOT substitute. DTLS, Ethernet, SKS, brokers and advanced adapter scenarios
do not install their own external infrastructure.
DTLS remains gated when the installed stack configuration validator rejects its
endpoint scheme, even if a host transport provider is registered.

For a self-contained source use the repository's
[Console Reference PubSub Client](../samples/PubSub/ConsoleReferencePubSubClient/README.md)
in publisher mode with matching explicit settings. The full stack guide is
[PubSub](PubSub.md).

### Companion Tasks

Choose a model, select **Discover**, then **Inspect** a returned instance. Discovery
uses actual typed instances, not just namespace presence. Inspection is read-only.
The task list is populated by the selected instance; an arbitrary operation name
cannot be executed without a current inspection.

Choose an offered task and enter any required input, then select **Prepare selected
task**. Preparation is read-only: it rechecks the offered operation and displays
the exact target, endpoint and effect. The prepared request captures its input
without displaying raw input or endpoint query strings in the summary.
Review it and explicitly **Run selected task**.

Typed task forms expose named inputs instead of accepting an arbitrary method
payload. Scalar fields retain their wire types, including signed/unsigned 64-bit
values and ByteStrings; file inputs use an explicit local-file picker. Arrays,
matrices, structures and enumerations use the shared structured editor when the
provider supplies a portable DataType and rank/dimension contract. Unsupported or
unresolved types are not replaced with untyped JSON.

Accepted values have independent backing storage. The form validates exact field
names, ordering, types, ranks and dimensions before domain preparation. Server
definitions and their dependencies are hashed at editing/preparation and checked
again for Run; a stale nested definition also requires new preparation. Each task is bounded to 32
fields, 65,536 String-field characters and 1 MiB of encoded inputs, with the shared
editor's lower array/session limits still applying. Changing a field, canceling
an editor or changing the session invalidates the corresponding preparation and
confirmation. Domain-specific review evidence is shown without serializing
the input into a saved workspace.

A custom input's definition graph is limited to 128 types and 1 MiB of encoded
metadata. Nested value validation is limited to 64 levels; cyclic type references
are visited once. Concrete structures embedded in Variant fields are included in
the fingerprint rather than treated as untyped payloads.

Preparation is single-use and expires after five minutes. Run rechecks the current
session, identity, namespace mapping, endpoint/security profile and offered operation.
A changed selection/input, discovery/inspection, rebind, failed preparation or Stop
invalidates the earlier preparation. Starting Run consumes it even if cancellation,
authorization or provider execution fails. Preparing an overlapping request cannot
make an earlier request executable again. No operation is automatically replayed
after reconnect.

Configured hosts can additionally offer an operation as **DeploymentMutation**.
It requires a signed and encrypted connection, a matching host-registered policy,
and separate confirmation of the prepared deployment request. The preflight shows
the rule identifier and revision; Run checks that same rule again. A policy change,
expiry, identity or server-application change requires new preparation. A delayed
authorization response cannot revive a superseded session or an operation the
server no longer offers.

The default deployment policy denies all requests. A `CompanionDeploymentRule`
matches one exact endpoint, server application URI, secure policy, provider,
namespace-qualified target and operation, with a finite expiry and required
identity and input predicates. These predicates are trusted host code, not
expressions loaded from a workspace. Overlapping grants are rejected, not selected
by registration order. The server still enforces its own permissions.

For example, a host can register a rule for an identity and request already
approved by its deployment configuration before calling `AddUaLens`:

```csharp
services.AddSingleton(new CompanionDeploymentRule(
    id: "approved-recipe",
    revision: approvedPolicyRevision,
    endpointUrl: approvedEndpointUrl,
    serverApplicationUri: approvedServerApplicationUri,
    securityPolicyUri: SecurityPolicies.Basic256Sha256,
    providerId: approvedProviderId,
    targetId: approvedNamespaceQualifiedTarget,
    operationId: approvedOperationId,
    expiresAt: approvedUntil,
    acceptsIdentity: identity => ReferenceEquals(identity, approvedIdentity),
    acceptsInput: request => request.Input == approvedRecipe));
services.AddUaLens();
```

The example deliberately pins the current identity object; a newly acquired
identity needs renewed host authorization. Hosts that need claim-based rules
must verify their configured identity evidence rather than trusting a display
name. A host may instead supply `ICompanionDeploymentPolicy`; its asynchronous
result is subject to the same session, expiry, rule-revision and confirmation checks.
Registered `ICompanionProvider` instances replace the default provider set; their
typed factory remains directly constructible. Registration does not execute a task.
Existing built-in sample tasks remain sample-only: a deployment rule does not
remove their loopback restrictions or create new server capabilities.

Sample mutations additionally require a loopback endpoint and explicit confirmation
that it is a repository sample. Loopback alone does not prove a server is safe to
mutate. Confirm only after preparing the specific request. Preparation, confirmation
and operation input are never saved, and confirmation is cleared when the request
changes or is attempted. File exports require an explicit destination.

| Family | Guided scope | Intentional limit |
|---|---|---|
| DI | Identity, bounded update observation, SHA-256-verified package preparation/upload, separate sample install/confirm and advertised abort/resume methods | Repository samples only; device-specific installation, trust and power-cycle behavior are not inferred |
| ISA-95 | Typed V1/V2 job authoring, offered lifecycle operations and job/state/response snapshots | Deployment policy is required; execution transitions remain server-owned |
| WoT / xRegistry | Asset, group and immutable-version lifecycle; scoped deletion, enabled/default selection and single-Version refresh | Exact scope/epoch and deployment authorization; no forced or whole-registry refresh |
| Robotics | Published device-system/controller/axis topology and telemetry | No physical actuation or Robot Intent commanding |
| Vision | Typed media/calibration inspection, reviewed simulated acquisition/inference/feedback and managed 2D overlays | Signed/encrypted session and deployment policy; no physical sensors, media downloads or GPU rendering |
| AI | Typed authorized inference, asynchronous jobs, bounded request/response transfer and supported learning/evaluation operations | External egress needs its own exact-request policy; sample accounting is not real training |
| OpenUSD | Representation/asset inspection, bounded live/history capture and configured peer telemetry export | No renderer, remote asset fetching or complete federated-stage composition |

The AI sample request is offered only for a loopback backend with egress disabled;
both the UA server and backend restrictions are rechecked before invocation.
Responses and displays are bounded. A response that requires a separate transfer
workflow is not silently downloaded.

AI deployment tasks require the normal deployment rule in addition to the
provider's egress check. The default permits only local, egress-disabled
destinations; an injected `IAITaskEgressPolicy` must approve the exact reviewed
external destination and request. Inline responses are limited to 16 KiB and
transfers to 1 MiB. Reading a transfer requires its independently supplied
SHA-256. Upload failure closes the owned file handle and attempts to abort only
the transfer created by that request; an ambiguous Execute reply is not resubmitted.
The advertised `MaxInlinePayloadSize` is a UInt32. Zero disables inline requests;
it is not an unlimited quota, and a transfer must be chosen explicitly.
Job observation is bounded to eight snapshots. Program Halt is a request, not
proof of backend cancellation. Learning tasks preserve explicit model/dataset/
deployment selections; evaluation reads existing metrics rather than inventing
a start-evaluation contract.

ISA-95 Store, Start, Update, Cancel and Clear use the selected V1/V2 typed client;
V2 additionally offers Pause, Resume and Abort where executable. A unique complete
order-receiver/response-provider/response-receiver set is required. Store does not
overwrite an existing job. Preparation pins the observed job and, for V2, its state.
Only the `ReturnStatus` success bitmap (bit 0, value 1) is accepted; zero or
additional error/unknown bits do not prove success. The subsequent job/state read remains
authoritative and does not imply execution or completion. V1's catalog does not
provide live execution state, and returned responses may be historical.
BeginExecution, Complete and Close are not desktop commands. Generated clients
may resolve an instance MethodId when a server rejects the type-declaration ID
with BadMethodInvalid; accepted or ambiguous requests are never replayed.

Vision inspection remains read-only. Sensor tasks list exact media endpoint
NodeIds, state and confidentiality without exposing endpoint URIs or credentials.
Calibration inspection reads published intrinsics/extrinsics; it neither solves
nor writes a calibration. Bad or Uncertain read quality is an error, not a
successful default value.

The simulated Vision workflow uses the same **Prepare / Review / Run** and
deployment authorization as other mutations:

| Selected target | Reviewed operations |
|---|---|
| Sensor | Get one clip, acquire/release one stream lease, configure a selected stream, select preferred media endpoints |
| Pipeline | Run one inference, start/stop a 1-15 second continuous window, submit detections, inspection, correction or frame-reference feedback |
| Detection result | Export a new local SVG containing managed pixel-space overlay geometry |

Only executable methods are offered, and a lease/window task also requires its
Release/Stop method. Mutations require a SignAndEncrypt session and a sensor
declared **Simulated**; Physical and Hybrid sensors are rejected. This declaration
is server-provided evidence, not hardware attestation, and does not replace the
host's exact deployment rule. Preparation pins the sensor/frame, component,
endpoint identity/address/authentication, pipeline state and disclosed AI
deployment. A changed session, identity, peer certificate, namespace table or
binding invalidates the request. A request expires within five minutes and cannot
be dispatched twice.

Pipeline execution is not assumed local merely because the camera is simulated.
A verified loopback, no-egress AI deployment on a loopback server can satisfy the
default destination check. An unknown or external destination requires a
host-injected `IVisionExecutionPolicy` accepting that exact task, in addition to
normal deployment confirmation. Implicit fallback routing and unsafe destination
URIs are not authorized. Feedback may affect overlays, reconciliation, acquisition
or learning according to its purpose; a successful method reply does not prove
training or a visible overlay.
An `OnServer` deployment need not advertise a separate `EndpointUri`; its reviewed
destination is the selected OPC UA server itself. That is accepted by the local
default only with egress disabled and a loopback UA server. Missing destinations
in other inference locations do not receive that exception.

Inputs retain their typed structures and independent storage. Arrays are limited
to 32 entries and a complete encoded request to 1 MiB. Image metadata is limited
to 8192 pixels per dimension, 16 megapixels and 1 MiB of encoded media. Inline
bytes must match the descriptor's byte count and SHA-256; feedback also respects
the server's advertised inline limit. Empty detections require an explicit
empty-scene observation. A correction supplies exactly one corrected result kind,
or explicitly retracts all; its existing result identity and geometry kind are
checked before submission.
Use **Omit optional structure** to leave an optional frame reference absent.
Omission is explicit, revokes preparation, and is not available for required
structures or arrays; accepting a new typed value clears the omission.
Result correlation IDs are limited to 256 characters. Frame-reference feedback
may establish provenance for an upcoming inspection ID; only corrections require
an already-published result. Opaque `opcua-inline` references must match an
advertised clip origin of the selected simulated sensor, with no credentials,
query or fragment. That origin is pinned until execution; the URI is never fetched.

UaLens never opens a returned media URI. GetClip checks the exact returned
endpoint, format and inline evidence. A returned stream token is released with a
fresh five-second cleanup budget, including when returned metadata is invalid or
the acquisition was cancelled. Neither token nor full URI is displayed or saved.
A continuous window does not take over an existing run: only an acknowledged
Start is followed by the owned Stop, and completion requires observing
Continuous=false. Stop rechecks the pipeline/sensor identities, frame, deployment
and simulated reality; changed bindings are not stopped as though still owned.
An unacknowledged Start is an unknown outcome requiring
inspection, not an excuse to replay Start or stop somebody else's run. Cleanup
failure remains visible alongside the original failure; review expiry does not
prevent cleanup on the unchanged owning session.

RunInference resolves the exact returned **ResultId property** within the selected
pipeline, considering at most 32 result candidates. A matching BrowseName suffix,
the latest result or a different sensor/pipeline is not accepted as correlation.
Typed detection, inspection and segmentation results are bounded and snapshotted;
failure to verify a returned result never causes another inference request.

The SVG export contains existing 2D boxes and labels, image dimensions, source
identifiers and frame SHA-256. It contains no image pixels, media URI, script,
external asset, GPU rendering or 3D reprojection. Geometry and number formatting
are checked, labels are XML-escaped, and an existing file is never overwritten.
Partial output uses a private staging file that is removed on failure.

Registry tasks retain exact identifiers, versions and independently copied
document bytes; identifiers are not derived from the uploaded content. JSON is
limited to 64 KiB of strict UTF-8 and depth 32, and duplicate members are rejected.
WoT documents must also match the selected Thing Model/Thing Description group.
Strict creation does not overwrite or silently reuse an existing version.

Inspect the scope before deletion: a logical Resource uses **MetaEpoch** and
includes all its versions, whereas a Version uses its own **Epoch** and the
server's default/last-Version rules. Group deletion includes its children.
Zero is not accepted as an expected epoch because it disables the server's
concurrency check. A changed Xid/scope or epoch invalidates preparation.
WoT enabled/default-version operations use logical-resource MetaEpoch, including
when selected through a Version node. Server dependency policy remains authoritative.

WoT registration separately acknowledges server AutoRefresh. **Refresh one
existing WoT Version** selects one exact group/resource/version, pins its content
digest and refresh generation, and checks returned request correlation, identity,
generation, counts and outcome. It never selects all resources or dependents,
uses no Force, and requests a five-second server budget with one worker.
Returned materialization evidence is not an external endpoint reachability test.

Asset creation compensates only its newly created asset if opening or uploading
fails; cleanup failures remain visible alongside the original error. Asset deletion
requires membership under the selected manager and rechecks the selected WoT file
and content digest. It does not delete another asset or a physical device.
Lifecycle operations have a 30-second client budget; registry upload-handle cleanup
uses a separate five-second budget and preserves both Write and Close failures.
Service acceptance is distinguished from a subsequently observed tree state.

OpenUSD capture uses the shared connector's conversion rules, retaining source
quality and timestamps rather than substituting a numeric default. Live capture
owns one V2 subscription and snapshots values inside its notification callback.
Its queue is limited to 128 entries and 1 MiB; a queue/encoding limit or subscription
failure rejects the capture rather than exporting incomplete success. Live values
retain the server's Publish sequence and time. Ordinary reads and history do not
invent that Publish evidence.

Observation windows are 1-15 seconds. History uses an increasing UTC range of at
most 24 hours and requires strictly increasing source timestamps inside the
reviewed half-open range. Both captures retain at most 128 converted samples,
further limited by the document's result-field budget. Each encoded sample is
limited to 16 KiB and their aggregate to 1 MiB. Empty observations, bad values,
metadata changes and unsupported conversions are explicit failures. Stopping
releases only the owned subscription or history continuation.

Configured OpenUSD peers require separate host-owned session factories, exact
endpoint/application/security-policy rules and identity predicates. Primary
credentials are never forwarded. At most sixteen distinct peers and four levels
are visited; unknown, ambiguous and cyclic origins are rejected. Namespace,
binding and peer-certificate evidence is pinned and rechecked after acquisition.
A faulty factory cannot transfer ownership of the primary or an ancestor session.

History and peer exports create a new local directory containing `values.usda`
and `evidence.json`. The evidence includes portable source IDs, binding and origin
identifiers, source/converted binary values and SHA-256 digests; it contains actual
sampled data and should be handled accordingly. Peer values are namespaced below
`/Peers/<configured-id>/...`. These are managed override/provenance exports, not
downloads of remote USD assets, composition of a complete federated stage, or a
hard real-time/lossless delivery guarantee. Existing paths are not overwritten.

DI package preparation snapshots at most 64 MiB from an explicitly selected local
file and checks the independently supplied SHA-256. Run uploads those verified
bytes, not a reopened file that could have changed after review. Upload does not
install; a returned completion state-machine NodeId remains separate evidence
requiring observation. Installation rechecks the state and method permissions
before calling the typed software-update client. Returned method success is kept
distinct from the subsequently observed device state.

The opt-in [pump software-update simulator](../samples/DI/PumpDeviceIntegrationServer/README.md#software-update-simulator)
provides an in-memory test target. Existing sample actions remain loopback-only;
the desktop does not provide a blanket switch for firmware operations on arbitrary
deployments. An optional recovery method is offered only when the server exposes
it as executable; its actual response remains authoritative.

OpenUSD export uses an unused destination, advertised assets and digest checks with
bounded asset counts and sizes. Metadata inspection alone is not proof that an
asset has been verified. Read-only snapshots do not enable command bindings.
Use the address-space explorer for generic browsing of an unrecognized model.

Matching servers are listed in [Sample Applications](samples.md). Brokers, token
authorities, redundant sets, hardware and privileged network facilities must be
supplied explicitly; a gated scenario is not reported as demonstrated.

## Saved workspaces

Version 2 stores typed document configurations, order/selection, layout preferences,
the safe connection profile, and connection-level publishing limits. Each tool
serializes its own versioned configuration with generated JSON metadata. It does
not serialize running jobs, server handles, collected histories, or credential
material.

Legacy `.subex`/version-1 session files are imported as disconnected monitor
configuration. Because those files omit security and identity intent, they do not
authorize an automatic Anonymous/None connection. Original files are not overwritten
as part of import. Unsupported document kinds, versions, or malformed configuration
are reported rather than silently discarded.

Document configuration and workspace connection intent commit together. A failure
while cleaning up replaced documents is reported without pairing the newly restored
documents with the previous workspace's connection profile. Profile-less legacy
imports are saved without a connection profile.

Appearance and favorites use per-user UaLens locations. Writes use a
completed sibling temporary file before replacing the destination. Missing favorites
are an empty initial state; corrupt or inaccessible favorites are an error, not a
misleading successful empty list. Endpoint paths are case-sensitive.

## Capability evidence and prerequisites

The catalog distinguishes **Supported**, **Unsupported**, **Unknown**, **Requires
configuration**, and **Denied**. These states describe available evidence, not
unconditional permission or full server support. A probe checks a concrete target
or advertised permission; it does not claim the server implements every operation
in a tool. For example, an EventNotifier attribute can advertise events without
proving that a particular alarm method is authorized. Write and Call probes inspect
permissions; they never perform a trial mutation.

Checks are bounded and tied to the current session and namespace mapping.
A reconnect or explicit refresh invalidates stale evidence. Transport failures
are retryable rather than being cached as an absent feature.
Unknown, denied, or transient failures do not become successful empty results.
Documents can be opened for configuration while disconnected.

The [guided workflows](#guided-workflows) use the stack's alarm, model, continuity,
PubSub, connection-provider and companion modules.

| Capability | External prerequisite or intentional limit |
|---|---|
| Alarms and Conditions | Real condition source, event support and operator permissions; acknowledgement and confirmation are explicit state changes |
| X.509 / issued identity | Existing certificate with usable key, or configured token authority/provider; no automatic OAuth or PKI provisioning |
| Durable transfer / failover | Compatible server storage, same-user transfer and configured redundancy; the Quickstarts store requires graceful shutdown and excludes issued-token persistence |
| Structured values | Exposed definitions or registered schemas; named bits and matrix creation retain wire semantics and declared bounds; opaque values stay read-only |
| PubSub | Explicit network interface or broker and matching security/key providers; restoring a workspace never starts traffic |
| Reverse connect | Registered binding/listener, expected server identity, trust and firewall permissions; unknown peers are not accepted |
| Diagnostic evidence | Server counters may require authorization; absent evidence is not zero and unsynchronized clocks do not prove end-to-end latency |
| Hardware keys | Host-registered store/crypto provider and device/PIN access; no key export, module installation, HSM provisioning or blanket FIPS claim |
| Companion tasks | Matching model instances and repository samples; guided operations are not complete domain-authoring suites |

The catalog labels model maturity independently. Published OPC 40010 Robotics is
not the draft Robot Intent extension. Vision, AI Model Management, OpenUSD,
xRegistry, WoT Connectivity and individual draft bindings have their own
draft/version labels rather than one blanket conformance claim.
