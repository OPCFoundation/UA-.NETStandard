# Configuration and State Persistence

> **When to read this:** Read this for `ApplicationConfiguration` changes, the removed Data-Contract serializer, Newtonsoft removal from `Opc.Ua.Core`, the new `ParseExtension` / `UpdateExtension` signature, and session / browser state persistence.

## Configuration

### Data Contract Serializer support removed

Because **Data Contract serialization** is not AOT compliant and does not support trimming, all use of `DataContract` in the configuration has been removed. Instead, the source generator enables generating *IEncodeable* implementations using the `DataType` and `DataTypeField` attributes which are now consequently used for all configuration. Because the configuration is now `IEncodeable` the existing encoders and decoders (in particular the new `XmlParser` which parses Xml and allows out of order fields) compliant with Part 6 can be used to serialize and deserialize all configuration and configuration extensions.

> Generated Data types still support DataContract based serialization, however, consider this a deprecated feature.

All configuration DTO classes (`ApplicationConfiguration`, `ServerConfiguration`, `TraceConfiguration`, `TransportConfiguration`, `ServerSecurityPolicy`, `OAuth2ServerSettings`, `OAuth2Credential`, `GlobalDiscoveryServerConfiguration`, `CertificateGroupConfiguration`, `BrowserOptions`, etc.) migrated from `[DataContract]`/`[DataMember]` to source-generated `[DataType]`/`[DataTypeField]` attributes and are now `partial` classes.

- `ApplicationConfiguration.LoadWithNoValidation` uses `XmlParser`/`IEncodeable.Decode()`. Existing XML config files should remain loadable.
- Browser and session state persistence switched from XML to OPC UA Binary encoding. **Old persisted files cannot be loaded** — delete and re-save.
- `SecuredApplication` uses `SecuredApplicationEncoding` helpers instead of `DataContractSerializer`.

**Change code as follows:**

- Replace `[DataContract(Namespace = ...)]` with `[DataType(Namespace = ...)]` and `[DataMember(...)]` with `[DataTypeField(...)]` on custom configuration subtypes.
- If the old namespace expression references a `Namespaces` constant generated from a model file in the same project, replace it with the URI literal or a `const string` from ordinary source. Same-run generated constants are unavailable while `[DataType]` attributes are analyzed and now produce `MODELGEN021`.
- Add the `partial` keyword to any subclass of these configuration types.
- Custom configuration extension types must implement `IEncodeable` (the `[DataType]` source generator handles this automatically for `partial` classes).
- Code using reflection to inspect `[DataContract]`/`[DataMember]` attributes must switch to `[DataType]`/`[DataTypeField]`.

### TraceConfiguration apply APIs removed

The legacy `TraceConfiguration` application path (`TraceConfiguration.ApplySettings()` and the fluent builder methods `SetOutputFilePath(...)`, `SetDeleteOnLoad(...)`, `SetTraceMasks(...)`) has been removed. Logging/tracing setup must now be done via `ITelemetryContext` and `ILoggerFactory`.

If your startup code used these APIs, remove those calls and configure logging providers directly on your telemetry context instead.

### MinMetadataSamplingInterval replaced by MinSupportedSampleRate

`ServerConfiguration.MinMetadataSamplingInterval` and the fluent
`SetMinMetadataSamplingInterval(int)` builder method have been removed. The
setting was never read by the stack, so it had no effect on the sampling
interval of any monitored item.

The 2.0 replacement is `ServerConfiguration.MinSupportedSampleRate` (a
`double`, in milliseconds), which occupies the same position in the XML
schema and is exposed by the fluent builder as
`SetMinSupportedSampleRate(double)`. Unlike its predecessor, it is applied:
it is published in `Server.ServerCapabilities.MinSupportedSampleRate` and
acts as a server-wide lower bound when the sampling interval of a monitored
item is revised.

```xml
<!-- before -->
<MinMetadataSamplingInterval>1000</MinMetadataSamplingInterval>

<!-- after -->
<MinSupportedSampleRate>1000</MinSupportedSampleRate>
```

```csharp
// before
builder.SetMinMetadataSamplingInterval(1000);

// after
builder.SetMinSupportedSampleRate(1000);
```

The two are **not** equivalent in behaviour. `MinSupportedSampleRate`
defaults to `0`, which keeps the pre-2.0 behaviour of not imposing a
server-wide lower bound. Setting it to a non-zero value changes the
`revisedSamplingInterval` returned to clients for every monitored item
except those on nodes that declare
`MinimumSamplingIntervals.Continuous` (`0`), which report by exception and
are not bound by a sampling rate. See
[Subscriptions.md § Sampling interval revision](../../Subscriptions.md#sampling-interval-revision)
for the full rule.

### Newtonsoft.Json removed from Opc.Ua.Core

`Newtonsoft.Json` is no longer a dependency of `Opc.Ua.Core`. Projects relying on its transitive availability must add an explicit reference:

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
```

### ParseExtension/UpdateExtension signature changed

`ParseExtension<T>()` and `UpdateExtension<T>()` now require `T` to implement `IEncodeable`. New delegate-based overloads were added for custom decoding:

```csharp
// Generic overload (T must implement IEncodeable)
var config = configuration.ParseExtension<MyConfig>();

// Delegate overload for custom decoding
var config = configuration.ParseExtension<MyConfig>(
    new XmlQualifiedName("MyConfig", myNamespace),
    decoder => { var c = new MyConfig(); c.Decode(decoder); return c; });
```

### ExtensionObject array helpers changed

`ExtensionObject.ToArray(object, Type)` and `ToList<T>(object)` removed. Use `extensionObjects.GetStructuresOf<T>()` or `ExtensionObject.ToArray<T>(ArrayOf<ExtensionObject>)`.

### IJsonEncodeable interface removed

The `IJsonEncodeable` interface and the entire "Default JSON Encoding" infrastructure have been removed. OPC UA JSON encoding is handled by the `JsonEncoder`/`JsonDecoder` classes which do not require per-type encoding node IDs — those classes are unaffected by this change.

**Migration steps:**

1. Remove `IJsonEncodeable` from any custom class that implements it:

    ```diff
    - public class MyType : IEncodeable, IJsonEncodeable
    + public class MyType : IEncodeable
    ```

2. Remove the `JsonEncodingId` property from those classes:

    ```diff
    - public ExpandedNodeId JsonEncodingId => ...;
    ```

## Session and Browser State Persistence

**Breaking Change**: Persistence switched from `DataContractSerializer` XML to `IEncoder` and `IDecoder`. `BrowserState`, `SessionState`, `SessionOptions`, `SubscriptionState`, and `MonitoredItemState` are annotated with `[DataType]` and use the standard `Encode`/`Decode` methods generated by the source generator.

To register the state types with the encodeable factory:

```csharp
context.Factory.Builder.AddOpcUaClientDataTypes();
```

> The encoding format for session state has changed. Existing persisted session state files **cannot** be loaded by the new `SessionConfiguration.Create()` method. Handle restore failures and re-persist the new session state.

---

**See also**

- Related: [packages.md](packages.md), [certificates.md](certificates.md), [identity.md](identity.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.
