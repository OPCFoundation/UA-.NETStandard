> **Design-only — no implementation in this release.** This document reserves the
> public surface for a future `Opc.Ua.Identity.Oidc` package. The reference
> implementation in this repository targets `IAccessTokenProvider` /
> `IClientIdentityProvider` / `IUserTokenAuthenticator`; this sibling-package
> design layers on top of those interfaces without modifying them.

# Generic OIDC Identity Provider Design

Reserved NuGet package: `OPCFoundation.NetStandard.Opc.Ua.Identity.Oidc`.

## Public surface

* `OidcAccessTokenProvider : IAccessTokenProvider`
* `OidcClientIdentityProvider : IClientIdentityProvider`
* `OidcJwksKeyResolver : IIssuerKeyResolver`

## Discovery

The provider starts from an issuer/authority URI and reads
`.well-known/openid-configuration` to discover authorization, token, and JWKS
endpoints. The discovered metadata maps to `AuthorizationServerMetadata` so the
existing `IssuedTokenIdentityProvider` can still materialize OPC UA
`IssuedIdentityToken` instances.

## JWKS rotation

`OidcJwksKeyResolver` caches keys by `kid` and algorithm. On signature failure
or unknown `kid`, it refreshes JWKS once before rejecting the token. Cache
lifetimes should honor HTTP cache headers where available and otherwise use a
short bounded default.

## PKCE for desktop and native clients

Native flows use authorization code + PKCE:

1. Generate a high-entropy `code_verifier`.
2. Send `code_challenge = BASE64URL(SHA256(code_verifier))` to the authorization endpoint.
3. Exchange the returned code and original verifier at the token endpoint.
4. Store refresh tokens through the shipped `ISecretStore` abstraction.

## Refresh-token storage hooks

`OidcAccessTokenProvider` should accept an `ISecretStore` or `ISecretRegistry`
so refresh tokens can be persisted in DPAPI, Keychain, Key Vault, Kubernetes
secrets, or another deployment-specific store.

## Usage sketch

```csharp
ISecretStore refreshTokens = new MyRefreshTokenStore();

IAccessTokenProvider tokens = new OidcAccessTokenProvider(
    authorityUri: "https://issuer.example",
    clientId: "opcua-client",
    redirectUri: new Uri("http://localhost:8400/callback"),
    refreshTokenStore: refreshTokens);

IClientIdentityProvider identity = new OidcClientIdentityProvider(
    new IssuedTokenIdentityProvider(tokens, Profiles.JwtUserToken));

await session.UpdateIdentityAsync(identity, ct).ConfigureAwait(false);
```
