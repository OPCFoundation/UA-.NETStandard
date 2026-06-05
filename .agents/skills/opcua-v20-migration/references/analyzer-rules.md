# UA00xx + MIG01 — full analyzer / generator rule reference

Each rule documented below ships as a `DiagnosticAnalyzer` in
`Opc.Ua.MigrationAnalyzer.dll` (or the `MigrationGenerator` for `MIG01`). The
companion `CodeFixProvider` (when available) lives in
`Opc.Ua.MigrationAnalyzer.CodeFixer.dll`.

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
| **Auto-fix** | ✅ Rewrites to `List<TElement>` (mutable) or `ArrayOf<TElement>` (read-only) based on usage |
| **Why** | 2.0 removed every model-compiler-emitted `<Type>Collection` wrapper. Mutable storage goes to `List<T>`; read-only consumers (which is most OPC UA APIs) go to `ArrayOf<T>`. |

```csharp
// Before
var items = new Int32Collection { 1, 2, 3 };
NodeIdCollection nodes = await session.BrowseAsync(...);

// After (mutable)
var items = new List<int> { 1, 2, 3 };
// After (read-only)
ArrayOf<NodeId> nodes = await session.BrowseAsync(...);
```

The source generator (see [`source-generator.md`](source-generator.md)) emits
`internal sealed [Obsolete] class <Name>Collection : List<TElement>` for every
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
| **Auto-fix** | ✅ Rewrites to call the instance method on a default-constructed factory |
| **Why** | 2.0 made `CertificateFactory` an instance type so multiple factories (with different defaults / providers) can coexist. |

```csharp
// Before
var cert = CertificateFactory.CreateCertificate(...).CreateForRSA();

// After
var factory = new CertificateFactory();
var cert = factory.CreateCertificate(...).CreateForRSA();
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

## UA0018 — `CertificateIdentifier.Certificate` getter → `LoadCertificate2Async`

| | |
|---|---|
| **Default severity** | Info |
| **Auto-fix** | No |
| **Why** | The 1.5.378 sync `Certificate` getter blocked on disk / cert-store I/O. 2.0 surfaces an async `LoadCertificate2Async` instead. The migration is structural — you reshape the caller to be async. |

```csharp
// Before
var cert = ci.Certificate;

// After
var cert = await ci.LoadCertificate2Async(applicationCertificate: true, ct).ConfigureAwait(false);
```

---

## UA0019 — `new DataValue(StatusCode[, ts])` → object initializer

| | |
|---|---|
| **Default severity** | Warning |
| **Auto-fix** | ✅ Rewrites `new DataValue(sc, ts)` to `new DataValue { StatusCode = sc, SourceTimestamp = ts }` |
| **Why** | Reduces ctor-overload combinatorics in 2.0; the object-initializer form scales to the now-richer property surface. |

```csharp
// Before
var dv = new DataValue(StatusCodes.Good, DateTime.UtcNow);

// After
var dv = new DataValue { StatusCode = StatusCodes.Good, SourceTimestamp = DateTimeUtc.Now };
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
| **Why** | 2.0 replaces the event-based per-error accept handler (`CertificateValidator.CertificateValidation += (s, e) => e.Accept = …`) with a return-value model (`ICertificateValidatorEx.ValidateAsync(...)` returns a `CertificateValidationResult`). Per-error accept logic moves to `CertificateValidationOptions.AcceptError`. |

```csharp
// Before
config.CertificateValidator.CertificateValidation += (s, e) => {
    if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
        e.Accept = true;
};

// After
config.CertificateManager.Options.AcceptError = (cert, error) =>
    error.StatusCode == StatusCodes.BadCertificateUntrusted;
// or implement ICertificateValidatorEx.ValidateAsync for full control.
```

See `Docs/MigrationGuide.md` §"Certificate Manager and segregated interfaces"
for the structural model in full.

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

## MIG01 — generator can't resolve element type for `<Foo>Collection`

| | |
|---|---|
| **Default severity** | Warning |
| **Source** | `Opc.Ua.MigrationAnalyzer.Generator` |
| **Auto-fix** | No |
| **Why** | The source generator's element-type lookup needs exactly one `INamedTypeSymbol` matching the short name (e.g. `Foo` for `FooCollection`). Zero or > 1 matches → MIG01. |

**Resolution steps:**

1. **Most common cause:** missing `using` for the namespace that defines `Foo`.
   Add the `using` to the file containing the `<Foo>Collection` reference.
2. **Multiple candidates:** the consumer compilation has two types named `Foo`
   in different namespaces. Fully-qualify the wrapper reference or rename one of
   the conflicting `Foo` types.
3. **`Foo` lives in an unreferenced NuGet:** add the missing package
   reference; the generator runs in the consumer's compilation context and can
   only see what `dotnet restore` brought in.
4. **`Foo` doesn't exist anywhere yet:** migrate the call site manually to
   `List<…>` / `ArrayOf<…>` of the actual element type you intended.

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
