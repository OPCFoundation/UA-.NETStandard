# OPC UA .NET Standard stack documentation

These guides lead from a first working application to advanced and experimental
capabilities. Follow the numbered stages when you are learning. Choose only the
paths that match your application, or use the contents list to find a feature.

- **New to the SDK?** Start with [Getting started](GettingStarted.md), read
  [OPC UA concepts](Concepts.md), then build
  [your first information model](FirstModel.md).
- **Upgrading from 1.5.x?** Read [What's New in 2.0](WhatsNewIn2.0.md), then
  follow the [Migration Guide](MigrationGuide.md).
- **Contributing to this repository?** See
  [Contributing and maintaining the SDK](#contributing-and-maintaining-the-sdk).

## Contents

- [How to use this index](#how-to-use-this-index)
- [1. Get started](#1-get-started)
- [2. Build your application](#2-build-your-application)
  - [Client path](#client-path)
  - [Server path](#server-path)
  - [PubSub path](#pubsub-path)
- [3. Add data types and application services](#3-add-data-types-and-application-services)
  - [Data types and models](#data-types-and-models)
  - [Application services](#application-services)
- [4. Use companion models](#4-use-companion-models)
- [5. Prepare for production](#5-prepare-for-production)
- [6. Advanced integration, reliability, and scale](#6-advanced-integration-reliability-and-scale)
  - [Transports and integration](#transports-and-integration)
  - [Session recovery and performance](#session-recovery-and-performance)
  - [Security infrastructure and fleet management](#security-infrastructure-and-fleet-management)
  - [High availability and deployment](#high-availability-and-deployment)
  - [Native AOT publishing](#native-aot-publishing)
- [7. Draft and experimental capabilities](#7-draft-and-experimental-capabilities)
- [Reference and upgrade](#reference-and-upgrade)
- [Contributing and maintaining the SDK](#contributing-and-maintaining-the-sdk)

## How to use this index

Each stage builds on the earlier ones, but you do not need to read every guide.
After stage 1, choose the client, server, or PubSub path. Stages 3 to 7 are
optional; use the guides your application needs. A stage or group names the
knowledge it assumes.

The following reference list provides direct links to individual topics:
* [OPC UA Profiles and Facets](Profiles.md) - Overview of supported OPC UA profiles, facets, security policies, and transport protocols.
* [Transport Profiles](Transports.md) - Developer guide for the wire transports: `opc.tcp` / HTTPS (binary + JSON + REST) / WSS (binary + JSON), including server hosting and client connect examples.
* [REST Binding (OpenAPI Mapping)](WebApi.md) - OPC UA Part 6 §G.3 OpenAPI mapping: ASP.NET Core MVC controllers for every spec service, Compact / Verbose encoding negotiation, four pluggable auth modes (Anonymous / Bearer JWT / HTTP Basic / Mutual TLS), and the symmetric `IWebApiClient`.
* [What's New in 2.0](WhatsNewIn2.0.md) - Developer-facing tour of the 1.5.378 → 2.0 changes, grouped by theme and layer, with links to deeper feature docs.
* [Migration Guide](MigrationGuide.md) - How to migrate from a previous version.
* [Sessions, Reconnection, and Subscription Engines](Sessions.md) - Architectural overview of `Session`, `ManagedSession`, `SessionReconnectHandler`, and the classic / V2 subscription engines, including guidance on which to use.
* About [.NET platform support, NuGet packages and versioning](DeveloperGuide.md#packages-platform-support-and-versioning).
* About [continuous integration](DeveloperGuide.md#continuous-integration) — which pipeline runs what, how to start a validation build on a pull request with `/azp run`, and the coverage gates a change has to satisfy.
* How X.509 [Certificates](Certificates.md) are used in the certificate stores.
* [CertificateManager](CertificateManager.md) - Centralized certificate lifecycle management, server-side push certificate rotation, and the OPC UA Part 12 PushManagement transaction model (`ApplyChanges`/`CancelChanges`, staged TrustList/Certificate updates, `DeleteCertificate`, pending-key persistence).
* [Crypto provider](CryptoProvider.md) - Replacing the stack's cryptography with another library, a remote service or hardware (TPM, HSM, PKCS#11, cloud key service), keeping private keys inside the device, selecting providers per purpose and security policy, and auditing the use of uncertified cryptography.
* Using the [Reverse Connect](ReverseConnect.md) for the UA-TCP and WSS transports.
* Support for the [TransferSubscriptions](TransferSubscription.md) service set.
* [Diagnostics](Diagnostics.md) — logging, telemetry, server audit events, server diagnostics nodes, and packet capture.
* [Performance Benchmarks](Benchmarks.md) — BenchmarkDotNet methodology, the 2.0 (`master`) vs 1.5.378 (`master378`) comparison, root-cause analysis of the encoder/decoder/session regressions and their real-world impact, the subscription-notification (pooled encodeable) micro-benchmarks, server session scalability (the 500-session capability, sizing and bottlenecks), and planned future work.
* [Server Session Scalability](ServerScalability.md) — why a single node tops out at ~2000 concurrent sessions, the establishment vs steady-state boundaries (socket backlog, the `BadTcpInternalError` retry-storm amplifier, the O(N²) diagnostics rescan, CreateSession crypto-under-lock, the RSA CPU wall, and the held-Publish worker-accounting coupling) with code references, and a prioritized admission-control / rate-limiting roadmap for moving beyond it.
* [Rate Limiting and Admission Control](RateLimiting.md) — the server's deterministic, configurable connection- and session-establishment limiters (on by default, `System.Threading.RateLimiting`-based, DI-pluggable), shared incomplete-message capacity and sessionless headroom, the `BadServerTooBusy` signalling, the diagnostics-independent server retry-after carriers (`ResponseHeader.additionalHeader`, HTTP `Retry-After`, UA-TCP ERR, and load-based `Server.ServiceLevel`), and the client's server-signal-aware adaptive reconnect backoff (`IReconnectPolicy.TryGetNextDelay`).
* Support for [WellKnownRoles & RoleBasedUserManagement](RoleBasedUserManagement.md).
* Pluggable [Identity Providers](IdentityProviders.md) — interfaces (`IClientIdentityProvider`, `IUserTokenAuthenticator`, `IAccessTokenProvider`, `ITokenIssuer`, `IIdentityClaims`) plus the OPC 10000-6 §6.5.2.2 `IssuerEndpointUrl` JSON parser for OAuth2 / OIDC / Entra / JWT flows.
* Support for [ECC Certificates](EccProfiles.md).
* Working with [ComplexTypes](ComplexTypes.md) - Custom structures and enumerations.
* Client-based [NodeSet Export](NodeSetExport.md) - Export server address space to NodeSet2 XML.
* Source generated [DataTypes] - How to annotate POCO classes and let the source generator generate the `IEncodeable` implementation.
* Runtime [Schema Generation](SchemaGeneration.md) - Produce XSD, OPC Binary (BSD) and JSON Schema (Part 6 Annex C, compact + verbose) for generated encodeable types and dynamically added complex types via the injectable `ISchemaProvider`; schemas are built as object models in code (trimmable, NativeAOT compatible).
* [NodeManagers](NodeManagers.md) - Overview of the server node-manager architecture, built-in managers (master, core, diagnostics/configuration), CoreNodeManager vs CustomNodeManager2 guidance, registration and runtime lifecycle rules, namespace metadata and historical-access reconciliation, and source-generated `AsyncCustomNodeManager` authoring with the fluent `INodeManagerBuilder` API. Includes NativeAOT single-file server guidance and samples such as [MinimalBoilerServer](../samples/MinimalApi/MinimalBoilerServer) and [PumpDeviceIntegrationServer](../samples/DI/PumpDeviceIntegrationServer).
* Runtime [NodeSets](RuntimeNodeSets.md) - Load one or more NodeSet2 XML documents into the server address space at startup without source generation. Covers file and stream sources, dependency ordering, parent-child browse-path resolution, fluent `Configure` callbacks, default namespace inference, and the default complex-type loading path. Use when the XML content changes independently of the server binary or for rapid prototyping.
* [WoT / NodeSet conversion](WoTNodeSetConversion.md) - `WotNodeSetConverter` readable mapping, preservation projections, and the WoT-to-NodeSet default/failure table.
* [WoT Connectivity](WoTConnectivity.md) - OPC 10100-1 asset connectivity, the WoT Connectivity 1.1 registry/client, dependency-closure materialization, and runtime NodeSet projection.
* [WoT protocol bindings](WotBindings.md) - The bindings that ship today (planner/executor architecture, bundled and separate packages, operation coverage, target mapping, lazy channels, generation lifetime) and the contributor guide for adding your own, with a complete memory binding, registration, diagnostics, tests, packaging, TFM, trimming, and NativeAOT guidance.
* [WoT aggregation sample](../samples/WotCon/README.md) - Two flat OPC UA sources aggregated into a runtime-loaded DI/Machinery/Pumps Pump model, including commands, Refresh, monitoring, replacement, troubleshooting, and NativeAOT publishing.
* [Device Integration (DI) developer guide](DeviceIntegration.md) - End-to-end documentation for the `Opc.Ua.Di*` library trio: fluent `IDeviceBuilder`, device sub-type extensions (`AddSoftware`, `AddBlock`, `AddConfigurableObject`, `AddLifetimeIndication`, `WithSupportInfo`), hosting integration (`AddOpcUaDi` / `ConfigureDevicesFor`), lock service, software-update package store, and client helpers (`DiLockClient`, `DiTopologyClient`, `SoftwareUpdateClient`). Includes a section enumerating supported OPC 10000-100 features against the spec.
* [OpenUSD](OpenUsd.md) — bridge an OPC UA address space to an OpenUSD stage, in two parts. **Part 1 — bindings**: the generic domain-agnostic `OpenUsdConnector` (discovers `Server/OpenUSD/Representations`, subscribes, composes, verifies stage/asset digests, replays history), the `Variant`-based `IUsdSink` with `UsdFileSink` / `MockUsdSink`, the fluent/DI `AddOpenUsdConnector` extensions + `OpenUsdConnectorFactory` / `OpenUsdConnectorOptions`, server-side `UsdAssetDelivery`, and the optional `--view` viewport. **Part 2 — scene materialization**: materializes a composed USD stage *inside* the address space so the prim tree is the node hierarchy — the `Opc.Ua.OpenUsd.Scene` companion and scene-document APIs, `Opc.Ua.OpenUsd.Scene.Conversion` `.usda` reader/writer and §6.2 `UsdValueTypeMap` (USD roles as DataTypes subtyping their built-in), and `Opc.Ua.OpenUsd.Server.Scene` materialization/export APIs with unknown-type fallbacks, Mode-A live attributes, portable Cesium georeference dual-authoring, discovery and Part 1 binding-target resolution. Both parts ship in the `Opc.Ua.OpenUsd` and `Opc.Ua.OpenUsd.Server` packages, and both companion models are currently drafts.
* [Robotics developer guide](Robotics.md) — the `Opc.Ua.Robotics` / `Opc.Ua.Robotics.Server` / `Opc.Ua.Robotics.Client` trio for OPC 40010 Robotics 1.02 over OPC 40001-1 IA and OPC 10000-100 DI: source-generated models, `AddRobotics` / `AddRoboticsModel` / `ConfigureRobotics(For)` hosting, `IRoboticsModelProvider` / `IRoboticsConfigurator` / `IRoboticsBuildContext`, validated fluent topology builders (`AddMotionDeviceSystemAsync` down to axes, power trains, motors, gears, drives, safety states, and task controls), semantic references, `ArrayOf<T>` snapshot contracts, and `RoboticsClient` discovery. It also covers [Robot Intent](Robotics.md#robot-intent), the draft task-level motion verbs OPC 40010 leaves undefined, including the MCP surface for LLM agents.
* [Vision developer guide](Vision.md) — the `Opc.Ua.Vision` / `Opc.Ua.Vision.Server` / `Opc.Ua.Vision.Client` / `Opc.Ua.Vision.OpenUsd` package family for the draft *OPC UA — Vision* companion specification: source-generated Vision model, `AddVision` / `ConfigureVision` hosting with `IVisionMediaProvider` / `IVisionInferenceProvider` / `IVisionFeedbackSink`, the two perception paths behind one contract (`OnServer` deterministic detector vs `EdgeOffServer` agent submissions), fluent topology builders for frames, sensors, calibrations, media endpoints and inference pipelines, `VisionClient` discovery + `VisionFrameGraph` §5.12 pose composition + `VisionResultReader` streaming detections + `VisionFeedbackClient` for off-server VLM agents, the §6.4 media-gating states, the `NoRenderingBackend` degrade path, facet derivation, and the composed [`vision` MCP profile](McpServer.md) with the [BinPickingCell / BinPickingClient](../samples/Robotics/BinPickingCell) example.
* [AI Model Management developer guide](AI.md) — the `Opc.Ua.AI` / `Opc.Ua.AI.Inference` / `Opc.Ua.AI.Server` / `Opc.Ua.AI.Client` package family for the draft *OPC UA — AI Model Management and Inference* companion specification over xRegistry: source-generated catalogues, datasets, deployments, inference endpoints and learning jobs, the `IInferenceBackend` contract with `Microsoft.Extensions.AI` `IChatClient` and OpenAI-compatible REST backends, `AINodeManagerFactory` hosting via `AddNodeManager<AINodeManagerFactory>`, `Invoke` routing, standard file-transfer artefact streaming, credential resolvers that keep secret material out of the address space, and the [ModelManagementServer / ModelManagementClient](../samples/AI/README.md) example.
* [Relative Spatial Location and Global Positioning](Positioning.md) — source-generated OPC 10000-210 RSL and OPC 10000-211 GPOS models, standalone/composed server hosting, provider contracts, high-level clients, frame-chain resolution, WGS84/ENU conversion, and ground-control-point fitting.
* [Generators (generating sets)](../samples/OpenUsd/GeneratorServer/Generators.md) — the draft Generators companion specification realised end to end by [GeneratorServer](../samples/OpenUsd/GeneratorServer): a datasheet-driven simulation in which load fraction is the only independent variable, DI + Machinery integration, and one independent OpenUSD twin per configured set. Includes [SiteCompositionServer](../samples/OpenUsd/SiteCompositionServer), a supervisory server that owns no devices and composes the pump and generator servers into a single scene through cross-server components.
* [ISA-95 developer guide](ISA95.md) - End-to-end documentation for the `Opc.Ua.ISA95*` library trio: the OPC-10030 Common Model and OPC-10031-4 Job Control V1/V2, the two transparently-documented normative NodeSet repairs, `AddIsa95Server`/`AddIsa95Client` hosting, the typed common-model builder, the shared Job Control state engine and its `Uncertain`/Annex-B `ReturnStatus` result model, the provider-backed `GeoSpatialLocationType` seam and planned Part 210/211 RSL/GPOS integration, and a conformance matrix distinguishing static NodeSet structure from runtime-tested behavior.
* [Alias Names](AliasNames.md) - Full server + client support for the OPC UA Part 17 alias-name model (`AliasNameType`, `AliasNameCategoryType`, `FindAlias`, `FindAliasVerbose`, `AddAliasesToCategory`, `DeleteAliasesFromCategory`, `LastChange`).
* [Alarms and Conditions](AlarmsAndConditions.md) - Full server + client support for OPC UA Part 9. Server-side state types for latched/silenced/out-of-service alarms, alarm groups and suppression engine, alarm rate metrics. Client-side `AlarmClient`, typed alarm event records, fluent `AlarmEventFilterBuilder`, `IAsyncEnumerable` alarm streaming via `AlarmStreamExtensions`.
* [Historical Access (Part 11)](HistoricalAccess.md) - Server provider model (`IHistorianProvider` family) and `InMemoryHistorianProvider`, plus the client `HistoryClient` (`session.Historian()`) for raw/modified/at-time/processed/event reads, modification audit metadata, annotations, and data/event/structured updates.
* [Aggregates (Part 13)](Aggregates.md) - All 37 standard Part 13 v1.05.07 aggregate functions over historical data: server `AggregateManager` / calculators, native push-down vs framework fallback, `AnnotationCount` via the annotation provider, `AggregateConfiguration` defaults, and the client `ReadProcessedAsync` helper.
* [Subscriptions and Monitored Items Service Set](Subscriptions.md) - V2 subscription engine API. Covers `ISubscriptionManager` for long-lived callback-based subscriptions, the declarative+imperative `SetTriggering` API with N:M support and automatic replay on recreate/reconnect, and `IStreamingSubscription` (`IAsyncEnumerable`-based) for state-machine waits and short-lived monitoring (`ManagedSession.DefaultStreaming`, `TakeUntilAsync` / `WithTimeoutAsync` helpers).
* [Unbounded Monitored Items](Subscriptions.md#unbounded-monitored-items) - V2 logical-subscription wrapper that transparently splits monitored items across multiple server-side partitions when the per-subscription cap is exceeded (`IPartitionedSubscription`, `MonitoredItemOptions.Affinity`, reactive `Bad_TooManyMonitoredItems` fallback, secondary-partition idle-delete).
* [State Machines](StateMachines.md) - Generic, extensible Part 16 state-machine API. Client side: streaming + read helpers on the source-generated `*TypeClient` proxies (`GetCurrentFiniteStateAsync`, `ObserveFiniteTransitionsAsync`, `WaitForStateAsync`). Server side: unified fluent `StateMachineBuilder` with two complementary modes — *definition* (`Create(...)` + `AddState` / `AddTransition` / `OnCause` for ad-hoc machines via `FluentFiniteStateMachineState`) and *lifecycle* (`For(...)` / `INodeBuilder.AsStateMachine()` + `OnEnterState` / `WithCause` / `WithTimedTransition` to attach behavior to stack-shipped or generator-emitted FSMs). Vendor state machines inherit both ends of the API automatically.
* [Model Change Tracking](ModelChangeTracking.md) - Client-side address-space change tracking with per-node `INodeCache` invalidation; server-side `ModelChangeAggregator` and auto-emitted `GeneralModelChangeEvent` from `CustomNodeManager.CreateNode/DeleteNode`.
* [NodeManagement Service Set](NodeManagement.md) - Server-side AddNodes / DeleteNodes / AddReferences / DeleteReferences, including the `INodeManagementAsyncNodeManager` opt-in pattern and per-NodeManager `AllowNodeManagement` gate.
* [High Availability and OPC UA Redundancy](HighAvailability.md) - OPC 10000-4 §6.6 mapping for server, client, and network redundancy; `RedundancySupport`, `ServiceLevel`, manual failover, transparent/non-transparent `ManagedSession` modes, HotAndMirrored/Transparent state mirroring, distributed address-space/session/subscription stores with CRDT (eventual) or Raft (strong) consistency, shared certificate/trust-list stores, snapshot+delta hydration, and the optional GetEndpoints load-direction seam.
  * [Replica-consistent NodeIds](ReplicaNodeIdentity.md) - Required fixed shared namespace layout, guarded factory assignment, protected store/peer contracts, writer-created identities, and unchanged client NodeIds across failover.
  * [Kubernetes High Availability Deployment](Kubernetes.md) - Consolidated Kubernetes guide for the `Opc.Ua.Redundancy.Kubernetes` package: Lease leader election, EndpointSlice peer discovery, ServiceLevel-driven readiness, StatefulSet/Deployment and Service manifests, RBAC, probes, time sync, secrets, and GDS/NTRS registration.
  * [Redundant Sample Integration Tests](RedundancySampleTests.md) - Process-level integration tests that launch the `RedundantServer` / `RedundantClient` / `RedundantPubSub` sample apps and assert on their failover, reconnect, and data-loss-visibility behavior; short-haul variants run on every PR, long-haul soak variants run via dedicated manual/scheduled GitHub Actions and Azure DevOps jobs (`SAMPLE_HA_DURATION_MINUTES`).
* [Dependency Injection](DependencyInjection.md) - The unified `services.AddOpcUa()` / `IOpcUaBuilder` surface for hosting OPC UA components in `Microsoft.Extensions.DependencyInjection` / the .NET Generic Host (servers as `IHostedService`, options via `Action<T>` or `IConfiguration`, AOT-friendly).
* [AuthorizationService](AuthorizationService.md) - Modern Part 12 `StartRequestToken` / `FinishRequestToken`, `ITokenIssuer`, and GDS token issuance.
* [Fuzz testing](../fuzzing/Fuzzing.md) - SharpFuzz + afl-fuzz + libFuzzer integration. Three areas: `Encoders` (Binary/JSON/XML decoders, built-in type readers, parser entry points), `Certificates` (`X509CRL`, X509 extension parsers, `PEMReader`, `Pkcs10CertificationRequest`, ASN.1 helpers), and `Network` (UA-SC framing via `Opc.Ua.Core.Diagnostics` + internal `TcpMessageParsers` seam on `Opc.Ua.Core`). The [`fuzz-tester`](../.github/agents/fuzz-tester.agent.md) custom agent drives the whole toolchain autonomously: it detects OS-available engines, runs them in parallel, fixes novel findings per repo guidelines, adds the failing input as a regression asset, and pushes one commit per fix until the user says stop.
* [KeyCredentialService](KeyCredentialService.md) - Pull, Push, and experimental bridge guidance for Part 12 KeyCredential flows.
* [xRegistry (abstract registry base model)](XRegistry.md) - Generic registry with structural `Xid` identity and a separate `IResourceContentIdProvider` Opaque-NodeId document fast path, true dirty-`Close` registration, federated resource proxies, native events, and resource-exhaustion bounds. Shipped as `Opc.Ua.XRegistry`, `Opc.Ua.XRegistry.Client`, and `Opc.Ua.XRegistry.Server`.
* [xRegistry OPC UA / HTTP bridge](XRegistryBridge.md) - Experimental write-through gateways, optional atomic native transactions, and durable bidirectional reconciliation with configurable conflict preferences and guarded deletion propagation.
* [PubSub (Part 14)](PubSub.md) - Publisher/subscriber support library: architecture, fluent builder, transports (UDP / MQTT 3.1.1 + 5.0 / Kafka / Ethernet Layer 2), encodings (UADP / JSON), security, and server-side address space.
  * [PubSub High Availability](PubSubHighAvailability.md) - Distributed Part 14 §9.1.6 active/standby publishers and subscribers with Cold/Warm/Hot modes, leader election or fenced leases, shared runtime checkpoints, protected SKS key storage, consistency guidance, and Kubernetes deployment notes.
  * [Migration sub-doc](migrate/2.0.x/pubsub.md) - 1.5.378 → 2.0 breaking API, transport, JSON, and field-encoding changes, plus the compatibility matrix.
  * [Ethernet transport](PubSub.md#transports) - Layer 2 PubSub (`opc.eth://`, EtherType `0xB62C`, 802.1Q VLAN) with native AF_PACKET / BPF, SharpPcap, and in-memory backends.
  * [Kafka transport](PubSub.md#apache-kafka) - Apache Kafka broker transport (`kafka://`, `kafkas://`) for UADP and JSON PubSub profiles with SASL/TLS and NativeAOT support on `net10.0`.
  * [PubSub Transcoding](PubSub.md#transcoding) - In-process subscriber-to-publisher transcoding with UADP/JSON cross-encoding, field/value/metadata transforms, identifier remap, receive hooks, egress, and managed UADP re-securing.
  * [External server adapter](PubSub.md#binding-pubsub-to-an-external-opc-ua-server-client-session-adapters) - Bind PubSub publishers, subscribers, and Action responders to an external OPC UA server through `ManagedSession`.
  * [Dependency Injection extensions](DependencyInjection.md) - `AddPubSub`, `AddPubSubPublisher`, `AddPubSubSubscriber`, `AddPubSubSecurityKeyServiceClient/Server`, `AddPubSubAddressSpace`.
  * [Profiles](Profiles.md#pubsub-transports) - Datagram-v2, SKS pull / push, AES-128/256-CTR security facets.
  * [PubSub Diagnostics](Diagnostics.md#5-pubsub-packet-capture-and-dissection) - packet capture, dissection and replay of UDP / MQTT PubSub traffic, including decryption of encrypted UADP messages.
Labels describe the maturity of a package or model:

- **Preview package**: the package family keeps a `-preview` version, even when
  the core packages are stable. See [Versioning](DeveloperGuide.md#versioning).
- **Draft model**: the guide implements a working-group draft specification.
  Its namespace URIs and NodeIds are provisional.
- **Experimental**: the guide marks the feature as experimental.

These labels are independent of difficulty. An advanced guide can describe a
stable feature, and a released specification can ship in a preview package.

## 1. Get started

Run a client and server, then learn the terms that the other guides use.

- [Getting started](GettingStarted.md) — run the minimal samples, or build a
  client and server from NuGet packages.
- [OPC UA concepts](Concepts.md) — applications, endpoints, sessions, the
  address space, values, subscriptions, and PubSub.
- [Your first information model](FirstModel.md) — add a model to the Getting
  started server, then read, write, and call it from the client.
- [Sample catalogue](Samples.md) — minimal, reference, PubSub, and
  companion-model applications.
- [Packages and supported platforms](DeveloperGuide.md#packages-platform-support-and-versioning)
  — choose packages and target frameworks.

<a id="ua-core-stack-related"></a>

## 2. Build your application

Choose the path for your application. The paths are independent: for example,
a PubSub application needs neither a client nor a server.

### Client path

Assumes [Getting started](GettingStarted.md).

1. [Dependency injection: client feature](DependencyInjection.md#client-feature)
   — configure a client in the .NET Generic Host.
2. [Managed sessions](Sessions.md#3-managedsession--the-connection-state-machine-facade)
   — connect, recover from connection loss, and close. To compare the managed
   and classic session APIs, see [choosing a session API](Sessions.md#7-putting-it-all-together).
3. [Read, write, and call methods](FirstModel.md#3-use-the-model-from-the-client)
   — resolve namespace URIs and browse paths, check the status of each
   operation, and call methods through a client generated from the model.
4. [Subscriptions](Subscriptions.md) — callback subscriptions for long-lived
   monitoring and streams for short observations.

### Server path

Assumes [Getting started](GettingStarted.md).

1. [Dependency injection: server feature](DependencyInjection.md#server-feature)
   — host a server and configure its endpoints, security, and node managers.
2. [Node managers: start with a model](NodeManagers.md#start-with-a-model) —
   generate a node manager from your model and attach callbacks.
   [Your first information model](FirstModel.md) is a complete example.
3. [Runtime NodeSets](RuntimeNodeSets.md#quick-start-examples) — load NodeSet2
   models without generated code when models change independently of your
   application.
4. [Asynchronous server support](AsyncServerSupport.md) — implement service and
   node-manager callbacks asynchronously.

### PubSub path

A PubSub application needs neither a client nor a server, so the Getting
started exercise and the other two paths are optional here.

1. [Concepts: client/server and PubSub](Concepts.md#clientserver-and-pubsub) —
   publishers, subscribers, and datasets.
2. [Run a first publisher and subscriber](PubSub.md#run-a-first-publisher-and-subscriber)
   — exchange signed and encrypted messages on one computer with the
   [reference PubSub sample](../samples/PubSub/ConsoleReferencePubSubClient/README.md).
3. [PubSub guide](PubSub.md) — the fluent builder, hosting, configuration,
   encodings, and security.
4. [Transport selection](PubSub.md#transports) — UDP, Ethernet, MQTT, and Kafka.

## 3. Add data types and application services

Assumes the client or server path. Choose the capabilities your application
needs.

### Data types and models

- [Source-generated data types](SourceGeneratedDataTypes.md) — generate
  encodeable implementations for your own structures.
- [Complex types](ComplexTypes.md) — decode server-defined structures and
  enumerations at run time.
- [Model dependencies](ModelDependencies.md) — compose models that depend on
  other models.
- [NodeId assignment](NodeIdAssignment.md) — control the identifiers of nodes
  created at run time.
- [Schema generation](SchemaGeneration.md) — produce XSD, BSD, and JSON schemas
  for data types.

### Application services

- [State machines](StateMachines.md) — define, drive, and observe state
  transitions.
- [Alarms and conditions](AlarmsAndConditions.md) — raise, acknowledge, and
  stream alarms. Alarm conditions use state machines, for example for shelving.
- [Historical access](HistoricalAccess.md) — store and read value and event
  history.
- [Aggregates](Aggregates.md) — calculate processed history; read historical
  access first.
- [File system client](FileSystemClient.md) — transfer files through the
  standard file objects.
- [Alias names](AliasNames.md) — find nodes through named aliases.
- [Node management](NodeManagement.md) — let clients add and delete nodes and
  references.
- [Model change tracking](ModelChangeTracking.md) — keep client caches current
  when the address space changes.
- [NodeSet export](NodeSetExport.md) — export a server's address space to
  NodeSet2 XML.

## 4. Use companion models

Companion specifications define models for particular industries and device
types; [Concepts](Concepts.md#information-models-and-companion-specifications)
explains how they relate to the base model. These libraries assume the server
path and, for typed client access, the client path. The packages load the
models that each model depends on.

- [Device Integration](DeviceIntegration.md) (OPC 10000-100; preview package) —
  devices, topology, and locking.
- [Software update](SoftwareUpdate.md) (OPC 10000-100; preview package) —
  software package storage, installation, and confirmation. Read Device
  Integration first.
- [ISA-95](ISA95.md) (OPC 10030 and OPC 10031-4; preview package) — the common
  model and job control.
- [Positioning](Positioning.md) (OPC 10000-210 and OPC 10000-211; preview
  package) — relative frames and geographic positions.
- [Machinery](Machinery.md) (OPC 40001-1, -2, -3, -4 and -101; preview
  package) — machines with identification, server-driven state machines,
  process values, job management, energy and result transfer, based on the
  Industrial Automation, Device Integration, PA-DIM, ECM and ISA-95 models.
- [Pumps](Pumps.md) (OPC 40223; preview package) — pumps and vacuum pumps,
  published both as Device Integration devices and as Machinery machines.
- [Scales](Scales.md) (OPC 40200 and OPC 30050; preview package) — weighing
  technology with every scale kind, scale systems, recipes and PackML state
  machines, based on the Machinery model.
- [Robotics](Robotics.md) (OPC 40010-1; preview package) — motion-device
  systems, based on the Industrial Automation and Device Integration models.
  The guide's draft Robot Intent model is listed in [stage 7](#7-draft-and-experimental-capabilities).
- [WoT connectivity](WoTConnectivity.md) (OPC 10100-1; preview package) —
  connect Web of Things assets. Its registry extension is listed in stage 7.

For more examples, including the Pumps and Machinery models, see the
[companion-model samples](Samples.md#companion-model-samples).

## 5. Prepare for production

Builds on the certificate trust you used in [Getting started](GettingStarted.md).
Before you deploy a client or server, work through the
[production readiness checklist](ProductionChecklist.md). It links to these
guides for the details:

1. [Certificates](Certificates.md) — certificate stores, trust, and chain
   validation.
2. [Identity providers](IdentityProviders.md) — authenticate users on clients
   and servers.
3. [Role-based user management](RoleBasedUserManagement.md) — map identities to
   roles and permissions.
4. [Certificate manager](CertificateManager.md) — certificate lifecycle,
   rotation, and remote updates.
5. [Diagnostics](Diagnostics.md) — logging, metrics, tracing, auditing, and
   packet capture.
6. [Rate limiting](RateLimiting.md) — admission limits and busy signals during
   connection storms.
7. [Server resource isolation](ResourceIsolation.md) — per-caller limits and
   reserved capacity for startup and recovery.
8. [Profiles and facets](Profiles.md) — implemented profiles and platform
   boundaries.
9. [Container reference server](ContainerReferenceServer.md) — run the
   reference server in a container.

## 6. Advanced integration, reliability, and scale

Each group names the stages it assumes.

### Transports and integration

Assumes the client or server path.

- [Transports](Transports.md) — UA-TCP, HTTPS, and WebSocket transports.
- [REST binding](WebApi.md) — OpenAPI mapping, encoding negotiation, and HTTP
  authentication.
- [Reverse Connect](ReverseConnect.md) — server-initiated connections.
- [PubSub external-server adapter](PubSub.md#binding-pubsub-to-an-external-opc-ua-server-client-session-adapters)
  — bind PubSub to an OPC UA server through a managed client session. Assumes
  the client and PubSub paths.
- [PubSub transcoding](PubSub.md#transcoding) — convert encodings, values, and
  metadata between PubSub messages.
- [MCP server](McpServer.md) — let AI agents browse, read, write, and diagnose
  OPC UA servers.
- [WoT / NodeSet conversion](WoTNodeSetConversion.md) and
  [WoT protocol bindings](WotBindings.md) — convert Thing Descriptions and add
  protocol bindings. Read WoT connectivity first.

### Session recovery and performance

The subscription guides assume the client path; the sizing guides assume
[Diagnostics](Diagnostics.md).

- [TransferSubscriptions](TransferSubscription.md) — move server subscriptions
  to a new session.
- [Durable subscriptions](DurableSubscription.md) — keep subscriptions and
  queued notifications while clients are disconnected.
- [Server scalability](ServerScalability.md) — sizing limits and bottlenecks.
- [Benchmarks](Benchmarks.md) — measured performance and methodology.

<a id="global-discovery-server-gds"></a>

### Security infrastructure and fleet management

Assumes [stage 5](#5-prepare-for-production).

- [ECC profiles](EccProfiles.md) — elliptic-curve security policies and their
  platform requirements.
- [Crypto providers](CryptoProvider.md) — replace the platform cryptography and
  keep private keys in hardware.
- [Provisioning mode](ProvisioningMode.md) — configure a new server securely
  before exposing application data.
- [Global Discovery Server](GDS.md) — application registration and central
  certificate management.
- [GDS sample](../samples/Gds/README.md) — a runnable onboarding workflow.
- [AuthorizationService](AuthorizationService.md) — issue access tokens.
- [KeyCredentialService](KeyCredentialService.md) — manage credentials for
  services that are not OPC UA. Its issued-token bridge is listed in stage 7.

### High availability and deployment

Assumes a working single server and [stage 5](#5-prepare-for-production).

- [High availability](HighAvailability.md) (preview package) — redundancy,
  failover, and distributed state.
- [Replica-consistent NodeIds](ReplicaNodeIdentity.md) (preview package) — keep
  node identifiers identical across replicas; required before replicating an
  address space.
- [Kubernetes](Kubernetes.md) (preview package) — cluster deployment,
  discovery, readiness, and secrets. Read high availability first.
- [PubSub high availability](PubSubHighAvailability.md) (preview package) —
  redundant publishers and subscribers. Assumes the PubSub path.

### Native AOT publishing

Publish an application as a native executable without a JIT compiler; start
with [Native AOT: publish an application](NativeAoT.md#publish-an-application).
Feature-specific guidance:

- [Servers with generated node managers](NodeManagers.md#nativeaot-publishing)
- [Applications hosted with dependency injection](DependencyInjection.md#native-aot)
- [PubSub applications](PubSub.md#native-aot)

## 7. Draft and experimental capabilities

These guides implement working-group drafts or experimental extensions.
Namespace URIs, NodeIds, and APIs can change before the specifications are
published, so evaluate them before you depend on them in production. Each
guide describes its current limitations.

- [xRegistry](XRegistry.md) (draft model; preview package) — the generic
  registry base model, resource storage, and federation.
- [xRegistry OPC UA / HTTP bridge](XRegistryBridge.md) (experimental) —
  write-through gateways, opt-in persistent short links, model-driven native
  mappings, and durable bidirectional reconciliation with guarded mutations.
- [AI model management](AI.md) (draft model; preview package) — model
  catalogues, deployments, inference, and learning jobs. Builds on xRegistry.
- [Vision](Vision.md) (draft model; preview package) — perception, media,
  feedback, and client workflows.
- [Robot Intent](Robotics.md#robot-intent) (draft model; preview package) —
  task-level motion commands. Does not require the Robotics model.
- [OpenUSD](OpenUsd.md) (draft models; preview package) — bind values to OpenUSD
  scenes and materialize scenes in the address space.
- [WoT Connectivity 1.1 registry](WoTConnectivity.md#11-wot-connectivity-11-registry-and-materialization-preview)
  (draft model; preview package) — registry and materialization. Read WoT
  connectivity first.
- [KeyCredential issued-token bridge](KeyCredentialService.md#experimental-keycredential-issued-token-bridge)
  (experimental).

These samples combine several draft or preview capabilities. Read the guides
for their models first:

- [Bin picking cell](../samples/Robotics/BinPickingCell/README.md) — Robot
  Intent, Vision, and an OpenUSD scene.
- [Visual inspection cell](../samples/Vision/VisualInspectionCell/README.md) —
  Vision, AI model management, and ISA-95 job control.
- [WoT aggregation sample](../samples/WotCon/README.md) — load Device
  Integration, Machinery, and Pumps models from WoT documents through the WoT
  Connectivity 1.1 registry, and bind them to two OPC UA source servers.
- [Generator sample model](../samples/OpenUsd/GeneratorServer/Generators.md)
  (draft model) — generating-set simulation and scene composition.

<a id="reference-application-related"></a>

## Reference and upgrade

- [What's New in 2.0](WhatsNewIn2.0.md) — a summary of new capabilities.
- [Migration Guide](MigrationGuide.md) — upgrade from 1.5.378, with
  [topic-specific 2.0 guides](migrate/2.0.x/README.md) and
  [PubSub migration](migrate/2.0.x/pubsub.md).
- [Reference client](../samples/Reference/ConsoleReferenceClient/README.md) —
  command-line configuration and transport profiles.
- [Reference server](../samples/README.md) — hosting and compliance-test setup.
- [Reference PubSub client](../samples/PubSub/ConsoleReferencePubSubClient/README.md)
  — publisher, subscriber, and external-adapter modes.

The Windows Forms reference applications are maintained in the
[Samples repository](https://github.com/OPCFoundation/UA-.NETStandard-Samples).

## Contributing and maintaining the SDK

These documents describe building and maintaining this repository, not using
the SDK in an application:

- [Developer Guide](DeveloperGuide.md) — prerequisites, build, tests, and coding
  conventions.
- [Continuous integration](DeveloperGuide.md#continuous-integration) —
  pipelines and coverage gates.
- [Native AOT test harness](NativeAoT.md#test-harness-overview) — build, run,
  and extend the Native AOT compatibility tests.
- [Release process](ReleaseProcess.md) — release branches, versioning,
  backports, and recovery.
- [Redundancy sample tests](RedundancySampleTests.md) — process-level failover
  and soak validation.
- [Fuzz testing](../fuzzing/Fuzzing.md) — encoder, certificate, and network
  campaigns.
