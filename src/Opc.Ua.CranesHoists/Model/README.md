# Pinned Cranes and Hoists model

The XML and documentation CSV in this directory are the unmodified official
OPC-40020-1 1.00 publication from UA-Nodeset commit
`96a08945425d0295edb3607d2a38f6841ef44046`. `provenance.json` records the
immutable source URLs, original and line-ending-normalized SHA-256 hashes,
model version and required model versions.

`Opc.Ua.CranesHoists.NodeIds.csv` is a **locally derived identifier drift guard**,
not an upstream publication. This upstream folder publishes no identifier CSV.
The table covers all 89 UA declarations (six ObjectTypes, four DataTypes, one
ReferenceType, 31 Objects and 47 Variables), using the existing
`tools\nodesets\derive-identifier-table.py` `NodeSet.symbolic_ids()` parser.
The generator validates the table against the XML; the XML remains authoritative.

To derive the table again from the repository root without inventing a published
CSV:

```powershell
@'
import csv
import runpy
from pathlib import Path

tool = runpy.run_path(r"tools\nodesets\derive-identifier-table.py")
model = Path(r"src\Opc.Ua.CranesHoists\Model")
nodeset = tool["NodeSet"](model / "Opc.Ua.CranesHoists.NodeSet2.xml")
symbols = nodeset.symbolic_ids()
rows = [
    (symbols[node.get("NodeId")], int(node.get("NodeId").split("i=")[1]),
     nodeset.tag(node)[2:])
    for node in nodeset.nodes
]
with (model / "Opc.Ua.CranesHoists.NodeIds.csv").open(
    "w", encoding="utf-8", newline=""
) as stream:
    writer = csv.writer(stream, lineterminator="\n")
    writer.writerow(("SymbolicName", "NodeId", "NodeClass"))
    writer.writerows(sorted(rows, key=lambda row: row[1]))
'@ | python -
```

## Preserved source discrepancies

- The wire namespace is `http://opcfoundation.org/UA/CranesHoists/`, not the
  `Robotics/CranesHoists` URI appearing in the specification's profile prose.
- The XML model publication date is 2023-09-01, while the published namespace
  metadata variable contains 2023-05-01. Both values are retained in the source.
- There are no newly declared Method nodes and no published profile metadata
  file in this pinned directory. The package does not invent actuation methods
  or profile URI constants.

Robotics, Machinery, IA and DI are resolved from their existing model assemblies.
Their NodeSets are not copied or regenerated in this project.
