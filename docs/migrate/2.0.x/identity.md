# Identity, Token Handlers, and Secrets

> **When to read this:** Read this for the pluggable identity-provider model (`IClientIdentityProvider`, `IUserTokenAuthenticator`, `IAccessTokenProvider`, `ITokenIssuer`, `IIdentityClaims`), the new `IUserIdentityTokenHandler` registry, and the secret-store / caller-password registry.

## User Identity Token Handlers

**Breaking Change**: Identity tokens no longer perform cryptographic
operations directly. The handler pattern introduced earlier is now
**fully asynchronous** and **non-disposable**, and the
`Certificate`-taking ctors of `UserIdentity` and
`X509IdentityTokenHandler` have been removed in favour of a
`CertificateIdentifier` + `ICertificateProvider` model that resolves
the private-key cert on demand.

**Before**:

```csharp
    var token = new X509IdentityToken();
    using var handler = token.AsTokenHandler();
    handler.Encrypt(certificate, nonce, securityPolicy, context);
    handler.Decrypt(certificate, nonce, securityPolicy, context);
    var signature = handler.Sign(data, securityPolicy);
    bool isValid = handler.Verify(data, signature, securityPolicy);

    using var userIdentity = new UserIdentity(certificate);   // legacy ctor
```

**After**:

```csharp
    var token = new X509IdentityToken();
    var handler = token.AsTokenHandler();                      // not IDisposable
    await handler.EncryptAsync(certificate, nonce, securityPolicy, context, ct: ct);
    await handler.DecryptAsync(certificate, nonce, securityPolicy, context, ct: ct);
    SignatureData signature = await handler.SignAsync(data, securityPolicy, ct);
    bool isValid = await handler.VerifyAsync(data, signature, securityPolicy, ct);

    // New cert-based UserIdentity: identifier + cache-aware provider.
    UserIdentity userIdentity = await UserIdentity.CreateAsync(
        certificateIdentifier,
        passwordProvider,
        configuration.CertificateManager.CertificateProvider,
        ct);
```

**New interface shape**:

```csharp
    public interface IUserIdentityTokenHandler :
        ICloneable, IEquatable<IUserIdentityTokenHandler>
    {
        UserIdentityToken Token { get; }
        string DisplayName { get; }
        UserTokenType TokenType { get; }

        void UpdatePolicy(UserTokenPolicy userTokenPolicy);

        ValueTask EncryptAsync(
            Certificate receiverCertificate, byte[] receiverNonce,
            string securityPolicyUri, IServiceMessageContext context,
            ..., CancellationToken ct = default);
        ValueTask DecryptAsync(
            Certificate certificate, Nonce receiverNonce,
            string securityPolicyUri, IServiceMessageContext context,
            ..., CancellationToken ct = default);
        ValueTask<SignatureData> SignAsync(
            byte[] dataToSign, string securityPolicyUri,
            CancellationToken ct = default);
        ValueTask<bool> VerifyAsync(
            byte[] dataToVerify, SignatureData signatureData,
            string securityPolicyUri, CancellationToken ct = default);
    }
```

**Migration required**:

| Removed | Replacement |
| ------- | ----------- |
| `IUserIdentityTokenHandler : IDisposable` | `IUserIdentityTokenHandler` (no `IDisposable`). Drop `using` on handler instances. Sensitive byte buffers (`UserNameIdentityTokenHandler.DecryptedPassword`, `IssuedIdentityTokenHandler.DecryptedTokenData`) are no longer cleared on disposal — secure-memory management is the secret store's responsibility (deferred to a future revision). |
| `UserIdentity : IDisposable`, `UserIdentity.Dispose()` | `UserIdentity` (no `IDisposable`). Drop `using` on `new UserIdentity(...)`. |
| `handler.Encrypt(...)` (sync) | `await handler.EncryptAsync(..., ct)` |
| `handler.Decrypt(...)` (sync) | `await handler.DecryptAsync(..., ct)` |
| `SignatureData handler.Sign(...)` (sync) | `await handler.SignAsync(..., ct)` |
| `bool handler.Verify(...)` (sync) | `await handler.VerifyAsync(..., ct)` |
| `new UserIdentity(Certificate)` (legacy ctor) | `await UserIdentity.CreateAsync(certificateIdentifier, passwordProvider, certificateProvider, ct)` — the new ctor stores the identifier; the cert is materialised on demand by the provider. |
| `new X509IdentityTokenHandler(Certificate)` | `new X509IdentityTokenHandler(CertificateIdentifier, ICertificatePasswordProvider, ICertificateProvider)` — handler holds no live Certificate; on `SignAsync` the provider's cache is consulted (`TryGetPrivateKeyCertificate`) then the store (`GetPrivateKeyCertificateAsync`). |
| `[Obsolete] new UserIdentity(CertificateIdentifier, CertificatePasswordProvider)` | `await UserIdentity.CreateAsync(certificateIdentifier, passwordProvider, certificateProvider, ct)` — the obsolete ctor blocked on async; the new factory does not pre-resolve. |
| `await UserIdentity.CreateAsync(certId, passwordProvider, telemetry, ct)` | `await UserIdentity.CreateAsync(certId, passwordProvider, certificateProvider, ct)` — `ICertificateProvider` (typically `configuration.CertificateManager.CertificateProvider`) replaces the telemetry-only argument list. |

**Available token handlers** (all non-disposable):
   - `AnonymousIdentityTokenHandler`
   - `UserNameIdentityTokenHandler`
   - `X509IdentityTokenHandler`
   - `IssuedIdentityTokenHandler`

**Note on secure-memory management**: with `IDisposable` gone, the
sync `Array.Clear` of decrypted password / issued-token bytes that
used to happen in `Dispose()` no longer fires. Bytes live in plain
fields until GC. A follow-up revision will route inbound decrypted
secrets through the new `ISecretStore` abstraction (see *Secrets*
below) so secure clearing becomes the store's responsibility, with no
public surface change.

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
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.

