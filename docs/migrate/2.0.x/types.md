# Improved Type Safety

> **When to read this:** Read this when hit by `CS0029` / `CS1503` / `CS0266` on `NodeId`, `Variant`, `DataValue`, `ExtensionObject`, `QualifiedName`, `LocalizedText`, `ArrayOf<T>` / `MatrixOf<T>`, `ByteString`, `StatusCode`, `XmlElement`, `EnumValue`, or by `[Obsolete]` warnings on built-in type APIs - covers every value-type and `Variant`-for-`object` migration. Maps to analyzer rules UA0001-UA0020.

### Several built in types are now immutable value types

The `Variant` and `TypeInfo`, `NodeId`, `ExpandedNodeId`, `ExtensionObject`, `LocalizedText` and `QualifiedName` are now `readonly struct`s. This is a large breaking change and affects existing usage:

1. You cannot compare any of these types against `null`. Use the instance properties: `NodeId.IsNull`, `ExpandedNodeId.IsNull`, `QualifiedName.IsNull`, `LocalizedText.IsNullOrEmpty`, `ExtensionObject.IsNull`. In case of `ArrayOf`/`MatrixOf`/`ByteString`, you can most often just check against `IsEmpty` which checks null and emptiness.
2. The default item can be created by assigning `default`, e.g. producing `NodeId.Null` for NodeId and `QualifiedName.Null` for QualifiedName. It is recommended to use the `Null` property on these types for readability and per your coding conventions.
3. Any API that mutated an instance of one of these built in types must be replaced with methods that return a new value of the type, e.g. `NodeId.WithNamespaceIndex(ushort)` as setters were removed.

### ByteString

Previously the OPC UA built-in type *ByteString* was represented as `byte[]`. This caused ambiguities with regards to it and the byte *array* type. This has changed and `ByteString` is now a type in the Opc.Ua namespace. It is a wrapper around `ReadOnlyMemory<byte>` and while `Variant` handles both still interchangeably, the generated API now simplifies mixing of byte arrays and `ByteString` without confusion.

Note that equality operation compare the content of the byte string. A `ByteString` is a value type while `System.Byte[]` is not. It cannot be compared against `null`. However, it supports checking for empty `IsEmpty` and `IsNull` whereby the first checks whether the ByteString is effectively a `ByteString.Empty` amd the second checks whether `ByteString` was initialized using `default`.

While it was tempting to make `ByteString` implicitly convertible from `byte[]`, an explicit cast is needed to strictly distinguish against `ArrayOf<byte>` which implicit converts to `byte[]`. Prefer the `ByteString.From` or `ToByteString()` calls to cast operators to make your code's intentions explicit. Note that a `byte[]` implicitly converts to `ReadOnlyMemory<byte>` in .net therefore any conversion from `ByteString` is explicit.

To migrate, perform the following general replacements in your code:

**Change code as follows:**

- Replace `byte[]` with `ByteString` in areas flagged as errors, e.g. wherever casting a `Variant` to a `byte[]` change it to `ByteString` or to `ArrayOf<byte>` if it is a byte array.
- When a `ByteString` is required as input and you have any form of enumerable bytes, try appending `.ToByteString()` to convert.
- Use `ByteString.Combine` in lieu of `Utils.Append`.
- Indexing and enumeration of bytes is only supported via the `Span` property. Change your code to replace `[i]` with `.Span[i]` to fix errors.
- If your code tried to set a byte in the ByteString, create a buffer `byte[]` and after changing convert to `ByteString` using `ByteString.From(buffer)` or `.ToByteString()` extension method
- Perform changes only where you encounter build breaks. This should be enough to get into a working state. Later adjust the code as needed.

### ArrayOf and MatrixOf

Similar to `ByteString`, `ArrayOf<T>` and `MatrixOf<T>` are new type safe and sliceable generic value types representing non-scalar values. They are immutable meaning the values at an index inside them cannot be "set" unless they are converted to a `Span<T>` (and then reconverted to a `ArrayOf`/`MatrixOf`).

In addition to slicing and range based access, both types provide the ability to apply a NumericIndex to them.  They are efficiently stored inside a Variant as well and can be used to allocate efficiently from `ArrayPool` providing the ability to built object pooling support at the array level. `ArrayOf<T>` implicitly converts to `List<T>` but not vice versa. For API that is taking `ArrayOf<T>` as input convert any list using `ToArrayOf`. `IsEmpty` returns true if `IsNull` is true but not necessarily vice versa.

Internally an `ArrayOf`/`MatrixOf` stores a reference to "memory" and a offset and length integer. They have the same layout as `ReadOnlyMemory<T>` although this is not guaranteed to stay so in the future. All generated collection types implicitly convert to and from `ArrayOf<T>` whereby `T` is the member type of the collection type.  E.g. `VariantCollection` is effectively `ArrayOf<Variant>`.

`ArrayOf<T>` provides helper methods e.g. to  `AddItem` an item or `AddItems` of items in another `ArrayOf<T>`. Both return a new `ArrayOf<T>`, very similar to the .net ImmutableCollection classes or the `Append` or `Concat` extension methods in the `System.Linq`.

