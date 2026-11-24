<!-- SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL -->
<!-- SPDX-License-Identifier: MIT -->

# Server startup and shutdown profile (test fixtures and hosts)

Status: measured 2026-11-23 on master `e6c29e434`, Windows 11, net10.0 Release,
shared developer machine. Test-framework changes are in this branch; product
recommendations are below and not implemented.

## Probe

`tests/Opc.Ua.Client.Tests/Perf/ServerStartupProbeTests.cs` (`[Explicit]`,
category `Perf`) runs one cold and N warm cycles of

1. `ServerFixture<T>.LoadConfigurationAsync` (ServerLoad)
2. `ServerFixture<T>.StartAsync` (ServerStart)
3. `ClientFixture.LoadClientConfigurationAsync` (ClientLoad)
4. `ClientFixture.ConnectAsync(Basic256Sha256)` (Connect)
5. session close and client dispose (Close)
6. `ServerFixture<T>.StopAsync` (ServerStop)

for `StandardServer` and `ReferenceServer` and prints the medians.
Environment variables: `OPCUA_STARTUP_PROBE_CYCLES` (10),
`OPCUA_STARTUP_PROBE_WORKERS` (parallel cycles, 1),
`OPCUA_STARTUP_PROBE_FRESH_PKI=1` (new PKI folder per cycle, which is what
most suites do: `ClientTestFramework` and the default `ServerFixture`
`pkiRoot` are random temp folders), `OPCUA_STARTUP_PROBE_PKI`,
`OPCUA_STARTUP_PROBE_OUT` (per-cycle CSV).

```powershell
$env:OPCUA_STARTUP_PROBE_FRESH_PKI = '1'
dotnet vstest tests/Opc.Ua.Client.Tests/bin/Release/net10.0/Opc.Ua.Client.Tests.dll --TestCaseFilter:FullyQualifiedName~ServerStartupProbe
```

## Where the time goes (before)

Per-phase medians of one StandardServer cycle, warm certificate store
(certificates already on disk), from temporary `Stopwatch` scopes around the
start and stop stages (the instrumentation patch is not committed):

| Stage | ms | Notes |
|---|---:|---|
| Server `CreateAsync` (configuration `ValidateAsync`) | 27-55 | loads the 5 application keys into the `CertificateManager` |
| Server `CheckApplicationInstanceCertificatesAsync` | 36-75 | loads the 5 keys again and validates them |
| `ServerBase.OnServerStartingAsync` | 27-56 | `LoadApplicationCertificatesAsync`: the 5 keys a third time |
| `MasterNodeManager.StartupAsync` | 85-165 | address space, see below |
| rest of `StartApplicationAsync` | ~10 | managers, discovery, namespace metadata |
| Client `CreateAsync` + `CheckApplicationInstanceCertificatesAsync` | 67-127 | 10 key loads |
| Connect | 27 | 2 more key loads in `Session.LoadInstanceCertificateEntryAsync` |
| `Server.StopAsync` | 13-24 | almost all in system NodeManager reclaim (`GenerationReclaimer`) |
| `await Task.Delay(100)` at the end of `ServerFixture.StopAsync` | 100 | fixed |

Ranges are two runs of 20 cycles at different machine load; the machine is
shared, so only the paired A/B below compares builds.

The spread between StandardServer and ReferenceServer is the ReferenceServer
node managers: initial publication (`BeginInitialPublicationAsync` ~48 ms,
`InitialPublication.CompleteAsync` ~61 ms).

### Certificates

A private-key load (`DirectoryCertificateStore.LoadPrivateKeyAsync`) costs
about 5 ms wall / 3 ms CPU: it enumerates and parses every `*.der` in the own
store to match the identifier (the server and client of a fixture share the
store, so 10 files), decrypts the PFX (PKCS#12 PBKDF, `Pkcs12Kdf.Derive` is the
top CPU frame) and signs and verifies with the key (`X509PfxUtils.VerifyKeyPair`).
One warm fixture cycle performs 27 such loads (counted with a stack trace per
call): server 5 x {configuration validation, certificate check, server start},
client 5 x {configuration validation, certificate check} and 2 on connect.

With a fresh PKI folder the certificate check generates the RSA and the four
ECC (nistP256/P384, brainpoolP256r1/P384r1) certificates for the server and
again for the client: server check ~350 ms, client check ~370 ms, so a
fresh-PKI StandardServer cycle took ~1.1 s instead of ~0.4 s.

### Address space (StandardServer, ~95 ms on a quiet machine)

