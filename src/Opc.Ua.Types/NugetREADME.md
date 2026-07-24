# OPC UA .NET Standard — Types (standard NodeSet proxies)

`OPCFoundation.NetStandard.Opc.Ua.Types` is the generated type
assembly for the standard OPC UA NodeSet (OPC 10000-5 / Part 5). It
contains the strongly-typed proxies for the standard
`ObjectType` / `VariableType` / `ReferenceType` definitions
(`BaseObjectState`, `BaseVariableState`, `MethodState`, all standard
NodeIds, browse names, status codes, error codes, …) plus the
companion data type classes (`Argument`, `Range`, `EUInformation`,
`HistoryUpdateResult`, …).

## Overview

Reference this package when you write a server-side
`NodeManager`, register a custom information model, or consume the
standard address-space proxies on the client side.

The classes in this package are emitted from the standard XML NodeSet
by the OPC UA source generators (`Opc.Ua.SourceGeneration.Stack`).

## WoT / NodeSet conversion

The package also exposes the dependency-light `Opc.Ua.Wot`
conversion surface used by the WoT source generator and WoT Connectivity
runtime. `WotNodeSetConverter` converts `UANodeSet` models to and from
Thing Models / Thing Descriptions.

The default output is native-first: the versioned `uav:nodes` projection
covers the complete UANodeSet schema and no `uav:nodeSet` envelope is
emitted when it reconstructs equivalently. Configure
`WotNodeSetConverterOptions.PreservationMode` as:

* `WhenRequired` (default) — use the envelope only for a demonstrated
  unsupported/future construct;
* `Always` — include an explicit byte-exact archival envelope;
* `Never` — reject any conversion that cannot be proven without the
  envelope (recommended for conformance tests).

Unmapped WoT JSON-LD members are retained individually as
pointer-addressed, digest-protected residue in standard NodeSet
`Extensions`; mapped OPC UA facts are never duplicated there.

Typed-reference links also use NamespaceUri-qualified model names such as
`ua:HasOrderedComponent` directly in `rel`, alongside a definitive
`uav:refType` ExpandedNodeId when needed. These names improve model
semantics without replacing ExpandedNodeIds for instance identity.

## Target frameworks

`net472`, `net48`, `netstandard2.0`, `netstandard2.1`, `net8.0`,
`net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Source-Generated Data Types guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/SourceGeneratedDataTypes.md)
for the model-driven extension story (how to add companion specs and
plug new generated NodeSets into the stack).