`Contains`, `IndexOf`, `Filter`, `Find`, `FindIndex` and `ConvertAll` methods mimic the Linq `Where`, `Any`, `FirstOrDefault`, `Select` or the respective methods on the `List<T>` type. Use `SafeSlice` instead of `Take` to slice up to the length and which returns an empty array instead of throwing which is what the regular Slice/range operators do.  You cannot use more advanced Linq expressions (e.g. order by or group by) without converting to a list (`ToList`) or array (`ToArray`) first. Linq is slow, so using the methods on the array type where possible will provide a performance improvement.

All generated APIs, Encoders/decoders, and the Variant type now use `ArrayOf`/`MatrixOf` instead of the previously generated/built-in non-generic collection types which have been removed.

Note that equality operators and methods now compare the content of the Array and Matrix, not just reference equality as with `T[]`. It supports checking for an empty array or matrix via `IsEmpty` and `IsNull` whereby the first checks whether the array is effectively a `ArrayOf.Empty<T>` amd the second is just a check against `ArrayOf<T>` initialized using `default` (since it is not a reference type anymore). `IsEmpty` returns true if `IsNull` is true but not necessarily vice versa.

**Change code as follows:**

> ℹ **Tip — install `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`** before touching collection sites. Its source generator emits an `internal sealed [Obsolete] class <Name>Collection : List<TElement>` shim per consumer compilation for every `<Type>Collection` the consumer references (including model-compiled `<UserType>Collection` patterns), so `CS0246: type or namespace 'XxxCollection' not found` is replaced with `[Obsolete]` warnings + `UA0002` analyzer guidance you can iterate through.

- Replace any `T[]` with `ArrayOf<T>` where T is the type of the element in the array. Do this where errors are flagged, e.g. wherever casting a Variant to a `T[]` change it to `ArrayOf<T>` if it is a T array.
- Change all use of `<Type>Collection` or `IList<Type>` to `List<Type>` (add a `using System.Collections.Generic` directive if needed). When the collection is never mutated (items added, inserted or removed), use `ArrayOf<Type>`.
- In case of `error CS4007: Instance of type 'System.ReadOnlySpan<T>.Enumerator' cannot be preserved across 'await' or 'yield' boundary` convert the enumerated `ArrayOf<T>` to a list using `ToList()` and enumerate the list.
- When trying to set a value in the previous array, create a buffer `T[]` and after mutating convert to `ArrayOf<T>` using `buffer.ToArrayOf()`.
- To add items to an `ArrayOf` use the new `AddItem`/`AddItems` methods where you would have used `Add` or `AddRange` before. Note that ArrayOf is immutable so the result needs to be assigned to the variable to which you want to add. You can also use the `+=` operator for less verbose code.
- In performance intensive code or where items are added in a loop it is best to first create a `List<T>` and then assign the list later (e.g. after the loop) to a variable of `ArrayOf<T>` type.
- Perform changes only where you encounter build breaks. This should be enough to get into a working state. Later adjust the code if needed.
- Remove any use of `Matrix` which is deprecated and replace with `MatrixOf<T>` which is type safe.

``` csharp
    // Some examples
    VariantCollection c = new VariantCollection();
    // if (c != null) if c is passed from outside
    c.Add(new Variant(1))
    var first = c.FirstOrDefault();
    Int32Collection i = c.Select(v => (int)v).ToList();

    // need to change to
    ArrayOf<Variant> c = [new Variant(1)]; // or
    ArrayOf<Variant> c = default; c = c.Add(new Variant(1)); // or
    ArrayOf<Variant> c = default; c += new Variant(1);
    var first = !c.IsEmpty ? c[0] : default;
    ArrayOf<int> i = c.ConvertAll(v => (int)v);
```

#### Configuration collection types removed

All `List<T>`-based collection wrappers for configuration types have been removed and replaced with `ArrayOf<T>`: `ServerSecurityPolicyCollection`, `TransportConfigurationCollection`, `SamplingRateGroupCollection`, `ReverseConnectClientCollection`, `ReverseConnectClientEndpointCollection`, `ServerRegistrationCollection`, `CertificateIdentifierCollection`, `CertificateGroupConfigurationCollection`, `OAuth2ServerSettingsCollection`, `OAuth2CredentialCollection`.

#### Generated data type fields with ValueRank=OneOrMoreDimensions

Previously, every structure field declared with `ValueRank="OneOrMoreDimensions"` in a model design was generated as `global::Opc.Ua.Variant`. The property is now typed as `global::Opc.Ua.MatrixOf<T>` (mirroring the `ArrayOf<T>` treatment already used for `ValueRank="Array"`). Encoding/decoding still flows through `Variant`, but the boxing/unboxing happens inside the encoder calls so consumers see the typed surface.

The element type follows the field's `DataType`:

