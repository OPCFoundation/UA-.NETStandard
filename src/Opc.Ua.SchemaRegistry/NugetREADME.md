# Opc.Ua.SchemaRegistry

The provisional OPC 30451 Schema Registry model, generated from the pinned companion
NodeSet. It supplies native JSON Schema, Avro and Arrow content structures, exact schema
reference/selection contracts, NodeStates and asynchronous ObjectType proxies.

Referencing the model does not host a Schema Registry or start a transport. The model
depends on `Opc.Ua.XRegistry`; runtime format libraries and server/client workflows are
separate modules.

Model provenance and content hashes are recorded in `tools\registry-models.json`.
