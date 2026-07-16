# Identity Providers (OPC UA Part 6 §6.5)

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
     ManagedSession,                                       SessionManager
     ConsoleReferenceClient                                ReferenceServer
     custom Entra/OIDC providers                           JwtAuthenticator, X509Authenticator
     custom Windows/AspNetCore providers                   AuthorizationServiceType
```

The model **does not replace** the existing
[`IUserIdentity`](../Stack/Opc.Ua.Core/Stack/Client/IUserIdentity.cs) /
[`IUserIdentityTokenHandler`](../Stack/Opc.Ua.Core/Stack/Types/IUserIdentityTokenHandler.cs)
contracts that ship on the wire — those are still the canonical
on-the-wire types. The provider model layers on top of those types so
legacy callbacks keep working during migration, while new code can
register `IUserTokenAuthenticator` instances directly.

## Quick start — dependency injection

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
from the dependency-injection container:

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
`AddIdentityAuthenticator<T>`, `AddIdentityAugmenter<T>`,
`AddDefaultIdentityAuthenticators`, `AddJwtIssuer` — each with both
`Action<>` and `IConfiguration` overloads where applicable). A GDS host
configures identity the same way as a regular server, just on the GDS
builder:

```csharp
services.AddOpcUa()
    .AddGdsServer(opt => opt.ApplicationName = "MyGds")
    .AddDefaultIdentityAuthenticators(opt => opt.EnableJwt = false);
```

`GdsServerHostedService` consumes the forwarded authenticator and
augmenter registrations during start-up and registers them with the same
identity registry used by the regular hosted server. The GDS default
authenticator helper also registers `GdsApplicationSelfAdminProvider` so
SelfAdmin elevation works without custom host code.

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
| **Augmentation** | `IIdentityAugmenter` | Runs after an accepted authenticator and may wrap the identity with deployment-specific bindings such as GDS ApplicationSelfAdmin. | Validate a token or run after rejection. |
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
  `ActivateSession` response error. Lockout / failure counters are
  recorded through the `SessionManager` failed-authentication path.
* `AuthenticationResult.NotHandled` — the authenticator does not own
  this token type / profile. The registry moves on to the next
  authenticator and ultimately falls back to the legacy callback when no
  authenticator claimed the token.

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

### Identity augmenters

`IIdentityAugmenter` is a post-authentication hook. It runs only after
an authenticator returns `AuthenticationOutcome.Accepted`; `NotHandled`
and `Rejected` skip the chain. Augmenters may return the same identity
or wrap it with extra deployment-specific state.

```csharp
public sealed class MyTenantAugmenter : IIdentityAugmenter
{
    public ValueTask<IUserIdentity> AugmentAsync(
        IUserIdentity identity,
        AuthenticationContext context,
        CancellationToken ct = default)
    {
        // Inspect context.ChannelCertificate, context.ChannelApplicationUri,
        // claims, or deployment state, then return identity or a wrapper.
        return new ValueTask<IUserIdentity>(identity);
    }
}

services.AddOpcUa()
    .AddServer(o => o.ApplicationUri = "urn:my-server")
    .AddIdentityAugmenter<MyTenantAugmenter>();

// Manual host alternative:
server.CurrentInstance.IdentityRegistry.RegisterAugmenter(
    new MyTenantAugmenter());
