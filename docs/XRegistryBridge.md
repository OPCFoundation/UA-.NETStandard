# xRegistry OPC UA / HTTP bridge

The xRegistry connector hosts a protocol gateway or reconciles two independently
writable registries. Gateways are **write-through**: a successful response means
the authoritative backend completed the operation. Synchronization is a separate,
explicitly selected mode; it is not used to acknowledge gateway writes early.

This is experimental support for **xRegistry 1.0-rc4** and the OPC UA working
drafts, not a claim of certification or final-standard conformance.

## Packages and construction

| Package | Responsibility |
| --- | --- |
| `Opc.Ua.XRegistry` | `Protocol.IXRegistryEndpoint`, lossless addressing, caller contexts and bounded envelopes |
| `Opc.Ua.XRegistry.Http` | HTTP binding client and modern .NET endpoint hosting |
| `Opc.Ua.XRegistry.Server` | Optional atomic generation provider and storage interface |
| `Opc.Ua.XRegistry.Bridge` | Native transport, experimental transaction extension, projection and reconciliation |
| `Opc.Ua.XRegistry.Connector` | Thin .NET 10 command-line host, installed as `opcua-xregistry` |

Library packages retain the stack's target frameworks. ASP.NET hosting requires
modern .NET; the connector requires .NET 10. Direct constructors and dependency
injection use the same implementation. Native models are source-generated;
registry JSON is not OPC UA Part 6 JSON.

## Choose a mode

Build the tool from the repository:

```powershell
$env:CustomTestTarget = "net10.0"
dotnet build tools\Opc.Ua.XRegistry.Connector -c Release
dotnet run --project tools\Opc.Ua.XRegistry.Connector -c Release --no-build -- --help
```

For a native executable use `dotnet publish tools\Opc.Ua.XRegistry.Connector
-c Release -r win-x64 -p:XRegistryPublishAot=true`. The tool-scoped property avoids
applying `PublishAot` to the .NET Standard source-generator build dependencies.

Replace the example root NodeId with the actual registry instance advertised by
the server. Namespace-URI form avoids depending on a server's current namespace
indexes. Provision OPC UA certificate trust before connecting.

```powershell
# HTTP clients access the authoritative OPC UA registry.
opcua-xregistry http-gateway `
  --opcua opc.tcp://localhost:4840 `
  --registry-node "nsu=http://opcfoundation.org/UA/xRegistry/;s=Registry" `
  --listen https://localhost:8443 --public-root https://localhost:8443/registry `
  --config connector.json --profile production

# OPC UA clients access the authoritative HTTP registry.
opcua-xregistry opcua-gateway `
  --http-root https://registry.example/catalog `
  --listen opc.tcp://localhost:4841/xregistry `
  --config connector.json --profile production

# Reconcile two independently writable registries.
opcua-xregistry sync `
  --opcua opc.tcp://localhost:4840 `
  --registry-node "nsu=http://opcfoundation.org/UA/xRegistry/;s=Registry" `
  --http-root https://registry.example/catalog `
  --state D:\RegistryState --job production `
  --conflict-policy manual --deletes on `
  --config connector.json --profile production
```

`--once` runs one reconciliation pass. `--dry-run` computes a plan without
applying registry changes. `--deletes off` disables background deletion
propagation, but does not disable explicit authorized gateway DELETE requests.
There is deliberately no unguarded-delete switch.

Each job uses a subdirectory below `--state`. Offline `conflicts --state ... --job ...`
lists active conflicts; `resolve --state ... --job ... --conflict ID
--resolution prefer-opcua|prefer-http` records a decision for revalidation on the
next synchronization pass. It does not force an immediate remote write.

`inspect` reports the effective model and backend guarantees without changing
registry entities. `--model` supplies an explicit model for native deployments
that do not expose a Model document; it does not grant missing backend guarantees.

The default projected bridge root is
`nsu=urn:opcfoundation.org:xregistry:bridge;s=XRegistryBridge`.

## Atomicity and capability negotiation

The base native binding and HTTP binding are not semantically interchangeable:

| Operation | HTTP requirement | Base native limitation |
| --- | --- | --- |
| Conditional mutation | Missing/null epoch skips checking; **zero is a real guard** | `ExpectedEpoch=0` means unconditional |
| Epoch width | Unsigned integer; never silently narrow | Companion properties commonly use UInt32 |
| Identical update | Every successful update advances epoch | Clean/identical FileType Close does not touch |
| Nested update | Any error rejects the entire request | Several Calls or Writes are not a transaction |

The optional native transaction extension preserves the complete request,
preconditions, response parameters and document bytes. It is in a **separate
experimental namespace**, not a modification to the companion specification.
Staged requests do not mutate registry entities. Publication uses a qualified
atomic provider, and replay/outcome support is advertised only when backed by
appropriate storage.

HTTP mutations additionally require real **prepare/commit** support. The gateway
first obtains an immutable response preview, fully encodes its actual HTTP body
and headers, and only then commits the prepared operation. Disposal before commit
aborts it. Any intervening registry mutation invalidates the candidate instead of
silently rebasing it. Checking only a transport envelope's serialization would not
protect against HTTP header, URL or response-body errors.

`IXRegistryPreparedEndpoint` and `IXRegistryPreparedOperation` expose this optional
seam without exposing a lock. The native extension carries prepared operations
through bounded, session-owned leases. A plain HTTP backend does not acquire
remote preparation merely by being wrapped, and unsupported gateway writes fail
before mutation.

An unextended server receives a limited profile. Requests requiring guarantees it
cannot provide are rejected **before mutation**. Read/check/write sequences,
compensating writes and a fabricated non-atomic capability are not substitutes
for HTTP atomicity. A protocol version label alone is not proof of runtime
capabilities or hierarchy.

OPC UA logical Resources remain distinct from exact Versions. Resource deletion
uses **MetaEpoch**, while Version deletion uses that Version's epoch. Existing
native clean-Close semantics are retained rather than changed to HTTP touch
semantics.

### Optional transactional provider

Applications upgrading a native server can inject their own `IXRegistryEndpoint`
or use `XRegistryTransactionalEndpoint`. All writable native surfaces in that
deployment must use the same provider.

```csharp
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