| Field `DataType`                                  | Generated property type                    | Encode call                                                       | Decode call                                                                 |
| ------------------------------------------------- | ------------------------------------------ | ----------------------------------------------------------------- | --------------------------------------------------------------------------- |
| primitive (e.g. `Boolean`, `Int32`, `String`)     | `MatrixOf<bool>` etc.                      | `encoder.WriteVariant(name, Variant.From(field));`                | `field = decoder.ReadVariant(name).GetBooleanMatrix();` (etc.)              |
| `Structure` / abstract structure parent           | `MatrixOf<ExtensionObject>`                | `encoder.WriteVariant(name, Variant.From(field));`                | `field = decoder.ReadVariant(name).GetExtensionObjectMatrix();`             |
| concrete `IEncodeable` (e.g. `Vector`)            | `MatrixOf<Vector>`                         | `encoder.WriteEncodeableMatrix(name, field);`                     | `field = decoder.ReadEncodeableMatrix<Vector>(name);`                       |
| typed enum (`MyEnum`)                             | `MatrixOf<MyEnum>`                         | `encoder.WriteVariant(name, Variant.From(field));`                | `field = decoder.ReadVariant(name).GetEnumerationMatrix<MyEnum>();`         |
| `BaseDataType` / `Number` / `Integer` / `UInteger`| `MatrixOf<Variant>`                        | `encoder.WriteVariant(name, Variant.From(field));`                | `field = decoder.ReadVariant(name).GetVariantMatrix();`                     |

`Variant` round-trip APIs are available for every `BasicDataType` value except `DiagnosticInfo`. For a `DiagnosticInfo` matrix field — which is not a valid structure field per OPC UA Part 5 in any case — the legacy `Variant` property surface is retained.

**Change code as follows:**

- Direct access on the property is now typed; replace the
  `new Variant(new Matrix(...))` wrapping / `Variant.Value` cast you
  needed in 1.5.378 with the typed `MatrixOf<T>` assignment:

  ``` csharp
      // Before (1.5.378) — field was object/Variant; wrap a Matrix
      myStruct.MyMatrix = new Variant(new Matrix(
          new int[] { 1, 2, 3, 4 },
          BuiltInType.Int32,
          new int[] { 2, 2 }));
      var back = (int[,])((Matrix)myStruct.MyMatrix.Value).ToArray();

      // After — field is MatrixOf<int>; assign / read directly
      myStruct.MyMatrix = new int[,] { { 1, 2 }, { 3, 4 } }.ToMatrixOf();
      MatrixOf<int> back = myStruct.MyMatrix;
  ```

- `IDecoder` gained a parameterless `ReadEncodeableMatrix<T>(string? fieldName) where T : IEncodeable, new()` overload that mirrors the existing `ReadEncodeableArray<T>(string? fieldName)` shape. Custom `IDecoder` implementations should add this overload alongside the existing encoding-id variant.

##### VariableType State classes, PropertyState instances, and service parameters

The same `MatrixOf<T>` opt-in now extends beyond structure data type fields to three sibling sites in the source generator:

- **VariableType State classes** — `VariableType` designs that restrict both the `DataType` and the `ValueRank` to a concrete matrix shape (e.g. `XYArrayItemType` with `DataType="XVType" ValueRank="OneOrMoreDimensions"`) now inherit the generic chain with a typed parameter. Previously: `XYArrayItemState : ArrayItemState<Variant>.Implementation<VariantBuilder>`. Now: `XYArrayItemState : ArrayItemState<MatrixOf<XVType>>.Implementation<StructureBuilder<XVType>>`. Consumers reading or writing `.Value` get a typed `MatrixOf<XVType>` directly.
- **PropertyState / BaseDataVariableState instances** — instance variables that *narrow* a generic variable type (e.g. `PropertyType` → `EnumDictionaryEntries` with `DataType="NodeId" ValueRank="OneOrMoreDimensions"`) now declare typed `PropertyState<MatrixOf<NodeId>>` instead of falling back to the simple `PropertyState` name and losing type information. Same for `FailureSystemIdentifier` → `BaseDataVariableState<MatrixOf<byte>>`.
- **Service method parameters** — Client/Server API generators now type matrix-rank arguments as `MatrixOf<T>` instead of `Variant`. No OPC UA standard service declares matrix arguments today, so this is forward-looking for custom service models.

**Change code as follows:**

For abstract base variable types (`ArrayItemType`, `CubeItemType`, `ImageItemType`, `NDimensionArrayItemType`, all of which declare `DataType="BaseDataType"`) the State class still uses the generic `<T>` parameter — consumers continue to instantiate with whatever element type matches their data.

For *concrete* matrix variable types (today only `XYArrayItemType`) and matrix-rank property/variable instances, the `Value` setter and getter are now typed. Replace the 1.5.378 `new Variant(new Matrix(...))` pattern with a typed `MatrixOf<T>` assignment:

``` csharp
    // Before (1.5.378) — Value was object; wrap a Matrix of XVType
    variable.Value = new Variant(new Matrix(
        new XVType[]
        {
            new XVType { X = 0.0, Value = 0.0f },
            new XVType { X = 1.0, Value = 1.0f }
        },
        BuiltInType.ExtensionObject,
        new int[] { 2 }));

    // After — Value is MatrixOf<XVType>; use a typed constructor
    variable.Value = new[]
    {
        new XVType { X = 0.0, Value = 0.0f },
        new XVType { X = 1.0, Value = 1.0f }
    }.ToMatrixOf(2);
```

### DateTimeUtc

