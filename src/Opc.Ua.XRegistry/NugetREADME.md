# Opc.Ua.XRegistry

The abstract **xRegistry** registry base model for OPC UA (Annex B): a content-addressed
resource-registry companion namespace plus the shared abstractions a concrete registry
(for example the PubSub Schema Registry) builds on.

This package contains:

- the `http://opcfoundation.org/UA/xRegistry/` abstract base companion NodeSet (embedded);
- `XRegistryWellKnown` — the base namespace and the provisional resource/method NodeIds;
- `IResourceContentIdProvider` — the seam that maps a resource document + format to its
  content-derived identity (the fingerprint that makes a resource addressable by an Opaque
  NodeId, stable across registries).

It has no dependency on the OPC UA server or client SDKs; the generic client and server
pieces live in `Opc.Ua.XRegistry.Client` and `Opc.Ua.XRegistry.Server`.

> This is an experimental API surface gated behind the `UA_NETStandard_Encoders`
> experimental diagnostic id.
