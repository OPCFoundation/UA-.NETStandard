# Assert.Ignore Implementation Plan

> **Status**: Living document. Updated as implementation progresses.
> **Last updated**: 2026-05-04

## Background

`Tests/Opc.Ua.Client.Conformance.Tests` ports the OPC Foundation Compliance Test Tool
(CTT) JavaScript test scripts to NUnit. As tests were initially scaffolded, many were
left as placeholder stubs that just call `Assert.Ignore(...)`. Some real tests also
fall back to `Assert.Ignore` when prerequisite features (e.g., shelving state) are not
exposed by the in-process reference server.

This plan documents the inventory and a strategy for converting every `Assert.Ignore`
into a real `Assert.Pass` / `Assert.Fail` outcome.

## Inventory (after 2026-05-04 cleanup pass)

| Category | Count | Description |
|---|---:|---|
| Stubs deleted (no JS) | 34 | Removed; placeholder tests with no corresponding CTT JS file. |
| `Assert.Ignore`-only stubs (CTT JS exists) | 257 | Remain in code; need real implementation. |
| Tests with logic that conditionally skip | 351 | Real tests; the `Assert.Ignore` paths are taken when an optional feature is absent. |
| **Total `Assert.Ignore` calls remaining** | **608** | |

### Stub classification (257)

| Class | Count | Notes |
|---|---:|---|
| `injection-required` | 72 | CTT JS uses `ServiceResult: StatusCode.Bad...` or `ExternalFunction` to mutate the response. Cannot be reproduced from a real client; needs a server-side mock that injects bad responses. |
| `call-only-helper` | 5 | JS body just calls `<ServiceSet>Helper.Execute({...})`. Helper does the actual work in CTT framework code. |
| `client-side-portable` | 180 | JS contains client-side test logic but uses CTT-specific helpers (`AggregateHelper`, `Test.Execute`, `CUVariables`, etc.). True port requires implementing equivalent C# helpers or rewriting from scratch with the same goal. |

## Implementation strategy

### Tier A — Quick wins (target: 50-80 tests, ~1 day each)

Categories where the test logic is simple enough to port directly:

1. **Address Space — type/node existence checks** (~10 tests)
   - Test pattern: read attribute → assert NodeId/Status.
   - Effort: trivial; can use `ReadValueAsync` helpers.

2. **Discovery — Find Servers / Get Endpoints** (already done, double-check stubs)
   - Effort: low.

3. **Base Info — RequestServerStateChange / ResendData / GetMonitoredItems** (~22 tests)
   - Test pattern: call standard server method → verify state changes.
   - Effort: medium; requires the methods to be implemented on the reference server.

### Tier B — Helper-port required (target: 80-150 tests, ~1-2 weeks each block)

Categories that require porting a CTT helper library to C#. Each block is
self-contained but non-trivial:

1. **Aggregate – Base** (32 stubs) + **Historical Access {Insert, Delete, Read} Value** (59 logic-tests)
   - Port `AggregateHelper.PerformSingleNodeTest` with its config/time/processing-interval enums.
   - Implement `CUVariables` equivalent (test data registration).
   - Effort: 1 week for the helper, 2-3 days to wire all 91 tests.

2. **GDS Application Directory / LDS-ME Connectivity** (33 stubs)
   - Need a working LDS or local discovery server in the test fixture.
   - Port CTT's GDS helper.
   - Effort: 1-2 weeks.

3. **Security User {Anonymous, X509, NamePwd}** (~38 stubs + logic)
   - Each is mostly endpoint discovery + activate-session with a specific token.
   - Effort: medium; share helpers across the three families.

4. **Security Certificate Validation / Policy Support** (~24 stubs)
   - Need test fixtures that publish multiple endpoints with each policy.
   - Effort: medium-high.

### Tier C — Server-mock required (target: 72 stubs)

These tests rely on injecting bad responses (`ServiceResult: BadFoo` or
`response.X = mutated`). Real OPC UA clients cannot trigger these conditions against
a well-behaved server. Two options:

- **Option C1**: Build a configurable proxy/stub OPC UA server that lets a test
  configure the response it should produce for the next request. NUnit fixtures
  spin up the stub and exercise the failure path.
  - Effort: 2-3 weeks for the proxy infrastructure, then 1-2 days per test family.

- **Option C2**: Leave them as `Assert.Ignore` and document the limitation.
  - Effort: zero. README already lists them as ⏭️.

**Recommendation**: Option C2 unless a stakeholder explicitly wants Option C1.

### Tier D — Conditional-skip refinements (target: 351 logic-with-Ignore tests)

These are real tests that gracefully skip when a prerequisite is missing. Two paths
to convert them to `Assert.Pass`/`Assert.Fail`:

1. **Add the missing feature to the reference server** so the prerequisite is always
   satisfied (preferred for OPC UA-mandatory features).
2. **Hard-fail when the prerequisite is absent** if the conformance unit treats the
   prerequisite as mandatory.

Top blocks:

