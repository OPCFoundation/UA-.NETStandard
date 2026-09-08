# UaLens

UaLens is the stack's Avalonia desktop engineering tool. It combines address-space
exploration, monitoring, history, events, diagnostics, and administration in one
workspace. Its source is in [`tools/Opc.Ua.Lens`](../tools/Opc.Ua.Lens).

There is one primary server connection. GDS tools can use a suitable primary
session or maintain their own secondary connection. Document tabs are working
contexts, not separate primary server sessions.

## Getting started

From the repository root:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet build tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0
dotnet run --project tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0 --no-build
```

The application also targets .NET 8 and .NET 9. Its assembly is `UaLens`, its
package is `OPCFoundation.NetStandard.Opc.Ua.Lens`, and its tool command is `ualens`.
Do not change these names when relocating or packaging it.

Use the connection bar to choose an endpoint and connect. The endpoint picker
shows the server's advertised security and identity policies. The primary state,
security policy, and identity remain visible while working.

The standard headless `--smoke` and protocol probes are development diagnostics
for a reference server's explicitly selected Anonymous/None endpoint. They do
not prove secure desktop behavior or authorize accepting an untrusted certificate.

## Workspace

The menu is stable: **File**, **View**, **Tools**, and **Help**. Add a document
through **Add Tool**, the Tools menu, or an applicable address-space action.
The searchable catalog groups tools into Explore / Connect, Observe, Administer,
and Diagnose. Advanced tool operations remain in their local settings or active
tool actions.

The empty workspace offers connection, workspace, discovery, and local-certificate
entry points; it is not a permanent Home or General tab. The address-space explorer
and contextual attributes/references inspector are resizable. Monitor, Write, Call,
Events, History, and Inspect are available according to the selected node.

An unavailable operation does not hide an entire document. Local certificate
management and discovery remain usable without a primary connection. Session-bound
documents retain their configuration while disconnected.

Fresh appearance preferences follow the operating system. Light and Dark can be
selected explicitly. Existing Light, DarkStandard, and DarkNavy preferences remain
explicit rather than being silently replaced by System. Charts use the same
semantic colors as dialogs and document surfaces.

Standard text-editing shortcuts retain their usual meaning. F2 renames a document;
Ctrl+Tab and Ctrl+Shift+Tab cycle documents. The command registry is the source of
truth for displayed shortcuts and their availability.

## Connection and trust

An untrusted server certificate is rejected unless the user makes an explicit
decision. The choices are Reject, Accept Once, and Trust Permanently.

Accept Once applies to the selected endpoint, certificate, and overridable
validation error. It is not global trust and does not authorize a GDS secondary
connection. Disconnecting or selecting a different target clears it. Permanent
trust must be written successfully to the certificate store before retrying.
Invalid or revoked certificates are not made acceptable by the untrusted-certificate
choice.

The validation callback never blocks on a window. Connection coordination rejects
and captures the validation failure, prompts asynchronously, and makes a bounded
retry using the decision. Cancellation does not apply a late dialog answer.

Changing engines or reconnecting preserves the selected security and identity
profile. Credentials are reacquired when necessary; a disposed session identity
is not reused. Changing the user replaces the session through the connection
owner, updating its profile and retained credentials together; document
configurations are rebound to the new session. Workspaces contain profile intent, not passwords, private keys, or
bearer tokens. The interactive picker supports Anonymous and UserName identities;
broader identity-provider workflows are an extension opportunity below.

## Monitoring

A monitor document starts with values, quality, and source timestamps. An optional
trend or timing visualization can be selected without changing server publishing.
Dots, Bars, Lines, Signal, Histogram, and Heatmap remain available.

The requested publishing interval and publishing-enabled control are immediately
available. The server's revised values are displayed separately. Item settings
control sampling, monitoring mode, queues, discard policy, and data-change filtering.
Source and server timestamps have different meanings; neither is replaced with
the time at which the UI happens to render.

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

Raw publish diagnostics use server subscription identifiers where the public
stack interface exposes an unambiguous identifier. Partitioned V2 callbacks can
instead carry a `client:` correlation identifier; this is deliberately not
presented as a server-assigned ID. Display-log buffering is bounded separately
from notification delivery.

## Existing tool coverage

| Tool | Workflows |
|---|---|
| Monitor | Values, events, quality/timestamps, charts, subscription/item settings, recursive node selection, export |
| Event View | Multiple sources, filters, selected fields, details, bounded event log, display pause/clear |
| Historian | Raw, processed, at-time and modified reads; cancellation, export, and explicitly requested updates/deletion |
| Subscription Bench | Variable pool, both live scaling sliders, aggregate rates/counts, shared defaults, shrinking and Stop cleanup |
| Performance | Explicit write/call workloads, rate/duration, run history, CSV, and the existing limited last-three comparison |
| File System | File/directory browsing, transfers, creation, rename, and deletion |
| Certificate Manager | Local application/trusted/issuer/rejected store management |
| GDS Discovery | Discovery endpoints and saved favorites |
| GDS Management / Push | Registered applications, issuance/CSR, trust lists, certificate update/apply |
| User / Role Management | Account/password restrictions and role/identity/application/endpoint mappings |

The bench remains slider-driven; there is no separate Run prerequisite. Shared
settings apply to existing resources and resources added later. Shrinking, Stop,
and document closure release the corresponding server resources.

Administration targets and prerequisites matter. History deletion, file deletion,
account/role changes, certificate application, and write/call workloads are not
automatically executed when a workspace loads. Certificate ApplyChanges can
intentionally terminate a connection.

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
imports remain profile-less when saved again.

Appearance and favorites retain their per-user UaLens locations. Writes use a
completed sibling temporary file before replacing the destination. Missing favorites
are an empty initial state; corrupt or inaccessible favorites are an error, not a
misleading successful empty list. Endpoint paths remain case-sensitive.

## Next stack-showcase capabilities

These are recommendations, not claims that UaLens already implements them. Stack
support, UaLens exposure, and the connected server's capabilities are separate.
The proposed additions should reuse the existing stack interfaces rather than
introduce another protocol implementation.

| Priority | Addition and value | Reusable stack surface | Prerequisites and safety | Acceptance scenario | Complexity |
|---|---|---|---|---|---|
| 1 | Condition-aware alarm workspace | `AlarmClient`, `AlarmEventFilterBuilder`, alarm streaming; [Alarms and Conditions](AlarmsAndConditions.md) | Alarm-capable server and operator authorization. Acknowledge/confirm changes operational state; require explicit actions. | Refresh active conditions, show branches and retained state, acknowledge/confirm one selected event and display its new state. | Medium |
| 1 | X.509 and issued-token connection workflows | `IClientIdentityProvider`, `X509ClientIdentityProvider`, issued-token providers; [Identity Providers](IdentityProviders.md) | Advertised token policy, certificate store or token authority. Keep keys/tokens in providers and never workspace JSON. | Establish the selected identity, reacquire credentials safely, and show policy/identity without exposing secret material. | Medium |
| 1 | Resilience and subscription continuity lab | `ManagedSession`, V2 `ISubscription.SetAsDurableAsync`, transfer/recovery policies; [High Availability](HighAvailability.md), [Durable Subscription](DurableSubscription.md), [Transfer](TransferSubscription.md) | Suitable server persistence, queue/lifetime limits and, for failover, a configured redundant set. Bound load and disclose gaps; durability is not an unconditional zero-loss promise. | Interrupt a test connection, show recovery/transfer/recreation distinctly, and correlate retained samples, sequence gaps and republish results. | High |
| 2 | Structured-value and model inspector | `ComplexTypeSystem`, NativeAOT-friendly schema adapters, `ISchemaProvider`, generated clients; [Complex Types](ComplexTypes.md), [Schema Generation](SchemaGeneration.md) | A server exposing type definitions and authorization for writes. Prefer schema-backed editing over Reflection.Emit. | Discover a custom structure, inspect fields/schema, edit a permitted value, and round-trip it through the stack. | Medium |
| 2 | PubSub inspection and controlled demonstrations | Fluent PubSub builders, transport/schema/diagnostic modules; [PubSub](PubSub.md) | Explicit transport/broker/interface/SKS configuration. Capture and publisher traffic are bounded and opt-in. | Inspect writer/reader metadata, receive a dataset, and correlate message/sequence/security diagnostics. | High |
| 2 | Reverse-connect and transport setup | `ReverseConnectManager`, registered transport bindings; [Reverse Connect](ReverseConnect.md), [Transports](Transports.md) | Configured peer URI/endpoint, listener/firewall permissions and transport prerequisites. Do not accept arbitrary reverse peers. | Receive a matching ReverseHello and establish a secure session without relaxing trust or identity selection. | Medium |
| 2 | Limits and correlated diagnostics | Telemetry/capture facilities, server operation limits, partitioned subscriptions; [Diagnostics](Diagnostics.md), [Subscriptions](Subscriptions.md) | Server diagnostics may need authorization; packet capture may require native dependencies/privileges. Redact sensitive trace data. | Explain a revised limit or throttled operation using bounded traces/counters, not merely a throughput number. | Medium |
| 3 | Hardware-backed credential selection | `ICryptoProviderRegistry`, `ICertificateStoreProvider`, optional PKCS#11 store; [Crypto Provider](CryptoProvider.md) | A supported device/provider and secure PIN/key access. No private-key export and no unsubstantiated FIPS claim. | Authenticate with a device-held key and demonstrate that renewal/reconnect preserves provider ownership. | High |
| 3 | Guided companion-model tasks | Existing DI, Robotics, Vision, AI, OpenUSD and WoT client/provider modules | Matching sample servers and domain assets; graphics/ML may need substantial resources. Label draft extensions explicitly. | Discover a supported model and run a small, clearly scoped task using its generated/provider interface. | High |

Robotics OPC 40010 and its draft Robot Intent extension are not the same maturity
claim. The Vision, AI Model Management, and OpenUSD companions documented here
include draft models. WoT Connectivity and individual draft bindings likewise
need their own status labels rather than one blanket conformance claim.
