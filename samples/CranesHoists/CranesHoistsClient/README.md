# CranesHoistsClient

A read-only-by-default typed client using `AddCranesHoistsClient`, an injected
`CranesHoistsClientFactory`, a managed session and generated axis proxies.
See the [paired walkthrough](../README.md).

```powershell
dotnet run --project samples\CranesHoists\CranesHoistsClient --framework net10.0 -- --help
```

| Flag | Default | Meaning |
| --- | --- | --- |
| `--endpoint <URL>` | `opc.tcp://localhost:62557/CranesHoistsServer` | Discovery endpoint. |
| `--system <browse-name>` | All | Exact system name; mandatory for a configuration request. |
| `--axis <browse-name>` | `Lift` | Exact axis name for generated-proxy access / explicit requests. |
| `--seconds <0..20>` | `5` | Each observation window; `0` skips observations. |
| `--count <1..100>` | `5` | Each observation's notification cap. |
| `--events` | Off | Also observe standard events on `Server` (`i=2253`), across all sources. |
| `--pki <directory>` | `artifacts\craneshoists-pki\client` | Private certificate-store root. |
| `--demo-trust` | Off | Accept unknown certificates; permitted only for a loopback endpoint. |
| `--request-limit <0..100>` | Off | Explicit plus-direction setpoint request, not motion. |
| `--prove-refusal` | Off | Second explicit request to a fresh sample's bounded provider. |
| `--help`, `-h` | Off | Print help and exit without creating a connection or certificates. |

The endpoint must provide Basic256Sha256/SignAndEncrypt. Normal certificate
validation, including domain checking, is retained. `--demo-trust` is only a
local-test trust exception, never a general accept-all-validation callback.

[`CraneReader`](CraneReader.cs) uses subtype-aware, continuation-safe discovery
with a page size of one, reads system/element snapshots, and prints native type
identities, selected signal access, raw values, original statuses/timestamps,
nameplates, all published axis-function metadata and directed references.
Optional absence is distinct from a present bad/uncertain value.

Data observation covers up to four discovered motor-temperature nodes in the
first selected system. Events, when requested, are observed on the Server
notifier; inspect the original event fields to distinguish sources. Each
operation has the requested duration/count caps and a 128 KiB decoded-payload
budget. Inventory uses the typed client's default 256-object / 64-page bounds.
Overflow and service failures are errors, not truncated success.

`--request-limit` requires an unambiguous, explicitly named system and axis.
The client prints requested and active values before and after
`ConfigureLimitAsync`. `--prove-refusal` additionally requires `--demo-trust`
and a fresh server started with `--advanced --allow-limit-requests --demo-trust`.
It expects that provider's second-request `BadResourceUnavailable`, rereads the
state and verifies the rejected request did not overwrite the first request
or the independently reported active limit. This check intentionally fails
against a different provider policy. No write is implicit in discovery,
reading or observation.

There is a 90-second overall work deadline; cancellation/error also runs
cleanup. Observations dispose only their own subscriptions, and
`OBSERVATION_CLEANUP` prints the before/after count while the session is still
connected. The application then closes/disposes its owned managed session and
asynchronously disposes its DI host. `CLIENT_COMPLETE` means cleanup completed,
not merely that a connection was attempted. Uncertain write outcomes require a
new read; never blindly repeat a canceled/disconnected request.