```

The GDS server ships `GdsApplicationSelfAdminProvider`, an augmenter
that implements OPC 10000-12 §7.2 ApplicationSelfAdmin by matching the
secure-channel ApplicationInstance certificate to the registered
application certificate.

### Claims surface — wiring `IdentityCriteriaType.GroupId` and `Role`

`IRoleManager.ResolveGrantedRoles` probes returned identities for
`IIdentityClaims` and uses that surface to satisfy OPC 10000-18 §4.4.4
claim criteria:

* `IdentityCriteriaType.GroupId` matches entries in `IIdentityClaims.Groups`.
* `IdentityCriteriaType.Role` matches entries in `IIdentityClaims.Roles`.
  The criterion may be either `roleName` or issuer-qualified as
  `issuerUri/roleName`; the issuer-qualified form also checks
  `IIdentityClaims.Issuer`.

Authenticators should populate `IIdentityClaims` when the token contains
claims. Role assignment remains a role-manager decision; authenticators
validate credentials and return an identity.

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

    public string DisplayName => Subject;
    public string PolicyId => TokenHandler.Token.PolicyId;
    public UserTokenType TokenType => UserTokenType.IssuedToken;
    public XmlQualifiedName IssuedTokenType => IssuedTokenType.JWT;
    public bool SupportsSignatures => false;
    public ArrayOf<NodeId> GrantedRoleIds => default;
    public IUserIdentityTokenHandler TokenHandler { get; }

    public IReadOnlyDictionary<string, object> Claims { get; }
    public IReadOnlyList<string> Groups { get; }
    public IReadOnlyList<string> Roles { get; }
    public string Issuer { get; }
    public string Subject { get; }
}
```

A role rule such as `CriteriaType = IdentityCriteriaType.GroupId` and
`Criteria = "engineering-leads"` matches when `Groups` contains
`engineering-leads`. See [Role-Based Security](RoleBasedUserManagement.md)
for a worked Entra JWT example.

### `ITokenIssuer` — server-side JWT issuance

`ITokenIssuer` is the server-side counterpart of `IAccessTokenProvider`.
It backs the modern Part 12 v1.05 `AuthorizationServiceType`
`StartRequestToken` / `FinishRequestToken` flow and the legacy
`RequestAccessToken` compatibility path.

```csharp
public sealed class CertificateJwtIssuer : ITokenIssuer
{
    public string IssuerUri => "https://my-gds.example.com";
    public string ProfileUri => Profiles.JwtUserToken;

    public async ValueTask<AccessToken> IssueAsync(
        TokenIssuanceRequest request,
        CancellationToken ct = default)
    {
        byte[] jws = await SignJwsAsync(request, ct).ConfigureAwait(false);
        return new AccessToken(
            Profiles.JwtUserToken,
            jws,
            DateTime.UtcNow + request.RequestedLifetime,
            request.Subject);
    }
}
```

GDS hosts register the default issuer with `WithAuthorizationService(...)`
or a custom issuer with `WithAuthorizationService<TIssuer>(...)`; see
[AuthorizationService](AuthorizationService.md).

### `IIssuerKeyResolver` + `IssuerVerificationKey` — JWT validation

Server-side JWT validation in the GDS `JwtAuthenticator` resolves
verification keys through `IIssuerKeyResolver`. Consumers receive each
key as a non-disposable `IIssuerVerificationKey` view (the resolver
owns and disposes the concrete `IssuerVerificationKey`). The helper
deliberately uses
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

## How-to: server-side authentication

Use dependency injection when the .NET Generic Host owns the server. This example wires the
shipped defaults, a custom authenticator, and a trusted JWT issuer.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Identity;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

services.AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "MyServer";
        o.ApplicationUri = "urn:example:my-server";
        o.EndpointUrls.Add("opc.tcp://localhost:4840");
    })
    .AddIdentityAuthenticator<MyAuthenticator>()
    .AddDefaultIdentityAuthenticators(o =>
    {
        o.EnableAnonymous = true;
        o.EnableUserNamePassword = true;
        o.EnableX509 = true;
        o.EnableJwt = true;
        o.ExpectedAudience = "urn:example:my-server";
    })
    .AddJwtIssuer(o =>
    {
        o.IssuerUri = "https://issuer.example";
        o.JwksUri = "https://issuer.example/.well-known/jwks.json";
        o.Audience = "urn:example:my-server";
    });

public sealed class MyAuthenticator : IUserTokenAuthenticator
{
    public UserTokenType TokenType => UserTokenType.UserName;
    public string? IssuedTokenProfileUri => null;

