# Plan 23 — GDS Client X.509 API Modernization (P4.1, deferred)

## Status

**Deferred** during the GDS Client.Common modernization
(commits `cb055b1d` and `9f29b751`). All other phases (P0.1–P5.2)
landed; this is the remaining work item.

## Problem

The public surface of `Libraries/Opc.Ua.Gds.Client.Common` still
exposes legacy .NET certificate types and raw `char[]` passwords:

| File | Line | Signature |
|------|------|-----------|
| `IGlobalDiscoveryServerClient.cs` | 185 | `Task StartSigningRequestAsync(..., char[] privateKeyPassword, ...)` |
| `GlobalDiscoveryServerClient.cs` | 757 | matching impl, calls `new string(privateKeyPassword)` |
| `IServerPushConfigurationClient.cs` | 130 | `Task AddCertificateAsync(..., X509Certificate2 certificate, ...)` |
| `IServerPushConfigurationClient.cs` | 141 | `Task<X509Certificate2Collection> GetRejectedListAsync(...)` |
| `ServerPushConfigurationClient.cs` | 541 | matching impl |
| `ServerPushConfigurationClient.cs` | 696 | matching impl, builds `new X509Certificate2Collection()` |
| `RegisteredApplication.cs` | 173 | `List<string> GetDomainNames(X509Certificate2 certificate)` |
| `CertificateWrapper.cs` | 40 | `X509Certificate2 Certificate { get; set; }` (kept; test-compat) |

Goals (per research §3.13):

1. Replace `X509Certificate2` parameters / returns with
   `Opc.Ua.Security.Certificates.IX509Certificate`.
2. Replace `X509Certificate2Collection` with the stack's collection
   abstraction (most likely `IReadOnlyList<IX509Certificate>` —
   confirm during implementation).
3. Replace `char[] privateKeyPassword` with `ReadOnlyMemory<char>` so
   callers can use `string.AsMemory()` and avoid the heap‑allocated
   `new string(char[])` in the impl.

## Why this was deferred

- `Opc.Ua.Security.Certificates.IX509Certificate` (in
  `Libraries/Opc.Ua.Security.Certificates/X509Certificate/IX509Certificate.cs`)
  is a property‑bag interface — it does **not** expose `RawData` and
  there is no static
  `IX509Certificate Certificate.From(X509Certificate2)` factory in the
  current stack. The push API needs to convert opaque server bytes
  (`GetRejectedList`) into managed certificate objects, and consumers
  pass `X509Certificate2` instances they already hold; without an
  interop story this turns into a leaky pseudo‑port.
- The cascade is wide — at minimum
  `Tests/Opc.Ua.Gds.Tests`, `Tools/GdsAdminUI`, and
  `Libraries/Opc.Ua.Gds.Server.Common` consume these signatures.
  Estimated ~50 call sites.
- All other phases shipped clean (Release build 0/0,
  `Opc.Ua.Gds.Tests` 518 passed). Holding P4.1 keeps the rest
  reviewable.

## Pre‑conditions before starting

1. Stack provides a public adapter or constructor that yields an
   `IX509Certificate` from raw DER bytes **and** from an existing
   `X509Certificate2`. Likely shape:
   - `IX509Certificate Certificate.Create(ReadOnlySpan<byte> der)`
   - `IX509Certificate Certificate.From(X509Certificate2 cert)` (or
     an explicit conversion operator on a concrete wrapper).
2. The collection abstraction is decided. Either:
   - extend `IReadOnlyList<IX509Certificate>` everywhere, or
   - introduce `CertificateCollection` in `Opc.Ua.Security.Certificates`.
3. `IX509Certificate` exposes (or is augmented with) the members the
   GDS layer actually needs: `RawData` / `Export()`, `Subject`,
   `Thumbprint`, plus any SAN accessor used by
   `RegisteredApplication.GetDomainNames`.

## Phased work

### Phase A — GDS.Client.Common public surface

1. Update `IGlobalDiscoveryServerClient.cs` and
   `IServerPushConfigurationClient.cs`:
   - `char[] privateKeyPassword` → `ReadOnlyMemory<char> privateKeyPassword`
   - `X509Certificate2` → `IX509Certificate`
   - `X509Certificate2Collection` → chosen collection type.
2. Update implementations
   (`GlobalDiscoveryServerClient`, `ServerPushConfigurationClient`).
   Replace `new string(char[])` with
   `privateKeyPassword.Span.ToString()` (or pass through to a
   `ReadOnlyMemory<char>`‑aware overload if/when added).
3. Add `[Obsolete]` legacy overloads forwarding to the new ones for
   one release cycle:
   - `Task AddCertificateAsync(NodeId, X509Certificate2, bool, CancellationToken)`
     → forwards via `Certificate.From(cert)`.
   - `char[]` overload of `StartSigningRequestAsync` →
     `privateKeyPassword.AsMemory()`.
4. Update `CertificateWrapper.Certificate` to `IX509Certificate`. The
   wrapper is internal‑use; review whether tests depend on the
   setter accepting `X509Certificate2` and add an obsolete
   X509Certificate2 setter that wraps if so.
5. Update `RegisteredApplication.GetDomainNames(X509Certificate2)`
   signature accordingly; keep `[Obsolete]` X509 overload.

### Phase B — Test cascade

- `Tests/Opc.Ua.Gds.Tests` — adjust call sites to either:
  - construct `IX509Certificate` via the new factory, or
  - use the `[Obsolete]` shims for legacy paths during transition.
- Validate `RegisteredApplicationTests` XML round‑trip parity.
- Run: `dotnet test Tests/Opc.Ua.Gds.Tests/Opc.Ua.Gds.Tests.csproj -c Release -f net10.0 --filter "TestCategory!=Network"`.

### Phase C — Downstream consumers

- `Libraries/Opc.Ua.Gds.Server.Common` — update where it calls
  `IServerPushConfigurationClient` / `IGlobalDiscoveryServerClient`.
- `Tools/GdsAdminUI` — UI bindings around the rejected list /
  certificate add flow.
- Verify `dotnet build UA.slnx -c Release` stays at 0/0.

### Phase D — Cleanup

- Remove `[Obsolete]` legacy overloads in the next major release.
- Update `Docs/` if certificate handling is documented.

## Acceptance criteria

- Public GDS+Push interfaces expose `IX509Certificate` and
  `ReadOnlyMemory<char>` exclusively (legacy types only on
  `[Obsolete]` shims).
- Full UA.slnx Release build clean.
- `Opc.Ua.Gds.Tests` (`TestCategory!=Network`) at parity or better
  with the pre‑P4.1 baseline (518 passed / 0 failed / 25 skipped).
- No `new string(char[])` of a private‑key password remains in
  `GlobalDiscoveryServerClient`.

## References

- Research §3.13 in
  `C:/Users/mschier/.copilot/session-state/9fc7524d-bbeb-4059-8add-d955832a7e86/research/how-to-refactor-and-modernize-the-gds-apis-client-.md`.
- Master plan in
  `C:/Users/mschier/.copilot/session-state/9fc7524d-bbeb-4059-8add-d955832a7e86/plan.md`
  ("Out of scope" → P4.1 deferred).
- Sibling plan `plans/22-objecttype-variable-proxies.md`.
