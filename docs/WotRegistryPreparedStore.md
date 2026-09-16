# Prepared WoT registry metadata commits

The file-backed WoT registry can publish projection metadata without rereading
the content of independent resources on each update. This is an optional,
owner-bound store capability, not a relaxation of content integrity checks.
It does not by itself provide an atomic NodeManager/coordinator publication unit.

## Normal service use

`WotRegistryService` discovers `IWotRegistryPreparedStore` on the store supplied
to its constructor. Initialize the service once, then keep it alive while
publishing metadata:

```csharp
using var store = new FileWotRegistryStore(registryPath);
using var registry = new WotRegistryService(store);
await registry.InitializeAsync(cancellationToken);

// Records refer to existing Resources and already prepared projection results.
await registry.ApplyProjectionResultsAsync(projectionRecords, cancellationToken);
```

Initialization and full content mutations establish validated generation and
content evidence. The service retains that evidence across subsequent metadata
generations and disposes it with the service. Calling `LoadAsync` again
invalidates earlier captures; disposing all captures releases their content
protection. A later fresh capture may need to validate content again.

Only `ApplyProjectionResultsAsync` selects the `ProjectionMetadata` commit scope.
Other mutations use `Full`, even when their change notification happens to set
`WotRegistryChangedEventArgs.ProjectionOnly`. That notification flag is not a
store-I/O isolation guarantee.

## Public prepared-store contract

`IWotRegistryPreparedStore` extends `IWotRegistryStore`:

- `SupportsPreparedCommits` truthfully reports whether the configured content
  provider can supply authoritative immutable-content leases.
- `CaptureValidatedGenerationAsync` returns an
  `IWotRegistryValidatedGeneration`. Its `Snapshot` identifies the validated
  manifest and store generation; the handle retains the verified content
  evidence until disposed.
- `PrepareCommitAsync(intendedSnapshot, expectedGeneration, scope, token)`
  returns an `IWotRegistryPreparedCommit` without publishing the replacement.
  Its `IntendedSnapshot` is the exact candidate for the durable decision.
- `IWotRegistryPreparedCommit.CommitAsync` rechecks the authoritative manifest
  and commits it. The owner is single-use; asynchronous disposal before its
  decision aborts it, while disposal after a decision does not roll it back.

For direct store integration, load the owner before capture, check capability,
and retain the capture until preparation has acquired its own ownership:

```csharp
if (store is not IWotRegistryPreparedStore { SupportsPreparedCommits: true } preparedStore)
{
    throw new NotSupportedException("This store cannot publish isolated metadata.");
}

using IWotRegistryValidatedGeneration captured =
    await preparedStore.CaptureValidatedGenerationAsync(cancellationToken);
await using IWotRegistryPreparedCommit prepared =
    await preparedStore.PrepareCommitAsync(
        intendedSnapshot,
        captured,
        WotRegistryCommitScope.ProjectionMetadata,
        cancellationToken);

await prepared.CommitAsync(cancellationToken);
```

`intendedSnapshot` must preserve Resource/Version membership, persistent
identities, content descriptors, labels and entity epochs in metadata scope.
Only projection state and validation results may change. `Full` permits a
complete replacement and still validates newly referenced content.
Unknown scope values, foreign or disposed captures, reload-invalidated inputs,
stale generations, and changed manifest bytes are rejected before publication.

When handing off successive generations directly, acquire the next validated
generation before disposing the successful prepared owner. The service performs
this handoff automatically. Store generation (`long`) remains distinct from
materialization refresh generation (`uint`).

## Integrity is retained, not cached by assumption

The store compares the current manifest with the exact validated manifest,
including same-generation rewrites. Captured content evidence is established
by hashing the actual bytes while an immutable lease protects both those bytes
and their resource-key association. A supplied digest, length, timestamp, or
previously observed hash without that protection is not accepted as evidence.

Prepared metadata uses the existing manifest serializer, structural validation,
atomic replacement, directory durability, and recovery classification. It does
not introduce another persistence engine or a JSON Schema validator. New content
in a full mutation is still hashed; metadata scope cannot substitute content or
advance Resource/Version epochs.

Resource and Version validation metadata is retained defensively so mutable
validation objects cannot change a prepared or committed snapshot through an
external reference.

## Resource-provider capability

An actual `IXRegistryResourceStore` owner may additionally implement
`IWotRegistryContentLeaseProvider`:

- `SupportsImmutableContentLeases` must be false unless the provider can protect
  the authoritative content version and its key mapping against overwrite,
  replacement and deletion, including other writers.
- `AcquireContentLeaseAsync` returns an `IWotRegistryContentLease` carrying the
  protected `ResourceKey`, `ContentLength`, and asynchronous `ReadAsync`.
- The store validates the lease's bytes before retaining it as evidence.
  Disposal releases the protection.

A decorator may forward this capability only from its actual wrapped owner.
Dependency-snapshot support is not a substitute. Instrument both ordinary and
leased content reads when observing acquisition behavior; switching read APIs
does not establish isolation.

The default committed-file adapter and `WotBlobResourceStore` with
`LocalFileSystem` advertise this capability on Windows, where kernel file sharing
prevents writes and replacement while the read lease is held. POSIX advisory
sharing and arbitrary `IFileSystem` implementations are not advertised as
equivalent guarantees. Other deployments can inject a provider with a genuine
immutable-version/key lease contract.

Unsupported providers retain the existing full-validation `IWotRegistryStore`
path. Consumers requiring isolated units must reject unsupported capability;
they must not silently advertise an isolated metadata operation.

## Authoritative outcomes and cancellation

| Outcome | Required handling |
| --- | --- |
| `WotRegistryCommitNotCommittedException` | The prior generation remains active. Do not publish the candidate. |
| `WotRegistryCommitDurabilityUncertainException` | Its validated committed snapshot is authoritative; the service publishes it and surfaces the persistence warning. |
| `WotRegistryCommitIndeterminateException` | Do not infer commit/noncommit or blindly retry. The service blocks mutation until a successful reload establishes a known generation. |

Cancellation before the durable decision can abort preparation. Cancellation
after manifest publication cannot turn a committed full or metadata mutation
into a reported noncommit; post-decision staging cleanup is not driven by the
caller cancellation token. Recovery artifacts are retained according to the
existing store outcome contract.

Read-only capture/preparation does not create a new storage root or repeat the
directory synchronization performed by actual load/commit operations. Pristine
rollback releases uncommitted content leases before removing artifacts owned
by that failed attempt.

See also [registry Version leases](WotRegistryVersionLeases.md), which protect
logical Version retention/incarnation and are distinct from immutable byte-store
evidence, and the [WoT Connectivity guide](WoTConnectivity.md).
