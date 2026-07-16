# KeyCredentialService Developer Guide

OPC 10000-12 §8 defines the **KeyCredentialService** — a mechanism for
a GDS to issue and manage credentials (e.g. username/password, API keys,
tokens) on behalf of applications that need to authenticate to
non-OPC UA services such as MQTT brokers or REST APIs.

> **Scope note**: this service is for credentials targeting **non-OPC
> UA** resources (MQTT brokers, REST APIs, etc. — see `ResourceUri`
> + `ProfileUris` on `KeyCredentialServiceType`). The optional bridge
> authenticator described below is **EXPERIMENTAL** under the vendor URI
> `urn:opcfoundation:netstandard:profile:authentication:keycredential`;
> it is **not** an OPC UA Part 6 §6.5.3 conformance claim. For
> standards-conformant OPC UA session authentication, use
> [AuthorizationService](AuthorizationService.md) (JWT) and the
> [Identity Providers](IdentityProviders.md) infrastructure.

## Architecture

```
┌─────────────────┐     StartRequest      ┌────────────────────────┐
│  OPC UA Client   │ ──────────────────► │  ApplicationsNodeManager │
│  (or application)│ ◄────────────────── │  (GDS Server)            │
│                  │     FinishRequest    │                          │
│                  │ ──────────────────► │  ┌──────────────────────┐ │
│                  │     Revoke          │  │ IKeyCredentialRequest │ │
└─────────────────┘                      │  │       Store          │ │
                                         │  └──────────┬───────────┘ │
                                         │             │             │
                                         │  ┌──────────▼───────────┐ │
                                         │  │     ISecretStore     │ │
                                         │  └──────────────────────┘ │
                                         └────────────────────────────┘
```

## Client API

### KeyCredentialServiceClient

A thin client proxy for the three KeyCredentialService methods. Requires
an active `ISession` and the `NodeId` of the KeyCredentialService
instance in the server's address space.

```csharp
using Opc.Ua.Gds.Client;

// After connecting a session to the GDS server:
var kcClient = new KeyCredentialServiceClient(session, serviceNodeId);

// 1. Request a new credential
NodeId requestId = await kcClient.StartRequestAsync(
    applicationUri: "urn:my-app",
    publicKey: myPublicKey,
    securityPolicyUri: null,         // server chooses
    requestedRoles: default);        // server chooses

// 2. Finish (collect) the credential
var (credentialId, credentialSecret, thumbprint, policyUri, grantedRoles)
    = await kcClient.FinishRequestAsync(requestId, cancelRequest: false);

// Use credentialId + credentialSecret to authenticate to the
// target service (MQTT broker, REST API, etc.)

// 3. Revoke when no longer needed
await kcClient.RevokeAsync(credentialId);
```

### Discovering the Service NodeId

The KeyCredentialService instance is typically found under the
server's `Directory` or `ServerConfiguration` node. Browse for
objects of type `KeyCredentialServiceType` (namespace 0):

```csharp
// Browse the server for KeyCredentialServiceType instances
var browser = new Browser(session);
// ... or use the well-known NodeId if your GDS exposes one.
```

## Server-Side: Implementing a Provider

### IKeyCredentialRequestStore

The server-side abstraction for the credential lifecycle:

```csharp
public interface IKeyCredentialRequestStore
{
    // Begin a new credential request. Returns a request ID.
    NodeId StartRequest(
        string applicationUri,
        ByteString publicKey,
        string? securityPolicyUri,
        ArrayOf<NodeId> requestedRoles);

    // Complete or cancel a request. Returns issued credential data.
    KeyCredentialRequestState FinishRequest(
        NodeId requestId,
        bool cancelRequest,
        out string? credentialId,
        out ByteString credentialSecret,
        out string? certificateThumbprint,
        out string? securityPolicyUri,
        out ArrayOf<NodeId> grantedRoles);

    // Revoke a previously issued credential.
    void Revoke(string credentialId);
}
```

