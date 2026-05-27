# OPC UA .NET Standard stack documentation

Here is a list of available documentation for different topics:

## UA Core stack related

* [OPC UA Profiles and Facets](Profiles.md) - Overview of supported OPC UA profiles, facets, security policies, and transport protocols.
* [Migration Guide](MigrationGuide.md) - How to migrate from a previous version.
* [Sessions, Reconnection, and Subscription Engines](Sessions.md) - Architectural overview of `Session`, `ManagedSession`, `SessionReconnectHandler`, and the classic / V2 subscription engines, including guidance on which to use.
* About [.NET platform](PlatformBuild.md) support, Nuget packages and versioning.
* How X.509 [Certificates](Certificates.md) are used in the certificate stores.
* Using the [Reverse Connect](ReverseConnect.md) for the UA-TCP transport.
* Support for the [TransferSubscriptions](TransferSubscription.md) service set.
* [Observability](Observability.md) support in the stack.
* Support for [WellKnownRoles & RoleBasedUserManagement](RoleBasedUserManagement.md).
* Pluggable [Identity Providers](IdentityProviders.md) — interfaces (`IClientIdentityProvider`, `IUserTokenAuthenticator`, `IAccessTokenProvider`, `ITokenIssuer`, `IIdentityClaims`) plus the OPC 10000-6 §6.5.2.2 `IssuerEndpointUrl` JSON parser for OAuth2 / OIDC / Entra / JWT flows.
* Support for [ECC Certificates](EccProfiles.md).
* Working with [ComplexTypes](ComplexTypes.md) - Custom structures and enumerations.
* Client-based [NodeSet Export](NodeSetExport.md) - Export server address space to NodeSet2 XML.
* Source generated [DataTypes] - How to annotate POCO classes and let the source generator generate the `IEncodeable` implementation.
* Source generated [NodeManagers](SourceGeneratedNodeManagers.md) - Emit an `AsyncCustomNodeManager` from a model design XML and wire callbacks via the fluent `INodeManagerBuilder` API; supports NativeAOT single-file servers (sample: [MinimalBoilerServer](../Applications/MinimalBoilerServer)).
* [Alias Names](AliasNames.md) - Full server + client support for the OPC UA Part 17 alias-name model (`AliasNameType`, `AliasNameCategoryType`, `FindAlias`, `FindAliasVerbose`, `AddAliasesToCategory`, `DeleteAliasesFromCategory`, `LastChange`).
* [Dependency Injection](DependencyInjection.md) - The unified `services.AddOpcUa()` / `IOpcUaBuilder` surface for hosting OPC UA components in `Microsoft.Extensions.DependencyInjection` / the .NET Generic Host (servers as `IHostedService`, options via `Action<T>` or `IConfiguration`, AOT-friendly).
* [AuthorizationService](AuthorizationService.md) - Modern Part 12 `StartRequestToken` / `FinishRequestToken`, `ITokenIssuer`, and GDS token issuance.
* [KeyCredentialService](KeyCredentialService.md) - Pull, Push, and experimental bridge guidance for Part 12 KeyCredential flows.


## Identity Providers — sibling packages

Design-only notes for future identity-provider packages:

* [Entra identity provider design](IdentityProviders.Entra.md) — MSAL-backed `IAccessTokenProvider`, `IClientIdentityProvider`, and Entra JWKS resolver.
* [OIDC identity provider design](IdentityProviders.Oidc.md) — generic discovery, PKCE, JWKS rotation, and refresh-token storage hooks.
* [Windows identity provider design](IdentityProviders.Windows.md) — Windows Integrated / Kerberos with Negotiate and PAC group SID extraction.
* [ASP.NET Core identity provider design](IdentityProviders.AspNetCore.md) — `Microsoft.Identity.Web` `ITokenAcquisition` adapter and token-store integration.

## Reference application related

* [Reference Client](../Applications/ConsoleReferenceClient/README.md) documentation for configuration of the console reference client using parameters.
* [Reference Server](../Applications/README.md) documentation for running against CTT.
* [Provisioning Mode](ProvisioningMode.md) for secure certificate provisioning and initial server configuration.
* Using the [Container support](ContainerReferenceServer.md) of the Reference Server in Visual Studio 2026 and for local testing.

Starting with version 1.5.375.XX the Windows Forms reference client & reference server were moved to the [OPC UA .NET Standard Samples](https://github.com/OPCFoundation/UA-.NETStandard-Samples) repository.

## For the PubSub support library

* The [PubSub](PubSub.md) library with samples.
* The [ConsoleReferencePublisher](../Applications/ConsoleReferencePublisher/README.md) documentation.
* The [ConsoleReferenceSubscriber](../Applications/ConsoleReferenceSubscriber/README.md) documentation.

## Global Discovery Server (GDS)

* [GDS Developer Guide](GDS.md) — Application registration, certificate management (pull & push models), roles and authorization, provider implementation, end-to-end examples.
* [KeyCredentialService](KeyCredentialService.md) — Credential issuance for non-OPC UA services (MQTT, REST), IKeyCredentialRequestStore provider guide, ISecretStore integration.
* [AuthorizationService](AuthorizationService.md) — OAuth2-style access token issuance, IAccessTokenProvider implementation guide.
* [Role-Based Security](RoleBasedUserManagement.md) — Part 18 roles and claim-based identity-mapping rules.
* [Identity Providers](IdentityProviders.md) — server and client identity-provider architecture.
* [Dependency Injection](DependencyInjection.md) — DI hosting and identity registration extensions.
