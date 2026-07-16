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
UA0021  | Migration | Info     | Replace `CertificateValidator` / `CertificateValidationEventArgs` with the 1.6 `ICertificateManager` / `ICertificateValidatorEx` / `CertificateValidationResult` pipeline. See Docs/migrate/2.0.x/certificates.md.
UA0022  | Migration | Warning  | Replace `ApplicationConfiguration.CertificateValidator` / `ServerBase.CertificateValidator` property access with `.CertificateManager`.
UA0023  | Migration | Warning  | Replace the legacy 1.04 PubSub top-level types (`UaPubSubApplication`, `IUaPubSubConnection`, `UaPubSubConnection`, `IUaPublisher`, `UaPublisher`, `IUaPubSubDataStore`, `UaPubSubDataStore`, `UaPubSubConfigurator`) with the new `IPubSubApplication` / `PubSubApplicationBuilder` surface (or `AddPubSub()` / `AddUdpTransport()` / `AddMqttTransport()` on `IOpcUaBuilder`). See Docs/migrate/2.0.x/pubsub.md.
