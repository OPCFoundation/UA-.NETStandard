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
- [Asset Management Basics](AMB.md) (OPC 10000-110; preview
  package) — make devices, machines, pumps or scales manageable assets:
  discovery, identification, health alarms, maintenance, documentation links
  and locations. Read Device Integration first.
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
- [OPC UA over OpenAPI](OpenApi.md) — serve the OpenAPI document the stack
  generates from its REST routes and call the server from generated REST
  clients.
- [Reverse Connect](ReverseConnect.md) — server-initiated connections.
- [Session-less invocation](SessionlessInvocation.md) — let clients call
  Read, Browse, Call and the other §6.3 Services without a Session.
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
- [Data Channels](DataChannels.md) (experimental) - inline streaming over a
  SecureChannel, with flow control, scheduling, and the data-channel Service Set
  in `Opc.Ua.Core`; `opc.quic` ships separately as
  `OPCFoundation.NetStandard.Opc.Ua.Bindings.Quic`. Identifiers are provisional,
  and the errata is not endorsed by the OPC Foundation.

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
