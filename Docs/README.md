# OPC UA .NET Standard stack documentation

Here is a list of available documentation for different topics:

## UA Core stack related

* [OPC UA Profiles and Facets](Profiles.md) - Overview of supported OPC UA profiles, facets, security policies, and transport protocols.
* [What's New in 2.0](WhatsNewIn2.0.md) - Developer-facing tour of the 1.5.378 → 2.0 changes, grouped by theme and layer, with links to deeper feature docs.
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
* Source generated [NodeManagers](SourceGeneratedNodeManagers.md) - Emit an `AsyncCustomNodeManager` from a model design XML and wire callbacks via the fluent `INodeManagerBuilder` API; supports NativeAOT single-file servers (samples: [MinimalBoilerServer](../Applications/MinimalBoilerServer), [PumpDeviceIntegrationServer](../Applications/PumpDeviceIntegrationServer)). Covers engineering units, property initialisation, alarms, simulation timers, instance creation, NAMUR-style supervision, multi-model composition, and the fluent state-machine builder on top of any `FiniteStateMachineState` subclass. Cross-assembly model references are tracked via the [ModelDependencyAttribute](ModelDependencies.md). Companion-spec packaging — model + server + client library trios — is covered end-to-end by the [Device Integration developer guide](DeviceIntegration.md) using the `Opc.Ua.Di` / `Opc.Ua.Di.Server` / `Opc.Ua.Di.Client` trio as the worked example.
* [Device Integration (DI) developer guide](DeviceIntegration.md) - End-to-end documentation for the `Opc.Ua.Di*` library trio: fluent `IDeviceBuilder`, device sub-type extensions (`AddSoftware`, `AddBlock`, `AddConfigurableObject`, `AddLifetimeIndication`, `WithSupportInfo`), hosting integration (`AddOpcUaDi` / `ConfigureDevicesFor`), lock service, software-update package store, and client helpers (`DiLockClient`, `DiTopologyClient`, `SoftwareUpdateClient`). Includes a section enumerating supported OPC 10000-100 features against the spec.
* [Alias Names](AliasNames.md) - Full server + client support for the OPC UA Part 17 alias-name model (`AliasNameType`, `AliasNameCategoryType`, `FindAlias`, `FindAliasVerbose`, `AddAliasesToCategory`, `DeleteAliasesFromCategory`, `LastChange`).
* [Alarms and Conditions](AlarmsAndConditions.md) - Full server + client support for OPC UA Part 9. Server-side state types for latched/silenced/out-of-service alarms, alarm groups and suppression engine, alarm rate metrics. Client-side `AlarmClient`, typed alarm event records, fluent `AlarmEventFilterBuilder`, `IAsyncEnumerable` alarm streaming via `AlarmStreamExtensions`.
* [Historical Access (Part 11)](HistoricalAccess.md) - Server provider model (`IHistorianProvider` family) and `InMemoryHistorianProvider`, plus the client `HistoryClient` (`session.Historian()`) for raw/modified/at-time/processed reads, annotations, and updates.
* [Aggregates (Part 13)](Aggregates.md) - All 37 standard Part 13 v1.05.07 aggregate functions over historical data: server `AggregateManager` / calculators, native push-down vs framework fallback, `AnnotationCount` via the annotation provider, `AggregateConfiguration` defaults, and the client `ReadProcessedAsync` helper.
* [Streaming Subscriptions](StreamingSubscription.md) - `IAsyncEnumerable`-based subscription API for state-machine waits and short-lived monitoring (`IStreamingSubscription`, `ManagedSession.DefaultStreaming`, `TakeUntilAsync` / `WithTimeoutAsync` helpers).
* [State Machines](StateMachines.md) - Generic, extensible Part 16 state-machine API. Client side: streaming + read helpers on the source-generated `*TypeClient` proxies (`GetCurrentFiniteStateAsync`, `ObserveFiniteTransitionsAsync`, `WaitForStateAsync`). Server side: unified fluent `StateMachineBuilder` with two complementary modes — *definition* (`Create(...)` + `AddState` / `AddTransition` / `OnCause` for ad-hoc machines via `FluentFiniteStateMachineState`) and *lifecycle* (`For(...)` / `INodeBuilder.AsStateMachine()` + `OnEnterState` / `WithCause` / `WithTimedTransition` to attach behavior to stack-shipped or generator-emitted FSMs). Vendor state machines inherit both ends of the API automatically.
* [Model Change Tracking](ModelChangeTracking.md) - Client-side address-space change tracking with per-node `INodeCache` invalidation; server-side `ModelChangeAggregator` and auto-emitted `GeneralModelChangeEvent` from `CustomNodeManager.CreateNode/DeleteNode`.
* [NodeManagement Service Set](NodeManagement.md) - Server-side AddNodes / DeleteNodes / AddReferences / DeleteReferences, including the `INodeManagementAsyncNodeManager` opt-in pattern and per-NodeManager `AllowNodeManagement` gate.
* [Dependency Injection](DependencyInjection.md) - The unified `services.AddOpcUa()` / `IOpcUaBuilder` surface for hosting OPC UA components in `Microsoft.Extensions.DependencyInjection` / the .NET Generic Host (servers as `IHostedService`, options via `Action<T>` or `IConfiguration`, AOT-friendly).
* [AuthorizationService](AuthorizationService.md) - Modern Part 12 `StartRequestToken` / `FinishRequestToken`, `ITokenIssuer`, and GDS token issuance.
* [KeyCredentialService](KeyCredentialService.md) - Pull, Push, and experimental bridge guidance for Part 12 KeyCredential flows.

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
* [Dependency Injection](DependencyInjection.md) — dependency injection hosting and identity registration extensions.
