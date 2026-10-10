# UaLens

UaLens is an Avalonia desktop application for exploring OPC UA address spaces,
monitoring values and events, editing data, and administering servers. It uses
one primary connection shared by its document tabs. GDS tools can also use
separate connections.

## Contents

- [Getting started](#getting-started)
- [Workspace](#workspace)
- [Connection and trust](#connection-and-trust)
  - [User identities](#user-identities)
  - [Configured token authorization](#configured-token-authorization)
  - [User-certificate enrollment](#user-certificate-enrollment)
  - [Hardware-backed keys](#hardware-backed-keys)
  - [Reverse connect](#reverse-connect)
- [Local repository samples](#local-repository-samples)
- [Monitoring](#monitoring)
- [Tools](#tools)
- [Alarms](#alarms)
- [Models and structured values](#models-and-structured-values)
- [Performance comparisons](#performance-comparisons)
- [Continuity Lab](#continuity-lab)
- [PubSub](#pubsub)
- [Opening NodeSet2 files](#opening-nodeset2-files)
  - [Missing dependencies](#missing-dependencies)
  - [Import errors and cancellation](#import-errors-and-cancellation)
  - [Exploring the imported graph](#exploring-the-imported-graph)
  - [Returning to a server](#returning-to-a-server)
- [Companion specification tasks](#companion-specification-tasks)
  - [Device Integration](#device-integration)
  - [ISA-95](#isa-95)
  - [WoT and xRegistry](#wot-and-xregistry)
  - [Robotics](#robotics)
  - [Vision](#vision)
  - [AI](#ai)
  - [OpenUSD](#openusd)
- [Saved workspaces](#saved-workspaces)
- [Tool availability and prerequisites](#tool-availability-and-prerequisites)

## Getting started

From a repository checkout:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet run --project tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0
```

The application supports .NET 8, .NET 9 and .NET 10. The managed tool package is
`OPCFoundation.NetStandard.Opc.Ua.Lens`, with the command `ualens`.
See the [tool README](../tools/Opc.Ua.Lens/README.md) for installation.

Enter a server address in the connection bar, select **Connect**, and choose an
advertised endpoint and identity. Review the security policy and certificate
before connecting. To work offline, choose **File > Open NodeSet2 files...**.

The screenshots use a local Console Reference Server. Select security and
identity settings appropriate to your own server.

## Workspace

Use **Add Tool** or the **Tools** menu to open a document. **Add Tool** opens a
searchable tool catalog grouped into Explore / Connect, Observe, Administer,
and Diagnose. Address-space actions open tools for the selected node.

The explorer and attributes/references inspector are resizable. The available
node actions include Monitor, Write, Call, Events, History, and Inspect.
Disconnected documents keep their configuration for the next connection.
Local certificate management and discovery are available without a connection.

![UaLens workspace with an expanded address space and monitored values](Images/UaLens/workspace.png)

Use the **Namespace** dropdown to highlight nodes from one namespace. The
background, left marker and bold label identify matching nodes, including
children opened later. **None (no highlighting)** clears the selection.
Highlighting leaves other nodes visible and does not change tree selection.

The default appearance follows the operating system. Choose Light, DarkStandard
or DarkNavy for an explicit appearance. Charts keep each item's palette slot
when the theme changes. Use item names and separate views to distinguish traces
whose colors are similar.

| Shortcut | Action |
| --- | --- |
| F2 | Rename the selected document |
| Enter / Escape while renaming | Save / cancel the title edit |
| Ctrl+Tab / Ctrl+Shift+Tab | Select the next / previous document |
| Ctrl+S / Ctrl+O | Save / open a workspace |
| Ctrl+C / Ctrl+Shift+C in the attributes inspector | Copy the value / `Name: Value` |

On macOS, the attribute-copy commands use Cmd. The tab and attribute context
menus expose the same actions. Escape closes the tool catalog without adding a
document.

Write reads the variable's type before enabling editing. After sending a value,
it displays the result until **Acknowledge and close**. **Cancel wait** cancels
the local wait; the server may already have applied the write. Read the variable
again if the outcome is unknown.

Closing UaLens waits for owned operations, documents and connection cleanup.
Shutdown progress and cleanup errors appear in the status and log.

## Connection and trust

The endpoint picker shows advertised security modes, policies and user identities.
UA-TCP, HTTPS and OpenAPI WebSocket endpoints use the same browsing and tool
workflows. WebSocket bearer authentication requires `wss://`.

![Endpoint picker with security modes, policies and identity choices](Images/UaLens/connection-policies.png)

For an untrusted certificate, choose **Reject**, **Accept Once**, or **Trust
Permanently**. Accept Once applies only to the selected endpoint and certificate;
disconnecting or changing the target clears it. Permanent trust is written to
the certificate store. Invalid or revoked certificates cannot be accepted through
the untrusted-certificate prompt.

### User identities

Choose Anonymous, UserName, an X.509 user certificate, or a configured issued-token
provider supported by the server's policy. Changing the user replaces the session
and rebinds documents. Reconnecting preserves the selected security and identity
profile. Saved workspaces do not contain passwords, private keys or bearer tokens.

An X.509 user certificate must have an accessible private key and a compatible
server token policy. It is separate from the application's channel certificate.

### Configured token authorization

If the selected provider supports interactive authorization, choose **Authorize
configured token provider...** and complete its prompts. Then choose **Use
identity** and connect. Authorization can be cancelled and has a five-minute
limit. Provider and authority configuration must already be available.

### User-certificate enrollment

When offered by the configured certificate source, select **Enroll / renew
configured user certificate...**. Use **Prepare signing request**, review the
application/group/type and authority shown, then confirm **Request certificate**.
Review the result and separately confirm **Add reviewed certificate**.

Closing cancels pending local work. It does not undo a request already submitted
or a certificate already added. The existing certificate stays in the store.
The available enrollment source determines certificate and authority requirements.

### Hardware-backed keys

Select a certificate from a configured hardware-backed provider and supply its
PIN when requested. Keys remain in the provider. Install and configure the device,
provider and certificate before using them in UaLens.

### Reverse connect

In connection settings, choose a registered listener and the expected server
identity. Select **Start listener**, then **Wait / discover**, and choose the
endpoint, security and identity for the connection. **Use setup** saves these
settings without starting a listener or connection.

The expected server, certificate trust, listener address and firewall access
must be configured. Stop and cancellation are available during startup and
waiting. Disconnect the primary reverse session before releasing its listener.

See [Identity Providers](IdentityProviders.md), [Crypto Provider](CryptoProvider.md),
[Reverse Connect](ReverseConnect.md) and [Transports](Transports.md) for configuration.

## Local repository samples

Open **Connection settings > Repository samples...** to run the Console Reference
Server, DI pump software-update simulator or
[Vision inspection cell](../samples/Vision/VisualInspectionCell/README.md).

Select a trusted local checkout and an existing Debug/Release build, framework
and layout. Confirm trust in the selected build and choose **Check setup**.
Build missing samples separately. The Vision cell requires .NET 10 and its
three checked-in PNG fixtures.

Choose a runtime from 1 to 300 seconds, confirm the run, and select **Start
sample**. The window shows process state, output and cleanup results.
**Use advertised endpoint** fills the connection address; connect and review
certificate trust separately.

**Stop / retry cleanup**, closing the sample window, and quitting UaLens stop the
owned run. Stop waits for the sample's configured shutdown deadline before using
its bounded termination fallback. If cleanup fails, retry it before starting
another sample.

## Monitoring

A Monitor document displays values, quality and source timestamps. Its **View**
selector offers Values, Trend, Timing: dots, Timing: bars, Timing: lines, Histogram,
and Heatmap.

![Monitor showing numeric values and a trend chart](Images/UaLens/monitoring.png)

In **Timing: lines**, select **Lane item** and **Line style** to choose
Interpolated, Wave or Zigzag. Clicking a lane label cycles the same styles.
Changing a view or line style does not change publishing or recorded samples.

![Add Item with sampling interval and data-change filter settings](Images/UaLens/monitored-item.png)

| Setting | Purpose |
| --- | --- |
| Sampling interval | Requested server sampling: `-1` inherits publishing, `0` requests fastest, or enter a positive interval up to 3,600,000 ms |
| Publishing interval | Requested subscription publishing interval; the revised value is shown separately |
| Monitoring mode | Disabled, Sampling or Reporting |
| Publishing enabled | Enable or disable subscription notification delivery |
| Deadband | Data-change filter; percent deadband accepts 0 through 100 |
| Display pause | Pause presentation without changing the server subscription |

Numbers use the current locale's decimal separator. Invalid settings remain
visible with a field-specific explanation.

Publish-worker/request limits are connection settings shared by its subscriptions.
Item settings and identities survive disconnects and engine changes. CSV and JSON
exports include each sample's recorded name, NodeId and timestamps, including data
from removed items.

Counters distinguish client queue drops, retired history, server overflow,
publish-sequence gaps and republish activity. Inspect these alongside server
quality and timestamps when diagnosing missing data.

## Tools

**Add Tool** opens the searchable tool catalog:

| Tool | Use it for |
| --- | --- |
| Monitor | Values, quality, timestamps, charts, item settings and export |
| Event View | Event sources, filters, selected fields, details and a bounded log |
| Alarms | Retained conditions, refresh, acknowledgement, confirmation and comments |
| Models | Type definitions, schemas, structured editing, Read, Write and Call |
| Continuity Lab | Recovery, transfer, recreation and configured durable/redundant scenarios |
| PubSub | Dataset observation, publishing, metadata, Actions and configured adapters |
| Companion specification tasks | Inspect industry-model instances and perform their offered tasks |
| Historian | Raw, processed, at-time and modified reads, export and authorized updates/deletion |
| Subscription Bench | Variable pools, scaling sliders, rates and resource counts |
| Performance | Write/Call workloads and comparison of recorded runs |
| File System | Browse, transfer, create, rename and delete files/directories |
| Certificate Manager | Local application, trusted, issuer and rejected stores |
| GDS Discovery | Server discovery and favorites |
| GDS Management / Push | Application registration, issuance, trust lists and certificate changes |
| User / Role Management | Accounts, passwords, roles and identity/application/endpoint mappings |

![Add Tool catalog with search and workflow groups](Images/UaLens/tool-catalog.png)

Subscription Bench responds to its scaling sliders. Shrinking, **Stop** and
closing the document release its resources. PubSub and Continuity Lab have
separate **Start** and **Stop** controls.

Administration commands require server permissions. Certificate **ApplyChanges**
may terminate a connection. Loading a workspace does not execute writes, calls,
deletions or administration commands.

## Alarms

Select an event notifier with condition support. Start observation and use
**Condition refresh** to load retained conditions. UaLens displays condition
branches, refresh progress and bounded event history.

![Alarms with retained conditions and refresh results](Images/UaLens/alarms.png)

Select the current event before acknowledging, confirming or adding a comment.
Permission failures and unsupported operations are displayed. Reconnecting or
opening a workspace does not repeat an operator command.
See [Alarms and Conditions](AlarmsAndConditions.md) for server requirements.

## Models and structured values

Open Models for a variable, DataType or method. Read its value or definition,
inspect fields, and preview or export a schema. The same editors are available
from Write and Call.

![Models displaying structured fields and a JSON schema](Images/UaLens/models.png)

Edits are local until explicitly submitted. Editors support nested structures,
optional fields, unions, OptionSets, arrays and matrices. OptionSets display named
bits and preserve unnamed bits.

For a matrix, enter comma-separated dimensions and choose **Apply shape / create
matrix**. Enable **Allow resizing / discarding trailing elements** when changing
the element count. The limit is 32 dimensions and 65,536 elements, subject to the
server's lower limits.

Metadata refresh or a connection change invalidates a pending edit. Missing type
definitions are reported; opaque values are read-only.
See [Complex Types](ComplexTypes.md) and [Schema Generation](SchemaGeneration.md).

## Performance comparisons

Choose a Write or Call target, configure rate or concurrency and duration, then
select **Run**. Call targets show their typed argument signature before acceptance.
**Stop** stops scheduling and waits for issued operations to finish.

In **Compare runs**, choose **Use as baseline**, then select another result.
The comparison shows throughput, operations, errors, elapsed time and latency,
along with target, workload and security differences. Latency percentiles are
estimates from the recorded buckets.

**Save results** exports JSON with configuration and distribution data, or
aggregate-only CSV. **Load results** imports history without starting work.
The document keeps the latest 64 runs; **Highlight latest 3** is a display option.

## Continuity Lab

Select a scenario and start observation. The timeline distinguishes reconnection,
transfer, recreation, missing messages and republish activity.

![Continuity Lab with subscription recreation and lifecycle counters](Images/UaLens/continuity.png)

Scenarios include observing a manually managed outage, recreating the lab's
subscription, restoring its snapshot, and configured durable or redundant
operation. For a restart scenario, follow the displayed save/close steps, restart
the selected server yourself, then request restore.

Durability and failover require compatible server configuration. See
[Transfer](TransferSubscription.md), [Durable Subscriptions](DurableSubscription.md)
and [High Availability](HighAvailability.md). Exported diagnostic evidence omits
credentials; server counters may require additional permissions.

## PubSub

Configure the transport, address/interface or broker, publisher/writer/reader
identifiers, encoding and security, then select **Start**. The document displays
metadata, decoded fields, state and diagnostics. **Stop** releases its runtime.

![PubSub with received fields, message history and counters](Images/UaLens/pubsub.png)

Use **Dataset / adapters** for scalar fields, identifiers, metadata versions,
UA mappings and content masks. Select **Validate draft**, then **Apply
configuration**. Changes require renewed Start authorization.
**Import configuration** and **Export applied configuration** exchange local JSON
files without starting traffic.

Observation displays datasets locally. Publishing, Actions and server write-back
require their own configuration and confirmation. A secured profile requires its
configured key provider or SKS group. Supply brokers, network interfaces and other
external prerequisites before starting.

For a sample publisher, use the
[Console Reference PubSub Client](../samples/PubSub/ConsoleReferencePubSubClient/README.md).
See [PubSub](PubSub.md) for transport and security configuration.

## Opening NodeSet2 files

Choose **File > Open NodeSet2 files...** or the same action in the welcome area.
Select one or more XML files to explore a combined read-only address space.
The bundled OPC UA core model is available without internet access.

### Missing dependencies

UaLens matches dependencies by model URI and revision. Files selected together
can reference one another despite different namespace-table orders. UaLens also
looks for XML files beside the selected files.

For each missing dependency, choose **Browse local file**, or **Search and
download** to permit a lookup in
[OPCFoundation/UA-Nodeset](https://github.com/OPCFoundation/UA-Nodeset).
Local model contents are not uploaded. If the repository has no match, or a
download fails, select a local file instead.

The selected file must satisfy the required `ModelVersion` and `PublicationDate`.
The human-readable `Version` is a label. Complementary files for the same model
revision are supported; conflicting revisions and duplicate NodeIds are rejected.
Dependencies may require further files.

An import supports up to 128 documents, 500,000 nodes, 64 dependency levels and
256 MiB of content, with at most 64 MiB per document. Downloads stay in memory
and do not overwrite local files.

### Import errors and cancellation

The current address space stays available until the complete import succeeds.
Cancelling or encountering an invalid file leaves it unchanged. A successful
import closes the primary live connection; existing documents keep their settings,
but session-bound actions are unavailable offline.

Import errors appear in the banner and **Log** panel. Correct the file and reopen
it. Standard names used as NodeIds need entries in that document's `<Aliases>`
table, for example:

```xml
<Aliases>
  <Alias Alias="HasProperty">i=46</Alias>
</Aliases>
```

Alternatively, use the NodeId directly:
`<Reference ReferenceType="i=46">ns=1;i=2</Reference>`.
UaLens does not infer undeclared aliases.

### Exploring the imported graph

The offline banner shows document, node and unresolved-reference counts.
Its tooltip lists source documents and model URIs. Displayed namespace indexes
belong to the combined graph.

Use **View** to browse Objects, ObjectTypes, VariableTypes, DataTypes,
ReferenceTypes or Views. **AllNodes** groups every imported node by namespace,
including nodes not reachable from Root. Type views include their members.

Offline search covers display names, browse names and NodeIds throughout the
imported graph. **Enter** starts a search; **F3** advances to the next match.
Double-click a reference target to inspect a locally resolved node.
The references panel shows forward and inverse links and identifies unresolved
targets.

**Find by path...** accepts relative paths with the combined namespace indexes,
such as `/2:Machine/3:Reading`. **View NodeState... > NodeSet2 XML** shows the
authored node definition, including values, data-type fields and extensions,
using the source file's namespace table and aliases.

Imported values and access flags are static metadata. Write, Call, Monitor,
Events and History are unavailable, and remote-server targets cannot be followed.

### Returning to a server

Choose **Close models** to clear the offline graph, or connect successfully to a
server to replace it. A cancelled or failed connection leaves the graph available.
Model files and downloads are not saved in a workspace; reopen them after restart.
For the XML format, see [OPC UA Part 6, Annex F](https://reference.opcfoundation.org/Core/Part6/v105/docs/F).

## Companion specification tasks

Open the industry-model task tool from **Add Tool**, choose a model, and select
**Discover**. Select a returned instance and choose **Inspect** to see its
published values and available tasks.

Choose a task, enter its typed inputs, and select **Prepare selected task**.
Review the target and effect, supply the required confirmation, then choose
**Run selected task**. Changing the target, input or connection requires fresh
preparation. Preparations expire after five minutes and can be used once.

Deployment mutations require an authorized identity, a signed and encrypted
connection, and a configured deployment policy. Sample tasks require confirmation
of the selected loopback sample. File operations ask for a local source or
destination. Reconnecting does not replay tasks.

### Device Integration

Inspect device identity and software-update state. Select a package file and
supply its SHA-256 to prepare an upload. Upload, installation, confirmation and
offered recovery methods are separate tasks. The
[pump simulator](../samples/DI/PumpDeviceIntegrationServer/README.md#software-update-simulator)
provides a local software-update target.

### ISA-95

Inspect jobs, resources, responses and available state. The selected V1/V2
model determines which Store, Start, Update, Cancel and Clear tasks are offered;
V2 may also offer Pause, Resume and Abort. Review the resulting job/state after
each command.

### WoT and xRegistry

Inspect assets, groups, resources and versions. Tasks include creation, immutable
version upload, enabled/default selection, deletion and refresh of one existing
WoT version. Review deletion scope carefully: deleting a resource includes its
versions, and deleting a group includes its children. Changed content or epochs
require new preparation.

### Robotics

Inspect published device systems, controllers, axes and telemetry. Physical
actuation and Robot Intent commanding are not available.

### Vision

Inspect sensors, media descriptors, calibration and results. For an authorized
simulated sensor, offered tasks include clip acquisition, stream leases,
inference, a bounded continuous run and typed feedback. Continuous runs last
1-15 seconds. Use **Omit optional structure** when an optional frame is absent.

Select a detection result to export a new local SVG with boxes and labels.
The SVG contains overlay geometry and provenance, not image pixels. Physical and
Hybrid sensors are not accepted by the simulation tasks.

### AI

Inspect configured inference deployments, jobs, datasets and evaluation results.
Offered tasks include authorized inference, job observation, bounded request/
response transfers and learning operations. External destinations need a matching
egress policy. Review the selected deployment, input and destination before Run.

### OpenUSD

Inspect representations, assets and bindings, or capture live/history values.
Live observation windows last 1-15 seconds; history selects a UTC range up to
24 hours. Configured peers need their own authorized connections.

Export to a new directory to create `values.usda` and `evidence.json`, including
the sampled data and provenance. Existing paths are not overwritten. Rendering
and complete federated-stage composition are not available.

Matching servers are listed in [Sample Applications](samples.md).

## Saved workspaces

**File > Save workspace** stores document settings, tab order and selection,
layout, safe connection choices and connection-level publishing limits.
Opening a workspace restores documents offline. It does not restore live jobs,
server handles, captured history or credentials.

Legacy `.subex`/version-1 files import as disconnected monitor configurations.
They do not contain enough security information to authorize an automatic
connection. Unsupported or malformed files produce an error.

Appearance and favorites are stored per user. NodeSet2 selections and downloaded
models are not included in workspace files.

## Tool availability and prerequisites

The **Add Tool** catalog shows **Supported**, **Unsupported**, **Unknown**,
**Requires configuration**, or **Denied** for the current target. A status
describes the available evidence; server permissions still govern each operation.
Tools can be opened for configuration while disconnected.

Missing certificates, token providers, broker/key configuration, reverse listeners
or compatible server models appear as setup requirements. Connection changes and
refresh update stale capability information. Model-specific entries identify their
published or draft specification/version.
