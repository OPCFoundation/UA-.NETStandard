<!-- SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL -->
<!-- SPDX-License-Identifier: MIT -->

# WoT test suite redesign (`tests/Opc.Ua.WotCon.Tests`)

## Problem

`Opc.Ua.WotCon.Tests` is the largest server-backed suite in the repository
(3,334 cases on net10.0, 3,221 on net48). On master `d8e5e29cd`
(CI run 45011528803, 2026-11-22) it needed **16 CI jobs and about 467
runner-minutes per CI run**, and its slowest job (macOS
`wot-materialization-integration`, 48 minutes including the build) was within
a few minutes of the 45-minute per-project ceiling. The macOS layout had grown
to eight hand-tuned shards keyed on class names
(`WotEventModesLiveTests`, `WotRegistryIdentityLiveTests(True)`,
`WotLegacy*`), and every new slow class needed another shard.

## Measurements before the change

### CI, master run 45011528803 (test execution from the TRX `Times`, job length from GitHub)

| Profile | Shard | Cases | Test wall | Sum of test durations | Job |
|---|---|---:|---:|---:|---:|
| Windows net48 | wot-materialization-core | 880 | 12.4 min | 12.4 min | 22.1 min |
| Windows net48 | wot-materialization-integration | 393 | 22.5 min | 22.4 min | 29.4 min |
| Windows net48 | wot-other-core | 1,563 | 8.1 min | 8.2 min | 19.7 min |
| Windows net48 | wot-other-integration | 385 | 24.9 min | 24.9 min | 31.9 min |
| Windows net11.0 | wot-materialization | 1,395 | 23.3 min | 22.5 min | 36.0 min |
| Windows net11.0 | wot-other | 1,948 | 19.9 min | 19.2 min | 32.8 min |
| Linux net11.0 | wot-materialization | 1,395 | 17.8 min | 17.1 min | 29.3 min |
| Linux net11.0 | wot-other | 1,948 | 12.5 min | 12.1 min | 19.3 min |
| macOS net11.0 | wot-materialization-core | 924 | 16.9 min | 14.3 min | 28.5 min |
| macOS net11.0 | wot-materialization-integration | 397 | 34.2 min | 32.4 min | 48.4 min |
| macOS net11.0 | wot-materialization-live-events | 74 | 24.0 min | 21.5 min | 35.4 min |
| macOS net11.0 | wot-other-core | 1,563 | 10.3 min | 7.8 min | 21.9 min |
| macOS net11.0 | wot-other-integration | 144 | 20.9 min | 18.4 min | 33.6 min |
| macOS net11.0 | wot-other-identity-integration | 57 | 14.3 min | 12.0 min | 26.0 min |
| macOS net11.0 | wot-other-identity-di-integration | 57 | 12.6 min | 11.2 min | 22.0 min |
| macOS net11.0 | wot-other-legacy-integration | 127 | 17.3 min | 15.0 min | 30.6 min |

Totals per profile (test wall / job minutes): Windows net48 67.9 / 103.1,
Windows net11.0 43.2 / 68.8, Linux net11.0 30.3 / 48.6, macOS net11.0
150.5 / 246.3.

### Where the time went

* **Wall time equals the sum of the test durations in every shard.** The
  assembly declared no parallelism, so NUnit ran fixtures and tests one at a
  time; 25 fixtures and seven test methods were additionally marked
  `[NonParallelizable]` without a recorded reason.
* **A real server per test.** Most cases of the 44 classes that use a server helper start a `ReferenceServer`
  (directly, through `PreparedWotTestRuntime`, or through helper classes such
  as `NativeEndpoint`), usually with a client session. Per-case averages on
  macOS: `WotEventModesLiveTests` 17.4 s (two servers), the registry identity
  fixtures 12 s, `WotConfiguredOptionsTests` 10 s, `WotLegacyNativeAdmissionLiveTests`
  7 s, `WotAtomicPublicationNativeTests` 4.3 s (303 cases, 22 minutes).
