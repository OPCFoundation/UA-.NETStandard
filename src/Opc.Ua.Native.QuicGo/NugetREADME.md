# Optional native QUIC backend

This package provides independently created QUIC listeners, connectors and native streams.
It does **not** implement Data Channel records, HTTP/3, Sessions or PubSub, and adds no
dependency to ordinary Core/client/server/PubSub packages. The separate HTTP/3 engine
reuses this runtime rather than loading another Go image.

The backend uses pinned quic-go **0.64.0**, Go **1.27.2** and Zig **0.17.0**, with three
hash-pinned exact-reset-09 observation overlays and a STOP_SENDING reliable-prefix policy
overlay. TLS 1.3, real RFC9221 datagrams and exact peer TP `0x1d` are mandatory.
The default policy requires supplied mutual application identities. ABI5 introduced an explicitly
selected server-authenticated transport TLS for independent UASC application authentication.
There is no transport downgrade or generated/system-trust fallback.

## Managed use

Supply DER certificates and explicit trust from the application's certificate-manager
provider. Supply an authorized URI/SPKI allowlist, not values asserted in HELLO.
`NativeQuicPrivateKey` accepts exportable unencrypted PKCS8 RSA2048..4096 or ECDSA
P256/P384/P521. Hardware/non-exportable signing is **not** supported by this interface: refuse
that capability rather than substituting another certificate.

```csharp
NativeQuicLibrary library = await NativeQuicLibrary.FromDirectoryAsync(trustedNativeDirectory);
services.AddNativeQuicGo(library); // or new NativeQuicBackend(library)

// The provider owns localCertificate, intermediates, key, roots and the peer policy.
using var signingKey = new NativeQuicPrivateKey(exportedPkcs8.Span);
var credentials = new NativeQuicCredentials(
    localCertificate, intermediates, signingKey, roots, localApplicationUri);
var policy = new NativeQuicPeer(expectedPeerUri, expectedPeerSpkiSha256);
var options = new NativeQuicOptions(
    "127.0.0.1:4843", "opcua-flow/1", credentials, [policy], expectedServerName);
NativeQuicCompletion connected = await backend.ConnectAsync(options, cancellationToken);
if (connected.Status != NativeQuicStatus.Success)
{
    throw new IOException($"Native connection failed: {connected.Status}");
}
// Use connected.Connection, allocate native streams and inspect every completion.
// Release all streams first, then the connection, using the corresponding Release operation.
```

Every admitted managed call polls nonblocking native work and drains cancellation before
releasing its operation. **Inspect the completion**, including `BytesTransferred`,
`NativeStatus`, reset fields and any returned handle, even after cancellation. A successful
race stays successful. Resource handles require explicit asynchronous release; there is
no finalizer that closes unrelated resources. Stream release cannot race a pending job.

For servers, `ListenAsync` creates a real UDP listener; the `Address` listener operation
returns its bound endpoint and `Accept` independently returns each authenticated connection.
Closing a listener does not close accepted connections. Its immutable policy supports
multiple application/key rules. New trust/revocation decisions require a fresh validated
snapshot and explicit closure of affected connections. Native X.509 verifies chain,
lifetime, usage and connector hostname; **CRL/OCSP fetching and online revocation are
not provided**, and must not be claimed by a policy provider.

For an error learned after a send starts, set
`new NativeQuicRequest(timeout, body) { Cancellation = reset }`, where `reset` is a
caller-owned `NativeQuicCancellation`. Later `await reset.CancelAsync(wireError)` signals
the existing native cancellation entry without waiting for the body lease. Await the
send separately and inspect its actual result and `WaitResetSent` evidence. The first
requested/delivered cancellation remains authoritative; completed-send races may require
ordinary Reset afterward. Dispose the control only after the operation drains.

ABI4 adds `WaitPeerStopped`, which observes an actual authenticated peer STOP_SENDING
even if local cancellation happened first. The older `WaitStopped` observes the first
send-context cause and is not sufficient for crossed shutdown. Actual STOP and reset
evidence remain distinct; no final/reliable size is invented for a STOP.

ABI6 preserves that default and adds a separate server-authenticated TLS policy
and bounded custom-label TLS exporter support:

```csharp
var trust = NativeQuicCredentials.ForTlsClient(serverTrustRoots);
var options = NativeQuicOptions.ForServerAuthenticatedTls(
    "localhost:4843", "h3", trust, "localhost");
NativeQuicCompletion connected = await backend.ConnectAsync(options, cancellationToken);
if (connected.Status != NativeQuicStatus.Success)
{
    throw new IOException($"Native TLS connection failed: {connected.Status}");
}
NativeQuicTlsEvidence evidence = await backend.GetTlsEvidenceAsync(connected.Connection, cancellationToken);
```

The client supplies no certificate, signing key or application URI. The server uses
`ForTlsServer(certificate, intermediates, key)` with the same explicit options factory;
it does not request client certificates or admit application identities. Server chain,
lifetime, usage and hostname checks remain mandatory. `GetPeerEvidenceAsync` refuses
these connections; TLS transport facts cannot authorize flows or replace UASC certificate
and key-possession authentication. This primitive alone is not a browser/WebTransport
baseline implementation. The managed loader requires ABI7; restart with rebuilt
assets. ABI7 adds graceful stream-handle release after FIN; ordinary Release
remains abortive.

The pinned QUIC overlay preserves an established reliable prefix when peer STOP_SENDING
requires a reset, and does not downgrade a reset that already owns the send direction.
Streams without a reliable boundary retain ordinary reset behavior. The overlay is pinned
to the exact upstream source hash and covered by native live tests; see `native/NATIVE_ABI.md`.

## Deployment and ownership

Only **win-x64** and **glibc linux-x64** are included. The source-adoption execution
matrix is serviced Windows/.NET and Linux glibc 2.38/2.39. The Linux compiler target is
glibc 2.28; this is **not minimum-host execution evidence**. There is no musl, ARM or
macOS claim. Windows uses OS UCRT; Linux uses system libc/pthread/resolver libraries.

Load one compatible Go C-shared image in the default assembly context for the **entire
process lifetime**. Never call `NativeLibrary.Free`, `FreeLibrary` or `dlclose` on it.
Restart to upgrade. `NativeQuicLibrary` checks absolute trusted paths, SHA256 and ABI
layouts; it never searches PATH. An initialization failure requires restart. The hash
sidecar is an integrity check, not a substitute for authenticating the distribution.

Native source, exact dependency locks, the hash-pinned QUIC overlay, build receipts and notices
are included. Native binaries and test identities are not checked into source control.
Build/package instructions and bounds are in `native/NATIVE_ABI.md`.

**Source adoption is not product-distribution approval.** Local builds are unsigned.
Release requires final-product signing, dependency/SBOM/notices and hash-bound advisory
review, plus package/NativeAOT/minimum-host acceptance. The older proof's VEX does not
apply to a new binary. See `native/THIRD-PARTY-NOTICES.txt` for native license obligations.
