# OPC UA Profiles and Facets Support

This document describes which [OPC UA Profiles and Facets](https://profiles.opcfoundation.org/) are implemented in the OPC UA .NET Standard Stack, and where in the codebase / documentation each one lives.

## Overview

The OPC UA .NET Standard Stack implements core OPC UA services and selected
companion specifications, with **OPC UA 1.05.07** as its core implementation
target. The individual parts have different latest published versions. The
[published specification support matrix](#published-specification-support-matrix)
at the end lists every specification in the
[OPC Foundation online reference](https://reference.opcfoundation.org/), its
latest listed version, and the SDK's support level.

The SDK provides full or partial support for these core areas:

- Parts 1-3 and 5-6: concepts, security, address-space and information models,
  encodings and transport mappings.
- Part 4: discovery, sessions, browsing, attributes, methods, node management
  and subscriptions. Query client APIs exist, but the default server does not
  implement the Query service set.
- Part 7: the server, client, transport and security profiles described below;
  coverage is a selection of the profile catalogue.
- Parts 8-11 and 13: Data Access, Alarms and Conditions,
  [Programs](Programs.md), [Historical Access](HistoricalAccess.md) and
  [Aggregates](Aggregates.md).
- Part 12: LDS/LDS-ME, GDS registration, certificate management, authorization
  and key credentials, with the [documented GDS gaps](GDS.md#remaining-optional--unsupported-part-12-items).
- Part 14: [PubSub](PubSub.md), including UADP/JSON, broker and datagram
  transports, with platform-dependent DTLS profile limits.
- Parts 16-20 and 23: state machines, aliases, roles, dictionary references,
  file transfer and common reference types.
- Part 21: registrar ticket administration; the complete device-facing
  onboarding state machine is outside the implemented scope.
- Part 22: [Base Network Model](BaseNetworkModel.md) runtime, typed clients and
  an in-memory simulator, including defined optional capabilities when supplied
  by the provider; applications supply production network providers.
- Parts 25 and 26: generated Object Serialization and
  LogObject declarations, without complete runtime services for those models.

The [companion and extension catalogue](#companion-and-extension-models)
describes the supported Devices, Asset Management Basics, Industrial Automation,
Positioning, ISA-95, WoT, AutoID, PackML, PA-DIM, LADS, Energy Consumption
Management, Machinery, Robotics, Cranes and Hoists, plastics/rubber, extrusion,
Machine Vision, weighing, measurement, Pumps and MachineTool families.
Source-generation tooling also implements selected modelling checks relevant
to OPC 11030.

Support describes SDK functionality, not certification of every current
feature, companion model or application. Historical reference-server
certification applies only to its tested product version and profile scope.
Applications still need the required providers, configuration and equipment.

See
[What's New in 2.0](WhatsNewIn2.0.md) for the broader change narrative.

The canonical list of OPC UA profile and facet URIs is maintained by the
OPC Foundation at <https://profiles.opcfoundation.org/>. Listing an interface
or URI here does not mean every reference-server configuration advertises it.
Inspect the actual endpoint descriptions, `ServerProfileArray` and contributed
conformance units. Do not derive a profile URI from a package or feature name.

## Contents

- [Overview](#overview)
- [Server Profiles](#server-profiles)
  - [Core Server Profiles](#core-server-profiles)
  - [Functional Facets](#functional-facets)
  - [Local Discovery Server (LDS) Profile](#local-discovery-server-lds-profile)
  - [Additional facets supported by the implementation (beyond the default advertised set)](#additional-facets-supported-by-the-implementation-beyond-the-default-advertised-set)
- [Companion and extension models](#companion-and-extension-models)
- [Client Profiles](#client-profiles)
- [Transport Profiles](#transport-profiles)
  - [Client and server transports](#client-and-server-transports)
  - [PubSub transports](#pubsub-transports)
- [Security Profiles](#security-profiles)
  - [RSA-based security policies](#rsa-based-security-policies)
  - [ECC-based security policies](#ecc-based-security-policies)
    - [Traditional ECC curves](#traditional-ecc-curves)
    - [Modern ECC curves (v2.0)](#modern-ecc-curves-v20)
    - [AES-GCM and ChaCha20-Poly1305 variants (v2.0)](#aes-gcm-and-chacha20-poly1305-variants-v20)
    - [RSA Diffie-Hellman (v2.0)](#rsa-diffie-hellman-v20)
  - [Deprecated security policies](#deprecated-security-policies)
  - [Security policy None](#security-policy-none)
- [User Authentication](#user-authentication)
- [Certificate Types](#certificate-types)
  - [RSA certificates](#rsa-certificates)
  - [ECC certificates](#ecc-certificates)
- [Global Discovery Server (GDS)](#global-discovery-server-gds)
- [Message Encoding](#message-encoding)
- [Specification Compliance](#specification-compliance)
- [Configuration](#configuration)
  - [Server profile configuration](#server-profile-configuration)
  - [Contributed profiles and conformance units](#contributed-profiles-and-conformance-units)
  - [Security policy configuration](#security-policy-configuration)
- [Related Documentation](#related-documentation)
  - [Core and companion spec related documentation](#core-and-companion-spec-related-documentation)
- [References](#references)
- [Published specification support matrix](#published-specification-support-matrix)

## Server Profiles

The reference server (`samples/Reference/ConsoleReferenceServer`) advertises the
following profiles in its `ServerProfileArray`:

### Core Server Profiles

- **[Standard UA Server Profile (2017)](http://opcfoundation.org/UA-Profile/Server/StandardUA2017)** — The core OPC UA Server profile that includes:
  - Basic server capabilities
  - Discovery services
  - Session management
  - Subscription management
  - MonitoredItem services
  - View services (Browse, BrowseNext, TranslateBrowsePathsToNodeIds)
  - Attribute services (Read, Write, HistoryRead, HistoryUpdate)

  Query request/response types and client calls are available, but the default
  server returns `BadServiceUnsupported` for `QueryFirst` and `QueryNext`.

### Functional Facets

- **[Data Access Server Facet](http://opcfoundation.org/UA-Profile/Server/DataAccess)** — Variables, data types, and data-change notifications.
- **[Method Server Facet](http://opcfoundation.org/UA-Profile/Server/Methods)** — Method calls on objects in the address space.
- **[Reverse Connect Facet](http://opcfoundation.org/UA-Profile/Server/ReverseConnect)** — Server-initiated connections to a client (see [Reverse Connect documentation](ReverseConnect.md)).
- **[Client Redundancy Facet](http://opcfoundation.org/UA-Profile/Server/ClientRedundancy)** — Subscription transfer between sessions/servers; see [Transfer Subscriptions](TransferSubscription.md).
- **Historical Access (15 released 2022 Server facets)** — the ReferenceServer
  capability-gates and advertises:
  - [Raw Data](http://opcfoundation.org/UA-Profile/Server/HistoricalRawData2022)
  - [Modified Data](http://opcfoundation.org/UA-Profile/Server/HistoricalModifiedData2022)
  - [AtTime](http://opcfoundation.org/UA-Profile/Server/HistoricalDataAtTime2022)
  - [Aggregate](http://opcfoundation.org/UA-Profile/Server/AggregateHistorical2022)
  - [Annotation](http://opcfoundation.org/UA-Profile/Server/HistoricalAnnotation2022)
  - [Structured Data](http://opcfoundation.org/UA-Profile/Server/HistoricalStructuredData2022)
  - [Raw Data Insert](http://opcfoundation.org/UA-Profile/Server/HistoricalDataInsert2022)
  - [Raw Data Replace](http://opcfoundation.org/UA-Profile/Server/HistoricalDataReplace2022)
  - [Raw Data Update](http://opcfoundation.org/UA-Profile/Server/HistoricalDataUpdate2022)
  - [Raw Data Delete](http://opcfoundation.org/UA-Profile/Server/HistoricalDataDelete2022)
  - [Event Read](http://opcfoundation.org/UA-Profile/Server/BaseHistoricalEvent2022)
  - [Event Insert](http://opcfoundation.org/UA-Profile/Server/HistoricalEventInsert2022)
  - [Event Replace](http://opcfoundation.org/UA-Profile/Server/HistoricalEventReplace2022)
  - [Event Update](http://opcfoundation.org/UA-Profile/Server/HistoricalEventUpdate2022)
  - [Event Delete](http://opcfoundation.org/UA-Profile/Server/HistoricalEventDelete2022)

  See [Historical Access](HistoricalAccess.md) and
  [Aggregates](Aggregates.md).

### Local Discovery Server (LDS) Profile

The `Opc.Ua.Lds.Server` library plus the `ConsoleLdsServer` reference
application implement the Local Discovery Server. The LDS application
advertises the **[Local Discovery Server 2017 Facet](http://opcfoundation.org/UA-Profile/Server/LocalDiscovery2017)**
(see `samples/Lds/ConsoleLdsServer/Lds.Server.Config.xml`).

### Additional facets supported by the implementation (beyond the default advertised set)

Beyond the default profiles advertised in the reference server's
`ServerProfileArray`, the master branch implements the following facets in
the SDK. They can be enabled per-application by registering the
corresponding NodeManager and, where applicable, adding the matching URI to
`ServerProfileArray` (consult <https://profiles.opcfoundation.org/> for the
canonical URI string before claiming a facet):

- **Alarms and Conditions** (Part 9) — Full server-side implementation
  with latched / silenced / out-of-service alarms, alarm groups, a
  suppression engine, and rate metrics. The reference server's
  `AlarmNodeManager` exposes a working sample. See
  [Alarms and Conditions](AlarmsAndConditions.md).
- **State Machine** (Part 16) — `StateMachineBuilder` and the
  `FluentFiniteStateMachineState` extensibility in
  `src/Opc.Ua.Server/StateMachines/`. The reference server's
  `BoilerStateMachineState` is an end-to-end sample. See
  [State Machines](StateMachines.md).
- **File Access** (Part 20) — Server-side `FileSystemNodeManager` +
  `IFileSystemProvider` in `src/Opc.Ua.Server/FileSystem/`. The
  reference server enables it via the `EnableFileSystemNodeManager`
  option. The matching System.IO-style client lives in
  [`FileSystemClient`](FileSystemClient.md).
- **Node Management** (Part 4 service set) —
  `INodeManagementAsyncNodeManager` opt-in plus a per-NodeManager
  `AllowNodeManagement` gate for `AddNodes` / `DeleteNodes` /
  `AddReferences` / `DeleteReferences`. See
  [Node Management](NodeManagement.md).
- **Alias Names** (Part 17) — `AliasNameStore` and the optional
  `AliasNameNodeManager` for `AliasNameCategory` / `FindAlias` /
  `FindAliasVerbose` / `AddAliasesToCategory` / `DeleteAliasesFromCategory`
  / `LastChange`. The reference server wires the standard Aliases and
  Topics nodes via `ConfigureAliasNameStore`. See
  [Alias Names](AliasNames.md).
- **Base Network Model** (Part 22) — `AddNetworkModel` binds application-owned
  providers to standard Resources entry points, interfaces, priority mappings,
  TSN streams and LLDP, including defined optional capabilities when supplied.
  The reference provider simulates state; it does not configure host interfaces
  or reserve TSN bandwidth. No profile URI is synthesized from the
  specification's conformance-unit names. See
  [Base Network Model](BaseNetworkModel.md).
- **WoT Connectivity** (OPC 10100-1) — `Opc.Ua.WotCon` /
  `Opc.Ua.WotCon.Server` / `Opc.Ua.WotCon.Client` library trio for
  surfacing OPC UA servers as Web-of-Things Thing Descriptions. See
  [WoT Connectivity](WotCon.md).
- **Device Integration** (Part 100) — `Opc.Ua.Di` / `Opc.Ua.Di.Server` /
  `Opc.Ua.Di.Client` library trio, including the lock service and the
  software-update package store. See [Device Integration](DeviceIntegration.md)
  and [Software Update](SoftwareUpdate.md).
- **Relative Spatial Location and Global Positioning** (Parts 210 and 211) —
  `Opc.Ua.Positioning` / `Opc.Ua.Positioning.Server` /
  `Opc.Ua.Positioning.Client` implement the released RSL and GPOS models,
  including RSL spatial lists and model changes plus the GPOS GlobalPosition,
  GlobalLocation, and Zones conformance units. See
  [Positioning](Positioning.md).
- **Industrial Automation** (OPC 10000-200), **Full** — shared `Opc.Ua.IA`
  model with `Opc.Ua.IA.Server` provider bindings for stacklights, statistics
  and calibration-target information, and `Opc.Ua.IA.Client` generated-proxy
  access, discovery and feature inspection. Statistics copy exact
  `EnumValueType` and `Argument` bodies field by field and accept
  application-defined structures as encoded `ExtensionObject` values. Providers
  encode custom structures before publication, and clients with the matching
  model can decode them. Physical procedures and storage remain
  application-owned. See
  [Industrial Automation](IndustrialAutomation.md).
- **Robotics** (OPC 40010-1) over **Industrial Automation** (OPC 10000-200) —
  `Opc.Ua.Robotics` / `Opc.Ua.Robotics.Server` / `Opc.Ua.Robotics.Client`
  library trio implementing the released Robotics 1.02 model over IA and
  Device Integration, with validated fluent topology builders for motion
  device systems, controllers, motion devices, axes, power trains, motors,
  gears, drives, safety states, and task controls. See
  [Robotics](Robotics.md).
- **Role Management** (Part 18) — Server-side role administration plus a
  pluggable [identity-provider model](IdentityProviders.md) for anonymous,
  username, X.509, and token-issuer flows. The server automatically
  assigns the OPC UA Part 3 §4.9 `TrustedApplication` role; see also
  [Role-Based User Management](RoleBasedUserManagement.md).
- **Auditing** — The server raises audit events for security-relevant
  service calls (channel/secure-channel/session/activate/cancel). The
  audit and redaction APIs are provided by `Opc.Ua.Server`.
- **Model Change Tracking** — Server-side `ModelChangeAggregator` with
  auto-emitted `GeneralModelChangeEvent` from `CustomNodeManager`;
  client-side per-node `INodeCache.InvalidateNode`. See
  [Model Change Tracking](ModelChangeTracking.md).
- **Durable Subscriptions** — Subscriptions that persist across reconnects.
  See [Durable Subscriptions](DurableSubscription.md).
- **Complex Types** — Custom structures and enumerations; see
  [Complex Types](ComplexTypes.md).
- **Async server NodeManagers** — TAP-based `AsyncCustomNodeManager` is the
  recommended base for new NodeManagers, and every NodeManager shipped with
  the stack has migrated to it. See [Async Server Support](AsyncServerSupport.md).
- **Session-less invocation** (Part 4 §6.3) - opt-in invocation of the permitted
  service sets through the existing identity/authorization pipeline; this does
  not enable session-less subscriptions. See [Session-less invocation](SessionlessInvocation.md).
- **Final-value publication before node removal** - a node manager can await
  inclusion of final values in server Publish responses before retiring dynamic
  nodes. This is an SDK lifecycle facility, not a separately certified profile
  or transport-acknowledgment guarantee. See
  [Node managers](NodeManagers.md#final-value-publication-before-node-removal).
- **Server redundancy (Part 4 §6.6)** — `AddServerRedundancy(...)` publishes
  the `Server.ServerRedundancy` object, drives `Server.ServiceLevel`, and
  supports every `RedundancySupport` mode (None / Cold / Warm / Hot /
  HotAndMirrored / Transparent). Non-transparent modes advertise the **`NTRS`**
  discovery capability and return the peer set from `FindServers`;
  `RequestServerStateChange` supports administrator-driven Maintenance / NoData
  failover. Opt-in distributed high availability — address-space, session, and
  subscription mirroring over an in-package CRDT (eventual) or Raft (strong)
  shared store, plus a Kubernetes deployment — is provided by the
  `Opc.Ua.Redundancy`, `.Server`, `.Client`, and `.Kubernetes` packages. See
  [High Availability](HighAvailability.md).

## Companion and extension models

This catalogue lists only implemented families. Full and Partial use the
[support definitions](#published-specification-support-matrix) below.
Generated declarations, runtime behavior, advertised conformance units and
external certification are separate evidence. A provider requirement is an
application integration boundary; a missing protocol or withheld facet is a
support gap.

| Model or family | Support | Implemented behavior and limits |
| --- | --- | --- |
| [Base Network Model](BaseNetworkModel.md), OPC 10000-22 | Full | Provider-backed Resources model, typed clients and simulator, with defined optional interface, mapping, TSN and LLDP capabilities when supplied; applications supply production network providers |
| [Device Integration](DeviceIntegration.md), OPC 10000-100 | Partial | Device composition, locking and software updates; offline/online representations, some loading modes and `UpdateStatus` remain incomplete |
| [Asset Management Basics](AMB.md), OPC 10000-110 | Partial | Identity, health, maintenance, documentation, locations and dictionary classification; asset-health and past-maintenance event history remain unimplemented |
| [Industrial Automation](IndustrialAutomation.md), OPC 10000-200 | Full | Stacklights, statistics and calibration targets with provider bindings, typed clients and a sample; physical drivers and calibration remain application-owned |
| [Machinery](Machinery.md), OPC 40001-1/-2/-3/-4/-101 | Full | Building blocks, process values, jobs, energy and result transfer; select capabilities and supply equipment/storage providers |
| [MachineTool and WMTP](MachineTool.md), OPC 40501-1 and OPC 40505 | Full | Monitoring, tools, production, jobs, files, peripheral measurements/configuration/records and typed clients; physical CNC/Bluetooth behavior belongs to providers |
| [GMS](GMS.md), OPC 40210 | Partial | Systems, sensors, calibration observations, jobs/parts and results; Production facet withheld because upstream leaves `GMS_ProductionStatisticsType` undefined |
| [Cranes and Hoists](CranesHoists.md), OPC 40020-1 | Full | Robotics-derived topology, load/protection observations and authorized limit requests; not a motion or safety controller |
| [Pumps](Pumps.md), OPC 40223 | Full | Fluent builders, typed identity writes, retained identification, DI NE107/limit conditions, event notification, shared raw/aggregate/event history and files; profile advertisement follows configured inherited facets; Table 155's Base dependency uses the Table 153 Pump Base URI while the CAS Base link remains a publication discrepancy; no physical pump executor |
| [AutoID Devices](AutoID.md), OPC 30010 | Partial | Reader hosting, scanning, RFID access, RTLS, events and clients; Full facet lacks `ScanActive` control/RuntimeParameters, and the optical-verifier Scan declaration is inconsistent upstream |
| [Scales](Scales.md), OPC 40200 | Partial | Weighing, zero/tare, products, statistics, recipes and clients; dedicated catchweigher, price-labeler and totalizing-hopper controllers are missing, as is server evaluation of recipe sleep thresholds |
| [ISA-95](ISA95.md), OPC 10030 and OPC 10031-4 | Full | Common resources and Job Control V1/V2 server/client interfaces; providers supply execution, catalogues and persistence |
| [Positioning](Positioning.md), OPC 10000-210/-211 | Full | RSL/GPOS models, spatial lists, model changes, positions, locations, zones and transforms; hardware observations require providers |
| [Robotics](Robotics.md), OPC 40010-1 | Full | Released motion-device topology and controller/program interfaces; physical motion and safety behavior remain equipment responsibilities |
| [Machine Vision](Vision.md), OPC 40100-1/-2 | Full | Control, configuration, recipes, results, asset/condition monitoring and clients; providers supply acquisition, separate from the Perception draft |
| [WoT Connectivity](WotCon.md), OPC 10100-1 | Full | Published 1.02 asset-connection API, server and client; additive registry extensions have separate scope |
| [WoT Binding](WotBindings.md), OPC 10101 | Partial | Published 1.00 consumer mode and conversions; general UA Binary payload codec and complete TD/TM/Binding-result schema validation are missing |
| [PackML](Scales.md), OPC 30050 | Partial | Generated model and Scales recipe/state integration; not a standalone implementation of every PackML unit mode |
| [PA-DIM and ECM](Machinery.md), OPC 30081 and OPC 34100 | Partial | Generated dependency models and Machinery process-value/energy bindings; not complete standalone PA-DIM/ECM device runtimes |
| [LADS](LADS.md), OPC 30500-1 | Partial | Devices, functional units, functions, programs, results and maintenance; optional Hold/Unhold/Suspend/Unsuspend/Reset/ToComplete server methods and persistence are missing |
| [Plastics and rubber common types](PlasticsRubber.md), OPC 40083 | Full | Common controls, monitoring, maintenance, materials, diagnostics and production datasets; independently authored 1.03 interface implementation on the licensed 1.02 basis |
| [Granulate drying](PlasticsRubber.md#granulate-drying-devices-opc-40082-6), OPC 40082-6 | Full | Device construction, selectable conformance units, provider-backed commands, observations, events and typed clients |
| [Extrusion](Extrusion.md), OPC 40084-1/-2/-3/-4/-5/-7/-8/-9/-10/-11/-12 | Full | General types and concrete device/line runtimes, jobs, datasets, events and clients; equipment authority remains with explicit providers |
| [Extrusion Filter](Extrusion.md#filter-profile-discrepancy), OPC 40084-6 | Partial | Filter model/runtime implemented; profile withheld pending clarification of the published requirement for the Part 5 Melt Pump conformance unit |

Implemented draft extensions are separate from the published-specification
matrix:

| Extension | Implemented behavior and limits |
| --- | --- |
| [Perception](Vision.md#perception-model) and [Robot Intent](Robotics.md#robot-intent) | Perception/media/inference/feedback and robot task-intent interfaces; provisional identifiers and provider-dependent behavior |
| [AI](AI.md), [xRegistry](XRegistry.md), [OpenUSD](OpenUsd.md) | Model/deployment/inference services, registry/version/federation and scene/address-space integration; draft models with backend-specific limits |

Package preview status is governed by [the release policy](ReleaseProcess.md#terms),
not by a specification's publication date. The MachineTool/GMS and released
Vision package families follow the root version; CranesHoists remains
preview-only. Draft identifiers are not promoted to standard identifiers by
shipping a package.

## Client Profiles

The client (`Opc.Ua.Client`) supports the standard UA Client functionality
through two coexisting paths:

- **Classic `Session`** — the lowest-level OPC UA session primitive,
  paired with `SessionReconnectHandler` for caller-driven reconnect.
- **`ManagedSession`** — recommended for new code. Encapsulates the
  connection state machine, reconnect policy, and pluggable subscription
  engine behind a fluent builder. See [Sessions, Reconnection, and
  Subscription Engines](Sessions.md).

Client-side feature coverage:

- **Base Network Model** (Part 22) — `AddNetworkModelClient` provides typed
  discovery, reads, observations and mapping methods. Mapping calls preserve
  operation and argument results without replaying commands. See
  [Base Network Model](BaseNetworkModel.md#discover-and-use-the-typed-client).
- **Subscriptions and monitored items** — Both the classic publish
  engine and the V2 subscription engine (`ISubscriptionManager` /
  `DefaultSubscriptionEngine`) are supported and selectable per session.
  The V2 surface includes a declarative + imperative
  [`SetTriggering`](Subscriptions.md#triggering-settriggering) API with
  N:M support and automatic replay on recreate / reconnect, plus an
  `IStreamingSubscription` (`IAsyncEnumerable`-based) facade for
  state-machine waits and short-lived monitoring
  (`ManagedSession.DefaultStreaming`, `TakeUntilAsync` /
  `WithTimeoutAsync` helpers). See
  [Subscriptions and Monitored Items](Subscriptions.md).
- **Transfer Subscriptions** — Transfer existing subscriptions between sessions;
  see [Transfer Subscriptions](TransferSubscription.md). An opt-in
  `SubscriptionRecoveryPolicy` lets the client tolerate
  `Good_SubscriptionTransferred` notifications from the server.
- **Reverse Connect** — Client can accept connections initiated by the
  server; see [Reverse Connect](ReverseConnect.md).
- **Model Change Tracking** — Client-side per-node cache invalidation
  driven by server-emitted model-change events; see
  [Model Change Tracking](ModelChangeTracking.md).
- **File System Operations** — Async, `System.IO`-style client over OPC
  UA file methods; see [FileSystemClient](FileSystemClient.md).
- **Alarms and Conditions** — Typed `AlarmClient` event records, generated
  `{Type}Record.EventFilters.Build()` event filters, and `IAsyncEnumerable` alarm streaming via
  `AlarmStreamExtensions`; see
  [Alarms and Conditions](AlarmsAndConditions.md).
- **Historical Access** — `HistoryClient` (`session.Historian()`) for
  raw, modified (including `ModificationInfo`), at-time, processed
  (aggregate), annotation, and event history reads / updates; see
  [Historical Access](HistoricalAccess.md).
- **State Machines** — Streaming and read helpers
  (`GetCurrentFiniteStateAsync`, `ObserveFiniteTransitionsAsync`,
  `WaitForStateAsync`) on the source-generated `*TypeClient` proxies;
  see [State Machines](StateMachines.md).
- **NodeSet Export** — Extract a server's address space to NodeSet2 XML;
  see [NodeSet Export](NodeSetExport.md).
- **Source-generated typed proxies** — `*TypeClient` proxies for
  ObjectTypes inside loaded models give strongly-typed method-call
  signatures; see [Source-Generated NodeManagers](NodeManagers.md#source-generated-node-managers).
- **Complex types** — Decode and consume server-defined structures and
  enumerations on the client; see [Complex Types](ComplexTypes.md).
- **Generation-bound session clients** - capture a transport/session generation
  and namespace mappings; invalidated bindings reject requests instead of
  retargeting them after reconnect. This is an additive SDK capability, not a
  separate conformance profile. See [Session bindings](SessionBindings.md).
- **Typed companion workflows** - the model families above include their
  documented discovery, read, operation and observation helpers. Companion
  and general stack workflows are also exposed through bounded
  [MCP profiles](McpServer.md), with separate host grants for privileged/live
  execution. MCP catalogue selection is neither an execution grant nor an
  OPC UA Client conformance profile.

## Transport Profiles

The stack implements the following transport profiles:

### Client and server transports

- **[UA TCP Transport](http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary)** (`opc.tcp://`) — Primary OPC UA binary transport over TCP.
  - Full UA Secure Conversation (UASC)
  - Binary encoding
  - Reverse-connect capability

- **[HTTPS Binary Transport](http://opcfoundation.org/UA-Profile/Transport/https-uabinary)** (`opc.https://` and `https://`) — OPC UA binary protocol over HTTPS with TLS.

- **[HTTPS JSON Transport](http://opcfoundation.org/UA-Profile/Transport/https-uajson)** (`opc.https://` and `https://`) - OPC UA JSON (compact / reversible) over HTTPS (OPC UA Part 6 §7.4.5)
  - Compact JSON encoding (`application/opcua+uajson`)
  - TLS/SSL encryption only — no UA SecureChannel layer
  - Restricted to `MessageSecurityMode.None`; transport security is provided exclusively by TLS

- **[WebSocket Secure (UA Binary)](http://opcfoundation.org/UA-Profile/Transport/wss-uasc-uabinary)** (`opc.wss://` and `wss://`) - UA Binary + UASC over secure WebSockets (OPC UA Part 6 §7.5.2, sub-protocol `opcua+uacp`)
  - Same UASC SecureChannel pipeline as `opc.tcp` carried over WebSocket binary frames (one frame per MessageChunk)
  - Supports all security modes (None / Sign / SignAndEncrypt)
  - TLS/SSL encryption at the WebSocket layer

- **[WebSocket Secure (JSON)](http://opcfoundation.org/UA-Profile/Transport/wss-uajson)** (`opc.wss://` and `wss://`) - OPC UA JSON over secure WebSockets (Part 6 §7.5.2, sub-protocol `opcua+uajson`)
  - Compact JSON encoding per WebSocket text frame
  - TLS/SSL encryption only — no UA SecureChannel layer
  - Restricted to `MessageSecurityMode.None`

- **[HTTPS OpenAPI](https://profiles.opcfoundation.org/profile/2338)** (`opc.https://` and `https://`) - OPC UA OpenAPI Mapping (Part 6 §G.3) — official OPC Foundation profile/2338, URI `http://opcfoundation.org/UA-Profile/Transport/https-uajson-openapi`
  - Path-routed REST surface: `POST /<service>` (e.g. `/read`, `/browse`, `/createsubscription`)
  - Body is the bare `<Service>Request` / `<Service>Response` — no `{UaTypeId, UaBody}` envelope at the HTTPS layer
  - Compact (default, mandatory per §5.4.9) and Verbose JSON flavours selected via the `application/json; encoding=compact|verbose` media-type parameter
  - TLS/SSL encryption only — no UA SecureChannel layer
  - Restricted to `MessageSecurityMode.None`
  - Authentication via Anonymous, Bearer JWT, HTTP Basic, or Mutual TLS (see [WebApi.md](WebApi.md))
  - Server-side discovery emission: `HttpsServiceHost` emits this sub-profile as a discovery-only twin alongside each `SecurityMode.None` HTTPS-binary endpoint, so discovery-driven clients can find the OpenAPI endpoint without hard-coding the URL.
  - Shipped as part of the `OPCFoundation.NetStandard.Opc.Ua.Bindings.Https` package (net8+ only)
  - Surfaced via `Profiles.HttpsOpenApiTransport`

- **[WSS OpenAPI](https://profiles.opcfoundation.org/profile/2339)** (`opc.wss://` and `wss://`) - OPC UA OpenAPI Mapping over secure WebSockets (Part 6 §7.5.2, sub-protocol `opcua+openapi` / `opcua+openapi+<accesstoken>`) — official OPC Foundation profile/2339, URI `http://opcfoundation.org/UA-Profile/Transport/wss-uajson-openapi`
  - Same on-wire `{TypeId, Body}` OPC UA JSON envelope as `opcua+uajson`, multiplexed over WebSocket text frames; distinguished by the negotiated sub-protocol and the advertised TransportProfileUri
  - Bearer-token variant negotiates the access token in the sub-protocol name (no `Authorization` header — required for browser fetch compatibility); the server extracts the token from the sub-protocol name and feeds it through the standard `ISessionlessIdentityProvider` pipeline
  - Server-side discovery emission: the WSS factories emit this sub-profile as a discovery-only twin alongside each `SecurityMode.None` WSS binary endpoint, mirroring the HTTPS OpenAPI emission.
  - Client surface: `src/Opc.Ua.Client/WebApi/WebApiWssTransportChannel.cs`; fluent shortcut `ManagedSessionBuilder.UseWssOpenApiEndpoint(url)`; DI registration via `services.AddWebApiTransportChannel()` (registers both HTTPS and WSS WebApi channel factories).
  - Surfaced via `Profiles.WssOpenApiTransport`

### PubSub transports

The [PubSub library](PubSub.md) supports the following PubSub transport
facets (URIs surfaced by `Profiles.PubSub*Transport` constants in
`src/Opc.Ua.Core/Security/Constants/SecurityConstants.cs`). Facet
machinery and conformance unit semantics are defined by
[Part 7 §4.3](https://reference.opcfoundation.org/specs/OPC-10000-7/v1.05.06/4.3).

- **[PubSub UDP UADP](http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp)** — UDP transport with UADP message encoding.
- **[PubSub Ethernet UADP](http://opcfoundation.org/UA-Profile/Transport/pubsub-eth-uadp)** — Ethernet (Layer 2) transport with UADP message encoding (see [PubSub transports](PubSub.md#transports)).
- **[PubSub MQTT UADP](http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-uadp)** — MQTT transport with UADP message encoding.
- **[PubSub MQTT JSON](http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-json)** — MQTT transport with JSON message encoding.
- **[PubSub Kafka UADP](http://opcfoundation.org/UA-Profile/Transport/pubsub-kafka-uadp)**
  and **[PubSub Kafka JSON](http://opcfoundation.org/UA-Profile/Transport/pubsub-kafka-json)** -
  Part 14 Annex B.2 broker mapping through `Opc.Ua.PubSub.Kafka`; see
  [Kafka transport and backend limits](PubSub.md#apache-kafka).
- **DTLS over UDP** - supported registered profiles use the UDP transport's
  platform/cryptographic capabilities; Curve25519/Curve448 DTLS profiles are
  not registered. See [PubSub security limits](PubSub.md#security).
- **Datagram-v2 connection profile** —
  [`DatagramConnectionTransport2DataType`](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.1.4)
  (Part 14 §6.4.1.4) is honoured for UDP transports. The
  `DiscoveryAnnounceRate`, `DiscoveryMaxMessageSize`, and `QosCategory`
  fields drive discovery cadence and the IP DSCP TOS byte.
- **PubSub SKS pull / push** —
  [Part 14 §8.5.1](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.5.1)
  / §8.5.2: the
  `AddPubSubSecurityKeyServiceClient` extension implements the pull
  client (calls `GetSecurityKeys` on a remote SKS), and
  `AddPubSubSecurityKeyServiceServer` hosts the in-memory SKS with
  `Get/SetSecurityKeys` and `AddSecurityGroup` methods bound on the
  address space.
- **AES-128-CTR / AES-256-CTR with HMAC-SHA-256** —
  [Part 14 §8.4.3](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.4.3)
  message security. Profiles registered as
  `PubSubAes128CtrPolicy` / `PubSubAes256CtrPolicy`; conformance to NIST
  SP 800-38A F.5.1 / F.5.5 is asserted by the test suite.
- **Anonymous certificate-based MQTT auth** — the MQTT transport
  exposes the `MqttClientAuthenticationOptions` with the
  certificate-based variant from
  [Part 14 §6.4.2.2.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.2.2.4).

PubSub additionally supports certificate-based MQTT authentication and
considers `WriterGroup`s in MQTT keep-alive calculations.

All transport profiles defined in OPC UA Part 6 §7.4 (HTTPS) and §7.5
(WebSockets) are supported, including the `opcua+openapi` and
`opcua+openapi+<accesstoken>` WebSocket sub-protocols (Part 6 §7.5.2
Table 81).

## Security Profiles

The stack supports the following security profiles for secure
communication. The canonical set is defined in
`src/Opc.Ua.Core/Security/Constants/SecurityPolicies.cs`.

### RSA-based security policies

- **[Basic256Sha256](http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256)**
  - 256-bit AES encryption
  - RSA-OAEP for key encryption
  - HMAC-SHA256 for message authentication
  - Minimum key size: 2048 bits
- **[Aes128_Sha256_RsaOaep](http://opcfoundation.org/UA/SecurityPolicy#Aes128_Sha256_RsaOaep)**
  - 128-bit AES encryption
  - RSA-OAEP for key encryption
  - HMAC-SHA256 for message authentication
- **[Aes256_Sha256_RsaPss](http://opcfoundation.org/UA/SecurityPolicy#Aes256_Sha256_RsaPss)**
  - 256-bit AES encryption
  - RSA-PSS signatures
  - HMAC-SHA256 for message authentication

### ECC-based security policies

ECC support is documented in detail in [ECC Profiles](EccProfiles.md).

#### Traditional ECC curves

- **[ECC_nistP256](http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256)** — NIST P-256 with SHA-256
- **[ECC_nistP384](http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP384)** — NIST P-384 with SHA-384
- **[ECC_brainpoolP256r1](http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1)** — Brainpool P-256r1 with SHA-256
- **[ECC_brainpoolP384r1](http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP384r1)** — Brainpool P-384r1 with SHA-384

#### Modern ECC curves (v2.0)

- **[ECC_curve25519](http://opcfoundation.org/UA/SecurityPolicy#ECC_curve25519)** — Curve25519 with ChaCha20-Poly1305
- **[ECC_curve25519_AesGcm](http://opcfoundation.org/UA/SecurityPolicy#ECC_curve25519_AesGcm)** — Curve25519 with AES-GCM
- **[ECC_curve448](http://opcfoundation.org/UA/SecurityPolicy#ECC_curve448)** — Curve448 with ChaCha20-Poly1305
- **[ECC_curve448_AesGcm](http://opcfoundation.org/UA/SecurityPolicy#ECC_curve448_AesGcm)** — Curve448 with AES-GCM

#### AES-GCM and ChaCha20-Poly1305 variants (v2.0)

Modern AEAD cipher alternatives for traditional ECC curves:

- **ECC_nistP256_AesGcm**, **ECC_nistP256_ChaChaPoly**
- **ECC_nistP384_AesGcm**, **ECC_nistP384_ChaChaPoly**
- **ECC_brainpoolP256r1_AesGcm**, **ECC_brainpoolP256r1_ChaChaPoly**
- **ECC_brainpoolP384r1_AesGcm**, **ECC_brainpoolP384r1_ChaChaPoly**

#### RSA Diffie-Hellman (v2.0)

- **RSA_DH_AesGcm** — RSA Diffie-Hellman key agreement with AES-GCM
- **RSA_DH_ChaChaPoly** — RSA Diffie-Hellman key agreement with ChaCha20-Poly1305

**Platform requirements for ECC.** Certificate creation, storage, and validation
are distinct from ECC SecureChannel and user-token security policies. On .NET 8.0 or
later those policies use the platform's raw ECDH agreement and authenticated ciphers;
on .NET Framework the agreement and the AES-GCM and ChaCha20-Poly1305 ciphers run in
the managed BouncyCastle implementation. Curve and cipher availability also depends on
the OS and, on .NET 8+, the runtime probes
(`AesGcm.IsSupported` / `ChaCha20Poly1305.IsSupported`). See the
[ECC platform limitations](EccProfiles.md#known-limitations) for the certificate-operation
and channel-policy boundaries. PubSub has its own
[transport security limits](PubSub.md#security).

### Deprecated security policies

The following security policies are deprecated but still supported for
backward compatibility:

- **[Basic256](http://opcfoundation.org/UA/SecurityPolicy#Basic256)** — uses SHA-1
- **[Basic128Rsa15](http://opcfoundation.org/UA/SecurityPolicy#Basic128Rsa15)** — uses SHA-1 and RSA-PKCS#1 v1.5

**Note.** SHA-1 signed certificates are rejected by default
(`RejectSHA1SignedCertificates` configuration option). These deprecated
policies should only be enabled for compatibility with legacy systems.

### Security policy None

- **[None](http://opcfoundation.org/UA/SecurityPolicy#None)** — No security.
  - For testing or isolated networks only.
  - Not recommended for production environments.

## User Authentication

The stack supports the following user authentication mechanisms:

- **Anonymous** — No user authentication.
- **Username / Password** — User credentials encrypted using the active
  security policy.
- **X.509 Certificate** — User authentication via X.509 certificates.
- **Issued Token** — Includes **JSON Web Tokens (JWT)** and other
  IssuerEndpointUrl-driven flows (OAuth2 / OIDC / Entra). The
  [Identity Providers](IdentityProviders.md) pluggable model
  (`IClientIdentityProvider`, `IUserTokenAuthenticator`,
  `IAccessTokenProvider`, `ITokenIssuer`, `IIdentityClaims`) is the
  recommended way to wire these in.

## Certificate Types

The stack supports the following certificate types for application
authentication:

### RSA certificates

- **RsaSha256ApplicationCertificateType** — RSA with SHA-256 signatures
  - Default minimum key size: 2048 bits
  - Recommended for production use

### ECC certificates

- **EccNistP256ApplicationCertificateType**
- **EccNistP384ApplicationCertificateType**
- **EccBrainpoolP256r1ApplicationCertificateType**
- **EccBrainpoolP384r1ApplicationCertificateType**

The `RejectSHA1SignedCertificates` configuration option (on by default)
prevents SHA-1 signed certificates from being accepted. See
[Certificates](Certificates.md) and [Certificate Manager](CertificateManager.md)
for storage, ref-counted lifetime, and the segregated-interface design.

## Global Discovery Server (GDS)

The stack ships a Global Discovery Server implementation covering the following
OPC UA Part 12 areas:

- Application registration and discovery.
- Pull and Push certificate-management models, including pushing to
  arbitrary certificate groups and custom certificate groups on the GDS
  itself.
- Sub-CA revocation without auto-creating an empty CRL.
- Support for both RSA and ECC certificate types and CRLs.
- **[AuthorizationService](AuthorizationService.md)** (OPC 10000-12 §9) —
  OAuth2-style `StartRequestToken` / `FinishRequestToken` issuance with a
  pluggable `IAccessTokenProvider` / `ITokenIssuer`.
- **[KeyCredentialService](KeyCredentialService.md)** (OPC 10000-12 §8) —
  Credential issuance for non-OPC UA services such as MQTT brokers and
  REST APIs, backed by `IKeyCredentialRequestStore` / `ISecretStore`.
- **Registrar ticket administration** (OPC 10000-21) -
  `OnboardingClient`, `ITicketStore` and the declared registrar methods.
  This is not the complete device-facing onboarding state machine; see
  [the implemented scope](GDS.md#opc-10000-21-onboardingclient).

See the [GDS Developer Guide](GDS.md) for the implemented facets, provider-dependent
features, partial support, and hosting integration. This is not a claim that every
optional Part 12 function is implemented or certified. The Local Discovery Server is a separate library
(`Opc.Ua.Lds.Server`) and reference application (`ConsoleLdsServer`) that
advertises the
[Local Discovery Server 2017](http://opcfoundation.org/UA-Profile/Server/LocalDiscovery2017)
facet.

## Message Encoding

The stack supports the following message encoding formats:

- **UA Binary** — OPC UA binary encoding (primary for UA-TCP and HTTPS).
- **UA XML** — OPC UA XML encoding (for configuration import / export and
  PubSub Dataset XML).
- **UA JSON** — OPC UA JSON encoding for PubSub MQTT.
- **UADP** — UA Data Protocol for PubSub.

The 2.0 release ships a new JSON decoder / encoder, array / matrix
abstractions, and a `ByteString` type. See the
[2.0 migration guide — Encoders and Complex Types](migrate/2.0.x/encoders.md#encoders-and-decoders)
for the encoder/decoder migration details and
[Complex Types](ComplexTypes.md) for client-side decode of
server-defined types.

## Specification Compliance

- **OPC UA Specification:** Version 1.05.07.
- **Certification:** Historical reference-server certification does not establish
  certification of the current release or of an application built with the SDK.
  Verify the certificate and tested profile scope for the exact product version.
- **Testing:** Automated repository tests provide implementation evidence, not a
  substitute for certification. The [GDS conformance matrix](GDS.md)
  explicitly distinguishes its test evidence from CTT results.
- **Companions and extensions:** The catalogue above records implemented
  interfaces and explicit gaps, not automatic whole-specification conformance.
  Selected instances, providers, endpoint security and actual advertised CUs
  determine an application's claim; package channel is a separate policy.
- **Redundancy (Part 4 §6.6):** Server and client redundancy are implemented
  (opt-in) — `RedundancySupport`, `Server.ServiceLevel`, non-transparent
  (`ServerUriArray` / `RedundantServerArray`) and transparent (`CurrentServerId`)
  modes, `RequestServerStateChange`, and client-side transparent failover via
  `ManagedSession.WithServerRedundancy()`; see
  [High Availability](HighAvailability.md).

## Configuration

### Server profile configuration

Server profiles are configured in the server configuration file using the
`ServerProfileArray` element. The reference server's default array is:

```xml
<ServerConfiguration>
  <!-- see https://profiles.opcfoundation.org/ for the canonical list of profile and facet URIs -->
  <ServerProfileArray>
    <ua:String>http://opcfoundation.org/UA-Profile/Server/StandardUA2017</ua:String>
    <ua:String>http://opcfoundation.org/UA-Profile/Server/DataAccess</ua:String>
    <ua:String>http://opcfoundation.org/UA-Profile/Server/Methods</ua:String>
    <ua:String>http://opcfoundation.org/UA-Profile/Server/ReverseConnect</ua:String>
    <ua:String>http://opcfoundation.org/UA-Profile/Server/ClientRedundancy</ua:String>
  </ServerProfileArray>
</ServerConfiguration>
```

To advertise additional facets (Historical Access, Aggregates, Alarms &
Conditions, File Access, Auditing, NodeManagement, State Machine, etc.)
look up the canonical URI for the facet on
<https://profiles.opcfoundation.org/> and add it to `ServerProfileArray`.
Only advertise a facet that the application implements — the
Compliance Test Tool will exercise every claimed facet. Bringing the
reference-server and CTT configs in line with the facets the stack
actually implements is tracked in
[#3875](https://github.com/OPCFoundation/UA-.NETStandard/issues/3875).

### Contributed profiles and conformance units

Node managers that implement `IConformanceContributor`, such as the
companion node managers, contribute the facets and conformance units they
actually wire. At startup the server registers them with the
`ConformanceUnitsManager` and publishes the aggregate to
`Server/ServerCapabilities/ConformanceUnits` and `ServerProfileArray`.
The startup lifecycle republishes after the actual endpoint descriptions are
installed, so security-dependent companion claims are evaluated against real
endpoint policies rather than a placeholder registry.

The manager keeps the registered contributors and reads them again on every
`PublishAsync`, because the ServerCapabilities describe the current
configuration of the server (OPC 10000-5 §6.3.2). A contributor whose
support changes at runtime calls `PublishAsync` afterwards (the manager is
`ServerInternalData.ConformanceUnitsManager`); a unit or profile
that no contributor reports any more is withdrawn. `Unregister` removes a
contributor. The profiles from the `ServerProfileArray` configuration are
always kept.

### Security policy configuration

Security policies are configured in the `SecurityPolicies` section:

```xml
<SecurityPolicies>
  <ServerSecurityPolicy>
    <SecurityMode>SignAndEncrypt_3</SecurityMode>
    <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</SecurityPolicyUri>
  </ServerSecurityPolicy>
  <ServerSecurityPolicy>
    <SecurityMode>SignAndEncrypt_3</SecurityMode>
    <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#Aes128_Sha256_RsaOaep</SecurityPolicyUri>
  </ServerSecurityPolicy>
  <!-- ECC security policies -->
  <ServerSecurityPolicy>
    <SecurityMode>SignAndEncrypt_3</SecurityMode>
    <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256</SecurityPolicyUri>
  </ServerSecurityPolicy>
</SecurityPolicies>
```

See the [reference server configuration file](../samples/Reference/ConsoleReferenceServer/Quickstarts.ReferenceServer.Config.xml)
for a complete example, or the
[CTT configuration file](../samples/Reference/ConsoleReferenceServer/Ctt.ReferenceServer.Config.xml)
for the variant selected by `--ctt`.

## Related Documentation

- [What's New in 2.0](WhatsNewIn2.0.md) — Narrative tour of the
  1.5.378 → 2.0 changes, grouped by theme and layer.
- [Migration Guide](MigrationGuide.md) — Prescriptive, per-API migration
  reference.
- [Subscriptions and Monitored Items](Subscriptions.md) —
  V2 `ISubscriptionManager`, declarative + imperative `SetTriggering`
  (N:M, replay on recreate / reconnect), and `IStreamingSubscription`.
- [Async Server Support](AsyncServerSupport.md) — TAP-based
  `AsyncCustomNodeManager` and the `IAsyncNodeManager` family.
- [Dependency Injection](DependencyInjection.md) — `services.AddOpcUa()`
  and the `IOpcUaBuilder` hosting surface.
- [Native AOT](NativeAoT.md) — AOT publishing, AOT-clean source
  generators, and the AOT test matrix.

### Core and companion spec related documentation

- [Alarms and Conditions](AlarmsAndConditions.md) (Part 9)
- [Historical Access](HistoricalAccess.md) (Part 11)
- [Aggregates](Aggregates.md) (Part 13)
- [State Machines](StateMachines.md) (Part 16)
- [Alias Names](AliasNames.md) (Part 17)
- [Base Network Model](BaseNetworkModel.md) (Part 22, provider and simulation boundaries)
- [Role-Based User Management](RoleBasedUserManagement.md) (Part 18)
- [Identity Providers](IdentityProviders.md)
- [Authorization Service](AuthorizationService.md) (Part 12 §9)
- [Key Credential Service](KeyCredentialService.md) (Part 12 §8)
- [GDS Developer Guide](GDS.md) (Part 12 implemented, partial and provider-gated areas)
- [File System Client](FileSystemClient.md) (Part 20)
- [Device Integration](DeviceIntegration.md) (Part 100)
- [Relative Spatial Location and Global Positioning](Positioning.md)
  (Parts 210 and 211)
- [Industrial Automation](IndustrialAutomation.md) (OPC 10000-200)
- [Robotics](Robotics.md) (OPC 40010-1 over OPC 10000-200)
- [Software Update](SoftwareUpdate.md)
- [WoT Connectivity](WotCon.md) (OPC 10100-1)
- [Node Management](NodeManagement.md) (Part 4)
- [Asset Management Basics](AMB.md) (OPC 10000-110)
- [Machinery](Machinery.md) (OPC 40001 series, PA-DIM and ECM)
- [Pumps](Pumps.md) (OPC 40223)
- [AutoID Devices](AutoID.md) (OPC 30010)
- [Scales](Scales.md) (OPC 40200 and PackML)
- [ISA-95](ISA95.md) (OPC 10030 and OPC 10031-4)
- [MachineTool and WMTP](MachineTool.md) (OPC 40501-1 and OPC 40505)
- [Geometric Measurement Systems](GMS.md) (OPC 40210)
- [Cranes and Hoists](CranesHoists.md) (OPC 40020-1; preview packages)
- [Machine Vision and Perception](Vision.md) (released OPC 40100-1/-2 and a distinct draft model)
- [AI Model Management](AI.md), [xRegistry](XRegistry.md) and [OpenUSD](OpenUsd.md) (draft extensions)
- [Session-less invocation](SessionlessInvocation.md) (Part 4 §6.3)
- [Generation-bound session clients](SessionBindings.md)
- [PubSub](PubSub.md) (Part 14 transports and platform limits)
- [WoT Binding](WotBindings.md) (OPC 10101 published consumer mode and gaps)
- [Laboratory and Analytical Devices](LADS.md) (OPC 30500-1)
- [Plastics and rubber common capabilities](PlasticsRubber.md)
  (OPC 40083 and OPC 40082-6 granulate drying)
- [Extrusion](Extrusion.md) (OPC 40084 parts 1-12)

## References

- [OPC Foundation Profile Reporting](https://profiles.opcfoundation.org/) — canonical profile and facet URI registry.
- [OPC UA Online Reference](https://reference.opcfoundation.org/) — published core and companion specifications and their latest listed versions.
- [OPC UA Compliance Test Tool (CTT)](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/) — official conformance test tool.

## Published specification support matrix

This table includes all **149 specifications** listed on the
[OPC Foundation online reference index](https://reference.opcfoundation.org/)
when checked on **2026-11-02**. Versions reproduce the index's latest listed
version for each document, including its original number formatting; they are
not NuGet package versions or a statement that every shipped model has that
exact revision. Specification names are shortened where needed. Each part of a
multipart specification has its own row.

Support describes the implementation in this repository:

- **Full**: the specification's applicable SDK model and protocol behavior
  are implemented, with no known functional gap identified in the linked guide.
  Applications must still select optional capabilities and supply the required
  equipment, execution and persistence providers.
- **Partial**: some model, protocol or tooling support exists, but an identified
  runtime, facet or validation gap remains. Generated declarations alone count
  as Partial, not a complete runtime implementation.
- **No**: no specification-specific implementation is shipped. Generic OPC UA
  browsing, NodeSet import or source generation can help an application implement
  the specification, but do not change this rating.

These are implementation ratings, not OPC Foundation certification results,
not a promise that every optional facet is enabled, and not an application's
`ServerProfileArray`. For concepts and guidance documents, the rating refers
to their application in the SDK rather than a separately executable protocol.
The [catalogue](#companion-and-extension-models) and linked guides identify
implementation evidence and provider boundaries. The final column records
specific gaps for Partial entries and implementation boundaries where they
clarify Full support; it does not repeat ordinary provider requirements.
The model-only core entries refer to declarations in the shipped
[core NodeSet](../src/Opc.Ua.Core/Schema/Opc.Ua.NodeSet.xml); their presence
does not establish executable management, serialization or logging methods.

| Specification | Latest version | Support | Implementation detail or partial gap |
| --- | --- | --- | --- |
| [OPC 10000-1: Overview and Concepts](https://reference.opcfoundation.org/specs/OPC-10000-1) | 1.05.06 | Full | - |
| [OPC 10000-2: Security Model](https://reference.opcfoundation.org/specs/OPC-10000-2) | 1.05.06 | Full | - |
| [OPC 10000-3: Address Space Model](https://reference.opcfoundation.org/specs/OPC-10000-3) | 1.05.06 | Full | - |
| [OPC 10000-4: Services](https://reference.opcfoundation.org/specs/OPC-10000-4) | 1.05.07 | Partial | Query client APIs exist; default server has no Query service implementation |
| [OPC 10000-5: Information Model](https://reference.opcfoundation.org/specs/OPC-10000-5) | 1.05.06 | Full | - |
| [OPC 10000-6: Mappings](https://reference.opcfoundation.org/specs/OPC-10000-6) | 1.05.07 | Full | - |
| [OPC 10000-7: Profiles](https://reference.opcfoundation.org/specs/OPC-10000-7) | 1.05.02 | Partial | Selected server/client/transport/security profiles, not the entire catalogue |
| [OPC 10000-8: Data Access](https://reference.opcfoundation.org/specs/OPC-10000-8) | 1.05.07 | Full | - |
| [OPC 10000-9: Alarms and Conditions](https://reference.opcfoundation.org/specs/OPC-10000-9) | 1.05.06 | Full | - |
| [OPC 10000-10: Programs](https://reference.opcfoundation.org/specs/OPC-10000-10) | 1.05.00 | Full | - |
| [OPC 10000-11: Historical Access](https://reference.opcfoundation.org/specs/OPC-10000-11) | 1.05.04 | Full | - |
| [OPC 10000-12: Discovery and Global Services](https://reference.opcfoundation.org/specs/OPC-10000-12) | 1.05.07 | Partial | GDS remote-application proxy incomplete; AuthorizationServices configuration folder absent |
| [OPC 10000-13: Aggregates](https://reference.opcfoundation.org/specs/OPC-10000-13) | 1.05.07 | Full | - |
| [OPC 10000-14: PubSub](https://reference.opcfoundation.org/specs/OPC-10000-14) | 1.05.06 | Partial | Curve25519/Curve448 DTLS profiles unavailable; other DTLS profiles depend on platform probes |
| [OPC 10000-15: Safety](https://reference.opcfoundation.org/specs/OPC-10000-15) | 1.05.04 | No | - |
| [OPC 10000-16: State Machines](https://reference.opcfoundation.org/specs/OPC-10000-16) | 1.05.04 | Full | - |
| [OPC 10000-17: Alias Names](https://reference.opcfoundation.org/specs/OPC-10000-17) | 1.05.07 | Full | - |
| [OPC 10000-18: Role-Based Security](https://reference.opcfoundation.org/specs/OPC-10000-18) | 1.05.06 | Full | - |
| [OPC 10000-19: Dictionary Reference](https://reference.opcfoundation.org/specs/OPC-10000-19) | 1.05.03 | Full | - |
| [OPC 10000-20: File Transfer](https://reference.opcfoundation.org/specs/OPC-10000-20) | 1.05.06 | Full | - |
| [OPC 10000-21: Device Onboarding](https://reference.opcfoundation.org/specs/OPC-10000-21) | 1.05.04 | Partial | Registrar tickets only; complete device-facing onboarding state machine missing |
| [OPC 10000-22: Base Network Model](https://reference.opcfoundation.org/specs/OPC-10000-22) | 1.05.04 | Full | Defined optional interface, mapping, TSN and LLDP capabilities are provider-backed; production adapters remain application-supplied |
| [OPC 10000-23: Common ReferenceTypes](https://reference.opcfoundation.org/specs/OPC-10000-23) | 1.05.02 | Full | - |
| [OPC 10000-24: Scheduler](https://reference.opcfoundation.org/specs/OPC-10000-24) | 1.05.02 | No | - |
| [OPC 10000-25: Object Serialization](https://reference.opcfoundation.org/specs/OPC-10000-25) | 1.05.06 | Partial | Generated serialization declarations; no complete ConfigureSerialization runtime |
| [OPC 10000-26: LogObject Model](https://reference.opcfoundation.org/specs/OPC-10000-26) | 1.05.07 | Partial | Generated LogObject/LogRecord declarations; no complete LogObject service runtime |
| [OPC 10000-80: UAFX Overview and Concepts](https://reference.opcfoundation.org/specs/OPC-10000-80) | 1.00.03 | No | - |
| [OPC 10000-81: UAFX Connecting Devices and Information Model](https://reference.opcfoundation.org/specs/OPC-10000-81) | 1.00.04 | No | - |
| [OPC 10000-82: UAFX Networking](https://reference.opcfoundation.org/specs/OPC-10000-82) | 1.00.03 | No | - |
| [OPC 10000-83: UAFX Offline Engineering](https://reference.opcfoundation.org/specs/OPC-10000-83) | 1.00.03 | No | - |
| [OPC 10000-84: UAFX Profiles](https://reference.opcfoundation.org/specs/OPC-10000-84) | 1.00.04 | No | - |
| [OPC 10000-100: Devices](https://reference.opcfoundation.org/specs/OPC-10000-100) | 1.05.0 | Partial | DI Offline, some software-loading modes and UpdateStatus missing |
| [OPC 10000-110: Asset Management Basics](https://reference.opcfoundation.org/specs/OPC-10000-110) | 1.01.1 | Partial | Asset-health tracking and past-maintenance event history missing |
| [OPC 10000-120: OPC UA and XML Data Type Mapping](https://reference.opcfoundation.org/specs/OPC-10000-120) | 1.00 | No | - |
| [OPC 10000-200: Industrial Automation](https://reference.opcfoundation.org/specs/OPC-10000-200) | 1.01.4 | Full | - |
| [OPC 10000-210: Relative Spatial Location](https://reference.opcfoundation.org/specs/OPC-10000-210) | 1.00.1 | Full | - |
| [OPC 10000-211: Global Positioning](https://reference.opcfoundation.org/specs/OPC-10000-211) | 1.0.0 | Full | - |
| [OPC 10030: ISA-95 Common Object Model](https://reference.opcfoundation.org/specs/OPC-10030) | 1.00 | Full | - |
| [OPC 10031-4: ISA-95 Job Control](https://reference.opcfoundation.org/specs/OPC-10031-4) | 2.00.0 | Full | - |
| [OPC 10100-1: WoT Connectivity API Definition](https://reference.opcfoundation.org/specs/OPC-10100-1) | 1.02 | Full | - |
| [OPC 10101: WoT Binding](https://reference.opcfoundation.org/specs/OPC-10101) | 1.00 | Partial | General UA Binary payload codec and complete TD/TM/Binding-result schema validation missing |
| [OPC 11030: UA Modelling Best Practices](https://reference.opcfoundation.org/specs/OPC-11030) | 1.03.03 | Partial | Selected model-authoring/generator checks; no complete best-practices validator |
| [OPC 30000: PLCs based on IEC 61131-3](https://reference.opcfoundation.org/specs/OPC-30000) | 1.02 | No | - |
| [OPC 30001: Client Function Blocks for IEC 61131-3](https://reference.opcfoundation.org/specs/OPC-30001) | 1.02 | No | - |
| [OPC 30010: AutoID Devices](https://reference.opcfoundation.org/specs/OPC-30010) | 1.01 | Partial | ScanActive control/RuntimeParameters missing; optical-verifier Scan has inconsistent upstream result type |
| [OPC 30020: MDIS](https://reference.opcfoundation.org/specs/OPC-30020) | 1.3 | No | - |
| [OPC 30030: BACnet](https://reference.opcfoundation.org/specs/OPC-30030) | 2.00.1 | No | - |
| [OPC 30040: AutomationML](https://reference.opcfoundation.org/specs/OPC-30040) | 1.00 | No | - |
| [OPC 30050: PackML](https://reference.opcfoundation.org/specs/OPC-30050) | 1.01 | Partial | Generated model and Scales integration; not a standalone all-unit-mode runtime |
| [OPC 30060: Tobacco Machine Communication](https://reference.opcfoundation.org/specs/OPC-30060) | 2.00.1 | No | - |
| [OPC 30081: PA-DIM](https://reference.opcfoundation.org/specs/OPC-30081) | 1.02.0 | Partial | Generated model and Machinery process values; no complete standalone PA-DIM runtime |
| [OPC 30090: Field Device Tool (FDT)](https://reference.opcfoundation.org/specs/OPC-30090) | 1.01 | No | - |
| [OPC 30100: SERCOS Devices](https://reference.opcfoundation.org/specs/OPC-30100) | 1.2 | No | - |
| [OPC 30110: POWERLINK](https://reference.opcfoundation.org/specs/OPC-30110) | 1.00 | No | - |
| [OPC 30120: IO-Link Devices and Masters](https://reference.opcfoundation.org/specs/OPC-30120) | 1.0 | No | - |
| [OPC 30130: Control and Communication System Profile for Machine](https://reference.opcfoundation.org/specs/OPC-30130) | 1.00 | No | - |
| [OPC 30140: PROFINET](https://reference.opcfoundation.org/specs/OPC-30140) | 1.00.1 | No | - |
| [OPC 30141: PROFIenergy](https://reference.opcfoundation.org/specs/OPC-30141) | 1.00 | No | - |
| [OPC 30142: PROFINET Remote IO](https://reference.opcfoundation.org/specs/OPC-30142) | 1.00.1 | No | - |
| [OPC 30143: PROFINET Encoder](https://reference.opcfoundation.org/specs/OPC-30143) | 1.00 | No | - |
| [OPC 30144: PROFINET GSD Generic Model](https://reference.opcfoundation.org/specs/OPC-30144) | 1.00 | No | - |
| [OPC 30145: PROFINET Drives](https://reference.opcfoundation.org/specs/OPC-30145) | 1.0.0 | No | - |
| [OPC 30200: Commercial Kitchen Equipment](https://reference.opcfoundation.org/specs/OPC-30200) | 1.00 | No | - |
| [OPC 30210-1: Unattended Retail and Coffee Solutions - Core Components](https://reference.opcfoundation.org/specs/OPC-30210-1) | 1.0.0 | No | - |
| [OPC 30250: DEXPI](https://reference.opcfoundation.org/specs/OPC-30250) | 1.0 | No | - |
| [OPC 30260: OPEN-SCS](https://reference.opcfoundation.org/specs/OPC-30260) | 1.02.03 | No | - |
| [OPC 30270: Asset Administration Shell (AAS)](https://reference.opcfoundation.org/specs/OPC-30270) | 1.00 | No | - |
| [OPC 30300: Generic Trust Anchor API Profile for OPC UA](https://reference.opcfoundation.org/specs/OPC-30300) | 1.00.0 | No | - |
| [OPC 30400-1: Cloud Library - Architecture and Use Cases](https://reference.opcfoundation.org/specs/OPC-30400-1) | 1.00.1 | No | - |
| [OPC 30400-2: Cloud Library - API Definition](https://reference.opcfoundation.org/specs/OPC-30400-2) | 1.00.0 | No | - |
| [OPC 30450-1: Digital Product Passport - Information Model](https://reference.opcfoundation.org/specs/OPC-30450-1) | 1.0.0 | No | - |
| [OPC 30500-1: Laboratory and Analytical Device Standard (LADS) - Basics](https://reference.opcfoundation.org/specs/OPC-30500-1) | 1.0.0 | Partial | Optional Hold/Unhold/Suspend/Unsuspend/Reset/ToComplete server methods and persistence missing |
| [OPC 34100: Energy Consumption Management](https://reference.opcfoundation.org/specs/OPC-34100) | 1.0.1 | Partial | Generated model and Machinery energy bindings; no complete standalone ECM runtime |
| [OPC 40001-1: Machinery - Basic Building Blocks](https://reference.opcfoundation.org/specs/OPC-40001-1) | 1.04.2 | Full | - |
| [OPC 40001-2: Machinery - Process Values](https://reference.opcfoundation.org/specs/OPC-40001-2) | 1.00 | Full | - |
| [OPC 40001-3: Machinery - Job Management](https://reference.opcfoundation.org/specs/OPC-40001-3) | 1.0.1 | Full | - |
| [OPC 40001-4: Machinery - Energy Management](https://reference.opcfoundation.org/specs/OPC-40001-4) | 1.00 | Full | - |
| [OPC 40001-101: Machinery - Result Transfer](https://reference.opcfoundation.org/specs/OPC-40001-101) | 1.01.0 | Full | - |
| [OPC 40010-1: Robotics - Vertical Integration](https://reference.opcfoundation.org/specs/OPC-40010-1) | 1.02 | Full | - |
| [OPC 40020-1: Cranes and Hoists - Base Model Motion Devices System](https://reference.opcfoundation.org/specs/OPC-40020-1) | 1.00 | Full | - |
| [OPC 40077: Injection Moulding Machines and MES](https://reference.opcfoundation.org/specs/OPC-40077) | 1.01 | No | - |
| [OPC 40082-1: Plastics/Rubber Peripheral Devices - Temperature Control](https://reference.opcfoundation.org/specs/OPC-40082-1) | 1.01 | No | - |
| [OPC 40082-2: Plastics/Rubber Peripheral Devices - Hot Runner](https://reference.opcfoundation.org/specs/OPC-40082-2) | 1.00 | No | - |
| [OPC 40082-3: Plastics/Rubber Peripheral Devices - LSR Dosing](https://reference.opcfoundation.org/specs/OPC-40082-3) | 1.02.0 | No | - |
| [OPC 40082-4: Plastics/Rubber Peripheral Devices - Dosing Systems](https://reference.opcfoundation.org/specs/OPC-40082-4) | 1.0.1 | No | - |
| [OPC 40082-6: Plastics/Rubber Peripheral Devices - Granulate Drying](https://reference.opcfoundation.org/specs/OPC-40082-6) | 1.0.0 | Full | - |
| [OPC 40083: Plastics and Rubber Machinery - General Type Definitions](https://reference.opcfoundation.org/specs/OPC-40083) | 1.03 | Full | - |
| [OPC 40084-1: Extrusion - General Type Definitions](https://reference.opcfoundation.org/specs/OPC-40084-1) | 2.00 | Full | - |
| [OPC 40084-2: Extrusion - Extrusion Line](https://reference.opcfoundation.org/specs/OPC-40084-2) | 2.00 | Full | - |
| [OPC 40084-3: Extrusion - Extruder](https://reference.opcfoundation.org/specs/OPC-40084-3) | 2.00 | Full | - |
| [OPC 40084-4: Extrusion - Haul-off](https://reference.opcfoundation.org/specs/OPC-40084-4) | 2.00 | Full | - |
| [OPC 40084-5: Extrusion - Melt Pump](https://reference.opcfoundation.org/specs/OPC-40084-5) | 2.00 | Full | - |
| [OPC 40084-6: Extrusion - Filter](https://reference.opcfoundation.org/specs/OPC-40084-6) | 2.00 | Partial | Filter profile withheld pending clarification of the published requirement for the Part 5 Melt Pump conformance unit |
| [OPC 40084-7: Extrusion - Die](https://reference.opcfoundation.org/specs/OPC-40084-7) | 2.00 | Full | - |
| [OPC 40084-8: Extrusion - Pelletizer](https://reference.opcfoundation.org/specs/OPC-40084-8) | 2.00 | Full | - |
| [OPC 40084-9: Extrusion - Cutter](https://reference.opcfoundation.org/specs/OPC-40084-9) | 2.00 | Full | - |
| [OPC 40084-10: Extrusion - Calibrator](https://reference.opcfoundation.org/specs/OPC-40084-10) | 2.00 | Full | - |
| [OPC 40084-11: Extrusion - Corrugator](https://reference.opcfoundation.org/specs/OPC-40084-11) | 2.00 | Full | - |
| [OPC 40084-12: Extrusion - Calender](https://reference.opcfoundation.org/specs/OPC-40084-12) | 2.00 | Full | - |
| [OPC 40092-1: Flexible PUR Foam - Cutting Machines](https://reference.opcfoundation.org/specs/OPC-40092-1) | 1.0.0 | No | - |
| [OPC 40100-1: Machine Vision - Control, Configuration, Recipe and Result Management](https://reference.opcfoundation.org/specs/OPC-40100-1) | 1.0 | Full | - |
| [OPC 40100-2: Machine Vision - Asset Management and Condition Monitoring](https://reference.opcfoundation.org/specs/OPC-40100-2) | 1.00.0 | Full | - |
| [OPC 40200: Weighing Technology](https://reference.opcfoundation.org/specs/OPC-40200) | 2.00 | Partial | Catchweigher/price-labeler/hopper controllers and server recipe sleep-threshold evaluation missing |
| [OPC 40210: Geometric Measurement Systems](https://reference.opcfoundation.org/specs/OPC-40210) | 1.0.1 | Partial | Production facet withheld: required GMS_ProductionStatisticsType undefined upstream |
| [OPC 40223: Pumps and Vacuum Pumps](https://reference.opcfoundation.org/specs/OPC-40223) | 1.0 | Full | Pump Base and Advanced profiles follow verified configured capabilities; Table 155's dependency uses the Table 153 Pump Base URI, while the CAS Base link remains a publication discrepancy |
| [OPC 40250-1: Compressed Air Systems - Main Control Systems](https://reference.opcfoundation.org/specs/OPC-40250-1) | 1.00.1 | No | - |
| [OPC 40301: Flat Glass Processing](https://reference.opcfoundation.org/specs/OPC-40301) | 2.0 | No | - |
| [OPC 40400-1: Powertrain - Asset Management](https://reference.opcfoundation.org/specs/OPC-40400-1) | 1.0 | No | - |
| [OPC 40444: Textile Testing Devices](https://reference.opcfoundation.org/specs/OPC-40444) | 1.0.0 | No | - |
| [OPC 40450-1: Joining Systems - Base](https://reference.opcfoundation.org/specs/OPC-40450-1) | 1.01.0 | No | - |
| [OPC 40451-1: Tightening Systems - General](https://reference.opcfoundation.org/specs/OPC-40451-1) | 2.00.1 | No | - |
| [OPC 40501-1: Machine Tools - Machine Monitoring and Job Management](https://reference.opcfoundation.org/specs/OPC-40501-1) | 1.02.0 | Full | - |
| [OPC 40502: Computerized Numerical Control (CNC) Systems](https://reference.opcfoundation.org/specs/OPC-40502) | 1.0 | No | - |
| [OPC 40503-1: Metal Forming - General](https://reference.opcfoundation.org/specs/OPC-40503-1) | 1.0.0 | No | - |
| [OPC 40504-1: Cutting Tools - Manufacturing](https://reference.opcfoundation.org/specs/OPC-40504-1) | 1.0.0 | No | - |
| [OPC 40505: Wireless Machine Tool Peripherals](https://reference.opcfoundation.org/specs/OPC-40505) | 1.0.0 | Full | - |
| [OPC 40530: Laser Systems](https://reference.opcfoundation.org/specs/OPC-40530) | 1.0.0 | No | - |
| [OPC 40540: Additive Manufacturing](https://reference.opcfoundation.org/specs/OPC-40540) | 1.0.0 | No | - |
| [OPC 40550-1: Woodworking Machines - Vertical Interface](https://reference.opcfoundation.org/specs/OPC-40550-1) | 1.02.0 | No | - |
| [OPC 40560: Mining - General](https://reference.opcfoundation.org/specs/OPC-40560) | 1.01.0 | No | - |
| [OPC 40561-1: Mining - Extraction General](https://reference.opcfoundation.org/specs/OPC-40561-1) | 1.0 | No | - |
| [OPC 40561-2: Mining - Shearer Loader](https://reference.opcfoundation.org/specs/OPC-40561-2) | 1.0 | No | - |
| [OPC 40562-1: Mining - Loading General](https://reference.opcfoundation.org/specs/OPC-40562-1) | 1.0 | No | - |
| [OPC 40562-2: Mining - Hydraulic Excavator](https://reference.opcfoundation.org/specs/OPC-40562-2) | 1.0 | No | - |
| [OPC 40563-1: Mining - Transport Dumping General](https://reference.opcfoundation.org/specs/OPC-40563-1) | 1.0 | No | - |
| [OPC 40563-2: Mining - Armoured Face Conveyor](https://reference.opcfoundation.org/specs/OPC-40563-2) | 1.0 | No | - |
| [OPC 40563-3: Mining - Rear Dump Truck](https://reference.opcfoundation.org/specs/OPC-40563-3) | 1.0 | No | - |
| [OPC 40563-4: Mining - Conveying](https://reference.opcfoundation.org/specs/OPC-40563-4) | 1.0.0 | No | - |
| [OPC 40564-1: Mining - Mineral Processing General](https://reference.opcfoundation.org/specs/OPC-40564-1) | 1.0 | No | - |
| [OPC 40564-2: Mining - Rock Crusher](https://reference.opcfoundation.org/specs/OPC-40564-2) | 1.0 | No | - |
| [OPC 40565-1: Mining - Development Support General](https://reference.opcfoundation.org/specs/OPC-40565-1) | 1.0 | No | - |
| [OPC 40565-2: Mining - Roof Support](https://reference.opcfoundation.org/specs/OPC-40565-2) | 1.0 | No | - |
| [OPC 40565-3: Mining - Dozer](https://reference.opcfoundation.org/specs/OPC-40565-3) | 1.0 | No | - |
| [OPC 40566-1: Mining - Monitoring and Supervision General](https://reference.opcfoundation.org/specs/OPC-40566-1) | 1.0 | No | - |
| [OPC 40567-1: Mining - PELO Services General](https://reference.opcfoundation.org/specs/OPC-40567-1) | 1.0 | No | - |
| [OPC 40567-2: Mining - Face Alignment System](https://reference.opcfoundation.org/specs/OPC-40567-2) | 1.0 | No | - |
| [OPC 40568-1: Mining - External Standards, IREDES](https://reference.opcfoundation.org/specs/OPC-40568-1) | 1.0.0 | No | - |
| [OPC 40569-1: Mining - Longwall Operation](https://reference.opcfoundation.org/specs/OPC-40569-1) | 1.0 | No | - |
| [OPC 40569-2: Mining - Discontinuous Loading](https://reference.opcfoundation.org/specs/OPC-40569-2) | 1.0 | No | - |
| [OPC 40569-3: Mining - Discontinuous Unloading](https://reference.opcfoundation.org/specs/OPC-40569-3) | 1.0 | No | - |
| [OPC 40569-4: Mining - Dozing](https://reference.opcfoundation.org/specs/OPC-40569-4) | 1.0 | No | - |
| [OPC 40570: Wire Harness Manufacturing](https://reference.opcfoundation.org/specs/OPC-40570) | 1.0.0 | No | - |
| [OPC 40600: Weihenstephan Standards](https://reference.opcfoundation.org/specs/OPC-40600) | 1.00.0 | No | - |
| [OPC 40700: Surface Technology - General Types](https://reference.opcfoundation.org/specs/OPC-40700) | 1.0 | No | - |
| [OPC 40701: Surface Technology - Cleaning and Pretreatment](https://reference.opcfoundation.org/specs/OPC-40701) | 1.0.0 | No | - |
| [OPC 40702: Surface Technology - Organic Coating Material Supply](https://reference.opcfoundation.org/specs/OPC-40702) | 1.0.0 | No | - |
| [OPC 40718: Surface Technology - Shot Blasting Machinery](https://reference.opcfoundation.org/specs/OPC-40718) | 1.0.0 | No | - |
| [OPC 40719: Plasma Surface Technology](https://reference.opcfoundation.org/specs/OPC-40719) | 1.0.0 | No | - |
| [OPC 40740: Process Air Extraction and Filtration Systems](https://reference.opcfoundation.org/specs/OPC-40740) | 1.0.1 | No | - |