* **Fresh certificates for every server and client.** `ServerFixture` creates
  an RSA certificate and up to four ECC certificates (P-256, P-384, two
  brainpool curves) when its store is empty, and each `ClientFixture` creates
  another set. Every live test used a new temporary store. A probe on the
  development machine (net10.0, six iterations each, first iteration
  excluded) measured one server start + client connect + stop at
  **1.6-2.5 s with fresh certificates, 1.0-1.4 s with seeded certificates**,
  and 0.6-0.7 s for a seeded plain `StandardServer`. RSA and ECC key
  generation is several times slower on the macOS runners, which is why the
  macOS live cases took 3-4 times as long as on Linux.
* Real time is not a cost driver. The slowest individual cases (4-20 s) are
  dominated by server lifecycles; time-related code in the live tests bounds
  gate waits (`TaskCompletionSource` with a 5-20 s failure deadline) or polls
  a condition every 25 ms, so it only costs time when a test fails.
* Private reflection is limited to 14 call sites in nine files and does
  not affect run time.
* The `Samples/` tests (133 cases, about 1.6 minutes) re-read the checked-in
  sample sets per test (see "Sample input cache" below: the cost is the
  conversions, not the reads); the 20-second
  `ManifestDocumentsMatchCompleteAsyncRegeneration` is the slowest single case
  of the suite and stays as is.

### Inventory (net10.0, local baseline)

| Folder | Classes | Cases | Serial minutes (local) |
|---|---:|---:|---:|
| (root, mostly registry and legacy live tests) | 36 | 860 | 19.3 |
| `Materialization/` | 59 | 1,395 | 16.6 |
| `RuntimeNodeSet/` | 3 | 47 | 2.1 |
| `Samples/` | 7 | 133 | 1.6 |
| `Hosting/` | 8 | 117 | 0.7 |
| `Registry/` | 18 | 518 | 0.5 |
| `Client/` | 10 | 172 | 0.1 |
| `Binding/` | 5 | 84 | <0.1 |
| other namespaces | 2 | 17 | <0.1 |

`[Category("Integration")]` was applied inconsistently: 25 of the 44 classes
that start a server did not carry it (for example
`WotMaterializationCoordinatorTests`, `WotObservedPropertyRuntimeTests`,
`WotRegistryLifecycleTests`), so the `*-core` shards were not server-free and
the category split did not separate fast from slow tests.