Previously the **DateTime** built in type was represented by the `System.DateTime` type. It is now represented by the `Opc.Ua.DateTimeUtc` type. This new type complies with the details of the spec without requiring external helper methods to be used. It's Value property returns the ticks, bounded by the information in Part 6 of the spec, and its time is always UTC. There are conversion operations to and from `DateTime`, but also `DateTimeOffset` and `long` and a minimal subset of `System.DateTime` API to allow for simpler porting. `DateTime` implicitly converts to `DateTimeUtc`, but not vice versa to force use of the new type.

**Change code as follows:**

- Replace `DateTime` with `DateTimeUtc` where appropriate, especially in places where comparing with `DateTime.MinValue`.
- Replace `DateTime.UtcNow` with `DateTimeUtc.Now` for UTC time "right now". `DateTime.Now` or `DateTime.Today` can be cast or replaced with its Utc variant, which is likely intended anyway as all date/time values in OPC UA are UTC.
- When assigning a `DateTime` value to a `DateTimeUtc` variable, add a cast, or use the corresponding `DateTimeUtc` constructor.

### QualifiedName and LocalizedText

There is no implicit conversion from `string` to `QualifiedName` or `LocalizedText` anymore. For one, it flags areas where null assignment is happening implicitly, and secondly, it makes the API more explicit. E.g. previously it was possible to assign a string to a browse name which landed the browse name accidentally in namespace 0 instead of the owning namespace. If you know what you are doing you can explicitly cast the string, but it is suggested to use the new static `From` API instead.

### StatusCode

`StatusCode` contains now not only a uint code, but also a symbol.  Symbols are interned strings and using the `StatusCodes` constants therefore come with the symbol string. This removes the need to look up the symbolic id, however, when receiving a uint code it needs to be translated to a StatusCode constant to retain the Symbol. Older API has been obsoleted with proper instructions. Since types are immutable it is important to replace mutation calls with the proper replacement method and store the returned value.

**Behavioral change — equality compares the code bits only.** The `==` / `!=` operators, the `Equals` overloads, and `GetHashCode` now compare only the 16 code bits (bits 16 - 31) of the status code and ignore the info, flag and additional bits. Previously the full 32-bit value was compared. This matches the almost-universal intent of comparing a received status against a well known `StatusCodes.XXX` constant (which never carries flag bits), so values such as `Good` with the `SemanticsChanged` flag set now compare equal to `StatusCodes.Good`. If you need an exact, all-bits comparison (for example to detect that the flag bits changed), use the new `Equals(StatusCode other, StatusCodeComparison comparison)` overload with `StatusCodeComparison.AllBits`:

```csharp
// Code-bits comparison (default): true even when statusCode carries flag bits.
if (statusCode == StatusCodes.Good) { /* ... */ }

// Exact comparison of all 32 bits.
if (statusCode.Equals(other, StatusCodeComparison.AllBits)) { /* ... */ }
```

A matching `GetHashCode(StatusCodeComparison comparison)` overload is available when an all-bits hash is required (for example when using `StatusCode` keys in a dictionary that must distinguish flag bits).

When migrating code that compared `statusCode.Code` (the raw `uint`) against a `StatusCodes.XXX` constant, prefer the `==` operator (`statusCode == StatusCodes.XXX`) so the comparison ignores the non-code bits. Likewise replace comparisons against `statusCode.CodeBits` with the `==` operator.

### NodeId/ExpandedNodeId

`NodeId`s with integer identifiers (the most common case) now do not box the integer identifier anymore into an object, making the entire NodeId heap allocation free (*).  ExpandedNodeId with integer identifiers only contain an allocated namespace Uri, which is mostly a const (interned) string, reducing small allocations across both types. Because both types are now immutable, they must be mutated using the provided `With<X>`. Access to the identifier in boxed form (object) is deprecated. Instead use the `TryGetValue(out uint/string/Guid/byte[])` API. If you need to get the identifier only to "stringify" it, use the `IdentifierAsText` property which avoids boxing integer identifiers.

There is no implicit conversion from `uint`/`Guid`/`string`/`byte[]` to `NodeId`/`ExpandedNodeId` to ensure assignment of null reference types (byte array and string) is not happening implicitly and to prevent accidental conversion of these identifiers into namespace 0. It also removes  hidden behavior such as parsing during assignments and flags areas where a proper Null/default NodeId should be inserted/returned. Use the explicit cast (e.g. `(NodeId)[(byte)3, 2]`) instead. For the previous implicit conversion from `string` to `NodeId` conversion use `NodeId.Parse` and `ExpandedNodeId.Parse`. On the same note, the constructor taking a string and no namespace index has been deprecated as it required a string to parse. Use Parse/TryParse instead.

> (*) Note that NodeId leverages the new `uint` field to cache the HashCode of a "non-uint" "Identifier", which provides faster lookup using NodeId/ExpandedNodeId as key.

### Variant, DataValue and ExtensionObject

Previously the `Variant` was a *mutable* struct containing a `TypeInfo` and `Value` property allowing setting the inner state and returning `object`.  All value types thus were implicitly boxed to object and landing on the heap. The new `Variant` only boxes value types > 8 bytes in size (*), and stores the rest in a union.  `TypeInfo`, previously a class, also now is stored as a 4 byte type (with padding).

