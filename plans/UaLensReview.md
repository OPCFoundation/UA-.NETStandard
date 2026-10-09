# UaLens review and ticket backlog

**Review date:** 2026-09-10. **Baseline:** `fe612275e` on `pisrapp`.

**Scope:** [`tools/Opc.Ua.Lens`](../tools/Opc.Ua.Lens), its tests, distribution
configuration, and documented desktop workflows.

This is a risk-based review, not an exhaustive audit or an OPC UA conformance
assessment. The application has 396 tracked files, approximately 92,770 lines
across C# and AXAML, and 81 tracked files in its test project. The review combines
source inspection, existing screenshots, build/test execution, and examination
of the seams between desktop presentation, document state, and stack operations.

The tickets below are proposed work, not GitHub issues that have already been
created. Application code was not changed during this review. Source links and
line ranges refer to the baseline above.

## Implementation follow-up

The code changes, Linux desktop checks, and Linux artifact certification are
complete. The remaining platform and assistive-technology acceptance checks are
listed below. The historical review describes the original baseline, not the
subsequent changes.

| Tickets | Implementation and regression scope |
| --- | --- |
| LENS-ARCH-01, LENS-ARCH-02, LENS-ARCH-06 | Retained monitor identity, mutation cancellation/draining, and actual channel eviction accounting. |
| LENS-ARCH-03 | Inspector row publication through the workspace dispatcher, with stale-selection and clear guards. |
| LENS-ARCH-04 | Synchronized log publication with an atomic snapshot/cursor and explicit overwrite count. |
| LENS-ARCH-05 | Local browse retry, distinct partial/failure state, single-flight expansion, and continuation cleanup. |
| LENS-FUNC-01, LENS-FUNC-03 | Immutable application/GDS issuance targets and mutation-owned list refresh. |
| LENS-FUNC-02 | Persisted File System root intent independent of live attachments. |
| LENS-FUNC-04 | Owned benchmark operations and isolated run measurements. |
| LENS-FUNC-05, LENS-FUNC-06 | Browse pagination and annotation cursor/cancellation ownership. |
| LENS-UX-01, LENS-UX-02 | Owned Write execution and discoverable monitored-item validation. |
| LENS-UX-03, LENS-UX-04 | Persistent certificate mutation outcomes, explicit dialog dismissal, and cancellable rename drafts. |
| LENS-UX-05, LENS-UX-06, LENS-UX-07 | Accessible control names, theme-safe contrast, and keyboard lane-style selection. |
| LENS-QA-01 | Restore obsolete-API diagnostics and migrate the exposed calls. |
| LENS-QA-02, LENS-QA-03 | Executable desktop tests and native/installed-tool smoke checks, with nonzero-test and bounded-exit gates. |

The first architecture checkpoint passed 13 focused cases and the then-current
944-test Lens suite. After integrating the remaining fixes, the ordinary
Debug/net10.0 suite passes **1,120 tests**. The 28 focused monitor/File System
lifecycle cases also pass. These ordinary runs exclude the explicit desktop and
live-server fixtures.

| Follow-up check | Evidence and current boundary |
| --- | --- |
| Ordinary coverage | Final frozen-source run: 31,642 / 72,243 lines (43.79%) and 6,778 / 19,511 branches (34.73%), including generated sources. Unique existing application-source file/line pairs, excluding `obj`/`bin`: 20,218 / 53,062 (38.10%), compared with the baseline's 32.53%. |
| Native desktop | The strict 24-case lane passed at actual X11 render scales 1.0, 1.25, 1.5, and 2.0: 96 executions, zero failures/skips. Each cell retains nine verified PNGs and two contrast/color-vision CSV reports. |
| Owned Write shutdown | The new native regression exposed Avalonia's child-first close veto preventing shell cleanup. The shell now handles closing first and drains owned operations before closing their windows. All three Write-dialog cases pass. |
| Distributed Linux applications | Frozen-source NativeAOT publish/launch and managed-tool pack/fresh local-only install/launch passed. Both real desktop smoke checks exited zero with the required success marker. All 3,443 checked production-file hashes remained unchanged; installed assembly bytes matched the fresh package. |
| Diagnostics | Final net8.0/net9.0 Release, net10.0 Debug, native publish and managed pack passed with zero warnings/errors. A temporary external obsolete-call probe failed with `CS0618` and `EXTOBS0001`; the rebuild after removing the probe was clean. |

The desktop capture loop also exposed one-pixel signal traces without a solid
palette-colored interior. Production traces now use two-pixel strokes. Exported
PNGs use grayscale text antialiasing, restore the original rendering options,
and retain the eight-opaque-pixel/RGB-tolerance-2 assertion. These are live
client-visual captures, not physical-display or OS-compositor screenshots.

The native desktop commands, strict expected-count gate, DPI configuration and
evidence boundaries are documented in
[`DesktopTesting.md`](../tools/Opc.Ua.Lens/DesktopTesting.md). The artifact
commands and platform matrix are in the
[tool README](../tools/Opc.Ua.Lens/README.md).

Windows/macOS artifact execution, supported screen-reader announcements,
physical keyboard input, and mixed-monitor behavior have not been verified by
these Linux checks. Color-vision simulation is not human usability evaluation.
Tickets with those acceptance criteria remain open until the corresponding
evidence exists. Code, CI configuration and an ordinary managed test pass alone
do not close a ticket.

## Decision summary

Keep the existing composition, connection ownership, and workspace modules.
The review identifies **22 proposed tickets: two P1 and twenty P2**. The first
fixes should prevent GDS certificate delivery from using another application's
destinations (LENS-FUNC-01) and prevent retained monitor data from being
misattributed after item-handle reuse (LENS-ARCH-01). Both are concrete
data-integrity risks established from source, not new live-server reproductions.

The remaining work is mainly consistency: apply existing lifetime, dispatcher,
paging, and validation patterns to paths that bypass them; correct specific
desktop interactions; and validate the shipped desktop rather than equating a
managed test pass with GUI or NativeAOT readiness.

