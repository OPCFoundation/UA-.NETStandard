# OPC UA Endpoint Registry Group Federation Server

This optional package hosts read-only Group references on an **existing**
Endpoint Registry. It composes the verified OPC UA and HTTPS federation providers
with `EndpointRegistryNodeManager`; it does not add another registry, store,
epoch producer, PubSub runtime, transport or conformance claim.

## Direct construction

Construct the binding after the registry manager has created its address space.
The source selection is server configuration, never a client-supplied assertion
of trust. The local Xid is independently allocated and must use the configured
reserved identifier prefix, in the same collection as the remote Group.

```csharp
var options = new EndpointRegistryGroupFederationOptions
{
    IdentifierPrefix = "federated-",
    Sources =
    [
        new GroupFederationSource(
            "/messagegroups/federated-orders",
            "/messagegroups/orders",
            trust,
            ct => uaProvider.PreloadGroupAsync(
                "/messagegroups/orders", cancellationToken: ct))
    ]
};

await using var binding = new EndpointRegistryGroupFederationBinding(
    registryManager, options);
await binding.StartAsync();
// Startup restores pins only. Explicit fresh provider checks authenticate the source.
await binding.RefreshAsync("/messagegroups/federated-orders");
```

The supplied provider must use the same configured `FederationTrustBinding`.
The observation delegate **must obtain a fresh verified observation on every
invocation**, not return a cached snapshot. The binding does not own the provider,
its Session or HttpClient. Wire provider loss or deauthorization to
`InvalidateAsync(localXid)` and perform another explicit refresh before reusing
the source. There is no background fetching or automatic reconnect.

For HTTPS, use the configured `HttpFederationProvider` boundary:

```csharp
new GroupFederationSource(
    "/messagegroups/federated-orders",
    "/messagegroups/orders",
    httpsTrust,
    ct => httpProvider.PreloadGroupAsync(
        authorizedMetadataUrl, authorizedObservationUrl, mapper,
        cancellationToken: ct));
```

Both HTTPS routes must be independently authorized. The HTTP binding has a stable
OriginUri and no ApplicationUri, registry Node or native target.

## Server lifetime and DI

Register `EndpointRegistryGroupFederationNodeManagerFactory(options)` **after**
`EndpointRegistryNodeManagerFactory` in a directly constructed server.
For the regular-server DI builder, register the existing registry manager first,
then call:

```csharp
serverBuilder.AddEndpointRegistryGroupFederation(options);
```

The integration manager exposes `Binding` after address-space creation and
disposes it during address-space deletion. Its startup never calls a provider.
The `Root` option selects the existing generic or media catalog. The selected
catalog must host all configured collections.

## Identity, native properties and persistence

The owning local Group publishes:

* `OriginRegistry`: read-only `RegistryOriginDataType`, with either OriginUri or
  the exclusive ServerUri / URI-qualified RegistryNodeId pair.
* `ExternalReference`: the actual verified remote Group ExpandedNodeId. Its
  ServerIndex resolves through **this host's ServerArray** to the authenticated
  remote ApplicationUri; its namespace URI is retained. HTTPS sources have a
  null target and never manufacture a native Node.
* `GroupUrl`: the exact authorized current provider locator.
* `SourceSnapshot`: a read-only `RegistryReadResultDataType` containing the
  complete, original `EndpointDataType` or `MessageGroupDataType` and the remote
  epoch. All declared fields, server-owned source fields, extensions, nulls,
  ordered arrays and exact numeric representations are preserved. Clients need
  not parse JSON.

The local Group's own Xid, identifier and epoch remain local. Its complete native
Snapshot also carries `x-ua-group-reference`, a versioned generic native record
containing the full original source metadata and every URI-qualified identity
pin. This attribute is stored **inside the authoritative host CAS generation**,
atomically with the local Group. It is not a peer-authentication credential.
Even a source extension with this same name is preserved inside the original
source metadata and cannot overwrite the owning Group's pins.

Restore requires the complete configured source set and the same identifier
prefix. Missing, contradictory or session-indexed pins fail closed; they are
not repaired. OriginRegistry and the retained native SourceSnapshot can be read
after restore, but ExternalReference and GroupUrl are cleared and return
`BadNotConnected`. The SourceSnapshot result also reports `BadNotConnected`.
`GetVerifiedSource(localXid)` returns null until a fresh configured observation
matches all restored pins. Authorized locator changes cannot change the origin,
application, registry root, collection-qualified Xid, Group role or native
target. Older remote epochs and changed metadata at the same epoch are rejected.
When source metadata declares `xid` or `epoch`, it must agree with the independently
observed remote selection and epoch before the durable commit. Unexpected pin
format fields are rejected rather than silently discarded on refresh.

All provider-owned Groups and descendants are protected against ordinary native,
compatibility, label and ancestor mutations by the existing host reservation.
The host remains the only metadata validation, epoch, CAS and activation owner.
References are invalidated before retirement and republished only after
activation. `InvalidateAsync` retains pins; `RemoveAsync` durably deletes the
local reference and pins. Disposal revokes transport exposure but deliberately
retains committed metadata and the host-lifetime read-only reservation.

Opting in does not change ProfileUris or advertise `XREG-GroupFederation`.
Deployment conformance claims still require the complete advertised facet.
