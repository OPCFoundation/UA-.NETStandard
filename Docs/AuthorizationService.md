# AuthorizationService Developer Guide

OPC 10000-12 §9 defines the **AuthorizationService** — a mechanism for
a GDS to issue access tokens that OPC UA applications use to
authenticate to other services or to each other using OAuth2-style
token exchange.

## Architecture

```
┌─────────────────┐  GetServiceDescription  ┌──────────────────────────┐
│  OPC UA Client   │ ────────────────────► │  ApplicationsNodeManager   │
│                  │  RequestAccessToken   │  (GDS Server)              │
│                  │ ────────────────────► │                            │
│                  │ ◄──── access token ── │  ┌──────────────────────┐  │
│                  │                       │  │ IAccessTokenProvider │  │
└─────────────────┘                       │  └──────────┬───────────┘  │
                                          │             │              │
                                          │      ┌──────▼──────┐      │
                                          │      │  OAuth2/JWT  │      │
                                          │      │  Provider    │      │
                                          │      └─────────────┘      │
                                          └────────────────────────────┘
```

## Client API

### AuthorizationServiceClient

A thin client proxy for the AuthorizationService methods:

```csharp
using Opc.Ua.Gds.Client;

// After connecting a session to the GDS server:
var authClient = new AuthorizationServiceClient(session, serviceNodeId);

// 1. Discover the authorization service
var (serviceUri, serviceCert, tokenPolicies)
    = await authClient.GetServiceDescriptionAsync();

// 2. Request an access token
string accessToken = await authClient.RequestAccessTokenAsync(
    identityToken: myUserIdentity,
    resourceId: "urn:target-service");
```

### GlobalDiscoveryServerClient helpers

The `GlobalDiscoveryServerClient` does not wrap the
AuthorizationService directly. Use `AuthorizationServiceClient`
with the GDS session:

```csharp
var gdsClient = new GlobalDiscoveryServerClient(config);
await gdsClient.ConnectAsync(gdsEndpointUrl);

// Browse for the AuthorizationServiceType instance
var authClient = new AuthorizationServiceClient(
    gdsClient.Session, authServiceNodeId);
```

## Server-Side: Implementing a Provider

### IAccessTokenProvider

The server-side abstraction for token issuance. When no provider is
injected, the GDS returns `Bad_NotSupported` for all token methods.

```csharp
public interface IAccessTokenProvider
{
    // Stable one-shot token exchange (§9.4)
    ValueTask<string> RequestAccessTokenAsync(
        UserIdentityToken identityToken,
        string resourceId,
        CancellationToken ct = default);

    // Two-phase token exchange — begin (§9.5, RC)
    ValueTask<(ByteString serviceData, Guid requestId)>
        StartRequestTokenAsync(
            string resourceId,
            string policyId,
            ByteString requestorData,
            CancellationToken ct = default);

    // Two-phase token exchange — complete (§9.6, RC)
    ValueTask<AccessTokenResult> FinishRequestTokenAsync(
        Guid requestId,
        ArrayOf<string> requestedRoles,
        UserIdentityToken userIdentityToken,
        SignatureData userTokenSignature,
        CancellationToken ct = default);

    // Refresh an existing token (§9.7, RC)
    ValueTask<AccessTokenResult> RefreshTokenAsync(
        string resourceId,
        string currentRefreshToken,
        CancellationToken ct = default);
}
```

### AccessTokenResult

Returned by the two-phase and refresh operations:

```csharp
public sealed class AccessTokenResult
{
    public string AccessToken { get; set; }
    public DateTime AccessTokenExpiryTime { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime RefreshTokenExpiryTime { get; set; }
}
```

### Writing a Custom Provider

Example: delegating to an OAuth2 authorization server:

```csharp
public class OAuth2AccessTokenProvider : IAccessTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _tokenEndpoint;

    public OAuth2AccessTokenProvider(string tokenEndpoint)
    {
        _httpClient = new HttpClient();
        _tokenEndpoint = tokenEndpoint;
    }

    public async ValueTask<string> RequestAccessTokenAsync(
        UserIdentityToken identityToken,
        string resourceId,
        CancellationToken ct)
    {
        // Validate the OPC UA identity token
        string userName = ((UserNameIdentityToken)identityToken).UserName;

        // Exchange for an OAuth2 token
        var request = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", userName),
            new KeyValuePair<string, string>("scope", resourceId)
        });

        var response = await _httpClient
            .PostAsync(_tokenEndpoint, request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        // Parse and return the access token
        return ParseAccessToken(json);
    }

    public ValueTask<(ByteString, Guid)> StartRequestTokenAsync(...)
        => throw new ServiceResultException(StatusCodes.BadNotSupported);

    public ValueTask<AccessTokenResult> FinishRequestTokenAsync(...)
        => throw new ServiceResultException(StatusCodes.BadNotSupported);

    public ValueTask<AccessTokenResult> RefreshTokenAsync(...)
        => throw new ServiceResultException(StatusCodes.BadNotSupported);
}
```

### Wiring into the GDS Server

Set the `AccessTokenProvider` property on `ApplicationsNodeManager`:

```csharp
var appNodeManager = new ApplicationsNodeManager(server, configuration, ...);
appNodeManager.AccessTokenProvider =
    new OAuth2AccessTokenProvider("https://auth.example.com/token");
```

When `AccessTokenProvider` is `null` (default), all token methods
return `Bad_NotSupported`.

## Audit Events

| Operation | Audit Event Type |
|-----------|-----------------|
| RequestAccessToken | `AccessTokenIssuedAuditEventType` |

Tokens themselves are **not** included in audit payloads.

## End-to-End Example

```csharp
// ── Server Side ──────────────────────────────────────

// 1. Create a token provider
IAccessTokenProvider tokenProvider =
    new OAuth2AccessTokenProvider("https://auth.example.com/token");

// 2. Create the GDS server
var gdsServer = new GlobalDiscoverySampleServer(
    database, requestStore, certGroup, userDb, telemetry);

// 3. After server starts, set the provider on the node manager
// (In the INodeManagerFactory, after creating ApplicationsNodeManager)
appNodeManager.AccessTokenProvider = tokenProvider;

// ── Client Side ──────────────────────────────────────

// 1. Connect to the GDS
var gdsClient = new GlobalDiscoveryServerClient(config);
await gdsClient.ConnectAsync(gdsEndpointUrl);

// 2. Discover the AuthorizationService
var authClient = new AuthorizationServiceClient(
    gdsClient.Session, authServiceNodeId);

var (serviceUri, cert, policies) =
    await authClient.GetServiceDescriptionAsync();

// 3. Request an access token
string token = await authClient.RequestAccessTokenAsync(
    myIdentityToken, "urn:target-resource");

// 4. Use the token to call the target service
httpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", token);
```
