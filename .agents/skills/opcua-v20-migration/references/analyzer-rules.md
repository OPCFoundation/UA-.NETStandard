# UA00xx + MIG01 — full analyzer / generator rule reference

The 26 implemented rules through `UA0030` below ship as `DiagnosticAnalyzer`
types in `Opc.Ua.MigrationAnalyzer.dll` (excluding the unused IDs `UA0013`,
`UA0016`, and `UA0017`, plus the shim-only marker `UA0029`). `MIG01` comes from
the `MigrationGenerator`. The companion `CodeFixProvider` types, when
available, live in `Opc.Ua.MigrationAnalyzer.CodeFixer.dll`.

Apply all auto-fixable rules in one shot via the
[`scripts/apply-codefixes.ps1`](../scripts/apply-codefixes.ps1) wrapper or
`dotnet format analyzers --diagnostics UA0002 UA0003 … --severity warn`.

---

## UA0001 — `Utils.Trace` / `Utils.LogX` → `ILogger`

| | |
|---|---|
| **Default severity** | Info |
| **Auto-fix** | No |
| **Why** | 2.0 routes logging through `Microsoft.Extensions.Logging.ILogger` instances obtained from `ITelemetryContext.CreateLogger<T>()`. The global static `Utils.Trace` / `Utils.LogX` helpers are obsolete. |

```csharp
// Before
Utils.Trace("Loaded {0} items in {1}ms", count, elapsed);
Utils.LogError(ex, "Failed to connect to {0}", endpointUrl);

// After
private readonly ILogger _logger;
public MyService(ITelemetryContext telemetry)
{
    _logger = telemetry.CreateLogger<MyService>();
}
_logger.LogInformation("Loaded {Count} items in {Elapsed}ms", count, elapsed);
_logger.LogError(ex, "Failed to connect to {EndpointUrl}", endpointUrl);
```

Manual because (a) you choose the `LogLevel`, (b) you choose the message
template's structured fields, (c) you need to plumb `ITelemetryContext` through
the call chain.

---

## UA0002 — Removed `<Type>Collection` wrappers → `List<T>` / `ArrayOf<T>`

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Always rewrites to `List<TElement>` |
| **Why** | 2.0 removed every model-compiler-emitted `<Type>Collection` wrapper. The mechanical fixer uses `List<T>` so collection initializers and mutations keep compiling. Review API boundaries afterward and convert to `ArrayOf<T>` where the 2.0 API expects immutable OPC UA collections. |

```csharp
// Before
var items = new Int32Collection { 1, 2, 3 };
NodeIdCollection nodes = await session.BrowseAsync(...);

// After (mutable)
var items = new List<int> { 1, 2, 3 };
// Manual follow-up at a read-only OPC UA API boundary
ArrayOf<NodeId> nodes = await session.BrowseAsync(...);
```

The source generator (see [`source-generator.md`](source-generator.md)) emits
`public sealed [Obsolete] class <Name>Collection : List<TElement>` for every
unresolved reference, so `CS0246` becomes a `CS0618` `[Obsolete]` warning plus
this `UA0002` diagnostic.

---

## UA0003 — `x == null` on now-struct built-in types → `x.IsNull`

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Rewrites `== null` / `!= null` to `.IsNull` / `!.IsNull` (or `IsNullOrEmpty` for `LocalizedText`) |
| **Why** | `Variant`, `TypeInfo`, `NodeId`, `ExpandedNodeId`, `QualifiedName`, `LocalizedText`, `ExtensionObject`, `StatusCode` are `readonly struct` in 2.0. Comparing to `null` is misleading (boxes the struct). |

```csharp
// Before
if (nodeId == null) return;
if (qualifiedName != null) Process(qualifiedName);

// After
if (nodeId.IsNull) return;
if (!qualifiedName.IsNull) Process(qualifiedName);
```

---

## UA0004 — Null-conditional `?.` on now-struct types → direct access

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Drops the `?` (use `.IsNull` guard upfront if needed) |
| **Why** | `?.` is meaningless on a value type. |

```csharp
// Before
var ns = nodeId?.NamespaceIndex;

// After
var ns = nodeId.NamespaceIndex;
// or, if still need the guard:
ushort ns = nodeId.IsNull ? (ushort)0 : nodeId.NamespaceIndex;
```

---

