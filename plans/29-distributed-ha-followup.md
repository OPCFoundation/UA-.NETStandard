# Plan 29 — Distributed / HA Server Integration (follow-up to Plan 28)

## Status

**In progress.** The enabler seam and dynamic ServiceLevel are implemented and tested on this branch; the deeper integration, Redis (blocked offline), and sample remain. Follow-up to [Plan 28](28-distributed-ha-node-state.md), whose P1–P6 blocks + P8 docs are already merged.

### Implementation status (this branch)

| Todo | State | Notes |
|------|-------|-------|
| `f-startup-seam` (F-A1) | ✅ done | `IServerStartupTask` (`Hosting/IServerStartupTask.cs`) invoked by `OpcUaServerHostedService` after `StartAsync` with `CurrentInstance` (DI-resolved; no forced subclass). |
| `f-servicelevel` (F-A2) | ✅ done | `ServiceLevelStartupTask` drives `Server.ServiceLevel` from `IServiceLevelProvider` (default 255 → no behavior change); `AddServerServiceLevel(...)` fluent. |
| `f-nm-adapter` (F-B1) | ✅ done | `ILocalAddressSpaceSource` opt-in implemented on `CustomNodeManager2` via a private nested adapter over `PredefinedNodes` (top-level filter, add/remove + events). |
| `f-serverredundancy` (F-A3) | ✅ done | `ServerRedundancyStartupTask` sets `RedundancySupport` + populates `RedundantServerArray` (`ArrayOf<RedundantServerDataType>` via `Variant.FromStructure`); `AddServerRedundancy(...)` fluent + `ServerRedundancyOptions`. |
| `f-fluent-di` (F-B2) | ✅ done | `UseDistributedAddressSpace(...)` + `DistributedAddressSpaceOptions` register store, election, service-level provider + startup tasks. |
| `f-wire-synchronizers` (F-B3) | ✅ done | `DistributedAddressSpaceStartupTask` builds the store with the server message context, registers it, starts election, and attaches a synchronizer to every `ILocalAddressSpaceSource` node manager. Integration-tested (seeds an opted-in manager into the shared store). |
| `f-sample` (F-E) | ✅ done | `Applications/RedundantServer` (Program + `HaSampleNodeManager` + README), added to `UA.slnx`; builds clean. |
| `f-docs` (F-Docs) | ✅ done | `Docs/HighAvailability.md` updated with the working DI API (`UseDistributedAddressSpace`/`AddServerRedundancy`) + status; Kubernetes section present. |
| `f-e2e-test` (F-C) | ◑ covered → **superseded** | Core wiring is integration-tested (`DistributedAddressSpaceStartupTaskTests`; `AddressSpaceSynchronizerTests`; client `ServerRedundancyHandlerTests`). The full two-server harness + **required security tests** is replanned as `f-e2e-test-secure` in **[Plan 30](30-distributed-ha-session-security.md)**. |
| `f-session-sharing` (F-A4) | ⛔ **superseded** | Security review found the token-only reconnect is **unsafe (session hijack / nonce replay)** and the shared store lacks integrity/encryption. Redesigned + gated in **[Plan 30](30-distributed-ha-session-security.md)** (`f-session-sharing-secure` + `sec-*` store hardening). |
| `f-redis` (F-D) | ⛔ blocked | `StackExchange.Redis` not in the offline NuGet cache — cannot restore/build in this environment. |

Build/test: server builds clean on net10.0 **and net48**; `dotnet test Tests/Opc.Ua.Server.Tests/Opc.Ua.Server.Tests.csproj -f net10.0 -c Release --filter "Category=Distributed" -p:NuGetAudit=false` → **59 passing**. No regression (Historian suite 115 passing).

**Original design / proposal** follows. Covers the server-lifecycle integration that makes Plan 28's blocks usable in a running `StandardServer`, the DI/fluent surface, a Redis backend, an end-to-end test, and a worked sample.

## Scope (confirmed with requester)

