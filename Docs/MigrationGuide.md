# Migration Guide

This document outlines the breaking changes introduced from version to version

## 1.5.378 -> 1.6

Version 1.6  introduces a major architectural change from pre-generated code files to runtime source generation. This improves maintainability and reduces the repository size while providing better developer experience.

## Major Breaking Changes

### Source Generation Architecture

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

### Several built in types are now immutable

The Variant, NodeId, ExpandedNodeId, ExtensionObject, LocalizedText and QualifiedName are now readonly structs. This affects existing usage:

1. You cannot compare these against `null`. Use the instance properties: `NodeId.IsNullNodeId`, `ExpandedNodeId.IsNull`, `QualifiedName.IsNullQn`, `LocalizedText.IsNullOrEmpty`, `ExtensionObject.IsNull`.
2. The default item can be created by assigning `default`, e.g. producing `NodeId.Null` for NodeId and `QualifiedName.Null` for QualifiedName.
3. Any API that mutated instances must be replaced with methods that return a new value, e.g. `NodeId.WithNamespaceIndex(ushort)`.

#### StatusCode

StatusCode contains now not only a uint code, but also a symbol.  Symbols are interned and using the StatusCodes constants therefore come with the symbol string. This removes the need to look up the symbolic id, however, when receiving a uint code it needs to be translated to a StatusCode constant to retain the Symbol.

Older API has been obsoleted with proper instructions. Since types are immutable it is important to replace mutation calls with the proper replacement method and store the returned value.

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
- `NodeIdExtensions.IsNull(NodeId)` -> `NodeId.IsNullNodeId`
- `NodeIdExtensions.IsNull(ExpandedNodeId)` -> `ExpandedNodeId.IsNull`
- `QualifiedNameExtensions.IsNull(QualifiedName)` -> `QualifiedName.IsNullQn`
- `LocalizedTextExtensions.IsNullOrEmpty(LocalizedText)` -> `LocalizedText.IsNullOrEmpty`
- `QualifiedName.IsNull(QualifiedName)` -> use `QualifiedName.IsNullQn`
- `ExtensionObject.IsNull(ExtensionObject)` -> use `ExtensionObject.IsNull`

#### APIs permanently removed

- `ICloneable`/`Clone()`/`MemberwiseClone()` on the immutable built-in types -> use assignment for copies
- Setters removed from immutable types:
  - `QualifiedName.Name`/`QualifiedName.NamespaceIndex` -> `WithName(string)`/`WithNamespaceIndex(ushort)`
  - `LocalizedText.Translations`/`LocalizedText.TranslationInfo` -> `WithTranslations(...)`/`WithTranslationInfo(...)`
  - `ExtensionObject.Body`/`ExtensionObject.TypeId` -> constructors and `WithTypeId(...)`
  - `NodeId.NamespaceIndex`/`NodeId.IdType`/`NodeId.Identifier` setters -> use constructors or `WithIdentifier(...)`

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

### Project Structure

New `Opc.Ua` project as an intermediate project.

Impact:

- Most applications using NuGet packages are not affected. Continue linking to Opc.Ua.Core project as it includes the Opc.Ua intermediate assembly
- Assembly loading order may change

### User Identity Token Handlers (Major Breaking Change)

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

### Build System Changes

**Breaking Change**: Source generators integrated into build process.

**New Dependencies**:

- `Opc.Ua.SourceGeneration.Stack` (build-time analyzer)
- `Opc.Ua.SourceGeneration.Core` (for model compilation)

**Impact**:

- Initial build may be slower due to source generation
- Generated files no longer visible in IDE by default
- Better incremental compilation

**Migration Required**:

- Ensure compatible .NET SDK version
- Update any custom build scripts that depend on pre-generated files

## Troubleshooting

### Source Generation Issues

**Problem**: Generated files not appearing or compilation errors

**Solutions**:

1. Clean and rebuild solution
2. Check .NET SDK version (10.0+ recommended)
3. Verify source generator references in project files
4. Check for analyzer/source generator conflicts

### Identity Token Migration Issues

**Problem**: Compilation errors with identity token operations

**Solutions**:

1. Replace direct token crypto operations with handler pattern
2. Ensure proper `using` statements for handlers
3. Use `.AsTokenHandler()` extension method
4. Implement proper disposal pattern

### Performance Issues

**Problem**: Slower build times

**Solutions**:

1. Use incremental compilation and avoid changes to code in Opc.Ua and Opc.Ua.Core project
2. Only build for your target framework

## Support

For additional migration support:

- Review sample applications in the repository
- Check unit tests for usage patterns
- Consult the OPC Foundation community forums
- Report issues in the GitHub repository

---
