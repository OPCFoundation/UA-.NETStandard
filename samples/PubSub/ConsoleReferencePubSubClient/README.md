# Console Reference PubSub

A single, self-contained OPC UA **Part 14 PubSub** reference application built on the
fluent `PubSubApplicationBuilder` + dependency injection + .NET Generic Host surface.
One executable exposes three command-line-selectable **modes**, and publishes as a
NativeAOT-ready single-file executable.

## Modes

```
ConsoleReferencePubSubClient <mode> [options]
```

| Mode | Purpose |
| ---- | ------- |
| `publisher` | Publishes a built-in sample DataSet over UDP/UADP or MQTT (UADP/JSON). |
| `subscriber` | Receives DataSets and logs each decoded message to the console. |
| `external` | Bridges an **external** OPC UA server to PubSub via the `Opc.Ua.PubSub.Adapter` library (publisher / subscriber / responder direction). |

### `publisher`

```bash
ConsoleReferencePubSubClient publisher --profile udp-uadp --interval 1000
ConsoleReferencePubSubClient publisher --profile mqtt-json --endpoint mqtt://localhost:1883
```

Options: `--profile udp-uadp|mqtt-uadp|mqtt-json`, `--config-file <xml>`,
`--publisher-id`, `--writer-group-id`, `--data-set-writer-id`, `--endpoint`, `--interval`.

### `subscriber`

```bash
ConsoleReferencePubSubClient subscriber --profile udp-uadp
```

Options: `--profile`, `--config-file <xml>`, `--publisher-id-filter`,
`--writer-group-id-filter`, `--data-set-writer-id-filter`, `--endpoint`.

### `external`

Bridges an external OPC UA server (defaults to the repository's ConsoleReferenceServer
at `opc.tcp://localhost:62541/Quickstarts/ReferenceServer`; override with `--endpoint` or
the `OPCUA_EXTERNAL_ENDPOINT` environment variable).

```bash
# Read an external server and publish its values (cyclic Read each cycle)
ConsoleReferencePubSubClient external --mode publisher --read-mode cyclic

# Read via a client Subscription cache, one subscription per WriterGroup
ConsoleReferencePubSubClient external --mode publisher --read-mode subscription --affinity writergroup

# Write received PubSub values back to an external server
ConsoleReferencePubSubClient external --mode subscriber

# Map an inbound PubSub Action to an external server method call
ConsoleReferencePubSubClient external --mode responder

# Run a bidirectional bridge in one process
ConsoleReferencePubSubClient external --mode publisher,subscriber
```

Options: `--mode publisher|subscriber|responder` (comma-separated list accepted),
`--read-mode cyclic|subscription`, `--affinity writergroup|datasetwriter`,
`--endpoint <external server>`, `--pubsub-endpoint <udp url>`, `--hot-reload`,
`--security-none [true|false]`, `--allow-unsecured-actions [true|false]`,
`--validate-configuration`, `--watch-configuration`.

#### Security and explicit lab consent

The external bridge host defaults to **SignAndEncrypt**, rejects untrusted peer
certificates, and does **not** accept unsecured PubSub actions. Outbound session
security and inbound action security are independent:

| Host configuration key | Environment variable | CLI override | Default |
| --- | --- | --- | --- |
| `ExternalBridge:UseSecurityNone` | `ExternalBridge__UseSecurityNone` | `--security-none` | `false` |
| `ExternalBridge:AllowUnsecuredActions` | `ExternalBridge__AllowUnsecuredActions` | `--allow-unsecured-actions` | `false` |

An omitted flag preserves JSON/environment consent. An explicit CLI value has
highest precedence, including `--security-none=false` and
`--allow-unsecured-actions false`; it remains authoritative after JSON reload.
Environment variables override JSON and are read at process startup.
Boolean assignments must be `true` or `false`; empty/malformed values and unknown
options fail with a nonzero exit code, including when combined with help.
Help does not construct a host, write configuration, create PKI files, or emit
relaxation warnings.

The host applies this policy **after** adapter option binding on every options
snapshot. Without host consent, legacy `Connection:SecurityMode=None`,
`Connection:SecurityPolicyUri=None`, and `ExternalResponder:AllowUnsecured=true`
cannot silently weaken it. `UseSecurityNone=true` selects mode/policy None for
all selected external-server connections; it does not allow unsecured actions or
auto-accept certificates. `AllowUnsecuredActions=true` independently permits
unauthenticated actions that can invoke the mapped external server methods.
Actionable warning logs precede each enabled relaxation, including reloads.
These are sample-host choices; packable adapter APIs and defaults are unchanged.

