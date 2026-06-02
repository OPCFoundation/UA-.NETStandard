# Migration patterns — categorical playbook

Pattern reference for the manual residuals + the cross-cutting changes the
`UA00xx` analyzers don't fully automate. Apply in the priority order below to
minimize cascading errors. Distilled from
[`.github/agents/opcua-v20-migration.agent.md`](../../../.github/agents/opcua-v20-migration.agent.md);
sync changes back to that file when you edit this one.

> **Priority order summary** — work top-to-bottom; later sections often
> dissolve under earlier fixes:
>
> 1. Source generation (project file changes)
> 2. Null comparisons on now-struct types (UA0003 / UA0004 cover most)
> 3. Collection types (UA0002 + source generator cover most)
> 4. Built-in type replacements (`DateTime` → `DateTimeUtc`, `byte[]` → `ByteString`, `Guid` → `Uuid`)
> 5. `Variant` / `DataValue` / `ExtensionObject` API changes
> 6. Encoder / Decoder signature updates
> 7. `NodeState` and generic `PropertyState<T>` changes
> 8. Server-side `NodeManager` changes
> 9. Client-side `Session` / `Subscription` changes
> 10. User-identity token handler pattern

---

## 1. Source generation (project file changes)

The 2.0 stack uses Roslyn source generators instead of pre-generated code from
ModelCompiler. **ModelCompiler-emitted C# is not supported** — the generator
replaces it.

Remove all pre-generated files (`*.Classes.cs`, `*.Constants.cs`,
`*.DataTypes.cs`, `*.PredefinedNodes.uanodes`, `*.PredefinedNodes.xml`).
Keep only the design files (`.xml` and `.csv`). Update the consumer csproj:

```xml
<PropertyGroup>
  <ModelSourceGeneratorUseAllowSubtypes>true</ModelSourceGeneratorUseAllowSubtypes>
</PropertyGroup>
<ItemGroup>
  <AdditionalFiles Include="Model\MyModelDesign.csv" />
  <AdditionalFiles Include="Model\MyModelDesign.xml" />
</ItemGroup>
```

**New intermediate `Opc.Ua` project.** A new `Opc.Ua` project sits between
`Opc.Ua.Types` and `Opc.Ua.Core`. NuGet consumers are unaffected — keep
referencing `Opc.Ua.Core`. Project-reference consumers must update the chain.

**Schema access.** `*.NodeSet2.xml` embedded zip resources and `*.Types.xsd` /
`*.Types.bsd` embedded resources are gone. Use the new `Schemas.XmlAsStream` /
`Schemas.BinaryAsStream` APIs.

## 2. Immutable value types — null-comparison fixes

The following types changed from **class** (reference type) to **`readonly
struct`** (value type) in 2.0:

| Type | Was | Now |
|---|---|---|
| `Variant` | mutable struct | `readonly struct` |
| `TypeInfo` | class | `readonly struct` (4 bytes) |
| `NodeId` | class | `readonly struct` |
| `ExpandedNodeId` | class | `readonly struct` |
| `QualifiedName` | class | `readonly struct` |
| `LocalizedText` | class | `readonly struct` |
| `ExtensionObject` | class | `readonly struct` |
| `StatusCode` | struct | `readonly struct` (now includes `Symbol`) |

**Null checks** (UA0003 covers most):

```csharp
// Before
if (nodeId == null) …
if (qualifiedName != null) …
if (localizedText == null) …

// After
if (nodeId.IsNull) …
if (!qualifiedName.IsNull) …
if (localizedText.IsNullOrEmpty) …
```

For `ArrayOf<T>`, `MatrixOf<T>`, and `ByteString`:

```csharp
// Before
if (array == null || array.Length == 0) …
// After — .IsEmpty checks both null and empty
if (array.IsEmpty) …
```

**Default values** — `default` or `.Null`:

```csharp
NodeId n = default;          // ≡ NodeId.Null
QualifiedName q = default;   // ≡ QualifiedName.Null
Variant v = default;         // ≡ Variant.Null
```

**Mutation** — setters removed; use `With*` methods that return new instances:

```csharp
// Before — compile error
nodeId.NamespaceIndex = 2;
qualifiedName.Name = "NewName";

// After
nodeId = nodeId.WithNamespaceIndex(2);
qualifiedName = qualifiedName.WithName("NewName");
```