## UA0005 — `byte[]` where `ByteString` is expected → `.ToByteString()`

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Appends `.ToByteString()` extension call |
| **Why** | `Opc.Ua.ByteString` is the new representation of the OPC UA `ByteString` built-in type, distinct from `ArrayOf<byte>` and `byte[]`. `byte[]` does **not** implicitly convert. |

```csharp
// Before
token.ServerNonce = nonceBytes;     // byte[] → ByteString expected

// After
token.ServerNonce = nonceBytes.ToByteString();
// or
token.ServerNonce = ByteString.From(nonceBytes);
```

---

## UA0006 — Obsolete `Variant` constructors → `Variant.From(...)`

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Rewrites `new Variant(arg)` to `Variant.From(arg)` |
| **Why** | The non-generic `Variant(object)` / `Variant(DateTime)` / `Variant(Guid)` / `Variant(byte[])` ctors box / lose the value's type information. `Variant.From<T>(T)` preserves it. |

```csharp
// Before
var v = new Variant(myDateTime);
var w = new Variant((object)42);

// After
var v = Variant.From(myDateTime);   // also auto-promotes DateTime → DateTimeUtc
var w = Variant.From(42);
```

---

## UA0007 — `new NodeId(string)` / `new ExpandedNodeId(string)` → `Parse`

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Rewrites to the `.Parse(...)` static factory |
| **Why** | The string-taking ctors are obsolete in favour of the explicit `Parse` factory; the obsolete ctors still work today but will be removed in 2.1+. |

```csharp
// Before
NodeId n = new NodeId("ns=2;s=MyNode");

// After
NodeId n = NodeId.Parse("ns=2;s=MyNode");
```

---

## UA0008 — `Session.Call(..., params object[])` → wrap with `Variant.From`

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Wraps each variadic argument with `Variant.From(...)` |
| **Why** | `Session.Call` / `Session.CallAsync` now takes `params Variant[]`, not `params object[]`. The `object` overload still exists but is `[Obsolete]`. |

```csharp
// Before
session.Call(objectId, methodId, "name", 42, true);

// After
session.Call(objectId, methodId, Variant.From("name"), Variant.From(42), Variant.From(true));
```

---

## UA0009 — `[DataContract]` / `[DataMember]` on config extensions → `[DataType]` / `[DataTypeField]`

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Rewrites the attribute pair |
| **Why** | The XML-config extension-point attribute pair changed in 2.0 to make NativeAOT-safe source generation possible. |

```csharp
// Before
[DataContract(Namespace = "http://acme.com/config")]
public class AcmeConfig
{
    [DataMember(Order = 1)] public string Setting1 { get; set; }
}

// After
[DataType(Namespace = "http://acme.com/config")]
public class AcmeConfig
{
    [DataTypeField(Order = 1)] public string Setting1 { get; set; }
}
```

---

## UA0010 — `using` / `Dispose` on cert / identity types → drop disposable

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Removes the `using` keyword / `Dispose()` call |
| **Why** | `CertificateIdentifier`, `UserIdentity`, `IUserIdentityTokenHandler` are no longer `IDisposable` in 2.0 — they don't own unmanaged resources. The disposable handlers (`X509IdentityTokenHandler`, etc.) returned by `token.AsTokenHandler()` *are* still disposable. |

```csharp
// Before
using var cert = new CertificateIdentifier(...);
using var user = new UserIdentity(...);

// After
var cert = new CertificateIdentifier(...);
var user = new UserIdentity(...);
```

---

## UA0011 — Sync `IUserIdentityTokenHandler.{Encrypt,Decrypt,Sign,Verify}` → `*Async`

| | |
|---|---|
| **Default severity** | Info |
| **Auto-fix** | No |
| **Why** | The sync methods are shimmed in `Opc.Ua.MigrationAnalyzer.Core` via `Task.Run(...).GetAwaiter().GetResult()` so 1.5.378 call sites continue to compile, but they are a migration aid only. Promote your call chain to `async`/`await` before production. |

See [`runtime-shim.md`](runtime-shim.md) for the sync-over-async caveat in
detail.

---

## UA0012 — `CertificateFactory.*` static helpers → instance methods

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Rewrites the receiver to `DefaultCertificateFactory.Instance` |
| **Why** | 2.0 moved certificate creation behind the injectable `ICertificateFactory` contract. `DefaultCertificateFactory.Instance` is the built-in fallback when no application-specific factory is available. |

