> **Design-only — no implementation in this release.** This document reserves the
> public surface for a future `Opc.Ua.Identity.Entra` package. The reference
> implementation in this repository targets `IAccessTokenProvider` /
> `IClientIdentityProvider` / `IUserTokenAuthenticator`; this sibling-package
> design layers on top of those interfaces without modifying them.

# Entra Identity Provider Design

Reserved NuGet package: `OPCFoundation.NetStandard.Opc.Ua.Identity.Entra`.

## Public surface

* `EntraIdAccessTokenProvider : IAccessTokenProvider`
* `EntraIdClientIdentityProvider : IClientIdentityProvider`
* `EntraIdJwtIssuerKeyResolver` — resolves Entra signing keys from the tenant JWKS endpoint.

The package would depend on `Microsoft.Identity.Client` (MSAL) and the core OPC UA identity interfaces.

## MSAL integration sketch

`EntraIdAccessTokenProvider` wraps either `IPublicClientApplication` or
`IConfidentialClientApplication`:

* public/native clients use silent acquisition first, then interactive or device-code fallback;
* daemon services use `AcquireTokenForClient`;
* MSAL owns token caching and refresh; the provider forwards each acquired JWT as an `AccessToken`.

## Scope and audience mapping

* Audience: `api://<server-app-id>` or the target OPC UA server `ApplicationUri`.
* Client-credentials scope: `<server-app-id>/.default`.
* User-delegated scopes come from the server's `AuthorizationServerMetadata.Scopes` when present.

## Client-side usage sketch

```csharp
IPublicClientApplication msal = PublicClientApplicationBuilder
    .Create(clientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithDefaultRedirectUri()
    .Build();

IAccessTokenProvider tokens = new EntraIdAccessTokenProvider(
    msal,
    authorityUri: $"https://login.microsoftonline.com/{tenantId}/v2.0");

IClientIdentityProvider identity = new EntraIdClientIdentityProvider(
    new IssuedTokenIdentityProvider(tokens, Profiles.JwtUserToken));

await session.UpdateIdentityAsync(identity, ct).ConfigureAwait(false);
```

## Server-side key resolution

`EntraIdJwtIssuerKeyResolver` would fetch and cache
`https://login.microsoftonline.com/{tenant}/discovery/v2.0/keys`, refresh on
cache expiry, and force-refresh once on signature failure to handle key rotation.
