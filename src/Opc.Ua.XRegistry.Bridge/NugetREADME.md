# Opc.Ua.XRegistry.Bridge

Experimental OPC UA / HTTP xRegistry gateways and durable bidirectional
reconciliation, built on the shared xRegistry model, client, server and HTTP
libraries.

Gateways are authoritative write-through adapters. The optional generated native
transaction extension lives in a separate experimental namespace and preserves
HTTP atomicity, explicit-zero guards and update-touch semantics without weakening
the existing native FileType behavior. Unqualified base servers expose only the
operations they can faithfully support.

Synchronization uses durable baselines, intents, outcomes and conflicts rather
than comparing clocks or cross-server epoch numbers. Conflict policies are manual
(default), prefer OPC UA, or prefer HTTP. Automatic deletions are guarded and enabled
by default; incomplete inventories never imply deletion.

Use the thin `Opc.Ua.XRegistry.Connector` tool or compose the library through
direct constructors and dependency injection. See `docs/XRegistryBridge.md` in
the source repository for configuration, qualification and recovery requirements.
