# OPC UA .NET Standard stack documentation

Start with a working application, then use the task guides and reference material
as your requirements grow. Repository build and release procedures are grouped
separately from SDK usage.

## Contents

- [Start here](#start-here)
- [UA Core stack related](#ua-core-stack-related)
  - [Connect, read, and subscribe](#connect-read-and-subscribe)
  - [Host a server and expose a model](#host-a-server-and-expose-a-model)
  - [Security and identity](#security-and-identity)
  - [Data types and schemas](#data-types-and-schemas)
  - [Deploy and operate](#deploy-and-operate)
- [PubSub](#pubsub)
- [Companion models and connectivity](#companion-models-and-connectivity)
- [Reference application related](#reference-application-related)
- [Global Discovery Server (GDS)](#global-discovery-server-gds)
- [Contributing and maintaining the SDK](#contributing-and-maintaining-the-sdk)

## Start here

1. Choose a [sample application](samples.md) and the packages for your client or
   server. Check [package and platform support](DeveloperGuide.md#packages-platform-support-and-versioning)
   before selecting a target framework.
2. Start a server with [dependency injection](DependencyInjection.md#server-feature)
   or the [reference server](../samples/README.md). Establish
   [certificate trust](Certificates.md) before enabling secured connections.
3. [Connect a managed client](Sessions.md), then
   [subscribe to data changes](Subscriptions.md#streaming-subscriptions).
4. Expose your own model with [node managers](NodeManagers.md#start-with-a-model)
   or [runtime NodeSets](RuntimeNodeSets.md#quick-start-examples).
5. Add the security, history, alarms, and deployment capabilities your application
   needs using the guides below.

Upgrading an existing application? Read [What's New in 2.0](WhatsNewIn2.0.md)
for orientation, then follow the [Migration Guide](MigrationGuide.md) and its
[topic-specific 2.0 guides](migrate/2.0.x/README.md).

## UA Core stack related

### Connect, read, and subscribe

- [Sessions](Sessions.md) — session selection, connection, reconnection, and subscription engines.
- [Subscriptions](Subscriptions.md) — streaming and callback APIs, monitored items, triggering, and partitioning.
- [TransferSubscriptions](TransferSubscription.md) — preserve server subscriptions across session changes.
- [Durable subscriptions](DurableSubscription.md) — persist subscriptions and notifications.
- [Transports](Transports.md) — UA-TCP, HTTPS, and WebSocket client/server configuration.
- [REST binding](WebApi.md) — OpenAPI mapping, encoding negotiation, and authentication.
- [Reverse Connect](ReverseConnect.md) — server-initiated transport connections.
- [File system client](FileSystemClient.md) — standard OPC UA file-transfer operations.
- [Model change tracking](ModelChangeTracking.md) — refresh client caches when the address space changes.
- [NodeSet export](NodeSetExport.md) — export a server model to NodeSet2 XML.

### Host a server and expose a model

- [Dependency injection](DependencyInjection.md) — shared configuration and Generic Host integration.
- [Node managers](NodeManagers.md) — model authoring, callbacks, routing, and runtime lifecycle.
- [Asynchronous server support](AsyncServerSupport.md) — async service and node-manager extension points.
- [Runtime NodeSets](RuntimeNodeSets.md) — load models from files or streams without regenerating code.
- [Node management](NodeManagement.md) — AddNodes, DeleteNodes, and reference services.
- [NodeId assignment](NodeIdAssignment.md) — identifier allocation and custom assignment.
- [Model dependencies](ModelDependencies.md) — register and compose dependent models.
- [Alarms and conditions](AlarmsAndConditions.md) — server alarm behavior and typed client access.
- [Historical access](HistoricalAccess.md) — historian providers and client read/update workflows.
- [Aggregates](Aggregates.md) — historical processing and aggregate configuration.
- [State machines](StateMachines.md) — define, drive, and observe state transitions.
- [Alias names](AliasNames.md) — named lookup and alias-category management.

### Security and identity

- [Certificates](Certificates.md) — certificate stores, trust, chain validation, and deployment.
- [Certificate manager](CertificateManager.md) — lifecycle, rotation, validation APIs, and push transactions.
- [Identity providers](IdentityProviders.md) — client identities, server authenticators, and token issuance.
- [Role-based user management](RoleBasedUserManagement.md) — authorization and identity-to-role mapping.
- [ECC profiles](EccProfiles.md) — curve support and target-framework restrictions.
- [Crypto providers](CryptoProvider.md) — hardware-held keys and injectable cryptography.
- [Provisioning mode](ProvisioningMode.md) — initial trust and secure server configuration.

### Data types and schemas

- [Source-generated data types](SourceGeneratedDataTypes.md) — generated encodeables and model annotations.
- [Complex types](ComplexTypes.md) — discover and load server-defined types at runtime.
- [Schema generation](SchemaGeneration.md) — XSD, BSD, and JSON Schema generation.

### Deploy and operate

- [Profiles and facets](Profiles.md) — capability overview and implementation boundaries.
- [Diagnostics](Diagnostics.md) — logging, metrics, tracing, auditing, and packet capture.
- [Native AOT](NativeAoT.md) — publishing without a JIT compiler.
- [Rate limiting](RateLimiting.md) — admission controls and overload/retry signaling.
- [High availability](HighAvailability.md) — redundancy, failover, and distributed state.
- [Replica-consistent NodeIds](ReplicaNodeIdentity.md) — stable identity across replicas.
- [Kubernetes](Kubernetes.md) — cluster deployment, discovery, readiness, and secrets.
- [Server scalability](ServerScalability.md) — sizing limits and bottlenecks.
- [Benchmarks](Benchmarks.md) — measured performance and methodology.
- [MCP server](McpServer.md) — browse, read, write, and diagnose OPC UA servers through tools.

## PubSub

- [PubSub guide](PubSub.md) — builder, configuration, transports, security, and address-space integration.
- [PubSub high availability](PubSubHighAvailability.md) — redundant publishers/subscribers.
- [Transport selection](PubSub.md#transports) — UDP, Ethernet, MQTT, and Kafka.
- [Transcoding](PubSub.md#transcoding) — bridge encodings, values, and metadata.
- [External-server adapter](PubSub.md#binding-pubsub-to-an-external-opc-ua-server-client-session-adapters) — bind PubSub to a managed client session.
- [PubSub migration](migrate/2.0.x/pubsub.md) — package/API transitions and wire-compatibility limits.

## Companion models and connectivity

- [Device Integration](DeviceIntegration.md) — device composition, topology, and locking.
- [Software update](SoftwareUpdate.md) — package storage, loading, installation, and confirmation.
- [Robotics](Robotics.md) — motion-device topology and client access.
- [Vision](Vision.md) — perception, media providers, feedback, and client workflows.
- [AI model management](AI.md) — catalogues, deployments, inference, and learning jobs.
- [Positioning](Positioning.md) — relative frames and geographic coordinates.
- [ISA-95](ISA95.md) — common models and job control.
- [OpenUSD](OpenUsd.md) — scene materialization and address-space integration.
- [xRegistry](XRegistry.md) — registry resources, storage, and federation.
- [WoT connectivity](WoTConnectivity.md) — hosting, registry access, and projection lifecycle.
- [WoT / NodeSet conversion](WoTNodeSetConversion.md) — mappings, preservation, and round trips.
- [WoT protocol bindings](WotBindings.md) — protocol planners, executors, and custom bindings.
- [WoT aggregation sample](../samples/WotCon/README.md) — compose live sources into a companion model.
- [Generator sample model](../samples/OpenUsd/GeneratorServer/Generators.md) — generating-set simulation and scene composition.

Draft companion models are identified in their own guides; do not assume their
namespace URIs or NodeIds are finalized.

## Reference application related

- [Sample catalogue](samples.md) — minimal, reference, PubSub, and companion-model examples.
- [Reference client](../samples/Reference/ConsoleReferenceClient/README.md) — command-line configuration.
- [Reference server](../samples/README.md) — hosting and CTT setup.
- [Reference PubSub client](../samples/PubSub/ConsoleReferencePubSubClient/README.md) — publisher, subscriber, and external-adapter modes.
- [Container reference server](ContainerReferenceServer.md) — container configuration and local deployment.

The Windows Forms reference applications are maintained in the
[Samples repository](https://github.com/OPCFoundation/UA-.NETStandard-Samples).

## Global Discovery Server (GDS)

- [GDS guide](GDS.md) — registration, pull/push certificates, hosting, and conformance evidence.
- [GDS sample](../samples/Gds/README.md) — runnable onboarding workflow.
- [KeyCredentialService](KeyCredentialService.md) — credentials for non-OPC UA services.
- [AuthorizationService](AuthorizationService.md) — access-token issuance and provider contracts.

## Contributing and maintaining the SDK

These documents concern building and maintaining this repository, rather than
adding the SDK to an application:

- [Developer Guide](DeveloperGuide.md) — prerequisites, build, tests, and coding conventions.
- [Continuous integration](DeveloperGuide.md#continuous-integration) — pipelines and coverage gates.
- [Release process](ReleaseProcess.md) — release branches, versioning, backports, and recovery.
- [Redundancy sample tests](RedundancySampleTests.md) — process-level failover and soak validation.
- [Fuzz testing](../fuzzing/Fuzzing.md) — encoder, certificate, and network campaigns.