### Built-in Implementation

`InMemoryKeyCredentialRequestStore` provides a ready-to-use
in-process implementation. It auto-approves requests and generates
random 32-byte secrets.

**With ISecretStore integration** — credential secrets are persisted
through any `ISecretStore` backend (Key Vault, DPAPI, etc.):

```csharp
// Use the default in-memory secret store
var store = new InMemoryKeyCredentialRequestStore();

// Or plug in a production secret store
ISecretStore vault = new MyKeyVaultSecretStore("KeyCredential");
var store = new InMemoryKeyCredentialRequestStore(vault);
```

### Writing a Custom Implementation

For production systems with approval workflows:

```csharp
public class DatabaseKeyCredentialStore : IKeyCredentialRequestStore
{
    private readonly IDbConnection _db;

    public NodeId StartRequest(
        string applicationUri,
        ByteString publicKey,
        string? securityPolicyUri,
        ArrayOf<NodeId> requestedRoles)
    {
        // 1. Insert a pending request into the database
        // 2. Return the generated request ID
        // 3. Optionally notify an admin for approval
        var id = _db.InsertPendingRequest(applicationUri, publicKey);
        return new NodeId(id);
    }

    public KeyCredentialRequestState FinishRequest(
        NodeId requestId,
        bool cancelRequest,
        out string? credentialId, ...)
    {
        var record = _db.GetRequest(requestId);
        if (record.State == "Pending")
        {
            // Still awaiting approval
            credentialId = null;
            // ... set other out params to defaults
            return KeyCredentialRequestState.New;
        }
        // Return the approved credential
        credentialId = record.CredentialId;
        // ...
        return KeyCredentialRequestState.Completed;
    }

    public void Revoke(string credentialId)
    {
        _db.RevokeCredential(credentialId);
        // Optionally notify the target service to invalidate
    }
}
```

### Wiring into the GDS Server

Set the `KeyCredentialRequestStore` property on the
`ApplicationsNodeManager` (done via `GlobalDiscoverySampleServer`
or a custom server):

```csharp
// In your INodeManagerFactory or server setup:
var appNodeManager = new ApplicationsNodeManager(server, configuration, ...);
appNodeManager.KeyCredentialRequestStore =
    new InMemoryKeyCredentialRequestStore(mySecretStore);
```

## Decision matrix: Push vs Pull vs Bridge

| Model | Primary actor | Use when | Standard / conformance posture |
|---|---|---|---|
| Pull (`KeyCredentialServiceType`) | Client/application asks a GDS to issue a credential. | The GDS owns approval and issuance; the caller later retrieves the issued secret with `FinishRequest`. | OPC 10000-12 §8 KeyCredentialService. |
| Push (`KeyCredentialConfigurationFolderType`) | Administrator or provisioning agent writes credentials to a resource server. | A resource server must store, rotate, or delete credentials locally through `CreateCredential`, `UpdateCredential`, and `DeleteCredential`. | OPC 10000-12 §8 Push model; enabled with `WithKeyCredentialPush()`. |
| Bridge (`KeyCredentialBridgeAuthenticator`) | OPC UA client presents a KeyCredential proof as an issued identity token. | Closed deployments need an interim bridge between GDS-issued secrets and UA session authentication. | **Experimental vendor extension only; not a Part 6 §6.5.3 conformance claim.** |

### Hybrid (Push + Pull)

- Enable both models when a GDS issues credentials (Pull) and hosts the Push subject. Admin tools can push
  freshly issued credentials to target resource servers in one round-trip.
- For wiring in the same process, use `services.AddOpcUa().AddGdsServer(...).WithKeyCredentialPush()`.
  The Pull side stays on the GDS, and the Push side is enabled on the same node so admin clients write
  credentials directly to the GDS's resource-server view.
