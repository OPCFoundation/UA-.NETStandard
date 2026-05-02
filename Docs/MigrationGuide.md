# Migration Guide

- [Migration Guide](#migration-guide)
  - [Migrating from 1.5.378 to 1.6.x](#migrating-from-15378-to-16x)
    - [Source Generation](#source-generation)
      - [Project Structure](#project-structure)
    - [Improved Type safety](#improved-type-safety)
      - [Several built in types are now immutable value types](#several-built-in-types-are-now-immutable-value-types)
      - [ByteString](#bytestring)
      - [ArrayOf and MatrixOf](#arrayof-and-matrixof)
      - [DateTimeUtc](#datetimeutc)
      - [QualifiedName and LocalizedText](#qualifiedname-and-localizedtext)
      - [StatusCode](#statuscode)
      - [NodeId/ExpandedNodeId](#nodeidexpandednodeid)
      - [Variant, DataValue and ExtensionObject](#variant-datavalue-and-extensionobject)
        - [Deprecated boxing behavior](#deprecated-boxing-behavior)
        - [Replacement of all use of System.Object in generated code and API](#replacement-of-all-use-of-systemobject-in-generated-code-and-api)
      - [XmlElement](#xmlelement)
      - [EnumValue to represent the enumeration built in type](#enumvalue-to-represent-the-enumeration-built-in-type)
      - [Other Data Types](#other-data-types)
      - [Obsoleted APIs and replacements](#obsoleted-apis-and-replacements)
      - [APIs permanently removed](#apis-permanently-removed)
    - [Encoders and Decoders](#encoders-and-decoders)
    - [Node States](#node-states)
      - [Generics and Typed BaseVariableState and BaseVariableTypeState](#generics-and-typed-basevariablestate-and-basevariabletypestate)
      - [Predefined node processing](#predefined-node-processing)
    - [User Identity Token Handlers](#user-identity-token-handlers)
    - [Serialization and Configuration](#serialization-and-configuration)
      - [DataContract to DataType migration](#datacontract-to-datatype-migration)
      - [Configuration collection types removed](#configuration-collection-types-removed)
      - [DataContractSerializer replaced](#datacontractserializer-replaced)
      - [Newtonsoft.Json removed from Opc.Ua.Core](#newtonsoftjson-removed-from-opcuacore)
      - [ParseExtension/UpdateExtension signature changed](#parseextensionupdateextension-signature-changed)
    - [NodeState Cloning and Lifecycle](#nodestate-cloning-and-lifecycle)
      - [Clone() replaced with CreateCopy()](#clone-replaced-with-createcopy)
      - [BaseVariableState Read/Write helpers removed](#basevariablestate-readwrite-helpers-removed)
      - [OnAfterCreate gains CancellationToken](#onaftercreate-gains-cancellationtoken)
    - [Encodeable Factory and Type System](#encodeable-factory-and-type-system)
      - [IType hierarchy](#itype-hierarchy)
      - [IEncodeableTypeLookup changes](#iencodeabletypelookup-changes)
      - [IEncodeableFactoryBuilder changes](#iencodeablefactorybuilder-changes)
      - [EncodeableFactory.GlobalFactory removed](#encodeablefactoryglobalfactory-removed)
      - [ExtensionObject array helpers changed](#extensionobject-array-helpers-changed)
      - [IJsonEncodeable interface removed](#ijsonencodeable-interface-removed)
    - [Complex Types](#complex-types)
      - [ComplexTypes moved to Opc.Ua.Client assembly](#complextypes-moved-to-opcuaclient-assembly)
      - [OptionSet DataType support](#optionset-datatype-support)
    - [Session and Browser State Persistence](#session-and-browser-state-persistence)
      - [Property type changes](#property-type-changes)
      - [`IUserIdentity` on `SessionOptions` is now computed](#iuseridentity-on-sessionoptions-is-now-computed)
      - [Encoding format is not guaranteed backward compatible](#encoding-format-is-not-guaranteed-backward-compatible)
    - [Other Breaking Changes](#other-breaking-changes)
      - [Boolean default values in source-generated data types](#boolean-default-values-in-source-generated-data-types)
    - [GDS Client API modernization](#gds-client-api-modernization)
      - [`Task` → `ValueTask` on GDS client interfaces](#task--valuetask-on-gds-client-interfaces)
      - [Removal of obsolete GDS APIs](#removal-of-obsolete-gds-apis)
    - [ManagedSession and Automatic Reconnection](#managedsession-and-automatic-reconnection)
  - [Migrating from 1.05.377 to 1.05.378](#migrating-from-105377-to-105378)
    - [Asynchronous as default](#asynchronous-as-default)
    - [Observability](#observability)
  - [Migrating from 1.04 to 1.05](#migrating-from-104-to-105)
  - [Support](#support)

This document outlines the breaking changes introduced from version to version.  General principles we follow:

1. All API that is replaced with newer API is marked as [Obsolete] and code should compile and work albeit of the warnings which can be suppressed.  [Obsolete] API will be cleaned up in the next "minor" version increment. Therefore we recommend to upgrade from minor version to minor version and fixing all [Obsolete] warnings as you go along.
2. API that "cannot" be supported anymore will be removed in a minor version and migration steps documented below. We are trying to keep this to an absolute minimum.
3. Bugs or issues found in Obsoleted API are not supported.
4. We now follow semver, but do not use the major version indicator to denote breaking changes like (1) or (2) as we should if we followed related conventions. We are a small team and cannot afford to maintain previous major versions, therefore we are trying to keep cases of (2) to a minimum and expect you to upgrade to the next minor version within 6 months of release.

> Pro TIP: Point your favorite coding agent at this doc and let them take care of the migration work!

## Migrating from 1.5.378 to 1.6.x

Version 1.6 introduces a major architectural change from pre-generated code files to runtime source generation and more efficient memory use with a several major Breaking Changes requiring changes to your applications.

### Source Generation

Instead of generating code for OPC UA design files using the [ModelCompiler](https://github.com/OPCFoundation/UA-ModelCompiler), this version of the stack uses [Source Generators](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/#source-generators) to generate code behind for your project. Input into the source generator can be NodeSet2.xml files or ModelDesign.xml files (the same that ModelCompiler consumes). Source generators are Roslyn analyzers, that are called by the Roslyn compiler and emit code during the build process.

**Model compiler generated csharp code is not supported in this version!**

To migrate remove all your generated files (ending in `*.Classes.cs`, `*.Constants.cs`, etc.) and only leave the design file(s) (.xml and .csv files) in your project. Add an entry into your `csproj` file similar to the following to provide the location of the design files to the source generation process:

```xml
  <PropertyGroup>
    <!-- Optional: to configure whether to allow sub types - see model compiler documentation -->
    <ModelSourceGeneratorUseAllowSubtypes>true</ModelSourceGeneratorUseAllowSubtypes>
  </PropertyGroup>
  <ItemGroup>
    <!-- Generate code behind for the following design or nodeset2.xml files during build-->
    <AdditionalFiles Include="Boiler\Generated\BoilerDesign.csv" />
    <AdditionalFiles Include="Boiler\Generated\BoilerDesign.xml" />
    <AdditionalFiles Include="MemoryBuffer\Generated\MemoryBufferDesign.csv" />
    <AdditionalFiles Include="MemoryBuffer\Generated\MemoryBufferDesign.xml" />
    <AdditionalFiles Include="TestData\Generated\TestDataDesign.csv" />
    <AdditionalFiles Include="TestData\Generated\TestDataDesign.xml" />
  </ItemGroup>
```

The [source generator model](https://devblogs.microsoft.com/dotnet/introducing-c-source-generators/) has several benefits that go beyond custom `msbuild` targets: Among the most important is that the generator ships with the stack and therefore code that is generated conforms to the stack version that ships the analyzer (the source generator will be part of `Opc.Ua.Core` nuget package). Therefore when updating to a newer version the code generated automatically takes advantage of the improvements made across the entire stack.

Code generation during compilation also allows not just emitting code ahead of time, but also to generate code while you are developing. We now take advantage of this feature to generate `IEncodeable` implementations for partial POCO types on the fly using the `[DataType]` and `[DataTypeField]` attributes as annotation (similar to `DataContract`/`DataMember`).

The stack itself uses source generators to generate the core opc ua code. Therefore all pre-generated code files (`Generated/` folders) have been removed and are now generated at build time. As a result of using source generators to generate the stack code all `*.nodeset2.xml` files previously included as embedded zip have been removed. Also, all `*.Types.xsd` and `*.Types.bsd` files are now included as string resource instead of embedded resources. If you need access to these, use the new `Schemas.XmlAsStream` and `Schemas.BinaryAsStream` APIs in the node manager namespace which produce a utf8 stream. Alternatively you can use the existing ModelCompiler tool to generate these files.

When you encounter slower build times use incremental compilation and avoid changes to code in Opc.Ua and Opc.Ua.Core project. In addition you can change your builds to only build for your target framework using the dotnet `-f <tfm>` command line option.

#### Project Structure

New `Opc.Ua` project as an intermediate project. Impact:

- Most applications using NuGet packages are not affected. Continue linking to Opc.Ua.Core project as it includes the Opc.Ua intermediate assembly
- Assembly loading order *may* change

### Improved Type safety

#### Several built in types are now immutable value types

The `Variant` and `TypeInfo`, `NodeId`, `ExpandedNodeId`, `ExtensionObject`, `LocalizedText` and `QualifiedName` are now `readonly struct`s. This is a large breaking change and affects existing usage:

1. You cannot compare any of these types against `null`. Use the instance properties: `NodeId.IsNull`, `ExpandedNodeId.IsNull`, `QualifiedName.IsNull`, `LocalizedText.IsNullOrEmpty`, `ExtensionObject.IsNull`. In case of `ArrayOf`/`MatrixOf`/`ByteString`, you can most often just check against `IsEmpty` which checks null and emptiness.
2. The default item can be created by assigning `default`, e.g. producing `NodeId.Null` for NodeId and `QualifiedName.Null` for QualifiedName. It is recommended to use the `Null` property on these types for readability and per your coding conventions.
3. Any API that mutated an instance of one of these built in types must be replaced with methods that return a new value of the type, e.g. `NodeId.WithNamespaceIndex(ushort)` as setters were removed.

#### ByteString

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

#### ArrayOf and MatrixOf

Similar to `ByteString`, `ArrayOf<T>` and `MatrixOf<T>` are new type safe and sliceable generic value types representing non-scalar values. They are immutable meaning the values at an index inside them cannot be "set" unless they are converted to a `Span<T>` (and then reconverted to a `ArrayOf`/`MatrixOf`).

In addition to slicing and range based access, both types provide the ability to apply a NumericIndex to them.  They are efficiently stored inside a Variant as well and can be used to allocate efficiently from `ArrayPool` providing the ability to built object pooling support at the array level. `ArrayOf<T>` implicitly converts to `List<T>` but not vice versa. For API that is taking `ArrayOf<T>` as input convert any list using `ToArrayOf`. `IsEmpty` returns true if `IsNull` is true but not necessarily vice versa.

Internally an `ArrayOf`/`MatrixOf` stores a reference to "memory" and a offset and length integer. They have the same layout as `ReadOnlyMemory<T>` although this is not guaranteed to stay so in the future. All generated collection types implicitly convert to and from `ArrayOf<T>` whereby `T` is the member type of the collection type.  E.g. `VariantCollection` is effectively `ArrayOf<Variant>`.

`ArrayOf<T>` provides helper methods e.g. to  `AddItem` an item or `AddItems` of items in another `ArrayOf<T>`. Both return a new `ArrayOf<T>`, very similar to the .net ImmutableCollection classes or the `Append` or `Concat` extension methods in the `System.Linq`.

`Contains`, `IndexOf`, `Filter`, `Find`, `FindIndex` and `ConvertAll` methods mimic the Linq `Where`, `Any`, `FirstOrDefault`, `Select` or the respective methods on the `List<T>` type. Use `SafeSlice` instead of `Take` to slice up to the length and which returns an empty array instead of throwing which is what the regular Slice/range operators do.  You cannot use more advanced Linq expressions (e.g. order by or group by) without converting to a list (`ToList`) or array (`ToArray`) first. Linq is slow, so using the methods on the array type where possible will provide a performance improvement.

All generated APIs, Encoders/decoders, and the Variant type now use `ArrayOf`/`MatrixOf` instead of the previously generated/built-in non-generic collection types which have been removed.

Note that equality operators and methods now compare the content of the Array and Matrix, not just reference equality as with `T[]`. It supports checking for an empty array or matrix via `IsEmpty` and `IsNull` whereby the first checks whether the array is effectively a `ArrayOf.Empty<T>` amd the second is just a check against `ArrayOf<T>` initialized using `default` (since it is not a reference type anymore). `IsEmpty` returns true if `IsNull` is true but not necessarily vice versa.

**Change code as follows:**

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

#### DateTimeUtc

Previously the **DateTime** built in type was represented by the `System.DateTime` type. It is now represented by the `Opc.Ua.DateTimeUtc` type. This new type complies with the details of the spec without requiring external helper methods to be used. It's Value property returns the ticks, bounded by the information in Part 6 of the spec, and its time is always UTC. There are conversion operations to and from `DateTime`, but also `DateTimeOffset` and `long` and a minimal subset of `System.DateTime` API to allow for simpler porting. `DateTime` implicitly converts to `DateTimeUtc`, but not vice versa to force use of the new type.

**Change code as follows:**

- Replace `DateTime` with `DateTimeUtc` where appropriate, especially in places where comparing with `DateTime.MinValue`.
- Replace `DateTime.UtcNow` with `DateTimeUtc.Now` for UTC time "right now". `DateTime.Now` or `DateTime.Today` can be cast or replaced with its Utc variant, which is likely intended anyway as all date/time values in OPC UA are UTC.
- When assigning a `DateTime` value to a `DateTimeUtc` variable, add a cast, or use the corresponding `DateTimeUtc` constructor.

#### QualifiedName and LocalizedText

There is no implicit conversion from `string` to `QualifiedName` or `LocalizedText` anymore. For one, it flags areas where null assignment is happening implicitly, and secondly, it makes the API more explicit. E.g. previously it was possible to assign a string to a browse name which landed the browse name accidentally in namespace 0 instead of the owning namespace. If you know what you are doing you can explicitly cast the string, but it is suggested to use the new static `From` API instead.

#### StatusCode

`StatusCode` contains now not only a uint code, but also a symbol.  Symbols are interned strings and using the `StatusCodes` constants therefore come with the symbol string. This removes the need to look up the symbolic id, however, when receiving a uint code it needs to be translated to a StatusCode constant to retain the Symbol. Older API has been obsoleted with proper instructions. Since types are immutable it is important to replace mutation calls with the proper replacement method and store the returned value.

#### NodeId/ExpandedNodeId

`NodeId`s with integer identifiers (the most common case) now do not box the integer identifier anymore into an object, making the entire NodeId heap allocation free (*).  ExpandedNodeId with integer identifiers only contain an allocated namespace Uri, which is mostly a const (interned) string, reducing small allocations across both types. Because both types are now immutable, they must be mutated using the provided `With<X>`. Access to the identifier in boxed form (object) is deprecated. Instead use the `TryGetIdentifier(out uint/string/Guid/byte[])` API. If you need to get the identifier only to "stringify" it, use the `IdentifierAsText` property which avoids boxing integer identifiers.

There is no implicit conversion from `uint`/`Guid`/`string`/`byte[]` to `NodeId`/`ExpandedNodeId` to ensure assignment of null reference types (byte array and string) is not happening implicitly and to prevent accidental conversion of these identifiers into namespace 0. It also removes  hidden behavior such as parsing during assignments and flags areas where a proper Null/default NodeId should be inserted/returned. Use the explicit cast (e.g. `(NodeId)[(byte)3, 2]`) instead. For the previous implicit conversion from `string` to `NodeId` conversion use `NodeId.Parse` and `ExpandedNodeId.Parse`. On the same note, the constructor taking a string and no namespace index has been deprecated as it required a string to parse. Use Parse/TryParse instead.

> (*) Note that NodeId leverages the new `uint` field to cache the HashCode of a "non-uint" "Identifier", which provides faster lookup using NodeId/ExpandedNodeId as key.

#### Variant, DataValue and ExtensionObject

Previously the `Variant` was a *mutable* struct containing a `TypeInfo` and `Value` property allowing setting the inner state and returning `object`.  All value types thus were implicitly boxed to object and landing on the heap. The new `Variant` only boxes value types > 8 bytes in size (*), and stores the rest in a union.  `TypeInfo`, previously a class, also now is stored as a 4 byte type (with padding).

The `ExtensionObject` was a reference type wrapping a `NodeId` and a body as a reference type of `object`. The `ExtensionObject` is now an immutable value type with type-safe access to its body.

##### Deprecated boxing behavior

Access to the `Value` property of `Variant` is marked as [Obsolete] to discourage use in favor of casting to `<Type>` or `Get<Type>()` (both throw) or preferably `bool TryGet(out <Type> value)` calls. The same applies to the `Value` property of `DataValue`. The APIs perform any required conversion between `BuiltInType.Int32` and `BuiltInType.Enumeration` as well as arrays of `BuiltInType.Byte` and `BuiltInType.ByteString`. This also applies to the `Body` property of `ExtensionObject`. Here prefer the use of `TryGetEncodeable<T>` and `TryGetBinary, TryGetJson, TryGetXml`.

Creating a `Variant` or `ExtensionObject` via the constructor taking a `object` parameter is also marked [Obsolete] to encourage using type safe API to create a Variant (and thus not storing the wrong value in the inner `object` variable that cannot be converted out again or makes the Variant a null variant unexpectedly).

In some cases it is desirable to gain access to what was returned from the now obsoleted `Value` property. To make the fact that the returned value is likely boxed, the new API is named `AsBoxedObject()`. While the Variant has conversion operators from all supported types and corresponding `From(<Type> value)` APIs, it is sometimes necessary to convert from `System.Object`. Note that `AsBoxedObject()` does not return .net array types but `ArrayOf<T>`, and `ByteString` for - yes - ByteString. `Value` property converts to the old style type expectations.

To perform conversion from `<T>` to a Variant, helper methods are available in `VariantHelper` static class. These helper methods are split into ones that use reflection and ones that do not. Overall, use of these helper methods is not recommended in favor of switching on the type information in the Variant.

> `DateTimeUtc` and `EnumValue` are always stored unboxed inside a Variant. However, converting a enum (`System.Enum`) to an EnumValue requires boxing on .net standard and .net framework.
> All other built in value types (`ExtensionObject`, `NodeId`, `QualifiedName`, `LocalizedText`, `Uuid`, etc.) are > 8 bytes in size and are therefore boxed when stored inside a Variant.
> Future improvements will make certain types like `ArrayOf` be stored *spliced* inside the Variant (where the array pointer is stored in the object, and length/offset inside the union).

##### Replacement of all use of System.Object in generated code and API

`Variant` is now the type reflecting the OPC UA Variant type in all API. That means all generated API now uses Variant instead of `System.Object` and all `Value` Properties are `Variant` too.  This provides type safety and removes the need for Reflection via `GetType()` when the underlying type already is `Variant`.

**System.Object and Variant comparable operations:**

- *Casting*: Casting from Variant to built in system type "will just work" the same way as casting from the object, e.g. `object a; uint b = (uint)a;` is equivalent to `Variant a; uint b = (uint)a;`. Both throw `InvalidCastException` if the cast is not possible.
- *Pattern matching*: If you use is pattern matching use the new `TryGet/TryGetStructure` calls. If you cast using as, use the same or if you prefer a default value in case the Variant has a different type, the `Get<BuiltInType>` or `GetStructure<T>` or equivalent array returning methods ending in `Array`. They do not throw, but return the default value.
- *Reflection*: Use `TypeInfo` property on Variant to obtain metadata for for example switching.
- *Conversion*: Previously TypeInfo had support to Cast an object aligned with Variant behavior. These API have been removed in favor of the `ConvertTo[<]BuiltInType]()` members or `ConvertTo(BuiltInType target)`. NOTE: Under the hood `IConvertible` is used, which means integer values are boxed.

To migrate, perform the following general replacements in your code:

- If you are setting the `Value` property of Variant, change the code to create a `new Variant` with the value via constructor or `Variant.From` or by casting to `Variant`.
- Generally replace all `IList<object>` with `IList<Variant>`
- Generally replace all `ref object` with `ref Variant`.
- In addition: for all callbacks registered in `BaseVariableState` change the callback signature to use `Variant` instead of `object` and `Variant[]` instead of `object[]`.
- For all remaining `object[]` instances, replace with `ArrayOf<Variant>` or `IList<Variant>` judiciously and depending on context.
- Keep all *casts* from **Variant** (not from its Value property) to the concrete type if you intend to preserve throw behavior. For any pattern matching (is/as) use `TryGet` if you need to check the result, or `Get<BuiltInType>` if you do not want to throw but are happy with the default value.

> IMPORTANT: Care must be taken to not accidentally box a `Variant` value into an `object`.  E.g. current code like `object f = state.Value` will not be flagged by the compiler but must be replaced with `Variant f = state.Value` to remain type safe. Here it is best to use `var` for locals which requires no code changes.

**Remaining work:**

- Assignments to Variants and casting from variant to type should be dealt with via implicit conversion except for Structures. Here change code from `Value = <structure>` to `Value = Variant.FromStructure(<structure>)` and `<structure> = Value` to `Value.TryGetStructure(out <structure>)`.
- Any pattern matching conversion used must be replaced with the TryGet/TryGetStructure pattern of Variant for checked conversions, e.g. `a = Value as uint?` must be replaced with `Value.TryGet(out uint a)` which most often produces more concise code and avoids the check for nullable result of the conversion. The same applies to `is` matching.
- For Variable and VariableType node state classes that provide a narrowed "Value" via generic `<T>` any access to `T Value` incurs a heavy type check.  It is recommended to use `WrappedValue` instead when possible for assignment and access.
- While most assignments work implicitly, use `TypeInfo.GetDefaultVariantValue` instead of `TypeInfo.GetDefaultValue` to initialize a variant value to a default that is `!= Variant.Null`.

#### XmlElement

Previously the `XmlElement` built in type was represented by the `System.Xml.XmlElement` system type. While officially a deprecated, there is now a value type `XmlElement` that merely wraps a string but provides conversion operations to `System.Xml.XmlElement` and `System.Linq.Xml.XNode` as well as validation and equality/hashing operations. Normally you just need to remove `using System.Xml` and code continues working as is.  If you need to have access to the `System.Xml.XmlElement` cast or use the `ToXmlElement` method.

> `XmlElement` types are compared via a normalized version of the XML `string` contained, which removes all whitespace before comparing. This can result in some ambiguity, but operates well enough for test operations. For complete equality, cast to XNode and use `DeepEquals`.

#### EnumValue to represent the enumeration built in type

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

#### Other Data Types

All generated data types implementing `IEncodeable` are now equality comparable using `==` and `!=` and implement `IEquatable<T>`. Equality defaults to the `IsEqual` implementation of the `IEncodeable` interface. In addition `ToString()` and `GetHashCode()` are implemented making all generated data types effectively equivalent to `record` classes with the exception of supporting `with` expressions.

**Change code as follows:**

No changes are required, however there can be subtle bugs exposed, e.g.:

- When comparing data type instances for reference equality, use `ReferenceEquals`, instead of `==` or `!=` operators. You can use the `RefEqualityComparer<T>` helper when creating Dictionaries that use the type as key and require reference equality semantics for it.
- When testing for `null`, use `is null` for more performant code.

#### Obsoleted APIs and replacements

- `NodeId(string text)` -> `NodeId.Parse(string)`
- `NodeId(object identifier, ushort namespaceIndex)` -> typed constructors: `new NodeId(uint, ushort)`, `new NodeId(Guid, ushort)`, `new NodeId(string, ushort)`, `new NodeId(ByteString, ushort)`
- `NodeId.Create(object identifier, string namespaceUri, NamespaceTable namespaceTable)` -> typed overloads: `NodeId.Create(string|uint|Guid|ByteString, string, NamespaceTable)`
- `NodeId.Identifier` -> `TryGetIdentifier(out uint|string|Guid|ByteString)` or `IdentifierAsString`
- `NodeId.SetNamespaceIndex(ushort)` -> `WithNamespaceIndex(ushort)` (store the return value)
- `NodeId.SetIdentifier(IdType, object)` -> `WithIdentifier(uint|string|Guid|ByteString)` or typed constructors
- `ExpandedNodeId(string text)` -> `ExpandedNodeId.Parse(string)`
- `ExpandedNodeId(object identifier, ushort namespaceIndex, string namespaceUri, uint serverIndex)` -> typed constructors: `new ExpandedNodeId(uint|Guid|string|ByteString, ushort, string, uint)`
- `ExpandedNodeId.Identifier` -> `TryGetIdentifier(out uint|string|Guid|ByteString)` or `IdentifierAsString`
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
- `Variant.Value` -> use `Variant.TryGet`, cast, or `AsBoxedObject` if absolutely necessary.
- `DataValue.GetValue`, `DataValue.GetValueOrDefault`, ,`DataValue.Value` -> use `DataValue.WrappedValue` and the new API on Variant (e.g. `Get[Type]`,  `TryGet`)
- `new DataValue(StatusCode)` and `new DataValue(StatusCode, DateTimeUtc)` -> use `DataValue.FromStatusCode(StatusCode)` and `DataValue.FromStatusCode(StatusCode, DateTimeUtc)`. The constructors suffered from a C# overload resolution bug where `new DataValue(42)` silently resolved to `DataValue(StatusCode)` instead of `DataValue(Variant)`, losing the value.

#### APIs permanently removed

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

### Encoders and Decoders

The `IEncoder` and `IDecoder` interfaces have changed to use `ArrayOf<T>` instead of Collection and `System.Array`. Also generic versions of `ReadEncodeable`/`WriteEncodeable` and `ReadEnumerated`/`WriteEnumerated` were added with the ones taking a `System.Type` paramter removed. There are 2 versions of `ReadEncodeable<T>` and `WriteEncodeable<T>`, one with a `new()` constraint bypassing `EncodeableFactory` lookups, and one with a `ExpandedNodeId` used to look up the concrete type and allowing to use `IEncodeable` as `T` constraint.

Furthermore, `ReadArray`/`WriteArray` methods have been removed. A new `ReadVariantValue` and `WriteVariantValue` method has been added to write "only" the content (Value) of a Variant, or read the value using `TypeInfo` information. Neither supports `DiagnosticInfo` but also supports writing and reading scalar values. The return type is Variant. To read a `TypeInfo.Scalars.Variant` use ReadVariant instead because a Variant cannot contain a scalar Variant.

In addition to the generic Write/ReadEnumerated, the non-generic `EnumValue` variants were also added.

- `IEncoder`: `WriteEnumerated(string, EnumValue)`, `WriteEnumeratedArray(string, ArrayOf<EnumValue>)`
- `IDecoder`: `ReadEnumerated(string)` returning `EnumValue`, `ReadEnumeratedArray(string)` returning `ArrayOf<EnumValue>`

Custom encoder/decoder implementations must adjust to comply with the new interfaces.

**Change code as follows:**

- Change all `ReadEncodeable`/`WriteEncodeable` calls to use the type as part of the generic expression. E.g. `ReadEncodeable("field", typeof(T))` to `ReadEncodeable<T>("field")` and `WriteEncodeable("field", value, typeof(T))` to `WriteEncodeable("field", value)`. If value is a type that cannot be created using a parameterless constructor, pass the type id as last argument.
- Change all `ReadEnumerated` calls to use the enumeration type as part of the generic expression. E.g. `ReadEnumerated("field", typeof(T))` to `ReadEnumerated<T>("field")`.
- Change calls to `ReadArray`/`WriteArray` to use `ReadVariantValue` and `WriteVariantValue` and extract the value from the returned `Variant` based on the type you intended to read. A good example can be found in `BaseComplexType` `EncodeProperty` and `DecodeProperty`.

### Node States

#### Generics and Typed BaseVariableState and BaseVariableTypeState

With the changes to Variant, the generic node state classes reflecting the inner value of the variant "value" have been changed to not rely on "casting" from object to T. The conversion is "baked in" when creating an instance of a typed state using a "builder" struct. Whether the value is scalar, array or matrix is irrelevant to which builder to use. There are 3 situations and the respective builder struct to use:

1. T is a built in type -> use `VariantBuilder`
2. T is a instance of `IEncodeable` (a complex structure) -> Use `StructureBuilder<T>` where T is the name of the structure.
3. T is an instance of Enum (an enumeration) -> Use `EnumBuilder<T>` where T is the name fo the enumeration type.

E.g. to create an instance of a `PropertyState<T>` where T is `ArrayOf<ExtensionObject>` use

``` csharp
    var state = new PropertyState<ArrayOf<ExtensionObject>>.Implementation<VariantBuilder>(parent)
    // or
    var state = PropertyState<ArrayOf<ExtensionObject>>.With<VariantBuilder>(parent)
```

To create an instance of a `PropertyState<T>` where T is `Argument` (an IEncodeable type) use

``` csharp
    var state = new PropertyState<Argument>.Implementation<StructureBuilder<Argument>>(parent)
    // or
    var state = PropertyState<Argument>.With<StructureBuilder<Argument>>(parent)
```

To create an instance of a `PropertyState<T>` where T is `MatrixOf<ComplexType>` (an IEncodeable type) use

``` csharp
    var state = new PropertyState<MatrixOf<ComplexType>>.Implementation<StructureBuilder<ComplexType>>(parent)
    // or
    var state = PropertyState<MatrixOf<ComplexType>>.With<StructureBuilder<ComplexType>>(parent)
```

Note: While this looks clunky, it does not use reflection and comes with 0 allocation including any allocations for `Func` or `Action` delegates and works around .net limitations regarding overload resolution for generic arguments (which also required the use of `FromStructure` or `FromEnumeration` on the Variant type instead of using `From`). In future versions it is possible the source generator could generate away some of the redundancies in the above expressions.

#### Predefined node processing

Filling the predefined node state list is now generated as source code.  This means the predefined Variable and Object instance states are the generated classes, not the root node states. This has an
impact on the AddBehaviorToPredefinedNode implementations which should use the received node state as "activeNode" and attach functionality to it instead of creating a active node.

Example guidance (mirrors BoilerNodeManager): the node passed to `AddBehaviorToPredefinedNode` is already the generated instance state, so attach behavior directly to it instead of creating a new state. This ensures the predefined list stays consistent and the generated type-specific fields are available.

``` csharp
    protected override void AddBehaviorToPredefinedNode(
        ISystemContext context,
        NodeState node)
    {
        if (node is BoilerTypeState boiler)
        {
            var activeNode = boiler;
            activeNode.Temperature.OnSimpleWriteValue = OnTemperatureWrite;
            activeNode.FlowRate.OnSimpleWriteValue = OnFlowRateWrite;
        }

        // Add callbacks to the node here if necessary
        // If not needed you do not need to implement this call at all.
    }
```

See [NodeStates](./../Stack/Opc.Ua.Types/State/readme.md) document for more information.

### User Identity Token Handlers

**Breaking Change**: Identity tokens no longer perform cryptographic operations directly. New handler pattern introduced for better security and lifetime management.

**Before**:

```csharp
    var token = new X509IdentityToken();
    token.Encrypt(certificate, nonce, securityPolicy, context);
    token.Decrypt(certificate, nonce, securityPolicy, context);
    var signature = token.Sign(data, securityPolicy);
    bool isValid = token.Verify(data, signature, securityPolicy);
```

**After**:

```csharp
    var token = new X509IdentityToken();
    using var handler = token.AsTokenHandler();
    handler.Encrypt(certificate, nonce, securityPolicy, context);
    handler.Decrypt(certificate, nonce, securityPolicy, context);
    var signature = handler.Sign(data, securityPolicy);
    bool isValid = handler.Verify(data, signature, securityPolicy);
```

**New Interface**:

```csharp
    public interface IUserIdentityTokenHandler :
        IDisposable, ICloneable, IEquatable<IUserIdentityTokenHandler>
    {
        UserIdentityToken Token { get; }
        string DisplayName { get; }
        UserTokenType TokenType { get; }

        void UpdatePolicy(UserTokenPolicy userTokenPolicy);
        void Encrypt(X509Certificate2 receiverCertificate, byte[] receiverNonce,
                    string securityPolicyUri, IServiceMessageContext context, ...);
        void Decrypt(X509Certificate2 certificate, Nonce receiverNonce,
                    string securityPolicyUri, IServiceMessageContext context, ...);
        SignatureData Sign(byte[] dataToSign, string securityPolicyUri);
        bool Verify(byte[] dataToVerify, SignatureData signatureData, string securityPolicyUri);
    }
```

**Migration Required**:

1. **Replace direct token crypto operations**:

    ```csharp
    // OLD - Direct operations on token
    userIdentityToken.Encrypt(...);

    // NEW - Use handler pattern
    using var handler = userIdentityToken.AsTokenHandler();
    handler.Encrypt(...);
    ```

2. **Proper lifetime management**:

    ```csharp
    // For temporary use - dispose immediately
    using var handler = token.AsTokenHandler();
    handler.Encrypt(...);

    // For storage - clone and dispose original
    var storedHandler = token.AsTokenHandler().Copy();
    // Use storedHandler later, remember to dispose when done
    ```

3. **Available token handlers**:
   - `AnonymousIdentityTokenHandler`
   - `UserNameIdentityTokenHandler`  
   - `X509IdentityTokenHandler`
   - `IssuedIdentityTokenHandler`

### Serialization and Configuration

Because **Data Contract serialization** is not AOT compliant and does not support trimming, all use of `DataContract` in the configuration has been removed. Instead, the source generator enables generating *IEncodeable* implementations using the `DataType` and `DataTypeField` attributes which are now consequently used for all configuration. Because the configuration is now `IEncodeable` the existing encoders and decoders (in particular the new `XmlParser` which parses Xml and allows out of order fields) compliant with Part 6 can be used to serialize and deserialize all configuration and configuration extensions.

> Generated Data types still support DataContract based serialization, however, consider this a deprecated feature.

#### DataContract to DataType migration

All configuration DTO classes (`ApplicationConfiguration`, `ServerConfiguration`, `TraceConfiguration`, `TransportConfiguration`, `ServerSecurityPolicy`, `OAuth2ServerSettings`, `OAuth2Credential`, `GlobalDiscoveryServerConfiguration`, `CertificateGroupConfiguration`, `BrowserOptions`, etc.) migrated from `[DataContract]`/`[DataMember]` to source-generated `[DataType]`/`[DataTypeField]` attributes and are now `partial` classes.

**Change code as follows:**

- Replace `[DataContract(Namespace = ...)]` with `[DataType(Namespace = ...)]` and `[DataMember(...)]` with `[DataTypeField(...)]` on custom configuration subtypes.
- Add the `partial` keyword to any subclass of these configuration types.
- Custom configuration extension types must implement `IEncodeable` (the `[DataType]` source generator handles this automatically for `partial` classes).
- Code using reflection to inspect `[DataContract]`/`[DataMember]` attributes must switch to `[DataType]`/`[DataTypeField]`.

#### Configuration collection types removed

All `List<T>`-based collection wrappers for configuration types have been removed and replaced with `ArrayOf<T>`: `ServerSecurityPolicyCollection`, `TransportConfigurationCollection`, `SamplingRateGroupCollection`, `ReverseConnectClientCollection`, `ReverseConnectClientEndpointCollection`, `ServerRegistrationCollection`, `CertificateIdentifierCollection`, `CertificateGroupConfigurationCollection`, `OAuth2ServerSettingsCollection`, `OAuth2CredentialCollection`.

See the [ArrayOf and MatrixOf](#arrayof-and-matrixof) section for migration guidance on using `ArrayOf<T>`.

#### DataContractSerializer replaced

`DataContractSerializer` has been removed from config loading and persistence paths:

- `ApplicationConfiguration.LoadWithNoValidation` uses `XmlParser`/`IEncodeable.Decode()`. Existing XML config files should remain loadable.
- Browser and session state persistence switched from XML to OPC UA Binary encoding. **Old persisted files cannot be loaded** — delete and re-save.
- `SecuredApplication` uses `SecuredApplicationEncoding` helpers instead of `DataContractSerializer`.

#### Newtonsoft.Json removed from Opc.Ua.Core

`Newtonsoft.Json` is no longer a dependency of `Opc.Ua.Core`. Projects relying on its transitive availability must add an explicit reference:

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
```

#### ParseExtension/UpdateExtension signature changed

`ParseExtension<T>()` and `UpdateExtension<T>()` now require `T` to implement `IEncodeable`. New delegate-based overloads were added for custom decoding:

```csharp
// Generic overload (T must implement IEncodeable)
var config = configuration.ParseExtension<MyConfig>();

// Delegate overload for custom decoding
var config = configuration.ParseExtension<MyConfig>(
    new XmlQualifiedName("MyConfig", myNamespace),
    decoder => { var c = new MyConfig(); c.Decode(decoder); return c; });
```

### NodeState Cloning and Lifecycle

#### Clone() replaced with CreateCopy()

`NodeState.Clone()` is now a concrete method that calls `CreateCopy()` + `CopyTo()`. The new `protected abstract NodeState CreateCopy()` must be overridden by all direct NodeState subclasses.

```csharp
// Before
public override object Clone()
{
    var clone = new MyNodeState(Parent);
    CopyTo(clone);
    return clone;
}

// After
protected override NodeState CreateCopy()
{
    return new MyNodeState(Parent);
}
```

If you had custom deep-copy logic beyond what `CopyTo()` does, override `CopyTo()` instead.

#### BaseVariableState Read/Write helpers removed

The `protected ServiceResult Read(object, ref object)` and `protected object Write(object)` methods were removed.
Use the `CopyPolicy` property or the new `CopyOnWrite` bool directly with `CoreUtils.Clone()` for copy-on-read/write semantics.

#### OnAfterCreate gains CancellationToken

`OnAfterCreate(ISystemContext, NodeState)` now has an optional `CancellationToken ct = default` parameter.
Existing overrides compile (source-compatible) but are **binary-incompatible** — pre-compiled assemblies won't match at runtime.

```csharp
protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
{
    base.OnAfterCreate(context, node, ct);
}
```

### Encodeable Factory and Type System

#### IType hierarchy

New type abstraction layer: `IType` (base) with `IBuiltInType`, `IEnumeratedType` (new), and `IEncodeableType` (now extends `IType`). Many APIs return `IType` instead of `Type`:

- `TypeInfo.GetSystemType(ExpandedNodeId, IEncodeableTypeLookup)` → returns `IType` (was `Type`). Use `.Type` property to get the CLR `Type`.
- The overload `TypeInfo.GetSystemType(BuiltInType, int valueRank)` was removed.

#### IEncodeableTypeLookup changes

- `TryGetEncodeableType<T>()` removed.
- Added: `TryGetEnumeratedType(ExpandedNodeId, out IEnumeratedType?)`, `TryGetType(XmlQualifiedName, out IType?)`.

#### IEncodeableFactoryBuilder changes

- `AddEncodeableType(ExpandedNodeId, Type)` → renamed to `AddType(ExpandedNodeId, Type)`.
- Added: `AddEnumeratedType(IEnumeratedType)`, `AddEnumeratedType(ExpandedNodeId, IEnumeratedType)`.
- `AddEncodeableType(Type)` and `AddEncodeableTypes(Assembly)` now have AOT annotations (`[DynamicallyAccessedMembers]`, `[RequiresUnreferencedCode]`).

#### EncodeableFactory.GlobalFactory removed

The `[Obsolete]` static `EncodeableFactory.GlobalFactory` was removed. `EncodeableFactory.Create()` renamed to `Fork()`. Use `ServiceMessageContext.Factory` instead.

#### ExtensionObject array helpers changed

`ExtensionObject.ToArray(object, Type)` and `ToList<T>(object)` removed. Use `extensionObjects.GetStructuresOf<T>()` or `ExtensionObject.ToArray<T>(ArrayOf<ExtensionObject>)`.

#### IJsonEncodeable interface removed

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

### Complex Types

#### ComplexTypes moved to Opc.Ua.Client assembly

Core complex type interfaces and default (non-reflection-emit) implementations moved from `Opc.Ua.Client.ComplexTypes` to `Libraries/Opc.Ua.Client/ComplexTypes/`.
Namespace remains `Opc.Ua.Client.ComplexTypes`. If you used the default constructors without specifying the builder, and want to use the Reflection.Emit based type builders,
you need to change your code to call `ComplexTypeSystem.Create(...)` instead of `new ComplexTypeSystem(...)` which now uses the new default builder not supporting Reflection.Emit.

#### OptionSet DataType support

Concrete Structure-backed sub-types of the abstract `OptionSet` DataType (`i=12755`) are now automatically registered by the default `ComplexTypeSystem` builder with a new runtime class `Opc.Ua.Encoders.OptionSet` (in `Stack/Opc.Ua.Types`). Bit-field metadata is resolved from `DataTypeDefinition` (`EnumDefinition`) or, as a fallback, synthesized from the `OptionSetValues` property (`LocalizedText[]`).

Impact on existing code:

- **Source-breaking for custom `IComplexTypeBuilder` implementations**: a new member `AddOptionSetType(QualifiedName, ExpandedNodeId, ExpandedNodeId, ExpandedNodeId, ExpandedNodeId, EnumDefinition)` was added to `IComplexTypeBuilder`. Custom implementations must provide it.
- The Reflection.Emit builder in `Opc.Ua.Client.ComplexTypes` throws `NotSupportedException` from `AddOptionSetType`; callers relying on the Reflection.Emit path for OptionSet sub-types should switch to the default builder (`new ComplexTypeSystem(session)`).
- No wire-format changes: encoders/decoders continue to route through `IEncodeableFactory` → `IEncodeableType.CreateInstance`, which now yields `Opc.Ua.Encoders.OptionSet` for registered sub-types.
- UInteger-backed OptionSet DataTypes remain treated as their underlying unsigned integer in a `Variant` (unchanged).

### Session and Browser State Persistence

**Breaking Change**: Persistence switched from `DataContractSerializer` XML to `IEncoder` and `IDecoder`. `BrowserState`, `SessionState`, `SessionOptions`, `SubscriptionState`, and `MonitoredItemState` are annotated with `[DataType]` and use the standard `Encode`/`Decode` methods generated by the source generator.

To register the state types with the encodeable factory:

```csharp
context.Factory.Builder.AddOpcUaClientDataTypes();
```

#### Property type changes

The following property types have changed to use the new stack value types:

| Class | Property | Old Type | New Type |
|---|---|---|---|
| `SessionState` | `ServerNonce` | `byte[]?` | `ByteString` |
| `SessionState` | `ClientNonce` | `byte[]?` | `ByteString` |
| `SessionState` | `ServerEccEphemeralKey` | `byte[]?` | `ByteString` |
| `SessionState` | `Timestamp` | `DateTime` | `DateTimeUtc` |
| `SessionState` | `Subscriptions` | `SubscriptionStateCollection?` | `ArrayOf<SubscriptionState>` |
| `SubscriptionState` | `MonitoredItems` | `MonitoredItemStateCollection` | `ArrayOf<MonitoredItemState>` |
| `SubscriptionState` | `Timestamp` | `DateTime` | `DateTimeUtc` |

#### `IUserIdentity` on `SessionOptions` is now computed

`SessionOptions.Identity` (`IUserIdentity?`) is no longer a serialized field. It is a computed property backed by `UserIdentityToken? IdentityToken`, which is the actual serialized field:

```csharp
public partial record class SessionOptions
{
    // Serialized field
    [DataTypeField(Order = 2, StructureHandling = StructureHandling.ExtensionObject)]
    public UserIdentityToken? IdentityToken { get; set; }

    // Computed — not serialized
    public IUserIdentity? Identity
    {
        get => IdentityToken != null ? new UserIdentity(IdentityToken) : null;
        set => IdentityToken = value?.TokenHandler?.Token;
    }
}
```

#### Encoding format is not guaranteed backward compatible

The encoding format for session state has changed. Existing persisted session state files **cannot** be loaded by the new `SessionConfiguration.Create()` method. Handle restore failures and re-persist the new session state.

### Other Breaking Changes

#### Boolean default values in source-generated data types

**Breaking Change**: Boolean properties on source-generated data types now correctly default to `false` instead of `true`.

Generated code produced by the model compiler contained a bug because it inverted the default value for boolean fields in generated data types. Boolean fields without an explicit `<DefaultValue>` in the model design XML were initialized to `true` instead of `false` as expected and defined in Part 6. This has been fixed.

**Impact**: Any code that creates instances of source-generated data types and relies on boolean properties being `true` by default must now explicitly set those properties to `true`. This primarily affects PubSub configuration types:

| Type | Property | Old Default | New Default |
|---|---|---|---|
| `PubSubConfigurationDataType` | `Enabled` | `true` | `false` |
| `PubSubConnectionDataType` | `Enabled` | `true` | `false` |
| `WriterGroupDataType` | `Enabled` | `true` | `false` |
| `ReaderGroupDataType` | `Enabled` | `true` | `false` |
| `DataSetWriterDataType` | `Enabled` | `true` | `false` |
| `DataSetReaderDataType` | `Enabled` | `true` | `false` |
| `PublishedDataSetCustomSourceDataType` | `CyclicDataSet` | `true` | `false` |

Other affected types include all source-generated structures with boolean fields (e.g., `AggregateConfiguration.TreatUncertainAsBad`, `MonitoringParameters.DiscardOldest`, `CreateSubscriptionRequest.PublishingEnabled`) as well as 
some hand-written types in `Opc.Ua.Types` (such as `BrowseDescription`, `RelativePathElement`).

**Migration**: Add explicit initialization where your code depends on `true` as the default:

```csharp
// Before (relied on incorrect true default)
var connection = new PubSubConnectionDataType
{
    Name = "MyConnection"
};

// After (explicitly set Enabled)
var connection = new PubSubConnectionDataType
{
    Enabled = true,
    Name = "MyConnection"
};
```

### GDS Client API modernization

The `Opc.Ua.Gds.Client.Common` package has undergone a significant cleanup. Two breaking changes affect almost every consumer of the GDS / LDS / Server-Push client APIs.

#### `Task` → `ValueTask` on GDS client interfaces

**Breaking Change**: All asynchronous methods on `IGlobalDiscoveryServerClient`, `ILocalDiscoveryServerClient`, and `IServerPushConfigurationClient` (and their concrete implementations) now return `ValueTask` / `ValueTask<T>` instead of `Task` / `Task<T>`.

**Rationale**: Many GDS operations complete synchronously when a session is already established. Returning `ValueTask` avoids the per-call `Task` allocation on those fast paths and keeps the surface consistent with the rest of the modernized client stack.

**Impact**: Pure `await` callers require **no change** — `await` works identically on `Task` and `ValueTask`. However, two patterns require a small adjustment.

| Pattern | Old (`Task`) | New (`ValueTask`) |
|---|---|---|
| `await` on the return value | works | works (no change) |
| Block synchronously via `.Result` / `.Wait()` | works | use `.AsTask().Result` / `.AsTask().Wait()` |
| Combine results with `Task.WhenAll` / `Task.WhenAny` | works | call `.AsTask()` first |
| Await the same return value more than once | works | **not supported** — call `.AsTask()` first |

> **Important**: A `ValueTask` may be awaited only once and the underlying value source must not be observed after the operation has completed. If you need to await a result more than once, fan it out across multiple consumers, or pass it to anything other than a single `await`, materialize it via `.AsTask()` first.

```csharp
// Before
Task<NodeId> registration = gds.RegisterApplicationAsync(application, ct);
NodeId id = await registration;
await Task.WhenAll(registration, otherTask);          // worked

// After
ValueTask<NodeId> registration = gds.RegisterApplicationAsync(application, ct);
NodeId id = await registration;                       // unchanged

// Multi-await / Task.WhenAll: materialize first
Task<NodeId> asTask = gds.RegisterApplicationAsync(application, ct).AsTask();
await Task.WhenAll(asTask, otherTask);
```

#### Removal of obsolete GDS APIs

**Breaking Change**: All `[Obsolete]` synchronous wrappers, APM (`Begin*`/`End*`) methods, and other deprecated members have been removed from the GDS client surface.

**Affected APIs (non-exhaustive)**:

- All synchronous wrappers on `GlobalDiscoveryServerClient` (~25 methods such as `FindApplication`, `RegisterApplication`, `StartNewKeyPairRequest`, …) — use the corresponding `*Async` overload returning `ValueTask`/`ValueTask<T>`.
- All synchronous wrappers on `ServerPushConfigurationClient` (~14 methods such as `UpdateCertificate`, `ReadTrustList`, `ApplyChanges`, …) — use the `*Async` overload.
- APM (`Begin*` / `End*`) overloads on `LocalDiscoveryServerClient` (e.g. `BeginFindServers` / `EndFindServers`) — use the `*Async` overload.
- The capability identifier constants are now source-generated as `Opc.Ua.ServerCapability` (singular, e.g. `ServerCapability.GDS`, `ServerCapability.LDS`, `ServerCapability.DA`). The `[Obsolete] public const string` shims previously exposed on the value-type `ServerCapability` class (now `ServerCapabilityInfo` in `Opc.Ua.Gds.Client`) have been removed. The runtime `ServerCapabilities.csv` parsing path (which never actually loaded — the resource was not embedded) has been replaced by the generated dictionary `ServerCapability.All`. The instance enumerable previously named `ServerCapabilityCatalog` is now `Opc.Ua.Gds.Client.ServerCapabilities` and its `Find` returns `ServerCapabilityInfo`.
- `RegisteredApplication` is now a `sealed record`; the obsolete extension methods that wrapped its property access have been removed — use the record properties directly.
- `CertificateWrapper` is now `sealed` and no longer implements `IEncodeable`; remove any code that treated it as an encodeable.

**Migration**:

```csharp
// Before
var apps = gds.FindApplication(uri);                       // sync wrapper
var caps = ServerCapability.GlobalDiscoveryServer;         // obsolete shim

// After
var apps = await gds.FindApplicationAsync(uri, ct);
var caps = ServerCapability.GDS;
```

If you currently rely on a `[Obsolete]` member, switch to the `Async` equivalent and apply the `ValueTask` migration notes above. If a particular API has no direct replacement, the migration is described inline in the XML doc comment of the replacement member.

### ManagedSession and Automatic Reconnection

Version 1.6 introduces `ManagedSession`, a wrapper around `Session` that automatically handles connection lifecycle including reconnection and server redundancy failover.

#### Key Changes

- **`ManagedSessionFactory`** is a **new** factory that creates `ManagedSession` instances which handle reconnection and failover automatically. Use this when you want managed-session behavior.
- **`DefaultSessionFactory`** is **unchanged** — it continues to create raw `Session` instances. Existing code that constructs `DefaultSessionFactory` directly keeps the same behavior in 1.6.
- **`SessionReconnectHandler`** is **retained** as a supported legacy entry point for callers that already manage raw `Session` instances. It is **not** marked obsolete in 1.6, but it now requires the wrapped `ISession` to be a `Session` (or a derived type) — passing a `ManagedSession` (or any other `ISession` facade) throws `NotSupportedException`, since those facades drive their own reconnect / failover state machine. New code should still prefer `ManagedSessionFactory` / `ManagedSession.CreateAsync`.

For a deeper architectural picture of how `Session`, `ManagedSession`, `SessionReconnectHandler`, and the subscription engines fit together, see [Sessions, Reconnection, and Subscription Engines](Sessions.md).

#### Migration Steps

**If you use `DefaultSessionFactory`:**
No code changes are required — `DefaultSessionFactory` still returns raw `Session`. To opt into automatic reconnection and redundancy failover, switch to `ManagedSessionFactory`:

```csharp
// Still supported in 1.6 — DefaultSessionFactory creates raw Session:
var defaultFactory = new DefaultSessionFactory(telemetry);
ISession rawSession = await defaultFactory.CreateAsync(...);

// Opt in to managed reconnect/failover — ManagedSessionFactory creates ManagedSession:
var managedFactory = new ManagedSessionFactory(telemetry);
ISession managedSession = await managedFactory.CreateAsync(...);
```

Both factories implement `ISessionFactory`. `ManagedSessionFactory` internally uses a `DefaultSessionFactory` to create the raw `Session` and then wraps it in a `ManagedSession`; the public surface is unchanged.

**If you use `SessionReconnectHandler`:**

`SessionReconnectHandler` continues to work in 1.6 against `Session` instances. The pattern below is unchanged — only the obsolete diagnostic has been removed:

```csharp
ISession session = await new DefaultSessionFactory(telemetry).CreateAsync(...);
using var reconnectHandler = new SessionReconnectHandler(telemetry);
session.KeepAlive += (s, e) =>
{
    if (e.Status != null && ServiceResult.IsNotGood(e.Status))
    {
        reconnectHandler.BeginReconnect(session, 1000, OnReconnectComplete);
    }
};
```

`SessionReconnectHandler.BeginReconnect` only supports the legacy `Session` class (or types derived from it). Passing a `ManagedSession` throws `NotSupportedException`. If you have already migrated to `ManagedSession`, **do not** wrap it with a `SessionReconnectHandler` — `ManagedSession` already runs its own reconnect state machine. Use the `StateChanged` event to observe transitions:

```csharp
ISession session = await ManagedSession.CreateAsync(
    configuration, endpoint,
    reconnectPolicy: new ReconnectPolicy
    {
        Strategy = BackoffStrategy.Exponential,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30)
    });
// Reconnection is automatic — no manual handler needed
((ManagedSession)session).StateMachine.StateChanged += (s, e) =>
{
    Console.WriteLine($"Session state: {e.NewState}");
};
```

Or, equivalently, via the factory:

```csharp
var factory = new ManagedSessionFactory(telemetry);
ISession session = await factory.CreateAsync(...);
```

#### Configuring Reconnection Policy

```csharp
var policy = new ReconnectPolicy
{
    Strategy = BackoffStrategy.Exponential,  // or Linear, Constant
    InitialDelay = TimeSpan.FromSeconds(1),
    MaxDelay = TimeSpan.FromSeconds(30),
    MaxRetries = 0,         // 0 = unlimited
    JitterFactor = 0.1      // ±10% jitter
};
```

#### Server Redundancy

`ManagedSession` automatically reads server redundancy information and can failover to backup servers:

```csharp
var session = await ManagedSession.CreateAsync(
    configuration, endpoint,
    redundancyHandler: new DefaultServerRedundancyHandler());
```

#### Service Call Behavior During Reconnect

When the session is reconnecting, service calls (Read, Write, Browse, etc.) automatically wait until the session is reconnected. This is transparent to the caller — no special handling needed. If reconnection fails permanently, calls will throw `ServiceResultException`.

#### Fluent Builder, V2 Subscriptions, and Dependency Injection

Version 1.6 introduces a fluent builder for `ManagedSession`, exposes the new options-based subscription API on the managed session, and adds Microsoft.Extensions.DependencyInjection integration for Azure / ASP.NET Core / generic-host scenarios.

**Fluent builder:**

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .WithSessionName("MyClient")
    .WithSessionTimeout(TimeSpan.FromSeconds(60))
    .WithReconnectPolicy(p => p with
    {
        Strategy = BackoffStrategy.Exponential,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30)
    })
    .WithServerRedundancy()
    .ConnectAsync(ct);
```

`Build()` returns an immutable `ManagedSessionOptions` snapshot; `ConnectAsync()` wraps `Build()` and `ManagedSession.CreateAsync(...)` so most callers can use the builder directly.

**New subscription API on `ManagedSession`:**

`ManagedSession` now exposes an `ISubscriptionManager` (the V2 options-based API) alongside the classic `Subscriptions` property. The V2 engine is the default for `ManagedSession`. Use `UseSubscriptionEngine(ClassicSubscriptionEngineFactory.Instance)` on the builder if you need the legacy classic engine instead — accessing `SubscriptionManager` then throws `InvalidOperationException`.

```csharp
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;

var handler = new MyNotificationHandler();   // : ISubscriptionNotificationHandler

ISubscription subscription = session.AddSubscription(handler,
    new SubscriptionOptions
    {
        PublishingInterval = TimeSpan.FromMilliseconds(500),
        KeepAliveCount = 10,
        LifetimeCount = 100
    });

subscription.TryAddMonitoredItem(
    "ServerStatus_CurrentTime",
    VariableIds.Server_ServerStatus_CurrentTime,
    o => o with
    {
        SamplingInterval = TimeSpan.FromMilliseconds(250),
        QueueSize = 10
    },
    out IMonitoredItem _);
```

The `SubscriptionOptions` and `MonitoredItemOptions` records used by this API live in `Opc.Ua.Client.Subscriptions` and `Opc.Ua.Client.Subscriptions.MonitoredItems`. They are distinct from the classic types of the same names in the `Opc.Ua.Client` namespace; use namespace aliases (or fully-qualified names) when both are visible in the same file.

The classic `ManagedSession.Subscriptions` collection (V1 `Subscription` objects) remains supported. Mixing classic subscriptions with the V2 manager on the same session is allowed; classic subscriptions still receive notifications via the internal `SubscriptionBridge` when the V2 engine is active.

**Dependency Injection:**

`AddOpcUaClient` registers a `ManagedSession` factory delegate that lazily connects on first use:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Client;

services.AddOpcUaClient(opt =>
{
    opt.Configuration = applicationConfiguration;
    opt.Session = new ManagedSessionOptions
    {
        Endpoint = endpoint,
        ReconnectPolicy = new ReconnectPolicyOptions
        {
            Strategy = BackoffStrategy.Exponential
        }
    };
});

// Resolve and connect on first use:
var sessionFactory = serviceProvider
    .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
ManagedSession session = await sessionFactory(ct);
```

The factory caches the connected session — subsequent awaits return the same instance. The DI registration also exposes `ITelemetryContext`, `ISessionFactory` (a `DefaultSessionFactory` configured with the V2 engine), `ManagedSessionFactory`, and the top-level `OpcUaClientOptions`.

This iteration uses single-instance options (no named/keyed registrations); the underlying V2 manager consumes options via `IOptionsMonitor<T>` unfiltered. For one-off use, the `AddSubscription`/`TryAddMonitoredItem` extensions adapt plain options snapshots into the required `IOptionsMonitor<T>` automatically. Named-options DI is deferred to a future iteration.

### `INodeCache` consolidation

Version 1.6 collapses the two parallel node-cache contracts into a single
public interface and removes the remaining synchronous wrappers from the
cache surface.

#### Key changes

- **`ILruNodeCache` is removed.** `LruNodeCache` now implements only
  `INodeCache`. All members previously on `ILruNodeCache` (the
  NodeId-keyed `Get*` family and `LoadTypeHierarchyAsync`) are now
  members of `INodeCache`.
- **All async methods on `INodeCache` return `ValueTask` /
  `ValueTask<T>`** (was `Task<T>` for `FindAsync`, `FetchNodeAsync`,
  `FetchNodesAsync`, `FetchSuperTypesAsync`, `FindReferencesAsync`).
  Callers that simply `await` these methods need no change. Callers
  that store the result in a `Task` variable, return the bare task, or
  re-await the same task must wrap with `.AsTask()` once.
- **`void INodeCache.LoadUaDefinedTypes(ISystemContext)` is removed.**
  The LRU implementation populates lazily and the prior method body
  was a no-op. Drop the call from your code; the cache is ready to
  use.
- **`bool ILruNodeCache.IsTypeOf(NodeId, NodeId)` is removed.** Use
  `IAsyncTypeTable.IsTypeOfAsync(NodeId, NodeId, CancellationToken)`
  instead — `INodeCache` inherits from `IAsyncTypeTable` so the
  method is reachable on the same instance.
- **`NodeCacheObsolete` synchronous extensions are removed.** The
  blocking wrappers `Find`, `FetchNode`, `FetchNodes`, `FetchSuperTypes`,
  `FindReferences`, `GetDisplayText`, `IsKnown`, `FindSuperType`, and
  `Exists` no longer compile. Switch to the matching async methods
  (`FindAsync`, `FetchNodeAsync`, …).
- **`LruNodeCacheExtensions` is renamed to `NodeCacheExtensions`** and
  retargets `this INodeCache cache`. The ExpandedNodeId convenience
  overloads (`GetNodeAsync`, `GetNodesAsync`, `GetValueAsync`,
  `GetValuesAsync`, `GetReferencesAsync`) keep the same shape. The
  `IsTypeOf(this ILruNodeCache, ExpandedNodeId, NodeId)` extension is
  removed.
- **`void Clear()` is unchanged.** It is a pure local-state mutation
  with no I/O and remains synchronous on the interface.

#### Subsequent slim-down of `INodeCache` (post-merge)

After the initial merge, `INodeCache` was further trimmed to remove
duplications and demote pure helpers to extension methods. Removed
**from the interface** (still callable on a `INodeCache` reference via
`NodeCacheExtensions`):

| Removed from interface | Replacement |
|---|---|
| `GetSuperTypeAsync(NodeId, ct)` | inherited `IAsyncTypeTable.FindSuperTypeAsync(NodeId, ct)` (identical semantics — the interface methods returned the same `NodeId.Null`-on-miss value) |
| `FindReferencesAsync(ExpandedNodeId, NodeId, bool, bool, ct)` | inherited `IAsyncNodeTable.FindAsync(source, refType, isInverse, includeSubtypes, ct)` (identical signature). A thin extension method preserves the old name for callers that prefer it. |
| `FindReferencesAsync(ArrayOf<ExpandedNodeId>, ArrayOf<NodeId>, …)` | extension method on `NodeCacheExtensions` (same signature). |
| `FindAsync(ArrayOf<ExpandedNodeId>, ct)` | extension method on `NodeCacheExtensions` that loops over the inherited `FindAsync(ExpandedNodeId)`. |
| `FetchSuperTypesAsync(ExpandedNodeId, ct)` | extension method that loops `FindSuperTypeAsync`. |
| `GetNodeWithBrowsePathAsync(NodeId, ArrayOf<QualifiedName>, ct)` | extension method on `NodeCacheExtensions`. |
| `GetBuiltInTypeAsync(NodeId, ct)` | extension method on `NodeCacheExtensions`. |
| `GetDisplayTextAsync(INode | ExpandedNodeId | ReferenceDescription, ct)` | three extension methods on `NodeCacheExtensions`. |

External implementations of `INodeCache` no longer need to implement
these members. Call sites that already used `using Opc.Ua;` keep
compiling unchanged because the extensions live in the same namespace.

#### Two complementary lookup families

The merged `INodeCache` deliberately keeps two name conventions side by
side. The XML doc on `INodeCache` spells this out as well:

| Family | Identity | Result | Behavior |
|---|---|---|---|
| `Find*` / `Fetch*` | `ExpandedNodeId` | nullable | `Find*` consults the cache, then the server; `Fetch*` always re-reads from the server. |
| `Get*` | `NodeId` | non-nullable / throws | LRU-style direct hit; cheaper for in-process callers that already have a local `NodeId`. |

#### Migration recipes

```csharp
// Before — Task-returning + sync helpers
INodeCache cache = session.NodeCache;
cache.LoadUaDefinedTypes(session.SystemContext); // removed
ArrayOf<INode?> nodes = await cache.FindAsync(nodeIds);
Task<Node?> tn = cache.FetchNodeAsync(nodeId);   // returned Task<T>
bool isType = cache.IsTypeOf(sub, super);        // sync, was on ILruNodeCache
```

```csharp
// After — single INodeCache surface, all async, no sync IsTypeOf
INodeCache cache = session.NodeCache;
ArrayOf<INode?> nodes = await cache.FindAsync(nodeIds);
ValueTask<Node?> tn = cache.FetchNodeAsync(nodeId);
bool isType = await cache.IsTypeOfAsync(sub, super);
```

#### Implementer / mock impact

External implementations of `INodeCache` must:

1. Add the `Get*` methods (NodeId-keyed) plus
   `LoadTypeHierarchyAsync`.
2. Convert their `Task<T>`-returning members to `ValueTask<T>`.
3. Remove any `LoadUaDefinedTypes(ISystemContext)` override or call.

Test doubles (Moq) need new `Setup` calls covering the `Get*` methods
they exercise. Members moved to `NodeCacheExtensions` (e.g.
`GetBuiltInTypeAsync`, `GetNodeWithBrowsePathAsync`,
`GetDisplayTextAsync`, the `ExpandedNodeId`-keyed
`FindReferencesAsync`/`FindAsync` overloads,
`FetchSuperTypesAsync`) no longer need to be set up — the extensions
delegate to the smaller core surface automatically.

#### Out of scope

`Session.TypeTree` continues to return a sync `ITypeTable` adapter for
compatibility with code that uses the synchronous type-table surface
in the server-side stack. Removing that adapter is out of scope of
this change; if you only consume `INodeCache.TypeTree` (the
`IAsyncTypeTable`), you can keep using the async surface end-to-end.

## Migrating from 1.05.377 to 1.05.378

### Asynchronous as default

The server now supports AsyncNodeManagers, see [Server Async (TAP) Support](Docs/AsyncServerSupport.md). The client APIs are async by default and all synchronous and APM
based API has been deprecated. To migrate update your code to use the Async version of all API if possible. Not recommended but for expedience sake you can use the Async
version and make it sync by appending `GetAwaiter().GetResult()` to it.

### Observability

[Observability](Docs/Observability.md) via `ITelemetryContext` in preparation for better DI support. See documentation for breaking changes.

## Migrating from 1.04 to 1.05

- A few features are still missing to fully comply for 1.05, but certification for V1.04 is still possible with the 1.05 release.

## Support

For additional migration support:

- Review sample applications in the repository
- Check unit tests for usage patterns
- Consult the OPC Foundation community forums
- Report issues in the GitHub repository

---
