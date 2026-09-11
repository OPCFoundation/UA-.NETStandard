# opcua-xregistry

Experimental .NET 10 command-line host for an xRegistry OPC UA / HTTP bridge.

```text
opcua-xregistry http-gateway --help
opcua-xregistry opcua-gateway --help
opcua-xregistry sync --help
opcua-xregistry inspect --help
opcua-xregistry conflicts --help
opcua-xregistry resolve --help
```

Choose one mode and registry pair per process. Gateway writes complete upstream;
synchronization uses persistent state and concurrency guards. Manual conflict
handling and guarded deletion propagation are the defaults. No unguarded-delete
or plaintext-password/token arguments are provided.

Provision certificates through the stack's stores and resolve credentials through
named secret/identity profiles. HTTP endpoints require HTTPS, with an explicit
uncredentialed loopback-only development exception.

This tool targets xRegistry 1.0-rc4 and experimental OPC UA bindings. Capability
negotiation does not turn sequential native calls into an HTTP transaction.
See `docs/XRegistryBridge.md` in the source repository before deploying.
