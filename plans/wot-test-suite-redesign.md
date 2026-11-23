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
* The `Samples/` tests (133 cases, about 1.6 minutes) parse the checked-in
  sample sets per test; the 20-second
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

CI after the change: see the pull request's CI run; the table there replaces
the estimate below.

Expected CI shape: 6 instead of 16 WotCon jobs per CI run
(Windows net11.0 1, Linux net11.0 1, Windows net48 2, macOS net11.0 2),
which also drops ten build-and-setup overheads of 8-14 minutes each.

## Follow-ups (not in this change)

* **Lighter server for tests that do not need the reference address space.**
  A seeded `StandardServer` starts in about half the time of a
  `ReferenceServer`. The reference server also brings user management, role
  mapping and its own namespace, which some WoT tests depend on, so the switch
  has to be made per fixture with its assertions reviewed
  (`WotAtomicPublicationNativeTests`, 303 cases, is the first candidate).
* **Server-free unit coverage for the atomic publication decisions.** Most
  `WotAtomicPublicationNativeTests` cases assert registry and coordinator
  state and read one `NodeClass` through a session. The decision logic could
  be covered against `INodeManagerLifecycle` fakes, leaving a small set of
  real-server outcome tests.
* **Consistent categories.** Mark every server-starting class
  `[Category("Integration")]` so a server-free subset can be run locally
  (`TestCategory!=Integration`) in well under a minute.
* **Sample data.** Cache the parsed sample sets per process in the
  `Samples/` tests.