`ConfigurationNodeManager.CreateAddressSpaceAsync` is almost all of it, plain
CPU and allocation work, no waits:

| Step | ms |
|---|---:|
| `AddOpcUa` (generated construction of the standard namespace nodes) | 24 |
| `AddPredefinedNodeAsync` loop (indexing) | 17 |
| `CoreNodeManager.ImportNodesAsync` | 29 |
| `AddReverseReferencesAsync` | 8 |
| `CreateCertificateAlarmsAsync` | 9 |
| CoreNodeManager create | 5 |

### Fixed delays found

- `ServerFixture.StopAsync`: `await Task.Delay(100)` after every stop
  (from #1434, when stop did not await listener teardown).
- `ClientTestFramework.OneTimeTearDownAsync` and `TestFixture` teardown:
  another `await Task.Delay(100)` after `ServerFixture.StopAsync`.
- `ClientFixture.StartReverseConnectHostAsync`: a random 100-1000 ms delay
  after every attempt, including the successful one.
- Connect retries wait 1 s, but only after a failed attempt.

### Not causes

- Server GC (checked by the caller session, no change).
- Lock contention: no contention frames in the startup path; parallel starts
  are CPU bound (see the 4-worker row below).
- Endpoint/transport setup (`InitializeServiceHostsAsync` ~2 ms), security
  policy setup, session/subscription manager start (<1 ms each).

## Changes in this branch (test frameworks only)

1. **`TestCertificateCache`** (`tests/Opc.Ua.Server.TestFramework`): the server
   and client fixtures still call `CheckApplicationInstanceCertificatesAsync`
   on every start, but an empty own store is first seeded with the certificate
   and key files that an identical identity created earlier in the same process
   (key: application URI and name, lifetime, server domain names, and per
   certificate its type, subject and minimum key size; only single directory
   stores found by subject, no password provider). The first certificate is
   added through `DirectoryCertificateStore.AddAsync`, which creates the
   private key folder with the product's owner-only permissions (Windows ACL,
   Unix 0700); the other files are copied into it (keys created 0600 on Unix).
   Adding all of them through the store would re-encrypt each key. The first fixture per
   identity creates the certificates under a per-identity gate so parallel
   fixtures do not generate them twice. After a seeded check the fixture calls
   `CertificateManager.UpdateAsync`, as the check itself does after creating a
   certificate: configuration validation already loaded the then empty store
   into the manager (without this the client's WSS reverse-connect listener
   found no TLS certificate). Seeded fixtures of one identity share one key
   pair per certificate type within a test process; trust lists stay per PKI
   folder.

   The cache is **opt-in** (`ServerFixture.ReuseCachedCertificates`,
   `ClientFixture.ReuseCachedCertificates`, default off). `ClientTestFramework`
   (49 fixture classes; `ReuseCachedCertificates` property, default on) and the
   conformance `TestFixture` (156 classes) turn it on: each owns one server and
   one client on a private PKI folder. A first version had it on for every
   fixture; CI then failed in fixtures that deliberately build several
   applications of one identity and assert different keys (GDS onboarding
   `PullRegistrarIntegrationTests`, the GDS certificate-rotation verifier,
   WotCon authority isolation, a Sessions mutual-TLS test). The roughly 230
   files that create fixtures directly keep generating certificates unless they
   opt in. `OPCUA_TEST_FRESH_CERTIFICATES=1` turns the cache off for a process.
2. Removed the fixed 100 ms delays after `ServerFixture.StopAsync` (fixture and
   both teardown helpers). `StopAsync` awaits listener close/dispose, server
   dispose and application dispose under the watchdog, so there is nothing left
   to wait for; ports are picked fresh per start.
3. `ClientFixture.StartReverseConnectHostAsync` only waits before a retry.
4. The probe above.
   `TestCertificateCacheTests` (Opc.Ua.Server.Tests) pin the cache contract:
   same-identity reuse and manager visibility, changed identity, opt-out,
   existing certificates kept, parallel first use, owner-only keys on Unix.

## Results (paired A/B)

Baseline (A, master `e6c29e434`) and this branch (B, `48d8be3f8`, with the
certificate-manager refresh and store-based seeding) were copied to separate
folders and run alternately, A/B then B/A, for 5 rounds: 10 cycles per test and
run, so 50 cycles per cell. Medians in ms, both `ServerFixture` and
`ClientFixture` phases. All 20 runs passed.

The probe opts in like the harnesses, so the fresh-PKI rows show what a
`ClientTestFramework` or `TestFixture` fixture saves.