services.AddSingleton<IXRegistryTransactionStore>(
    _ => new FileXRegistryTransactionStore(registryStateDirectory));
services.AddXRegistryTransactions(new XRegistryTransactionalOptions
{
    RegistryId = "plant-registry",
    Model = modelDocument.RootElement,
    PublicRoot = new Uri("https://registry.example/catalog")
});
```

The built-in provider atomically publishes metadata, document bytes and operation
outcomes in a generation. It prepares the response before publication. The
process-local store is suitable for tests and transient hosting, but does not
advertise durable replay. File storage claims one local writer and uses staged
writes, file flushes, directory durability barriers and atomic replacement.
It is not an active/active or eventually-consistent shared-storage protocol.
An initialization marker prevents a missing previously committed data file from
being interpreted as a fresh empty registry. Restore data and its marker together;
do not remove recovery artifacts to bypass a storage failure.
`InitialMetadata` supplies required root attributes that have no model default.
Root defaults are present before the first write; persisted roots take precedence
over initialization options. Prepared candidates have separate count and aggregate
byte quotas, released on commit or abort.

The built-in provider qualifies a bounded model profile. It supports multiple
group/resource collections, typed attributes and nested objects, defaults,
required/read-only fields, resource Meta, exact document bytes, manual/createdat/
modifiedat version ordering, sticky defaults and retention. Optional query
capabilities are reported explicitly. Unknown/unsupported flags are not a promise
that their behavior is implemented.

External model imports, conditional attribute definitions, typed-reference
constraints, domain document URL resolution, cross-reference resources, semantic
version ordering, and format/compatibility validation require a qualified domain
endpoint. The built-in provider rejects unsupported models or operations rather
than silently weakening them. An HTTP-backed gateway delegates model semantics
to its authoritative HTTP registry instead of rewriting the model to fit this
local provider.

## Synchronization safety

Reconciliation compares each side with a confirmed baseline, not with the other
side's numeric epoch. Backend epochs, timestamps, links and correlation IDs are
not replicated as if they were user content. A root epoch is not a recursive
change watermark; child inventories must be read.

`--conflict-policy manual` is the default and holds conflicting entities while
allowing unrelated entities to proceed. `prefer-opcua` and `prefer-http` choose
a side, but do not bypass destination preconditions or use clock-based
last-writer-wins.

Automatic deletion requires confirmed prior synchronization, a complete inventory
showing source absence, stable registry/scope identities, and still-current
destination guards. Timeouts, authentication failures, incomplete scans, model
changes and root disappearance are not deletion signals. Subtree deletion must
not remove concurrently edited descendants.

Ordinary endpoints can safely authorize empty-group deletion using their epoch
guard. Resource/subtree and exact-Version deletion requires a qualified prepared
destination: the bridge verifies affected descendants and Resource Meta/default
state after preparation, and the global generation guard protects the final commit.
Without that guarantee those operations are held, because the ordinary HTTP
contract cannot atomically combine those guards. Similarly, model/configuration drift, external
references, server-assigned Version IDs, automatic retention/ordering and changes
to existing ancestry require explicit handling. These are reported as conflicts or
unsupported records, never silently skipped or implemented with unguarded retries.
See the [synchronization profile](../src/Opc.Ua.XRegistry.Bridge/Sync/README.md)
for the exact supported mutation matrix.

The state provider persists intent before a write and verified outcome before
advancing a baseline. After a lost response, the bridge consults outcome support
or guarded read-back; it does not blindly repeat PUT or server-assigned version
creation. Ambiguous operations remain pending. Corruption, unsupported state
formats, uncertain durability and exhausted quotas fail closed. Do not delete
state to clear a conflict: doing so discards the evidence that makes deletion
propagation safe. Tombstones have no arbitrary time-based expiry.

Native events are optional invalidation hints. HTTP polling is the common
denominator; the core binding does not specify a watch endpoint. Optional
`xregcorrelationid` is not assumed to be caller-controlled or sufficient for
echo suppression.

## Credentials and deployment

The connector uses an explicit operator credential profile by default.
Inbound authentication and authorization are independent of upstream credentials.
Embedding hosts can supply per-caller endpoint/identity resolution with isolated
sessions instead. Never forward arbitrary inbound credential headers upstream.

There are no password or access-token command-line options. Secret references
resolve through `ISecretRegistry`; the executable can map names to environment
variables with its read-only `Environment` store. Custom hosts can substitute
other secret stores and identity/token providers. Environment-variable **names**,
not secret values, belong in configuration.

For example, `connector.json` can contain:

```json
{
  "PkiRoot": "D:\\RegistryState\\pki",
  "Secrets": {
    "upstream-http": "REGISTRY_HTTP_TOKEN",
    "upstream-ua": "REGISTRY_UA_PASSWORD",
    "inbound-http": "REGISTRY_BRIDGE_TOKEN"
  },
  "Profiles": {
    "production": {
      "Http": {
        "BearerSecret": "upstream-http",
        "IsQualifiedBinding": true
      },
      "OpcUa": {
        "Identity": {
          "EnableAnonymous": false,
          "UserName": {
            "UserName": "registry-operator",
            "SecretName": "upstream-ua",
            "SecretStoreType": "Environment"
          }
        }
      }
    }
  },
  "HttpServer": {
    "BearerSecret": "inbound-http",
    "AllowAnonymousReads": false
  },
  "NativeGateway": {
    "AllowedSubjects": ["CN=AuthorizedRegistryClient"]
  }
}
```

`Http:IsQualifiedBinding` is an explicit deployment attestation, not automatic
trust in a version string. Set it only for an HTTP registry qualified to provide
the binding's atomic failure, epoch and successful-update semantics. The adapter
also checks advertised capabilities. Leave it false for inspection of an unknown
server.

The executable's native gateway advertises X.509 user authentication and
SignAndEncrypt. Provision its Users trust list and explicitly allow the identities'
display names in `NativeGateway:AllowedSubjects`. The default projected root is
`nsu=urn:opcfoundation.org:xregistry:bridge;s=XRegistryBridge`. Embedding applications
can provide other authenticators, context mappings and isolated visibility scopes
through the existing server hosting API.

HTTP gateway callers use the separately configured inbound bearer secret over
HTTPS. That secret is never forwarded to the upstream registry. Its
`/_bridge/ready` endpoint performs an authoritative inspection and reports the
availability of atomic writes. Diagnostic logs go to stderr; command status,
inspection and reconciliation records use JSON on stdout.

OPC UA connections select SignAndEncrypt and do not automatically trust unknown
certificates. Certificate lifecycle uses the stack's certificate configuration,
manager and stores. HTTP operator bearer credentials require HTTPS.
`--allow-loopback-http` is only for uncredentialed local development HTTP, not
remote plaintext deployment.

Use a private, persistent local state directory, an explicit public HTTP root,
bounded request limits, and one writer per job. Do not trust `Host` or forwarding
headers to select an upstream registry or to construct public links.

See [Identity Providers](IdentityProviders.md),
[Certificate Manager](CertificateManager.md), and
[Dependency Injection](DependencyInjection.md) for the shared infrastructure.

## Source baseline

The implementation baseline is pinned rather than floating:

* `xregistry/spec@a1544396d63b74cdf1de5da6a269d02b88696802`:
  `core/spec.md`, `core/http.md`, `core/model.md`, `core/events.md`,
  `pagination/spec.md`, and `workingdrafts/bindings/opcua.md`.
* `OPCF-Members/spec-drafts@9d3fdeb77259dedd257ee2f3f522cf7cc16f676e`:
  `source/core-specs/xregistry/spec.md` (authorized access required).

The public OPC UA working draft points to an older companion location. This
implementation selects the supplied OPCF companion for native type definitions
and treats that discrepancy explicitly. Companion model version, xRegistry
`specversion`, resource `versionid`, and the experimental envelope version are
different identifiers and must not be interchanged.
