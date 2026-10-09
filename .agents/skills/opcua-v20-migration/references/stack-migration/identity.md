# Identity, Token Handlers, and Secrets

> **When to read this:** Read this for the pluggable identity-provider model (`IClientIdentityProvider`, `IUserTokenAuthenticator`, `IAccessTokenProvider`, `ITokenIssuer`, `IIdentityClaims`), the new `IUserIdentityTokenHandler` registry, and the secret-store / caller-password registry.

## Contents

- [User Identity Token Handlers](#user-identity-token-handlers)
  - [Migrating from 1.5.x](#migrating-from-15x)
  - [Earlier 2.0 previews](#earlier-20-previews)
- [User Identity Providers](#user-identity-providers)
  - [`SessionManager.ImpersonateUser` → registry authenticators](#sessionmanagerimpersonateuser--registry-authenticators)
  - [`ManagedSessionOptions.Identity` → `IdentityProvider`](#managedsessionoptionsidentity--identityprovider)
- [Secrets — caller-supplied passwords go through a secret registry](#secrets--caller-supplied-passwords-go-through-a-secret-registry)

## User Identity Token Handlers

### Migrating from 1.5.x

The wire token types are unchanged, but cryptographic operations now live on
`IUserIdentityTokenHandler` rather than on the generated identity tokens.
Most applications should use [identity providers](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/IdentityProviders.md)
and let session activation handle the token cryptography.

For X.509 identities, replace direct certificate-based construction with an
identifier and the certificate manager's provider. Initialize the application
certificates before accessing `configuration.CertificateManager`:

```csharp
UserIdentity userIdentity = await UserIdentity.CreateAsync(
    certificateIdentifier,
    passwordProvider,
    configuration.CertificateManager.CertificateProvider,
    ct);
```

For code that operates on raw tokens, obtain a handler with `token.AsTokenHandler()`
and await `EncryptAsync`, `DecryptAsync`, `SignAsync`, or `VerifyAsync`. Handlers
are not disposable. The current signatures, including optional certificate and
ephemeral-key arguments, are defined by
[`IUserIdentityTokenHandler`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/src/Opc.Ua.Core/Stack/Types/IUserIdentityTokenHandler.cs).
Do not replace the asynchronous operations with blocking waits.

### Earlier 2.0 previews

The following table applies only to applications that adopted earlier preview
handler/provider APIs. These are not additional APIs that 1.5.x consumers must
have used.

| Earlier preview API | Current replacement |
| --- | --- |
| `IUserIdentityTokenHandler : IDisposable` | Drop `using` on handlers; the current interface is not disposable. |
| `UserIdentity : IDisposable` | Drop `using` on identities; manage certificate and secret ownership through their providers. |
| Synchronous `handler.Encrypt`, `Decrypt`, `Sign`, `Verify` | Await the corresponding `*Async` operation. |
| `new UserIdentity(Certificate)` | Use `UserIdentity.CreateAsync(certificateIdentifier, passwordProvider, certificateProvider, ct)`. |
| `new X509IdentityTokenHandler(Certificate)` | Use the `CertificateIdentifier`, `ICertificatePasswordProvider`, and `ICertificateProvider` constructor. |
| `UserIdentity.CreateAsync(certId, passwordProvider, telemetry, ct)` | Pass an `ICertificateProvider` instead of the telemetry argument. |

The current handler types are `AnonymousIdentityTokenHandler`,
`UserNameIdentityTokenHandler`, `X509IdentityTokenHandler`, and
`IssuedIdentityTokenHandler`.

**Sensitive-buffer lifetime:** handlers provide no disposal hook for clearing
server-side decrypted password or issued-token buffers. Do not log or retain
those buffers. The secret registry below manages caller-supplied secrets; it
does not promise secure clearing of every inbound decrypted token buffer.

## User Identity Providers

The identity-provider redesign is a source-level migration only. The OPC UA
wire token types and `ActivateSession` service behavior are unchanged, so
servers and clients can roll forward independently. Obsolete members remain
functional while you migrate to the provider model.

| Obsolete API | Replacement |
|---|---|
| `ISessionManager.ImpersonateUser` | Implement `IUserTokenAuthenticator` and register it with `services.AddIdentityAuthenticator<T>()` or `server.CurrentInstance.IdentityRegistry.Register(...)`. |
| `SessionManager.ImpersonateUser` | Same replacement; the event remains a fallback after the registry declines a token. SelfAdmin elevation logic should move to `IIdentityAugmenter`. |
| SelfAdmin logic in an `ImpersonateUser` subscriber | Implement `IIdentityAugmenter` and register it with `services.AddIdentityAugmenter<T>()` or `IdentityRegistry.RegisterAugmenter(...)`. GDS hosts can use `AddGdsApplicationSelfAdminProvider()`. |
| `ManagedSessionOptions.Identity` | Set `ManagedSessionOptions.IdentityProvider` so long-lived sessions can reacquire expiring identities. |
| `AuthorizationServiceClient.RequestAccessTokenAsync` | Use `StartRequestTokenAsync` followed by `FinishRequestTokenAsync`. |
| `Opc.Ua.Gds.Server.IAccessTokenProvider.RequestAccessTokenAsync` | Implement `StartRequestTokenAsync` and `FinishRequestTokenAsync`; keep the legacy method as a compatibility shim if you serve v1.04 clients. |

- Custom `IAccessTokenProvider` implementations now have a default `EnableRefreshTokens = true`
  behavior on the in-memory provider. Implementers who do not support refresh tokens can override
  `RefreshTokenAsync` to throw `Bad_NotSupported` or set
  `AuthorizationServiceOptions.EnableRefreshTokens = false`.

### `SessionManager.ImpersonateUser` → registry authenticators

Legacy event wiring:

```csharp
server.CurrentInstance.SessionManager.ImpersonateUser +=
    SessionManager_ImpersonateUser;

private void SessionManager_ImpersonateUser(
    Session session, ImpersonateEventArgs args)
{
    if (args.NewIdentity is UserNameIdentityToken token &&
        ValidatePassword(token.UserName, token.DecryptedPassword))
    {
        args.Identity = new UserIdentity(token);
    }
}
```

Modern authenticator plus dependency injection registration:

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

services.AddOpcUa()
    .AddServer(o => o.ApplicationUri = "urn:example:server")
    .AddIdentityAuthenticator<MyUserNameAuthenticator>();

// Manual host alternative:
server.CurrentInstance.IdentityRegistry.Register(new MyUserNameAuthenticator());
```

Repeat the pattern per token type: `UserTokenType.UserName`,
`UserTokenType.Certificate`, `UserTokenType.IssuedToken` with
`IssuedTokenProfileUri = Profiles.JwtUserToken`, or a vendor profile such
as the experimental KeyCredential bridge.

- SelfAdmin elevation now runs through `IIdentityAugmenter` after an authenticator accepts. Register an
  augmenter via `services.AddIdentityAugmenter<T>()` or `IdentityRegistry.RegisterAugmenter(...)`.
- GDS hosts get `GdsApplicationSelfAdminProvider` automatically via `AddDefaultIdentityAuthenticators(...)`
  on the GDS builder — opt out with `DisableGdsApplicationSelfAdminProvider()` (see GDS docs).
- Legacy `ImpersonateUser` subscribers that only layered SelfAdmin should drop the subscription; the
  augmenter sees the secure-channel `ChannelCertificate` + `ChannelApplicationUri` through
  `AuthenticationContext`.

### `ManagedSessionOptions.Identity` → `IdentityProvider`

Before, an eager identity was fixed for the lifetime of the managed session:

```csharp
var options = new ManagedSessionOptions
{
    Endpoint = endpoint,
    Identity = new UserIdentity("alice", passwordBytes)
};
```

After, use a lazy provider. `ManagedSession` refreshes by calling
`Session.UpdateIdentityAsync` before `provider.ExpiresAt` where possible:

```csharp
IClientIdentityProvider provider = new CompositeClientIdentityProvider(
    new UserNamePasswordIdentityProvider(
        "alice",
        secretRegistry,
        new SecretIdentifier("alice-password", "InMemory")),
    new IssuedTokenIdentityProvider(accessTokenProvider));

var options = new ManagedSessionOptions
{
    Endpoint = endpoint,
    IdentityProvider = provider
};
```

## Secrets — caller-supplied passwords go through a secret registry

A new low-level abstraction layer carries caller-supplied secrets
(currently the password held by `CertificatePasswordProvider`) without
forcing a `byte[] DecryptedPassword`-style field to live on the
identity object.

```csharp
public sealed record SecretIdentifier(string Name, string StoreType, string? StorePath = null);
public interface ISecret : IDisposable { ReadOnlySpan<byte> Bytes { get; } }
public interface ISecretStore { ISecret? TryGet(SecretIdentifier id); /* + async Get/Set/Remove */ }
public interface ISecretRegistry { void RegisterStore(ISecretStore store); /* + Get/TryGet */ }
```

The default `InMemorySecretStore` keeps bytes in a `ConcurrentDictionary`
keyed by `SecretIdentifier.Name`. Every `TryGet`/`GetAsync` returns a
fresh `ISecret` view; the receiver disposes it when done. The
implementation chooses what disposal does — no-op for `InMemorySecret`
in this revision, future stores (DPAPI, Kubernetes secret, Azure Key
Vault) can implement clear-on-dispose, lease-return, or watch-handle
release.

`CertificatePasswordProvider` is reimplemented over this registry.
**The existing public ctors stay BC** — they internally create a
per-instance `InMemorySecretStore` and register the password under an
opaque identifier:

```csharp
new CertificatePasswordProvider();                                  // empty
new CertificatePasswordProvider("password");                        // string
new CertificatePasswordProvider(passwordBytes, isUtf8String: true); // bytes
new CertificatePasswordProvider(passwordSpan);                      // ReadOnlySpan<char>

// New advanced ctor for callers who want to plug in a custom store:
new CertificatePasswordProvider(secretRegistry, secretIdentifier);
```

`ICertificatePasswordProvider.GetPassword(CertificateIdentifier)` still
returns `char[]` for backward compatibility — internally it resolves
the secret bytes from the registry and decodes UTF-8 on every call.

---

**See also**

- Related: [certificates.md](certificates.md), [configuration.md](configuration.md), [sessions-subscriptions.md](sessions-subscriptions.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md) — landing page across versions.