| Workstream | In scope |
|------------|----------|
| Server integration (A) | Drive the live `Server.ServiceLevel` node; populate `Server.ServerRedundancy`; share session state via `ISharedSessionStore` in `SessionManager`. |
| DI + node-manager adapter (B) | `ILocalAddressSpace` adapter over `CustomNodeManager.PredefinedNodes`; `UseDistributedAddressSpace(...)` on `IOpcUaServerBuilder`; post-start wiring. |
| End-to-end test (C) | Two real server instances sharing a store; client failover via `ManagedSession`; subscription transfer. |
| Redis provider (D) | `ISharedKeyValueStore` over Redis in a **separate** package. |
| Sample (E) | Worked HA reference server. |
| Redundancy modes | **Both** non-transparent (per-replica endpoints + `ServiceLevel`) and transparent (single endpoint + subscription transfer). |
| CRDT | **Out of scope** (remains the future conflict-free active/active option). |

## Integration map (verified)

Startup / lifecycle:

- `OpcUaServerHostedService.ExecuteAsync` creates `m_server = new StandardServer(m_telemetry, m_timeProvider)` (`Libraries/Opc.Ua.Server/Hosting/OpcUaServerHostedService.cs:218`), registers node managers, then `await m_application.StartAsync(m_server, …)` (`:231`). **There is no server-type factory** — wiring must be a post-start hook or a `StandardServer` subclass.
- `StandardServer.CurrentInstance` (`IServerInternal`) is public after start (`Libraries/Opc.Ua.Server/Server/StandardServer.cs:2458`) — gives access to node managers, `ServerObject`, `SessionManager`, `MessageContext`/`NamespaceUris`.
- Protected hooks `OnNodeManagerStarted(IServerInternal)` (`:4009`, after predefined nodes load) and `OnServerStarted(IServerInternal)` (`:4018`, fully started) exist but require a subclass.
- `MasterNodeManager.StartupAsync` calls each node manager's `CreateAddressSpaceAsync` (`Libraries/Opc.Ua.Server/NodeManager/MasterNodeManager.cs:333`), which loads predefined nodes.

Address space:

- `CustomNodeManager.PredefinedNodes` populated via `LoadPredefinedNodes`/`AddPredefinedNode` (`CustomNodeManager.cs:507-591`); runtime mutation via `CreateNode` (`:381-411`) and `DeleteNode`/`RemovePredefinedNode` (`:435-654`). Top-level enumeration pattern at `DeleteAddressSpace` (`:968-980`).
- Server message context / namespace table: `ServerInternalData.MessageContext` / `NamespaceUris` (`ServerInternalData.cs:102-117`); reachable via `ServerSystemContext` (`ServerSystemContext.cs:48-52`).

ServiceLevel:

- Set once at `serverObject.ServiceLevel!.Value = 255` in `ServerInternalData.CreateServerObjectAsync` (`ServerInternalData.cs:~770`). Runtime update: `ServerObject.ServiceLevel.Value = x; ServerObject.ServiceLevel.ClearChangeMasks(systemContext, false);`. `ServerObject` is public on `IServerInternal`.

ServerRedundancy:

- `Server.ServerRedundancy` is a **`ServerRedundancyType` instance** (i=2034) — natively only `RedundancySupport` (i=2035). `ServerUriArray` (i=2040) is on `NonTransparentRedundancyType` (i=2039); `CurrentServerId` (i=2037) + `RedundantServerArray` (i=2038) on `TransparentRedundancyType` (i=2036). The SDK already re-adds `RedundantServerArray` as an optional child via `DiagnosticsNodeManager.AddServerRedundancySdkOptionalChildren` (`DiagnosticsNodeManager.cs:528-540`). We extend this seam to set `RedundancySupport` and add+populate `ServerUriArray` (non-transparent) / `CurrentServerId` (transparent) as optional children.

SessionManager:

- `CreateSessionAsync` (`SessionManager.cs:193`), `ActivateSessionAsync` (`:346`), token lookup `m_sessions.TryGetValue(authenticationToken, …)` (`:611`). Create generates `authenticationToken` + `serverNonce` and `m_sessions.TryAdd(…)` (`:312-320`). Activate validates via `ValidateBeforeActivate` using `m_serverNonce`, `ClientNonce`, `m_userTokenNonce`, `ClientCertificate`. **Security-sensitive:** `m_serverNonce` / `m_userTokenNonce` must be shared (encrypted) for a standby to validate a reconnect with just the token. Create/Activate are `public virtual` → a `DistributedSessionManager` subclass is the natural seam, but needs a **session-manager factory** to be injected.