The `ExtensionObject` was a reference type wrapping a `NodeId` and a body as a reference type of `object`. The `ExtensionObject` is now an immutable value type with type-safe access to its body.

`Session.Call` / `Session.CallAsync` previously took `params object[]` and silently boxed every argument. The new signature takes `params Variant[]`, so each call argument must be wrapped explicitly:

```csharp
// Before
var output = session.Call(objectId, methodId, 1, "two", DateTime.UtcNow);

// After
var output = session.Call(objectId, methodId,
    Variant.From(1),
    Variant.From("two"),
    Variant.From(new DateTimeUtc(DateTime.UtcNow)));
```

`null` arguments must be passed as `Variant.Null` (a literal `null` will not bind to the `params Variant[]` overload).

#### Deprecated boxing behavior

Access to the `Value` property of `Variant` is marked as [Obsolete] to discourage use in favor of casting to `<Type>` or `Get<Type>()` (both throw) or preferably `bool TryGetValue(out <Type> value)` calls. The same applies to the `Value` property of `DataValue`. The APIs perform any required conversion between `BuiltInType.Int32` and `BuiltInType.Enumeration` as well as arrays of `BuiltInType.Byte` and `BuiltInType.ByteString`. This also applies to the `Body` property of `ExtensionObject`. Here prefer the use of `TryGetValue<T>` and `TryGetBinary, TryGetJson, TryGetXml`.

Creating a `Variant` or `ExtensionObject` via the constructor taking a `object` parameter is also marked [Obsolete] to encourage using type safe API to create a Variant (and thus not storing the wrong value in the inner `object` variable that cannot be converted out again or makes the Variant a null variant unexpectedly).

In some cases it is desirable to gain access to what was returned from the now obsoleted `Value` property. To make the fact that the returned value is likely boxed, the new API is named `AsBoxedObject()`. While the Variant has conversion operators from all supported types and corresponding `From(<Type> value)` APIs, it is sometimes necessary to convert from `System.Object`. Note that `AsBoxedObject()` does not return .net array types but `ArrayOf<T>`, and `ByteString` for - yes - ByteString. `Value` property converts to the old style type expectations.

To perform conversion from `<T>` to a Variant, helper methods are available in `VariantHelper` static class. These helper methods are split into ones that use reflection and ones that do not. Overall, use of these helper methods is not recommended in favor of switching on the type information in the Variant.

> `DateTimeUtc` and `EnumValue` are always stored unboxed inside a Variant. However, converting a enum (`System.Enum`) to an EnumValue requires boxing on .net standard and .net framework.
> All other built in value types (`ExtensionObject`, `NodeId`, `QualifiedName`, `LocalizedText`, `Uuid`, etc.) are > 8 bytes in size and are therefore boxed when stored inside a Variant.
> Future improvements will make certain types like `ArrayOf` be stored *spliced* inside the Variant (where the array pointer is stored in the object, and length/offset inside the union).

#### Replacement of all use of System.Object in generated code and API

`Variant` is now the type reflecting the OPC UA Variant type in all API. That means all generated API now uses Variant instead of `System.Object` and all `Value` Properties are `Variant` too.  This provides type safety and removes the need for Reflection via `GetType()` when the underlying type already is `Variant`.

**System.Object and Variant comparable operations:**

- *Casting*: Casting from Variant to built in system type "will just work" the same way as casting from the object, e.g. `object a; uint b = (uint)a;` is equivalent to `Variant a; uint b = (uint)a;`. Both throw `InvalidCastException` if the cast is not possible.
- *Pattern matching*: If you use is pattern matching use the new `TryGetValue/TryGetStructure` calls. If you cast using as, use the same or if you prefer a default value in case the Variant has a different type, the `Get<BuiltInType>` or `GetStructure<T>` or equivalent array returning methods ending in `Array`. They do not throw, but return the default value.
- *Reflection*: Use `TypeInfo` property on Variant to obtain metadata for for example switching.
- *Conversion*: Previously TypeInfo had support to Cast an object aligned with Variant behavior. These API have been removed in favor of the `ConvertTo[<]BuiltInType]()` members or `ConvertTo(BuiltInType target)`. NOTE: Under the hood `IConvertible` is used, which means integer values are boxed.

To migrate, perform the following general replacements in your code:

- If you are setting the `Value` property of Variant, change the code to create a `new Variant` with the value via constructor or `Variant.From` or by casting to `Variant`.
- Generally replace all `IList<object>` with `IList<Variant>`
- Generally replace all `ref object` with `ref Variant`.
- In addition: for all callbacks registered in `BaseVariableState` change the callback signature to use `Variant` instead of `object` and `Variant[]` instead of `object[]`.
- For all remaining `object[]` instances, replace with `ArrayOf<Variant>` or `IList<Variant>` judiciously and depending on context.
- Keep all *casts* from **Variant** (not from its Value property) to the concrete type if you intend to preserve throw behavior. For any pattern matching (is/as) use `TryGetValue` if you need to check the result, or `Get<BuiltInType>` if you do not want to throw but are happy with the default value.