    public ValueTask<AuthenticationResult> AuthenticateAsync(
        AuthenticationContext context, CancellationToken ct = default)
    {
        if (context.TokenHandler is not UserNameIdentityTokenHandler userName)
        {
            return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
        }

        bool ok = userName.UserName == "alice" &&
            !Utils.Utf8IsNullOrEmpty(userName.DecryptedPassword);
        return new ValueTask<AuthenticationResult>(ok
            ? AuthenticationResult.Accept(new UserIdentity(userName))
            : AuthenticationResult.Reject(new ServiceResult(StatusCodes.BadUserAccessDenied)));
    }
}
```

Manual hosts can register against the running server instance. Prefer dependency injection
for hosted applications, but this is useful for existing `StandardServer`
subclasses:

```csharp
server.CurrentInstance.IdentityRegistry.Register(new MyAuthenticator());
server.CurrentInstance.IdentityRegistry.Register(
    new JwtAuthenticator(keyResolver, expectedAudience: "urn:example:my-server"));
```

`GdsServerHostedService` consumes the same dependency-injection registrations, so a GDS host
uses `.AddIdentityAuthenticator<T>()`, `.AddDefaultIdentityAuthenticators()`,
and `.AddJwtIssuer(...)` exactly like a regular server.

## How-to: client-side provider selection

The shipped client providers are composable. `CompositeClientIdentityProvider`
tries providers in order and picks the first one that satisfies the server's
selected `UserTokenPolicy`.

```csharp
using Opc.Ua;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

ISecretRegistry secrets = BuildSecretRegistry();
IAccessTokenProvider accessTokens = BuildAccessTokenProvider();
ICertificateProvider certificates = configuration.CertificateManager.CertificateProvider;
ICertificatePasswordProvider passwords = new CertificatePasswordProvider();

IClientIdentityProvider provider = new CompositeClientIdentityProvider(
    new UserNamePasswordIdentityProvider(
        "alice",
        secrets,
        new SecretIdentifier("alice-password", "InMemory")),
    new X509ClientIdentityProvider(
        new CertificateIdentifier
        {
            StoreType = CertificateStoreType.Directory,
            StorePath = "pki/user",
            SubjectName = "CN=Alice"
        },
        passwords,
        certificates),
    new IssuedTokenIdentityProvider(accessTokens, Profiles.JwtUserToken));

await session.UpdateIdentityAsync(provider, ct).ConfigureAwait(false);
```

The access-token-backed shipped type is `IssuedTokenIdentityProvider`, which
wraps an `IAccessTokenProvider` and materializes a UA `IssuedIdentityToken`
for `ActivateSession`.

For managed clients, put the same provider on
`ManagedSessionOptions.IdentityProvider` so `ManagedSession` can refresh
proactively:

```csharp
var options = new ManagedSessionOptions
{
    Endpoint = endpoint,
    IdentityProvider = provider
};

ISessionFactory sessionFactory = sp.GetRequiredService<ISessionFactory>();
ManagedSession managed = await ManagedSession.CreateAsync(
    configuration, endpoint, sessionFactory, identityProvider: provider, ct: ct);
```

## How-to: migrate from `SessionManager.ImpersonateUser`

The event remains as a compatibility fallback, but it is marked
`[Obsolete]`. New code should move validation into one
`IUserTokenAuthenticator` per token type. See
[2.0 migration guide — User Identity Providers](migrate/2.0.x/identity.md#user-identity-providers)
for the full migration table.

### 1. Legacy event code

```csharp
server.CurrentInstance.SessionManager.ImpersonateUser +=
    SessionManager_ImpersonateUser;

private void SessionManager_ImpersonateUser(
    Session session, ImpersonateEventArgs args)
{
    if (args.NewIdentity is UserNameIdentityToken userName &&
        ValidatePassword(userName.UserName, userName.DecryptedPassword))
    {
        args.Identity = new UserIdentity(userName);
        return;
    }

    args.Identity = null;
}
```

### 2. Implement an authenticator for that token type

```csharp
public sealed class MyUserNameAuthenticator : IUserTokenAuthenticator
{
    public UserTokenType TokenType => UserTokenType.UserName;
    public string? IssuedTokenProfileUri => null;