Redis: no Redis package is present today (`Directory.Packages.props` has no `StackExchange.Redis`).

## Proposed work

### A. Server integration

**A1 — Post-start wiring seam (the enabler).** Add an additive `IServerStartupTask` (name TBD) resolved from DI and invoked by `OpcUaServerHostedService` immediately after `StartAsync` returns, receiving `m_server.CurrentInstance`. This avoids forcing a `StandardServer` subclass. The distributed feature registers one such task that performs A2/A3/B3. (Alternative considered: a `DistributedStandardServer` subclass overriding `OnNodeManagerStarted` + a server-factory option on the hosted service — heavier; the startup-task seam is preferred and reusable by other features.)

**A2 — Dynamic `ServiceLevel`.** Resolve `IServiceLevelProvider` (default `ConstantServiceLevelProvider(255)` — no behavior change). Set the initial `ServerObject.ServiceLevel` from it (replacing the constant) and subscribe `ServiceLevelChanged` to update the node + `ClearChangeMasks` so monitored `ServiceLevel` items fire. The default keeps every existing test (which expects 255) green.

**A3 — `ServerRedundancy` population.** Make `AddServerRedundancySdkOptionalChildren` configuration-driven: set `RedundancySupport` to the configured mode (`None`/`Cold`/`Warm`/`Hot`/`Transparent`), and for non-transparent add+populate `ServerUriArray` and `RedundantServerArray` from the peer set; for transparent populate `CurrentServerId`. Peer set + mode come from `DistributedAddressSpaceOptions` (B2) / membership. Update these when membership changes.

**A4 — Session sharing.** Add a session-manager factory seam and a `DistributedSessionManager : SessionManager` that: persists a `SharedSessionEntry` (with the encrypted server nonce + identity metadata) on create/activate; on a reconnect whose token is unknown locally, consults `ISharedSessionStore`, decrypts the nonce, and materializes a local `Session` before the `BadSessionIdInvalid` path. The shared secret used to encrypt the nonce is a deployment secret (shared across replicas). **Requires a security review** (nonce/identity sharing). Document the trade-off and offer a "re-auth on failover" lighter mode as a fallback.

### B. DI + node-manager adapter

**B1 — `CustomNodeManagerAddressSpace`.** An `ILocalAddressSpace` adapter over a `CustomNodeManager`'s `PredefinedNodes`, raising `NodeAdded`/`NodeRemoved` from `CreateNode`/`DeleteNode` (hook the existing model-change emission, see `Docs/ModelChangeTracking.md`, or wrap the create/delete methods). `Context` = the node manager's `SystemContext`.

**B2 — `UseDistributedAddressSpace(this IOpcUaServerBuilder, Action<DistributedAddressSpaceOptions>)`.** Options: store backend (in-memory default or a `Func<IServiceProvider, ISharedKeyValueStore>` for Redis), HA mode (A/P or A/A), redundancy mode (non-transparent/transparent), leader-election config (static / lease / k8s), peer URIs, value-cache opt-in, session sharing on/off. Registers the A1 startup task. **Store creation is deferred** into the startup task so it can use the server's populated `IServiceMessageContext` (not a fresh `CreateEmpty`, which would mis-map namespaces). Direct-construction fluent for non-DI hosting mirrors `UseHistorian`.

**B3 — Startup wiring.** The startup task: builds the `INodeStateStore` with the server message context; registers it in `INodeStateStoreRegistry`; for each `CustomNodeManager`, wraps `PredefinedNodes` in `CustomNodeManagerAddressSpace`, creates an `AddressSpaceSynchronizer` with `isWriter = () => election.IsLeader`, and `SeedOrHydrateAsync` + `Start`. Starts the election + service-level provider; runs A2/A3.

### C. End-to-end test

