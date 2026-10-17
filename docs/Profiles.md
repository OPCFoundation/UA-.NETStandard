# OPC UA Profiles and Facets Support

This document describes which [OPC UA Profiles and Facets](https://profiles.opcfoundation.org/) are implemented in the OPC UA .NET Standard Stack, and where in the codebase / documentation each one lives.

## Overview

The OPC UA .NET Standard Stack is a reference implementation that targets
**OPC UA specification version 1.05.07**. Historical reference-server
certification does not certify every current SDK feature, companion model or
application. Use the implementation and provider limits below, the exact
product's advertised capabilities, and its version-specific certification scope.

The SDK ships implementations and helpers for:

- Part 1 (Overview and Concepts)
- Part 2 (Security Model)
- Part 3 (Address Space Model)
- Part 4 (Services)
- Part 5 (Information Model)
- Part 6 (Mappings)
- Part 7 (Profiles)
- Part 8 (Data Access)
- Part 9 (Alarms & Conditions)
- [Part 10 (Programs)](Programs.md)
- Part 11 (Historical Access)
- Part 12 (Discovery and Global Services, including LDS/GDS)
- Part 13 (Aggregates)
- Part 14 (PubSub)
- Part 16 (State Machines)
- Part 17 (Alias Names)
- Part 18 (Role Management)
- Part 20 (File Transfer)
- Part 21 registrar ticket administration (not a complete device-onboarding state machine)
- Part 100 (Device Integration / Software Update)
- OPC 10100-1 (WoT Connectivity)
- OPC 10000-200 (Industrial Automation)
- OPC 10000-110 (Asset Management Basics)
- OPC 40001-1/-2/-3/-4/-101 (Machinery)
- OPC 40010-1 (Robotics)
- OPC 40100-1/-2 (Machine Vision)
- OPC 40200 (Weighing Technology), OPC 40223 (Pumps and Vacuum Pumps)
- OPC 40501-1 (MachineTool), with OPC 40505 (Wireless Machine Tool Peripherals)
- OPC 40210 (Geometric Measurement Systems), with the Production-facet exception below
- OPC 40020-1 (Cranes and Hoists)

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
  - Query services

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
- **WoT Connectivity** (OPC 10100-1) — `Opc.Ua.WotCon` /
  `Opc.Ua.WotCon.Server` / `Opc.Ua.WotCon.Client` library trio for
  surfacing OPC UA servers as Web-of-Things Thing Descriptions. See
  [WoT Connectivity](WoTConnectivity.md).
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

This is the SDK implementation catalogue, not a list of facets enabled in every
server. Generated model coverage, provider-backed runtime behavior, advertised
conformance and external certification are different claims. Use each guide's
limitations and the actual configured instance before claiming a facet.

| Model or family | Implemented support | Conditions and limits |
| --- | --- | --- |
| [Device Integration](DeviceIntegration.md), OPC 10000-100 | Device composition, locking and software-update server/client interfaces | Equipment and storage behavior use application providers |
| [Asset Management Basics](AMB.md), OPC 10000-110 | Asset discovery/identity, health, maintenance, documentation and location | Sidecar registry composes with the asset's existing namespace owner |
| [Industrial Automation](Robotics.md), OPC 10000-200 | Shared generated IA model used by companion builders | Loading the model does not by itself implement every IA facet |
| [Machinery](Machinery.md), OPC 40001-1/-2/-3/-4/-101 | Building blocks, process values, jobs, energy and result-transfer runtime/client interfaces | Select capabilities and providers; durable result storage is application-supplied |
| [MachineTool and WMTP](MachineTool.md), OPC 40501-1 and OPC 40505 | Native monitoring, tools, retained/modern production, files, peripheral measurements/configuration/records and typed clients/MCP | WMTP is bundled but independently modeled; no included Bluetooth driver or physical CNC executor |
| [GMS](GMS.md), OPC 40210 | Native systems, sensors/qualification, calibration observations, jobs/parts and Machinery-backed results | Full GMS Production facet is withheld because its required `GMS_ProductionStatisticsType` is undefined upstream; no invented acquisition/calibration-control methods |
| [Cranes and Hoists](CranesHoists.md), OPC 40020-1 | Robotics-derived topology, load/protection observations and provider-authorized limit requests | Preview-only packages; not a motion or safety controller |
| [Pumps](Pumps.md), OPC 40223 | Released model, fluent device builders and typed clients | Actual equipment observations and effects require providers |
| [Scales](Scales.md), OPC 40200 | Weighing, zero/tare, products, statistics, recipes, PackML and typed clients | Stock runtime withholds Catchweigher, AutomaticWeightPriceLabeler and TotalizingHopper kind facets; application recipe execution/parser remains explicit |
| [ISA-95](ISA95.md), OPC 10030 and OPC 10031-4 | Common resource model and Job Control V1/V2 providers, server bindings and clients | Providers supply actual execution, catalogs and responses |
| [Positioning](Positioning.md), OPC 10000-210/-211 | Released RSL/GPOS models, spatial lists, model changes, position/location/zones interfaces | Positioning packages remain preview-only; do not infer GNSS hardware support |
| [Robotics](Robotics.md), OPC 40010-1 | Released motion-device topology and supported controller/program interfaces | Preview-only package policy is independent from the released Robotics model |
| [Machine Vision](Vision.md), OPC 40100-1/-2 | Released control/configuration/recipe/result and asset/condition-monitoring interfaces, clients and MCP | Providers supply acquisition and equipment behavior; separate from Perception |
| [Perception](Vision.md#perception-model) and [Robot Intent](Robotics.md#robot-intent) | Perception/media/inference/feedback and robot task-intent implementations | Draft models with provisional identifiers, not released-companion conformance claims |
| [AI](AI.md), [xRegistry](XRegistry.md), [OpenUSD](OpenUsd.md) | Model/deployment/inference services, registry/version/federation and scene/address-space integration | Draft extension models; preview-only families, with provider/backend-specific limits |
| [WoT Connectivity](WoTConnectivity.md) | OPC 10100-1 interfaces and WoT conversion/binding implementations | Preview package policy; registry extensions and transport-specific bindings have separate scope |
| [PA-DIM/ECM](Machinery.md) and [PackML](Scales.md) | Shared generated dependency models and the behavior bound by Machinery/Scales | Not a blanket standalone device or all-facet implementation claim |

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
- **Alarms and Conditions** — Typed `AlarmClient` event records, fluent
  `AlarmEventFilterBuilder`, and `IAsyncEnumerable` alarm streaming via
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
abstractions, and a first-class `ByteString` type. See the
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
Only advertise a facet that the application genuinely implements — the
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
- [Role-Based User Management](RoleBasedUserManagement.md) (Part 18)
- [Identity Providers](IdentityProviders.md)
- [Authorization Service](AuthorizationService.md) (Part 12 §9)
- [Key Credential Service](KeyCredentialService.md) (Part 12 §8)
- [GDS Developer Guide](GDS.md) (Part 12 implemented, partial and provider-gated areas)
- [File System Client](FileSystemClient.md) (Part 20)
- [Device Integration](DeviceIntegration.md) (Part 100)
- [Relative Spatial Location and Global Positioning](Positioning.md)
  (Parts 210 and 211)
- [Robotics](Robotics.md) (OPC 40010-1 over OPC 10000-200)
- [Software Update](SoftwareUpdate.md)
- [WoT Connectivity](WoTConnectivity.md) (OPC 10100-1)
- [Node Management](NodeManagement.md) (Part 4)
- [Asset Management Basics](AMB.md) (OPC 10000-110)
- [Machinery](Machinery.md) (OPC 40001 series, PA-DIM and ECM)
- [Pumps](Pumps.md) (OPC 40223)
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

## References

- [OPC Foundation Profile Reporting](https://profiles.opcfoundation.org/) — canonical profile and facet URI registry.
- [OPC UA Specification](https://reference.opcfoundation.org/) — online reference for the OPC 10000 series.
- [OPC UA Compliance Test Tool (CTT)](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/) — official conformance test tool.
