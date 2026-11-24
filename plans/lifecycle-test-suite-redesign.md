<!-- SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL -->
<!-- SPDX-License-Identifier: MIT -->

# NodeManager lifecycle test suite redesign (`NodeManagerLifecycleTests`)

Item N3 of the NodeManager lifetime redesign (F10 in issue #4931). Builds on the
WoT test suite redesign (`plans/wot-test-suite-redesign.md`, PR #5148).

## Problem

PR #5011 (merged as `d8e5e29cd`) grew `NodeManagerLifecycleTests` in
`tests/Opc.Ua.Server.Tests/NodeManager/` to 721 cases (CI count, net48 and
net11.0) in 39 partial files. The fixture was `[NonParallelizable]` and started
a real `ReferenceServer` with an activated session for every case. Before #5011
(master run 44992939283) the non-prepared lifecycle tests ran as one shard,
39 minutes on Windows net48 and 46 minutes on macOS net11.0. After #5011 they no
longer fit the 45-minute per-project ceiling and were split into three entries on
net48 and macOS (`server-node-lifecycle-prepared`, `-a-n` and the rest, keyed on
the first letter of the test name). On Windows and Linux net11.0 the whole
`Opc.Ua.Server.Tests` project runs in one entry, which took 49 and 43 minutes.

## Measurements before the change

### CI, master run 45011528803 (test execution from the TRX, job length from GitHub)

| Profile | Shard | Cases | Test wall | Mean per case | Job |
|---|---|---:|---:|---:|---:|
| Windows net48 | server-node-lifecycle-prepared | 146 | 10.4 min | 4.3 s | 22 min |
| Windows net48 | server-node-lifecycle-a-n | 315 | 19.0 min | 3.6 s | 28 min |
| Windows net48 | server-node-lifecycle | 260 | 19.5 min | 4.5 s | 31 min |
| macOS net11.0 | server-node-lifecycle-prepared | 146 | 7.1 min | 2.9 s | 14 min |
| macOS net11.0 | server-node-lifecycle-a-n | 315 | 21.2 min | 4.0 s | 35 min |
| macOS net11.0 | server-node-lifecycle | 260 | 17.0 min | 3.9 s | 31 min |

Totals: Windows net48 48.9 min test wall / **81 job minutes**; macOS net11.0
45.3 min / **80 job minutes**. Every job pays about 8-14 minutes of checkout,
build and upload on top of its tests.

### Where the time went

* **Wall time equals the sum of the test durations.** The fixture was
  `[NonParallelizable]` and ran one case at a time on 3- and 4-core runners.
* **No case is cheap.** Not a single case finished under 2 s in CI (median 3.1-4 s);
  the test bodies are short and gate-driven, so the per-case server start, session
  activation and stop is the cost. Locally (net10.0, Release) the same cycle is
  0.8 s median.
* **Password hashing in the `ReferenceServer` constructor, worst on net48.**
  The largest silent gap in each case's log sits between the end of
  `CheckApplicationInstanceCertificatesAsync` and the server's first certificate load:
  2.4-3.2 s on Windows net48 (over two thirds of each case), 0.45 s on macOS and
  0.31 s locally. In that window `ServerFixture` constructs the server, and
  `ReferenceServer`'s constructor created its seven demo users in a
  `LinqUserDatabase`, each a PBKDF2-SHA512 derivation with 100,000 iterations.
  Measured on the development machine: **1,876 ms for the seven users on .NET
  Framework 4.8** (managed `Rfc2898DeriveBytes`), 339 ms on .NET 11. Every test
  server in every suite that uses `ReferenceServer` pays this.
* **Double servers.** 56 cases host their own server (a cleanup-recovery harness,
  a custom server type, a logger or factory that must be installed before startup)
  on top of the fixture's server, and one more
  (`AddAsyncBeforeServerStartRejectsWithoutInvokingFactoryAsync`) needs no running
  server at all, but all of them still paid for the fixture server. They were the
  slowest cases (6-10 s in CI).
* **Fresh certificates for nested servers.** The fixture shared one store across
  its cases, but the nested servers used new subfolders
  (`cleanup-<guid>`, `cursor-<guid>`, ...), so each of them created an RSA and four
  ECC certificates again (27 cases locally logged a certificate creation).
* **A fixed 100 ms delay** at the end of every `ServerFixture.StopAsync`.
* Fixed delays inside test bodies are not a cost driver: the 31 `Task.Delay` calls
  either poll a condition with a bound, bound a wait with `Task.WhenAny`, or are
  negative observation windows ("still pending after 100 ms / 1 s") that cannot be
  replaced by a condition wait without weakening the assertion.

### Local baseline (development machine, 24 cores, net10.0 Release)

`dotnet test --filter "FullyQualifiedName~Opc.Ua.Server.Tests.NodeManager.NodeManagerLifecycleTests."`
on `098cd120c` (the #5148 branch): **721 passed, 0 failed, 12 min 26 s**
(sum of case durations 745 s, median 0.79 s).

## Design

The redesign keeps every case, every name and every category; the real-server
owner and drain checks stay real-server checks. It removes the structural costs
instead of rewriting tests.

1. **Demo users hashed once per process (`samples/Quickstarts.Servers/ReferenceServer/ReferenceServer.cs`).**
   `ReferenceServer` creates its seven demo users once per process (a static
   `Lazy`) and seeds each instance's own `LinqUserDatabase` through the public
   `Users` setter, which copies the stored records. Every server still owns its
   database, so user changes stay private; only the password verifiers (and their
   salts) are shared between instances of one process. Users, passwords and roles
   are unchanged. This is sample code, not `src/`; it is the one change outside
   the test projects and it speeds up every suite that starts a `ReferenceServer`.
2. **Certificates created once per process (`TestCertificateCache`).** Every
   server and client fixture a lifecycle case creates sets
   `ReuseCachedCertificates`, the opt-in of PR #5166's shared
   `TestCertificateCache`: the first fixture of an identity (one per server type,
   `LifecycleTestServer`, `StandardServer`, `ShutdownStageTestServer`,
   `RetainedWorkerTestServer`, and the client) creates its certificates, and every
   later fixture seeds its empty store from them. `[SetUp]` picks a new store per
   case (`NodeManager/NodeManagerLifecycleTests.TestPki.cs`) and `[TearDown]`
   deletes it; nested servers keep their own subfolders (`cleanup-<guid>`,
   `cursor-<guid>`, ...). So no lifecycle case after the first of each identity
   creates a certificate. The first version of this PR copied a fixture-local
   template (as `WotTestPki` from PR #5148 does); it switched to the shared cache
   once #5166 was merged, at the same wall time (net10.0, default workers: 3 min
   33 s with the template, 3 min 37 s with the cache for 758 cases).
3. **No fixture server for cases that do not use it.** A private
   `[NoFixtureServer]` attribute marks the 57 cases above; `[SetUp]` still creates
   their store, logger and recovery stubs but skips the server and session. They
   keep their names and categories.
4. **Parallel fixture.** `NodeManagerLifecycleTests` is
   `[Parallelizable(ParallelScope.All)]` with
   `[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]`. Every case already created
   its server, session, mocks and gates in `[SetUp]` or in its body; the only
   fixture-wide state was the shared certificate store, which item 2 replaces. No
   case changes process-wide state (the `GC.Collect` ownership checks only observe
   their own weak references). Two cases keep a method-level `[NonParallelizable]`
   because their expected continuation-point outcome depends on retirement timing
   (see "Open items"); nothing is serialized for process-wide state. The worker count
   is NUnit's default (one per core: 4 on the Windows and Linux runners, 3 on macOS).
5. **No fixed stop delay.** `ServerFixture.StopAsync` no longer waits 100 ms after
   stopping. PR #5166 made the same change and is merged, so this PR no longer
   touches that file.
6. **CI layout.** net48 and macOS run all lifecycle cases in one entry,
   `server-node-lifecycle` (`FullyQualifiedName~...NodeManagerLifecycleTests.`),
   instead of three (`-prepared`, `-a-n`, rest); that removes two jobs' build and
   upload overhead per profile. Windows and Linux net11.0 keep the single `all`
   entry for the project.

### Racy assertions exposed by parallel load

Running 24 servers at once made each case about ten times slower and exposed two
assertions that read "the first value published after an action":

* `ShadowReloadedDataMonitoredItemMigratesWithStableIdAsync`: re-enabling the
  item queues an initial sample of the current value (4242), which a slow sampling
  pass can publish after the following push of 5353. The test now publishes until
  5353 arrives (`PublishUntilDataChangeAsync`).
* `PreparedCutoffPreservesAuthorizedSourceSnapshotsAndCoreIdentityAsync` (both
  cases): the retired source's last queued emission (`captured-772`, or the data
  item's final `BadNodeIdUnknown`) can reach the subscription after the survivor's
  post-commit fence event. The test now publishes until both the fence and that
  emission arrived; the assertions on the exact captured values are unchanged.
* `SlotChangeSetLegacyResultRetainsOriginalRegistrationHandleAsync`,
  `SlotChangeSetLifecycleResultMetadataDoesNotRetainRegistrationOrPublishedManagerAsync`
  and `VerifiedPrivateCleanupReleasesWriterUnitAndPreservesFailureAsync`: these
  checked weak references after one collection (or three back to back). On a
  loaded net48 runner a retirement continuation can still hold the manager or
  factory for a moment (CI run 45360448331). The tests now collect and poll for
  up to about five seconds (`CollectUntilReleasedAsync`); the assertions are
  unchanged.

### Fixed delays

The 31 `Task.Delay` calls left in the test bodies are bounded poll intervals,
`Task.WhenAny` deadlines, or negative observation windows ("still pending after
100 ms / 1 s"). They only cost time on failure or in a handful of cases, and a
negative window cannot be replaced by a condition wait without weakening the
assertion, so they stay.

### Considered and not done

* **One server per fixture, fresh `NodeManagerSlot` per case.** Most cases change
  server-wide state on purpose (shutdown, readiness, namespace table, routing
  table, host boundary hooks, request callbacks, cleanup recovery adapters bound
  before startup) and assert baselines such as the startup registrations. Sharing a
  server would need per-case resets of all of that and would weaken the
  "unaffected by other tests" guarantee the fixture documents. With the hashing
  and certificate costs gone, a server cycle costs about 1 s and runs in parallel,
  so the remaining gain did not justify the risk.
* **Moving server-free cases into component fixtures.** Only one case
  (`AddAsyncBeforeServerStartRejectsWithoutInvokingFactoryAsync`) turned out not
  to need a running server; the three ChangeSet ownership cases that looked
  server-free use the fixture server through their helpers. Moving the one case
  would rename it and move it to another CI shard for no measurable gain, so it
  stays and runs without the fixture server instead. The direct component
  fixtures (`GenerationReclaimerTests`, `GenerationReconcilerTests`, ...) are
  unchanged.

### Intentional removals

None. The case inventory (721 on net10.0, the CI count on net48 and net11.0) and
all categories are unchanged.

## Measurements after the change

Local, same machine (24 cores, shared with other sessions' builds, so numbers vary
by about ±20 %), same filter, Release:

| Run | Result | Wall | Mean per case |
|---|---|---:|---:|
| Baseline, net10.0, serial (`098cd120c`) | 721 passed | 12 min 26 s | 1.03 s |
| Parallel + seeded stores, net10.0, default workers (runs 1-3, before the two test fixes) | 718-720 passed, 1-3 failed (racy assertions above) | 4 min 5 s - 5 min 30 s | 10.6 s |
| + racy assertions fixed, net10.0, default workers (3 runs) | 721, 720 (1 failed, `PreparedCutoff...(True)`, fixed afterwards), 721 passed | 4 min 43 s - 5 min 22 s | |
| + demo users hashed once, net10.0, default workers | 721 passed | 3 min 33 s | 7.0 s |
| + demo users hashed once, net10.0, 4 workers (as on CI) | 721 passed | 4 min 28 s | 1.49 s |
| + demo users hashed once, net48, 4 workers | 721 passed | 7 min 22 s | 2.45 s |
| Rebased on master (more cases), `TestCertificateCache` instead of the template, net10.0, default workers | 758 passed | 3 min 37 s | |
| Same, net48, 4 workers | 758 passed | 3 min 59 s | 1.26 s |

With 24 workers every case runs about ten times slower (the address-space build in
`MasterNodeManager.Startup` goes from 0.16 s to 2.7-4 s), so more workers than
the CI runners have do not help much; four workers already reach most of the gain.

Other suites run with the `ReferenceServer` change (net10.0): the
`Opc.Ua.Server.Tests` cases matching `ReferenceServer|UserManagement|UserDatabase|Authentication|Identity|ServerFixture`
(701 passed) and `Opc.Ua.Core.Security.Tests` (563 passed, 61 skipped, 0 failed),
which log in with the demo users. net48 builds without warnings.

### Expected CI cost

Before: 81 job minutes on Windows net48 and 80 on macOS net11.0 (three jobs each),
against 39 and 46 minutes for the lifecycle tests before #5011. After: one job per
profile. Per case the net48 runner saves about 2.5-3 s of password hashing and the
nested-server cases save a full server start and a certificate set; four parallel
workers then divide the remaining time. The CI numbers of this PR's first run are
recorded below.

### CI, this PR

First run, 45343833827 (head `612709c00`; macOS is not part of PR CI):

| Profile | Entry | Lifecycle cases | Lifecycle test wall | Mean per case | Job |
|---|---|---:|---:|---:|---:|
| Windows net48 | server-node-lifecycle | 721 passed | 7.0 min | 2.3 s | 19 min |
| Windows net11.0 | all (whole project) | 743 passed, 1 failed | 13.2 min (in parallel with the rest) | 2.0 s | 33 min |
| Linux net11.0 | all (whole project) | 743 passed, 1 failed | 13.3 min (in parallel with the rest) | 2.3 s | 30 min |

(744 lifecycle results on net11.0 include the two parallel component fixtures
in the same files.) On net48 the lifecycle tests went from three jobs and 81 job
minutes to **one job of 19 minutes**, under the 39 minutes they took before #5011;
the certificate-check-to-start phase of a case fell from 3.3 s to 0.5 s. The whole
`Opc.Ua.Server.Tests` job went from 49 to 33 minutes on Windows net11.0 and from
43 to 30 minutes on Linux.

The two net11.0 failures were the continuation-point cases described in "Open
items"; they now run alone.

## Open items

* `InFlightBrowsePinsItsViewButNotItsSuccessorContinuationAsync` and
  `FirstHistoryPageInFlightAtRetirementStillRejectsItsSavedContinuationAsync`
  release a request that was blocked inside a generation while that generation
  retires, then assert whether the request could still bind a continuation point
  (Browse: must not; first HistoryRead page: must, then the saved point must be
  rejected). The binding (`TryBindHistoryResult` and its Browse counterpart)
  depends on how far the asynchronous retirement got, and under the load of the
  whole project running in parallel each case once saw the other outcome (CI run
  45343833827, Linux and Windows net11.0). They now run alone, as they did before.
  Whether the product should decide this deterministically (for example at
  publication) is a question for the lifecycle owners (#4931).

* `PreparedCutoffPreservesAuthorizedSourceSnapshotsAndCoreIdentityAsync`: under
  load the retired source's last queued emission can be published after a
  survivor event reported once the commit returned. The test now waits for it; whether a
  committed removal should guarantee that order is a question for the lifecycle
  owners (#4931).
