# AuthorizationService Developer Guide

Open Platform Communications Unified Architecture (OPC UA) Part 12 §9
defines the **AuthorizationService**, which a Global
Discovery Server (GDS) provides to issue access tokens for OPC UA
applications. Part 12 v1.05 uses the two-phase `StartRequestToken` /
`FinishRequestToken` flow. `RequestAccessToken` remains available for
legacy compatibility.

For client-side JSON Web Token (JWT) use during `ActivateSession`, see
[Identity Providers](IdentityProviders.md).

## Contents

- [Architecture](#architecture)
- [Modern Part 12 v1.05 flow](#modern-part-12-v105-flow)
  - [Refreshing an access token](#refreshing-an-access-token)
- [Server hosting](#server-hosting)
  - [Custom `ITokenIssuer` for cloud or HSM signing](#custom-itokenissuer-for-cloud-or-hsm-signing)
- [Server-side abstraction](#server-side-abstraction)
- [Client identity-provider bridge](#client-identity-provider-bridge)
- [Legacy compatibility](#legacy-compatibility)
- [Audit events](#audit-events)

## Architecture

```text
┌─────────────────┐  GetServiceDescription   ┌──────────────────────────┐
│  OPC UA Client  │ ───────────────────────► │  ApplicationsNodeManager │
│                 │  RequestAccessToken      │  (GDS Server)            │
│                 │  (legacy, [Obsolete])    │                          │
│                 │ ───────────────────────► │  ┌────────────────────┐  │
│                 │  StartRequestToken       │  │ IAccessTokenProvider│  │
│                 │  FinishRequestToken      │  └──────────┬─────────┘  │
│                 │ ───────────────────────► │             │            │
│                 │ ◄──── access token ───── │      ┌──────▼──────┐     │
│                 │                          │      │ ITokenIssuer │     │
└─────────────────┘                          │      │ JWT / HSM /  │     │
                                             │      │ cloud signer │     │
                                             │      └─────────────┘     │
                                             └──────────────────────────┘
```

`Opc.Ua.Gds.Server.IAccessTokenProvider` routes requests for the
AuthorizationService method handlers. Before returning a token, the provider
validates resource IDs, policies, pending request IDs, and user-token inputs.

Modern Part 12 v1.05 clients call `StartRequestToken` followed by
`FinishRequestToken`. Legacy clients can still call `RequestAccessToken`;
the provider dispatches that call to
`IAccessTokenProvider.RequestAccessTokenAsync`. The API marks this method
`[Obsolete]`.

`Opc.Ua.Identity.ITokenIssuer` signs a token after the provider accepts a
request. The default issuer uses local certificate material. Custom issuers
can delegate signing to a cloud key-management service, a hardware security
module, or a corporate authorization service without changing the OPC UA
method wiring.

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

`StartRequestToken` validates the requested audience and scopes. It allocates
a continuation request ID and stores the pending request in memory.
`FinishRequestToken` exchanges that ID for a compact JWT
(`tokenType = "JWT"`) signed by the configured
`Opc.Ua.Identity.ITokenIssuer`.

### Refreshing an access token

```csharp
if (refreshToken != null && DateTime.UtcNow >= expiresAt.AddMinutes(-5))
{
    var refreshed = await authClient.RefreshTokenAsync(resourceId, refreshToken);
    jwt = refreshed.accessToken;
    expiresAt = refreshed.accessTokenExpiryTime;
    refreshToken = refreshed.newRefreshToken; // store the rotated token
}
```

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

`MyTokenIssuer` implements `Opc.Ua.Identity.ITokenIssuer`. The default
`CertificateJwtIssuer` signs JSON Web Signature (JWS) tokens defined by RFC
7515. It uses the Elliptic Curve Digital Signature Algorithm (ECDSA)
(`ES256`, `ES384`, or `ES512`) or RSA (`RS256` by default). Custom issuers
can also use RSA-PSS. The default issuer does not add a JWT package
dependency.

### Custom `ITokenIssuer` for cloud or HSM signing

Use `WithAuthorizationService<TIssuer>()` when the signing key is not
resident in the process. External signing options include:

- Azure Key Vault
- AWS KMS
- An HSM
- A corporate token service

The issuer builds the JWS header and payload, then delegates the signing
operation to the external service.

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
key material that local verifiers expect. The custom issuer controls how it
produces the signature.

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

`RequestAccessToken` and
`AuthorizationServiceClient.RequestAccessTokenAsync` are obsolete because
Part 12 v1.05 replaced the one-shot exchange with `StartRequestToken` and
`FinishRequestToken`. When a provider is configured, the service still
dispatches the wire method to
`IAccessTokenProvider.RequestAccessTokenAsync` so v1.04 clients continue to
work.

## Audit events

Successful and failed token operations raise `AccessTokenIssuedAuditEventType`. Tokens and private credentials are not included in audit payloads.