> IMPORTANT: Care must be taken to not accidentally box a `Variant` value into an `object`.  E.g. current code like `object f = state.Value` will not be flagged by the compiler but must be replaced with `Variant f = state.Value` to remain type safe. Here it is best to use `var` for locals which requires no code changes.

**Remaining work:**

- Assignments to Variants and casting from variant to type should be dealt with via implicit conversion except for Structures. Here change code from `Value = <structure>` to `Value = Variant.FromStructure(<structure>)` and `<structure> = Value` to `Value.TryGetStructure(out <structure>)`.
- Any pattern matching conversion used must be replaced with the TryGetValue/TryGetStructure pattern of Variant for checked conversions, e.g. `a = Value as uint?` must be replaced with `Value.TryGetValue(out uint a)` which most often produces more concise code and avoids the check for nullable result of the conversion. The same applies to `is` matching.
- For Variable and VariableType node state classes that provide a narrowed "Value" via generic `<T>` any access to `T Value` incurs a heavy type check.  It is recommended to use `WrappedValue` instead when possible for assignment and access.
- While most assignments work implicitly, use `TypeInfo.GetDefaultVariantValue` instead of `TypeInfo.GetDefaultValue` to initialize a variant value to a default that is `!= Variant.Null`.

### DataValue

`DataValue` has been converted from a reference type (class) to a `readonly struct` to relieve GC pressure on hot subscription/encoder paths. The semantics are aligned with the other immutable built-in types (`NodeId`, `ExtensionObject`, etc.).

**What changed:**

1. **You cannot compare a `DataValue` against `null` anymore.** Use the `DataValue.IsNull` instance property, or the `DataValue.Null` static field (equivalent to `default(DataValue)`).
2. **Property setters were removed.** Use the new `With<Property>()` fluent mutators — each returns a *new* `DataValue` with that field replaced, e.g. `dv = dv.WithStatus(StatusCodes.BadInternalError)`. Chaining a `default` value with `With*` calls is folded by the JIT into a single constructor call.
3. **`IsGood` / `IsBad` / `IsUncertain` / `IsNotGood` / `IsNotBad` / `IsNotUncertain` are instance properties** on `DataValue` now. The previous static `DataValue.IsGood(dv)` style helpers were removed; they remain as `[Obsolete]` extension methods on `DataValueExtensions` so existing source still compiles, but new code should prefer `dv.IsGood`.
4. **`Nullable<DataValue>` (`DataValue?`) is redundant** and should be removed from your code. Because `DataValue` is itself nullable via `IsNull`, wrapping it in `Nullable<>` doubles the storage and adds boxing on the `HasValue`/`Value` access pattern. Replace `DataValue?` fields/parameters/locals with `DataValue` and use `dv.IsNull` / `DataValue.Null` instead of `dv == null` / `null`. The compiler will not flag this automatically.
5. **`IsNull` has sentinel semantics**: `default(DataValue)` reports `IsNull == true`, while any *explicitly* constructed `DataValue` (e.g. `new DataValue(Variant.Null)` with all-default fields) reports `IsNull == false`. This preserves the distinction between "absent" and "explicitly empty" on the wire — the binary, JSON and XML encoders now round-trip both forms without conflation. If you currently rely on "all fields are at default" semantics, replace your check with explicit field comparisons instead of `IsNull`.
6. **Decoders use the sentinel.** `IDecoder.ReadDataValue` (Binary, Xml, Json) returns `DataValue.Null` when the field is absent (or, for the binary encoder, when the encoding byte is `0`), allowing callers to distinguish "missing" from "present but empty".
7. **Prefer `in DataValue` for synchronous method parameters.** The struct is large (~64 bytes after the IsNull sentinel) and copying it on every call is wasteful. The server `IDataChangeMonitoredItem.QueueValue(in DataValue, ...)` API has been updated accordingly. Async methods cannot use `in`/`ref` parameters, so leave those by-value.
8. **`object? GetValue(Type)` and `T? GetValueOrDefault<T>()` are now `[Obsolete]`.** Use `WrappedValue.TryGetValue<T>(out T value)` or `WrappedValue.TryGetStructure<T>(out T value)` for type-safe extraction without throwing. `GetValue<T>(T defaultValue)` remains supported.
9. **`DataValue.FromStatusCode(StatusCode)` and `FromStatusCode(StatusCode, DateTimeUtc serverTimestamp)`** are the preferred way to construct a `DataValue` that conveys only a status. The `DataValue(StatusCode)` and `DataValue(StatusCode, DateTimeUtc)` constructors are `[Obsolete]` because they conflict with overload resolution against the numeric `Variant` types (`uint`/`int`/`StatusCode` all implicitly convert in different directions). Recompile existing `new DataValue(statusCode)` calls and replace the resulting obsolete warnings with `DataValue.FromStatusCode(statusCode)`; this also avoids relying on historical compiler overload selection that could encode the StatusCode as a Good Variant value.

**Change code as follows:**

