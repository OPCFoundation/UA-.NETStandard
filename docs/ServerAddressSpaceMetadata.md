# Server address-space metadata

This guide covers two server-startup behaviours that keep the published
address space consistent with what the server can actually serve:

- namespace metadata objects under `Server/Namespaces`;
- historical-access advertisement on variables.

## NamespaceMetadata for every namespace

OPC UA Part 5 requires the `Server/Namespaces` object to describe the
namespaces exposed by a server. Companion specifications repeat the
same requirement in their namespace-metadata clauses so clients can
compare `NamespaceVersion` and `NamespacePublicationDate` against cached
models.

`StandardServer` calls the overridable
`PublishNamespaceMetadataAsync(IServerInternal, CancellationToken)` seam
during startup, after conformance units are published and before the
server accepts sessions. The default implementation uses
`NamespaceMetadataPublisher` to walk `NamespaceArray` and ensure every
namespace URI has a `NamespaceMetadataType` object under
`Server/Namespaces`.

For source-generated models, the publisher fills
`NamespaceVersion` and `NamespacePublicationDate` from the
`ModelDependencyAttribute` stamped on model assemblies. Existing
metadata objects and already-populated values are preserved.

### Node-manager authoring note

Attaching a child to an object owned by another node manager is not
enough to make it browseable through the master node manager. This is
common for namespace metadata because `Server/Namespaces` is a namespace
0 object owned by the configuration node manager, while the metadata
object may be created by another manager. Register the link as a
cross-manager reference with `AddReferencesAsync` when the owner differs.
`NamespaceMetadataPublisher` does this check automatically for metadata
objects it creates.

Servers that publish namespace metadata themselves can override
`StandardServer.PublishNamespaceMetadataAsync` and either add custom
metadata or return without doing work.

## Historical-access reconciliation

Official companion NodeSets often declare `Historizing="true"` or set
`AccessLevel` bits such as `HistoryRead` on variables whose type is
capable of history. A concrete server still needs a historian provider
before it can serve `HistoryRead` or `HistoryUpdate` for those variables.

During master-node-manager startup, every `AsyncCustomNodeManager`
reconciles this advertisement before external references are applied.
For each variable that advertises historical access, the server checks
whether an `IHistorianProvider` resolves through:

1. the node manager's `GetHistorianProvider(NodeState)` override;
2. the server-wide historian registry (`RegisterForNode`,
   `RegisterForNamespace`, then `RegisterDefault`).

If no provider resolves, the server clears `Historizing` and masks
`HistoryRead` / `HistoryWrite` from `AccessLevel`,
`UserAccessLevel`, and the corresponding attribute read callbacks. This
keeps direct reads of the attributes consistent with the values stored
on the node.

Variables with a historian keep their NodeSet-declared history surface.
Use `builder.UseHistorian()` and `.Historize()` from the fluent server
API, or override `GetHistorianProvider`, when a NodeSet variable should
continue advertising historical access.

## See also

- [Historical Access](HistoricalAccess.md) — historian provider model
  and fluent `.Historize()` wiring.
- [Source-generated NodeManagers](SourceGeneratedNodeManagers.md) —
  NodeSet2 import, fluent node creation, and runtime instance NodeId
  assignment.
