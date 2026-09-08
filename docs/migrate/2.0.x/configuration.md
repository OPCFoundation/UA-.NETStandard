# Configuration and State Persistence

> **When to read this:** Read this for `ApplicationConfiguration` changes, moving server customization to dependency injection, the removed Data-Contract serializer, Newtonsoft removal from `Opc.Ua.Core`, the new `ParseExtension` / `UpdateExtension` signature, and session / browser state persistence.

## Configuration

### Data Contract Serializer support removed

Because **Data Contract serialization** is not AOT compliant and does not support trimming, all use of `DataContract` in the configuration has been removed. Instead, the source generator enables generating *IEncodeable* implementations using the `DataType` and `DataTypeField` attributes which are now consequently used for all configuration. Because the configuration is now `IEncodeable` the existing encoders and decoders (in particular the new `XmlParser` which parses Xml and allows out of order fields) compliant with Part 6 can be used to serialize and deserialize all configuration and configuration extensions.

> Generated Data types still support DataContract based serialization, however, consider this a deprecated feature.

All configuration DTO classes (`ApplicationConfiguration`, `ServerConfiguration`, `TraceConfiguration`, `TransportConfiguration`, `ServerSecurityPolicy`, `OAuth2ServerSettings`, `OAuth2Credential`, `GlobalDiscoveryServerConfiguration`, `CertificateGroupConfiguration`, `BrowserOptions`, etc.) migrated from `[DataContract]`/`[DataMember]` to source-generated `[DataType]`/`[DataTypeField]` attributes and are now `partial` classes.