## 3. Collection type migration

All `<Type>Collection` classes have been **removed**. The source generator
(see [`source-generator.md`](source-generator.md)) emits a per-consumer
`internal sealed [Obsolete]` shim so call sites still compile. UA0002 then
rewrites them. Replacement choices:

| Old type | New type (immutable) | New type (mutable) |
|---|---|---|
| `<T>Collection` | `ArrayOf<T>` | `List<T>` |
| `ReadOnlyList<T>` | `ArrayOf<T>` | — |
| `IList<T>` parameters in OPC UA APIs | `ArrayOf<T>` | — |

Use `ArrayOf<T>` when the collection is **never** mutated after creation; use
`List<T>` when items are added/removed, then convert to `ArrayOf<T>` via
`.ToArrayOf()`.

**ArrayOf<T> key surface:**

```csharp
// Construction
ArrayOf<int> a = [1, 2, 3];                  // collection expression
ArrayOf<int> b = default;                    // ArrayOf<int>.Null; IsNull == true
ArrayOf<int> c = ArrayOf<int>.Empty;         // IsEmpty == true, IsNull == false

// Immutable operations (return new instances)
a = a.AddItem(4);
a = a.AddItem(99, index: 2);                 // insert at index
a = a.RemoveItem(4);
a = a.AddItems(b);                           // append another ArrayOf
a += 5;                                       // shorthand for AddItem

// Query
bool has = a.Contains(3);
int idx = a.IndexOf(3);
ArrayOf<int> filtered = a.Filter(x => x > 2);
ArrayOf<string> mapped = a.ConvertAll(x => x.ToString());
ArrayOf<int> sliced = a.SafeSlice(0, 2);

// Access
int val = a[0];
ReadOnlySpan<int> span = a.Span;             // for hot paths
ReadOnlyMemory<int> mem = a.Memory;

// Conversion
List<int> list = a.ToList();
int[] array = a.ToArray();

// CAUTION: cannot enumerate across await boundary (CS4007)
// Fix: convert to list first
foreach (var item in a.ToList()) { await … }
```

**Configuration-types removed wrappers** (no longer have a collection wrapper at
all — go straight to `ArrayOf<T>`): `ServerSecurityPolicyCollection`,
`TransportConfigurationCollection`, `SamplingRateGroupCollection`,
`ReverseConnectClientCollection`, `ReverseConnectClientEndpointCollection`,
`ServerRegistrationCollection`, `CertificateIdentifierCollection`,
`CertificateGroupConfigurationCollection`, `OAuth2ServerSettingsCollection`,
`OAuth2CredentialCollection`.

## 4. Built-in type replacements

### `DateTime` → `DateTimeUtc`

`DateTimeUtc` is the new representation of the OPC UA `DateTime` built-in. It
stores UTC ticks and is always UTC.

```csharp
// Before
DateTime ts = DateTime.UtcNow;
DateTime min = DateTime.MinValue;
if (dv.SourceTimestamp != DateTime.MinValue) …

// After
DateTimeUtc ts = DateTimeUtc.Now;
DateTimeUtc min = DateTimeUtc.MinValue;
if (dv.SourceTimestamp != DateTimeUtc.MinValue) …

// Conversion
DateTime dt = utc.ToDateTime();
DateTimeOffset dto = utc.ToDateTimeOffset();
DateTime local = utc.ToDateTime().ToLocalTime();

// DateTime implicitly converts TO DateTimeUtc
DateTimeUtc x = DateTime.UtcNow;       // OK
// DateTimeUtc does NOT implicitly convert TO DateTime — explicit cast required
DateTime y = (DateTime)x;
```

### `byte[]` → `ByteString`

UA0005 covers the parameter mismatches. The pattern in full:

```csharp
// Before
byte[] nonce = token.ServerNonce;
byte[] combined = Utils.Append(a, b);
byte v = nonce[0];

// After
ByteString nonce = token.ServerNonce;
ByteString combined = ByteString.Combine(a, b);
byte v = nonce.Span[0];                       // index via .Span

// Conversion
ByteString bs = ByteString.From(someBytes);
ByteString bs2 = someBytes.ToByteString();
byte[] arr = bs.ToArray();
ReadOnlySpan<byte> span = bs.Span;             // zero-copy
```

