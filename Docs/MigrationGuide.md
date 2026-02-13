# Migration Guide

- [Migration Guide](#migration-guide)
  - [From 1.5.378 to 1.6.x](#from-15378-to-16x)
    - [Source Generation](#source-generation)
      - [Project Structure](#project-structure)
    - [Several UA built in types are now immutable](#several-ua-built-in-types-are-now-immutable)
      - [Variant](#variant)
        - [Deprecated boxing behavior](#deprecated-boxing-behavior)
        - [Replacement of all use of object in generated code and API](#replacement-of-all-use-of-object-in-generated-code-and-api)
      - [QualifiedName and LocalizedText](#qualifiedname-and-localizedtext)
      - [StatusCode](#statuscode)
      - [NodeId/ExpandedNodeId](#nodeidexpandednodeid)
      - [ArrayOf/MatrixOf](#arrayofmatrixof)
      - [Obsoleted APIs and replacements](#obsoleted-apis-and-replacements)
      - [APIs permanently removed](#apis-permanently-removed)
    - [Node State handling](#node-state-handling)
    - [User Identity Token Handlers](#user-identity-token-handlers)
  - [From 1.05.377 to 1.05.378](#from-105377-to-105378)
    - [Asynchronous as default](#asynchronous-as-default)
    - [Observability](#observability)
  - [From 1.04 to 1.05](#from-104-to-105)
  - [Support](#support)

This document outlines the breaking changes introduced from version to version.  General principles we follow:

1. All API that is replaced with newer API is marked as [Obsolete] and code should compile and work albeit of the warnings which can be suppressed.  [Obsolete] API will be cleaned up in the next "minor" version increment. Therefore we recommend to upgrade from minor version to minor version and fixing all [Obsolete] warnings as you go along.
2. API that "cannot" be supported anymore will be removed in a minor version and migration steps documented below. We are trying to keep this to an absolute minimum.
3. Bugs or issues found in Obsoleted API are not supported.
4. We now follow semver, but do not use the major version indicator to denote breaking changes like (1) or (2) as we should if we followed related conventions. We are a small team and cannot afford to maintain previous major versions, therefore we are trying to keep cases of (2) to a minimum and expect you to upgrade to the next minor version within 6 months of release.

## From 1.5.378 to 1.6.x

Version 1.6 introduces a major architectural change from pre-generated code files to runtime source generation and more efficient memory use with a several major Breaking Changes requiring changes to your applications.

### Source Generation

Instead of generating code for OPC UA design files using the [ModelCompiler](https://github.com/OPCFoundation/UA-ModelCompiler), this version of the stack uses [Source Generator](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/#source-generators)s to generate code behind for your project. Input into the source generator can be NodeSet2.xml files or ModelDesign.xml files (the same that ModelCompiler consumes). Source generators are Roslyn analyzers, that are called by the Roslyn compiler and emit code during the build process.

**Model compiler generated csharp code** is not supported in this version.

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

The [source generator model](https://devblogs.microsoft.com/dotnet/introducing-c-source-generators/) has several benefits that go beyond custom `msbuild` targets: Among the most important is that the generator ships with the stack and therefore code that is generated conforms to the stack version that ships the analyzer (the source generator is part of Opc.Ua.Core nuget package). Therefore when updating to a newer version the code generated takes automatically advantage of the improvements made across the entire stack. Code generation during compilation also allows not just emitting code ahead of time, but also to generate code while you are developing. We intend to take advantage of this to generate data types and node states from stub code on the fly in future releases including enabling faster migration of code by injecting functionality such as conversion between types.

The stack itself uses source generators to generate the core opc ua code. Therefore all pre-generated code files (`Generated/` folders) have been removed and are now generated at build time. As a result of using source generators to generate the stack code all `*.nodeset2.xml` files previously included as embedded zip have been removed. Also, all `*.Types.xsd` and `*.Types.bsd` files are now included as string resource instead of embedded resources. If you need access to these, use the new `Schemas.XmlAsStream` and `Schemas.BinaryAsStream` APIs in the node manager namespace which produce a utf8 stream. Alternatively you can use the existing ModelCompiler tool to generate these files.

When you encounter slower build times use incremental compilation and avoid changes to code in Opc.Ua and Opc.Ua.Core project. In addition you can change your builds to only build for your target framework using the dotnet `-f <tfm>` command line option.

#### Project Structure

New `Opc.Ua` project as an intermediate project.

Impact:

- Most applications using NuGet packages are not affected. Continue linking to Opc.Ua.Core project as it includes the Opc.Ua intermediate assembly
- Assembly loading order may change

### Several UA built in types are now immutable

The `Variant` and `TypeInfo`, `NodeId`, `ExpandedNodeId`, `ExtensionObject`, `LocalizedText` and `QualifiedName` are now `readonly struct`s. This is a larger breaking change and  affects existing usage:

1. You cannot compare any of these types against `null`. Use the instance properties: `NodeId.IsNull`, `ExpandedNodeId.IsNull`, `QualifiedName.IsNull`, `LocalizedText.IsNullOrEmpty`, `ExtensionObject.IsNull`.
2. The default item can be created by assigning `default`, e.g. producing `NodeId.Null` for NodeId and `QualifiedName.Null` for QualifiedName. It is recommended to use the `Null` property on these types for readability and per your coding conventions.
3. Any API that mutated an instance of one of these built in types must be replaced with methods that return a new value of the type, e.g. `NodeId.WithNamespaceIndex(ushort)` as setters were removed.

#### Variant

Previously the `Variant` was a *mutable* struct containing a TypeInfo and Value property allowing setting the inner state and returning `object`.  All value types thus were implicitly boxed to object and landing on the heap. The new Variant only boxes value types > 8 bytes in size (*), and stores the rest in a union.  `TypeInfo`, previously a class, also now is stored as a 4 byte type (with padding).

##### Deprecated boxing behavior

Access to the Value property of Variant is marked as [Obsolete] to discourage use in favor of casting to `<Type>` or `Get<Type>()` (both throw) or preferably `bool TryGet(out <Type> value)` calls. The APIs perform any required conversion between `BuiltInType.Int32` and `BuiltInType.Enumeration` as well as arrays of `BuiltInType.Byte` and `BuiltInType.ByteString`.

Creating a Variant via the constructor taking a `object` parameter is also marked [Obsolete] to encourage using type safe API to create a Variant (and thus not storing the wrong value in the inner `object` variable that cannot be converted out again or makes the Variant a null variant unexpectedly).

In some cases it is desirable to gain access to what was returned from the now obsoleted `Value` property. To make the fact that the returned value is likely boxed, the new API is named `AsBoxedValue()`. While the Variant has conversion operators from all supported types and corresponding `From(<Type> value)` APIs, it is sometimes desirable to convert from an object. To perform conversion from `<T>` to a Variant, helper methods are available in `VariantHelper` static class that provide additional overloads of From.

> (*) Note that Enumerations while sized always below or equal 8 bytes are only stored "unboxed" in .net 8 or higher, but boxed in .net framework due to missing APIs.
> `DateTime` is always stored unboxed.
> All other built in value types (`ExtensionObject`, `NodeId`, `QualifiedName`, `LocalizedText`, `Uuid`, etc.) are > 8 bytes in size and are therefore boxed when stored inside a Variant.
> `ArrayOf`/`MatrixOf` are stored *spliced* inside the Variant (where the array pointer is stored in the object, and length/offset inside the union).

##### Replacement of all use of object in generated code and API

`Variant` is now the type reflecting the OPC UA type in all API.  That means all generated API now uses Variant instead of `object` and all Value Properties are Variant too.  This provides type safety and removes the need for Reflection via `GetType()`. To migrate, perform the following general replacements in your code:

**Change code as follows:**

- Generally replace all `IList<object>` with `VariantCollection`
- Generally replace all `ref object` with `ref Variant`.
- In addition: for all callbacks registered in `BaseVariableState` change the callback signature to use `Variant` instead of `object` and `Variant[]` instead of `object[]`.
- For all remaining `object[]` instances, replace with `VariantCollection` judiciously.

> IMPORTANT: Care must be taken to not accidentally box a `Variant` value into an `object`.  E.g. current code like `object f = state.Value` will not be flagged by the compiler but must be replaced with `Variant f = state.Value` to remain type safe. Here it is best to use `var` for locals which requires no code changes.

**Remaining work:**

- Assignments to Variants and casting from variant to type should be dealt with via implicit conversion except for Structures. Here change code from `Value = <structure>` to `Value = Variant.FromStructure(<structure>)` and `<structure> = Value` to `Value.TryGetStructure(out <structure>)`.
- Any pattern matching conversion used must be replaced with the TryGet/TryGetStructure pattern of Variant for checked conversions, e.g. `a = Value as uint?` must be replaced with `Value.TryGet(out uint a)` which most often produces more concise code and avoids the check for nullable result of the conversion. The same applies to `is` matching.
- For Variable and VariableType node state classes that provide a narrowed "Value" via generic `<T>` any access to `T Value` incurs a heavy type check.  It is recommended to use `WrappedValue` instead when possible for assignment and access.
- While most assignments work implicitly, use `TypeInfo.GetDefaultVariantValue` instead of `TypeInfo.GetDefaultValue` to initialize a variant value to a default that is `!= Variant.Null`.

#### QualifiedName and LocalizedText

There is no implicit conversion from `string` to `QualifiedName` or `LocalizedText` anymore. For one, it flags areas where null assignment is happening implicitly, and secondly, it makes the API more explicit. E.g. previously it was possible to assign a string to a browse name which landed the browse name accidentally in namespace 0 instead of the owning namespace. If you know what you are doing you can explicitly cast the string, but it is suggested to use the new static `From` API instead.

#### StatusCode

`StatusCode` contains now not only a uint code, but also a symbol.  Symbols are interned strings and using the `StatusCodes` constants therefore come with the symbol string. This removes the need to look up the symbolic id, however, when receiving a uint code it needs to be translated to a StatusCode constant to retain the Symbol. Older API has been obsoleted with proper instructions. Since types are immutable it is important to replace mutation calls with the proper replacement method and store the returned value.

#### NodeId/ExpandedNodeId

`NodeId`s with integer identifiers (the most common case) now do not box the integer identifier anymore into an object, making the entire NodeId heap allocation free (*).  ExpandedNodeId with integer identifiers only contain an allocated namespace Uri, which is mostly a const (interned) string, reducing small allocations across both types. Because both types are now immutable, they must be mutated using the provided `With<X>`. Access to the identifier in boxed form (object) is deprecated. Instead use the `TryGetIdentifier(out uint/string/Guid/byte[])` API. If you need to get the identifier only to "stringify" it, use the `IdentifierAsText` property which avoids boxing integer identifiers.

There is no implicit conversion from `uint`/`Guid`/`string`/`byte[]` to `NodeId`/`ExpandedNodeId` to ensure assignment of null reference types (byte array and string) is not happening implicitly and to prevent accidental conversion of these identifiers into namespace 0. It also removes  hidden behavior such as parsing during assignments and flags areas where a proper Null/default NodeId should be inserted/returned. Use the explicit cast (e.g. `(NodeId)[(byte)3, 2]`) instead. For the previous implicit conversion from `string` to `NodeId` conversion use `NodeId.Parse` and `ExpandedNodeId.Parse`. On the same note, the constructor taking a string and no namespace index has been deprecated as it required a string to parse. Use Parse/TryParse instead.

> (*) Note that NodeId leverages the new `uint` field to cache the HashCode of a "non-uint" "Identifier", which provides faster lookup using NodeId/ExpandedNodeId as key.

#### ArrayOf/MatrixOf

`ArrayOf<T>` and `MatrixOf<T>` are new type safe and sliceable value types. They are immutable meaning the values at an index inside them cannot be "set" unless they are converted to a `Span<T>` (and then reconverted to a `ArrayOf`/`MatrixOf`). In addition to slicing and range based access, both types provide the ability to apply a NumericIndex to them.  They are efficiently stored inside a Variant as well and can be used to allocate efficiently from ArrayPool providing the ability to built object pooling support at the array level. All *generated* collection types now convert to and from `ArrayOf<T>` to use them in API now taking `ArrayOf<T>` as input instead of the generated type. Internally an ArrayOf/MatrixOf stores a reference to "memory" and a offset and length integer. They have the same layout as `ReadOnlyMemory<T>` although this is not guaranteed to stay so in the future.

#### Obsoleted APIs and replacements

- `NodeId(string text)` -> `NodeId.Parse(string)`
- `NodeId(object identifier, ushort namespaceIndex)` -> typed constructors: `new NodeId(uint, ushort)`, `new NodeId(Guid, ushort)`, `new NodeId(string, ushort)`, `new NodeId(byte[], ushort)`
- `NodeId.Create(object identifier, string namespaceUri, NamespaceTable namespaceTable)` -> typed overloads: `NodeId.Create(string|uint|Guid|byte[], string, NamespaceTable)`
- `NodeId.Identifier` -> `TryGetIdentifier(out uint|string|Guid|byte[])` or `IdentifierAsString`
- `NodeId.SetNamespaceIndex(ushort)` -> `WithNamespaceIndex(ushort)` (store the return value)
- `NodeId.SetIdentifier(IdType, object)` -> `WithIdentifier(uint|string|Guid|byte[])` or typed constructors
- `ExpandedNodeId(string text)` -> `ExpandedNodeId.Parse(string)`
- `ExpandedNodeId(object identifier, ushort namespaceIndex, string namespaceUri, uint serverIndex)` -> typed constructors: `new ExpandedNodeId(uint|Guid|string|byte[], ushort, string, uint)`
- `ExpandedNodeId.Identifier` -> `TryGetIdentifier(out uint|string|Guid|byte[])` or `IdentifierAsString`
- `NodeIdExtensions.IsNull(NodeId)` -> `NodeId.IsNull`
- `NodeIdExtensions.IsNull(ExpandedNodeId)` -> `ExpandedNodeId.IsNull`
- `QualifiedNameExtensions.IsNull(QualifiedName)` -> `QualifiedName.IsNull`
- `LocalizedTextExtensions.IsNullOrEmpty(LocalizedText)` -> `LocalizedText.IsNullOrEmpty`
- `QualifiedName.IsNull(QualifiedName)` -> use `QualifiedName.IsNull`
- `ExtensionObject.IsNull(ExtensionObject)` -> use `ExtensionObject.IsNull`
- Implicit cast from `string` or `byte[]` to `NodeId`/`ExpandedNodeId` -> use explicit cast or `From()` API
- Implicit cast from `string` to `LocalizedText`/`QualifiedName` -> use explicit cast or `From()` API
- `Format` and `ToString` APIs return `string.Empty` instead of `null` for `NodeId`, `QualifiedName`, `ExpandedNodeId`, `LocalizedText` to prevent NullReferenceExceptions

#### APIs permanently removed

- `ICloneable`/`Clone()`/`MemberwiseClone()` on the immutable built-in types -> use assignment for copies
- Setters removed from immutable types:
  - `QualifiedName.Name`/`QualifiedName.NamespaceIndex` -> `WithName(string)`/`WithNamespaceIndex(ushort)`
  - `LocalizedText.Translations`/`LocalizedText.TranslationInfo` -> `WithTranslations(...)`/`WithTranslationInfo(...)`
  - `ExtensionObject.Body`/`ExtensionObject.TypeId` -> constructors and `WithTypeId(...)`
  - `NodeId.NamespaceIndex`/`NodeId.IdType`/`NodeId.Identifier` setters -> use constructors or `WithIdentifier(...)`
- Implicit cast operator of type string to NodeId/ExpandedNodeId -> use Parse/TryParse
- `WriteGuid(string, Guid)` -> use `WriteGuid(string, Uuid)`
- `WriteGuidArray(string, IList<Guid>)` -> use `WriteGuidArray(string, IList<Uuid>)`
- new `Variant(Guid)` -> use `Variant.From(Uuid)` or `new Variant(Uuid)`

### Node State handling

Filling the predefined node state list is now generated as source code.  This means the predefined Variable and Object instance states are the generated classes, not the root node states. This has an
impact on the AddBehaviorToPredefinedNode implementations which should use the received node state as "activeNode" and attach functionality to it instead of creating a active node.

Example guidance (mirrors BoilerNodeManager): the node passed to `AddBehaviorToPredefinedNode` is already the generated instance state, so attach behavior directly to it instead of creating a new state. This ensures the predefined list stays consistent and the generated type-specific fields are available.

    ```csharp
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

## From 1.05.377 to 1.05.378

### Asynchronous as default

The server now supports AsyncNodeManagers, see [Server Async (TAP) Support](Docs/AsyncServerSupport.md). The client APIs are async by default and all synchronous and APM based API has been deprecated. To migrate update your code to use the Async version of all API if possible. Not recommended but for expedience sake you can use the Async version and make it sync by appending `GetAwaiter().GetResult()` to it.

### Observability

[Observability](Docs/Observability.md) via `ITelemetryContext` in preparation for better DI support. See documentation for breaking changes.

## From 1.04 to 1.05

- A few features are still missing to fully comply for 1.05, but certification for V1.04 is still possible with the 1.05 release.

## Support

For additional migration support:

- Review sample applications in the repository
- Check unit tests for usage patterns
- Consult the OPC Foundation community forums
- Report issues in the GitHub repository

---