```csharp
// Before
var cert = CertificateFactory.CreateCertificate(...).CreateForRSA();

// After
var cert = DefaultCertificateFactory.Instance
    .CreateCertificate(...)
    .CreateForRSA();
```

---

## UA0014 — `DataValue.IsGood(dv)` static → `dv.IsGood` property

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Rewrites the static call to a property access |
| **Why** | The static helper still exists but is `[Obsolete]`; the instance property is the canonical 2.0 form. |

```csharp
// Before
if (DataValue.IsGood(dv)) Process(dv);

// After
if (dv.IsGood) Process(dv);
```

---

## UA0015 — Sync / APM members on GDS / LDS clients → `*Async`

| | |
|---|---|
| **Default severity** | Info |
| **Auto-fix** | No |
| **Why** | Same shape as UA0011: shim ships sync + APM wrappers so 1.5.378 call sites still compile, but production code should be `async`/`await` only. |

---

## UA0018 — `CertificateIdentifier.Certificate` getter → `CertificateIdentifierResolver.ResolveAsync`

| | |
|---|---|
| **Default severity** | Info |
| **Auto-fix** | No |
| **Why** | The 1.5.378 sync `Certificate` getter blocked on disk / cert-store I/O. 2.0 resolves an identifier asynchronously with explicit registry, private-key, application-URI, telemetry, and cancellation inputs. The migration is structural — reshape the caller to be async and dispose the returned ref-counted `Certificate`. |

```csharp
// Before
var cert = ci.Certificate;

// After
using Certificate? cert = await CertificateIdentifierResolver.ResolveAsync(
    ci,
    registry: registry,
    needPrivateKey: true,
    applicationUri: applicationUri,
    telemetry: telemetry,
    ct: ct).ConfigureAwait(false);
```

---

## UA0019 — `new DataValue(StatusCode[, ts])` → `DataValue.FromStatusCode`

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Rewrites to `DataValue.FromStatusCode(sc[, serverTimestamp])` |
| **Why** | The named factory avoids constructor ambiguity with numeric types while preserving the optional server timestamp. |

```csharp
// Before
var dv = new DataValue(StatusCodes.Good, DateTime.UtcNow);

// After
var dv = DataValue.FromStatusCode(StatusCodes.Good, DateTimeUtc.Now);
```

---

## UA0020 — `EncodeableFactory.GlobalFactory` / `Create()` → `ServiceMessageContext.Factory` / `Fork()`

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Rewrites `factory.Create()` to `factory.Fork()`. **GlobalFactory does NOT auto-fix** — the replacement (`ServiceMessageContext.Factory`) requires a context instance the analyzer can't conjure. |
| **Why** | `EncodeableFactory.GlobalFactory` is a process-singleton anti-pattern that doesn't compose well with multi-tenant servers or request-scoped contexts. `ServiceMessageContext` carries a per-context factory you `Fork()` to derive child factories. |

```csharp
// Before
var f = EncodeableFactory.GlobalFactory;     // process singleton
var child = f.Create();                       // shallow copy

// After
var f = serverContext.MessageContext.Factory; // request-scoped
var child = f.Fork();                          // explicit "branch from this"
```

---

## UA0021 — `CertificateValidator` / `CertificateValidationEventArgs` (structural)

| | |
|---|---|
| **Default severity** | Info |
| **Auto-fix** | No (structural redesign) |
| **Why** | 2.0 replaces the event-based per-error accept handler (`CertificateValidator.CertificateValidation += (s, e) => e.Accept = …`) with a return-value model (`ICertificateValidatorEx.ValidateAsync(...)` returns a `CertificateValidationResult`). Set the manager's global `AcceptError` callback, or pass `CertificateValidationOptions.AcceptError` to one validation call. |

```csharp
// Before
config.CertificateValidator.CertificateValidation += (s, e) => {
    if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
        e.Accept = true;
};

// After — global policy for this manager
config.CertificateManager.AcceptError = (cert, error) =>
    error.StatusCode == StatusCodes.BadCertificateUntrusted;

// Or create CertificateValidationOptions with AcceptError and pass it
// to the applicable ValidateAsync call for per-call policy.
```

See [`stack-migration/certificates.md`](stack-migration/certificates.md)
§"Certificate Manager and segregated interfaces" for the structural model in full.