`byte[]` does **not** implicitly convert to `ByteString` (to distinguish from
`ArrayOf<byte>`).

### `Guid` → `Uuid` in Variant contexts

```csharp
// Before
new Variant(Guid.NewGuid())

// After
new Variant((Uuid)Guid.NewGuid())
// or
Variant.From((Uuid)Guid.NewGuid())
```

### `T[]` → `ArrayOf<T>` in Variant cast

```csharp
// Before
string[] names = (string[])variant.Value;

// After
ArrayOf<string> names = (ArrayOf<string>)variant;
// or
variant.TryGet(out ArrayOf<string> names);
```

## 5. Variant / DataValue / ExtensionObject

### `Variant` is now a `readonly struct` (union-based; no boxing for ≤ 8-byte values)

```csharp
// Before — object Value property
object val = variant.Value;
uint num = (uint)variant.Value;
variant.Value = 42;       // ERROR: no setter

// After — type-safe access
uint num = (uint)variant;                  // cast (throws on mismatch)
variant.TryGet(out uint num);             // safe extraction
uint num = variant.GetUInt32();            // returns default on mismatch

// After — construction
Variant v = new Variant(42);
Variant v = 42;                            // implicit
Variant v = Variant.From(42);             // explicit factory

// For IEncodeable structures
Variant v = Variant.FromStructure(myEncodeable);
v.TryGetStructure(out MyType result);

// For enumerations
Variant v = Variant.FromEnumeration(MyEnum.Value);
v.TryGet(out int enumAsInt);

// Boxing (only when truly needed)
object boxed = variant.AsBoxedObject();
```

> **Warning:** do not accidentally box `Variant`. `object f = state.Value;`
> boxes silently. Use `var f = state.Value;` (infers `Variant`) or
> `Variant f = state.Value;`.

### `DataValue`

```csharp
// Before
object val = dv.Value;
dv.Value = something;

// After
Variant val = dv.WrappedValue;      // preferred: type-safe
dv.WrappedValue = someVariant;
// .Value still works but is [Obsolete]

// Timestamps changed from DateTime to DateTimeUtc
DateTimeUtc src = dv.SourceTimestamp;
DateTimeUtc srv = dv.ServerTimestamp;
```

### `ExtensionObject` is now a `readonly struct`; `.Body` is deprecated

```csharp
// Before
object body = eo.Body;
if (body is MyType obj) …
eo.Body = myEncodeable;

// After — type-safe accessors
if (eo.TryGetEncodeable(out IEncodeable enc)) …
if (eo.TryGetEncodeable<MyType>(out MyType obj)) …
if (eo.TryGetBinary(out ByteString binary)) …
if (eo.TryGetJson(out string json)) …
if (eo.TryGetXml(out XmlElement xml)) …

// Construction
var eo = new ExtensionObject(myEncodeable);
var eo = new ExtensionObject(typeId, binaryBody);   // ByteString, not byte[]
```

## 6. Encoders and decoders

### `IEncoder` changes

```csharp
// Before
encoder.WriteDateTime("Timestamp", dt);
encoder.WriteByteString("Data", bytes);
encoder.WriteGuid("Id", guid);
encoder.WriteEncodeable("Value", enc, typeof(MyType));
encoder.WriteEncodeableArray("Items", arr, typeof(MyType));
encoder.WriteEnumerated("Mode", e, typeof(MyEnum));
encoder.EncodeMessage(enc);

// After
encoder.WriteDateTime("Timestamp", dtUtc);
encoder.WriteByteString("Data", byteString);
encoder.WriteGuid("Id", uuid);
encoder.WriteEncodeable("Value", enc);                     // no typeof
encoder.WriteEncodeableArray<MyType>("Items", arrayOf);    // generic
encoder.WriteEnumerated("Mode", e);                        // no typeof
encoder.EncodeMessage<MyType>(enc);                        // generic
```

### `IDecoder` changes

