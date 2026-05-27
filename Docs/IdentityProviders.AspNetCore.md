> **Design-only — no implementation in this release.** This document reserves the
> public surface for a future `Opc.Ua.Identity.AspNetCore` package. The reference
> implementation in this repository targets `IAccessTokenProvider` /
> `IClientIdentityProvider` / `IUserTokenAuthenticator`; this sibling-package
> design layers on top of those interfaces without modifying them.

# ASP.NET Core Identity Provider Design

Reserved NuGet package: `OPCFoundation.NetStandard.Opc.Ua.Identity.AspNetCore`.

## Public surface

* `AspNetCoreAccessTokenProvider : IAccessTokenProvider`
* `AspNetCoreClientIdentityProvider : IClientIdentityProvider`

`AspNetCoreAccessTokenProvider` delegates to `Microsoft.Identity.Web`
`ITokenAcquisition.GetAccessTokenForUserAsync(...)` for user-delegated tokens.
The client identity provider can wrap the shipped `IssuedTokenIdentityProvider`.

## Token acquisition adapter

The adapter maps OPC UA authorization metadata to ASP.NET Core scopes:

* `AuthorizationServerMetadata.Scopes` becomes the requested scope list.
* `AuthorizationServerMetadata.ResourceUri` can be used as an additional audience/resource hint.
* The current `ClaimsPrincipal` comes from `IHttpContextAccessor` or an explicit accessor delegate.

## Refresh-token persistence

ASP.NET hosts often persist refresh-token references in cookies, session, or a
server-side token cache. A future package should allow an adapter over
`IUserTokenStore` while still keeping raw token material behind the shipped
`ISecretStore` / `ISecretRegistry` abstractions.

## Usage sketch

```csharp
services.AddScoped<IAccessTokenProvider>(sp =>
    new AspNetCoreAccessTokenProvider(
        sp.GetRequiredService<ITokenAcquisition>(),
        sp.GetRequiredService<IHttpContextAccessor>(),
        authorityUri: "https://login.microsoftonline.com/{tenant}/v2.0"));

services.AddScoped<IClientIdentityProvider>(sp =>
    new AspNetCoreClientIdentityProvider(
        new IssuedTokenIdentityProvider(
            sp.GetRequiredService<IAccessTokenProvider>(),
            Profiles.JwtUserToken)));

IClientIdentityProvider identity = httpContext.RequestServices
    .GetRequiredService<IClientIdentityProvider>();
await session.UpdateIdentityAsync(identity, ct).ConfigureAwait(false);
```
