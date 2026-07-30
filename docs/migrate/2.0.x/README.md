# Migrating from 1.5.378 to 2.0.x

Version **2.0** introduces a major architectural change from pre-generated
code files to runtime source generation and more efficient memory use,
with several major breaking changes requiring changes to your applications.

This folder includes all of the 1.5.378 → 2.0 migration content as a
set of focused sub-documents so you can read only the parts that affect
your application. Start with the **Migration sub-doc index** below to
find the sub-doc that matches the symptom you are seeing.

> **Automate the migration.** Add the
> [`OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer)
> analyzer package to your projects to receive analyzer warnings and
> one-click fixes for many of the patterns in these sub-docs. Rule IDs
> `UA0001` – `UA0020` map directly to the type-safety patterns
> described in [`types.md`](types.md).

> **Pro TIP.** Point your favorite coding agent at the
> [`opcua-v20-migration`](../../../.agents/skills/opcua-v20-migration/SKILL.md)
> skill, which knows when to load each sub-doc and runs the codefixer
> for you.

## Migration sub-doc index

Find the row that matches the error / API surface you are dealing with
and read **only** the listed sub-doc. The agent skill uses the same
table; loading a single sub-doc keeps the context window small.

| When you hit… | Read |
| --- | --- |
| `CS0029` / `CS1503` / `CS0266` on `NodeId`, `Variant`, `DataValue`, `ExtensionObject`, `QualifiedName`, `LocalizedText`, `ArrayOf<T>` / `MatrixOf<T>`, `ByteString`, `StatusCode`, `XmlElement`, `EnumValue`; `[Obsolete]` warnings on built-in type APIs (analyzer `UA0001`–`UA0020`) | [`types.md`](types.md) |
| Loggers (`Utils.LogX`, `Utils.Trace`), removed static logger helpers (`Utils.SetLogger` / `Utils.SetLogLevel`), telemetry context, constructor `ITelemetryContext` parameter changes, OLD-vs-NEW snippets, fluent DI registration (`AddOpcUa().AddLogging().AddMetrics()`), breaking-changes inventory, migration utilities | [`telemetry.md`](telemetry.md) |
| `OPCFoundation.NetStandard.Opc.Ua.*` package upgrade, TFM changes, Newtonsoft removal | [`packages.md`](packages.md) |
| Source-generated `*Collection` shims, NodeManager generator, default of boolean properties, project structure | [`source-generation.md`](source-generation.md) |
| `IEncodeableFactoryBuilder`, `IType`, JSON / XML / binary encoders, `EncodeableFactory.GlobalFactory`, `IJsonEncodeable`, `ComplexTypes` namespace move | [`encoders.md`](encoders.md) |
| `CustomNodeManager`, `NodeState` clone / read / write helpers, `OnAfterCreate(CancellationToken)`, `INodeManager3`, `INodeCache.InvalidateNode`, generics on `BaseVariableState` / `BaseVariableTypeState`, predefined-node processing | [`node-states.md`](node-states.md) |
| `IUserIdentityTokenHandler`, `IClientIdentityProvider`, `IUserTokenAuthenticator`, `IAccessTokenProvider`, `ITokenIssuer`, `IIdentityClaims`, caller-supplied secrets, secret store | [`identity.md`](identity.md) |
| `CertificateValidator`, ref-counted `Certificate` wrapper, `CertificateManager`, `ICertificateProvider`, obsoleted `X509Certificate2` direct-exposure APIs, PushManagement transactions (`ApplyChanges`-gated TrustList updates) | [`certificates.md`](certificates.md) |
| `ApplicationConfiguration` changes, Data-Contract serializer removal, `ParseExtension` / `UpdateExtension` signature, session / browser state persistence | [`configuration.md`](configuration.md) |
| `Session` → `ManagedSession`, V2 subscription engine, GDS-client `Task` → `ValueTask` modernisation, removed obsolete GDS APIs, durable subscriptions, PubSub, reverse-connect | [`sessions-subscriptions.md`](sessions-subscriptions.md) |
| `UaPubSubApplication.Create*`, `IUaPubSubConnection`, `UaPubSubConfigurator`, `IUaPublisher`, AMQP transport, `JsonEncodingMode.Reversible/NonReversible`, PubSub JSON encoder changes, `DataSetFieldContentMask` RawData / timestamp behaviour | [`pubsub.md`](pubsub.md) |
| `AlarmConditionState` state-transition behaviour, auto-emitted `GeneralModelChangeEvent`, `ModelChangeAggregator`, `INodeCache.InvalidateNode` triggered by model change | [`alarms-model-change.md`](alarms-model-change.md) |
| `DateTime.UtcNow`, `Timer`, deterministic time in tests; `System.TimeProvider` adoption | [`timeprovider.md`](timeprovider.md) |
| `ITransportListener.Open` / `Close` removed, `ReverseConnectManager.StartService` / `Dispose` obsolete, reverse-connect DI/provider migration, custom `ITransportListenerFactory` / `ITransportListenerCertificateRotation` implementers need the new async method names | [`transport-listener-async.md`](transport-listener-async.md) |

## All sub-documents

- [`telemetry.md`](telemetry.md) — Telemetry and Logging
- [`packages.md`](packages.md) — Package, Target Framework, and Dependency Changes
- [`source-generation.md`](source-generation.md) — Source Generation
- [`types.md`](types.md) — Improved Type Safety
- [`encoders.md`](encoders.md) — Encoders and Complex Types
- [`node-states.md`](node-states.md) — Node States and `INodeCache`
- [`identity.md`](identity.md) — Identity, Token Handlers, and Secrets
- [`certificates.md`](certificates.md) — Certificates and `ICertificateProvider`
- [`configuration.md`](configuration.md) — Configuration and State Persistence
- [`sessions-subscriptions.md`](sessions-subscriptions.md) — Sessions, GDS Client, and Subscriptions
- [`pubsub.md`](pubsub.md) — PubSub (Part 14): breaking API, transport, JSON, and field-encoding changes
- [`alarms-model-change.md`](alarms-model-change.md) — Alarms and Address-Space Model Changes
- [`timeprovider.md`](timeprovider.md) — Time and Timer Abstraction (`TimeProvider`)
- [`transport-listener-async.md`](transport-listener-async.md) — Async `ITransportListener` API (issue #3923)

## See also

- [Migration Guide landing page](../../MigrationGuide.md) — index across
  all versions, including the small `1.05.377` → `1.05.378` and
  `1.04` → `1.05` legacy notes.
- [What's New in 2.0](../../WhatsNewIn2.0.md) — narrative tour of the
  2.0 changes, grouped by theme and layer.
- [Profiles](../../Profiles.md) — facet / profile coverage of the 2.0
  release.
