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
  - [Performance, memory, and pooling](#performance-memory-and-pooling)
  - [Async, cancellation, and `TimeProvider`](#async-cancellation-and-timeprovider)
  - [Native AOT](#native-aot)
  - [OPC UA companion-spec coverage](#opc-ua-companion-spec-coverage)
  - [Security and certificates](#security-and-certificates)
  - [Global Discovery Server](#global-discovery-server)
  - [Part 14 PubSub modernization](#part-14-pubsub-modernization)
  - [High availability and redundancy](#high-availability-and-redundancy)
  - [Tooling and diagnostics](#tooling-and-diagnostics)
- [Further reading](#further-reading)

## At a glance

| Area | What 2.0 enables |
| --- | --- |
| Application development | Host clients and servers through one dependency-injection builder; generate model types and typed client proxies instead of writing service plumbing. |
| Client connections | Use `ManagedSession` for reconnection and failover, with callback-based or asynchronous streaming subscriptions. |
| Data and deployment | Use strongly typed values, arrays, and matrices; publish supported applications with Native AOT. |
| Server capabilities | Add historian providers, alarms, state machines, file access, and the models in the [specification catalogue](#opc-ua-companion-spec-coverage) through reusable services. |
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

### Performance, memory, and pooling

Value-type representations and pooled buffers reduce allocations in encoding and
notification paths. Server work also improves resource lifetime and repeated
permission checks. These changes do not imply that every workload is faster;
use the measured results and sizing guidance for your deployment.
See [Benchmarks](Benchmarks.md), [Server Scalability](ServerScalability.md),
and [Rate Limiting](RateLimiting.md).

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
See [Native AOT: publish an application](NativeAoT.md#publish-an-application)
and [Complex Types](ComplexTypes.md#type-builders).

### OPC UA companion-spec coverage

The SDK adds model libraries, hosting support, and client helpers for the following
services and domains. Core OPC UA specification parts are listed separately from
domain-specific companions and draft extensions.

**Core specification services and models**

| Capability | Specification | Guide |
| --- | --- | --- |
| Alarms and conditions | OPC 10000-9 | [Alarms and Conditions](AlarmsAndConditions.md) |
| Historical access and aggregates | OPC 10000-11 and OPC 10000-13 | [Historical Access](HistoricalAccess.md) and [Aggregates](Aggregates.md) |
| State machines | OPC 10000-16 | [State Machines](StateMachines.md) |
| Alias names | OPC 10000-17 | [Alias Names](AliasNames.md) |
| Node and reference management | OPC 10000-4 | [Node Management](NodeManagement.md) |
| File transfer and file systems | OPC 10000-5 Annex C and OPC 10000-20 | [File System Client](FileSystemClient.md) |
| Roles and user management | OPC 10000-18 | [Role-Based User Management](RoleBasedUserManagement.md) |
| Global discovery and certificate management (GDS) | OPC 10000-12 | [Global Discovery Server](GDS.md) |
| Local discovery (LDS and LDS-ME) | OPC 10000-12 | [LDS / LDS-ME](GDS.md#lds--lds-me-45) and the [Console LDS Server](../samples/Lds/ConsoleLdsServer) sample |
| Device onboarding: registrar ticket administration | OPC 10000-21 | [OnboardingClient](GDS.md#opc-10000-21-onboardingclient) and the [onboarding sample](../samples/Gds/README.md) |

**Companion and extension models**

| Domain | Specification or status | Guide |
| --- | --- | --- |
| Device composition and software updates | OPC 10000-100 (Devices) | [Device Integration](DeviceIntegration.md) and [Software Update](SoftwareUpdate.md) |
| Machines, job management, process values, energy and result transfer | OPC 40001-1/-2/-3/-4/-101 (Machinery), with OPC 34100 (Energy Consumption Management) and OPC 30081 (PA-DIM) | [Machinery](Machinery.md) |
| Pumps, compressors and vacuum pumps | OPC 40223 (Pumps and Vacuum Pumps) | [Pumps](Pumps.md) |
| Scales and scale systems | OPC 40200 (Weighing Technology), with OPC 30050 (PackML) | [Scales](Scales.md) |
| Motion-device systems and controllers | OPC 40010-1 (Robotics), over OPC 10000-200 (Industrial Automation, its own `Opc.Ua.IA` package) | [Robotics](Robotics.md) |
| Task-level robot commands | Robot Intent (draft) | [Robot Intent](Robotics.md#robot-intent) |
| Manufacturing resources and job control | OPC 10030 (ISA-95 Common Model) and OPC 10031-4 (Job Control V1/V2) | [ISA-95](ISA95.md) |
| Relative and geographic positioning | OPC 10000-210 (RSL) and OPC 10000-211 (GPOS) | [Positioning](Positioning.md) |
| Perception, media, and feedback | Vision (draft) | [Vision](Vision.md) |
| Model catalogues, deployments, and inference | AI Model Management and Inference (draft) | [AI Model Management](AI.md) |
| Resource registration, versioning, and federation | xRegistry abstract base model (draft) | [xRegistry](XRegistry.md) |
| Scene bindings and address-space materialization | OpenUSD Bindings and OpenUSD Scene (both draft) | [OpenUSD](OpenUsd.md) |
| Web of Things connectivity and conversion | OPC 10100-1, with preview registry extensions | [WoT Connectivity](WoTConnectivity.md), [WoT / NodeSet Conversion](WoTNodeSetConversion.md), and [WoT Bindings](WotBindings.md) |

The draft [Generators model](../samples/OpenUsd/GeneratorServer/Generators.md) in the
generator server is a sample-local model, not a standalone SDK library.

Draft entries are identified by model name rather than an unverified specification
number; their namespace URIs and NodeIds remain provisional. The guides describe
provider requirements and supported operations. Model coverage does not imply
complete runtime implementation or certification for every specification.

### Security and certificates

Reference-counted certificates and `CertificateManager` provide explicit ownership,
trust-list management, rotation, and lifecycle notifications. Identity providers
support application-specific authentication. Pluggable crypto providers let an
application replace platform cryptography with another library, a remote service,
or hardware such as a TPM, HSM, or PKCS#11 token. Providers can be selected by
purpose and security policy, including support for hardware-held private keys.
ECC SecureChannel and user-token policies require .NET 8-or-later stack assets;
curve and cipher availability also depends on the platform.
See [Certificate Manager](CertificateManager.md), [Identity Providers](IdentityProviders.md),
[Crypto Providers](CryptoProvider.md), and [ECC Profiles](EccProfiles.md#known-limitations).

### Global Discovery Server

GDS support extends application registration, pull/push certificate management,
custom certificate groups, token issuance, and credential services. A Local
Discovery Server is also available as a hosted component or standalone sample.
For OPC 10000-21 device onboarding, clients register and unregister tickets with
a device registrar through `OnboardingClient`, and the registrar keeps them in a
pluggable `ITicketStore`.
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
