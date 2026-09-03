# Opc.Ua.XRegistry

The abstract **xRegistry** registry base model for OPC UA: structurally identified registry
entities, an independent content-addressed document fast path, and shared abstractions a concrete registry
(for example a schema registry) builds on.

This package contains:

- the `http://opcfoundation.org/UA/xRegistry/` abstract base companion model, **compiled into
  the assembly by the OPC UA model source generator** — the ObjectTypes, their Methods and
  Variables, the generated `NodeState` classes, `*TypeClient` ObjectType proxies, and the
  xRegistry 0.5.0 `*EventTypeRecord` / `EventFilters` client surface. No NodeSet2 XML is parsed
  at runtime;
- `XRegistryWellKnown` — the base companion namespace URI and the provisional NodeIds of the
  *instances* a registry materializes at runtime (the registry root, the federation proxy and
  the start of the dynamic instance range). The identifiers of the model itself come from the
  generated `ObjectTypeIds`, `MethodIds` and `VariableIds` classes;
- `IResourceContentIdProvider` — the seam that maps a resource document + format to its
  opaque content key. The key makes document bytes addressable by an Opaque NodeId but never
  replaces the entity's structural `Xid`, `ResourceId`, or `VersionId`.

It has no dependency on the OPC UA server or client SDKs; the generic client and server
pieces live in `Opc.Ua.XRegistry.Client` and `Opc.Ua.XRegistry.Server`.