Local baseline (development machine, net10.0, Release, shared with other
sessions' builds): **3,334 passed, 40 min 51 s.**

## Design

The redesign keeps every case and every category. It removes the two
structural costs instead of rewriting tests.

1. **One certificate set per process (`Support/WotTestPki.cs`).** Before a
   server start or client configuration load, a test seeds its store from a
   template that is created once per test process and identity, then copied
   (a few milliseconds). Each test still owns its copy, so trust lists,
   rejected certificates and deletions stay private. Tests that need servers
   which authenticate as *different* authorities seed them with different
   identities; `WotEventModesLiveTests.NativeEndpoint` takes the next identity
   for each endpoint of a test, which keeps the authority-isolation cases
   (`NativeTransparentAuthoritiesCannotAliasAsync` and two others) meaningful.
   Federation proxy servers with custom application URIs are not seeded.
2. **Parallel by default.** `Properties/AssemblyInfo.cs` declares
   `Parallelizable(ParallelScope.All)`, `FixtureLifeCycle(InstancePerTestCase)`
   and `LevelOfParallelism(4)`. Every test already created its servers,
   clients, stores and folders in `[SetUp]` or in the test body, so a fixture
   instance per test case is all the isolation they need; the one instance
   `[OneTimeTearDown]` (`WotAtomicPublicationNativeTests`) now keeps its store
   under the per-test root. Unjustified `[NonParallelizable]` attributes are
   removed. Two remain: `WotPreparedMetadataReviewTests` changes the process
   working directory, and `WotCompanionModelMaterializationTests` holds the
   explicit writers that rewrite the checked-in sample documents.
3. **Deterministic time.** No conversion to `FakeTimeProvider` was needed:
   the live tests are gate-driven, and their timeouts only bound failures.
   New timing-dependent unit tests should still take a `TimeProvider`.
4. **CI layout by namespace only.** Windows and Linux net11.0 run the whole
   project in one entry (`all`). net48 and macOS run two exhaustive,
   disjoint entries, `wot-materialization` (`FullyQualifiedName~...Materialization.`)
   and `wot-other`. No class names or categories appear in the filters any
   more, so a new slow class does not need a new shard.

### Intentional removals

None. The case inventory (3,334 on net10.0) and all categories are unchanged.

## Measurements after the change

Local, same machine and build, net10.0 Release (the machine was shared with
other sessions' builds and test runs, so the numbers vary by about ±20 %):

| Run | Result | Wall |
|---|---|---:|
| Baseline (master) | 3,334 passed | 40 min 51 s |
| Seeded certificates + parallel, run 1 | 3,331 passed, 3 failed (authority isolation, fixed by identities) | 9 min 11 s |
| Seeded certificates + parallel, run 2 | 3,334 passed | 12 min 28 s |
| Seeded certificates + parallel, run 3 | 3,334 passed | 10 min 36 s |
| Seeded certificates, serial (`NUnit.NumberOfTestWorkers=0`) | 3,334 passed | 23 min 47 s |

`WotEventModesLiveTests` alone: 74 passed in 1 min 9 s (about 4 minutes
serially before).

net48 (same machine, Release, `dotnet vstest`): **3,212 passed in
40 min 19 s**, with the sum of test durations at 161 minutes (four tests in
flight throughout). A .NET Framework server is more CPU-bound per test than on
.NET 10; raising the thread pool minimum from 10 to 32 made no difference
(`WotRegistryIdentityLiveTests`, 114 cases: 6 min 0 s vs 5 min 56 s), so the
net48 profile keeps two shards.

CI after the change, PR run 45149363008 (head `43e265e`, same coverage
settings as the master run: coverage on net11.0, off on net48; the PR profile
has no macOS entries):

| Profile | Entries | Test wall (before → after) | Sum of test durations | Job minutes (before → after) |
|---|---:|---:|---:|---:|
| Linux net11.0 | 2 → 1 | 30.3 → **16.8 min** | 64.4 min | 48.6 → **27.2** |
| Windows net11.0 | 2 → 1 | 43.2 → **14.3 min** | 54.3 min | 68.8 → **21.7** |
| Windows net48 | 4 → 2 | 67.9 → 48.6 min (longest entry 24.9 → 25.9) | 193.0 min | 103.1 → **70.4** |

Windows and Linux net11.0 take a third to half of the runner minutes they
took before. net48 gains less: its four parallel tests take almost three times
as long each as they do serially, so its entries are no shorter. The two-entry
layout still saves a third of its runner minutes, mostly by building twice
instead of four times. Lowering net48's parallelism or giving it more entries
is a separate trade-off between wall time and runner minutes.

CI shape: 6 instead of 16 WotCon jobs per CI run
(Windows net11.0 1, Linux net11.0 1, Windows net48 2, macOS net11.0 2),
which also drops ten build-and-setup overheads of 8-14 minutes each.

## `WotAtomicPublicationNativeTests` on a plain server

Follow-up change on top of the redesign. All 303 cases and their names are
kept, and no assertion was changed or removed.

1. **`StandardServer` instead of `ReferenceServer`.** None of the 23 partial
   files uses the reference server's node managers, users, roles or
   namespace. Namespace indices are looked up by URI, and the `ns=1` in the
   inline NodeSets is local to each NodeSet. The only dependency was
   indirect: the recovery cases restart into a `PreparedWotTestRuntime`, and
   the server type names the application, so a `ReferenceServer` restart saw
   a different server URI than the one in the committed canonical view graph
   (6 failures). `PreparedWotTestRuntime.StartAsync<T>(factory)` now starts a
   runtime of the fixture's own type, and the native fixture restarts with
   `StandardServer`. No case still needs `ReferenceServer`.
2. **One client configuration per fixture.** Every case loaded a client
   configuration and checked its certificates (about 400 ms). The fixture now
   loads it once and disposes it in a static `[OneTimeTearDown]`. Every case
   still opens and closes its own session.
3. **No conversion to lifecycle fakes.** 140 of the 149 test methods read,
   browse, call or subscribe through the session to prove the native outcome,
   so a fake would replace the thing they assert. The other 9 assert
   `NodeManagerLifecycle.Registrations`, the server namespace table or
   `ApplyAsync` results of the real lifecycle. Converting them would save
   about 1 % of the fixture and weaken what they check.

Per-case setup after step 1, measured with a temporary stopwatch (4 workers,
mean over 299 cases): server start 919 ms, client configuration 400 ms,
connect 48 ms, teardown 186 ms, registry store 8 ms. The server start is now
the largest cost; it lives in `ServerFixture`, outside this test project.

Local, net10.0 Debug, `FullyQualifiedName~WotAtomicPublicationNativeTests`
(303 cases, all passed in every run):

| Run | Wall | Sum of test durations |
|---|---:|---:|
| Quiet machine, before | 3 min 43 s | 893 s |
| Quiet machine, `StandardServer` only | 2 min 9 s | 515 s |
| Quiet machine, `StandardServer` + shared client | 1 min 52 s | 450 s |
| Paired, 2 other test hosts running, before | 4 min 6 s | 984 s |
| Paired, 2 other test hosts running, after | 1 min 58 s | 472 s |

The paired runs used copies of both builds run back to back, so they saw the
same load: the fixture takes about half the time it did.

## Integration category (follow-up change)

`[Category("Integration")]` now means exactly "this test starts a real
server" (and with it, any client session the test opens).

* **Measured, not grepped.** Every server start goes through
  `ServerBase.StartAsync`, which creates the request queue's
  `opcua.server.request_queue.rejections` instrument. A `MeterListener` is
  told about a new instrument synchronously on the starting thread, so the
  NUnit execution context there names the test that started the server, also
  under parallel execution. One recorded run of the whole project found
  1,106 of 3,334 cases starting a server, in 45 classes; 263 of them had no
  `Integration` category and 14 categorized cases started none.
* **Placement.** Classes whose every case starts a server carry the category
  at class level (16 added). In mixed classes it sits on the methods
  (22 methods in 9 classes). No parameterized method mixes server and
  server-free cases. The 14 server-free cases of four class-level
  `Integration` fixtures moved into sibling `...ServerFreeTests` classes in
  the same files; a nested class would not do, because the NUnit adapter
  files it under the outer fixture and so it inherits the category.
* **Guard.** `Support/IntegrationCategoryGuard.cs`, an assembly-level
  `ITestAction` with the same listener, fails a test that starts a server
  without the category and a passing test that carries it but starts none.
  It reads categories through `IPropertyBag.TryGet`: the indexer adds a
  missing key to the shared fixture's property bag, which corrupted it under
  parallel tests in the first recorded run.
* **Local fast loop.** Documented in `tests/Opc.Ua.WotCon.Tests/README.md`
  and `Properties/AssemblyInfo.cs`. CI filters do not use the category.

Local, net10.0 Release, same shared machine as above:

| Run | Result | Wall |
|---|---|---:|
| `--filter TestCategory!=Integration` | 2,228 passed | 1 min 10 s |
| Whole project | 3,334 passed | 15 min 45 s |

The server-free subset spends 197 s of test time in 63 s of test-run wall
time; its critical path is one sample regeneration test
(`ManifestDocumentsMatchCompleteAsyncRegeneration`, 46 s). The whole-project
run was slower than the 10 to 12 minutes above because other sessions were
building and testing on the machine at the same time; the guard itself adds
a dictionary lookup per test.

## Sample input cache (`Samples/`)

### Where the time went

A probe that timed the shared helpers twice in one process (net10.0
Release, serial) showed that reading and parsing the checked-in inputs is
not the cost:

| Helper | Time |
|---|---:|
| `ReadNodeSet` (sample pump / DI / Machinery / Pumps NodeSet) | 0-100 ms |
| `ReadManifestDocuments` (manifest and all 200+ documents) | 17-22 ms |
| `CreateCompanionNodeResolver` (converts DI, Machinery, Pumps) | 5.1-5.4 s |
| `GenerateVerifiedDocumentSetAsync` for Pumps | 10.8-12.9 s |
| `GenerateAggregationDocumentsAsync` (complete regeneration) | 17.4-17.5 s |

Three cases carry two thirds of the namespace's serial time:
`ManifestDocumentsMatchCompleteAsyncRegeneration` (14-19 s),
`PumpsModelRetainsEverySourceFactAsync` (11-20 s) and
`NativePumpsPartitionsRestoreTheSourceNodeSetWithoutChangeAsync` (7-9 s). The
first two are the Pumps model conversion itself; the third and the
regeneration each built the 5-second companion resolver.

### Change

`Samples/SampleInputCache.cs` holds, per process and per path, the companion
node resolver, the manifest documents and the unbound pump declaration
documents behind `Lazy<T>` (execution and publication). What it hands out is
immutable to callers: `ArrayOf<T>` and `ByteString` expose read-only views,
and `WotDocumentNodeResolver` only reads its indexes after construction.
`UANodeSet` is mutable and cheap to read, so it is not cached. A failed load
is evicted and retried. Every writer (`WriteBytesAsync`,
`WriteDocumentsAsync` and the three `File.WriteAllBytes` writers) clears the
cache, and the writers are `[NonParallelizable]`, so no reader runs while a
file is replaced. `SampleInputCacheTests` (two new cases) prove the
invalidation and the retry. All six explicit writers were run after the
change and rewrote the checked-in samples byte for byte.

`ManifestDocumentsMatchCompleteAsyncRegeneration` still regenerates every
document from its source NodeSet; only the resolver, which is read-only
context derived from the companion NodeSets no writer touches, is shared.

### Measurements

Local, net10.0 Release, `Samples` namespace (124 cases, the 9 explicit
writers and probes excluded), baseline and change built side by side and run
alternately on the shared development machine:

| Run | Baseline wall / summed | Cache wall / summed |
|---|---:|---:|
| Parallel (4 workers), pair 1 | 55.8 s / 182.1 s | 50.0 s / 163.6 s |
| Parallel, pair 2 | 48.7 s / 164.8 s | 43.3 s / 152.9 s |
| Parallel, pair 3 | 40.5 s / 144.8 s | 39.2 s / 135.1 s |
| Serial (1 worker), pair 1 | 67.6 s / 66.7 s | 59.4 s / 58.6 s |
| Serial, pair 2 | 57.4 s / 56.6 s | 70.6 s / 69.7 s |

The parallel runs are 3-11 % shorter in every pair. The serial pairs are
within the machine's noise (in serial pair 2 the untouched
`PumpsModelRetainsEverySourceFactAsync` alone went from 11.3 s to 22.8 s).
The stable per-case effect is
`NativePumpsPartitionsRestoreTheSourceNodeSetWithoutChangeAsync`: 7.0-9.4 s
before, 4.1-4.7 s after. The remaining cost is the converter work these
cases exist to prove, not repeated parsing. Running the Pumps conversion
faster would need converter changes, not test changes.

## Follow-ups (not in this change)

* **Lighter server for the other fixtures.** A seeded `StandardServer`
  starts in about half the time of a `ReferenceServer`. The reference server
  also brings user management, role mapping and its own namespace, which some
  WoT tests depend on, so the switch has to be made per fixture with its
  assertions reviewed. `WotAtomicPublicationNativeTests` is done (see above).
  A fixture that restarts into `PreparedWotTestRuntime` must restart on the
  same server type.
* **Faster `ServerFixture` start.** About 0.9 s of every live WoT case is the
  server start itself; it was not profiled further.
* **net48 parallelism.** net48 tests take almost three times as long each
  under four-way parallelism, so its CI entries did not get shorter. Fewer
  workers or more entries for net48 trade wall time against runner minutes.
