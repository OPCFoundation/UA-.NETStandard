# Identity Providers (OPC UA Part 6 §6.5)

> **Status**: phases P1 – PI shipped. The full identity surface (server
> `IServerIdentityRegistry` + default authenticators, client
> `IClientIdentityProvider` + `Session.UpdateIdentityAsync` with
> proactive refresh, Part 18 §4.4.4 `GroupId` / `Role` claim mapping,
> in-box `StaticIssuerKeyResolver` / `JwksIssuerKeyResolver`, and full
> `Microsoft.Extensions.DependencyInjection` integration with
> `appsettings.json` binding) is available. One known gap: the GDS
> hosted service does not yet consume identity-authenticator
> registrations deposited through the GDS forwarders — the forwarders
> exist and tests verify they produce the correct DI registrations,
> but `GdsServerHostedService` needs to read them at startup (tracked
> alongside the P5–P8 reference-server migration). Reference-server
> migration (P5), `AuthorizationServiceType` modernisation (P6),
> KeyCredential push + bridge (P7), and sibling-package design (P8)
> remain. See [Roadmap](#roadmap) below.

The OPC UA .NET Standard stack exposes a pluggable identity-provider model
that covers every user identity mechanism defined in
[OPC UA Part 6 §6.5](https://reference.opcfoundation.org/Core/Part6/v105/docs/6.5) and the
identity-provider handshakes described in
[OPC UA Part 4 §6.2](https://reference.opcfoundation.org/Core/Part4/v105/docs/6.2).

The design is intentionally **symmetric**:

```
                 ┌──────────────────────────────────────────────┐
                 │            Opc.Ua.Core / Opc.Ua.Identity     │
                 │  (shared interfaces, no behaviour change)    │
                 └──────────────────────────────────────────────┘
                              ▲                       ▲
              ┌───────────────┘                       └───────────────┐
              │                                                       │
   IClientIdentityProvider                                IUserTokenAuthenticator
   IAccessTokenProvider                                   IServerIdentityRegistry
   AuthorizationServerMetadata                            ITokenIssuer
              │                                                       │
   Used by:                                              Used by:
     ManagedSession,                                       SessionManager (P2)
     ConsoleReferenceClient (P5)                           ReferenceServer (P5)
     Opc.Ua.Identity.Entra/Oidc                            JwtAuthenticator, X509Authenticator
     (sibling packages, P8)                                AuthorizationServiceType (P6)
```

The model **does not replace** the existing
[`IUserIdentity`](../Stack/Opc.Ua.Core/Stack/Client/IUserIdentity.cs) /
[`IUserIdentityTokenHandler`](../Stack/Opc.Ua.Core/Stack/Types/IUserIdentityTokenHandler.cs)
contracts that ship on the wire — those are still the canonical
on-the-wire types. The provider model layers on top, so you can stage
adoption: keep your existing
`SessionManager.ImpersonateUser` callback running while you migrate one
token type at a time to an `IUserTokenAuthenticator`.

## Quick start — DI

For applications hosted on `Microsoft.Extensions.Hosting` (the default
in this stack — see [Dependency Injection](DependencyInjection.md)),
the identity surface is configured through fluent extensions on
`IOpcUaServerBuilder` and `IOpcUaClientBuilder`, plus matching
`appsettings.json` sections.

### Server example

```csharp
services.AddOpcUa()
    .AddServer(opt =>
    {
        opt.ApplicationName = "MyServer";
        opt.ApplicationUri  = "urn:my-server";
    })
    .AddDefaultIdentityAuthenticators(opt =>
    {
        opt.EnableAnonymous        = true;
        opt.EnableUserNamePassword = true;
        opt.EnableX509             = true;
        opt.EnableJwt              = true;
        opt.ExpectedAudience       = "urn:my-server";
    })
    .AddJwtIssuer(opt =>
    {
        opt.IssuerUri = "https://login.microsoftonline.com/{tenant}/v2.0";
        opt.JwksUri   = "https://login.microsoftonline.com/{tenant}/discovery/v2.0/keys";
    });
```

Equivalent `appsettings.json` picked up automatically by
`builder.AddServer(IConfiguration)` — that overload walks the section
and (in addition to binding `OpcUaServerOptions.Identity`) **also**
registers each `Issuers[]` entry through `AddJwtIssuer(...)`. The
`Identity:Defaults` block is honoured because `AddServer(IConfiguration)`
enables the configured-default-authenticators bridge:

```json
{
  "OpcUa": {
    "Server": {
      "ApplicationName": "MyServer",
      "ApplicationUri": "urn:my-server",
      "Identity": {
        "Defaults": {
          "EnableAnonymous": true,
          "EnableUserNamePassword": true,
          "EnableX509": true,
          "EnableJwt": true,
          "ExpectedAudience": "urn:my-server"
        },
        "Issuers": [
          {
            "IssuerUri": "https://login.microsoftonline.com/{tenant}/v2.0",
            "JwksUri":   "https://login.microsoftonline.com/{tenant}/discovery/v2.0/keys"
          }
        ]
      }
    }
  }
}
```

> The `AddServer(Action<OpcUaServerOptions>)` overload (code-only) does
> NOT wire the default authenticators or JWT issuers from
> `OpcUaServerOptions.Identity` — that bridge is enabled only on the
> `AddServer(IConfiguration)` path. With the `Action<>` overload, call
> `AddDefaultIdentityAuthenticators(opt => …)` and `AddJwtIssuer(opt => …)`
> explicitly.

### Client example

The composite builder API takes the supporting service (an
`ISecretRegistry` for passwords, an `IAccessTokenProvider` for
issued tokens) directly:

```csharp
ISecretRegistry secrets = /* register and populate elsewhere */;
IAccessTokenProvider tokens = new JwtBearerAccessTokenProvider(
    authorityUri: "https://issuer.example",
    tokenBytes:   System.Text.Encoding.UTF8.GetBytes(jwt),
    expiresAt:    DateTime.UtcNow.AddMinutes(55));

services.AddOpcUa()
    .AddClient(opt =>
    {
        opt.SessionName = "MyClient";
    })
    .AddAccessTokenProvider(tokens)
    .AddIdentityProvider(builder =>
    {
        builder.AddAnonymous();
        builder.AddUserName(
            configure: opt =>
            {
                opt.UserName        = "alice";
                opt.SecretName      = "alice-password";
                opt.SecretStoreType = "InMemory";
            },
            registry: secrets);
        builder.AddIssuedToken(
            configure: opt => opt.ProfileUri = Profiles.JwtUserToken,
            provider:  tokens);
    });
```

Or, drive the composite from configuration — the `IConfiguration`
overload resolves the `ISecretRegistry` and `IAccessTokenProvider`
from DI:

```csharp
services.AddOpcUa()
    .AddClient(configuration.GetSection("OpcUa:Client"))
    .AddAccessTokenProvider(tokens)
    .AddIdentityProvider(configuration.GetSection("OpcUa:Client:Identity"));
```

The matching `appsettings.json` (only the `AddIdentityProvider(section)`
overload picks this up — `AddClient(IConfiguration)` alone binds the
section into `OpcUaClientOptions.Identity` but does not register
providers):

```json
{
  "OpcUa": {
    "Client": {
      "SessionName": "MyClient",
      "Identity": {
        "EnableAnonymous": true,
        "UserName": {
          "UserName":        "alice",
          "SecretName":      "alice-password",
          "SecretStoreType": "InMemory"
        },
        "IssuedToken": {
          "ProfileUri":   "http://opcfoundation.org/UA/UserToken#JWT",
          "AuthorityUri": "https://issuer.example"
        }
      }
    }
  }
}
```

`IssuedToken.AuthorityUri` selects which registered
`IAccessTokenProvider` to use (matched against
`IAccessTokenProvider.AuthorityUri`). Omit it if exactly one provider
is registered.

### GDS example

`IGdsServerBuilder` forwards every identity-related extension to the
underlying server builder (`ConfigureRoles`,
`AddIdentityAuthenticator<T>`, `AddDefaultIdentityAuthenticators`,
`AddJwtIssuer` — each with both `Action<>` and `IConfiguration`
overloads). A GDS host configures identity the same way as a regular
server, just on the GDS builder:

```csharp
services.AddOpcUa()
    .AddGdsServer(opt => opt.ApplicationName = "MyGds")
    .AddDefaultIdentityAuthenticators(opt => opt.EnableJwt = false);
```

`GdsServerHostedService` consumes the forwarded authenticator
registrations during start-up and registers them with the same identity
registry used by the regular hosted server.

### Configuration reference

| Key | Type | Default |
|---|---|---|
| `OpcUa:Server:Identity:Defaults:EnableAnonymous` | `bool` | `true` |
| `OpcUa:Server:Identity:Defaults:EnableUserNamePassword` | `bool` | `true` |
| `OpcUa:Server:Identity:Defaults:EnableX509` | `bool` | `true` |
| `OpcUa:Server:Identity:Defaults:EnableJwt` | `bool` | `true` |
| `OpcUa:Server:Identity:Defaults:ExpectedAudience` | `string?` | `null` (required when `EnableJwt`) |
| `OpcUa:Server:Identity:Defaults:ClockSkewTolerance` | `TimeSpan` | `00:01:00` |
| `OpcUa:Server:Identity:Defaults:UserCertificateTrustList` | `TrustListIdentifier` | `Users` |
| `OpcUa:Server:Identity:Issuers[].IssuerUri` | `string` | — (required) |
| `OpcUa:Server:Identity:Issuers[].JwksUri` | `string?` | `null` |
| `OpcUa:Server:Identity:Issuers[].StaticKeys[].Kid` | `string?` | `null` |
| `OpcUa:Server:Identity:Issuers[].StaticKeys[].Algorithm` | `string` | `RS256` |
| `OpcUa:Server:Identity:Issuers[].StaticKeys[].RsaPublicKeyPem` | `string?` | `null` (SPKI or PKCS#1 PEM) |
| `OpcUa:Server:Identity:Issuers[].StaticKeys[].RsaModulus` / `.RsaExponent` | `string?` | base64url-encoded JWK `n` / `e` |
| `OpcUa:Server:Identity:Issuers[].StaticKeys[].EcCurve` | `string?` | `P-256` / `P-384` / `P-521` |
| `OpcUa:Server:Identity:Issuers[].StaticKeys[].EcX` / `.EcY` | `string?` | base64url-encoded JWK `x` / `y` |
| `OpcUa:Server:Identity:Issuers[].Algorithms` | `string[]` | `["RS256"]` |
| `OpcUa:Server:Identity:Issuers[].Audience` | `string?` | `null` (falls back to `Defaults.ExpectedAudience`) |
| `OpcUa:Client:Identity:EnableAnonymous` | `bool` | `true` |
| `OpcUa:Client:Identity:UserName:UserName` | `string` | — |
| `OpcUa:Client:Identity:UserName:SecretName` | `string` | — |
| `OpcUa:Client:Identity:UserName:SecretStoreType` | `string` | — |
| `OpcUa:Client:Identity:UserName:SecretStorePath` | `string?` | `null` |
| `OpcUa:Client:Identity:X509:StoreType` | `string` | — |
| `OpcUa:Client:Identity:X509:StorePath` | `string` | — |
| `OpcUa:Client:Identity:X509:SubjectName` | `string?` | `null` (exclusive with `Thumbprint`) |
| `OpcUa:Client:Identity:X509:Thumbprint` | `string?` | `null` (exclusive with `SubjectName`) |
| `OpcUa:Client:Identity:IssuedToken:ProfileUri` | `string` | `http://opcfoundation.org/UA/UserToken#JWT` |
| `OpcUa:Client:Identity:IssuedToken:AuthorityUri` | `string?` | `null` (selects the matching `IAccessTokenProvider` by `AuthorityUri`) |
| `OpcUa:Client:Identity:Order` | `string[]` | `[]` (registration order) |

#### What gets auto-wired

| Section | Path that auto-wires it | Notes |
|---|---|---|
| `OpcUa:Server:Identity:Defaults` | `AddServer(IConfiguration)` | The hosted service materialises the four default authenticators from the bound flags. |
| `OpcUa:Server:Identity:Issuers[]` | `AddServer(IConfiguration)` | Each entry is registered through `AddJwtIssuer(...)` at builder time. |
| `OpcUa:Client:Identity` | `AddClient(IConfiguration)` (fallback at session-factory resolution) | The session factory builds a composite from the bound options when **no** `IClientIdentityProvider` is registered AND at least one non-default field is set (any of `UserName`, `X509`, `IssuedToken`, `Order`, or `EnableAnonymous = false`). Pass the section to `.AddIdentityProvider(IConfiguration)` for an explicit eager registration. |
| `OpcUa:Server:Identity:*` from the `AddServer(Action<>)` path | **Not auto-wired** | Use the explicit fluent calls (`AddDefaultIdentityAuthenticators`, `AddJwtIssuer`). |

See [Dependency Injection](DependencyInjection.md) for the full
`services.AddOpcUa()` surface and how options flow through
`IConfiguration`.

## Three layers, kept separate

A common pitfall when designing identity systems is to collapse three
distinct responsibilities into one interface. This API keeps them
separate so each layer can be replaced independently:

| Layer | Interface | What it does | What it must NOT do |
|---|---|---|---|
| **Authentication** | `IUserTokenAuthenticator` | Validates a single token type / profile (password lookup, X.509 trust, JWT signature). Produces an `IUserIdentity`. | Pre-grant Part 18 role NodeIds. |
| **Claim extraction** | `IIdentityClaims` (probe interface on the returned identity) | Surfaces OIDC / JWT / X.509 claims so role mapping has data to work with. | Decide which roles get granted. |
| **Role mapping** | `IRoleManager.ResolveGrantedRoles` (already exists, see [Role-Based Security](RoleBasedUserManagement.md)) | Applies OPC UA Part 18 §4.4 identity-mapping rules to the claims and emits the granted role NodeIds. | Authenticate the token. |

If you find yourself granting roles inside an authenticator, you have
overstepped — push that logic into an `IRoleManager` identity-mapping
rule instead, then the same rule applies whether the user came in via
UserName, X509, JWT, or a future token type.

## Client side

### `IClientIdentityProvider` — what to send in `ActivateSession`

`IClientIdentityProvider` returns an `IUserIdentity` lazily, **per
activation**. Long-lived OPC UA sessions outlive short OAuth2 access
tokens; the provider model is built around that fact.

```csharp
using Opc.Ua.Identity;

public sealed class MyEntraIdentityProvider : IClientIdentityProvider
{
    private readonly IAccessTokenProvider m_tokens;
    private DateTime m_lastExpiry = DateTime.MaxValue;

    public MyEntraIdentityProvider(IAccessTokenProvider tokens)
    {
        m_tokens = tokens;
    }

    public IReadOnlyList<UserTokenType> SupportedTokenTypes
        => new[] { UserTokenType.IssuedToken };

    public IReadOnlyList<string> SupportedIssuedTokenProfileUris
        => new[] { Profiles.JwtUserToken };

    public bool CanSatisfy(UserTokenPolicy policy, IdentitySelectionContext _)
        => policy.TokenType == UserTokenType.IssuedToken
           && string.Equals(policy.IssuedTokenType, Profiles.JwtUserToken,
               StringComparison.Ordinal);

    public DateTime ExpiresAt => m_lastExpiry;

    public async ValueTask<IUserIdentity> GetIdentityAsync(
        UserTokenPolicy policy,
        IdentitySelectionContext context,
        CancellationToken ct = default)
    {
        // 1. Parse the server's IssuerEndpointUrl JSON per OPC 10000-6 §6.5.2.2.
        if (!AuthorizationServerMetadata.TryFromPolicy(policy, out var meta))
        {
            throw ServiceResultException.Create(
                StatusCodes.BadIdentityTokenRejected,
                "Policy is not a JWT IssuedToken policy.");
        }

        // 2. Acquire the access token via OAuth2 / MSAL / GDS / etc.
        using AccessToken accessToken = await m_tokens
            .AcquireAsync(meta, ct)
            .ConfigureAwait(false);
        m_lastExpiry = accessToken.ExpiresAt;

        // 3. Wrap in an IssuedIdentityToken handler.
        return new UserIdentity(
            accessToken.TokenData.ToArray(),
            accessToken.ProfileUri)
        {
            PolicyId = policy.PolicyId
        };
    }
}
```

Key design notes:

* **Token refresh is the session's responsibility, not the provider's.**
  Call `await session.UpdateIdentityAsync(provider, ct)` to reactivate
  a raw `Session` with a fresh identity. The method serialises with the
  existing reactivation lock and binds the new token to the current
  server nonce.
* **Use `ManagedSessionOptions.IdentityProvider` for managed clients.**
  `ManagedSession` calls `UpdateIdentityAsync` after connect, then
  schedules proactive refresh at `provider.ExpiresAt - 60s` using the
  configured `TimeProvider`. Refresh failures are logged and retried
  with backoff; they do not close the session.

```csharp
var options = new ManagedSessionOptions
{
    Endpoint = endpoint,
    IdentityProvider = new IssuedTokenIdentityProvider(accessTokens)
};

ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .WithIdentityProvider(options.IdentityProvider)
    .ConnectAsync(ct);
```

### `IAccessTokenProvider` — orthogonal to the OPC UA stack

`IAccessTokenProvider` is decoupled from the OPC UA session entirely.
A single provider serves any number of OPC UA endpoints whose
`UserTokenPolicy.IssuerEndpointUrl` points at the same Authorization
Service.

```csharp
public sealed class StaticJwtBearerProvider : IAccessTokenProvider
{
    private readonly byte[] m_bytes;
    public StaticJwtBearerProvider(string jwt, string authorityUri)
    {
        m_bytes = System.Text.Encoding.UTF8.GetBytes(jwt);
        AuthorityUri = authorityUri;
    }

    public string AuthorityUri { get; }

    public ValueTask<AccessToken> AcquireAsync(
        AuthorizationServerMetadata metadata,
        CancellationToken ct = default)
    {
        return new ValueTask<AccessToken>(new AccessToken(
            Profiles.JwtUserToken,
            (byte[])m_bytes.Clone(),
            DateTime.MaxValue,
            "static-jwt"));
    }
}
```

`AccessToken` is `IDisposable` and zeroes its buffer on dispose — that
is the only reliable "secure clear" path until the JWT is encrypted
into the wire token. **Always** use `using` or pass ownership to a
`UserIdentity`/handler that takes ownership.

### `AuthorizationServerMetadata` — the JSON nobody told you about

The OPC 10000-6 §6.5.2.2 `IssuerEndpointUrl` field of a JWT
`UserTokenPolicy` is, despite its name, **not a URL** — it is a JSON
object that describes the Authorization Service (authority URI,
resource URI, request types, scopes, optionally token / JWKS
endpoints).

```csharp
using Opc.Ua.Identity;

if (AuthorizationServerMetadata.TryFromPolicy(policy, out var meta))
{
    Console.WriteLine($"Authority   : {meta.AuthorityUri}");
    Console.WriteLine($"Resource    : {meta.ResourceUri}");
    Console.WriteLine($"Flows       : {string.Join(",", meta.RequestTypes)}");
    Console.WriteLine($"Scopes      : {string.Join(",", meta.Scopes)}");
    Console.WriteLine($"Token EP    : {meta.TokenEndpoint ?? "(OIDC discovery)"}");
}
```

The parser:

* Tolerates the `ua:` namespace prefix used by the spec
  (`ua:resourceUri` → `ResourceUri`).
* Accepts OIDC-style aliases (`issuer`, `token_endpoint`,
  `authorization_endpoint`, `jwks_uri`, `scopes_supported`) so you can
  feed an OIDC `.well-known/openid-configuration` payload directly.
* Accepts a bare string where the spec allows an array (some servers
  emit a single value for `requestTypes`).
* Preserves unknown fields in `AdditionalFields` for vendor-specific
  use (e.g. Entra `tenant_id`, PKCE configuration).
* Throws `BadDecodingError` on malformed JSON or non-object root.
* Returns an empty instance (no exception) for null, empty, or
  whitespace-only payloads.

## Server side

### `IUserTokenAuthenticator` — what to do with the incoming token

```csharp
using Opc.Ua.Identity;

public sealed class StaticUserNameAuthenticator : IUserTokenAuthenticator
{
    private readonly Func<string, byte[], bool> m_checkPassword;

    public StaticUserNameAuthenticator(Func<string, byte[], bool> checkPassword)
    {
        m_checkPassword = checkPassword;
    }

    public UserTokenType TokenType => UserTokenType.UserName;
    public string IssuedTokenProfileUri => null;  // n/a for UserName

    public ValueTask<AuthenticationResult> AuthenticateAsync(
        AuthenticationContext context,
        CancellationToken ct = default)
    {
        if (context.TokenHandler is not UserNameIdentityTokenHandler u)
        {
            return new ValueTask<AuthenticationResult>(
                AuthenticationResult.NotHandled);
        }
        if (string.IsNullOrEmpty(u.UserName) || u.DecryptedPassword == null)
        {
            return new ValueTask<AuthenticationResult>(
                AuthenticationResult.Reject(
                    new ServiceResult(StatusCodes.BadIdentityTokenInvalid)));
        }
        if (!m_checkPassword(u.UserName, u.DecryptedPassword))
        {
            return new ValueTask<AuthenticationResult>(
                AuthenticationResult.Reject(
                    new ServiceResult(StatusCodes.BadUserAccessDenied)));
        }

        var identity = new UserIdentity(u);  // wraps the wire handler
        return new ValueTask<AuthenticationResult>(
            AuthenticationResult.Accept(identity));
    }
}
```

The three outcomes:

* `AuthenticationResult.Accept(identity)` — token validated, identity
  returned.
* `AuthenticationResult.Reject(serviceResult)` — token decoded but
  refused. The status code flows back to the client as the
  `ActivateSession` response error. Lockout / failure counters
  integrate with the existing `SessionManager`
  `RecordFailedAuthentication` path (P2 wires this).
* `AuthenticationResult.NotHandled` — the authenticator does not own
  this token type / profile. The registry moves on to the next
  authenticator and ultimately falls back to the legacy
  `SessionManager.ImpersonateUser` event when no authenticator claimed
  the token.

### `IServerIdentityRegistry` — composing authenticators

```csharp
var registry = new ServerIdentityRegistry(
    new AnonymousAuthenticator(),
    new StaticUserNameAuthenticator(MyUserDb.CheckPassword),
    new X509Authenticator(myCertValidator),
    new JwtAuthenticator(myEntraKeyResolver, expectedAudience: applicationUri));
```

Authenticators are tried in registration order. For
`UserTokenType.IssuedToken` the registry dispatches by the
`IssuedTokenProfileUri` carried on the inbound
`IssuedIdentityTokenHandler` — register a JWT authenticator with
`IssuedTokenProfileUri = Profiles.JwtUserToken` and it only sees JWT
tokens; a SAML or Kerberos authenticator on the same channel is left
to handle the rest. Register with `IssuedTokenProfileUri = null` for a
catch-all (useful when bridging to a legacy `ITokenValidator`).

### Claims surface — wiring `IdentityCriteriaType.GroupId` and `Role`

`IRoleManager.ResolveGrantedRoles` will (P3) probe the returned
identity for `IIdentityClaims` and use it to satisfy the OPC 10000-18
§4.4.4 `GroupId` and `Role` identity criteria — neither of which works
today (`GroupId` returns `false` unconditionally, `Role` matches
already-granted role NodeIds which contradicts the spec). Authenticators
should populate the claims surface even though the role-mapping
consumer isn't wired yet; P3 will pick it up automatically.

```csharp
internal sealed class JwtUserIdentity : IUserIdentity, IIdentityClaims
{
    public JwtUserIdentity(
        IUserIdentityTokenHandler handler,
        IReadOnlyDictionary<string, object> claims,
        IReadOnlyList<string> groups,
        IReadOnlyList<string> roles,
        string issuer,
        string subject)
    {
        TokenHandler = handler;
        Claims = claims;
        Groups = groups;
        Roles = roles;
        Issuer = issuer;
        Subject = subject;
    }

    // IUserIdentity surface…
    public string DisplayName => Subject;
    public string PolicyId => TokenHandler.Token.PolicyId;
    public UserTokenType TokenType => UserTokenType.IssuedToken;
    public XmlQualifiedName IssuedTokenType => /* parsed from handler */;
    public bool SupportsSignatures => false;
    public ArrayOf<NodeId> GrantedRoleIds => default; // RoleManager fills this.
    public IUserIdentityTokenHandler TokenHandler { get; }

    // IIdentityClaims surface…
    public IReadOnlyDictionary<string, object> Claims { get; }
    public IReadOnlyList<string> Groups { get; }
    public IReadOnlyList<string> Roles { get; }
    public string Issuer { get; }
    public string Subject { get; }
}
```

When P3 ships, the same identity instance feeds an
`IdentityMappingRuleType` like:

```
CriteriaType = GroupId, Criteria = "engineering-leads"
```

— and the rule matches when `Groups` contains `engineering-leads`.

### `ITokenIssuer` — server-side JWT issuance (P6 wires this)

The `ITokenIssuer` interface is the server-side counterpart of
`IAccessTokenProvider`. P6 will use it to back the modern Part 12 v1.05
`AuthorizationServiceType.StartRequestToken` / `FinishRequestToken`
flow. You can implement it now to test against a GDS in isolation:

```csharp
public sealed class EcdsaJwtIssuer : ITokenIssuer
{
    public string IssuerUri => "https://my-gds.example.com";
    public string ProfileUri => Profiles.JwtUserToken;

    public async ValueTask<AccessToken> IssueAsync(
        TokenIssuanceRequest request,
        CancellationToken ct = default)
    {
        // 1. Build the JWS header + payload (sub, aud, exp, iss, …).
        // 2. Sign with your ECDSA / RSA / KMS key.
        // 3. Return as an AccessToken.
        byte[] jws = SignJws(request);
        return new AccessToken(
            Profiles.JwtUserToken,
            jws,
            DateTime.UtcNow + request.RequestedLifetime,
            request.Subject);
    }
}
```

### `IIssuerKeyResolver` + `IssuerVerificationKey` — JWT validation

Server-side JWT validation in P2's `JwtAuthenticator` resolves
verification keys through `IIssuerKeyResolver` and exercises them
through `IssuerVerificationKey`. The helper deliberately uses
`byte[]` overloads (no `System.IdentityModel.Tokens.Jwt`) so it works
on netstandard2.1 / net472 / net48 / net8+/net9+/net10+ and is
AOT-friendly.

Supported JWS algorithms (RFC 7518 §3.1):

| Algorithm | Key type | Notes |
|---|---|---|
| `RS256` / `RS384` / `RS512` | RSA | PKCS#1 v1.5 padding |
| `PS256` / `PS384` / `PS512` | RSA | RSA-PSS padding |
| `ES256` / `ES384` / `ES512` | ECDSA | IEEE P1363 fixed-size r‖s format |

HS256 (HMAC) is **intentionally unsupported** because the OPC UA
multi-party authorization model would require sharing a symmetric
secret with every verifier.

```csharp
using RSA rsa = LoadRsaPublicKey();
using var key = new IssuerVerificationKey("kid-1", rsa, "RS256");
bool isValid = key.VerifySignature(signingInputBytes, signatureBytes);
```

## Recipes

### "I just want anonymous + username+password to start"

That is the historical `SessionManager.ImpersonateUser` shape and it
**still works** as-is. No code change required to keep using the event
handler. The provider model is opt-in; mix-and-match is supported (P2
will register the legacy event handler as a fallback authenticator
automatically).

### "I want to validate Entra (Azure AD) JWTs"

Implement an `IIssuerKeyResolver` that fetches the tenant's signing
keys from
`https://login.microsoftonline.com/{tenant}/discovery/v2.0/keys`,
cache them, and pass them to the `JwtAuthenticator` (P2). For the
client side, implement an `IAccessTokenProvider` that wraps
`Microsoft.Identity.Client` (MSAL) and acquires tokens via
`AcquireTokenForClient` (client credentials) or
`AcquireTokenInteractive` (user flows). The
`AuthorizationServerMetadata.AuthorityUri` and `ResourceUri` give you
exactly the inputs MSAL needs.

A sibling package `Opc.Ua.Identity.Entra` is reserved for this in P8.

### "I want to use a GDS-issued KeyCredential as my OPC UA identity"

That is a vendor extension under
`urn:opcfoundation:netstandard:profile:authentication:keycredential` —
the OPC UA specification does not define a §8 KeyCredential → §6.5
IssuedIdentityToken bridge. P7 ships an experimental implementation
explicitly marked as not spec-conformant. Use it for closed
deployments where the same GDS issues both broker credentials and
OPC UA session credentials.

For standards-conformant flows, use `AuthorizationServiceType` (P6)
which issues real JWTs.

### "I'm hosting in ASP.NET Core and have an `ITokenAcquisition`"

Wrap it in an `IAccessTokenProvider`. Its `AcquireAsync` body looks
roughly like:

```csharp
public async ValueTask<AccessToken> AcquireAsync(
    AuthorizationServerMetadata meta, CancellationToken ct)
{
    var result = await m_tokenAcquisition.GetAccessTokenForUserAsync(
        scopes: meta.Scopes,
        tenantId: meta.AdditionalFields.GetValueOrDefault("tenant_id")?.GetString());
    byte[] bytes = Encoding.UTF8.GetBytes(result);
    return new AccessToken(Profiles.JwtUserToken, bytes,
        DateTime.UtcNow.AddMinutes(55), m_currentUser?.Identity?.Name ?? "");
}
```

A sibling package `Opc.Ua.Identity.AspNetCore` is reserved for this in P8.

## Roadmap

| Phase | Status | Adds |
|-------|--------|------|
| **P1** | ✅ shipped (`735dcd87`) | Interfaces + `AuthorizationServerMetadata` parser + `ServerIdentityRegistry` + `IssuerVerificationKey` helper. No behaviour change. |
| **P2** | ✅ shipped (`f097f7e2`) | `SessionManager` routes incoming tokens through the registry first, falls back to the existing `ImpersonateUser` event when no authenticator matches. Default `Anonymous`, `UserNamePassword`, `X509`, `Jwt` authenticators in `Opc.Ua.Server`. |
| **P3** | ✅ shipped (`4ecdeb53`) | `IRoleManager.ResolveGrantedRoles` probes `IIdentityClaims` to wire OPC 10000-18 §4.4.4 `GroupId` + `Role` criteria correctly (forced spec-correct migration — the previous behaviour was never released). |
| **P4** | ✅ shipped (`df56442d`) | Client side: `Session.UpdateIdentityAsync(IClientIdentityProvider, ct)` + proactive refresh scheduler + `ManagedSessionOptions.IdentityProvider` (eager `Identity` setter is `[Obsolete]`). |
| **PI** | ✅ shipped (`c8ff0d48`, `c4848919`, `fbc32fe8`, `1d5cfe7a`, `b113a5c1`) | DI integration completeness: `OpcUaServerIdentityOptions`, `OpcUaClientIdentityOptions`, `JwtIssuerOptions`, in-box `StaticIssuerKeyResolver` / `JwksIssuerKeyResolver`, `JwtBearerAccessTokenProvider`, `IConfiguration` overloads on `ConfigureRoles` / `AddDefaultIdentityAuthenticators` / `AddJwtIssuer`, GDS forwarders. Identity is now fully reachable from `appsettings.json`. |
| **P5** | pending | `ReferenceServer` and `ConsoleReferenceClient` migrate to the provider model. `SessionManager.ImpersonateUser` event is marked `[Obsolete]` (functional but discouraged). |
| **P6** | pending | Modern Part 12 v1.05 `AuthorizationServiceType.StartRequestToken` / `FinishRequestToken` flow with `ITokenIssuer` backing. `RequestAccessToken` stays available but `[Obsolete]` (deprecated in v1.05 per spec). |
| **P7** | pending | `KeyCredentialConfigurationFolderType` Push model wiring + experimental KeyCredential → IssuedIdentityToken bridge under a vendor profile URI. |
| **P8** | pending | Sibling packages design notes for `Opc.Ua.Identity.{Entra,Oidc,Windows,AspNetCore}`. |

## See also

* [Migration Guide — User Identity Token Handlers](MigrationGuide.md#user-identity-token-handlers)
  for the 1.6 token-handler async / non-disposable refactor that this
  builds on.
* [Role-Based Security (OPC UA Part 18)](RoleBasedUserManagement.md) for
  the role-mapping layer that consumes `IIdentityClaims`.
* [AuthorizationService](AuthorizationService.md) for the OPC 10000-12 §9
  service that issues access tokens (P6 will modernize this).
* [KeyCredentialService](KeyCredentialService.md) for the OPC 10000-12 §8
  service that provisions credentials for non-OPC-UA brokers (P7
  experimental bridge).
* [Sessions](Sessions.md) for the `Session` / `ManagedSession`
  reconnection and reactivation mechanics that interact with token
  refresh.
* OPC UA specification references:
  * [Part 4 §6.2 — Authorization Services](https://reference.opcfoundation.org/Core/Part4/v105/docs/6.2)
  * [Part 4 §7.40 — UserIdentityToken parameters](https://reference.opcfoundation.org/Core/Part4/v105/docs/7.40)
  * [Part 6 §6.5 — Issued User Identity Tokens](https://reference.opcfoundation.org/Core/Part6/v105/docs/6.5)
  * [Part 6 §6.5.2.2 — JWT UserTokenPolicy IssuerEndpointUrl](https://reference.opcfoundation.org/Core/Part6/v105/docs/6.5.2.2)
  * [Part 12 §8 — KeyCredentialManagement](https://reference.opcfoundation.org/GDS/v105/docs/8)
  * [Part 12 §9 — AuthorizationServices](https://reference.opcfoundation.org/GDS/v105/docs/9)
  * [Part 18 — Role-Based Security](https://reference.opcfoundation.org/Core/Part18/v105/docs/)
