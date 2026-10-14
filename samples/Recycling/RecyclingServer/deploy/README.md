# Generic recycling deployment examples

These examples run the [sample](../README.md) as one coordinator and ten process views.
They use the same synthetic configuration and typed coordination model as the console host.
They do not add a WoT connector, cloud ingestion, hardware control, or certification.
Check the sample README's current limitations before using the data.

The repository does not publish a prebuilt image for these examples. Build your own image.
All private scenarios, certificate stores, user keys, and runtime state stay outside the
repository and the container build context.

## Shared simulation configuration

All eleven roles must use the same effective simulation configuration and embedded sample
model. The checked-in Compose and Kubernetes commands omit `--scenario` for every role, so
all use `healthy`; the shared image also supplies the same compiled sample model.

To select another named scenario, add the same `--scenario <name>` to the coordinator and all
ten process-view commands/argument lists. The option is local to each executable; configuring
only the coordinator does not propagate it to the views.

For an external scenario JSON file, supply identical effective contents to every role and
add `--scenario <container-path>` to each command. Mount the file read-only into each Compose
service and each Kubernetes workload using your deployment's configuration provisioning.
The existing identity-mapping mounts do not automatically supply a scenario file. Keep scenario
files out of the image context and keep existing data mounts and persistent claims intact.

Views verify the complete configuration/model hash before membership commands or projection.
A mismatch or missing identity produces `BadConfigurationError`, leaves the endpoint available
for diagnosis, and stops automatic reconnect. Correct the view's scenario/build and restart
it; do not delete journals, reset the run, or replace retained volumes to bypass the refusal.
Matching scenario names alone is insufficient: residence, tick duration, leases, limits and
all other effective simulation settings must also match. Role-specific certificates/endpoints
and wall-clock pacing may remain independent.

## Provision first

Use the stack's [certificate manager](../../../../docs/CertificateManager.md) and
[certificate-store guidance](../../../../docs/Certificates.md). Do not enable automatic
acceptance of unknown certificates to make startup appear successful.

Provision a separate data directory for each role:

```text
external-recycling-data
  identities.json
  coordinator
    pki
    state
  collection
    pki
    users
    state
  sorting
    pki
    users
    state
  ...
  bagging
    pki
    users
    state
```

`pki` is the application's normal certificate-store root. `users` is a directory certificate
store containing only that process's user identity and private key. `state` is writable runtime
storage; only the coordinator has a production journal. Create these directories through
the appropriate storage/certificate APIs and allow the container's non-root UID/GID 1654 to
access them. Protect private keys and encrypt persistent storage and backups as appropriate.

The sample's application URI is stable:
`urn:localhost:OPCFoundation:Recycling-line-1-<role>`.
Application certificates must include the configured endpoint DNS name, such as
`recycling-sorting`, in their permitted names. The process user subject selected by these
examples is `CN=Recycling-line-1-<role>`; provision that subject in its own `users` store.
Application identity and user identity are separate certificates and trust checks.

Populate `identities.json` using the sample README's schema. Give each of the ten process
identities its own `ProcessView` binding and process selection. Add separately provisioned
observer/operator/administrator user identities as needed. Never place operator or administrator
private keys in a process-view container. The mapping contains public certificate fingerprints,
not private keys; public fingerprints alone do not authenticate a caller.

Exchange the required application public certificates into peer trust and the required user
public certificates into user trust. Preserve the stack's actual directory-store layout and
filenames; do not manufacture private-key filenames or store keys in JSON.

## Docker Compose

From the repository root:

```powershell
docker build -f samples\Recycling\RecyclingServer\Dockerfile -t recycling-server:local .
$env:RECYCLING_DATA_DIR = 'D:\recycling-data'
docker compose -f samples\Recycling\RecyclingServer\compose.yaml config --quiet
docker compose -f samples\Recycling\RecyclingServer\compose.yaml up -d
```