Two in-process server instances (hosted or `StandardServer`) sharing one `InMemorySharedKeyValueStore`; a real `ManagedSession` client. Validate: write on the active leader replicates to the standby; on failover (stop leader / lower its `ServiceLevel`) the client re-selects the standby (highest `ServiceLevel`) via `DefaultServerRedundancyHandler`; subscriptions transfer; and fast reconnect via `AuthenticationToken`. Lives in an integration project (e.g. `Tests/Opc.Ua.Sessions.Tests` or a new `Tests/Opc.Ua.Server.HighAvailability.Tests`).

### D. Redis provider

New package **`Opc.Ua.Server.Redundancy.Redis`** (`Libraries/Opc.Ua.Server.Redundancy.Redis`) referencing `Opc.Ua.Server` + **StackExchange.Redis** (MIT — license-compatible; add to `Directory.Packages.props`). `RedisSharedKeyValueStore : ISharedKeyValueStore`:

- `TryGet`/`Set`/`Delete` → `StringGet`/`StringSet`/`KeyDelete`.
- `CompareAndSwap` → a Lua script (atomic compare-and-set / set-if-absent).
- `ScanAsync` → `SCAN` by prefix.
- `WatchAsync` → Redis keyspace notifications (pub/sub) for the prefix.

Keeping it in a separate package keeps the core `Opc.Ua.Server` dependency-free. **NativeAOT:** StackExchange.Redis uses reflection in places — add AOT tests in `Opc.Ua.Aot.Tests` exercising the Redis paths, or mark the package non-AOT and document. Integration tests use a Redis container / `testcontainers` or are gated on a `REDIS_URL`.

### E. Sample

A worked HA reference server — either a new `Applications/RedundantServer` or an `--ha` mode in `ConsoleReferenceServer` — wiring `UseDistributedAddressSpace` + lease election + `LeaderServiceLevelProvider`, with README instructions to run two instances against a shared store (in-memory single-process demo and Redis multi-process).

## Redundancy modes (both)

- **Non-transparent (now):** each replica is its own endpoint; A2 drives `ServiceLevel` (leader high, standby low) and A3 fills `RedundancySupport`/`ServerUriArray`/`RedundantServerArray`. The client (`DefaultServerRedundancyHandler` + `ManagedSession`) already fails over to the highest `ServiceLevel`.
- **Transparent (documented + helper):** a single virtual endpoint (k8s `Service`/LB) fronts replicas; `RedundancySupport = Transparent`, `CurrentServerId` set; failover is hidden and relies on the shared session store (A4) + subscription transfer (`Docs/TransferSubscription.md`). Mostly deployment + the A4 session sharing; no new transport.

## Security considerations

- Session nonce/identity sharing (A4) is the highest-risk item: store only encrypted material (`EncryptedSecret`/`CryptoUtils`), zero plaintext after use, redact in logs, and require a shared encryption key provisioned out-of-band. Run the security-review agent on A4.
- Redis traffic must be TLS + auth in production; document.

## Testing strategy

- Unit: `CustomNodeManagerAddressSpace` adapter, the startup task wiring (with a fake server internal), A2 service-level update, A3 redundancy population, A4 persist/restore (and the re-auth fallback), Redis store (against a local/container Redis or a fake).
- Integration: workstream C (two servers + client failover + subscription transfer + fast reconnect).
- AOT: Redis paths if the package is marked AOT-compatible.
- Coverage ≥80% for new non-application code; no regression in existing server/session tests.

## Documentation

- Update `Docs/HighAvailability.md` (wiring now implemented; `UseDistributedAddressSpace` usage; ServiceLevel/ServerRedundancy population).
- New `Docs/KubernetesDeployment.md` (replicaset, headless Service, Lease election, readiness tied to ServiceLevel, Redis).
- Update `Docs/MigrationGuide.md` (new opt-in feature), `Docs/README.md`, and the new package's `NugetREADME.md`.

## Phased todos