```csharp
// Before
DateTime dt = decoder.ReadDateTime("Timestamp");
byte[] data = decoder.ReadByteString("Data");
IEncodeable obj = decoder.ReadEncodeable("Value", typeof(MyType));
Enum e = decoder.ReadEnumerated("Mode", typeof(MyEnum));
IEncodeable msg = decoder.DecodeMessage(typeof(MyType));

// After
DateTimeUtc dt = decoder.ReadDateTime("Timestamp");
ByteString data = decoder.ReadByteString("Data");
MyType obj = decoder.ReadEncodeable<MyType>("Value");          // generic, typed return
MyEnum e = decoder.ReadEnumerated<MyEnum>("Mode");             // generic, typed return
MyType msg = decoder.DecodeMessage<MyType>();
```

### `ReadArray` / `WriteArray` removed → `ReadVariantValue` / `WriteVariantValue`

```csharp
// Before
encoder.WriteArray("Values", values);
object result = decoder.ReadArray("Values", typeInfo);

// After
encoder.WriteVariantValue("Values", variant);
Variant result = decoder.ReadVariantValue("Values", typeInfo);
```

Collection parameters changed from `IList<T>` / `T[]` / `<T>Collection` to
`ArrayOf<T>`.

## 7. NodeState and generic PropertyState

### `BaseVariableState.Value` is now `Variant`

```csharp
// Before
object val = vs.Value;
vs.Value = something;
DateTime ts = vs.Timestamp;

// After
Variant val = vs.Value;
vs.Value = someVariant;
DateTimeUtc ts = vs.Timestamp;
```

### Callbacks: `object` → `Variant`

```csharp
// Before
state.OnSimpleReadValue  = (ctx, node, ref object value) => …;
state.OnSimpleWriteValue = (ctx, node, ref object value) => …;

// After
state.OnSimpleReadValue  = (ctx, node, ref Variant value) => …;
state.OnSimpleWriteValue = (ctx, node, ref Variant value) => …;
```

### Generic `PropertyState<T>` — builder pattern

| Value-type category | Builder |
|---|---|
| Built-in (int, string, NodeId, …) | `VariantBuilder` |
| `IEncodeable` (structure) | `StructureBuilder<T>` |
| Enum | `EnumBuilder<T>` |

```csharp
// Before
var p = new PropertyState<int>(parent);
var q = new PropertyState<Argument>(parent);

// After
var p = PropertyState<int>.With<VariantBuilder>(parent);
var q = PropertyState<Argument>.With<StructureBuilder<Argument>>(parent);
var r = PropertyState<MyEnum>.With<EnumBuilder<MyEnum>>(parent);
var s = PropertyState<ArrayOf<ExtensionObject>>.With<VariantBuilder>(parent);
var t = PropertyState<MatrixOf<MyStruct>>.With<StructureBuilder<MyStruct>>(parent);
```

**Tip:** use `WrappedValue` instead of `Value` on generic typed states for
direct `Variant` access without the type-check overhead.

### Predefined node processing

Generated nodes are already their actual types — no need to construct a new
"active" node in `AddBehaviorToPredefinedNode`:

```csharp
// Before
protected override NodeState AddBehaviorToPredefinedNode(
    ISystemContext context, NodeState predefinedNode)
{
    var active = new MyTypeState(null);
    active.Create(context, predefinedNode);
    // … attach callbacks
    return active;
}

// After
protected override void AddBehaviorToPredefinedNode(
    ISystemContext context, NodeState node)
{
    if (node is MyTypeState my)
    {
        my.Temperature.OnSimpleWriteValue = OnTemperatureWrite;
        my.FlowRate.OnSimpleWriteValue   = OnFlowRateWrite;
    }
}
```

## 8. Server-side node manager changes

### `INodeManager3` — new interface

```csharp
public interface INodeManager3 : INodeManager2
{
    ServiceResult ValidateEventRolePermissions(
        IEventMonitoredItem monitoredItem,
        IFilterTarget filterTarget);

    ServiceResult ValidateRolePermissions(
        OperationContext operationContext,
        NodeId nodeId,
        PermissionType requestedPermission);
}
```

If your custom node manager implements `INodeManager2`, consider upgrading
to `INodeManager3` for role-based permission validation.

### `AsyncCustomNodeManager` — new async-first base class

For new node managers or significant refactors, prefer `AsyncCustomNodeManager`
over `CustomNodeManager2`:

- All operations return `ValueTask` / `ValueTask<T>`.
- Uses `MonitoredNode2` with `ConcurrentDictionary` (thread-safe by default).
- Automatically wraps itself in `SyncNodeManagerAdapter` for `INodeManager3`
  compatibility.
