# Opc.Ua.XRegistry.Bridge

Experimental OPC UA / HTTP xRegistry gateways and durable bidirectional
reconciliation, built on the shared xRegistry model, client, server and HTTP
libraries.

Gateways are authoritative write-through adapters. The optional generated native
transaction extension lives in a separate experimental namespace and preserves
HTTP atomicity, explicit-zero guards and update-touch semantics without weakening
the existing native FileType behavior. Unqualified base servers expose only the
operations they can faithfully support.

`XRegistryBridgeNativeOptions.AttributeMappings` supplies explicit,
model-driven mappings for typed native Properties and canonical String
Properties. Namespace-URI browse paths preserve logical attribute roles,
numeric precision, arrays and registered structure fields without turning
ordinary string labels into arbitrary domain data. Conversion and quota checks
run before projected publication. Unknown shapes, ambiguous paths and missing
required values reject explicitly; a mapping never grants write capability.
Partial leaf profiles cannot silently omit compound members. Base reads require
complete declared object coverage; open-ended maps need a whole-value profile.
Actual discriminators override defaults, and mapped label absence remains distinct
from an ordinary label read failure.
The read-only base adapter can reuse existing WoT layouts through an explicit
profile without inventing missing Registry Epoch or SpecVersion properties.

An authoritative transactional provider can enable persistent `shortself`
aliases. Native envelopes retain the original address through resolution,
authorization and guarded commit; aliases do not redirect writes. An ordinary
HTTP upstream requires the explicitly qualified stable-alias profile and still
does not acquire the native extension's transaction guarantees.

Synchronization uses durable baselines, intents, outcomes and conflicts rather
than comparing clocks or cross-server epoch numbers. Conflict policies are manual
(default), prefer OPC UA, or prefer HTTP. Automatic deletions are guarded and enabled
by default; incomplete inventories never imply deletion.

Compatible model extensions are reconciled before their dependent entities.
Prepared Resource closures protect matching attributes, defaults, ancestry and
retention; explicit and prepared server-assigned Version correspondence retains
each endpoint's real identity. Lost creation outcomes are never blindly retried.
Caller-bound endpoint leases, full-repair scheduling, native buffer spooling and
readiness/lag diagnostics are reusable library services, not only CLI features.
Acknowledged compaction and validated restore retain unresolved recovery evidence.
External document references synchronize as URIs without fetching their content.
Gateway modes cannot implicitly run a registered synchronization job. After
stopping a runner, await `WaitForPendingOperationsAsync` before releasing its
caller-owned transports; a timed-out operation retains its pass ownership.

Use the thin `Opc.Ua.XRegistry.Connector` tool or compose the library through
direct constructors and dependency injection. See `docs/XRegistryBridge.md` in
the source repository for configuration, qualification and recovery requirements.
