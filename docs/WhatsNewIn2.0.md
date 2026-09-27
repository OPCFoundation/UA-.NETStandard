# What's New in OPC UA .NET Standard 2.0

Version 2.0 expands the SDK beyond the 1.5.378 client/server APIs with managed
connections, model-driven application development, injectable services, and
distributed deployment options. This overview highlights what those changes
enable; the linked guides contain API contracts, examples, and limitations.

## Contents

- [At a glance](#at-a-glance)
- [Feature details](#feature-details)
  - [Dependency injection and hosting](#dependency-injection-and-hosting)
  - [Client](#client)
  - [Source generators and modeling](#source-generators-and-modeling)
  - [Type system and immutability](#type-system-and-immutability)
  - [Async, cancellation, and `TimeProvider`](#async-cancellation-and-timeprovider)
  - [Native AOT](#native-aot)
  - [OPC UA services](#opc-ua-services)
  - [OPC UA companion-spec coverage](#opc-ua-companion-spec-coverage)
  - [Security and certificates](#security-and-certificates)
  - [Global Discovery Server](#global-discovery-server)
  - [Part 14 PubSub modernization](#part-14-pubsub-modernization)
  - [High availability and redundancy](#high-availability-and-redundancy)
  - [Performance, memory, and pooling](#performance-memory-and-pooling)
  - [Tooling and diagnostics](#tooling-and-diagnostics)
- [Further reading](#further-reading)

## At a glance

| Area | What 2.0 enables |
| --- | --- |
| Application development | Host clients and servers through one dependency-injection builder; generate model types and typed client proxies instead of writing service plumbing. |
| Client connections | Use `ManagedSession` for reconnection and failover, with callback-based or asynchronous streaming subscriptions. |
| Data and deployment | Use strongly typed values, arrays, and matrices; publish supported applications with Native AOT. |
| Server capabilities | Add historian providers, alarms, state machines, file access, and companion models through reusable services. |
| Security and discovery | Manage certificate lifecycles and identities through providers, and integrate GDS pull/push management and token services. |
| PubSub and availability | Compose publishers/subscribers with transport packages, or opt into redundancy and distributed state for client/server deployments. |

## Feature details

### Dependency injection and hosting

`services.AddOpcUa()` provides a common entry point for clients, servers, and
feature libraries. The .NET Generic Host manages server lifetime; options,
telemetry, identities, certificates, and application providers use the same
container. Direct construction remains available.
See [Dependency Injection](DependencyInjection.md).

### Client

`ManagedSession` manages connection recovery behind a fluent builder while the
classic `Session` remains available for applications that own that lifecycle.
The V2 subscription API adds options-based callbacks, triggering relationships,
automatic partitioning, and `IAsyncEnumerable` streams for short-lived monitoring.
Typed helpers cover history, alarms, files, and model-change tracking.
See [Sessions](Sessions.md), [Subscriptions](Subscriptions.md),
[File System Client](FileSystemClient.md), and [Model Change Tracking](ModelChangeTracking.md).

### Source generators and modeling

ModelDesign and NodeSet2 inputs generate node-manager scaffolding, state types,
and typed ObjectType client proxies. POCO annotations generate `IEncodeable`
implementations, and model dependencies can be registered across assemblies.
Models that change independently of the binary can instead be loaded at runtime.
See [Node Managers](NodeManagers.md), [Source-Generated Data Types](SourceGeneratedDataTypes.md),
[Model Dependencies](ModelDependencies.md), and [Runtime NodeSets](RuntimeNodeSets.md).

### Type system and immutability

Readonly value types and the `Variant`-based API reduce boxing and make OPC UA
values explicit. `ByteString`, `ArrayOf<T>`, and `MatrixOf<T>` represent binary,
array, and matrix data. Runtime complex-type loading supports server-defined
structures and enumerations without requiring a generated CLR class for each type.
See [Complex Types](ComplexTypes.md) and [Schema Generation](SchemaGeneration.md).

### Async, cancellation, and `TimeProvider`

`AsyncCustomNodeManager` supports asynchronous service and data-source operations.
Request cancellation follows the request lifetime, while `TimeProvider` enables
controlled clocks and timers for testing. Existing synchronous compatibility APIs
do not make blocking waits appropriate in new asynchronous handlers.
See [Async Server Support](AsyncServerSupport.md) and [Node Managers](NodeManagers.md).

### Native AOT

Source-generated models, runtime type representations, and revised serialization
paths support applications published without a JIT compiler. Choose AOT-compatible
providers and the default complex-type builder; the optional Reflection.Emit
builder still requires runtime code generation.
See [Native AOT](NativeAoT.md) and [Complex Types](ComplexTypes.md#type-builders).

### OPC UA services

Reusable client and server components cover these application tasks:

| Task | Guide |
| --- | --- |
| Raise, acknowledge, and stream alarms | [Alarms and Conditions](AlarmsAndConditions.md) |
| Store and query history, including processed values | [Historical Access](HistoricalAccess.md) and [Aggregates](Aggregates.md) |
| Define and observe state transitions | [State Machines](StateMachines.md) |
| Discover aliases and manage address-space nodes | [Alias Names](AliasNames.md) and [Node Management](NodeManagement.md) |
| Transfer files and administer roles | [File System Client](FileSystemClient.md) and [Role-Based User Management](RoleBasedUserManagement.md) |

The guides describe provider requirements and supported operations; this overview
is not a claim of complete implementation or certification for every specification.

### OPC UA companion-spec coverage

The SDK adds model libraries, hosting support, and client helpers for industrial
domains and asset connectivity:

| Domain | Guide |
| --- | --- |
| Device composition and software updates | [Device Integration](DeviceIntegration.md) and [Software Update](SoftwareUpdate.md) |
| Motion-device systems and controllers | [Robotics](Robotics.md) |
| Relative and geographic positioning | [Positioning](Positioning.md) |
| Perception, media, and feedback | [Vision](Vision.md) |
| Model catalogues, deployments, and inference | [AI Model Management](AI.md) |
| Web of Things connectivity and conversion | [WoT Connectivity](WoTConnectivity.md), [WoT / NodeSet Conversion](WoTNodeSetConversion.md), and [WoT Bindings](WotBindings.md) |

Vision, AI Model Management, and Robot Intent use draft models. Their guides
identify provisional contracts and distinguish model coverage from runtime behavior.

### Security and certificates

Reference-counted certificates and `CertificateManager` provide explicit ownership,
trust-list management, rotation, and lifecycle notifications. Identity and crypto
providers support application-specific authentication and hardware-held keys.
ECC SecureChannel and user-token policies require .NET 8-or-later stack assets;
curve and cipher availability also depends on the platform.
See [Certificate Manager](CertificateManager.md), [Identity Providers](IdentityProviders.md),
[Crypto Providers](CryptoProvider.md), and [ECC Profiles](EccProfiles.md#known-limitations).

### Global Discovery Server

GDS support extends application registration, pull/push certificate management,
custom certificate groups, token issuance, and credential services. A Local
Discovery Server is also available as a hosted component or standalone sample.
See [GDS](GDS.md) for capability boundaries and conformance evidence,
[Authorization Service](AuthorizationService.md), and [Key Credential Service](KeyCredentialService.md).

### Part 14 PubSub modernization

A fluent builder and dependency-injection extensions compose PubSub applications
from separate runtime, transport, and server-integration packages. UADP and JSON
encoding, discovery, retained metadata, runtime configuration, diagnostics, and
Security Key Service integration support publisher/subscriber workflows.
Transport and cryptographic capabilities remain platform-dependent; in particular,
the DTLS Curve25519 / Curve448 profiles are not registered.
See [PubSub](PubSub.md) for supported profiles, transports, and examples.

### High availability and redundancy

Server redundancy metadata and `ManagedSession` failover support OPC UA redundancy
workflows. Optional distributed stores add address-space, session, and subscription
mirroring with eventual or strong consistency choices. Kubernetes integration
provides deployment, discovery, and readiness support.
See [High Availability](HighAvailability.md), [Replica-Consistent NodeIds](ReplicaNodeIdentity.md),
and [Kubernetes](Kubernetes.md) for configuration and deployment constraints.

### Performance, memory, and pooling

Value-type representations and pooled buffers reduce allocations in encoding and
notification paths. Server work also improves resource lifetime and repeated
permission checks. These changes do not imply that every workload is faster;
use the measured results and sizing guidance for your deployment.
See [Benchmarks](Benchmarks.md), [Server Scalability](ServerScalability.md),
and [Rate Limiting](RateLimiting.md).

### Tooling and diagnostics

The MCP server exposes client operations and packet-capture tools to agents.
`ITelemetryContext` supplies application logging, metrics, and tracing, with
audit and redaction support. A migration analyzer and code fixer assist with
mechanical API updates.
See [MCP Server](McpServer.md) and [Diagnostics](Diagnostics.md).

## Further reading

- [Migration Guide](MigrationGuide.md) — breaking changes, per-API replacements,
  and the migration analyzer workflow for upgrading from 1.5.378.
- [Documentation index](README.md) — task-based routes into the SDK guides.
- [Sample applications](Samples.md) — runnable client, server, PubSub, and
  companion-model examples.
- [Profiles and facets](Profiles.md) — capability and platform overview.