| Area | Tickets | Main concerns |
| --- | --- | --- |
| [Architecture/lifecycle](#architecture-and-lifecycle-tickets) | LENS-ARCH-01 through LENS-ARCH-06 | Stable identity, command ownership, dispatcher affinity, telemetry, browse retry, loss accounting |
| [Feature correctness](#feature-correctness-tickets) | LENS-FUNC-01 through LENS-FUNC-06 | GDS target association, retained roots, refresh ordering, benchmark completion, paging and cancellation |
| [GUI/interaction](#gui-and-interaction-tickets) | LENS-UX-01 through LENS-UX-07 | Write ownership, validation/results, dismissal, accessible names, contrast, keyboard chart controls |
| [Coding/delivery](#coding-and-delivery-tickets) | LENS-QA-01 through LENS-QA-03 | Diagnostic guards, executable desktop tests, native and managed artifact checks |

## Architecture and design assessment

The composition root is explicit:
[`Program.Main`](../tools/Opc.Ua.Lens/Program.cs#L115-L135) owns the service
container and awaits its disposal after the desktop lifetime ends.
[`AddUaLens`](../tools/Opc.Ua.Lens/UaLensServiceCollectionExtensions.cs#L53-L89)
registers connection ownership, telemetry, capability probing, document
coordination, commands, and the main view model through typed construction
delegates. This is preferable to service lookup throughout feature code and
should be preserved.

There are already useful replaceable seams:
`IWorkspaceDispatcher`, `ICapabilityProbe`, `IPluginFactory`, the connection
backend, and typed feature factories. The
[showcase registrations](../tools/Opc.Ua.Lens/UaLensShowcaseServiceCollectionExtensions.cs#L50-L82)
avoid starting network workloads during registration. Existing
[DI tests](../tests/Opc.Ua.Lens.Tests/Workspace/DependencyInjectionTests.cs#L42-L105)
check shared preferences, idempotent defaults, and fresh offline documents.

The architectural goal for follow-up work should be deeper modules, not more
interfaces for their own sake: callers should not need to coordinate session
generations, continuation-point ownership, UI-thread delivery, and cleanup
independently. Put each invariant behind the seam that already owns its
lifetime, and exercise it through that interface.

| Module | Current responsibility | Design direction |
| --- | --- | --- |
| `ConnectionService` | Primary session, credentials, replacement, and awaited state delivery | Preserve the single owner and the distinction between temporary transport loss and session replacement. |
| `DocumentWorkspace<TDocument>` | Document membership, queued work, cancellation, and drain-before-dispose | Route document mutations through its existing lifetime contract instead of creating competing ownership paths. |
| `PluginDocumentOperations` | Adapts workspace lifecycle to individual tools and monitor bindings | Keep session-generation changes behind this seam. |
| `SubscriptionViewModel` and subscription adapters | Retained monitor intent, live handles, and one capture task | Separate durable item identity from adapter-local handles; include user commands in lifetime tracking. |
| `NotificationRecorder` | Bounded retained history and independent readers | Preserve independent cursors and reconnect history, while retaining attribution metadata. |
| Browser and inspector view models | Session-backed exploration and selected-node data | Publish completed results on the dispatcher with explicit loading/error/retry state. |
| `LogRingBuffer` | Bounded telemetry handoff to the desktop | Return a coherent committed snapshot and cursor rather than exposing independent counters. |

Representative feature implementations also provide patterns worth reusing:
capability and structured-value paths check generations and distinguish failure
states; companion providers separate discovery, inspection, and execution;
several observation paths use bounded storage and awaited cleanup; the JSON
store writes a completed sibling file before replacing its destination.
The tickets target inconsistencies around these patterns, not a wholesale
replacement of the application architecture.

## GUI and interaction assessment

All nine checked-in [UaLens screenshots](../docs/Images/UaLens) were inspected. They show
a coherent engineering workspace: stable navigation, a persistent primary
connection strip, a resizable explorer, contextual node actions, and a searchable
tool catalog. The [workspace](../docs/Images/UaLens/workspace.png),
[catalog](../docs/Images/UaLens/tool-catalog.png), and
[monitor](../docs/Images/UaLens/monitoring.png) make this structure visible.
Connection state, quality, timestamps, and delivery counters have textual
representations; the UI does not rely solely on status colors.

The [trust dialog](../tools/Opc.Ua.Lens/Views/CertificateTrustDialog.axaml#L47-L69)
offers explicit choices with rejection as the default. Chart axes and legends
follow semantic theme resources, and the Trend screenshot identifies its
horizontal axis as a sample sequence rather than network latency. Standard
Avalonia controls retain useful keyboard and automation defaults. Existing
accessible document-close/catalog names, list virtualization, splitters,
panel toggles, and tab scrolling should not be discarded in a redesign.

The main weaknesses are operation ownership in some dialogs, inconsistent
cancel/error behavior, specific accessible-name gaps, and color choices that
are not safe for all their uses. Fix these at the existing presentation and
operation seams. Do not turn every click handler into a new interface or move
protocol orchestration into another large UI controller.

**Layout remains a validation question.** The
[minimum shell size](../tools/Opc.Ua.Lens/Views/MainWindow.axaml#L37-L38),
[explorer width](../tools/Opc.Ua.Lens/Views/MainWindow.axaml#L222-L231),
380-DIP [diagnostics pane](../tools/Opc.Ua.Lens/Views/DiagnosticsView.axaml#L34),
and [fixed monitor columns](../tools/Opc.Ua.Lens/Views/SubscriptionDocumentView.axaml#L80-L105)
warrant minimum-size/DPI checks in LENS-QA-02. At a 980-DIP shell width, the
default explorer, splitter, and diagnostics widths leave about 216 DIP before
document padding, versus 400 DIP of fixed monitor columns. This is a layout
constraint, not a verified clipping defect: existing resize/hide/scroll
mechanisms mitigate it, and no such layout was rendered during this review.

## Validation evidence

Environment: Linux, .NET SDK `10.0.401`.

| Check | Observed result | Interpretation |
| --- | --- | --- |
| Lens Debug/net10.0 tests with Coverlet | 931 passed, 0 failed | The ordinary test selection passes; explicit probes are not included. |
| Lens Release/net10.0 build | 0 warnings, 0 errors | Build succeeds with the project's existing suppressions. This is not a native publish or installed-tool test. |
| Whole-solution filtered coverage command | Lens tests passed; command returned failure from three unrelated NativeAOT test projects rejecting the VSTest target | Retried the Lens project directly and obtained a successful run. The solution command must not be described as passing. |
| Existing endpoint-picker desktop probe | Two explicit selectors produced zero executed tests | No desktop pass is claimed. The runner reported discovery followed by "No test matches"; the cause was not established. |
| Interactive workflow, screen-reader, DPI, and live chart inspection | Not performed | GUI findings use source and checked-in screenshots, not a full desktop usability session. |
| NativeAOT publish, installed tool, net8.0/net9.0, hardware identities, and external-server probes | Not performed | These remain separate validation obligations. |

The successful isolated run reported 27,605 / 70,654 covered lines (39.07%) and
5,864 / 19,051 covered branches (30.78%) for `UaLens`. The collector includes
generated `obj` sources. Grouping unique file/line pairs under existing
application source paths and excluding `obj`/`bin` gives 16,784 / 51,590 covered
lines (32.53%). These are execution measurements for this test selection, not a
claim about functional completeness or a new mandatory coverage threshold.

| Source area | Covered / executable lines | Line coverage |
| --- | ---: | ---: |
| Capabilities | 425 / 454 | 93.61% |
| Connection | 2,823 / 4,407 | 64.06% |
| Plugins | 9,913 / 28,282 | 35.05% |
| StructuredValues | 640 / 746 | 85.79% |
| Subscriptions | 73 / 1,020 | 7.16% |
| ViewModels | 1,041 / 2,747 | 37.90% |
| Views | 232 / 10,297 | 2.25% |
| Workspace | 827 / 1,047 | 78.99% |

The low execution of presentation and subscription paths is a reason to target
tests at the tickets, not to replace them with superficial line-count tests.
The test suite already contains meaningful connection, workspace, capability,
structured-value, and controlled PubSub checks. It is not merely a collection
of UI snapshots.

Reproduce the successful checks from the repository root, after restoring the
matching target graph if assets are missing:

```bash
dotnet test tests/Opc.Ua.Lens.Tests/Opc.Ua.Lens.Tests.csproj \
  --no-restore -p:CustomTestTarget=net10.0 \
  --collect:"XPlat Code Coverage" -m:2 \
  -- 'DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include=[UaLens]*'

dotnet build tools/Opc.Ua.Lens/Opc.Ua.Lens.csproj \
  --no-restore -c Release -f net10.0 -p:CustomTestTarget=net10.0 -m:2
```

## Ticket conventions

| Priority | Meaning |
| --- | --- |
| P1 | Correctness, data loss, resource ownership, or a major workflow failure. |
| P2 | Reliability, accessibility, testability, or demonstrated maintenance debt. |
| P3 | Usability or design improvement without a demonstrated correctness failure. |

**Static defect** means a concrete trigger and failing path were established
from source; it does not imply a new runtime reproduction was performed.
**Design debt** identifies a specific interface or maintenance problem.
**Validation gap** identifies missing evidence, not a claim that the feature
is broken. **Measured design shortfall** identifies a calculated mismatch
against an explicitly chosen usability benchmark. Acceptance criteria below
are proposed requirements for the fixes, not claims about existing behavior.

## Architecture and lifecycle tickets

### LENS-ARCH-01: Preserve notification identity across adapter replacement

**Priority:** P1. **Type:** Static defect. **Confidence:** High.

**Evidence:** `SubscriptionViewModel.AttachAdapterAsync` recreates retained
items with reset IDs and assigns the new adapter's IDs
([lines 391-425](../tools/Opc.Ua.Lens/ViewModels/SubscriptionViewModel.cs#L391-L425)),
while detach preserves recorded history. Both
[Classic](../tools/Opc.Ua.Lens/Subscriptions/ClassicEngineAdapter.cs#L165-L195)
and [V2](../tools/Opc.Ua.Lens/Subscriptions/ChannelV2EngineAdapter.cs#L153-L176)
allocate fresh local handles.
[NotificationEvent](../tools/Opc.Ua.Lens/Subscriptions/NotificationEvent.cs#L41-L47)
stores the local item ID without durable identity or generation. Export builds
a map from
[the current items](../tools/Opc.Ua.Lens/Views/NodeInteractionController.cs#L512-L550)
and applies it to
[historical records](../tools/Opc.Ua.Lens/Connection/NotificationRecorder.cs#L198-L270).

**Trigger and impact:** Monitor A and B as IDs 1 and 2, record values, remove A,
and replace the session or switch engines. B can become ID 1. Export then
attributes A's retained ID-1 values to B, while B's old ID-2 values lose their
name. This is silent data misattribution, not merely a display-order issue.

**Scope:** Introduce stable document item identity or immutable
generation-specific attribution. Keep history across reconnect; clearing it
would discard intended behavior rather than fix attribution.

**Acceptance criteria:**

1. CSV and JSON retain the original item attribution after removal, offline
   edits, repeated reconnects, and Classic/V2 switching.
2. Reuse of an adapter handle cannot merge different items' histories.
3. Tests reproduce the A/B removal sequence for both adapters and verify old
   and new records, including records for removed items.

### LENS-ARCH-02: Include monitor commands in document lifetime ownership

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** Monitor
[add/remove/configure methods](../tools/Opc.Ua.Lens/ViewModels/SubscriptionViewModel.cs#L547-L618)
call the adapter with `CancellationToken.None` and mutate retained items after
awaiting results. Disposal
[drains capture and detaches](../tools/Opc.Ua.Lens/ViewModels/SubscriptionViewModel.cs#L743-L788),
not outstanding user mutations. The workspace only drains
[work registered with its queue](../tools/Opc.Ua.Lens/Workspace/DocumentWorkspace.cs#L402-L450).
The UI
[invokes add commands directly](../tools/Opc.Ua.Lens/Views/NodeInteractionController.cs#L174-L205)
rather than registering them as workspace work.

**Trigger and impact:** Hold a Classic add response, then close the monitor or
replace its connection. Cleanup can dispose the old adapter before the command
completes. Its late continuation can still install an old handle into a closed
or rebound document. Concurrent item changes can also invalidate the collection
index captured by configuration.

**Scope:** Use the existing workspace work seam, or a document-owned mutation
tracker that detach/dispose drains. Apply it to direct callers as well as UI
commands. Existing queued initialization cleanup is a strength, not something
to replace.

**Acceptance criteria:**

1. Close and disconnect cancel and drain accepted mutations before adapter
   disposal.
2. Late results cannot mutate disposed documents, install previous-generation
   handles, or configure a different item.
3. Deferred-adapter tests cover add/remove/configure versus close and
   replacement, while preserving offline editing.

**Related:** LENS-ARCH-01 supplies stable identity but does not replace
cancellation and cleanup ownership.

### LENS-ARCH-03: Publish inspector rows through the dispatcher

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** `NodeAttributesViewModel.LoadAsync` awaits a network read with
`ConfigureAwait(false)` and then mutates bound `Rows`
([lines 76-145](../tools/Opc.Ua.Lens/ViewModels/NodeAttributesViewModel.cs#L76-L145)).
`ReferencesViewModel.LoadAsync` has the same sequence after browsing and reading
([lines 75-190](../tools/Opc.Ua.Lens/ViewModels/ReferencesViewModel.cs#L75-L190)).
The caller's
[UI-context await](../tools/Opc.Ua.Lens/Views/NodeInteractionController.cs#L142-L159)
does not marshal continuations inside those methods.

**Trigger and impact:** A genuinely asynchronous response can cause bound
collection notifications on a worker thread. Avalonia's
[threading guidance](https://docs.avaloniaui.net/docs/app-development/threading)
warns that such updates can fail or appear incomplete. Cancelling an earlier
selection does not itself establish dispatcher affinity.

**Scope:** Build result rows independently, then publish success or error state
through `IWorkspaceDispatcher`. Recheck the request identity and cancellation
inside the dispatched action.

**Acceptance criteria:**

1. All bound collection mutations, including error paths, run on the owning
   dispatcher.
2. An older delayed selection cannot publish after a new selection or clear.
3. Deferred fake reads completed on worker threads verify notification-thread
   access and complete, deterministic row sets.

### LENS-ARCH-04: Make telemetry publication and cursor advancement coherent

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** `LogRingBuffer.Add` increments the observable sequence before
storing the entry; writers do not take the lock used by `Snapshot`
([lines 67-92](../tools/Opc.Ua.Lens/Telemetry/LogRingBuffer.cs#L67-L92)).
`MainViewModel.PumpLog` separately reads the total and snapshot, then advances
its cursor from the earlier total
([lines 1126-1148](../tools/Opc.Ua.Lens/ViewModels/MainViewModel.cs#L1126-L1148)).

**Trigger and impact:** Pause a writer after incrementing the sequence. The UI
can observe that sequence, snapshot an old or uninitialized slot, and advance
past the unpublished entry. The later store does not increment the sequence
again, so the real entry can be missed. A write between the separate total and
snapshot reads also makes the consumer's window arithmetic inconsistent.

**Scope:** Publish payload and committed sequence together; return snapshot
window/cursor metadata atomically. Preserve bounded retention. A simple
synchronized module is preferable to an unverified lock-free protocol, and the
UI should not reconstruct the buffer's internals.

**Acceptance criteria:**

1. Snapshots contain only fully published entries and a matching cursor.
2. Concurrent writes cannot advance a consumer past unseen retained entries
   or duplicate a retained sequence.
3. Concurrent producer/consumer and wraparound tests distinguish intentional
   overwrite from publication loss.

### LENS-ARCH-05: Make failed address-space expansion retryable

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** `BrowserViewModel.LoadChildrenAsync` marks a node loaded before
the browse succeeds
([lines 210-220](../tools/Opc.Ua.Lens/ViewModels/BrowserViewModel.cs#L210-L220)),
skips bad per-result statuses
([lines 238-264](../tools/Opc.Ua.Lens/ViewModels/BrowserViewModel.cs#L238-L264)),
and clears children on exceptions without restoring retry state
([lines 299-309](../tools/Opc.Ua.Lens/ViewModels/BrowserViewModel.cs#L299-L309)).
The expansion callback permanently sets its first-load flag
([lines 699-705](../tools/Opc.Ua.Lens/ViewModels/BrowserViewModel.cs#L699-L705)).
Same-session recovery intentionally
[does not rebuild the tree](../tools/Opc.Ua.Lens/ViewModels/BrowserViewModel.cs#L138-L148).

**Trigger and impact:** Expand during a timeout, transport interruption, or
temporary browse rejection. The node becomes an apparent leaf that does not
retry on collapse/re-expand or same-session reconnect. Global refresh recovers,
but discards the user's expanded-tree context.

**Scope:** Distinguish loading, successfully empty, partial, and failed results.
Commit loaded state only after success and expose a local retry.

**Acceptance criteria:**

1. A failed browse is visibly different from an empty node.
2. Retry recovers that node without a whole-tree refresh; same-session recovery
   preserves successfully loaded branches.
3. Fail-first/succeed-second, bad-result, and continuation-failure tests verify
   retry behavior and prevent overlapping loads for one node.

### LENS-ARCH-06: Count actual channel evictions

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** The Classic adapter's capacity-8192 `DropOldest` queue
([lines 111-117](../tools/Opc.Ua.Lens/Subscriptions/ClassicEngineAdapter.cs#L111-L117))
increments its loss counter when occupancy is full *after* writing
([lines 325-377](../tools/Opc.Ua.Lens/Subscriptions/ClassicEngineAdapter.cs#L325-L377)).
V2 samples occupancy *before* writing, separately from the write
([lines 269-280](../tools/Opc.Ua.Lens/Subscriptions/ChannelV2EngineAdapter.cs#L269-L280)).
Neither observation is the eviction itself.

**Trigger and impact:** Exactly 8192 Classic writes into an empty undrained
queue report a drop even though none occurred. Interleaved consumption makes
occupancy sampling inaccurate in both adapters, misleading diagnosis of local
notification loss.

**Scope:** Count the queue's actual dropped-item callback; the
[bounded-channel overload](https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channel.createbounded?view=net-10.0)
supports this. Keep adapter delivery loss distinct from recorder history
retirement.

**Acceptance criteria:**

1. Capacity writes report zero drops; capacity plus N writes report N
   evictions when there is no reader.
2. Consumption interleavings do not count successful non-evicting writes as
   loss.
3. Parameterized Classic/V2 tests cover capacity-1, capacity, capacity+1,
   wraparound, and concurrent accounting.

## Feature correctness tickets

### LENS-FUNC-01: Bind certificate issuance and delivery to one application

**Priority:** P1. **Type:** Static defect. **Confidence:** High.

**Evidence:** `GdsManagementPlugin.IssueAndDeliverAsync` uses
`SelectedApp.ApplicationId` for the request, but independently takes subject,
domains, and delivery configuration from `Workspace.CurrentRegisteredApp`
([lines 773-819](../tools/Opc.Ua.Lens/Plugins/GdsManagement/GdsManagementPlugin.cs#L773-L819)).
Registration
[sets that workspace context](../tools/Opc.Ua.Lens/Plugins/GdsManagement/GdsManagementPlugin.cs#L597-L603);
[changing selection](../tools/Opc.Ua.Lens/Plugins/GdsManagement/GdsManagementPlugin.cs#L1910-L1939)
does not reconcile it. Pull delivery
[uses the context's paths](../tools/Opc.Ua.Lens/Plugins/GdsManagement/GdsManagementPlugin.cs#L1294-L1342),
and the file writer
[overwrites their contents](../tools/Opc.Ua.Lens/Plugins/GdsManagement/GdsManagementPlugin.cs#L1767-L1775).

**Trigger and impact:** Register application A with local certificate/key
destinations, select a different application B, and successfully issue B's
certificate. Delivery can overwrite A's files with material issued for B.
Checking only for a non-null context does not prevent the mismatch; concurrency
is not required.

**Scope:** Construct one immutable issuance target containing application
identity, GDS identity, and matching delivery configuration. Validate it before
any request or write. Put issuance/delivery orchestration behind an injectable
module rather than combining independent mutable selections.

**Acceptance criteria:**

1. A mismatched application or GDS context fails with an actionable message
   before remote issuance or delivery.
2. Matching targets preserve application and HTTPS certificate workflows.
3. Fake-client and recording-delivery tests cover A/A, B/A, absent context, and
   equal application NodeIds belonging to different GDS endpoints.

### LENS-FUNC-02: Persist File System roots independently of attachments

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** State capture
[serializes `m_userRoots`](../tools/Opc.Ua.Lens/Plugins/FileSystem/FileSystemState.cs#L54-L81),
while restore populates only `m_pendingRoots`. Connection transitions
[clear live roots and consume pending roots before attachment](../tools/Opc.Ua.Lens/Plugins/FileSystem/FileSystemPlugin.cs#L155-L192).
The existing
[offline restoration test](../tests/Opc.Ua.Lens.Tests/Administration/AdministrationPersistenceTests.cs#L192-L211)
even expects restored roots to disappear from a subsequent offline capture.

**Trigger and impact:** Load a saved workspace with custom roots while
disconnected, then save it before connecting: those roots disappear. After a
successful attachment, disconnect/reconnect also loses custom roots because
the pending list was consumed. This conflicts with retaining document
configuration across disconnection.

**Scope:** Keep a canonical collection of configured roots and derive live
attachments per session. Failed or cancelled attachment must not consume
persisted intent. Update the existing assertion along with the implementation;
adding a second conflicting test would not repair the contract.

**Acceptance criteria:**

1. Offline restore/capture preserves every configured root and label.
2. Reconnect attaches each configured root once, without duplicates.
3. Failure/cancellation preserves roots for capture and retry, verified with
   controlled attachment lifecycle tests.

### LENS-FUNC-03: Remove nested busy-guard no-ops from GDS refresh

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** `GdsPushPlugin.RefreshAsync`
[returns while busy](../tools/Opc.Ua.Lens/Plugins/GdsPush/GdsPushPlugin.cs#L617-L624),
but successful
[add](../tools/Opc.Ua.Lens/Plugins/GdsPush/GdsPushPlugin.cs#L730-L753)
and [remove](../tools/Opc.Ua.Lens/Plugins/GdsPush/GdsPushPlugin.cs#L778-L800)
operations call it before releasing their busy state. `GdsManagementPlugin`
has the same
[refresh guard](../tools/Opc.Ua.Lens/Plugins/GdsManagement/GdsManagementPlugin.cs#L507-L514)
and ordering in
[register](../tools/Opc.Ua.Lens/Plugins/GdsManagement/GdsManagementPlugin.cs#L589-L603)
and [unregister](../tools/Opc.Ua.Lens/Plugins/GdsManagement/GdsManagementPlugin.cs#L687-L699).

**Trigger and impact:** A successful mutation leaves its list stale even though
the command reports success. The intended refresh makes no read. Manual
refresh works after busy state is cleared, but does not repair the automatic
workflow.

**Scope:** Separate the externally guarded command from a cancellation-aware
refresh implementation. The outer operation owns busy state through mutation
and refresh. These four sites justify correcting shared orchestration; they
do not justify rewriting all GDS session handling.

**Acceptance criteria:**

1. Each successful mutation performs one refresh and updates the relevant list.
2. Busy remains set until both mutation and refresh finish.
3. Recording-client tests cover all four call sequences and distinguish
   successful mutation/failed refresh from failed mutation, so retry guidance
   does not imply that an already completed mutation should be repeated.

### LENS-FUNC-04: Finish benchmark runs only after their operations finish

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** `BenchmarkRunner.RunAsync`
[discards operation tasks](../tools/Opc.Ua.Lens/Plugins/Performance/BenchmarkRunner.cs#L266-L298).
Cancellation during semaphore acquisition bypasses draining; normal draining
stops after five seconds even if operations remain.
[`StopAsync`](../tools/Opc.Ua.Lens/Plugins/Performance/BenchmarkRunner.cs#L178-L207)
awaits only the scheduler, while
[late completions still emit samples](../tools/Opc.Ua.Lens/Plugins/Performance/BenchmarkRunner.cs#L355-L377).
Successive runners
[share plugin counters and histograms](../tools/Opc.Ua.Lens/Plugins/Performance/PerformancePlugin.cs#L852-L881)
without a callback run identity.

**Trigger and impact:** Hold service operations across cancellation or the
drain deadline, start a successor after completion is reported, then finish an
older operation. Its sample can enter the new run's measurements; the first
run's saved snapshot can omit pending completions.

**Scope:** Track and join issued tasks on all termination paths. If cleanup
times out, report that state rather than successful completion. Tag callbacks
by run identity and reject obsolete callbacks, but do not use filtering as a
substitute for resource ownership. The existing semaphore-release exception
catch does not establish task completion; Microsoft also documents that
[`SemaphoreSlim.Dispose`](https://learn.microsoft.com/dotnet/api/system.threading.semaphoreslim.dispose?view=net-10.0)
must not race other instance operations.

**Acceptance criteria:**

1. Successful run completion follows the final owned operation/sample,
   including cancellation paths.
2. A callback from an older run cannot alter its successor's results.
3. Deferred Write/Call tests and a controlled deadline verify completion,
   timeout reporting, and cross-run isolation without sleep-based races.

### LENS-FUNC-05: Follow browse continuation points in Subscription Bench

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** `WalkVariablesAsync`
[processes only the first reference page](../tools/Opc.Ua.Lens/Plugins/SubscriptionBench/SubscriptionBenchPlugin.cs#L402-L470)
and never consumes or releases its continuation point.
Both [subtree picking](../tools/Opc.Ua.Lens/Plugins/SubscriptionBench/SubscriptionBenchPlugin.cs#L354-L399)
and [node seeding](../tools/Opc.Ua.Lens/Plugins/SubscriptionBench/SubscriptionBenchPlugin.cs#L500-L545)
depend on it. The underlying
[`ManagedSession.BrowseAsync`](../src/Opc.Ua.Client/Session/ManagedSession.Services.cs#L106-L120)
forwards one response; it does not automatically collect later pages.

**Trigger and impact:** A server paginates references. Variables and descendant
folders after page one never enter the benchmark pool, yet the collected count
does not disclose incomplete discovery. Server continuation points are
abandoned. Depth limits and visited-node tracking do not address pagination.

**Scope:** Reuse or extract the bounded paging/cleanup behavior already present
in
[`IndustrialCompanionAccess.BrowseAsync`](../tools/Opc.Ua.Lens/Plugins/Companions/Providers/IndustrialCompanionAccess.cs#L170-L238).
Keep cancellation, cleanup, traversal budgets, and partial-result reporting
inside that seam.

**Acceptance criteria:**

1. Multipage and equivalent single-page trees produce the same unique pool.
2. Failure/cancellation releases outstanding continuation points.
3. Tests put a variable and a folder with descendants on page two, and cover
   BrowseNext failure and budget exhaustion with an incomplete-result status.

### LENS-FUNC-06: Preserve cancellation and cursor ownership in annotation reads

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** `HistoryReader.AttachAnnotationsAsync`
[catches all exceptions](../tools/Opc.Ua.Lens/Plugins/Historian/HistoryReader.cs#L174-L185),
including cancellation.
[Annotation paging](../tools/Opc.Ua.Lens/Plugins/Historian/HistoryReader.cs#L285-L324)
holds a continuation point without failure/cancellation cleanup.
The [plugin](../tools/Opc.Ua.Lens/Plugins/Historian/HistorianPlugin.cs#L773-L793)
expects cancellation to propagate, then posts rows without another check.
The primary read path already has
[cursor cleanup](../tools/Opc.Ua.Lens/Plugins/Historian/HistoryReader.cs#L424-L461),
but annotation paging bypasses it.

**Trigger and impact:** Primary values finish, an annotation page returns a
cursor, and the user cancels the next annotation request. Cancellation is
swallowed, rows are still committed, and the annotation cursor is left for
server/session cleanup.

**Scope:** Propagate cancellation separately from optional-feature errors,
release outstanding cursors with a bounded independent cleanup token, and
recheck cancellation before publishing results. Consolidate cursor ownership
without making unsupported annotations fatal to a valid primary read.

**Acceptance criteria:**

1. Annotation cancellation reaches the cancelled outcome and prevents that
   operation's row commit.
2. Failure/cancellation explicitly releases outstanding annotation cursors.
3. Fake-history tests cover primary success, an annotation cursor followed by
   cancellation, and unsupported annotations on an uncancelled read.

## GUI and interaction tickets

### LENS-UX-01: Give the Write dialog one owned operation

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** Each Write activation
[starts `OnWriteAsync`](../tools/Opc.Ua.Lens/Views/WriteValueDialog.axaml.cs#L63-L90);
there is no in-flight guard, the request uses `CancellationToken.None`, and
[success closes the dialog after 450 ms](../tools/Opc.Ua.Lens/Views/WriteValueDialog.axaml.cs#L211-L289).
Close dismisses immediately. The
[caller awaits the window](../tools/Opc.Ua.Lens/Views/NodeInteractionController.cs#L405-L412),
not outstanding writes. By contrast,
[`MethodCallDialog`](../tools/Opc.Ua.Lens/Views/MethodCallDialog.axaml.cs#L86-L139)
already tracks work and owns shutdown.

**Trigger and impact:** Repeated activation during a slow response can send
multiple writes. Closing leaves the request without dialog-owned completion,
and a successful result has only a brief display lifetime. Server reordering
or rollback was not demonstrated.

**Scope:** Introduce single-flight, lifetime-owned execution through an
injectable operation model. Reuse the Method Call ownership pattern. Preserve
metadata/type validation and inline status explanations. Cancellation after
dispatch must not be described as remote rollback.

**Acceptance criteria:**

1. Repeated pointer or keyboard activation while pending sends one request.
2. Editing, writing, and closing states are explicit; closing causes no late
   updates to the dismissed view.
3. Success, failure, or uncertain remote outcome remains available until
   acknowledgment.
4. Deferred-session tests cover duplicate activation, close-before-response,
   metadata failure, and cancellation; the desktop lane verifies keyboard
   operation and result discovery.

### LENS-UX-02: Explain invalid Add Item parameters

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** Invalid sampling text, invalid/negative deadband, and percentage
deadband above 100
[return silently](../tools/Opc.Ua.Lens/Views/AddItemDialog.axaml.cs#L91-L114).
The [form](../tools/Opc.Ua.Lens/Views/AddItemDialog.axaml#L52-L87) has no
validation surface. The separate
[item-settings flow](../tools/Opc.Ua.Lens/Views/MonitoredItemSettingsDialog.axaml.cs#L108-L148)
already provides explanations that can be reused.

**Trigger and impact:** Enter `abc` for sampling or `101` for percentage
deadband, then press OK. The dialog stays open without explaining the rejected
field, making the command appear unresponsive.

**Scope:** Share applicable validation rules, preserve entered values, identify
the invalid field, and focus the first correction target. The
[screenshot](../docs/Images/UaLens/monitored-item.png) establishes the form's appearance,
not its invalid-input behavior.

**Acceptance criteria:**

1. Every rejection names the field and accepted format/range without adding
   an item or closing the dialog.
2. Correcting input clears the error and permits one add.
3. Tests cover invalid text, negative deadband, percentage overflow, and valid
   boundaries; keyboard users and automation peers can discover the errors.

### LENS-UX-03: Preserve certificate mutation outcomes across refresh

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** Certificate trust/clear actions
[set a result and then reload](../tools/Opc.Ua.Lens/Views/CertificateStoreDialog.axaml.cs#L116-L150).
[`ReloadAsync`](../tools/Opc.Ua.Lens/Views/CertificateStoreDialog.axaml.cs#L162-L180)
overwrites that same status with a certificate count.
[Untrust/delete-expired](../tools/Opc.Ua.Lens/Views/CertificateStoreDialog.axaml.cs#L184-L208)
has the same ordering. The dialog is reachable through
[Manage Certificates](../tools/Opc.Ua.Lens/Views/ConnectionController.cs#L428-L435).

**Trigger and impact:** A mutation fails and subsequent listing succeeds. The
failure explanation is replaced with a message such as "Trusted: N
certificate(s)", obscuring whether the requested mutation succeeded. Actions
are already explicitly initiated and labeled; this is not an unauthorized
mutation finding.

**Scope:** Separate loading status from operation outcome and retain partial
success/failure independently of refresh. As a separate design improvement,
bulk deletion/trust removal can offer a scope review identifying the store and
affected targets before execution; lack of that preview alone is not the
confirmed defect.

**Acceptance criteria:**

1. Successful listing cannot erase a mutation failure.
2. Results identify the operation/store and distinguish attempted, succeeded,
   and failed items.
3. Results remain until acknowledgment or an explicit subsequent operation.
4. Fake-store tests cover successful listing after failed mutation, partial
   bulk failure, and delayed refresh.

### LENS-UX-04: Correct Close and Escape semantics

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** The certificate dialog's
[Close button](../tools/Opc.Ua.Lens/Views/CertificateStoreDialog.axaml#L171)
has `IsCancel="True"`, but
[`WireUp`](../tools/Opc.Ua.Lens/Views/CertificateStoreDialog.axaml.cs#L84-L152)
attaches no closing handler or command. Catalog
[Cancel](../tools/Opc.Ua.Lens/Views/ToolCatalogDialog.axaml#L76-L80)
lacks `IsCancel`. Document rename
[binds directly to `Title`](../tools/Opc.Ua.Lens/Views/MainWindow.axaml#L337-L339),
and
[Enter/Escape handling](../tools/Opc.Ua.Lens/Views/ShellPresenter.cs#L274-L289)
ends editing without restoring the original on Escape.

**Trigger and impact:** Certificate Close/Escape does not execute dismissal;
catalog Escape is inconsistent with neighboring dialogs; Escape during rename
retains the edit. Window-manager close and catalog Cancel-by-click remain
available. In the version-matched
[Avalonia Button implementation](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Controls/Button.cs),
cancel-key handling invokes the click path; it does not supply the
application's missing close action.

**Scope:** Wire Close explicitly, enable catalog Escape cancellation, and keep
a rename draft/original value until commit.

**Acceptance criteria:**

1. Certificate Close and Escape dismiss; catalog Escape cancels without
   opening a tool and follows existing disposal.
2. Rename Enter commits; Escape restores the prior title.
3. Desktop tests exercise click, Space/Enter, Escape, and useful focus
   restoration independently.

### LENS-UX-05: Name glyph actions and associate parameter labels

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** The
[connection strip](../tools/Opc.Ua.Lens/Views/MainWindow.axaml#L141-L167)
has a visually adjacent Server label and glyph-based history/favorite/settings
actions. [Add Item inputs](../tools/Opc.Ua.Lens/Views/AddItemDialog.axaml#L52-L82)
and [monitor controls](../tools/Opc.Ua.Lens/Views/SubscriptionDocumentView.axaml#L36-L45)
have visual labels without accessible associations.
The [diagnostics hide button](../tools/Opc.Ua.Lens/Views/DiagnosticsView.axaml#L37-L39)
also uses glyph content.

**Trigger and impact:** Direct screen-reader navigation can encounter unnamed
parameter inputs or glyph-named actions. This does not mean all those controls
lack accessibility: roles, focus indicators, IDs from `x:Name`, tooltip help,
and ordinary text-button names already exist. Adjacent text and tooltip
HelpText are not substitutes for the control's semantic Name, as the
[version-matched automation peer](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Controls/Automation/Peers/ControlAutomationPeer.cs)
and
[TextBox peer](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Controls/Automation/Peers/TextBoxAutomationPeer.cs)
show.

**Scope:** Associate actual labels with fields and name glyph actions.
Preserve stable automation identifiers and helpful tooltips. Follow
[Avalonia accessibility guidance](https://docs.avaloniaui.net/docs/app-development/accessibility).

**Acceptance criteria:**

1. Automation peers expose the action/parameter and applicable units.
2. Names remain meaningful as values and connection state change; existing
   IDs and help text remain stable.
3. Peer-level assertions and supported screen-reader checks cover connection,
   Add Item, and monitor controls. Do not infer runtime announcements solely
   from a markup-property count.

### LENS-UX-06: Correct status-text and chart-series contrast

**Priority:** P2. **Type:** Measured design shortfall. **Confidence:** High.

**Evidence:** Semantic colors are defined in
[Light](../tools/Opc.Ua.Lens/Themes/Light.axaml#L33-L54),
[Dark Standard](../tools/Opc.Ua.Lens/Themes/DarkStandard.axaml#L33-L54), and
[Dark Navy](../tools/Opc.Ua.Lens/Themes/DarkNavy.axaml#L33-L54).
Consumers include
[warning/error text](../tools/Opc.Ua.Lens/Views/MainWindow.axaml#L172-L191)
and the [filter heading](../tools/Opc.Ua.Lens/Views/AddItemDialog.axaml#L56-L61).
The [fixed series palette](../tools/Opc.Ua.Lens/Views/ItemColors.cs#L45-L94)
is used against [theme-dependent chart surfaces](../tools/Opc.Ua.Lens/Themes/ChartTheme.cs#L40-L61).

| Source-color pair | Calculated contrast |
| --- | ---: |
| Light warning `#EAB308` / white | 1.918:1 |
| Light heading `#0891B2` / `#F3F4F6` | 3.346:1 |
| Light error `#DC2626` / `#E7EFFF` | 4.183:1 |
| Dark Standard error `#EF4444` / `#29384F` | 3.148:1 |
| Dark Navy error `#EF4444` / `#0E2A47` | 3.873:1 |
| Amber series `#F59E0B` / Light chart `#F8F9FB` | 2.039:1 |
| Cyan series `#06B6D4` / Light chart `#F8F9FB` | 2.305:1 |
| Lime series `#D9F99D` / Light chart `#F8F9FB` | 1.108:1 |

**Impact and scope:** Small status text and several series lack sufficient
foreground/background separation against the chosen benchmark. Primary text
and theme-aware axes/legends already perform better. Separate text-safe status
colors from decorative accents and provide theme-appropriate series colors
without changing item identity.

Ratios use source colors and the W3C luminance formula, not screenshot pixels.
The proposed targets follow
[text contrast](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html)
and [non-text contrast](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html)
guidance. [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) informs application to
desktop software; this review is not a legal conformance determination.

**Acceptance criteria:**

1. Normal-size semantic text reaches 4.5:1 on its actual backgrounds.
2. Essential series graphics reach 3:1 or have an equivalent distinguishable
   presentation.
3. Theme changes preserve data, axes, and item identification.
4. Automated palette checks and desktop captures cover error/warning states
   and all series colors in Light/Dark, including color-vision evaluation.

### LENS-UX-07: Make Timing: lines style selection keyboard accessible

**Priority:** P2. **Type:** Static defect. **Confidence:** High.

**Evidence:** `AnimationCanvas`
[enables hit testing but is not focusable](../tools/Opc.Ua.Lens/Views/AnimationCanvas.cs#L237-L245).
Lane styles use
[painted labels](../tools/Opc.Ua.Lens/Views/AnimationCanvas.cs#L963-L972)
and [pointer-only hit rectangles](../tools/Opc.Ua.Lens/Views/AnimationCanvas.cs#L1186-L1224).
The normal [chart options](../tools/Opc.Ua.Lens/Views/SubscriptionDocumentView.axaml#L69-L78)
have no equivalent style selector; the
[Timing: lines binding](../tools/Opc.Ua.Lens/Views/SubscriptionDocumentView.axaml.cs#L261-L294)
selects this renderer.

**Trigger and impact:** A keyboard-only user cannot select a lane's style.
Pointer users must discover the clickable painted label and interpret its
glyph. Latest values and zoom already have standard controls; the finding is
about this specific operation, not every chart interaction.

**Scope:** Add a labeled, focusable selected-item style selector and keep
pointer cycling as a shortcut over the same state.

**Acceptance criteria:**

1. Every lane style is selectable without a pointer, with item and full style
   names exposed.
2. Keyboard and pointer selections remain synchronized.
3. Tests verify every style and demonstrate that presentation changes do not
   alter subscription configuration or recorded samples.

## Coding and delivery tickets

### LENS-QA-01: Restore the obsolete-API diagnostic guard

**Priority:** P2. **Type:** Design debt. **Confidence:** High.

**Evidence:** The application
[project file](../tools/Opc.Ua.Lens/Opc.Ua.Lens.csproj#L18-L24) globally suppresses
`CS0618` and `EXTOBS0001`, in addition to `CA1812`. The repository's
[coding standards](../docs/DeveloperGuide.md#coding-standards-dos-and-donts) prohibit
new use of obsolete APIs outside test code. The successful build therefore
does not establish compliance with that rule.

**Impact and scope:** A newly introduced obsolete call can pass unnoticed.
Inventory existing hits and migrate supported replacements. Keep any necessary
compatibility exception local and explained; do not remove compatibility
behavior merely to make the diagnostics disappear. This finding concerns the
disabled guard, not an assertion that every suppressed call is defective.

**Acceptance criteria:**

1. Remove the application-wide `CS0618` and `EXTOBS0001` suppressions.
2. Migrate each resulting diagnostic or document a narrow exception with the
   repository-required reason and follow-up TODO.
3. Build the supported desktop targets with analyzers enabled; a newly added
   obsolete call must be visible to the normal validation command.

### LENS-QA-02: Add an executable desktop regression lane

**Priority:** P2. **Type:** Validation gap. **Confidence:** High.

**Evidence:** The
[endpoint-picker fixture](../tests/Opc.Ua.Lens.Desktop.Tests/Connection/EndpointCredentialsPickerDialogTests.cs)
is explicitly selected, requires a desktop and STA, and exercises one modal
accept/cancel path. The ordinary 931-test run does not exercise it. Both
attempted selections in this review executed zero tests. The
[theme tests](../tests/Opc.Ua.Lens.Tests/Themes/ThemeDictionaryTests.cs#L38-L75)
check resource loading and chart state preservation, not full keyboard,
layout, or assistive-technology behavior.

**Impact and scope:** A passing unit run can coexist with a broken binding,
inaccessible action, unusable small-window layout, or modal regression.
Establish a supported desktop runner and test the actual shell and critical
dialogs. Reuse state-level tests for logic; add headless control tests only
where the platform behavior they omit is immaterial.

**Acceptance criteria:**

1. The CI desktop lane reports a nonzero expected test count and fails on
   zero tests, unexpected skips, or a missing display.
2. Cover connection selection/cancel, tool opening and closing, keyboard
   document navigation, save/load, and shutdown with task-owned windows.
3. Add repeatable layout/focus checks at the supported minimum window size and
   a documented DPI/theme matrix. Keep screen-reader checks distinct from
   mere control construction.
4. Publish runner prerequisites and a command that actually executes the
   explicit modal fixture on its supported platform.

### LENS-QA-03: Validate both distributed artifacts in CI

**Priority:** P2. **Type:** Validation gap. **Confidence:** High.

**Evidence:** The
[project](../tools/Opc.Ua.Lens/Opc.Ua.Lens.csproj#L18-L41) declares AOT
compatibility, enables NativeAOT publishing on net10.0, and also defines a
managed `ualens` tool package. The
[README](../tools/Opc.Ua.Lens/README.md#L45-L61) correctly describes separate
native-publish and managed-pack workflows. The inspected
[GitHub AOT job](../.github/workflows/buildandtest.yml#L607-L648) names the
stack, historian, and MCP test applications, while the
[historical Azure AOT matrix](https://github.com/OPCFoundation/UA-.NETStandard/blob/fe612275e/.azurepipelines/test-aot.yml#L45-L50) selects
`Opc.Ua.Aot.Tests*.csproj`; none of those test projects references Lens.

**Impact and scope:** A normal build does not validate the trimmed desktop,
native resources, or an installed tool package. This is not evidence that
either distribution is currently broken. Microsoft's
[Native AOT guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
distinguishes analyzer compatibility checks from publishing for a concrete RID.

**Acceptance criteria:**

1. Publish and launch the native desktop on each explicitly supported
   distribution platform, with trimming/AOT diagnostics retained.
2. Pack the managed tool, install it into an isolated tool directory from the
   local artifact, and execute a bounded startup/exit check.
3. Exercise resource loading, a document factory, and a representative chart
   in the distributed application, rather than testing only its managed
   project reference.
4. Keep artifact-generation settings separate and document the supported
   platform matrix. Do not advertise an untested platform as validated.

## Implementation order and completion criteria

1. Address LENS-FUNC-01 and LENS-ARCH-01 first, with fake-client/adapter tests
   that reproduce the mismatched destination and historical-attribution
   scenarios. Do not exercise certificate mutation against a production GDS.
2. Close the lifetime and result-integrity gaps: LENS-ARCH-02 through
   LENS-ARCH-06, LENS-FUNC-02 through LENS-FUNC-06, and LENS-UX-01. Reuse the
   workspace work queue, dispatcher, bounded paging, and owned-task patterns.
   Avoid separate cancellation or cursor conventions in each document.
3. Implement the remaining interaction fixes alongside LENS-QA-02. The desktop
   lane enables their validation; it need not block a focused source fix.
   Contrast calculations and automation-peer assertions can run before a
   desktop runner is available.
4. Restore diagnostic guards and validate distribution artifacts through
   LENS-QA-01 and LENS-QA-03. Rebaseline coverage after adding behavioral tests,
   keeping generated and application-source measurements separate.

A ticket is complete when its acceptance criteria and targeted regression
tests pass, the existing Lens suite still passes, and applicable desktop or
artifact checks actually execute. A zero-test result, screenshot of a different
workflow, swallowed cleanup failure, or normal managed build is not a substitute
for the check required by that ticket.

This review did not exhaustively inspect every feature or perform a separate
security audit, hardware validation, load campaign, or server-interoperability
assessment. Existing screenshots are appearance evidence for their captured
states only. The proposed regression scenarios were not added or executed as
part of this documentation task; only the baseline checks listed above ran.
