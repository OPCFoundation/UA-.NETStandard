<!-- SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL -->
<!-- SPDX-License-Identifier: MIT -->

# Server startup: product-code optimizations

Status: implemented and measured 2026-11-24 on master `e6c29e434`, Windows 11,
net10.0 Release, shared developer machine. Follows the profile in
`plans/server-startup-profile.md` (draft PR #5166), which fixed the test
framework side and left the product recommendations below.

## Changes

### 1. Each application key is loaded once per start

Before, every private key was decrypted (PKCS#12, ~5 ms wall each) by

1. `ApplicationConfiguration.ValidateAsync` →
   `CertificateManager.LoadApplicationCertificatesAsync`,
2. `ApplicationInstance.CheckApplicationInstanceCertificatesAsync` →
   `CheckOrCreateCertificateAsync` → `CertificateIdentifierResolver`,
3. `ServerBase.OnServerStartingAsync` → `LoadApplicationCertificatesAsync`
   again (server only),

so 15 loads for a server with the default five certificates and 10 for a
client configuration.

Now the `CertificateManager` remembers each key it loaded from a directory
store, together with a snapshot of that store: full path, size and last write
time of every file below the store directory, taken before the first key of the
store is loaded and compared again after all keys are loaded (a store that
changed during the load is not remembered). If a store changed during a load
that reused one of its keys, the load runs again without reusing any key, so
old and new certificates are never mixed.

- `LoadApplicationCertificatesAsync` reuses a remembered key, and the
  certificate check asks for it through the internal
  `CertificateManager.TryAcquireLoadedApplicationKey`, when
  - the lookup is the same: store type and path, subject, certificate type,
    application URI and the same password provider instance;
  - the thumbprint matches: a lookup without a thumbprint only matches a key
    looked up without one, a lookup with a thumbprint matches a key whose
    certificate has that thumbprint. The certificate check fills in the
    identifiers' thumbprints after the first load, so server start looks the
    keys up by thumbprint. In an unchanged store every resolver pass that
    filters by thumbprint T can only return certificate T, and the earlier
    lookup with the same subject, URI and type found it, so the result is
    the same;
  - the current snapshot of the store equals the remembered one;
  - the key was read at most 30 s ago. This bounds the window for a
    replacement that keeps name, size and timestamp (the directory store's
    own cache uses the same bound).
- Issuer chains are resolved again on every load, as before.
- `ReloadApplicationCertificatesAsync` and `UpdateAsync` always read every key
  from the store and refresh the remembered keys. This covers the certificate
  check after it creates a certificate (`UpdateAsync`), push management
  (`ConfigurationNodeManager` `UpdateCertificate`/`ApplyChanges` and the
  certificate slots call `UpdateAsync`) and configuration reload.
  `UpdateApplicationCertificateAsync` drops all remembered keys in the same
  critical section that replaces the certificate, and a load that was already
  running when it did so does not remember its keys.
- Only stores served by the built-in `DirectoryStoreProvider` qualify. X509Store,
  PKCS#11, shared key/value and stores from any custom provider registered for
  the directory type are always loaded from the store. The snapshot is taken
  from the directory without opening the store.

### 2. `DirectoryCertificateStore.LoadPrivateKeyAsync` uses the cached entries

The lookup parsed `*.der`/`*.pem` files in a private loop on every call,
separately from the store's own entry cache. It now matches against the
store's cached, change-detected entries (the ones `EnumerateAsync`,
`FindByThumbprintAsync` and `GetPrivateKeyFilePath` use) and reads only the
matching key file:

- When the entries are cached and the directory is unchanged, nothing is
  parsed.
- When they are not (a new store instance, or the directory changed), the
  store is parsed only up to the first match that has a key file, as the old
  loop did. If that key cannot be imported or verified, the rest of the store
  is parsed and the remaining matches are tried in store order, skipping what
  was tried.
- A retry after a failed key import rereads the directory, as before.

The PFX → PEM key file → PEM file with key fallback, the
MachineKeySet/UserKeySet retry and `VerifyKeyPair` are unchanged. A
certificate file that fails to parse is still skipped and logged.

A first version always parsed the whole store into the cache. Because every
resolver call opens a new store instance, that made configuration validation
about 5 ms slower per server (ServerLoad 55.9 → 61.4 ms in a paired A/B
against master), so the scan now stops at the first match as before. The gain
on its own is small for the same reason: in the startup path every lookup uses
a fresh instance. It helps the resolver's fallback passes (up to three lookups
on one store) and long-lived store instances.

