# Generation-bound client requests

`Opc.Ua.Client.SessionRequestContext` is an opt-in borrowed client for workflows
that retain a server file handle, history continuation point, or a target
classification across asynchronous calls. Obtain the owning `ManagedSession`
through the existing client/DI infrastructure; the context does not establish
another connection.

```csharp
using Opc.Ua.Client;

await using SessionRequestContext context =
    await SessionRequestContext.CaptureAsync(managedSession, ct);

// Resolve namespace-qualified targets and apply policy using this session too.
ISession requestSession = context.Session;
var file = new FileTypeClient(requestSession, fileId, requestSession.MessageContext.Telemetry);
```

Keep the context alive until every cursor, file and subscription created through
it has been cleaned up. Its `Session` can be retained across awaits and passed to
existing generated proxies or higher-level clients. It has its own local
namespace tables and node cache, but borrows the original connection.

## Guarantees and limits

- Each service dispatch enters the owner's service gate and rechecks the actual
  session generation, authentication identity, namespace mappings and transport.
  Checking `SessionId` before calling a managed session is not an equivalent
  guarantee: recovery can recreate its inner session in place.
- Requests use the captured authentication token and physical transport. They
  do not wait behind a mutable transport lease and then adopt its replacement.
  The context does not replay a failed request.
- Recovery conservatively invalidates the context, including recovery that
  might eventually reactivate the same server session. Stale dispatch fails
  with `BadSessionIdInvalid`; it never sends an old handle to the replacement.
- `Invalidated` completes on session-generation change, managed recovery/close,
  context disposal, or namespace remapping detected at dispatch. It is not a
  monitor for server-side model changes. Keep model-change and authorization
  invalidation policies in the caller.
- `ExecuteAsync` is an optional callback convenience, not a transaction. It
  always invokes the callback so local cleanup can run after invalidation.
  Every request made through the callback's session remains guarded.
- There is no post-response cancellation or generation check. If an already
  dispatched mutation returns a result, callers retain that result even when
  cancellation or recovery follows it. An interrupted request with no response
  may still have committed remotely; no rollback or safe retry is implied.
- Closing or disposing the borrowed session/context does not send
  `CloseSession` for the owner's real session or dispose its channel or manager.
  Explicit server resources remain the caller's responsibility.

Capture requires the stack's `ManagedSession`. Ordinary/custom `ISession`
implementations are rejected with `BadNotSupported` because the SDK cannot
synchronize their lifecycle. A custom managed transport without the stack's
fixed physical-transport access is also rejected. Custom raw transports must
not be independently retargeted outside the owning managed session.

Normal session behavior remains unchanged when no context is requested.

## Generic MCP mutations

The Core MCP Call/CallMethod, Write/WriteValue, HistoryUpdate and node/reference
mutation tools capture a context automatically for a stack `ManagedSession`.
Classification and dispatch use that same borrowed session and the original
host registration name. Replacing the named registration invalidates an
outstanding preflight; it never selects the replacement for that operation.

An embedding host that registers a custom `ISession` must explicitly register
an `Opc.Ua.Mcp.IMcpFixedSessionContract` through DI before using those generic
mutations. No contract means `BadNotSupported`, not an unsafe direct fallback.
A contract grants no operation capability; it declares that the custom session
will not reconnect, retarget, change authentication, or change the
namespace/type-cache metadata used during classification and dispatch.
Both the contract and observable metadata are checked again before dispatch.
These checks do not synchronize an independently mutable custom implementation.
Use a managed session instead when that fixed guarantee cannot be made.

Read-only generic tools are unchanged. Neither kind of session turns preflight
into a server-side transaction: same-session remote model changes still need
the host's model-invalidation policy and the server's own authorization.

## Files and temporary commits

Prefer the opaque `SessionFileHandle` APIs when retaining an open handle:

```csharp
using Opc.Ua.Client.FileSystem;

await using SessionFileHandle file = await context.OpenFileAsync(fileId, mode: 6, ct);
long bytes = await file.WriteFromAsync(source, maxBytes: 64 * 1024 * 1024, ct: ct);
CallMethodResult close = await file.CloseAsync(ct);
```

The handle has no public raw-handle constructor or import method. It cannot be
attached to a new context. Use it sequentially like a stream. Disposal attempts
best-effort Close only on the original context and logs cleanup failure without
replacing the transfer failure.

For whole transfers, `context.UploadFromWithResultAsync` and
`context.DownloadToStreamWithResultAsync` return the transferred byte count and
the unmodified Close result, including a refused Close. They enforce chunk and
total-byte bounds, continue after short nonempty reads, and never dispose the
caller's stream.

`TemporaryFileTransferClient(context.Session, transferId)` has context-taking
read/write generation overloads returning `SessionFileHandle`, plus bounded
`UploadFromAsync` and `DownloadToAsync`. Temporary upload returns the file ID,
completion-state-machine ID and the actual `CloseAndCommit` result. Regular
Close is not an atomic commit. `IsCommitted` remains true if a successful
temporary commit has malformed output; disposal will not close that consumed
handle a second time.

Existing raw-handle helpers remain compatible. When using them, keep every
Open/Generate, chunk, Close/CloseAndUpdate or CloseAndCommit on the same captured
session; never reconstruct a proxy from the newly current named connection.

## History and role mapping limits

`HistoryClientOptions.MaxDecodedPageItems` limits each decoded raw, modified or
event page. Zero preserves the legacy unbounded behavior. Both incoming modified
arrays are checked before a modified-value copy is allocated; an oversized page
fails with `BadEncodingLimitsExceeded` before yielding any item. This is not a
wire decoder quota: configure transport message and array limits separately.

`BindSessionGeneration = true` captures a context for each history enumeration
and its cleanup. When policy/namespace resolution precedes enumeration, instead
capture first and construct `HistoryClient(context.Session, options)`. Retain
the context across all later `MoveNextAsync`/`DisposeAsync` calls. Early disposal
and cancellation release the last known point on the original session when
possible; replacement never receives the old point. Cleanup failures are logged
without discarding the read failure.

`new RoleManagementClient(context.Session, maxMappingEntries: 1000)` bounds each
identity, application and endpoint mapping collection before decoded-array/list
copies. The one-argument constructor and a zero limit retain existing behavior.
This does not bound the separate full-role-list traversal.
