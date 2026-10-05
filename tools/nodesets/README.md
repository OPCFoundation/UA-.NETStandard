# NodeSet vendoring helpers

## `derive-identifier-table.py`

Derives a source-generator-compatible identifier table (`*.NodeIds.csv`) from a
vendored NodeSet2 document and the OPC Foundation's published table.

The published table's numeric ids and node classes are authoritative, but its
symbolic names follow the UA-ModelCompiler's conventions, which differ from the
ones this repository's model source generator derives in
`NodeSetToModelDesign.ImportSymbolicName` / `BuildSymbolicId`. Feeding the
published table to the generator as a `ModelSourceGeneratorIdentifierFile`
therefore fails with `MODELGEN025`. Four differences occur in practice:

| Published | Generated | Why |
| --- | --- | --- |
| `Server_Namespaces_http___…_` | `http___…_` | the publication qualifies the namespace-metadata object with its server path; the NodeSet's own `SymbolicName` does not |
| `X_ControlChannel` | `X_ControlChannel_Placeholder` | a `<Placeholder>` BrowseName maps to `Name_Placeholder` |
| `DefaultBinary` | `RGBWDataType_Encoding_DefaultBinary` | encoding nodes are qualified with the data type they encode |
| `Aliases_Assets_AssetsByAssetId` | `AssetsByAssetId` | the publication prefixes a top-level instance with the path of the objects organizing it, down from a base-namespace object (OPC 10000-110 AMB: `0:Aliases`, `0:Locations`); the generator starts the symbolic id at the instance, because organized nodes are no children |

The table guards the symbols the generator derives, not the identifier constants
it emits. Two kinds of valid rows have no constant in `Variables` /
`VariableIds`: the entries of the type dictionaries
(`TypeDictionary_BinarySchema_<DataType>`), and the children of a top-level
instance that its type does not declare mandatory, such as a child the NodeSet
adds to an instance on its own (AMB's `AssetsByProductInstanceUri_NodeVersion`).

Run it once per vendored model and keep both files:

```sh
python3 tools/nodesets/derive-identifier-table.py \
    src/Opc.Ua.IA/Model/Opc.Ua.IA.NodeSet2.xml \
    src/Opc.Ua.IA/Model/Opc.Ua.IA.Upstream.NodeIds.csv \
    src/Opc.Ua.IA/Model/Opc.Ua.IA.NodeIds.csv
```

`*.Upstream.NodeIds.csv` is the unmodified publication and is deliberately kept
out of `<AdditionalFiles>`, so the two can be diffed to review exactly what the
derivation changed — the same convention `src/Opc.Ua.ISA95` uses.

The script exits non-zero when a published row disagrees with the NodeSet on the
numeric id or node class. That is an upstream defect rather than a naming
convention and needs a documented repair in the vendored NodeSet, not a silent
rewrite of the table.