Two details keep the result the same as the old per-file loop:

- The entry cache is keyed by thumbprint. When the same certificate is in
  several files (`.der` and `.pem`), the entry remembers the earlier files with
  their positions in the scan (file, and certificate within a PEM file), and
  every matching file is tried in scan order, as before.
- The store's own cache checks only the directory timestamps and the file
  count, so it can serve a certificate file that was rewritten in place for up
  to 30 s. The lookup therefore records the path, size and last write time of
  every certificate file the scan read, also of files that failed to parse,
  and reads the directory again when the files differ.
- A PFX whose certificate is not the matched one is rejected.

## PFX loads per warm StandardServer fixture cycle

Counted with a temporary stack trace per `LoadPrivateKeyAsync` PFX import (not
committed), `ServerStartupProbe` StandardServer, warm PKI, cold + 1 cycle:

| Caller | Before | After |
|---|---:|---:|
| Server `ValidateAsync` | 5 | 5 |
| Server certificate check | 5 | 0 |
| `ServerBase.OnServerStartingAsync` | 5 | 0 |
| Client `ValidateAsync` | 5 | 5 |
| Client certificate check | 5 | 0 |
| Session connect (`Session.LoadInstanceCertificateEntryAsync`) | 2 | 2 |
| **Total** | **27** | **12** |

## Results (paired A/B)

