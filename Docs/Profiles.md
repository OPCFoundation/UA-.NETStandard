# OPC UA Profiles and Facets Support

This document describes which [OPC UA Profiles and Facets](https://profiles.opcfoundation.org/) are implemented in the OPC UA .NET Standard Stack, and where in the codebase / documentation each one lives.

## Overview

The OPC UA .NET Standard Stack is a reference implementation that targets
**OPC UA specification version 1.05.07**. The stack has been certified for
compliance through an OPC Foundation Certification Test Lab and is
continuously tested for compliance using the latest Compliance Test Tool (CTT).

Version 2.0 substantially extends companion-spec coverage over the
previous 1.5.378 version. The stack now
ships full server- and client-side support for: Part 9 (Alarms &
Conditions), Part 11 (Historical Access) + Part 13 (Aggregates), Part 16
(State Machines), Part 17 (Alias Names), Part 18 (Role Management), Part 20
(File Transfer), Part 100 (Device Integration / Software Update), OPC
10100-1 (WoT Connectivity), and the Local Discovery Server. See
[What's New in 2.0](WhatsNewIn2.0.md) for the broader change narrative.

The canonical list of all OPC UA profile and facet URIs is maintained by the
OPC Foundation at <https://profiles.opcfoundation.org/>. Where this document
hyperlinks a URI, that URI is the same string the reference server
advertises in `ServerProfileArray`; URIs not yet present in a shipping
config are referred to *by name* and the reader should consult the OPC
Foundation registry for the canonical URI form.

## Server Profiles

The reference server (`Applications/ConsoleReferenceServer`) advertises the
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

### Local Discovery Server (LDS) Profile

The `Opc.Ua.Lds.Server` library plus the `ConsoleLdsServer` reference
application implement the Local Discovery Server. The LDS application
advertises the **[Local Discovery Server 2017 Facet](http://opcfoundation.org/UA-Profile/Server/LocalDiscovery2017)**
(see `Applications/ConsoleLdsServer/Lds.Server.Config.xml`).

### Additional facets supported by the implementation (beyond the default advertised set)

Beyond the five facets advertised by default in the reference server's
`ServerProfileArray`, the master branch implements the following facets in
the SDK. They can be enabled per-application by registering the
corresponding NodeManager and, where applicable, adding the matching URI to
`ServerProfileArray` (consult <https://profiles.opcfoundation.org/> for the
canonical URI string before claiming a facet):

- **Historical Access** (Part 11) — `IHistorianProvider` provider model in
  `Libraries/Opc.Ua.Server/Historian/`, with a `InMemoryHistorianProvider`
  enabled by the reference server (`ReferenceNodeManager.cs`). Covers raw,
  modified, at-time, processed (aggregate), and annotation reads / updates.
  See [Historical Access](HistoricalAccess.md).
- **Aggregates** (Part 13) — `AggregateManager` and the
  `AggregateCalculator` family in `Libraries/Opc.Ua.Server/Aggregates/`.
  All **37 standard aggregate functions** of v1.05.07 are implemented;
  servers can additionally push down aggregation by implementing
  `IHistorianProcessedProvider`. See [Aggregates](Aggregates.md).
- **Alarms and Conditions** (Part 9) — Full server-side implementation
  with latched / silenced / out-of-service alarms, alarm groups, a
  suppression engine, and rate metrics. The reference server's
  `AlarmNodeManager` exposes a working sample. See
  [Alarms and Conditions](AlarmsAndConditions.md).
- **State Machine** (Part 16) — `StateMachineBuilder` and the
  `FluentFiniteStateMachineState` extensibility in
  `Libraries/Opc.Ua.Server/StateMachines/`. The reference server's
  `BoilerStateMachineState` is an end-to-end sample. See
  [State Machines](StateMachines.md).
- **File Access** (Part 20) — Server-side `FileSystemNodeManager` +
  `IFileSystemProvider` in `Libraries/Opc.Ua.Server/FileSystem/`. The
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
- **Transfer Subscriptions** — Subscription transfer between servers;
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
  raw, modified, at-time, processed (aggregate), and annotation reads /
  updates; see [Historical Access](HistoricalAccess.md).
- **State Machines** — Streaming and read helpers
  (`GetCurrentFiniteStateAsync`, `ObserveFiniteTransitionsAsync`,
  `WaitForStateAsync`) on the source-generated `*TypeClient` proxies;
  see [State Machines](StateMachines.md).
- **NodeSet Export** — Extract a server's address space to NodeSet2 XML;
  see [NodeSet Export](NodeSetExport.md).
- **Source-generated typed proxies** — `*TypeClient` proxies for
  ObjectTypes inside loaded models give strongly-typed method-call
  signatures; see [Source-Generated NodeManagers](SourceGeneratedNodeManagers.md).
- **Complex types** — Decode and consume server-defined structures and
  enumerations on the client; see [Complex Types](ComplexTypes.md).

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

- **[WebSocket Secure (UA Binary)](http://opcfoundation.org/UA-Profile/Transport/uawss-uasc-uabinary)** (`opc.wss://` and `wss://`) - UA Binary + UASC over secure WebSockets (OPC UA Part 6 §7.5.2, sub-protocol `opcua+uacp`)
  - Same UASC SecureChannel pipeline as `opc.tcp` carried over WebSocket binary frames (one frame per MessageChunk)
  - Supports all security modes (None / Sign / SignAndEncrypt)
  - TLS/SSL encryption at the WebSocket layer

- **WebSocket Secure (JSON)** (`opc.wss://` and `wss://`) - OPC UA JSON over secure WebSockets (Part 6 §7.5.2, sub-protocol `opcua+uajson`)
  - Compact JSON encoding per WebSocket text frame
  - TLS/SSL encryption only — no UA SecureChannel layer
  - Restricted to `MessageSecurityMode.None`

### PubSub transports

The [PubSub library](PubSub.md) supports the following PubSub transport
facets (URIs surfaced by `Profiles.PubSub*Transport` constants in
`Stack/Opc.Ua.Core/Security/Constants/SecurityConstants.cs`):

- **[PubSub UDP UADP](http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp)** — UDP transport with UADP message encoding.
- **[PubSub MQTT UADP](http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-uadp)** — MQTT transport with UADP message encoding.
- **[PubSub MQTT JSON](http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-json)** — MQTT transport with JSON message encoding.

PubSub additionally supports certificate-based MQTT authentication and
considers `WriterGroup`s in MQTT keep-alive calculations.

All transport profiles defined in OPC UA Part 6 §7.4 (HTTPS) and §7.5
(WebSockets) are supported. The `opcua+openapi` and
`opcua+openapi+<accesstoken>` WebSocket sub-protocols (Part 6 §7.5.2
Table 81) are tracked in
[`plans/25-wss-openapi-subprotocols.md`](../plans/25-wss-openapi-subprotocols.md).

## Security Profiles

The stack supports the following security profiles for secure
communication. The canonical set is defined in
`Stack/Opc.Ua.Core/Security/Constants/SecurityPolicies.cs`.

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

**Platform requirements for ECC.** ECC support is available on .NET
Framework 4.8, .NET Standard 2.1, and .NET 5.0 or later. Modern curves
(Curve25519, Curve448) and AEAD ciphers (AES-GCM, ChaCha20-Poly1305)
require .NET 8.0 or later (`AesGcm.IsSupported` /
`ChaCha20Poly1305.IsSupported` guard the runtime registration). Not all
curves are supported by every OS platform and .NET implementation.

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

The stack ships a Global Discovery Server implementation that is
**full OPC UA Part 12 compliance**, including:

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

See the [GDS Developer Guide](GDS.md) for the full feature breakdown and
hosting integration. The Local Discovery Server is a separate library
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
- **Certification:** The reference server has been certified for
  compliance through an OPC Foundation Certification Test Lab.
- **Testing:** All releases are verified for compliance using the latest
  Compliance Test Tool (CTT).

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

See the [reference server configuration file](../Applications/ConsoleReferenceServer/Quickstarts.ReferenceServer.Config.xml)
for a complete example, or the
[CTT configuration file](../Applications/ConsoleReferenceServer/Ctt.ReferenceServer.Config.xml)
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
- [GDS Developer Guide](GDS.md) (Part 12 full compliance)
- [File System Client](FileSystemClient.md) (Part 20)
- [Device Integration](DeviceIntegration.md) (Part 100)
- [Software Update](SoftwareUpdate.md)
- [WoT Connectivity](WoTConnectivity.md) (OPC 10100-1)
- [Node Management](NodeManagement.md) (Part 4)

## References

- [OPC Foundation Profile Reporting](https://profiles.opcfoundation.org/) — canonical profile and facet URI registry.
- [OPC UA Specification](https://reference.opcfoundation.org/) — online reference for the OPC 10000 series.
- [OPC UA Compliance Test Tool (CTT)](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/) — official conformance test tool.
