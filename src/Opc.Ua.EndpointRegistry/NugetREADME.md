# Opc.Ua.EndpointRegistry

The provisional OPC 30455 Endpoint Registry model, including reusable and inline Message
Definitions in one namespace. It supplies generated native catalog, protocol, envelope,
media and PubSub binding structures, NodeStates and asynchronous ObjectType proxies.

Message-only catalogs do not require an Endpoint collection, a local Schema Registry or
a PubSub runtime. The Schema Registry reference is a dependency on its information model,
not on a hosted registry.

Model provenance and content hashes are recorded in `tools\registry-models.json`.