- `ApplicationConfiguration.LoadWithNoValidation` uses `XmlParser`/`IEncodeable.Decode()`. Existing XML config files should remain loadable.
  Applications moving to the dependency-injection surface can pass such a file directly to `services.AddOpcUa().AddServer("MyServer.Config.xml")` or `.AddClient("MyClient.Config.xml")` and keep every setting from it — see [Migrating with an existing configuration XML file](../../DependencyInjection.md#migrating-with-an-existing-configuration-xml-file) and [Client: using an existing configuration XML file](../../DependencyInjection.md#client-using-an-existing-configuration-xml-file).
- Browser and session state persistence switched from XML to OPC UA Binary encoding. **Old persisted files cannot be loaded** — delete and re-save.
- `SecuredApplication` uses `SecuredApplicationEncoding` helpers instead of `DataContractSerializer`.

**Change code as follows:**

- Replace `[DataContract(Namespace = ...)]` with `[DataType(Namespace = ...)]` and `[DataMember(...)]` with `[DataTypeField(...)]` on custom configuration subtypes.
- If the old namespace expression references a `Namespaces` constant generated from a model file in the same project, replace it with the URI literal or a `const string` from ordinary source. Same-run generated constants are unavailable while `[DataType]` attributes are analyzed and now produce `MODELGEN021`.
- Add the `partial` keyword to any subclass of these configuration types.
- Custom configuration extension types must implement `IEncodeable` (the `[DataType]` source generator handles this automatically for `partial` classes).
- Code using reflection to inspect `[DataContract]`/`[DataMember]` attributes must switch to `[DataType]`/`[DataTypeField]`.

### Moving server customization to dependency injection

Applications migrating from 1.5.378 to 2.0 can replace routine server
overrides with fluent registration on `IOpcUaServerBuilder`. These
extensions are in `Microsoft.Extensions.DependencyInjection`; the
standard hosted path does not require an application server subclass.

| 1.5.378 integration pattern | 2.0 hosted replacement |
|----------------------------|------------------------|
| Override `LoadServerProperties` for product and build information | `ConfigureServerProperties(Action<Opc.Ua.ServerProperties>)`. |
| Override `CreateResourceManager` to install translations | `ConfigureResources(Action<Opc.Ua.Server.ResourceManager>)`, or the overload also receiving `IServiceProvider`. |
| Attach `SessionManager.ImpersonateUser` for authentication | Register an `IUserTokenAuthenticator` by type, instance, or a factory receiving `IServiceProvider` and `ICertificateValidatorEx?`. See [identity migration](identity.md#user-identity-providers). |
| Override `OnServerStarted` for application initialization | `AddStartupTask<TTask>()` where `TTask : class, IServerStartupTask`, or `AddStartupTask(Func<IServiceProvider, IServerContext, CancellationToken, ValueTask>)`. |
| Override `CreateMasterNodeManager` to assemble application factories | `AddNodeManager<TFactory>()`, the existing legacy `AddSyncNodeManager<TFactory>()`, instance overloads, or `AddNodeManagers(...)`. |
| Select a custom reverse-connect server solely for outbound connections | The regular DI server already derives from `ReverseConnectServer`; use `AddReverseConnect(...)` or loaded `ServerConfiguration.ReverseConnect`. |

An existing XML configuration remains the source of application identity
and endpoint settings while runtime customization moves to the builder:

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddOpcUa()
    .AddServer("PlantServer.Config.xml")
    .ConfigureServerProperties(properties =>
    {
        properties.ManufacturerName = "Example Automation";
        properties.SoftwareVersion = "2.0.0";
        properties.BuildNumber = "42";
        properties.BuildDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    })
    .AddNodeManagers((sp, configuration) =>
    [
        ActivatorUtilities.CreateInstance<PlantNodeManagerFactory>(sp, configuration)
    ])
    .ConfigureResources(resources =>
        resources.Add("DeviceUnavailable", "en-US", "The device is unavailable."));
```

`PlantNodeManagerFactory` is an application-defined
`IAsyncNodeManagerFactory` whose constructor accepts the effective
`ApplicationConfiguration` and other DI dependencies. Generic factory
registrations are resolved lazily after configuration is loaded.
For configuration-dependent constructors, use the explicit
`AddNodeManagers(Func<IServiceProvider, ApplicationConfiguration, ArrayOf<IAsyncNodeManagerFactory>>)`
callback as above, not a global `ApplicationConfiguration` service.
Its result can contain zero or many factories. Already-created async
and legacy factories are accepted by `AddNodeManager(IAsyncNodeManagerFactory)`
and `AddNodeManager(INodeManagerFactory)`.

Observe the following precedence and lifecycle rules:

- XML and stream configuration are authoritative. Configuration-building
  options, including `AddReverseConnect(...)`, do not overwrite loaded
  settings. Use `ConfigureLoadedConfiguration` or the optional
  `AddServer(pathOrStream, callback)` callback for those overrides.
  Runtime registrations such as authenticators, factories, startup
  tasks, metadata, resources, and alias settings still apply.
- A directly registered `Opc.Ua.ServerProperties` takes precedence over
  `IOptions<Opc.Ua.ServerProperties>` configured by
  `ConfigureServerProperties`. Unspecified `ProductName` and `ProductUri`
  use the effective application's name and product URI.
  `SoftwareVersion` and `BuildNumber` fall back to the existing **stack**
  version helpers, not the publisher's version. Configure the publisher's
  `ManufacturerName` and `BuildDate` explicitly.
- Resource callbacks run after the default status text is loaded. The
  server retains and owns its resource manager; there is no live
  `Opc.Ua.Server.ResourceManager` service to inject.
- Typed startup-task registration is idempotent; separate delegates are
  distinct. Tasks run sequentially once after server startup, and failure
  or cancellation aborts hosted startup and triggers cleanup.
  Use existing typed lookups such as
  `context.FindNodeManagers<IDiagnosticsNodeManager>().Single()` and
  await `GetDefaultHistoryCapabilitiesAsync(...)`; no new
  `IServerContext` members are required. Prefer the existing historian
  capabilities rollup when it already describes the provider's support.
- For Part 17 aliases, register stores before address-space startup with
  `AddAliasNameStore(...)` or `AddAliasNameStoreRegistry(...)`.
  `ConfigureAliasNames(Action<AliasNameServerOptions>)` opts the normal
  `ConfigurationNodeManager` into materializing standard-category
  aliases and optional capabilities. `AliasNameServerOptions` lives in
  `Opc.Ua.Server.AliasNames` and defaults `MaterializeAliasNodes` to
  `false`; the custom `AliasNameNodeManagerOptions` default remains
  `true`. Browse nodes are a startup snapshot: later store mutations
  change `FindAlias` and `LastChange`, not those nodes.

These registrations are additive and introduce no new dependencies.
Manual server construction remains supported, and ordinary
`StandardServer` behavior is unchanged. If retaining a custom
`AddServer<TServer>()`, DI-only metadata, resource, and alias settings
require `DependencyInjectionStandardServer`; a non-DI custom server
rejects them clearly rather than ignoring them. Existing session,
subscription, and durable-subscription DI hooks coexist with the
reverse-connect base.

See [Dependency Injection](../../DependencyInjection.md#server-feature),
[Alias Names](../../AliasNames.md), and
[Reverse Connect](../../ReverseConnect.md#server-side-dependency-injection)
for the complete current APIs and examples.

### TraceConfiguration apply APIs removed

The legacy `TraceConfiguration` application path (`TraceConfiguration.ApplySettings()` and the fluent builder methods `SetOutputFilePath(...)`, `SetDeleteOnLoad(...)`, `SetTraceMasks(...)`) has been removed. Logging/tracing setup must now be done via `ITelemetryContext` and `ILoggerFactory`.

If your startup code used these APIs, remove those calls and configure logging providers directly on your telemetry context instead.

### MinMetadataSamplingInterval removed, MinSupportedSamplingInterval added

`ServerConfiguration.MinMetadataSamplingInterval` and the fluent
`SetMinMetadataSamplingInterval(int)` builder method have been removed. In
1.5.378 the setting defaulted to `1000` but was never read by the stack, so
whatever value you configured had no effect on any monitored item.

2.0 adds `ServerConfiguration.MinSupportedSamplingInterval` (a `double`, in
milliseconds) in the same position of the XML schema, exposed by the fluent
builder as `SetMinSupportedSamplingInterval(double)`. Unlike its predecessor it
is applied: it is published in `Server.ServerCapabilities.MinSupportedSampleRate`
and acts as a server-wide lower bound when the sampling interval of a monitored
item is revised.

**These are two different settings, not a rename.** Do not carry the old value
across — in 1.5.378 it was inert, so reusing it silently introduces a sampling
floor that changes the `revisedSamplingInterval` your clients receive. Delete
the old element and only add the new one if you actually want that floor:

```xml
<!-- before: had no effect in 1.5.378 -->
<MinMetadataSamplingInterval>1000</MinMetadataSamplingInterval>

<!-- after: simply drop it to keep 1.5.378 behaviour -->

<!-- ...or opt in deliberately, knowing clients will now be revised up -->
<MinSupportedSamplingInterval>1000</MinSupportedSamplingInterval>
```

```csharp
// before: had no effect in 1.5.378
builder.SetMinMetadataSamplingInterval(1000);

// after: drop the call, or opt in deliberately
builder.SetMinSupportedSamplingInterval(1000);
```

Doing nothing is safe. `MinSupportedSamplingInterval` defaults to `0`, which
reproduces 1.5.378 exactly: that release hard-coded
`Server.ServerCapabilities.MinSupportedSampleRate` to `0` and bounded the
revised sampling interval only by the `MinimumSamplingInterval` declared by the
monitored node. Setting a non-zero value raises the `revisedSamplingInterval`
for every monitored item except those on nodes declaring
`MinimumSamplingIntervals.Continuous` (`0`), which report by exception and are
not bound by a sampling interval. See
[NodeManagers.md § Sampling interval revision](../../NodeManagers.md#sampling-interval-revision)
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