    public ValueTask<AuthenticationResult> AuthenticateAsync(
        AuthenticationContext context, CancellationToken ct = default)
    {
        if (context.TokenHandler is not UserNameIdentityTokenHandler userName)
        {
            return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
        }

        return new ValueTask<AuthenticationResult>(
            ValidatePassword(userName.UserName, userName.DecryptedPassword)
                ? AuthenticationResult.Accept(new UserIdentity(userName))
                : AuthenticationResult.Reject(new ServiceResult(StatusCodes.BadUserAccessDenied)));
    }
}
```

### 3. Register via dependency injection or the server registry

```csharp
services.AddOpcUa()
    .AddServer(o => o.ApplicationUri = "urn:example:my-server")
    .AddIdentityAuthenticator<MyUserNameAuthenticator>();

// Without dependency injection:
server.CurrentInstance.IdentityRegistry.Register(new MyUserNameAuthenticator());
```

Migrate one token type at a time. If no authenticator handles a token, the
registry falls back to the obsolete event so existing deployments can stage
the change safely.

## Implementing your own provider

The shipped interfaces are intentionally small so deployments can build provider packages without changing
stack internals. The sections below show how to implement common provider integrations on top of the core
OPC UA identity abstractions plus the provider SDK you need.

### Entra ID provider

- Implement `EntraIdAccessTokenProvider : IAccessTokenProvider` by wrapping MSAL
  `IPublicClientApplication` for native/user-delegated clients or `IConfidentialClientApplication` for
  daemon services. Public clients should call `AcquireTokenSilent` first, then fall back to interactive or
  device-code flows; services should use `AcquireTokenForClient`.
- Let MSAL own token caching and refresh. Return the acquired JWT as an `AccessToken`, mapping the audience
  from `AuthorizationServerMetadata.ResourceUri`, `api://<server-app-id>`, or the target server
  `ApplicationUri`; map user-delegated scopes from `AuthorizationServerMetadata.Scopes`, and use
  `<server-app-id>/.default` for client credentials.
- Implement `EntraIdClientIdentityProvider : IClientIdentityProvider` by composing the access-token provider
  with `IssuedTokenIdentityProvider` and `Profiles.JwtUserToken`.
- On the server, validate Entra JWTs with `JwksIssuerKeyResolver` (or a provider-specific
  `EntraIdJwtIssuerKeyResolver`) against
  `https://login.microsoftonline.com/{tenant}/discovery/v2.0/keys`. Register it through `AddJwtIssuer(...)`
  or `JwtAuthenticator`, cache keys by `kid`, and refresh once on unknown keys or signature failure to handle
  tenant key rotation.

### OIDC provider

- Implement `OidcAccessTokenProvider : IAccessTokenProvider` from an issuer or authority URI. Read
  `.well-known/openid-configuration` to discover the authorization, token, and JWKS endpoints, and map the
  discovered values into `AuthorizationServerMetadata` so existing OPC UA token-policy metadata still drives
  provider selection.
- For desktop and native clients, use authorization code + PKCE: generate a high-entropy `code_verifier`, send
  `BASE64URL(SHA256(code_verifier))` as the challenge, exchange the returned code at the token endpoint, and
  return the access token as an OPC UA `AccessToken`.
- Persist refresh tokens and related state through `ISecretStore` or `ISecretRegistry` so deployments can use
  DPAPI, Keychain, Key Vault, Kubernetes secrets, or another durable store. Try the refresh-token grant before
  launching a new authorization-code flow.
- Implement `OidcClientIdentityProvider : IClientIdentityProvider` as an `IssuedTokenIdentityProvider` wrapper.
  For server validation, use `JwksIssuerKeyResolver` or an `OidcJwksKeyResolver : IIssuerKeyResolver` against
  the discovered `jwks_uri`, cache by `kid` and algorithm, honor HTTP cache headers, and refresh once before
  rejecting an unknown `kid`.

### Windows Integrated provider

- Implement `WindowsIntegratedClientIdentityProvider : IClientIdentityProvider` to run a SPNEGO/Kerberos
  exchange and emit the resulting AP-REQ or wrapped Negotiate token as an issued user token profile owned by
  the package.
- Use `System.Net.Security.NegotiateAuthentication` on `net8.0+`; use `System.Net.NegotiateStream` on
  down-level TFMs where the newer API is unavailable.
