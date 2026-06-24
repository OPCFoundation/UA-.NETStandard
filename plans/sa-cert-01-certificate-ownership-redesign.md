# SA-CERT-01 — `Certificate` reference-counted ownership redesign

> Standalone work item, to be executed on its own (NOT bundled into a feature branch).
> Status: **open / accepted-risk Info finding.** A first implementation was attempted
> on 2026-06-24 and reverted (see "Attempt outcome" below).

## Problem / root cause
`Opc.Ua.Security.Certificates.Certificate`
(`Stack/Opc.Ua.Security.Certificates/X509Certificate/Certificate.cs`) is a single
object holding `m_refCount` (start 1) and the inner `X509Certificate2`. `AddRef()`
returns **`this`** (the same instance) and increments the count; each owner is
expected to call `Dispose()` once, decrementing. Because all logical owners share ONE
instance and call the SAME `Dispose()`, an owner that **double-disposes its own
logical reference** over-decrements the shared count and prematurely disposes the
`X509Certificate2` still used by other owners (CWE-672 / CWE-416).

A per-instance idempotency guard does **not** work: it breaks legitimate per-AddRef
disposal (since `AddRef()` returns the same instance, the same object is intentionally
disposed once per AddRef). Verified by the Core `RefCounting` / GetIssuers leak tests.

## Confirmed facts
- `Certificate` has **no subclasses** (repo-wide search) — safe to restructure.
- `Equals(Certificate)`/`GetHashCode` are **by value** (not reference) — a distinct
  AddRef handle is safe for dictionary/equality use.
- ~30 `AddRef()` call sites use the return as a new owned reference; **none rely on the
  returned identity being the same object** at the call site — BUT several subsystems
  (stores/collections/resolvers) rely on the end-to-end refcount arithmetic that
  `AddRef`-returns-`this` produces (see Attempt outcome).
- Counters: `InstancesCreated` increments per `new Certificate(...)`; `InstancesDisposed`
  increments when refcount reaches 0; leak tests assert `InstancesCreated ==
  InstancesDisposed` (cores created == cores disposed). DEBUG-only: `Track()`, finalizer
  `~Certificate` (leak if `m_refCount>0`), `EnumerateLiveCertificates`.

## Design: per-owner handle over a shared reference-counted core
1. Private `sealed class CertificateCore` holds the shared state: `X509Certificate2 X509`,
   `int m_refCount` (start 1), `AddRef()` (throws `ObjectDisposedException` if was 0),
   `Release()` (decrements; on 0 disposes `X509` + increments `s_instancesDisposed`).
2. `Certificate` becomes a thin **handle**: `private readonly CertificateCore m_core;`
   + `private int m_disposed;`.
   - Public ctors create a NEW core (refcount 1) and increment `s_instancesCreated`
     (one per core — preserves the leak-test invariant).
   - Private ctor `Certificate(CertificateCore core)` shares an existing core and does
     **not** increment `s_instancesCreated`.
   - `internal X509Certificate2 X509 => m_core.X509;`
   - `AddRef()`: `m_core.AddRef(); return new Certificate(m_core);` (distinct handle).
   - `Dispose(bool)`: `if (Interlocked.Exchange(ref m_disposed,1)!=0) return;
     m_core.Release(); GC.SuppressFinalize(this);` (idempotent per handle).
3. Counter semantics preserved: created = cores, disposed = cores released to 0.
   Correct balanced code behaves identically; only a buggy double-Dispose of one handle
   becomes a safe no-op.
4. DEBUG leak tracking per handle: `Track()` per handle; finalizer reports a leak if
   `m_disposed==0`; `EnumerateLiveCertificates` yields `m_core.RefCount`.

## Attempt outcome (2026-06-24) — why a dedicated effort is needed
The redesign above was implemented and built **0-warning**; the full Core suite had
**only 2 of 3353 failures**:
1. a test-only bad assumption (the certificate builder makes multiple cores), and
2. `GetIssuersAsyncReturnedReferencesAreCallerOwnedAndDisposable`
   (`Tests/.../CertificateManager/CertificateManagerTests.cs`) — createdDelta=1 vs
   disposedDelta=0.

Failure #2 is the blocker: it exposes that the `DirectoryCertificateStore` parsed-cert
**cache** + `CertificateCollection` + `CertificateIdentifierResolver` /
`CertificateValidationCore` ownership flows are tuned to `AddRef`-returns-`this`
arithmetic. Under the distinct-handle model one core reference is left unreleased in
that path. Reconciling it requires a **stack-wide audit of every AddRef/Dispose
pairing** (stores, collections, resolvers, validators, transport, encrypted secret),
which is disproportionate to an Info-level latent finding with no concrete exploit, and
must be validated against the entire stack test suite — hence a standalone work item.

The attempt was reverted; `GetIssuers` + `RefCounting` pass again.

## Recommended execution (standalone)
1. Land `CertificateCore` + handle conversion (as above) behind the existing public API.
2. Audit and fix EVERY AddRef/Dispose site so ownership is handle-correct:
   `DirectoryCertificateStore` (cache entry ownership + Enumerate/FindByThumbprint),
   `CertificateCollection` (Add/Insert/indexer/Dispose), `CertificateIdentifier(Resolver)`,
   `CertificateValidationCore.GetIssuersAsync`, `HttpsTransportListener`,
   `EncryptedSecret`, `RejectedCertificateProcessor`, client channel cert rotation.
3. Add a regression test for the exact SA-CERT-01 scenario: with two live references
   (root + `AddRef()`), double-`Dispose()` of ONE must not free the shared inner cert;
   the other reference stays usable; full release disposes exactly once; counters balance.
4. Validate: build Core all-TFM (0 warnings) + run the FULL `Opc.Ua.Core.Tests`
   (RefCounting, GetIssuers leak, CertificateFactory, validator, LeakDetectionSetup)
   plus a broad Server/Client/PubSub sweep (Certificate is foundational). Mark
   SA-CERT-01 remediated only when all green.

## Risk
Foundational class used stack-wide. Treat as a focused, well-tested migration on its
own branch. If a leak/refcount test cannot be reconciled, stop and re-scope.
