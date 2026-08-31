; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category  | Severity | Notes
--------|-----------|----------|------------------------------------------------------------------------------------------------------
UA0001  | Migration | Info     | Replace Utils.Trace/Utils.LogX calls with ILogger obtained from ITelemetryContext.
UA0002  | Migration | Warning  | Replace removed `<Type>Collection` types with `List<T>` or `ArrayOf<T>`.
UA0003  | Migration | Warning  | Replace `== null` / `!= null` on now-struct built-in types with the `.IsNull` property.
UA0004  | Migration | Warning  | Remove null-conditional `?.` on now-struct built-in types (NodeId, Variant, DataValue, ...).
UA0005  | Migration | Warning  | Convert `byte[]` to `ByteString` at API boundaries that now require `ByteString`.
UA0006  | Migration | Warning  | Replace obsoleted non-generic Variant constructors with Variant.From.
UA0007  | Migration | Warning  | Replace `new NodeId(string)` / `new ExpandedNodeId(string)` with `NodeId.Parse(s)` / `ExpandedNodeId.Parse(s)`.
UA0008  | Migration | Warning  | Wrap `params object[]` arguments to `Session.Call`/`CallAsync` with `Variant.From(...)`.
UA0009  | Migration | Warning  | Replace `[DataContract]`/`[DataMember]` on configuration extensions with `[DataType]`/`[DataTypeField]`.
UA0010  | Migration | Warning  | Remove `using`/`Dispose()` on `CertificateIdentifier`, `UserIdentity`, `IUserIdentityTokenHandler` (no longer IDisposable).
UA0011  | Migration | Info     | Replace sync `IUserIdentityTokenHandler.Encrypt/Decrypt/Sign/Verify` with `…Async`.
UA0012  | Migration | Warning  | Replace obsolete static `CertificateFactory.*` helpers with `DefaultCertificateFactory.Instance.*`.
UA0014  | Migration | Warning  | Replace `DataValue.IsGood(dv)`/`IsBad`/`IsUncertain` static helpers with `dv.IsGood`/`IsBad`/`IsUncertain` instance properties.
UA0015  | Migration | Info     | Replace sync/APM members on GDS/LDS clients with their `…Async` counterparts.
UA0018  | Migration | Info     | Replace `CertificateIdentifier.Certificate` getter with `CertificateIdentifierResolver.ResolveAsync(...)`.
UA0019  | Migration | Warning  | Replace `new DataValue(StatusCode[, ts])` with `DataValue.FromStatusCode(...)`.
UA0020  | Migration | Warning  | Replace `EncodeableFactory.GlobalFactory` / `EncodeableFactory.Create()` with `ServiceMessageContext.Factory` / `Fork()`.
UA0021  | Migration | Info     | Replace `CertificateValidator` / `CertificateValidationEventArgs` with the 2.0 `ICertificateManager` / `ICertificateValidatorEx` / `CertificateValidationResult` pipeline. See docs/migrate/2.0.x/certificates.md.
UA0022  | Migration | Warning  | Replace `ApplicationConfiguration.CertificateValidator` / `ServerBase.CertificateValidator` property access with `.CertificateManager`.
UA0023  | Migration | Warning  | Replace the removed legacy 1.04 PubSub application, connection, publisher, and configurator types with the new `IPubSubApplication` / `PubSubApplicationBuilder` surface, or call `AddPubSub(...)` on `IOpcUaBuilder` and configure transports on its `IPubSubBuilder`. Migrate the retained obsolete `IUaPubSubDataStore` bridge to `IPublishedDataSetSource`. See docs/migrate/2.0.x/pubsub.md.
UA0024  | Migration | Warning  | Replace `IServerInternal`/`ISession`/`ISubscription` `DiagnosticsLock`/`DiagnosticsWriteLock` access with `UpdateServerDiagnostics(...)` / `UpdateDiagnostics(...)` (and `ISession.ReadDiagnostics` / `ISubscription.ReadDiagnostics` for projections).
UA0025  | Migration | Warning  | Replace `ILocalNode.DataLock` / `Node.DataLock` access with a lock you own; the node guards its own state.
UA0026  | Migration | Warning  | Replace `BaseVariableValue.Lock` access with a lock you own and pass to the `BaseVariableValue` constructor.
UA0027  | Migration | Warning  | Remove `NodeBrowser.DataLock` usage: a browser is single-consumer in 2.0, so drop the `lock (DataLock)` statement and keep its body. See docs/migrate/2.0.x/node-states.md.
UA0028  | Migration | Warning  | Replace `ApplicationConfiguration.PropertiesLock` usage: `Properties` synchronizes itself in 2.0, so drop the `lock` and use `GetOrAddProperty(...)` where a read and a write must be atomic.
UA0030  | Migration | Warning  | Remove use of the server `ISubscription` publish-pipeline members (`PublishTimerExpired`, `Acknowledge`, `PublishTimeout`, `SubscriptionTransferred`, `AvailableSequenceNumbersForRetransmission`, `QueueOverflowHandler`, `SessionClosed`, `Publish` — internalized; `ItemReadyToPublish`/`ItemNotificationsAvailable` — deleted no-ops; `TransferSessionAsync` — deleted, use the `TransferSubscriptions` service) and of `SessionPublishQueue` (internal): the publish pipeline is server-internal in 2.0, and custom subscriptions derive from `Subscription`. See docs/MigrationGuide.md#ua0030.
