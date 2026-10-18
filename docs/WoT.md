# WoT design and provider contracts

This page describes the document-to-NodeSet conversion contracts of the WoT
types library. See [bindings and conversion](WotBindings.md) for document
authoring and [NodeSet conversion](WoTNodeSetConversion.md) for the mapping
rules. These contracts do not establish Full-profile or full JSON Schema
conformance. Registry, publication, and recovery contracts are documented
separately when the registry runtime is available.

## Contents

- [Conversion providers](#conversion-providers)
- [DataType declaration inputs](#datatype-declaration-inputs)

## Conversion providers

`WotNodeSetConverter` converts a document to a NodeSet2 model. It resolves
declarations from the captured sibling documents of the Thing before any
loaded type. Callers supply the exact original bytes and the contextual
declarations; the converter does not perform a fresh default lookup or use an
arbitrary Web resolver.
See [type binding](WotBindings.md#resolving-a-type-binding-the-local-context).

An embedded `uav:nodeSet` needs no external model fetch. Required models absent
from both the converted document and the caller's resolver remain diagnostics.

### DataType declaration inputs

Stored definitions can be referenced by graph identity, qualified name, or
portable NodeId. Lookup uses the carrying element's original context. Graph
identities need not resemble document locations; ambiguous owners fail.
`uav:dataTypeDefinition`, `uav:fieldDataTypeDefinition`, typed links, and
`uav:dataTypeSubtypeOf` contribute dependencies. Recursive fields are legal;
inheritance cycles are not. An acyclic base/derived pair in one document does
not create a cycle between documents.
Opaque metadata and literal instance values are not definition indexes.

`IWotCapturedDataTypeDefinitions` on the captured Thing resolver supplies
complete `WotDataTypeDefinitionSource` values through `DataTypeDefinitions`.
`IWotDataTypeDefinitionResolver.ResolveDataTypeDefinitionsAsync` resolves
definitions on demand. Resolvers own the documents they return; the converter
does not dispose borrowed documents and does not retain them after conversion.

Generated NodeSets declare the namespaces their Nodes own; merely referring to
a namespace does not claim it.