- For wiring across processes, the GDS uses the default Pull path (`InMemoryKeyCredentialRequestStore`),
  while each resource server enables Push with `WithKeyCredentialPush()`. The admin tool reads from the
  GDS and writes to each resource server.
- The bridge authenticator is independent of Push/Pull: it lets the resource server accept KeyCredential
  blobs as a UA identity. The hybrid model addresses issuance and distribution; the bridge addresses
  consumption.

## Resource Server Push Binding

A regular OPC UA resource server can opt in to the Part 12 §8 Push model
with `WithKeyCredentialPush()`. The binding mounts on the standard
`ServerConfiguration/KeyCredentialConfiguration` folder by default and
stores secrets through `IKeyCredentialStore` (default:
`InMemoryKeyCredentialStore` backed by `ISecretStore`).

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Server.Hosting;

services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationUri = "urn:my-resource-server";
        options.ProductUri = "uri:my-company:resource-server";
        options.EndpointUrls.Add("opc.tcp://localhost:4840");
    })
    .WithKeyCredentialPush();
```

Production deployments should register an `IKeyCredentialStore` backed by
a durable secret store before calling `WithKeyCredentialPush()`.

## Experimental KeyCredential Issued-Token Bridge

> **WARNING — EXPERIMENTAL**: `KeyCredentialBridgeAuthenticator` is a
> vendor extension under
> `urn:opcfoundation:netstandard:profile:authentication:keycredential`.
> It is **not** a Part 6 §6.5.3 conformance claim. Deployments that
> claim OPC UA conformance **MUST NOT** use this bridge as their
> standards-conformant user-token mechanism. Use it only in closed
> deployments where the GDS, resource server, and clients are controlled
> by the same operator.

The bridge lets a server validate a JSON issued-token payload containing
a `credentialId`, nonce timestamp, and HMAC proof. The server looks up
the credential in `IKeyCredentialStore`, validates the proof with the
stored secret, and returns an identity populated from the credential's
subject claims.

Client-side, `GdsKeyCredentialAccessTokenProvider` adapts a
`KeyCredentialServiceClient` to `IAccessTokenProvider`, so the standard
`IssuedTokenIdentityProvider` can materialize a UA `IssuedIdentityToken`
for the bridge profile.

## Audit Events

All KeyCredentialService operations emit audit events automatically:

| Operation | Audit Event Type |
|-----------|-----------------|
| StartRequest | `KeyCredentialRequestedAuditEventType` |
| FinishRequest | `KeyCredentialDeliveredAuditEventType` |
| Revoke | `KeyCredentialRevokedAuditEventType` |

Credential secrets are **never** included in audit payloads.

## End-to-End Example

```csharp
// ── Server Side ──────────────────────────────────────

// 1. Create the credential store
var secretStore = new InMemorySecretStore("KeyCredentials");
var kcStore = new InMemoryKeyCredentialRequestStore(secretStore);

// 2. Create the GDS server with credential support
var gdsServer = new GlobalDiscoverySampleServer(
    database, requestStore, certGroup, userDb, telemetry);

// 3. After server starts, wire the store
// (done automatically by ApplicationsNodeManager when
//  KeyCredentialRequestStore is set before CreateAddressSpace)

// ── Client Side ──────────────────────────────────────

// 1. Connect to the GDS
var gdsClient = new GlobalDiscoveryServerClient(config);
await gdsClient.ConnectAsync(gdsEndpointUrl);

// 2. Find the KeyCredentialService node
// (browse or use a well-known NodeId)

// 3. Create the client proxy
var kcClient = new KeyCredentialServiceClient(
    gdsClient.Session, serviceNodeId);

// 4. Request → Finish → Use → Revoke
NodeId reqId = await kcClient.StartRequestAsync(
    "urn:my-mqtt-bridge", default, null, default);

var (credId, secret, _, _, _) =
    await kcClient.FinishRequestAsync(reqId, false);

// Use credId/secret to connect to MQTT broker...

// When done:
await kcClient.RevokeAsync(credId);
```