The sample supplies its own trusted-only application configuration through the
existing client DI/configuration provider rather than the adapter's auto-accepting
convenience fallback. On a real bridge start, that provider initializes application
certificates through the certificate store system under
`pki\Opc.Ua.PubSub.Adapter` next to the executable. Provision the server certificate
or its issuer in the trusted stores and trust the bridge's application certificate
on the server. The sample does not add a blanket certificate-acceptance callback.
See [certificate management](../../../docs/CertificateManager.md).

Only in an isolated development lab, explicitly opt into the insecure demo:

```bash
# Unencrypted external connection, still no unsecured action acceptance
ConsoleReferencePubSubClient external --mode publisher --security-none

# Both independent relaxations are explicit for this unsecured responder demo
ConsoleReferencePubSubClient external --mode responder --security-none --allow-unsecured-actions
```

The earlier secure examples require provisioned trust. The supplied
`appsettings.json` explicitly keeps both host consent keys false.
Outbound OPC UA certificates do not secure PubSub datagrams. Secure action delivery
also requires appropriate PubSub message security and keys; the built-in UDP demo
is not made production-ready by selecting SignAndEncrypt for its external session.

#### Hot reload

Add `--hot-reload` to the `external` bridge to run the opt-in live reconfiguration
demo:

```bash
ConsoleReferencePubSubClient external --mode publisher,subscriber --hot-reload
```

The bridge writes `pubsub-config.xml` next to the executable before starting and
loads adapter options from the copied `appsettings.json` in the same directory.
Edit and save either file while the bridge is running:

- `appsettings.json`: change `ExternalPublisher:ReadMode` from `Cyclic` to
  `Subscription`, or change `ExternalPublisher:Affinity` to `DataSetWriter`, to
  rewire the external-server publisher options.
- `pubsub-config.xml`: add or remove a `DataSetWriter` to change the PubSub
  topology. The watching XML store raises a configuration change and the adapter
  incrementally rewires the affected binding.

Without hot reload, host consent is read from the generic host's configuration in
the working directory. With hot reload, both consent and adapter sections come
from the executable directory. The normal `--endpoint`, `--read-mode`, and
`--affinity` options select normal-mode adapter settings; the corresponding
`ExternalPublisher`, `ExternalSubscriber`, and `ExternalResponder` sections select
them in the hot-reload demo. Security CLI overrides apply in **both** paths.

For an isolated lab hot-reload demo, consent must still be explicit:

```bash
ConsoleReferencePubSubClient external --mode publisher,responder --hot-reload --security-none --allow-unsecured-actions
```

To revoke a relaxation supplied on the CLI, restart with the corresponding flag
false or omitted. Editing JSON cannot override an explicit CLI setting. When only
JSON supplies consent, changing or removing its keys reapplies secure defaults
to subsequent adapter options snapshots through the existing reload coordinator.

#### Check configuration without connecting

```bash
# One-shot effective security report: no connections, PubSub startup, PKI creation, or XML writes
ConsoleReferencePubSubClient external --mode publisher,subscriber,responder --validate-configuration

# Bind the hot-reload sections and monitor their actual options changes without starting the bridge
ConsoleReferencePubSubClient external --mode publisher,subscriber,responder --hot-reload --validate-configuration --watch-configuration
```

`--watch-configuration` requires both `--hot-reload` and
`--validate-configuration`; stop it with Ctrl-C. Reports contain security settings,
not credentials. Validation exercises host option projection, not server trust,
connectivity, the XML topology reload, or PubSub action delivery. See the
[external-server adapter guide](../../../docs/PubSub.md#binding-pubsub-to-an-external-opc-ua-server-client-session-adapters).

## Build / publish

```bash
dotnet build samples/PubSub/ConsoleReferencePubSubClient/ConsoleReferencePubSubClient.csproj
dotnet publish samples/PubSub/ConsoleReferencePubSubClient/ConsoleReferencePubSubClient.csproj -r win-x64
```

See [docs/PubSub.md](../../../docs/PubSub.md) for the full PubSub guide.

The executable-process policy tests live in `Opc.Ua.Tools.Tests`. Build this
sample first, then run the `SamplePubSubHostPolicyTests` filter for `net10.0`.
They use isolated repository-local working directories and do not start a server.
If the sample executable has not been built, the fixture reports that prerequisite
as skipped rather than claiming configuration coverage.