| Phase | Todo id | Summary | Depends on |
|-------|---------|---------|-----------|
| F-A1 | `f-startup-seam` | Additive `IServerStartupTask` seam invoked by the hosted service post-`StartAsync` with `CurrentInstance`. | — |
| F-A2 | `f-servicelevel` | Dynamic `Server.ServiceLevel` from `IServiceLevelProvider` (default 255); event-driven node update. | f-startup-seam |
| F-A3 | `f-serverredundancy` | Configurable `Server.ServerRedundancy` population (RedundancySupport + ServerUriArray/RedundantServerArray/CurrentServerId) for both modes. | f-startup-seam |
| F-B1 | `f-nm-adapter` | `CustomNodeManagerAddressSpace` (`ILocalAddressSpace` over `PredefinedNodes` with create/delete events). | — |
| F-B2 | `f-fluent-di` | `UseDistributedAddressSpace(...)` + `DistributedAddressSpaceOptions`; deferred store creation. | f-startup-seam |
| F-B3 | `f-wire-synchronizers` | Startup task wires store + per-node-manager synchronizers (writer = leader) + seed/hydrate/start. | f-nm-adapter, f-fluent-di |
| F-A4 | `f-session-sharing` | Session-manager factory seam + `DistributedSessionManager` persist/restore via `ISharedSessionStore` (encrypted nonce); re-auth fallback. Security review. | f-startup-seam |
| F-C | `f-e2e-test` | Two-server end-to-end test: replication, `ManagedSession` failover, subscription transfer, fast reconnect. | f-servicelevel, f-serverredundancy, f-wire-synchronizers, f-session-sharing |
| F-D | `f-redis` | `Opc.Ua.Server.Redundancy.Redis` package: `RedisSharedKeyValueStore` (CAS Lua, keyspace-notification watch) + tests; add StackExchange.Redis. | — |
| F-E | `f-sample` | HA reference sample wiring the fluent API + lease election + service level. | f-wire-synchronizers |
| F-Docs | `f-docs` | HighAvailability/Kubernetes/Migration/README + package NugetREADME. | f-wire-synchronizers, f-session-sharing |

## Open questions / risks

1. **Wiring seam shape.** Preferred: additive `IServerStartupTask` invoked by the hosted service (no forced subclass). Confirm vs. a `DistributedStandardServer` subclass + server-factory option. (Plan assumes the startup-task seam.)
2. **Session fast-reconnect security.** Sharing the server nonce enables true token-only reconnect but is security-sensitive; the lighter "recognize session + re-auth" mode is safer but not strictly "just the token". Plan ships encrypted-nonce sharing **plus** the re-auth fallback, gated by config; needs security review.
3. **ServerRedundancy on a fixed `ServerRedundancyType` instance.** Optional children for subtype-specific fields are added pragmatically (as the SDK already does for `RedundantServerArray`); confirm this is acceptable vs. re-typing the instance.
4. **Redis + NativeAOT.** StackExchange.Redis reflection may not be fully AOT-safe; decide AOT-support-with-tests vs. mark non-AOT + document.
5. **Active/active correctness** on the simple k/v still relies on single-writer election; conflict-free A/A remains the deferred CRDT work.

## Primary references

- `Libraries/Opc.Ua.Server/Hosting/OpcUaServerHostedService.cs:218,231` — server creation + start (no factory seam).
- `Libraries/Opc.Ua.Server/Server/StandardServer.cs:2458` (`CurrentInstance`), `:4009` (`OnNodeManagerStarted`), `:4018` (`OnServerStarted`).
- `Libraries/Opc.Ua.Server/NodeManager/MasterNodeManager.cs:333` (`CreateAddressSpaceAsync`).
- `Libraries/Opc.Ua.Server/NodeManager/CustomNodeManager.cs:507-591` (load/add), `:381-654` (create/delete), `:968-980` (top-level enumeration).
- `Libraries/Opc.Ua.Server/Server/ServerInternalData.cs:~770` (ServiceLevel constant), `:102-117` (MessageContext/NamespaceUris).
- `Libraries/Opc.Ua.Server/Diagnostics/DiagnosticsNodeManager.cs:528-540` (ServerRedundancy seam).
- `Libraries/Opc.Ua.Server/Session/SessionManager.cs:193,346,611,312-320` (create/activate/lookup); `Session.cs` nonce/identity fields.
- Plan 28 building blocks: `Libraries/Opc.Ua.Server/Distributed/*`.