- Takes `ILogger` in constructor for structured logging.

### `CoreNodeManager` + `DiagnosticsNodeManager` now extend `AsyncCustomNodeManager`

If you previously subclassed either, you inherit the new async base.

## 9. Client-side Session / Subscription

### Collection parameters: nullable `IList<T>?` → non-nullable `ArrayOf<T>`

```csharp
// Before
await session.OpenAsync("MySession", 1000, identity,
    new StringCollection { "en-US" }, true, true, ct);

// After
await session.OpenAsync("MySession", 1000, identity,
    (ArrayOf<string>)["en-US"], true, true, ct);
```

### `Session.Call` with `Variant`

```csharp
// Before
session.Call(objectId, methodId, (object)arg1, (object)arg2);

// After
session.Call(objectId, methodId, (Variant)arg1, (Variant)arg2);
```

UA0008 auto-fixes this with `Variant.From(...)` wrapping.

### Subscription

```csharp
// Before
DateTime publishTime = sub.PublishTime;
IList<MonitoredItem> created = await sub.CreateItemsAsync(ct);
UInt32Collection seq = …;

// After
DateTimeUtc publishTime = sub.PublishTime;
ArrayOf<MonitoredItem> created = await sub.CreateItemsAsync(ct);
ArrayOf<uint> seq = …;
```

### MonitoredItem

```csharp
// Before
filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.EventId);

// After — BrowseNames is string; need QualifiedName
filter.AddSelectClause(ObjectTypes.BaseEventType, QualifiedName.From(BrowseNames.EventId));
```

### Other client-side notes

- `SessionChannel.cs` deleted. Channel management folded into `Session.cs`. No
  replacement needed unless you were using `SessionChannel` directly.
- New base interfaces: `IServiceRequest` (has `RequestHeader`) and
  `IServiceResponse` (has `ResponseHeader`).

## 10. User-identity token handlers

Crypto operations moved from token objects to dedicated handler objects.
UA0011 flags the sync→async migration; the shim ships sync wrappers so the
1.5.378 call sites still compile (with the sync-over-async caveat).

```csharp
// Before — crypto directly on token
var token = new X509IdentityToken();
token.Encrypt(cert, nonce, policy, context);
token.Decrypt(cert, nonce, policy, context);
var sig = token.Sign(data, policy);
bool valid = token.Verify(data, sig, policy);

// After — handler pattern with IDisposable
var token = new X509IdentityToken();
using var handler = token.AsTokenHandler();
await handler.EncryptAsync(cert, nonce, policy, context, ct);
await handler.DecryptAsync(cert, nonce, policy, context, ct);
var sig = await handler.SignAsync(data, policy, ct);
bool valid = await handler.VerifyAsync(data, sig, policy, ct);
```

Available handlers: `AnonymousIdentityTokenHandler`,
`UserNameIdentityTokenHandler`, `X509IdentityTokenHandler`,
`IssuedIdentityTokenHandler`.

## 11. XmlElement

The `XmlElement` built-in is now `Opc.Ua.XmlElement` (a value type wrapping a
string), not `System.Xml.XmlElement`. Usually just removing the `using
System.Xml;` fixes it. If you need the BCL type:

```csharp
System.Xml.XmlElement sysXml = opcUaXmlElement.ToXmlElement();
```

## 12. Other generated data types

All generated `IEncodeable` types now:

- Implement `IEquatable<T>` with `==` and `!=` (based on `IsEqual`).
- Have proper `ToString()` and `GetHashCode()`.

**Subtle change:** if you relied on **reference** equality for data types, you
now get **content** equality. Use `ReferenceEquals()` for reference comparison
or `RefEqualityComparer<T>` for dictionary keys.

## 13. NodeId / ExpandedNodeId

### Construction

```csharp
// Before — removed conversions / obsolete ctors
NodeId n = "ns=2;s=MyNode";                  // ERROR: no implicit string
NodeId n = new NodeId("ns=2;s=MyNode");      // OBSOLETE: use Parse
ExpandedNodeId en = "nsu=…;s=MyNode";        // ERROR: no implicit string

// After
NodeId n = NodeId.Parse("ns=2;s=MyNode");
ExpandedNodeId en = ExpandedNodeId.Parse("nsu=…;s=MyNode");
// or for known identifier types
NodeId n = new NodeId(1234, 2);              // uint identifier, namespace index
NodeId n = new NodeId("MyNode", 2);          // string identifier, namespace index
```

