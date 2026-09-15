# Awaited NodeManager readiness

`INodeManagerReadinessParticipant` is an optional interface for initialization
that needs a published address space and initialized server subsystems. In
particular, registering a dependent runtime NodeManager belongs here, not in
`CreateAddressSpaceAsync`.

```csharp
public interface INodeManagerReadinessParticipant
{
    ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default);
}
```

Existing NodeManagers need not implement it. The interface uses ordinary
interface dispatch, with no reflection, dynamic code generation, additional
container registration, or NativeAOT annotations. A directly constructed manager
and one produced by a DI-registered factory follow the same contract.
`AsyncNodeManagerAdapter` and `SyncNodeManagerAdapter` forward the asynchronous
callback without introducing a synchronous wait.

## Ordering and visibility

| Hosting path | Readiness point | Caller completion |
| --- | --- | --- |
| Initial `StandardServer` startup | After all initial address spaces and server subsystems are initialized, and runtime lifecycle operations are legal | Startup awaits each initial participant before `OnServerStarted` and before returning |
| Runtime `AddAsync` | After the new generation is committed and registered | Add awaits readiness before returning its handle |
| Normal, shadow, or immediate reload | After the replacement is committed and the applicable retired-generation completion work has run | Reload awaits replacement readiness before returning its next-generation handle |

Initial startup captures the initial manager set before opening Running
admission. A runtime manager added during the Running-state transition or by a
readiness callback receives readiness through its own Add operation, not a
second pass over an expanding initial set. Static initial managers participate
even though they are not removable lifecycle registrations.

Preparation and publication remain staged by the existing host. Readiness is
**after** the client-visible commit, not another preparation transaction: the
parent can already be read while its dependencies are being initialized.
Successful caller completion means that its readiness callback finished; it
does not promise an atomic visibility switch across several registrations.

## Generation ownership and cancellation

The lifecycle releases registration serialization while awaiting readiness, but
reserves that specific generation until the owning Add/reload operation ends.
Unrelated lifecycle operations, including dependent Add, can proceed.

A competing removal or reload of the reserved registration throws
`InvalidOperationException` with a readiness-in-progress diagnostic. Retry only
after the owning startup operation completes. It does not wait while retaining
serialization, and it does not unpublish or destroy the initializing generation.
Do not attempt to remove or reload a participant's own registration from inside
its readiness callback.

The caller's cancellation token reaches the participant and is checked both
before and after the callback. A participant should forward it to its awaited
work. Cancellation after publication is still a post-commit outcome; it cannot
be interpreted as proof that the generation was never published.

Shutdown accounts for readiness as an active lifecycle operation. It waits
outside registration serialization before dismantling the server, and rejects
new lifecycle operations once shutdown begins.

An orderly `StopAsync` leaves the provider reusable for the same server's next
`StartAsync`, so an injected `INodeManagerLifecycle` reference does not become
stale. Restart re-enables it only after all prior operations, shutdown stages,
registrations and retired generations have finished. Disposing the server or
the lifecycle provider is still final.

## Failure and cleanup

| Failure point | Result |
| --- | --- |
| Preparation fails | Readiness is not invoked; the existing preparation/rollback cleanup applies |
| Add readiness fails or is cancelled | Add throws a post-commit `InvalidOperationException` retaining the original error; the live handle remains in `Registrations` for recovery or removal |
| Reload readiness fails or is cancelled | `NodeManagerReloadCommittedException.Registration` identifies the committed replacement; the old generation is not restored as though publication never happened |
| Initial readiness fails or is cancelled | `StandardServer` awaits its ordered stop path, including dependent runtime registrations, and reports the startup error; cleanup errors are also reported |

A participant must retain the handles of successfully registered dependencies
and release them in `DeleteAddressSpaceAsync`, including those created before a
later readiness failure. That method does not imply ownership of an injected
registry, coordinator, store, or other provider; retain the provider's existing
ownership rules.

Removal and retired-generation cleanup keep their existing generation claims
and request/notification drains. Once detached, address-space deletion runs
outside lifecycle and host startup serialization, so a parent can await removal
of its actual dependent registrations. Competing cleanup cannot claim the same
generation. Shadow-retired generations still wait for their existing monitored
items to drain.

## WoT registry startup

`WotRegistryNodeManager` prepares only its stable registry graph during
address-space creation. Its readiness callback awaits startup refresh using the
supplied cancellation token, then reconciles the materialized projection.
Reopening a populated `FileWotRegistryStore` therefore makes its successful
projections readable before runtime registration or ordinary server startup
returns, without an explicit `Refresh` call.

This applies both to `AddWotRegistryServer` hosting and direct
`WotRegistryNodeManagerFactory` construction. `AutoRefresh = false` disables
automatic refresh after content changes, not the awaited initial refresh.
Failed materialization results are visible in the registry and fail startup
with `BadConfigurationError`; they are not logged and discarded as successful
readiness. On runtime Add, this is reported through the post-commit exception
described above, and the parent remains removable.

While startup owns the registry's existing refresh admission gate, a concurrent
native `Refresh` returns `BadServerTooBusy`. It must not become a tracked request
waiting on the coordinator while startup's nested Add drains requests.
The gate is released on success, failure or cancellation; subsequent explicit
Refresh calls use the normal admission rules.

See [WoT Connectivity](WoTConnectivity.md#111-architecture) for configuration
and [NodeManagers](NodeManagers.md#runtime-registration) for lifecycle handles.

## Custom host migration

A custom host that does not use `StandardServer` must explicitly await readiness
after creating the address space and initializing the server subsystems. Merely
calling `MasterNodeManager.StartupAsync` or `CreateAddressSpaceAsync` is not a
server-readiness signal. Capture the initial set before invoking callbacks:

```csharp
ArrayOf<IAsyncNodeManager> initialManagers = [.. master.AsyncNodeManagers];

// Initialize the remaining server subsystems and enable runtime lifecycle operations.
for (int ii = 0; ii < initialManagers.Count; ii++)
{
    if (initialManagers[ii] is INodeManagerReadinessParticipant participant)
    {
        await participant.OnServerReadyAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

Such a host must own its initial startup/shutdown ordering and cleanup on error.
Runtime managers must still go through `INodeManagerLifecycle`; do not invoke a
second readiness callback after Add/reload, or bypass its generation ownership.