| Scenario | Server | A total | B total | Change | Main phase changes (A → B) |
|---|---|---:|---:|---:|---|
| Fresh PKI per cycle (harness fixtures) | StandardServer | 901 | 376 | −58 % | start 420 → 221, client load 328 → 104, stop 117 → 11 |
| Fresh PKI per cycle | ReferenceServer | 1407 | 911 | −35 % | start 889 → 731, client load 336 → 111, stop 131 → 29 |
| Warm PKI | StandardServer | 443 | 340 | −23 % | stop 118 → 13 |
| Warm PKI | ReferenceServer | 965 | 855 | −11 % | stop 135 → 32 |

An earlier A/B of the first version (before the certificate-manager refresh,
which reloads the five keys once more per seeded application, ~45 ms each) had
fresh-PKI StandardServer at 973 → 319 ms; with 4 parallel workers it measured
StandardServer 880 → 797 ms and ReferenceServer 1824 → 1690 ms (stop phase
only, as the cache did not apply to warm stores).

- Under 4 parallel workers a warm StandardServer start takes ~530 ms instead
  of ~200 ms serially and a ReferenceServer start ~1.36 s instead of ~0.75 s.
  The probe threads are CPU bound (address space, PFX decryption); there is no
  lock contention in the startup stacks.
- The first ReferenceServer start in a process (JIT, type initialisation)
  takes ~3.1 s (start phase) and ~4 s for the whole cycle, with or without
  the change. The probe's StandardServer "cold" row runs second in the
  process and is therefore not cold.
- With a fresh PKI, the first fixture per identity still generates its
  certificates (the cold rows: client load ~350-400 ms on both sides); every
  later fixture in the process gets copies.

## Recommendations (product code, not done here)

1. **Load each application key once per start.** Configuration validation,
   `ApplicationInstance.CheckApplicationInstanceCertificatesAsync` and
   `ServerBase.OnServerStartingAsync` each reload every application key from
   the store (15 PFX decryptions for a server with the default five
   certificates, ~75 ms warm). The reloads exist so a certificate created or
   rotated by the check is picked up. A safe fix needs change detection, e.g.
   the `CertificateManager` keeps the own-store file snapshot (name, size,
   write time) of what it loaded and the check reuses the loaded entries, and
   server start skips the reload, when the snapshot is unchanged. Not done here
   because it touches the certificate rotation path (push management,
   `ReloadApplicationCertificatesAsync`) and needs its own review.
2. **`DirectoryCertificateStore.LoadPrivateKeyAsync` parses every certificate
   in the store** per lookup before it finds the match. Use the store's cached
   entries (it already tracks them for `GetPrivateKeyFilePath`) to go straight
   to the matching `.der`/`.pfx` pair.
3. **Address space build (~95 ms per StandardServer start).** The standard
   namespace is rebuilt from generated code per server; `NodeState` trees are
   mutable and per server, so caching them is not safe. Candidates if hosts
   ever need faster starts: `CoreNodeManager.ImportNodesAsync` browses the
   diagnostics node for every reference of every node (29 ms), and the
   predefined-node indexing allocates an ambient `AsyncLocal` scope per node.
4. Do not lower the PKCS#12 iteration count to speed up key loads; it protects
   keys at rest.
5. Ad-hoc fixture users with one server and one client per PKI folder (WotCon,
   GDS, the companion suites) can opt in per fixture; WotCon's own template
   seeding (PR #5148) could switch to `TestCertificateCache` once both are
   merged, except for its authority-isolation endpoints.

## Verification (local, net10.0 Release)

- `Opc.Ua.Client.Tests`: 3946 passed, 0 failed.
- `Opc.Ua.Sessions.Tests`: 874 passed, 0 failed, 379 skipped. The first run
  found the missing manager refresh after seeding (WSS reverse connect).
- `Opc.Ua.Server.Tests`: 12043 passed, 8 failed; the 8 are
  `PhysicalFileSystemProviderConfinementTests` symbolic-link cases that fail
  identically on master on this machine (no symlink privilege).
- `Opc.Ua.WotCon.Tests` spot run (event modes, live projection, atomic
  publication, configured options, runtime and storage slices): 511 tests green.
- After the opt-in change: the CI-failing GDS onboarding classes (74),
  `SessionlessChannelSecurityTests` (44) and `WotEventModesLiveTests` (74) pass.
- net48: `Opc.Ua.Client.Tests` and `Opc.Ua.Sessions.Tests` build.