---

## UA0022 — `config.CertificateValidator` / `server.CertificateValidator` → `.CertificateManager`

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Rewrites property access from `.CertificateValidator` to `.CertificateManager` |
| **Why** | The property rename that goes with UA0021's structural rename. The new property returns `ICertificateManager`, not `CertificateValidator`. |

```csharp
// Before
var v = config.CertificateValidator;

// After
var m = config.CertificateManager;
```

---

## UA0023 — Legacy PubSub top-level API → builder and DI APIs

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | No (application lifecycle and DI design require judgement) |
| **Why** | 2.0 removes or obsoletes `UaPubSubApplication.Create*` and the legacy `IUaPubSubConnection`, `UaPubSubConnection`, `IUaPublisher`, `UaPublisher`, `IUaPubSubDataStore`, `UaPubSubDataStore`, and `UaPubSubConfigurator` surface. `IUaPubSubDataStore` remains temporarily as an obsolete bridge. |

```csharp
// Before
UaPubSubApplication app = UaPubSubApplication.Create("publisher.xml");
app.Start();

// After (using the application's ITelemetryContext)
var builder = new PubSubApplicationBuilder(telemetry)
    .UseConfigurationFile("publisher.xml");
await using IPubSubApplication app = await builder.BuildAndStartAsync();
```

Use `PubSubApplicationBuilder`, or call `AddPubSub(...)` on `IOpcUaBuilder` and
configure `AddUdpTransport()` / `AddMqttTransport()` on the callback's
`IPubSubBuilder`.
See the bundled
[`pubsub.md`](stack-migration/pubsub.md)
guide.

---

## UA0024 — Exposed diagnostics locks → owner-side update/read methods

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | No (the lock body must be reshaped into a callback safely) |
| **Why** | `IServerInternal`, `ISession`, and `ISubscription` no longer expose `DiagnosticsLock` / `DiagnosticsWriteLock`. The owner now performs synchronization. |

```csharp
// Before
lock (server.DiagnosticsLock)
{
    server.ServerDiagnostics.RejectedSessionCount++;
}

// After
server.UpdateServerDiagnostics(
    diagnostics => diagnostics.RejectedSessionCount++);
```

