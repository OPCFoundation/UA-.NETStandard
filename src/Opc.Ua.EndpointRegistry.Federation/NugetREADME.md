# OPC UA Endpoint Registry Federation

Opt-in, explicit OPC UA and HTTPS providers for Endpoint Registry metadata observations.
Trust comes from server configuration, not client-authored origin fields. Preloading validates
the observed application, registry root, target ancestry, ownership and epoch before publishing
immutable evidence into the resolution cache. Resolution itself never opens a Session or fetches
an HTTP resource.

`PreloadGroupAsync` observes complete Endpoint or Message Group metadata as a named native
record. It returns the verified collection-qualified Xid and, for OPC UA observations, the
actual browsed target NodeId. It does not guess instance NodeIds from names.

HTTPS callers must disable automatic redirects on their supplied `HttpClient`. Only configured
HTTPS metadata and independent observation routes on the authorized authority are accepted.
Redirects, identity mismatch, incomplete observations, cancellation and changed epochs fail
explicitly without replacing valid cached evidence.