| Unit | Count | Strategy |
|---|---:|---|
| Historical Access Insert/Delete/Read | 59 | Tier B helper port enables most. |
| Node Management Add/Delete | 25 | Already partially implemented after May 3 work; revisit residual skips. |
| Security Role Server * | 39 | Implement role/identity management on reference server (existing stub in `RoleManagementHandler`). |
| Push Model GDS | 9 | Tier B GDS port. |
| Address Space Notifier Hierarchy | 9 | Add notifier hierarchy nodes to AlarmNodeManager. |
| Data Access Analog/MultiState | 15 | Add missing properties (EuRange, EngineeringUnits) to ReferenceNodeManager. |
| Security None / Basic 128Rsa15 / 256 / Aes / Sha256 | 50 | Configure endpoint per policy, skip-or-fail consistently. |
| A&C Shelving / Refresh2 | 11 | Set `optional: true` in `AlarmNodeManager.CreateAlarm` calls so ShelvingState/SuppressedState exist on alarm holders. |
| AliasName Base | 7 | Already partially implemented; review residual. |
| Subscription Minimum 02 | 7 | Verify timing/reliability assumptions. |
| Auditing Secure Communication | 4 | Need server endpoint with security to drive audit events. |

## Phased execution roadmap

| Phase | Scope | Outcome |
|---|---|---|
| 1 (done) | Delete 34 unmappable stubs; refresh README. | -34 stubs. |
| 2 | Tier A: Base Info methods, AliasName residuals, Address Space simple checks. | -50 stubs / -10 logic-skips. |
| 3 | Tier B-1: Aggregate helper port + Historical Access. | -32 stubs / -59 logic-skips. |
| 4 | Tier B-3: Security User token families. | -38 stubs. |
| 5 | Tier B-2: GDS Application Directory / LDS-ME. | -33 stubs. |
| 6 | Tier D: Role server, Push, Notifier, DA Analog/MultiState, A&C optional features. | -83 logic-skips. |
| 7 | Tier C decision: server-mock (or close as documented limitation). | -72 stubs (kept as Ignore with explanation). |

## Open questions / decisions

- **Server-mock infrastructure**: do we want it (Tier C, Option C1)? If no, lock the
  72 server-mock stubs as a permanent `Assert.Ignore` with a stable explanation.
- **Optional feature exposure**: should `AlarmNodeManager` create alarms with
  `optional: true` so ShelvingState/SuppressedState exist? This unblocks ~11 logic
  skips with a small server change.
- **GDS test infrastructure**: spin up a real GDS in `[OneTimeSetUp]`? That's a
  significant test fixture investment.

## Cleanup completed in this pass

- Deleted 34 stubs whose ConformanceUnit/Tag did not map to any CTT JS file (no
  porting source).
- Updated README: dropped 27 corresponding rows.
- Generated `plans/stub_classified.csv` (257 rows) and `plans/logic_ignore.csv`
  (351 rows) with per-test classification for future work.

## Tier A pass (2026-05-04)

- Re-classified all 257 remaining stubs against CTT JS contents:
  - 25 `manual-not-implemented` (CTT just shows a message box; no automation possible)
  - 32 `aggregate-helper` (Tier B helper port)
  - 52 `injection` (Tier C server-mock)
  - 54 `test-function` (real automated logic; most need helpers)
  - 69 `other` (mixed)
  - 24 `no-js` (in second-pass scan; mostly metadata edge cases)
- Deleted the 25 `manual-not-implemented` stubs:
  - `BaseInfoBehavioralTests.cs`: 16 (RequestServerStateChange*, DeviceFailure*, EventQueueOverflow004, GetMonitoredItemsErr002, GetMonitoredItemsErr004)
  - `AuditingOperationTests.cs`: 3 (AuditEventAfterWrite/Call IsIgnored, AuditConditionSilenceAndResetAreIgnored)
  - `SecurityPolicyDepthTests.cs`: 6 (ConnectWith{Unsupported,Basic}* + 4 cert/token *Ignored)
- Ported 1 cross-session test:
  - `GetMonitoredItemsErr003CrossSessionTesting` → `GetMonitoredItemsErr003CrossSessionReturnsBadStatusAsync`
    creates a subscription on session 1, opens session 2, calls
    `Server.GetMonitoredItems(subscriptionId)` from session 2, asserts a Bad
    status. Passes.
- README: dropped 25 corresponding rows.

## Remaining backlog (after Tier A pass)

| Category | Count | Strategy |
|---|---:|---|
| `injection` (server-mock-required) | 52 | Tier C — keep as `Assert.Ignore` unless a stakeholder wants a mock server. |
| `aggregate-helper` (Aggregate – Base) | 32 | Tier B — port `AggregateHelper.PerformSingleNodeTest`. |
| `test-function` (real automated tests with CTT helpers) | 54 | Tier B — each block needs its own helper port. |
| `other` (mixed CTT helper-using tests) | 69 | Tier B. |

| Logic-with-Ignore | 351 | Tier D — fix prerequisites in reference server. |
