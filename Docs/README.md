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
* [Diagnostics](Diagnostics.md) — logging, telemetry, server audit events, server diagnostics nodes, and packet capture.
* [Performance Benchmarks](Benchmarks.md) — BenchmarkDotNet methodology, the 2.0 (`master`) vs 1.5.378 (`master378`) comparison, root-cause analysis of the encoder/decoder/session regressions and their real-world impact, the subscription-notification (pooled encodeable) micro-benchmarks, and planned future work.
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
* [Subscriptions and Monitored Items Service Set](Subscriptions.md) - V2 subscription engine API. Covers `ISubscriptionManager` for long-lived callback-based subscriptions, the declarative+imperative `SetTriggering` API with N:M support and automatic replay on recreate/reconnect, and `IStreamingSubscription` (`IAsyncEnumerable`-based) for state-machine waits and short-lived monitoring (`ManagedSession.DefaultStreaming`, `TakeUntilAsync` / `WithTimeoutAsync` helpers).
* [Unbounded Monitored Items](Subscriptions.md#unbounded-monitored-items) - V2 logical-subscription wrapper that transparently splits monitored items across multiple server-side partitions when the per-subscription cap is exceeded (`IPartitionedSubscription`, `MonitoredItemOptions.Affinity`, reactive `Bad_TooManyMonitoredItems` fallback, secondary-partition idle-delete).
* [State Machines](StateMachines.md) - Generic, extensible Part 16 state-machine API. Client side: streaming + read helpers on the source-generated `*TypeClient` proxies (`GetCurrentFiniteStateAsync`, `ObserveFiniteTransitionsAsync`, `WaitForStateAsync`). Server side: unified fluent `StateMachineBuilder` with two complementary modes — *definition* (`Create(...)` + `AddState` / `AddTransition` / `OnCause` for ad-hoc machines via `FluentFiniteStateMachineState`) and *lifecycle* (`For(...)` / `INodeBuilder.AsStateMachine()` + `OnEnterState` / `WithCause` / `WithTimedTransition` to attach behavior to stack-shipped or generator-emitted FSMs). Vendor state machines inherit both ends of the API automatically.
* [Model Change Tracking](ModelChangeTracking.md) - Client-side address-space change tracking with per-node `INodeCache` invalidation; server-side `ModelChangeAggregator` and auto-emitted `GeneralModelChangeEvent` from `CustomNodeManager.CreateNode/DeleteNode`.
* [NodeManagement Service Set](NodeManagement.md) - Server-side AddNodes / DeleteNodes / AddReferences / DeleteReferences, including the `INodeManagementAsyncNodeManager` opt-in pattern and per-NodeManager `AllowNodeManagement` gate.
* [High Availability and OPC UA Redundancy](HighAvailability.md) - OPC 10000-4 §6.6 mapping for server, client, and network redundancy; `RedundancySupport`, `ServiceLevel`, manual failover, non-transparent `RedundantManagedClient` modes, HotAndMirrored/Transparent state mirroring, CRDT active/active extensions, and shared-store limitations.
  * [Kubernetes High Availability Deployment](HighAvailabilityKubernetes.md) - Consolidated Kubernetes guide for the `Opc.Ua.Server.Redundancy.K8s` package: Lease leader election, EndpointSlice peer discovery, ServiceLevel-driven readiness, StatefulSet/Deployment and Service manifests, RBAC, probes, time sync, secrets, and GDS/NTRS registration.
* [Dependency Injection](DependencyInjection.md) - The unified `services.AddOpcUa()` / `IOpcUaBuilder` surface for hosting OPC UA components in `Microsoft.Extensions.DependencyInjection` / the .NET Generic Host (servers as `IHostedService`, options via `Action<T>` or `IConfiguration`, AOT-friendly).
* [AuthorizationService](AuthorizationService.md) - Modern Part 12 `StartRequestToken` / `FinishRequestToken`, `ITokenIssuer`, and GDS token issuance.
* [Fuzz testing](../Fuzzing/Fuzzing.md) - SharpFuzz + afl-fuzz + libFuzzer integration. Three areas: `Encoders` (Binary/JSON/XML decoders, built-in type readers, parser entry points), `Certificates` (`X509CRL`, X509 extension parsers, `PEMReader`, `Pkcs10CertificationRequest`, ASN.1 helpers), and `Network` (UA-SC framing via `Opc.Ua.Core.Diagnostics` + internal `TcpMessageParsers` seam on `Opc.Ua.Core`). The [`fuzz-tester`](../.github/agents/fuzz-tester.agent.md) custom agent drives the whole toolchain autonomously: it detects OS-available engines, runs them in parallel, fixes novel findings per repo guidelines, adds the failing input as a regression asset, and pushes one commit per fix until the user says stop.
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
