# Generic recycling simulation

The [RecyclingServer sample](RecyclingServer/README.md) separates one
causal, durable simulation coordinator from ten independently hosted OPC UA process views.
It is a synthetic software simulation, not a controller for physical equipment.

The application supplies its own typed coordination
model, sample DI process types, Machinery state AddIns, native Scales/Pumps instruments,
Machine Vision systems, PackML state information, native granulate-drying/extrusion/filter/
pelletizer equipment, and AMB discovery. Sample process context remains distinct from the
actual companion instances; mechanical dewatering is not relabeled as a desiccant dryer.
The application does not claim formal companion conformance or reproduce an unavailable
original producer contract. `ProcessView.CompanionBindingsComplete` identifies the implemented
selected read-only equipment profile, not original-schema parity or optional remote controls.

## Architecture

One coordinator owns the logical clock, material inventories, contribution-based genealogy,
utility accounting, quality holds, accepted commands, and persistent production state.
Process views receive committed snapshots; they do not start independent clocks or create
new material when communication fails.

The ten selections are collection, sorting, shredding, washing, density separation, drying,
extrusion, melt filtration, pelletizing, and bagging. Detailed equipment substeps remain
separate in the model. Buffers, residence times, reject streams, water removal, package
residuals, and source contributions prevent an independent-random-tag interpretation of
the flow.

Source zones, configurable synthetic pH/RGB responses, and mechanical fault windows are
coordinator-owned state. Repeated projections do not resample those signals or increment
jam/strand-break counters. These are labeled synthetic operating models, not recovered
measurements or a calibrated physical color/chemistry model.

The sample keeps equipment execution, data availability, and material disposition separate.
A running machine can produce held material, and a `Good` OPC UA DataValue does not imply
acceptable product. Restoring equipment health does not release a held batch; disposition
requires an explicit authorized operation.

## Typed interfaces and composition

The sample-owned namespace is `urn:opcfoundation.org:sample:recycling`.
Generated structures and clients expose snapshots, commands/results, and bounded genealogy
queries. Coordination is OPC UA, not an opaque JSON payload transported in a UA string.

`RecyclingHost.Build` accepts normal dependency-injection configuration and additional server
configuration. `IRecyclingProcessViewAdapter` is the seam for native companion instances
under the existing owner. It applies absolute committed state rather than incrementing a
second set of production counters.

Reuse the existing [Machinery](../../docs/Machinery.md), [Machine Vision](../../docs/Vision.md),
[Pumps](../../docs/Pumps.md), [Scales](../../docs/Scales.md), and [AMB](../../docs/AMB.md) facilities when adding bindings.
The additional standard families are described in [Plastics and rubber](../../docs/PlasticsRubber.md),
[Extrusion](../../docs/Extrusion.md), and
[Granulate drying](../../docs/PlasticsRubber.md#granulate-drying-devices-opc-40082-6).
Pumps and Scales can now compose into the same Machinery owner using
`AddPumpsToMachinery` / `AddScalesToMachinery` and their `Configure*For` callbacks.
Do not register competing DI owners or copy those runtimes into the sample.

An optional end-user command forwarder must preserve the authenticated user identity.
Process service credentials are not operator credentials. The unconfigured forwarder
refuses calls rather than elevating the process identity. Simulator administration and
batch disposition are coordinator operations.

## Identity, recovery, and operation

Each role needs independently provisioned application and X.509 user identities, explicit
certificate trust, and an external fingerprint-to-role mapping. Unknown peers are not
automatically accepted. A fresh run starts after all ten authenticated process roles
register and initialize.

Every role must use the same effective simulation configuration and embedded sample model.
Views reject a missing or mismatched coordinator configuration/model identity before membership
commands or projection. Pass the same scenario to all ten views as well as the coordinator,
and make external scenario files readable in every role. A mismatch reports `BadConfigurationError`;
correct the view's inputs and restart it without resetting retained coordinator state.

The coordinator journals commits and command outcomes before acknowledging them. Control-command
outcomes have a fixed durable capacity; lease renewals retain only the latest durable heartbeat
outcome per process and cannot consume that control budget. Older heartbeat retries are rejected
by their stale revision once superseded, without renewing a lease twice. Recovery
validates record integrity, configuration/model identity, and the retained sequence; it does
not silently replace corrupt state with a new run. Retries preserve request identity and do
not repeat committed material transfers. A paused recovery stays paused.

The store is single-writer and bounded. It provides process-crash recovery, not consensus,
unlimited retention, or a power-loss guarantee for every filesystem. See the sample README
for the provider contract, bounds, explicit failure behavior, and synthetic defaults.

## Running

The application targets .NET 10. Its command line selects `--role coordinator` or one
`--process` value. The [sample README](RecyclingServer/README.md)
describes certificate provisioning, arguments, typed client use, and implementation limits.

[Deployment examples](RecyclingServer/deploy/README.md) cover one
image, Docker Compose, and generic Kubernetes manifests with isolated role storage.
They require external identities and trusted certificates; they are not pre-provisioned
production deployments.

The sample does not include an OPC UA-to-WoT connector, MQTT or ontology exporter, or
cloud-specific ingestion integration. A downstream consumer must inspect actual type,
instance, status, and unit metadata rather than infer completeness from an endpoint name.
