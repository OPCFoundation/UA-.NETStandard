# OPC UA .NET Standard — Types

`OPCFoundation.NetStandard.Opc.Ua.Types` provides foundational OPC UA
values such as `NodeId`, `Variant`, `DataValue`, `ByteString` and `ArrayOf<T>`,
encoding and message-context infrastructure, and node-state base classes
such as `BaseObjectState`, `BaseVariableState` and `MethodState`.
Generated standard NodeSet model and state types are supplied by the
dependent `OPCFoundation.NetStandard.Opc.Ua.Core.Types` package.

## Overview

Reference this package for built-in values, encoding/context infrastructure
or the dependency-light WoT conversion surface below. Use `Opc.Ua.Core.Types`
when you also need the generated standard model types. Server and client
libraries bring their required layers through package dependencies.

## Captured metadata

`StringTable.GetSnapshot(out long version)` captures an independent mapping and
its monotonic mutation `Version` in one observation. `EncodeableFactory.Fork()`
retains independent registration maps. Internal reference, type-table and factory
images allow publication owners to prepare isolated changes and reject stale
captures; they do not introduce a public node-manager batch or retention API.
Empty nodes do not allocate a reference image until mutation or owner capture.

## WoT / NodeSet conversion

The package also exposes the dependency-light `Opc.Ua.Wot`
conversion surface used by the WoT source generator and WoT Connectivity
runtime. `WotNodeSetConverter` converts `UANodeSet` models to and from
Thing Models / Thing Descriptions.

The default output is semantic-first: the converter omits `uav:nodes` when
the readable vocabulary reconstructs equivalently and adds that complete
structured projection only for source facts not yet expressible. No
`uav:nodeSet` envelope is emitted when the structured path is complete.
Configure
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
`uav:refId` ExpandedNodeId when needed. These names improve model
semantics without replacing ExpandedNodeIds for instance identity.

## Target frameworks

`net48`, `netstandard2.0`, `net8.0`,
`net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Types migration guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/migrate/2.0.x/types.md)
for built-in value representation and migration guidance.
