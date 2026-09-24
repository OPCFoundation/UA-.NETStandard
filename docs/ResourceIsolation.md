# Resource-isolation capacity planning (staged)

**This release adds an offline sizing calculator, not noisy-neighbor isolation.**
`ServerResourceIsolationOptions.CreatePlan` validates a prospective reassembly
partition and the configured Session/SecureChannel relationship. It returns an
immutable `ServerResourceIsolationPlan`; it does not change configuration,
acquire leases, reserve bytes, or install an admission policy.

There is deliberately **no server property, DI registration helper, hosting
option, or configuration-file binding** for enabling these profiles yet.
Registering the calculator in an application's DI container does not enable
protection. Running servers retain their existing shared admission behavior,
including the [incomplete-message budget](RateLimiting.md#incomplete-messages).
Balanced is the **planning default and intended eventual enforcement default**,
not the current server default.

## Profile selection

| Mode | What this calculator does |
| --- | --- |
| `SharedOnly` | Keeps the whole byte total shared; no reserved floors or fairness guarantee. |
| `FairShare` | The same capacity partition as SharedOnly. Weighted scheduling is not implemented. |
| `Balanced` | Proposes separate, non-borrowable bootstrap and reconnect byte floors, each sufficient for one maximum retained message by default. |
| `TrustedReservations` | Throws `NotSupportedException`: trusted-owner provisioning and classification are not implemented by this module. No silent fallback to Balanced. |

These options are independent of `ServerRateLimitOptions.Enabled`. Creating a
plan neither enables nor disables the existing rate limiters.

## Explicit sizing, without increasing totals

The caller supplies the **existing** `ChunkReassemblyBudget`, configured
`MaxSessionCount` and `MaxChannelCount`, and two finite deployment bounds:

- `maxRetainedChunkCount`: the maximum intermediate chunks that a permitted
  incomplete message can retain. Include a peer that sends intermediate chunks
  right up to the chunk-count limit, even though another final chunk would then
  be rejected.
- `maxPooledBufferLength`: the maximum actual backing-array length for any such
  chunk, including pool rounding and metadata, not its payload/wire length.

The conservative footprint `F` is their product, using 64-bit arithmetic.
These must be proven bounds across the applicable listeners and negotiations,
not measurements of typical traffic. `IBufferManager.GetExpectedBufferSize`
provides the buffer-manager sizing contract; a custom pool must actually honor
the assumed upper bound. The calculator cannot inspect or enforce that promise.
Independent worst-case chunk-count and array-length bounds can overestimate the
footprint; an impossible result is rejected, not silently reduced.

For Balanced, each unset floor becomes `F`. A manual floor must be at least
`F`. The two floors must fit **inside** the existing total, and the unreserved
remainder must also fit `F` so the shared class still supports one message.
The bootstrap floor must fit the existing sessionless threshold. That last
check is only a sizing constraint: the legacy threshold remains a **global
occupancy threshold, not a bootstrap reservation**. Activated-session traffic
can still fill the entire legacy budget.

SharedOnly and FairShare accept only zero or unset floor overrides, and require
one message to fit the total. They preserve even a zero sessionless threshold
(sessionless intermediate chunks disabled) or a threshold equal to the total.
The calculator does not impose Balanced's proposed floors on legacy servers.

All supplied counts/lengths must be positive and finite; zero does not mean
"auto" or "unlimited". A single-chunk-only workload needs no intermediate-byte
reserve and is outside this calculator's positive retained-footprint model.
Invalid totals/thresholds are rejected by `ChunkReassemblyBudget` itself.
No percentage policy or machine-memory heuristic is introduced.

The channel check requires **N+1 SecureChannels for N configured Sessions**
([OPC 10000-4, 5.7.2.1](https://reference.opcfoundation.org/specs/OPC-10000-4/v1.05.07/5.7.2)).
It does not add channels or subtract reservations from advertised Session
capacity. Reported channel headroom is arithmetic, not protected connection
capacity.

```csharp
using Opc.Ua.Bindings;
using Opc.Ua.Server;

var budget = new ChunkReassemblyBudget(64L * 1024 * 1024, 32L * 1024 * 1024);
var options = new ServerResourceIsolationOptions
{
    Mode = ServerResourceIsolationMode.Balanced
    // BootstrapReservedBytes and ReconnectReservedBytes are independent overrides.
};

// Illustrative deployment-proven bounds, NOT automatically derived reference defaults.
ServerResourceIsolationPlan plan = options.CreatePlan(
    budget,
    maxSessionCount: 75,
    maxChannelCount: 1000,
    maxRetainedChunkCount: 65,
    maxPooledBufferLength: 65536);
```

This example keeps the reference budget **64 MiB** and sessionless threshold
**32 MiB**, with 75 Sessions and 1000 SecureChannels unchanged. For these
explicit illustrative buffer bounds, `F = 4,259,840` bytes: each floor is
4,259,840 bytes, reserved capacity totals 8,519,680 bytes, and unreserved
capacity is 58,589,184 bytes. No automatic reference-server enforcement plan
is claimed: actual negotiated/pool bounds must be established before wiring it.
Later mutation of the options does not change an existing plan.

## Not implemented and limits of future guarantees

This slice does not size or enforce physical connection, pending handshake,
cryptographic work, session-establishment, request-worker, queue, or
unfinished-message-count reservations. It does not implement classifiers,
per-owner accounting, leases, hard owner ceilings, weighted shares, schedulers,
transport adapters, telemetry, or server startup selection. It adds no
subscription or PubSub quotas.

Future strict floors at **every** protected stage must be non-borrowable;
weighted unreserved shares must remain work-conserving and distinct from
configurable hard ceilings. Already admitted leases must never be revoked.
Reassembly stays **fail-fast**; asynchronous fair waiting belongs only to
decoded-request scheduling.

Trusted classification requires validated application identity, explicit
provisioning, and the versioned live `SessionBindingContext`, followed by normal
execution-time validation. An IP address, recent IP history, a claimed
ApplicationUri/token, or an activated anonymous Session is not proof of trust.
Pre-authentication protection requires explicitly trusted ingress.

Even eventual enforcement is server/node-local, not a total-process-memory,
network-DDoS, fleet-wide, or hard-real-time guarantee. Unknown/anonymous
clients remain best effort under distributed load; upstream network/TLS
protection and cooperative application handlers are still required.