UA0007 auto-fixes the obsolete string ctor.

### Identifier access

```csharp
// Before — .Identifier returned boxed object
object id = nodeId.Identifier;
uint numId = (uint)nodeId.Identifier;

// After — type-safe extraction
if (nodeId.TryGetIdentifier(out uint numId)) …
if (nodeId.TryGetIdentifier(out string strId)) …
if (nodeId.TryGetIdentifier(out Guid guidId)) …
if (nodeId.TryGetIdentifier(out ByteString opaqueId)) …

// Display / logging only (no boxing)
string text = nodeId.IdentifierAsText;
```

### `NodeId.Null.ToString()` returns `""`, not `null`

`Format` / `ToString` now return `string.Empty` instead of `null` for the
`Null` NodeId. Adjust any code that compared against `null` explicitly.

## 14. StatusCode

`StatusCode` is now a `readonly struct` containing both the uint code and an
interned symbol string. Mutators became immutable `With*` methods that **must**
be assigned back:

```csharp
// Before
sc.SetCodeBits(bits);
sc.SetFlagBits(flags);

// After — store the return value
sc = sc.WithCodeBits(bits);
sc = sc.WithFlagBits(flags);
sc = sc.WithLimitBits(limits);
sc = sc.WithAggregateBits(agg);
```

---

## Quick reference: error pattern → fix

| Compiler error | Fix |
|---|---|
| `CS0019: Operator '==' cannot be applied to 'NodeId' and '<null>'` | UA0003: `== null` → `.IsNull` |
| `CS0019: Operator '!=' cannot be applied to 'QualifiedName' and '<null>'` | UA0003: `!= null` → `!.IsNull` |
| `CS0246: type '<T>Collection' not found` | UA0002 + source generator emit the shim |
| `CS0029: Cannot implicitly convert type 'object' to 'Variant'` | UA0006: use `Variant.From(value)` or typed ctor |
| `CS1503: 'System.DateTime' not convertible to 'DateTimeUtc'` | Implicit conversion is allowed in that direction; check overloads |
| `CS1503: 'byte[]' not convertible to 'ByteString'` | UA0005: `.ToByteString()` |
| `CS1503: 'System.Guid' not convertible to 'Uuid'` | Cast: `(Uuid)guid` |
| `CS0117: '<T>Collection' does not contain a definition for …` | Type removed — switch to `List<T>` / `ArrayOf<T>` |
| `CS0200: property X is read-only` | Immutable struct — use `With*` methods |
| `CS0619: '<method>' is obsolete` | Follow the obsolete message; UA00xx is usually offering an auto-fix |
| `CS0103: 'Matrix' does not exist` | Use `MatrixOf<T>` |
| `CS0411: type arguments cannot be inferred` on `ReadEncodeable` | Add generic: `ReadEncodeable<T>(...)` |
| `CS4007: Instance of 'Span<T>.Enumerator' cannot cross await` | Convert to `.ToList()` before `foreach` |
| `CS1503: 'string' to 'QualifiedName'` | `QualifiedName.From(str)` or explicit cast |
| `CS1503: 'string' to 'LocalizedText'` | `LocalizedText.From(str)` or explicit cast |
| `CS1503: 'string' to 'NodeId'` | `NodeId.Parse(str)` |
| `CS0050: Inconsistent accessibility: return type 'XxxCollection'` | Generator shim is `internal`; migrate the public API to `List<T>` / `ArrayOf<T>` first |
| `MIG01` | See [`source-generator.md`](source-generator.md) resolution playbook |

---

## Caveats checklist

1. **Do not suppress `[Obsolete]` warnings.** Obsolete API will be removed in
   the next minor 2.0 release.
2. **`var` is your friend.** Many type mismatches (`object` → `Variant`) become
   non-issues when you use `var` for locals.
3. **Test thoroughly.** Value-type semantics differ from reference types in
   equality, default values, and parameter passing.
4. **Build incrementally.** Fix one priority layer at a time and rebuild to
   track progress.
5. **Refer to canonical docs.** `Docs/MigrationGuide.md` is the upstream
   source of truth — sync this skill against it on every PR.
