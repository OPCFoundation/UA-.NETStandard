# OPC UA MCP workflows

`OPCFoundation.NetStandard.Opc.Ua.Mcp.Workflows` provides the `roles`, `users`, `files`, `alarms` and
`history` profiles. It reuses the host's existing OPC UA session, generated
model proxies, file clients, alarm operations and historian.

```csharp
services.AddOpcUaMcpCore(options);
services.AddOpcUaMcpClientWorkflows();
services.AddMcpServer()
    .WithOpcUaClientWorkflowTools(
        new McpToolProfileSet([McpToolProfile.Files, McpToolProfile.History]));
```

Profiles select tools, not permissions. Role mutations and all user administration require the
host's `IdentityAdministration` opt-in, an exact trusted caller/session grant,
and an encrypted OPC UA session. Server authorization remains authoritative.
Passwords are `alias:name` or owned `issued:...` secret references; configure
`AddOpcUaMcpSecrets` before registering this module. Bind aliases using
`McpSecretReferences.GetResourceBinding` for the actual UserManagement object.
There is no tool for reading a password or choosing a caller identity.

Pages default to 100 and cannot exceed 500 items. Observations default to one
second and 100 notifications, and cannot exceed 30 seconds or 500 notifications.
Host options may lower ceilings. File transfers use the host's `TransferRoot`
and 64 MiB default ceiling. Downloads never replace local files. Upload
replacement is an explicit argument, remote partial effects are reported, and
no tool exposes a server file handle.

Historical reads are stateless unless retention is requested. Retained cursors
require a trusted caller, have host-owned quotas and expiry, and are bound to
the exact session instance and UA session id. Release cursors when finished.
An offset is not a historical continuation point. Updates expose actual
per-item statuses and do not claim atomicity.

## Catalogue

The package contributes **74 domain tools**. Any selected profile adds the same
four deduplicated connection tools. All five profiles composed contribute 78
tools before host-disabled identity administration is filtered.

| Profile | Domain tools | Host capability for protected operations |
| --- | ---: | --- |
| `roles` | 13 | `IdentityAdministration` |
| `users` | 6 | `IdentityAdministration` |
| `files` | 13 | General UA access; protected administration targets are refused |
| `alarms` | 19 | General UA access; server condition permissions remain authoritative |
| `history` | 23 | General UA access plus the shared protected-target guard |

### Roles

- `roles_list`, `roles_read`, `roles_create`, `roles_remove`
- `roles_add_identity`, `roles_remove_identity`
- `roles_add_application`, `roles_remove_application`
- `roles_add_endpoint`, `roles_remove_endpoint`
- `roles_set_flags`, `roles_read_permissions`, `roles_write_permissions`

Permission writes replace the declared `RolePermissions` attribute. They never
write server-derived `UserRolePermissions`. An explicitly empty permissions
array clears the declaration; omission is rejected. Omitted role flags remain
unchanged. Role membership and self-lockout rules remain server decisions.

### Users

- `users_policy`, `users_list`, `users_add`, `users_modify`
- `users_remove`, `users_change_password`

User-list paging requests a bounded server-side index range. A server that
rejects index ranges is not retried with an unbounded read. For modification,
omitted fields are unchanged; `description=""` and `userConfiguration=0` are
explicit updates. Password change has no user-name override.

### Files

- `files_roots`, `files_list`, `files_metadata`
- `files_create_file`, `files_create_directory`
- `files_copy`, `files_move`, `files_delete`
- `files_upload`, `files_download`
- `files_read_temporary`, `files_write_temporary`, `files_completion`

Every source, parent, destination and generated file is checked at the shared
semantic guard. General file tools additionally reject TrustList subtypes and
ServerConfiguration descendants, even when administration is enabled. Use the
dedicated certificate/configuration tools for those targets.

Upload requires `replaceExistingContent=true`. Moving or copying a nonempty
directory requires `includeChildren=true`; deleting it requires
`recursive=true`. These are explicit scope selections, not authorization.
No operation invents an atomic check-and-write guarantee.

Temporary writes generate a private file handle, stream a confined artifact,
and call `CloseAndCommit` once. A returned completion state-machine id means
processing may still be pending. Inspect it explicitly with `files_completion`.
A completed commit is not replaced by a late cancellation error.

### Alarms

- `alarms_inspect`, `alarms_group_memberships`, `alarms_observe`, `alarms_refresh`
- `alarms_acknowledge`, `alarms_confirm`, `alarms_comment`
- `alarms_enable`, `alarms_disable`, `alarms_silence`
- `alarms_suppress`, `alarms_unsuppress`
- `alarms_out_of_service`, `alarms_in_service`, `alarms_reset`
- `alarms_timed_shelve`, `alarms_one_shot_shelve`, `alarms_unshelve`
- `alarms_respond_dialog`

