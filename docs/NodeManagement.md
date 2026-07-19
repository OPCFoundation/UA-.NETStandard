# NodeManagement service set

The OPC UA NodeManagement service set (OPC 10000-4 §5.8) gives clients
the ability to manipulate the server's address space at runtime:

| Service | Description |
| --- | --- |
| `AddNodes` | Create a new node beneath an existing parent (Object, Variable, ...). |
| `DeleteNodes` | Remove a node and, optionally, all references that target it. |
| `AddReferences` | Add a reference between two existing nodes. |
| `DeleteReferences` | Remove a reference between two existing nodes. |

The SDK ships full request validation, per-item dispatch, audit-event
emission, and per-NodeManager opt-in. NodeManagers do not need to
override the four `StandardServer` service methods themselves.

## Architecture

1. The client request lands on `StandardServer.AddNodesAsync` /
   `DeleteNodesAsync` / `AddReferencesAsync` / `DeleteReferencesAsync`.
2. Each service override validates the request and forwards the
   per-item collection to the matching
   `IMasterNodeManager.AddNodesAsync` etc. dispatcher.
3. `MasterNodeManager` looks up the owning NodeManager for every item
   (requested namespace or parent for `AddNodes`, node for `DeleteNodes`,
   and source for reference changes)
   and validates the matching `PermissionType` before asking it to
   perform the work via the optional `INodeManagementAsyncNodeManager`
   interface. `AddNodes` requires `AddNode` in the target namespace's
   default policy and `AddReference` on the parent. Every Node permission
   is evaluated together with the Node's applicable
   `AccessRestrictions`.
4. The aggregated per-item results are wrapped in the matching response
   envelope and the audit event (`AuditAddNodesEvent`,
   `AuditDeleteNodesEvent`, `AuditAddReferencesEvent`,
   `AuditDeleteReferencesEvent`) is emitted with an aggregated status.

NodeManagers that do not implement `INodeManagementAsyncNodeManager` —
or have not opted in — return `BadUserAccessDenied`, which is the
status defined for "server does not allow this operation". This keeps
the default behavior of every existing NodeManager unchanged.

## Opting in: `INodeManagementAsyncNodeManager`

```csharp
public interface INodeManagementAsyncNodeManager
{
    // Sub-class returns true to opt in. Default is false.
    bool AllowNodeManagement { get; }

    ValueTask<(ServiceResult result, NodeId addedNodeId)> AddNodeAsync(
        OperationContext context,
        AddNodesItem item,
        CancellationToken cancellationToken = default);

    ValueTask<ServiceResult> DeleteNodeAsync(
        OperationContext context,
        DeleteNodesItem item,
        CancellationToken cancellationToken = default);

    ValueTask<ServiceResult> AddReferenceAsync(
        OperationContext context,
        AddReferencesItem item,
        CancellationToken cancellationToken = default);

    ValueTask<ServiceResult> DeleteReferenceAsync(
        OperationContext context,
        DeleteReferencesItem item,
        CancellationToken cancellationToken = default);
}
```

`AsyncCustomNodeManager` already implements
`INodeManagementAsyncNodeManager` with default behavior that handles
every common case. Sub-classes opt in by overriding a single property:

```csharp
public sealed class MyNodeManager : AsyncCustomNodeManager
{
    public MyNodeManager(IServerInternal server, ApplicationConfiguration config, ILogger logger)
        : base(server, config, logger, "http://example.org/MyNodes/")
    {
    }

    public override bool AllowNodeManagement => true;
}
```

That is the only change needed for a NodeManager to start accepting
AddNodes / DeleteNodes / AddReferences / DeleteReferences requests.

The reference server's `ReferenceNodeManager` opts in this way; you can
use it as the working example.

## Dispatch and routing rules

- **AddNodes**: routed by the namespace of `RequestedNewNodeId` when
  the caller supplies one. When `RequestedNewNodeId` is null, routed
  to the parent's owning NodeManager. As a pragmatic fallback for the
  common case where the parent is `ObjectsFolder` (owned by the
  read-only `CoreNodeManager`), the dispatcher routes to the first
  NodeManager that has opted in to NodeManagement.
- **DeleteNodes**: routed to the owning NodeManager of the target node.
- **AddReferences / DeleteReferences**: routed to the source node's
  owning NodeManager. Target-side validation and inverse mutation occur
  only when the target is explicitly local: `targetServerUri` is empty,
  and the `ExpandedNodeId` has neither a server index nor namespace URI.
  When that target lives in a different opted-in NodeManager, the
  dispatcher resolves the actual source and target `NodeClass`, checks
  both local endpoints before mutation, and mirrors the complementary
  edge. If the inverse mutation fails or is cancelled, the source
  mutation is compensated with an independent bounded cleanup token
  before the original failure is returned or rethrown.

## Request handling on `AsyncCustomNodeManager`

The default implementation honors the following request fields:

| Field | Behavior |
| --- | --- |
| `RequestedNewNodeId` | Used as the new node's `NodeId` when valid; otherwise `BadNodeIdRejected`. When null, a fresh `NodeId` is allocated via `INodeIdFactory.New`; if the override returns a null `NodeId` (for example, because it derives identifiers from a parent that is not yet attached) the SDK falls back to the base allocator so AddNodes always yields a usable identifier. `BadNodeIdExists` if it collides with an existing node. |
| `BrowseName` | Must be non-null. `BadBrowseNameDuplicated` when a sibling under the same parent already uses the same browse name — both local children and previously-added cross-NodeManager children attached to the same parent by this NodeManager are checked. |
| `ParentNodeId` | Used to attach the new child to the parent. Cross-NodeManager parents are supported — the forward `parent → child` edge is added via the master so the parent's NodeManager records it, and the inverse `child → parent` edge is added on the new node. |
| `ReferenceTypeId` | Must be a `HierarchicalReferences` subtype. |
| `NodeAttributes` | Optional `VariableAttributes` / `ObjectAttributes` are applied to the new node (`DisplayName`, `Description`, `DataType`, `ValueRank`, `AccessLevel`, `UserAccessLevel`, `Historizing`, `MinimumSamplingInterval`, `Value`, `EventNotifier`). |
| `DeleteTargetReferences` | When true, `DeleteNodes` also removes references on other NodeManagers that target the deleted node. |
| `DeleteBidirectional` | When true on `DeleteReferences`, the inverse edge is also deleted only for an explicitly local target. Remote targets remain source-side operations. |

## Error codes returned

| Service | Status | Reason |
| --- | --- | --- |
| AddNodes | `BadBrowseNameInvalid` | `BrowseName` is null. |
| AddNodes | `BadParentNodeIdInvalid` | `ParentNodeId` is null or unknown. |
| AddNodes | `BadReferenceTypeIdInvalid` | `ReferenceTypeId` is null or unknown. |
| AddNodes | `BadReferenceNotAllowed` | `ReferenceTypeId` is not a hierarchical reference. |
| AddNodes | `BadNodeIdRejected` | `RequestedNewNodeId` is outside this NodeManager's namespace. |
| AddNodes | `BadNodeIdExists` | `RequestedNewNodeId` already exists. |
| AddNodes | `BadBrowseNameDuplicated` | A sibling beneath the same local parent already uses the browse name. |
| AddNodes | `BadNodeClassInvalid` | Only `Object` and `Variable` are supported by the default implementation. |
| AddNodes | `BadNodeAttributesInvalid` | The supplied attributes extension object does not match the node class. |
| AddNodes / DeleteNodes / AddReferences / DeleteReferences | `BadUserAccessDenied` | The owning NodeManager has not opted in to NodeManagement or the Session lacks `AddNode`, `DeleteNode`, `AddReference`, or `RemoveReference` permission. |
| AddNodes / DeleteNodes / AddReferences / DeleteReferences | `BadSecurityModeInsufficient` | An applicable namespace, parent, source, or local target `AccessRestrictions` policy requires a stronger SecureChannel. |
| DeleteNodes | `BadNodeIdInvalid` / `BadNodeIdUnknown` | The node to delete is null or unknown. |
| AddReferences | `BadSourceNodeIdInvalid` | Source is null or unknown to this NodeManager. |
| AddReferences | `BadTargetNodeIdInvalid` | Target is null. |
| AddReferences | `BadDuplicateReferenceNotAllowed` | The exact reference (type + direction + target) already exists on the source. |
| DeleteReferences | `BadNoMatch` | No reference with the exact triple (type + direction + target) was found on the source. |

## Audit events

Every NodeManagement service emits its corresponding audit event:

- `AuditAddNodesEventType`
- `AuditDeleteNodesEventType`
- `AuditAddReferencesEventType`
- `AuditDeleteReferencesEventType`

The audit `Status` is the first bad per-item status, or `Good` when
every item succeeded. Audit events are emitted from both the success
and failure paths so an early `ServiceResultException` (request-level
validation) still appears in the audit log.

## Client-side example

The standard `Session` / `ManagedSession` AddNodes / DeleteNodes /
AddReferences / DeleteReferences APIs work unchanged against any
server whose NodeManager has opted in. See OPC 10000-4 §5.8 for the
service contract.

## Custom behavior

To customize behavior for a particular NodeManager (e.g. enforce a
custom NodeId scheme, persist the new node, or veto specific node
classes), override the matching method on
`INodeManagementAsyncNodeManager` after opting in. For example, to
restrict additions to `BaseObjectState`:

```csharp
public override async ValueTask<(ServiceResult result, NodeId addedNodeId)> AddNodeAsync(
    OperationContext context,
    AddNodesItem item,
    CancellationToken cancellationToken = default)
{
    if (item.NodeClass != NodeClass.Object)
    {
        return (new ServiceResult(StatusCodes.BadNodeClassInvalid), NodeId.Null);
    }
    return await base.AddNodeAsync(context, item, cancellationToken).ConfigureAwait(false);
}
```

## See also

- [Reference Server](../samples/README.md) — opt-in example in
  `ReferenceNodeManager`.
- [Source generated NodeManagers](SourceGeneratedNodeManagers.md) —
  combine NodeManagement with generated models.
- [Model Change Tracking](ModelChangeTracking.md) — `AddNodes` and
  `DeleteNodes` emit `GeneralModelChangeEvent` automatically when
  model change tracking is enabled.