```csharp
// Before
DataValue dv = ReadValue();
if (dv == null) { ... }
dv.Value = 42;                                     // mutating setter — gone
dv.StatusCode = StatusCodes.Bad;                   // mutating setter — gone
if (DataValue.IsGood(dv)) { ... }                  // static helper — moved to Obsolete extension

// After
DataValue dv = ReadValue();
if (dv.IsNull) { ... }
dv = dv.WithWrappedValue(new Variant(42));         // returns a new DataValue
dv = dv.WithStatus(StatusCodes.Bad);
if (dv.IsGood) { ... }                             // instance property

// And to convey only a status (no value):
DataValue bad = DataValue.FromStatusCode(StatusCodes.BadInternalError);

// Drop redundant Nullable<DataValue>:
//   private DataValue? m_lastValue;       ->  private DataValue m_lastValue;
//   m_lastValue = null;                   ->  m_lastValue = DataValue.Null;
//   if (m_lastValue != null) { ... }      ->  if (!m_lastValue.IsNull) { ... }
//   m_lastValue.Value.StatusCode          ->  m_lastValue.StatusCode

// Pass by 'in' on hot paths:
public void QueueValue(in DataValue value, ServiceResult? error) { ... }
```

Async methods cannot accept `in` / `ref` parameters. When an async caller needs to forward a `DataValue` into an `in` API, copy it to a local first so the local owns the storage that gets captured by the state machine:

```csharp
// In async code, copy DataValue to a local before passing in.
async Task EnqueueAsync(DataValue dv)
{
    var snapshot = dv;
    queue.QueueValue(in snapshot, error: default);
    await Task.Yield();
}
```

### XmlElement

Previously the `XmlElement` built in type was represented by the `System.Xml.XmlElement` system type. While officially a deprecated, there is now a value type `XmlElement` that merely wraps a string but provides conversion operations to `System.Xml.XmlElement` and `System.Linq.Xml.XNode` as well as validation and equality/hashing operations. Normally you just need to remove `using System.Xml` and code continues working as is.  If you need to have access to the `System.Xml.XmlElement` cast or use the `ToXmlElement` method.

> `XmlElement` types are compared via a normalized version of the XML `string` contained, which removes all whitespace before comparing. This can result in some ambiguity, but operates well enough for test operations. For complete equality, cast to XNode and use `DeepEquals`.

### EnumValue to represent the enumeration built in type

`EnumValue` bundles a symbol with a integer value (same as `StatusCode`). While most API works with standard .net `enum` types, these do not work in scenarios where the enum value is the result of a `EnumDefinition`. For these
cases the `EnumValue` overloads provide a similar experience to using `enum`. In addition, the `EnumValue` type
allows more efficient storage inside `Variant`. For this case, `Variant(Enum)` constructor, `IEquatable<Enum>`, and `operator ==/!=(Variant, Enum)` do not exist anymore.

Change code as follows:

```csharp
// Before
Variant v = new Variant(MyEnum.Value);
// After
Variant v = EnumValue.From(MyEnum.Value); // or
Variant v = new Variant(EnumValue.From(MyEnum.Value)); // or
Variant v = Variant.From(MyEnum.Value);
```

### ExtensionObject array helpers changed

`ExtensionObject.ToArray(object, Type)` and `ToList<T>(object)` removed. Use `extensionObjects.GetStructuresOf<T>()` or `ExtensionObject.ToArray<T>(ArrayOf<ExtensionObject>)`.

### Other Data Types

All generated data types implementing `IEncodeable` are now equality comparable using `==` and `!=` and implement `IEquatable<T>`. Equality defaults to the `IsEqual` implementation of the `IEncodeable` interface. In addition `ToString()` and `GetHashCode()` are implemented making all generated data types effectively equivalent to `record` classes with the exception of supporting `with` expressions.

**Change code as follows:**

No changes are required, however there can be subtle bugs exposed, e.g.:

- When comparing data type instances for reference equality, use `ReferenceEquals`, instead of `==` or `!=` operators. You can use the `RefEqualityComparer<T>` helper when creating Dictionaries that use the type as key and require reference equality semantics for it.
- When testing for `null`, use `is null` for more performant code.

### Obsoleted APIs and replacements

