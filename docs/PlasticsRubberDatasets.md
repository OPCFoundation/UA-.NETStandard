# Production dataset protocol

`Opc.Ua.PlasticsRubber.Server` and `Opc.Ua.PlasticsRubber.Client` implement
[OPC 40083 version 1.03, chapter 20](https://reference.opcfoundation.org/specs/OPC-40083/v1.03/20).
The protocol uses the generated common-model structures, method states, object
clients and event records. It is independent of an extrusion-device family,
machine vendor, file format or sample application.

## Server composition

Bind an existing generated `ProductionDatasetManagementState` to its owning
`AsyncCustomNodeManager`. The application supplies **both** a per-instance
`IProductionDatasetProvider` and the common `IPlasticsRubberAccessPolicy`.
There is no implicit allow-all policy or fabricated hardware activation.

```csharp
services.AddProductionDatasets();

// Resolve through the application's existing service provider.
var factory = serviceProvider.GetRequiredService<ProductionDatasetBindingFactory>();
ProductionDatasetBinding binding = await factory.BindAsync(
    nodeManager,
    managementNode,
    machineDatasetProvider,
    accessPolicy,
    new ProductionDatasetOptions
    {
        MaximumFileBytes = 8 * 1024 * 1024,
        MaximumTransfers = 4,
        TransferTimeout = TimeSpan.FromMinutes(1)
    },
    cancellationToken);
```

`ProductionDatasetBinding.CreateAsync` is the direct-construction alternative.
Both paths accept the existing `IFileTypeBinder` infrastructure; the direct
path also accepts an injected `TimeProvider`. The owner must load the generated
common model and its dependencies before creating instances.

By default the binding materializes every optional chapter 20 member.
`ProductionDatasetFeatures` selects grouped optional capabilities. For an
existing family materializer that selects individual optional nodes, set:

```csharp
new ProductionDatasetOptions { PreserveExistingSelection = true }
```

This mode does not add absent optional members. Active and preparation layers
may independently include `Load`, `Save`, `Frozen` and `Modified`. The two
information-exchange methods can also be selected independently. Mandatory
members of every selected object are still required.

Retain the binding and asynchronously dispose it before disposing the owner.
The binding does not own or dispose the machine provider or management node.

### Device authority contract

`IProductionDatasetProvider` is the application boundary:

| Operation | Required authority behavior |
| --- | --- |
| `ReadStatusAsync` | Return current information, modified state and frozen policy for one control layer. |
| `ReadAsync` | Capture matching file bytes and metadata from one source. |
| `LoadAsync` | Actually activate the requested stored components before returning the resulting status. |
| `SaveAsync` | Persist the control-layer data before reporting the resulting name and saved state. |
| `SetFrozenAsync` | Commit the device's parameter-change restriction, including local/operator changes. |
| `CommitAsync` | Atomically apply every selected destination before returning affected control-layer snapshots. |
| `GetListAsync` | Return bounded file-system candidates; the binding enforces `*`, `?` and exact mould filtering. |
| `ReceiveListAsync` | Accept the client's available dataset descriptions, including an empty list. |

The generic package does not interpret machine-specific dataset bytes.
Implementations must validate their format, enforce their own storage bounds,
honor cancellation, and enforce frozen state inside the authority transaction.
For a multi-destination upload, validate every destination before modifying
any of them. Do not return after a partial application. If a device cannot
perform the requested operation, fail explicitly with a `ServiceResultException`.

`CommitAsync` receives optional metadata: clients need not implement the
optional `SendProductionDatasetInformation` method. When absent, an authority
must obtain required information from its supported format rather than claim
an unperformed activation.

The binding checks frozen state before mutations and again immediately before
commit. The provider must protect against concurrent physical/operator changes
as well; protocol serialization is not a substitute for hardware atomicity.
`Save` is blocked while frozen because it also changes the dataset's recorded
name/save status. Reads remain available. An authorized `SetFrozenAsync(false)`
can release the restriction.

No provider is called during binding creation using a substitute server user.
Status properties initially report `BadWaitingForInitialData`; authenticated
reads fetch authoritative snapshots. `RefreshAsync(context)` can publish
external device changes when the application supplies the operation context
and authorization policy explicitly.

### Caller identity and access

Every method and value read/write is checked through
`IPlasticsRubberAccessPolicy.AuthorizeAsync`. Temporary-file operations and
metadata reads are also checked, not just transfer creation.

The `PlasticsRubberRequestContext.SystemContext` is the original caller context.
`TargetId` is the actual method-owning or property-owning instance, which can
be a status object, list object, transfer object or temporary file. The provider
is already bound to its machine; it must not resolve authority solely by
assuming that every target is the management node.

An upload commit uses the **commit request's** user context, not the context
that originally opened the file. This matters when a session changes identity.
Files remain session-owned. A different session cannot use another session's
file handle, read its metadata, send information, or commit its bytes.

Normal server role permissions still govern browsing, event subscriptions and
other attributes. Configure those permissions on the containing address space;
the asynchronous operation policy does not replace the stack's subscription
authorization.

## Options, state and lifecycle

- Read options select exactly one of `PRODUCTION` (1), `PREPARATION` (2) or
  `FILE_SYSTEM` (4). Only file-system reads have a nonempty required name.
  Control-layer reads require an empty name.
- Write options select any nonzero combination of bits 1, 2 and 4. Unknown bits
  are rejected; destinations are never silently dropped.
- Write names are required. Component selection applies to control-layer
  destinations, not a file-system-only upload. Empty selection means all.
- Information preserves component ordering and multiplicity: `[0, 1, 1]`
  describes three component occurrences, not a set of two types.
- `Information`, `Modified` and `Frozen` are provider-backed. No proposed or
  failed change is published as committed state.
- `GenerateFileForRead` returns a pre-opened read handle and matching metadata.
  `GetProductionDatasetInformation` is valid only while that read handle lives.
- `GenerateFileForWrite` returns a pre-opened writer. Optional metadata belongs
  to that handle; `SendProductionDatasetInformation` is rejected for readers,
  foreign sessions, closed or expired handles, and a different dataset name.
- `CloseAndCommit` awaits the authority and shared FileType commit hook before
  returning success. This server completes processing within the call and
  therefore returns a null completion-state-machine NodeId.
- A failed commit preserves the writer for retry or explicit abort. Plain
  `Close` discards uncommitted content. Additional `Open` calls on a temporary
  file are rejected: each transfer owns its single generated handle.
- Expiry, close, successful commit, session shutdown and disposal remove owned
  temporary files. Stream cleanup on session shutdown is supplied by
  `FileTypeBinder`; the periodic sweep reclaims the remaining node.
- Disposal cancels providers first, drains active and queued operations, and
  then releases streams and nodes. Concurrent callers await the same disposal
  outcome. Cleanup failures are propagated rather than reported as success.

Defaults are 16 MiB per file, 16 transfers, 1,024 descriptions per list, 256
component codes/product names, 1,024 characters per string, a 60-second inactivity
timeout and a 30-second operation deadline. Limits are configurable and
validated. Periodic cleanup runs at most one second apart; `ReclaimExpiredAsync`
also supports explicit maintenance and deterministic expiry tests.

Chapter 20.8's lifecycle description specifies sending information for a
**write** transfer; its argument-table reference to a read handle is not used
to admit metadata changes on download handles. Transfer-request events use the
current chapter 20 storage mask, including preparation, rather than older
control-system naming found in descriptive text.

## Client operations

Create `ProductionDatasetClient` directly, or register
`services.AddProductionDatasetClient()` and resolve
`ProductionDatasetClientFactory`. Sessions remain owned by the caller.

```csharp
var datasets = new ProductionDatasetClient(session, managementNodeId);

ArrayOf<ProductionDatasetInformationType> available =
    await datasets.GetListAsync("recipe-*", mouldId: "", cancellationToken);

await datasets.LoadAsync(
    StorageEnumeration.PRODUCTION, "recipe-300", [], cancellationToken);

await datasets.SetFrozenAsync(
    StorageEnumeration.PRODUCTION, true, cancellationToken);

bool modified = await datasets.ReadModifiedAsync(
    StorageEnumeration.PRODUCTION, cancellationToken);
```

Upload bytes in the device's supported format:

```csharp
await datasets.UploadAsync(
    new ProductionDatasetWriteOptionsType
    {
        Name = "recipe-301",
        Storage = StorageEnumeration.FILE_SYSTEM | StorageEnumeration.PREPARATION,
        Components = []
    },
    inputStream,
    new ProductionDatasetInformationType
    {
        Name = "recipe-301",
        Components = [6, 1, 1]
    },
    cancellationToken);
```

`DownloadAsync(options, destination)` returns matching handle-scoped information.
Use `DownloadContentAsync` when the server does not select optional information
exchange. `OpenReadAsync` and `OpenWriteAsync` expose the existing
`UaFileStream`/`UaTemporaryWriteFile` lifecycles for advanced callers.
Dispose them asynchronously; disposing a writer without committing aborts it.

The high-level client applies independent byte/list limits and an overall
deadline. Uploads to peers that return an asynchronous completion state machine
wait for `Idle` and reject `Error`; an outstanding commit is not reported as
completed. A canceled or failed download can leave partial bytes in the
caller-owned destination stream. A communication failure during commit is
inherently ambiguous: inspect authoritative state before retrying a new upload.

Optional-property absence is reported as unsupported, not a false Boolean or
empty successful response. `Proxy` and `GetStatusProxyAsync` expose the generated
typed clients; no reflection or hand-written production-dataset wire types are
used.

## Request events

The binding publishes list, download and upload requests through
`RequestListAsync`, `RequestReadAsync` and `RequestWriteAsync`. Callers supply an
operation context and pass authorization before publication.

The published events are concrete application subtypes of the model's abstract
request event types. List requests originate on `ProductionDatasetLists`;
transfer requests originate on `ProductionDatasetTransfer`. Both propagate
through the management object's notifier hierarchy.

The client supplies `ObserveListRequestsAsync`, `ObserveReadRequestsAsync` and
`ObserveWriteRequestsAsync`. Pass an existing `IStreamingSubscription`; its
queue limits and lifetime remain caller-controlled. The methods return the
generated `RequestProductionDataset*EventTypeRecord` types, including verified
concrete subtypes, without a second event schema.

## Verification

The dedicated `tests/Opc.Ua.PlasticsRubber.Datasets.Tests` project uses NUnit and
the existing server/client fixtures with real UA-TCP sessions. Its bounded
in-memory byte-per-component authority exists only in the test project.
It exercises actual persistence/activation, typed clients, malformed options,
session ownership, multiple transfers, frozen state, rollback, failed/retried
commits, commit ordering, bounds, expiry, cancellation and concurrent disposal.

Run from the solution root using the repository's installed test SDK:

```powershell
dotnet test tests\Opc.Ua.PlasticsRubber.Datasets.Tests\Opc.Ua.PlasticsRubber.Datasets.Tests.csproj -f net10.0
dotnet test tests\Opc.Ua.PlasticsRubber.Datasets.Tests\Opc.Ua.PlasticsRubber.Datasets.Tests.csproj -f net48
```

See the common model's `Model\NOTICE.txt` for interface provenance and licensing.