Occurrence commands require the binary EventId encoded as base64. Following
acknowledgement, a refreshed EventId is returned only when an addressable
condition reports a changed id on the same branch. Otherwise observe the new
occurrence before confirmation.

Observations borrow `ManagedSession.DefaultStreaming` and dispose only their
owned enumerators. Refresh snapshots require actual RefreshStart/RefreshEnd
markers. Count/time limits and server queue overflow are explicit. Local
dropped-notification counts, when available, cover the shared subscription;
other stream implementations report this counter as unknown.

### History

- `history_capabilities`, `history_configuration`
- `history_read_raw`, `history_read_modified`, `history_read_at_time`
- `history_read_processed`, `history_read_events`, `history_read_annotations`
- `history_insert_values`, `history_replace_values`, `history_update_values`
- `history_insert_events`, `history_replace_events`, `history_update_events`
- `history_insert_annotations`, `history_replace_annotations`, `history_update_annotations`
- `history_delete_range`, `history_delete_at_time`
- `history_delete_events`, `history_delete_annotations`
- `history_next`, `history_release`

Times use ISO 8601 with `Z` or a numeric `+HH:mm`/`-HH:mm` offset. Modified reads
include actual ModificationInfo, not a relabeled raw value. Annotation reads
retain DataValue quality and source/server timestamps. Aggregate reads require
an actual advertised AggregateFunctions node and at most the host's bounded
number of processing periods.

The bounded update contract accepts supported built-in scalar values and
one-dimensional arrays. It does not accept arbitrary ExtensionObject bodies,
multi-dimensional matrices, executable event-filter expressions or source
code. Standard annotations have their own typed contract. Bad server results
remain exact per-item statuses.

Input arrays are first-class tool arguments, not nested `input` objects.
Each DataValue is a flat declaration:

```json
{
  "nodeId": "ns=2;s=Temperature",
  "values": [
    {
      "type": "Double",
      "value": 12.5,
      "sourceTimestamp": "2026-01-01T00:00:00Z",
      "statusCode": 0
    }
  ]
}
```

For an array-valued sample, set `arrayLength` instead of `value`, and supply
flat indexed `arrayElements`. `valueIndex` addresses the entry in `values`;
`elementIndex` addresses its array. Length zero represents a typed empty array.
Missing, duplicate and out-of-range cells are refused before mutation.

```json
{
  "nodeId": "ns=2;s=Samples",
  "values": [
    {"type": "Int32", "arrayLength": 2, "sourceTimestamp": "2026-01-01T00:00:00Z"}
  ],
  "arrayElements": [
    {"valueIndex": 0, "elementIndex": 0, "value": 4},
    {"valueIndex": 0, "elementIndex": 1, "value": 9}
  ]
}
```

Event selectors use `select: [{"browseName":"EventId"},{"browseName":"Time"}]`.
A selector may include its exact `namespaceUri` and `typeDefinitionId`.
Multi-segment paths use separate `paths` rows with `selectIndex`, `segmentIndex`,
`name` and `namespaceUri`; do not also set that selector's inline `browseName`.

Event updates use flat `events` cells with `eventIndex`, `fieldIndex`, `type`
and scalar `value`. Every event index must contain every selected field
exactly once. Array-valued fields use `arrayLength` and `arrayElements`, where
`valueIndex` now addresses the corresponding cell in `events`. Returned
operation statuses follow `eventIndex` order.

Annotations use a root `annotations` array, permissions a root `permissions`
array, and at-time operations a root `timestamps` array. Historical event
deletion uses root `eventIds`. Temporary-file generation accepts `optionType`
and an actual JSON scalar or flat scalar array in `optionValue`, without an
`options` object or a JSON-encoded string document.

Every workflow interface has at most eight root arguments. Actual advertised
schemas are shallow, typed and closed; scalar types, timestamps, quality and
namespace identities are retained rather than hidden in untyped objects.

Cursor defaults are 16 per host, four per caller, five minutes idle and thirty
minutes absolute lifetime; the host may lower them. They are independent of
the protocol runtime registry and never consume WoT/PubSub capabilities.

## Session generation safety

The default `IClientWorkflowSessionFactory` captures the SDK's
`SessionRequestContext`. Its borrowed session binds each actual service dispatch
to the original authentication token, physical transport, namespace mapping and
managed-session generation. A saved reference to `ManagedSession.InnerSession`
alone is **not** sufficient, because recovery can recreate that object in place.

Every private file handle and historical iterator retains one guarded context
until its terminal cleanup. Historical cursor disposal runs the SDK iterator's
cleanup before releasing the local context. A stale cleanup request is refused
at dispatch rather than being sent through a replacement session. Disposing
the context never closes the host's real session or shared subscription.

The default capture supports stack `ManagedSession` instances, including the
sessions opened by MCP connection tools. Other embedding hosts must implement
`IClientWorkflowSessionFactory` with equivalent dispatch-generation guarantees;
there is no unsafe fallback for an arbitrary `ISession`.