Method as in `plans/server-startup-profile.md`: the probe
(`tests/Opc.Ua.Client.Tests/Perf/ServerStartupProbeTests.cs`, copied from
#5166 without its certificate-cache opt-in) built in Release for the baseline
(A, master `e6c29e434` + probe) and the candidate (B), both copied to short
paths and run alternately A/B, B/A for 5 rounds with
`OPCUA_STARTUP_PROBE_CYCLES=10` and a warm, shared PKI folder (pre-warmed once),
serially (warm) and with `OPCUA_STARTUP_PROBE_WORKERS=4`. 50 cycles per cell,
medians in ms, cold cycles excluded. All runs passed.

The machine was busy: this session's own `Opc.Ua.Server.Tests` run was in
progress during the first A/B, so absolute numbers are higher than in the
earlier profile. The alternating order spreads that load over both sides.

### First version (store scan parsed the whole store)

| Mode | Server | A total | B total | Change | Phase changes (A → B) |
|---|---|---:|---:|---:|---|
| Warm | StandardServer | 675 | 511 | −24 % | start 299 → 179, client load 139 → 81, server load 56 → 61 |
| Warm | ReferenceServer | 1691 | 1386 | −18 % | start 1211 → 1006, client load 159 → 94, server load 59 → 65 |
| 4 workers | StandardServer | 1020 | 964 | −6 % | start 618 → 533, client load 144 → 102, server load 57 → 74 |
| 4 workers | ReferenceServer | 2219 | 2138 | −4 % | start 1716 → 1630, client load 157 → 117, server load 60 → 75 |

Per-round medians of the warm total (A/B): StandardServer 632/528, 675/524,
659/510, 636/522, 1039/491; ReferenceServer 2030/1627, 1524/1380, 1557/1396,
2008/1356, 1864/1310. B is faster in every warm round. With 4 workers the
StandardServer total was faster in 4 of 5 rounds and ReferenceServer in 4 of 5.

Server start lost the certificate check's and `OnServerStartingAsync`'s key
loads (~120 ms warm), and the client load lost the check's (~58 ms). Server
load (configuration validation) got slower by ~5 ms (~15 ms with 4 workers),
which the change to the store scan above removes.

### Final version

Same method, master `e6c29e434` + probe (A) against the final branch (B),
2026-11-24 12:15–12:20, with no test run of this session in parallel (absolute
numbers are lower than in the first A/B for that reason).

| Mode | Server | A total | B total | Change | Phase changes (A → B) |
|---|---|---:|---:|---:|---|
| Warm | StandardServer | 407 | 333 | −18 % | start 157 → 105, client load 74 → 47, server load 32 → 33 |
| Warm | ReferenceServer | 998 | 805 | −19 % | start 708 → 535, client load 85 → 48, server load 33 → 32 |
| 4 workers | StandardServer | 690 | 633 | −8 % | start 397 → 351, client load 85 → 60, server load 36 → 40 |
| 4 workers | ReferenceServer | 1325 | 1252 | −5 % | start 1006 → 914, client load 100 → 75, server load 37 → 35 |

Per-round medians of the total (A/B):

- Warm StandardServer 383/302, 404/318, 442/348, 646/465, 386/309.
- Warm ReferenceServer 814/731, 842/762, 1030/870, 1395/958, 1301/750.
- 4 workers StandardServer 697/582, 713/631, 733/653, 671/599, 682/653.
- 4 workers ReferenceServer 1358/1277, 1350/1280, 1854/1266, 1302/1226,
  1244/1250.

B is faster in every round except one 4-worker ReferenceServer round
(1244/1250). Server load is now within noise of master (warm +1.1 ms
StandardServer, −1.1 ms ReferenceServer; ±3 ms with 4 workers, in both
directions), so the regression of the first version is gone. Connect, close
and stop are unchanged within noise.

## Not implemented

- **Address space build (~95 ms per StandardServer start).**
  `CoreNodeManager.ImportNodesAsync` is not slowed by
  `UpdateDiagnostics`/`AddReferenceToDiagnostics` at startup: its only caller,
  `DiagnosticsNodeManager.CreateAddressSpaceAsync`, passes `isInternal: true`,
  which skips the diagnostics update. Its ~29 ms is the indexing of the
  diagnostics node manager's predefined nodes into the core node manager
  (`AddPredefinedNodeAsync` per node and child). The per-node `AsyncLocal`
  ambient scope in `AsyncCustomNodeManager.AddPredefinedNodeAsync`
  (`m_registrationScope`) came with the NodeManager lifecycle work in #5011 and
  was left alone. `NodeState` trees are mutable and owned by one server, so
  they are not cached across servers.
- **Configuration validation still loads every key.** See "Loading only the
  keys the policies use" below: evaluated and not implemented.
- The PKCS#12 iteration count is unchanged.

## Follow-ups (2026-11-25)

### 3. An unmanaged session connect reuses the validated client key

An unmanaged `Session` (no channel manager) resolves its certificate from
the configured store on purpose ("unmanaged callers retain configured-store
selection, including explicit store replacements"). Both the channel
(`DefaultSessionFactory`) and the session itself did so through
`SecurityConfiguration.FindApplicationCertificateAsync`, which decrypted the
client key twice per connect.

That lookup differs from the manager's load in two ways:

- **Store resolution.** It opens the store through the built-in resolution
  (`CertificateStoreIdentifier.CreateStore`), not through the manager's
  providers. For the type `Directory` the built-in resolution always creates a
  `DirectoryCertificateStore`; a type registered with
  `RegisterCertificateStoreType` cannot replace it. The manager reuses keys
  only from a store its own providers serve with the built-in
  `DirectoryStoreProvider`. So when both apply, both read the same directory
  with the same store class.
- **Application URI.** It passes no application URI, and the manager passes the
  configuration's. The manager falls back to lookups by application URI when the
  identifier's subject or thumbprint finds nothing, so it can hold a key that
  this lookup would not find. Reusing such a key would make a session connect
  with a certificate that the store lookup rejects.

`SecurityConfiguration` now has an internal overload of
`FindApplicationCertificateAsync` that first asks
`CertificateManager.TryAcquireLoadedApplicationKeyForConfiguredStore`. It
returns the remembered key only when all of these hold:

- the identifier has a thumbprint (the certificate check fills it in), so in
  the unchanged store only that certificate can match;
- the identifier's store is a directory store for the built-in resolution
  (explicit `StoreType`, or `DetermineStoreType` of the path). The manager's
  snapshot check adds its own provider check;
- the lookup, password provider, snapshot and 30 s age match, as for
  `TryAcquireLoadedApplicationKey`;
- the certificate matches the identifier's thumbprint, subject and certificate
  type with the directory store's own match
  (`DirectoryCertificateStore.MatchesPrivateKeyLookup`) and no application URI.
  The store's first lookup applies exactly this match, so it would load the
  same certificate.

Otherwise the key is loaded from the store as before. An explicit replacement
of the configured identifier, the case the remark protects, changes the lookup
or the store snapshot, so the replacement is loaded from the store. With a mocked
`ICertificateManager` (the existing regression test) nothing is reused. The
managed path (channel manager, `AcquireApplicationCertificateBySecurityPolicy`)
is unchanged.

Tests: `CertificateManagerLoadedKeyTests` covers reuse by thumbprint, no
reuse without a thumbprint, no reuse of a key that the manager found only through
its application URI, and no reuse with a custom directory provider.

### 4. Loading only the keys the policies use (evaluated, not implemented)

The idea: load at validation only the keys of the certificate types that the
configured security policies need, or load each key lazily on first use.

- **Server.** `ServerConfiguration.SecurityPolicies` names the policies. A key
  type that no policy uses would not be decrypted at startup. The test fixture
  (`ServerFixture`) enables the RSA and the ECC policies, so all five types
  are in use there and the saving is zero. A
  production server usually configures only the certificate types it uses.
- **Client.** The policy is chosen per connect from the server's endpoints, so
  a client has no configured subset. Lazy loading would only move the
  decryption into the first connect that uses each type.
- **Broken keys.** Today a key that cannot be read or does not match its
  certificate fails `ApplicationConfiguration.ValidateAsync` and the
  certificate check at startup. With lazy loading it would fail the first
  secure channel or the first GDS push that needs it, at runtime and on
  another thread. With policy filtering, the key of an unused type would not be
  checked until a configuration change (push management, a new policy) needs
  it. `CheckApplicationInstanceCertificatesAsync` also creates or renews every
  configured certificate, which needs every key, so that check would have to
  change as well.

The measured setups would gain nothing, and the change moves broken-key
reporting out of validation. It is not implemented. A server that configures
certificate types it does not use can remove those identifiers instead.

### 5. Same-key PEM renewal rewritten in place

`DirectoryCertificateStoreTests.LoadPrivateKeyAsyncFindsSameKeyPemRenewalRewrittenInPlaceAsync`:
`certs/app.pem` holds A and `private/app.pem` its key. A lookup by subject
warms the store's cache, then `certs/app.pem` is rewritten with B, a renewal of
A with the same key (`CreateRenewal`). The key file is not touched. The lookup
by subject returns B with its key, and the lookup by A's thumbprint returns
nothing. The store's directory check alone (timestamps, file count) does not
notice the change. The per-file check of the lookup does.

### PFX loads and paired A/B (follow-up 3)

Measured 2026-11-25 10:55–11:00. A is the #5171 head `9c497dc54`; B is this
follow-up (`ef25f9bb7`). Both use the same probe and are net10.0 Release builds
copied to short paths (`C:\sA`, `C:\sB`). The PKI folder is warm, shared and
pre-warmed once. Each side ran 5 alternating rounds (A/B, B/A) with
`OPCUA_STARTUP_PROBE_CYCLES=10`, serially and with
`OPCUA_STARTUP_PROBE_WORKERS=4`: 50 cycles per cell, medians in ms, cold cycles
excluded. All runs passed. The probe ran under `bench.lock`. Both builds carry
the same temporary, environment-guarded PFX trace line (`OPCUA_PFX_TRACE`, not
committed), which is off during the timing rounds.

PFX loads, StandardServer, warm PKI, cold + 1 cycle (2 cycles), from the trace:

| Caller | A | B |
|---|---:|---:|
| `ValidateAsync` (server 5 + client 5 per cycle) | 20 | 20 |
| Connect (`Session.LoadInstanceCertificateEntryAsync`: channel and session) | 4 | 0 |
| **Total (per cycle)** | **24 (12)** | **20 (10)** |

| Mode | Server | A connect | B connect | A total | B total | Total change |
|---|---|---:|---:|---:|---:|---:|
| Warm | StandardServer | 39.2 | 21.8 | 280 | 249 | −11 % |
| Warm | ReferenceServer | 43.4 | 23.3 | 892 | 817 | −8 % |
| 4 workers | StandardServer | 48.8 | 32.8 | 605 | 574 | −5 % |
| 4 workers | ReferenceServer | 52.1 | 33.2 | 1421 | 1382 | −3 % |

Per-round medians of connect (A/B):

- Warm StandardServer 32.0/20.7, 34.3/20.3, 33.1/21.7, 44.1/21.0, 191.2/26.7.
- Warm ReferenceServer 34.8/20.8, 39.1/24.1, 41.9/21.1, 54.2/27.0, 71.8/27.3.
- 4 workers StandardServer 43.4/31.3, 41.4/35.2, 49.8/29.1, 140.5/34.3, 55.3/34.4.
- 4 workers ReferenceServer 47.0/33.4, 47.2/31.6, 44.2/31.3, 55.9/38.7, 140.5/37.8.

Connect is faster in every round of every cell: ~17–20 ms (−44 %) warm and
~16–19 ms with 4 workers, which is the two key decryptions. The other phases
are unchanged within noise. Server load was 39.3 → 36.6 (StandardServer) and
40.0 → 37.0 (ReferenceServer), the same code on both sides. Totals are
noisier: per-round warm StandardServer totals were 226/255, 252/238, 250/248,
313/235 and 852/298, and a round where A was faster occurs in each cell. The
total change in the table is the connect saving plus noise on a shared
machine.