Use `UpdateDiagnostics(...)` for session/subscription writes and
`ReadDiagnostics(...)` for projections. Do not let the diagnostics object
escape the callback. See the canonical
[diagnostics-lock guidance](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#migrating-code-that-used-the-exposed-diagnostics-locks).

---

## UA0025 — `ILocalNode.DataLock` / `Node.DataLock` → owner-controlled synchronization

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | No (the required atomic boundary is application-specific) |
| **Why** | A node guards its own state and no longer exposes its synchronization root. |

```csharp
// Before
lock (node.DataLock)
{
    value = node.Value;
}

// After — a single node operation is already synchronized
value = node.Value;
```

For an atomic operation spanning multiple calls, use a
`System.Threading.Lock` owned by the calling component. See the canonical
[`ILocalNode.DataLock` guidance](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#migrating-code-that-used-ilocalnodedatalock).

---

## UA0026 — `BaseVariableValue.Lock` → caller-owned `System.Threading.Lock`

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | No (the correct owner of the critical section is contextual) |
| **Why** | `BaseVariableValue` no longer hands its lock to callers. A component needing shared atomicity passes a lock it owns to the constructor; derived value classes use `EnterLock()` / `ExitLock()`. |

```csharp
EnterLock();
try
{
    // Read or update the derived value fields.
}
finally
{
    ExitLock();
}
```

See the canonical
[`BaseVariableValue.Lock` guidance](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#migrating-code-that-used-basevariablevaluelock).

---

## UA0027 — `NodeBrowser.DataLock` → single-consumer browser access

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | No (the analyzer cannot prove the lock body is safe to unwrap) |
| **Why** | A browser belongs to one consumer and no longer exposes a lock. Its owner serializes continuation-point use. |

```csharp
// Before
lock (DataLock)
{
    return base.Next();
}

// After
return base.Next();
```

Custom `CreateBrowser` overrides that populate a browser directly should use
`PopulateBrowserSynchronized`. See the bundled
[`node-states.md`](stack-migration/node-states.md)
guide.

---

## UA0028 — `ApplicationConfiguration.PropertiesLock` → concurrent properties APIs

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | No (multi-operation critical sections need manual review) |
| **Why** | `Properties` synchronizes individual operations internally, so the dictionary is no longer exposed as a lock. |

```csharp
// Before
lock (configuration.PropertiesLock)
{
    configuration.Properties["MyKey"] = value;
}

// After
configuration.Properties["MyKey"] = value;
```

Use `GetOrAddProperty(...)` for atomic get-or-add behavior. See the canonical
[`PropertiesLock` guidance](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#migrating-code-that-used-applicationconfigurationpropertieslock).

---

## UA0029 — `SecurityPolicies` lookup/crypto statics → `ISecurityPolicyRegistry`

| | |
|---|---|
| **Current status** | Runtime-shim/manual-migration marker; no active `DiagnosticAnalyzer` reports `UA0029` |
| **Signal** | Calls supplied by the migration shim produce compiler `[Obsolete]` warnings (`CS0618`) whose messages reference `UA0029` |
| **Auto-fix** | No |
| **Why** | Lookup and cryptography operations depend on the application's registered policy set and its logger, so they moved from the constants class to the registry that owns that state. |

```csharp
// Before
string? uri = SecurityPolicies.GetUri("Basic256Sha256");

// After — preferred when the application container is available
public sealed class MyService(ISecurityPolicyRegistry policies)
{
    public string? Uri => policies.GetUri("Basic256Sha256");
}

// Fallback when no container is in scope
string? uri = SecurityPolicies.Default.GetUri("Basic256Sha256");
```

`SecurityPolicies` still owns the policy URI constants. For `Encrypt` and
`Decrypt`, call the registry instance and remove the old `ILogger` argument.

---

## UA0030 — server subscription publish pipeline became internal

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | No |
| **Why** | The publishing state machine must be driven by `SubscriptionManager` and the server publish queue. Direct calls could consume unseen notifications, advance sequence numbers, or release a subscription its session still owned. |

Remove calls to deleted no-op members such as `ItemReadyToPublish` and
`ItemNotificationsAvailable`. Route acknowledgements, publishing, republishing,
and transfers through the corresponding service or `ISubscriptionManager` API.
Custom implementations must derive from `Subscription`; do not implement
`ISubscription` directly to recreate the old pipeline.

See the canonical
[`UA0030` migration guidance](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0030).

---

## MIG01 — generator can't resolve element type for `<Foo>Collection`

| | |
|---|---|
| **Default severity** | Warning |
| **Source** | `Opc.Ua.MigrationAnalyzer.Generator` |
| **Auto-fix** | No |
| **Why** | The source generator needs one source-declared type matching the short name (e.g. `Foo` for `FooCollection`), or a metadata type named exactly `System.<Type>` / `Opc.Ua.<Type>`. Unsupported metadata, zero source matches, or > 1 source matches → MIG01. |

**Resolution steps:**

1. **Zero source matches:** migrate the wrapper reference manually to
   `List<global::Namespace.Foo>` / `ArrayOf<global::Namespace.Foo>`, using the
   intended element type.
2. **Multiple source matches:** choose the intended fully qualified element
   type in the manual replacement. Qualifying the legacy wrapper does not
   disambiguate the generator's element lookup.
3. **`Foo` only exists in dependency metadata without the exact full name
   `System.Foo` or `Opc.Ua.Foo`:** adding or importing the dependency does not
   extend the lookup. Migrate manually or define the legacy wrapper class
   explicitly in consumer source.
4. **`Foo` no longer exists:** replace the stale wrapper with `List<NewFoo>` /
   `ArrayOf<NewFoo>` using the replacement element type.

See [`source-generator.md`](source-generator.md) for the generator pipeline.

---

## Suppression recipes

For TreatWarningsAsErrors consumers, see
[`assets/Directory.Build.targets.example.xml`](../assets/Directory.Build.targets.example.xml).

For one-off in-source suppression of a single line (avoid this unless you
genuinely cannot migrate now):

```csharp
#pragma warning disable UA0008 // Wrap Session.Call arguments with Variant.From
session.Call(objectId, methodId, "legacy");
#pragma warning restore UA0008
```

For project-wide severity overrides, use `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.UA0001.severity = none      # silence UA0001 entirely
dotnet_diagnostic.UA0008.severity = error     # promote UA0008 to error
```
