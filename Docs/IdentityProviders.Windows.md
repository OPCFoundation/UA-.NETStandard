> **Design-only — no implementation in this release.** This document reserves the
> public surface for a future `Opc.Ua.Identity.Windows` package. The reference
> implementation in this repository targets `IAccessTokenProvider` /
> `IClientIdentityProvider` / `IUserTokenAuthenticator`; this sibling-package
> design layers on top of those interfaces without modifying them.

# Windows Integrated Identity Provider Design

Reserved NuGet package: `OPCFoundation.NetStandard.Opc.Ua.Identity.Windows`.

## Public surface

* `WindowsIntegratedClientIdentityProvider : IClientIdentityProvider`
* `KerberosUserTokenAuthenticator : IUserTokenAuthenticator`

## Protocol shape

The package would use Windows Integrated Authentication / Kerberos to produce
an issued user token profile reserved by the package. The client provider owns
the SPNEGO/Kerberos exchange and the server authenticator validates the final
AP-REQ or wrapped token before returning an `IUserIdentity`.

## .NET APIs

* `System.Net.Security.NegotiateAuthentication` on `net8.0+`.
* `System.Net.NegotiateStream` on down-level TFMs where the newer API is not available.

## SPN binding

Service principal names should be derived from the target OPC UA server
endpoint and `ApplicationUri`:

* `HOST/<server-fqdn>` for conventional host-bound deployments.
* `OPCUA/<server-fqdn>` where deployments reserve an OPC UA-specific SPN.

The authenticator should validate that the ticket target matches the expected
server identity to avoid replay across UA applications.

## PAC decoding and claims

`KerberosUserTokenAuthenticator` should decode PAC group SIDs and expose them
through `IIdentityClaims.Groups`. Role mapping then uses the shipped Part 18
`IdentityCriteriaType.GroupId` support without changing `IRoleManager`.

## Usage sketch

```csharp
IClientIdentityProvider identity = new WindowsIntegratedClientIdentityProvider(
    servicePrincipalName: "OPCUA/server01.contoso.com");

await session.UpdateIdentityAsync(identity, ct).ConfigureAwait(false);

services.AddOpcUa()
    .AddServer(o => o.ApplicationUri = "urn:contoso:server01")
    .AddIdentityAuthenticator<KerberosUserTokenAuthenticator>();
```
