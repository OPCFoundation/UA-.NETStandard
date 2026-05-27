# AuthorizationService Developer Guide

OPC 10000-12 §9 defines the **AuthorizationService** — a GDS service that issues access tokens for OPC UA applications. Part 12 v1.05 uses the two-phase `StartRequestToken` / `FinishRequestToken` flow; `RequestAccessToken` remains available only for legacy compatibility.

For client-side JWT use during `ActivateSession`, see [Identity Providers](IdentityProviders.md).

## Modern Part 12 v1.05 flow

```csharp
using Opc.Ua.Gds.Client;

var authClient = new AuthorizationServiceClient(session, serviceNodeId);
var (serviceUri, serviceCertificate, tokenPolicies) =
    await authClient.GetServiceDescriptionAsync();

var (serviceData, requestId) = await authClient.StartRequestTokenAsync(
    resourceId: "urn:target-server",
    policyId: "jwt",
    requestorData: ByteString.From(Encoding.UTF8.GetBytes("read write")));

var (jwt, expiresAt, refreshToken, refreshExpiresAt) =
    await authClient.FinishRequestTokenAsync(
        requestId,
        Array.Empty<string>().ToArrayOf(),
        new AnonymousIdentityToken(),
        new SignatureData());
```

`StartRequestToken` validates the requested audience and scopes, allocates a continuation request id, and stores the pending request in memory. `FinishRequestToken` exchanges that id for a compact JWT (`tokenType = "JWT"`) signed by the configured `Opc.Ua.Identity.ITokenIssuer`.

## Server hosting

Use `WithAuthorizationService` to register the default in-box issuer and in-memory request store:

```csharp
services.AddOpcUa()
    .AddGdsServer(o =>
    {
        o.ApplicationName = "GlobalDiscoveryServer";
        o.ApplicationUri = "urn:example:gds";
        o.EndpointUrls.Add("opc.tcp://localhost:58810/GDS");
    })
    .WithAuthorizationService(o =>
    {
        o.IssuerUri = "urn:example:gds";
        o.SigningCertificate = new CertificateIdentifier
        {
            StoreType = CertificateStoreType.Directory,
            StorePath = "%LocalApplicationData%/OPC Foundation/GDS/pki/own",
            SubjectName = "CN=GlobalDiscoveryServer"
        };
        o.AllowedAudiences.Add("urn:target-server");
        o.DefaultScopes.Add("read");
    });
```

When `SigningCertificate` is omitted in the hosted GDS, the default issuer falls back to the GDS application instance certificate. Custom deployments can replace the signer:

```csharp
builder.WithAuthorizationService<MyTokenIssuer>(o =>
{
    o.IssuerUri = "https://issuer.example";
});
```

`MyTokenIssuer` implements `Opc.Ua.Identity.ITokenIssuer`. The default `EcdsaJwtIssuer` signs hand-rolled RFC 7515 JWS tokens with ECDSA (`ES256`/`ES384`/`ES512`) or RSA (`RS256` default; RSA-PSS supported by custom issuers) without adding a JWT package dependency.

### Custom `ITokenIssuer` for cloud or HSM signing

Use `WithAuthorizationService<TIssuer>()` when the signing key is not
resident in the process (for example Azure Key Vault, AWS KMS, an HSM, or
a corporate token service). The issuer builds the JWS header/payload and
delegates the signing operation to the external service.

```csharp
public sealed class CloudKmsTokenIssuer : ITokenIssuer
{
    public string IssuerUri => "https://issuer.example/gds";
    public string ProfileUri => Profiles.JwtUserToken;

    public async ValueTask<AccessToken> IssueAsync(
        TokenIssuanceRequest request,
        CancellationToken ct = default)
    {
        byte[] signingInput = BuildJwtSigningInput(request);
        byte[] signature = await SignWithKeyVaultOrKmsAsync(signingInput, ct)
            .ConfigureAwait(false);
        byte[] compactJws = CombineCompactJws(signingInput, signature);

        return new AccessToken(
            Profiles.JwtUserToken,
            compactJws,
            DateTime.UtcNow + request.RequestedLifetime,
            request.Subject);
    }
}

services.AddOpcUa()
    .AddGdsServer(o => o.ApplicationUri = "urn:example:gds")
    .WithAuthorizationService<CloudKmsTokenIssuer>(o =>
    {
        o.SigningCertificate = new CertificateIdentifier
        {
            StoreType = CertificateStoreType.Directory,
            StorePath = "%LocalApplicationData%/OPC Foundation/GDS/pki/own",
            SubjectName = "CN=GlobalDiscoveryServer"
        };
        o.IssuerUri = "https://issuer.example/gds";
        o.AllowedAudiences.Add("urn:target-server");
    });
```

The `SigningCertificate` option still advertises the issuer identity and
key material expected by local verifiers; the custom issuer decides how
the actual signature is produced.

## Server-side abstraction

`Opc.Ua.Gds.Server.IAccessTokenProvider` backs the GDS method handlers:

```csharp
ValueTask<(ByteString serviceData, Guid requestId)> StartRequestTokenAsync(...);
ValueTask<AccessTokenResult> FinishRequestTokenAsync(...);

[Obsolete("Use StartRequestTokenAsync + FinishRequestTokenAsync for Part 12 v1.05 compliance.")]
ValueTask<string> RequestAccessTokenAsync(...);
```

The default `AuthorizationServiceManager` delegates to `InMemoryAccessTokenProvider`, which keeps pending request ids in memory and calls the configured `ITokenIssuer` for final JWT issuance.

## Client identity-provider bridge

`GdsAccessTokenProvider` adapts `AuthorizationServiceClient` to the client-side `Opc.Ua.Identity.IAccessTokenProvider` used by `IssuedTokenIdentityProvider` and `Session.UpdateIdentityAsync(IClientIdentityProvider)`.

## Legacy compatibility

`RequestAccessToken` and `AuthorizationServiceClient.RequestAccessTokenAsync` are marked obsolete because Part 12 v1.05 replaced the one-shot exchange with `StartRequestToken` / `FinishRequestToken`. The wire method is still dispatched to `IAccessTokenProvider.RequestAccessTokenAsync` so v1.04 clients continue to work when a provider is configured.

## Audit events

Successful and failed token operations raise `AccessTokenIssuedAuditEventType`. Tokens and private credentials are not included in audit payloads.
