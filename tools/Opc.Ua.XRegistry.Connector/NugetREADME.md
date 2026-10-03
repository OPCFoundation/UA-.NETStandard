# opcua-xregistry

Experimental .NET 10 command-line host for an xRegistry OPC UA / HTTP bridge.

```text
opcua-xregistry http-gateway --help
opcua-xregistry opcua-gateway --help
opcua-xregistry sync --help
opcua-xregistry inspect --help
opcua-xregistry conflicts --help
opcua-xregistry resolve --help
opcua-xregistry state-status --help
opcua-xregistry state-backup --help
opcua-xregistry state-restore --help
opcua-xregistry state-compact --help
```

Choose one mode and registry pair per process. Gateway writes complete upstream;
synchronization uses persistent state and concurrency guards. Manual conflict
handling and guarded deletion propagation are the defaults. No unguarded-delete
or plaintext-password/token arguments are provided.

Provision certificates through the stack's stores and resolve credentials through
named secret/identity profiles. HTTP endpoints require HTTPS, with an explicit
uncredentialed loopback-only development exception.
Optional native username and JWT/JWKS profiles use existing authenticators;
passwords are secret-store references, and allowlists remain independent.
The shared runner maintains full repair, readiness and lag in all gateway/sync
modes. Native file buffers spill under explicit memory and disk quotas.
Shutdown defers owned-resource disposal when work continues after cancellation,
instead of closing sessions or stores still used by a timed-out operation.
Offline state commands reuse validated durable storage. Backup/restore require
pristine private destination directories; compaction requires an exact observed
generation and explicit terminal operation acknowledgments. Pending evidence is
never discarded, and no maintenance command contacts the registries.

`NativeGateway:AttributeMappings` configures namespace-URI/browse-path profiles
for native discovery and projection. Typed Properties and canonical String
Properties use the declared logical model; `BasePropertyNamespaceUris` supports
explicit inherited-property layouts, and `MaxMappedProperties` bounds projection.
Registered structure activators are supplied by an embedding host through code/DI,
not reflection-based type names in JSON.

`Profiles:<name>:Http:ShortLinkPrefix` enables only the explicitly qualified
immutable, never-reused upstream alias profile. HTTP writes keep the original
alias address and are not redirected. An authoritative transactional provider
must initialize its own opt-in persistent catalog before listeners start;
connector inspection and offline sync-state commands never migrate that store.
Invalid mapping and HTTP profiles fail during configuration, before hosting.

This tool targets xRegistry 1.0-rc4 and experimental OPC UA bindings. Capability
negotiation does not turn sequential native calls into an HTTP transaction.
See `docs/XRegistryBridge.md` in the source repository before deploying.
