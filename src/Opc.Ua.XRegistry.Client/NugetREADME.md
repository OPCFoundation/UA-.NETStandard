# Opc.Ua.XRegistry.Client

The generic **xRegistry** registry client for OPC UA. It talks to a registry hosted in an
OPC UA server address space and provides the two core registry operations:

- **Resolve** a resource from its content-derived id through the Opaque-NodeId fast path — a
  single `Read` of the node whose Identifier is the raw content-id bytes (no Browse, no
  fingerprint recomputation).
- **Register** a resource through the `CreateResource` / `Write` / `Close` lifecycle, which
  returns the content-derived id the server computed.

A concrete registry client (for example the PubSub `SchemaRegistryClient`) derives from
`XRegistryClient` and adds domain-specific naming and defaults.

