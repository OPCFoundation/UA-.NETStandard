# Opc.Ua.EndpointRegistry.Server

Hosts the provisional OPC 30455 Endpoint Registry on an OPC UA server. The
`EndpointRegistryNodeManager` serves the well-known `EndpointRegistry` root and, when
configured, the separately selected `MediaEndpointRegistry` root.

Each root owns one committed native registry state:

- `TypedAccess` provides `ReadDocument`, `WriteDocument`, `ApplyChanges` and the bounded
  native snapshot Methods. Clients exchange generated Structures, never JSON text.
- Every mutation validates the complete resulting registry against the specification rules
  (`EndpointRegistryRules`), commits it once through an `IRegistryStateStore` and activates the
  browseable projection before it reports success.
- Endpoints, Message Groups and metadata-only Messages are projected as typed Objects with a
  `Snapshot` Variable. Endpoint `Options` use the protocol-specific ObjectType.
- `CommitMetadata` offers the optional RFC 7396 JSON compatibility patch.

Mutations require a SignAndEncrypt channel and the ConfigureAdmin or SecurityAdmin role by
default; supply `EndpointRegistryServerOptions.Authorize` to change the policy. Use
`FileRegistryStateStore` for restart-safe single-writer storage. The node manager disposes only
the in-memory store it creates; the application owns and disposes a store it supplies.

Hosting a catalog does not create a Schema Registry root, start a PubSub runtime or connect
to an advertised Endpoint. Advertise `ProfileUris` only for facets the deployment provides.

```csharp
server.AddNodeManager(new EndpointRegistryNodeManagerFactory(new EndpointRegistryServerOptions
{
    Generic = new EndpointRegistryCatalogOptions
    {
        RegistryId = "plant-registry",
        Store = new FileRegistryStateStore("registry-state")
    }
}));
```