- Derive the service principal name from the target OPC UA endpoint and `ApplicationUri`, for example
  `HOST/<server-fqdn>` for host-bound deployments or `OPCUA/<server-fqdn>` where an OPC UA-specific SPN is
  registered.
- Implement `KerberosUserTokenAuthenticator : IUserTokenAuthenticator` to validate the AP-REQ or wrapped token,
  verify the ticket target matches the expected server identity, and return an `IUserIdentity`.
- Decode PAC group SIDs into `IIdentityClaims.Groups` so Part 18 `IdentityCriteriaType.GroupId` and role
  criteria can map Windows groups without changing `IRoleManager`.

### ASP.NET Core provider

- Implement `AspNetCoreAccessTokenProvider : IAccessTokenProvider` by adapting `Microsoft.Identity.Web`
  `ITokenAcquisition.GetAccessTokenForUserAsync(...)`. Resolve the current `ClaimsPrincipal` from
  `IHttpContextAccessor` or from an explicit accessor delegate supplied by the host.
- Map `AuthorizationServerMetadata.Scopes` to the requested scope list and use
  `AuthorizationServerMetadata.ResourceUri` as the additional resource or audience hint when the identity
  provider requires one.
- Use the ASP.NET Core / Microsoft.Identity.Web token cache for user-delegated tokens. If the host persists
  refresh-token references in cookies, session, or a server-side token cache, keep raw token material behind
  `ISecretStore` or `ISecretRegistry` abstractions.
- Implement `AspNetCoreClientIdentityProvider : IClientIdentityProvider` by composing
  `AspNetCoreAccessTokenProvider` with `IssuedTokenIdentityProvider`, then register both as scoped services so
  request handlers can resolve the identity provider and call `Session.UpdateIdentityAsync(...)`.
- Server-side JWT validation is the same as other OIDC providers: configure `AddJwtIssuer(...)` or
  `JwtAuthenticator` with the issuer, audience, and JWKS endpoint used by the ASP.NET Core authority.

## See also

* [2.0 migration guide — User Identity Providers](migrate/2.0.x/identity.md#user-identity-providers)
  for source migrations from obsolete identity APIs.
* [2.0 migration guide — User Identity Token Handlers](migrate/2.0.x/identity.md#user-identity-token-handlers)
  for the 1.6 token-handler async / non-disposable refactor.
* [Role-Based Security (OPC UA Part 18)](RoleBasedUserManagement.md) for
  the role-mapping layer that consumes `IIdentityClaims`.
* [AuthorizationService](AuthorizationService.md) for the OPC 10000-12 §9
  service that issues access tokens.
* [KeyCredentialService](KeyCredentialService.md) for the OPC 10000-12 §8
  service, Push model, and experimental bridge.
* [Dependency Injection](DependencyInjection.md) for the `services.AddOpcUa()`
  hosting surface.
* [Implementing your own provider](#implementing-your-own-provider) for Entra ID, generic OIDC,
  Windows Integrated, and ASP.NET Core provider guidance.
* OPC UA specification references:
  * [Part 4 §6.2 — Authorization Services](https://reference.opcfoundation.org/Core/Part4/v105/docs/6.2)
  * [Part 4 §7.40 — UserIdentityToken parameters](https://reference.opcfoundation.org/Core/Part4/v105/docs/7.40)
  * [Part 6 §6.5 — Issued User Identity Tokens](https://reference.opcfoundation.org/Core/Part6/v105/docs/6.5)
  * [Part 6 §6.5.2.2 — JWT UserTokenPolicy IssuerEndpointUrl](https://reference.opcfoundation.org/Core/Part6/v105/docs/6.5.2.2)
  * [Part 12 §8 — KeyCredentialManagement](https://reference.opcfoundation.org/GDS/v105/docs/8)
  * [Part 12 §9 — AuthorizationServices](https://reference.opcfoundation.org/GDS/v105/docs/9)
  * [Part 18 — Role-Based Security](https://reference.opcfoundation.org/Core/Part18/v105/docs/)