- `NodeId(string text)` -> `NodeId.Parse(string)`
- `NodeId(object identifier, ushort namespaceIndex)` -> typed constructors: `new NodeId(uint, ushort)`, `new NodeId(Guid, ushort)`, `new NodeId(string, ushort)`, `new NodeId(ByteString, ushort)`
- `NodeId.Create(object identifier, string namespaceUri, NamespaceTable namespaceTable)` -> typed overloads: `NodeId.Create(string|uint|Guid|ByteString, string, NamespaceTable)`
- `NodeId.Identifier` -> `TryGetValue(out uint|string|Guid|ByteString)` or `IdentifierAsString`
- `NodeId.SetNamespaceIndex(ushort)` -> `WithNamespaceIndex(ushort)` (store the return value)
- `NodeId.SetIdentifier(IdType, object)` -> `WithIdentifier(uint|string|Guid|ByteString)` or typed constructors
- `ExpandedNodeId(string text)` -> `ExpandedNodeId.Parse(string)`
- `ExpandedNodeId(object identifier, ushort namespaceIndex, string namespaceUri, uint serverIndex)` -> typed constructors: `new ExpandedNodeId(uint|Guid|string|ByteString, ushort, string, uint)`
- `ExpandedNodeId.Identifier` -> `TryGetValue(out uint|string|Guid|ByteString)` or `IdentifierAsString`
- `NodeIdExtensions.IsNull(NodeId)` -> `NodeId.IsNull`
- `NodeIdExtensions.IsNull(ExpandedNodeId)` -> `ExpandedNodeId.IsNull`
- `QualifiedNameExtensions.IsNull(QualifiedName)` -> `QualifiedName.IsNull`
- `LocalizedTextExtensions.IsNullOrEmpty(LocalizedText)` -> `LocalizedText.IsNullOrEmpty`
- `QualifiedName.IsNull(QualifiedName)` -> use `QualifiedName.IsNull`
- `ExtensionObject.IsNull(ExtensionObject)` -> use `ExtensionObject.IsNull`
- Implicit cast from `string` or `byte[]` to `NodeId`/`ExpandedNodeId` -> use explicit cast or `From()` API
- Implicit cast from `string` to `LocalizedText`/`QualifiedName` -> use explicit cast or `From()` API
- `Format` and `ToString` APIs return `string.Empty` instead of `null` for `NodeId`, `QualifiedName`, `ExpandedNodeId`, `LocalizedText` to prevent NullReferenceExceptions
- `Matrix` class -> use `MatrixOf<T>`
- `<T>Collection` classes -> use `ArrayOf<T>` or `List<T>`
- `new Variant(object)` -> use `Variant.From(T)`
- `Variant.Value` -> use `Variant.TryGetValue`, cast, or `AsBoxedObject` if absolutely necessary.
- `DataValue.GetValue`, `DataValue.GetValueOrDefault`, ,`DataValue.Value` -> use `DataValue.WrappedValue` and the new API on Variant (e.g. `Get[Type]`,  `TryGetValue`)
- `new DataValue(StatusCode)` and `new DataValue(StatusCode, DateTimeUtc)` -> use `DataValue.FromStatusCode(StatusCode)` and `DataValue.FromStatusCode(StatusCode, DateTimeUtc)`. The constructors suffered from a C# overload resolution bug where `new DataValue(42)` silently resolved to `DataValue(StatusCode)` instead of `DataValue(Variant)`, losing the value.
- `SessionManager.ImpersonateUser` -> register `IUserTokenAuthenticator` instances via `services.AddIdentityAuthenticator<T>()` or `server.CurrentInstance.IdentityRegistry.Register(...)`. The event remains functional as a fallback, but is now `[Obsolete]`; the in-box ReferenceServer, GlobalDiscoverySampleServer, and ConsoleReferenceClient samples use the provider model.

### APIs permanently removed

- All `<Type>Collection` classes, e.g. Int32Collection or ArgumentCollection -> use `List<Type>` or `ArrayOf<T>`
- `ICloneable`/`Clone()`/`MemberwiseClone()` on the immutable built-in types -> use assignment for copies
- Creating `NodeId` or `ExpandedNodeId` using `byte[]` -> use `ByteString` and type safe constructor.
- Setters removed from immutable types:
  - `QualifiedName.Name`/`QualifiedName.NamespaceIndex` -> `WithName(string)`/`WithNamespaceIndex(ushort)`
  - `LocalizedText.Translations`/`LocalizedText.TranslationInfo` -> `WithTranslations(...)`/`WithTranslationInfo(...)`
  - `ExtensionObject.Body`/`ExtensionObject.TypeId` -> constructors and `WithTypeId(...)`
  - `NodeId.NamespaceIndex`/`NodeId.IdType`/`NodeId.Identifier` setters -> use constructors or `WithIdentifier(...)`
- Implicit cast operator of type string to NodeId/ExpandedNodeId -> use Parse/TryParse
- `WriteGuid(string, Guid)` -> use `WriteGuid(string, Uuid)` and - `WriteGuidArray(string, IList<Guid>)` -> use `WriteGuidArray(string, ArrayOf<Uuid>)`
- `WriteDateTime(string, DateTime)` -> use `WriteDateTime(string, DateTimeUtc)` and - `WriteDateTimeArray(string, IList<DateTime>)` -> use `WriteDateTimeArray(string, ArrayOf<DateTimeUtc>)`
- `WriteByteString(string, byte[])` -> use `WriteByteString(string, ByteString)` and - `WriteByteStringArray(string, IList<byte[]>)` -> use `WriteByteStringArray(string, ArrayOf<ByteString>)`
- new `Variant(Guid)` -> use `Variant.From(Uuid)` or `new Variant(Uuid)`
- new `Variant(DateTime)` -> use `Variant.From(DateTimeUtc)` or `new Variant(DateTimeUtc)`
- new `Variant(byte[])` -> use `Variant.From(ByteString)` or `new Variant(ByteString)` or `Variant.From(ArrayOf<byte>)` or `new Variant(ArrayOf<byte>)`
- Session `Call/CallAsync(param object[])` -> use `Call/CallAsync(param Variant[])`
- `byte[]` as ByteString -> use `ByteString`
- `new DataValue(DataValue)` copy constructor -> use `DataValue.Copy()` instance method or `Clone()`

---

**See also**

- Related: [encoders.md](encoders.md), [source-generation.md](source-generation.md), [node-states.md](node-states.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.
