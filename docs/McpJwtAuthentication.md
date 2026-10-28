# JWT user authentication through MCP

Use `ConnectJwt` to open an OPC UA session with a JSON Web Token (JWT) acquired
outside the MCP server. The target OPC UA server validates the token and assigns
the user's permissions. This tool does not sign in to an identity provider,
refresh a token, or authenticate an HTTP request to the MCP server.

## Server and certificate prerequisites

The OPC UA endpoint must advertise an `IssuedToken` user-token policy with the
`http://opcfoundation.org/UA/UserToken#JWT` profile. Call `GetEndpoints` and inspect
`userIdentityTokens`: `issuedTokenType` identifies the profile and
`issuerEndpointUrl` describes the server's issuer requirements. Discovery
metadata does not establish trust in an issuer or authorize token disclosure.

Obtain a token intended for that server through the identity provider's approved
flow. The server remains responsible for checking its signature, issuer, audience,
expiration, and permissions. The MCP server does not infer a trusted identity or
a local token lifetime from unverified claims.

Trust the server's application certificate through the existing
[certificate stores](CertificateManager.md) before connecting. `ConnectJwt` has no
`autoAcceptCerts` argument. Its connection uses an isolated certificate manager
with automatic acceptance disabled, even when another connection or the shared
client configuration permits unknown certificates.

## Calling the tool

Supply the raw compact token without an HTTP `Bearer ` prefix:

```json
{
  "endpointUrl": "opc.tcp://server.example:4840",
  "jwt": "<pre-acquired-jwt>",
  "securityMode": "SignAndEncrypt",
  "securityPolicy": "Basic256Sha256",
  "name": "plant"
}
```

Only `endpointUrl` and `jwt` are required. Omitting the security selectors chooses
the strongest compatible endpoint offering a usable JWT policy, not an anonymous
endpoint with a higher advertised security level. A missing or incompatible JWT
policy fails rather than falling back to another authentication type.

| Argument | Meaning |
| --- | --- |
| `endpointUrl` | OPC UA discovery/server URL. |
| `jwt` | Raw token acquired externally; treat it as a bearer secret. |
| `securityMode` | Optional `None`, `Sign`, or `SignAndEncrypt` filter. |
| `securityPolicy` | Optional channel-policy name, such as `Basic256Sha256`. |
| `name` | Optional session name; otherwise derived from the endpoint host and port. |

A successful call returns the session name, session ID, and security settings.
Use that session name for subsequent tools and disconnect it when finished.
Existing `Connect` and `ConnectCertificate` arguments are unchanged.

## Confidentiality and token lifetime

The token must be encrypted in transit. A `SignAndEncrypt` channel provides
confidentiality even when the user-token policy specifies `None`. On a `Sign` or
`None` channel, the user-token policy must independently encrypt the token using
a supported policy. An empty user-token policy URI inherits the channel policy;
it does not establish encryption on a `None` channel.

The existing client policy selector still enforces supported algorithms and
channel/token compatibility. For example, a `None` channel cannot negotiate an
ephemeral-key token policy requiring a secure-channel handshake. Server
certificate trust remains required even when only the token is encrypted.
Prefer `SignAndEncrypt` when application data must also remain confidential.

The shared OPC UA client checks issued-token confidentiality before activation,
reactivation, and identity update. Previously tolerated issued-token connections
with neither channel nor token encryption are rejected; configure an encrypted
channel or an encrypted token policy. Reconnecting does not authorize a policy
downgrade or a switch from the selected issued-token profile.

The session may retain the supplied token for reactivation. There is no automatic
acquisition or refresh, and the tool does not keep a token in startup configuration
or a persistent credential alias. When a new token is needed, call `ConnectJwt`
again with that token and the same session name. Existing ownership checks apply:
another caller cannot replace or borrow the session. Replacing a connection does
not transfer its existing subscriptions to the new session.

## Errors and secret handling

JWT connection failures use the established JSON error shape:

```json
{
  "error": true,
  "statusCode": "BadIdentityTokenRejected",
  "message": "JWT connection failed. Check the server's JWT policy, token validity and certificate trust."
}
```

Blank tokens and values with whitespace are rejected. Token length is bounded by
the client's existing string, byte-string, and message quotas. Invalid token
input is rejected before an existing same-name session is removed. JWT error
responses do not copy exception messages, inner exceptions, or the token.
Cancellation still propagates instead of becoming a connection-error response.

Rejected credentials and certificate-validation failures stop the current
connection retry cycle, so an initial connection returns the server's status
instead of repeatedly submitting the same token until cancellation. Transient
transport failures retain the normal reconnect backoff. An embedding host can
inject `IReconnectPolicy` to configure that backoff; JWT authentication and
certificate errors still stop the cycle.

An MCP client can retain tool arguments in conversation history, traces, or
audit storage. Restrict access to those records and to the client itself.
Server-side output redaction cannot erase those copies or reliably clear an
immutable .NET string. Do not paste production tokens into shared conversations.
Protect remote MCP connections independently with authenticated HTTPS; OPC UA
token encryption protects the MCP-to-OPC-UA hop, not the client-to-MCP hop.

## Embedding

Register the existing core services and connection tools as described in the
[Core package reference](../tools/Opc.Ua.Mcp.Core/NugetREADME.md). The connection
catalogue includes `ConnectJwt`; no separate provider registration is required.
Embedding code can also use its injected `OpcUaSessionManager` directly:

```csharp
string result = await sessionManager.ConnectJwtAsync(
    name: "plant",
    endpointUrl: "opc.tcp://server.example:4840",
    jwt: externallyAcquiredToken,
    securityMode: "SignAndEncrypt",
    securityPolicy: "Basic256Sha256",
    ct: cancellationToken);
```

The manager API throws on failure; the MCP tool translates failures to the JSON
contract above. Both paths use the same caller ownership, certificate trust, and
issued-token policy checks.

Protocol definitions:
[OPC 10000-4, UserTokenPolicy](https://reference.opcfoundation.org/specs/OPC-10000-4/v1.05.07/7.41).