Use an absolute, provisioned `RECYCLING_DATA_DIR` outside the repository; the path above is an
example, not a folder the sample creates. Each container mounts only its own role directory
and the shared read-only fingerprint mapping. Its application filesystem is read-only, with a
temporary filesystem for runtime scratch work. Change `RECYCLING_IMAGE` to use a different
locally built image.

Coordinator port 62570 and view ports 62571 through 62580 are published on the host's loopback
interface only. Inside the Compose network every role listens on port 62570 and advertises
its own `recycling-<role>` DNS endpoint. An external client must be able to resolve the advertised
DNS names and trust matching certificates; publishing a port does not rewrite OPC UA discovery
URLs or certificate names. Configure deployment-appropriate endpoints rather than disabling
domain validation.

Views can start before the coordinator is usable and retry without inventing local production.
The new run starts only after all ten roles authenticate, register, and initialize. Inspect
the typed `Coordinator.State` and view availability with an authenticated OPC UA client;
an existing container or an open TCP port does not prove the material flow is running.

```powershell
docker compose -f samples\Recycling\RecyclingServer\compose.yaml logs --tail 100
docker compose -f samples\Recycling\RecyclingServer\compose.yaml down
```

Stopping containers does not delete the externally mounted state. Do not delete journals or
reset the run as a substitute for recovery.

## Kubernetes

`recycling.yaml` contains one namespace, eleven headless Services, and eleven single-replica
StatefulSets. Each role has its own persistent volume claim; the coordinator is not an HA
replica set. The workload has no Kubernetes API permissions or mounted service-account token.
Pods run without root privileges, drop capabilities, and mount a read-only application filesystem.

Choose an explicit disposable/test context. Load the locally built image into that cluster or
change the image reference to a registry you control; `recycling-server:local` is not a published
registry image.

```powershell
kubectl kustomize samples\Recycling\RecyclingServer\deploy
kubectl --context <test-context> create namespace recycling-sample
kubectl --context <test-context> -n recycling-sample create configmap recycling-identities `
    --from-file=identities.json=<external-identities-file>
```

Provision each role's claim through your cluster's approved storage/certificate tooling before
starting it. Claim names follow `data-recycling-<role>-0`; their root contains the same `pki`,
`users` (views), and `state` directories as the console/Compose layout. The manifests use the
cluster's default storage class and request 1 GiB per role. Adjust the storage class, encryption,
capacity, resources, and network policy to your environment. For staged provisioning, pre-create
and populate the claims with those exact names before enabling the roles. A StatefulSet with zero
replicas does not itself create its claims.

```powershell
kubectl --context <test-context> apply --dry-run=server `
    -k samples\Recycling\RecyclingServer\deploy
kubectl --context <test-context> apply -k samples\Recycling\RecyclingServer\deploy
kubectl --context <test-context> -n recycling-sample get pods,pvc,services
```

Do not run these commands against an implicit current context. Do not force-delete a coordinator
pod or scale it above one while its prior writer may still own the volume. `ReadWriteOnce`
alone is not a distributed writer lock, and a StatefulSet is not a consensus protocol.
The coordinator also enforces exclusive writer ownership in its store.

TCP startup/readiness/liveness probes check transport availability only. They deliberately do
not wait for all participants, which would hide the endpoints needed to satisfy that barrier.
Use the typed run state, leases, snapshot revisions, and unavailable statuses for production
readiness and recovery checks. A paused run is not a liveness failure.

## Operational checks

With provisioned test identities, verify a new run's participant barrier, material progress,
hold/disposition behavior, one view's loss/reconnection, and a coordinator restart with its
volume preserved. Confirm the run ID, committed counters, source contributions, and command
outcomes are recovered without duplicate transfers. Leave paused runs paused after recovery.

Do not interpret static manifest parsing, a listening socket, or successful process startup
as evidence of full companion conformance or source-data parity. Native/container startup and
cluster checks require the relevant runtime and an explicitly provisioned test environment.
