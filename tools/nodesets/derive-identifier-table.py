#!/usr/bin/env python3
"""Derives a source-generator-compatible identifier table from a NodeSet2 file.

The OPC Foundation publishes a *.NodeIds.csv next to every NodeSet2 document.
Its numeric ids and node classes are authoritative, but its symbolic names follow
the UA-ModelCompiler's conventions, which differ from the ones this repository's
model source generator derives. Feeding the published table to the generator as
a ModelSourceGeneratorIdentifierFile therefore fails with MODELGEN025.

This script recomputes each symbolic name with the generator's own rules
(NodeSetToModelDesign.ImportSymbolicName / BuildSymbolicId) and emits a repaired
table. It also reports every row whose numeric id or node class disagrees with
the NodeSet, which is a real upstream defect rather than a naming convention.

Usage:
    derive-identifier-table.py <nodeset.xml> <upstream.NodeIds.csv> <out.NodeIds.csv>
"""

import sys
import csv
import xml.etree.ElementTree as ET

NS = "{http://opcfoundation.org/UA/2011/03/UANodeSet.xsd}"

# NodeSetToModelDesign.s_keywords - the C# keywords a symbolic name may not be.
KEYWORDS = {
    "private", "public", "protected", "internal", "lock", "char", "byte", "int",
    "uint", "long", "ulong", "short", "ushort", "float", "double", "decimal",
    "bool", "string", "object", "void", "class", "struct", "enum", "interface",
    "namespace", "using", "static", "readonly", "const", "new", "override",
    "virtual", "abstract", "sealed", "partial", "base", "this", "null", "true",
    "false", "if", "else", "switch", "case", "default", "for", "foreach",
    "while", "do", "break", "continue", "return", "goto", "try", "catch",
    "finally", "throw", "operator", "params", "ref", "out", "in", "is", "as",
    "typeof", "sizeof", "checked", "unchecked", "unsafe", "fixed", "delegate",
    "event", "explicit", "implicit", "extern", "stackalloc", "volatile",
}

DATA_TYPE_ENCODING_TYPE = "i=76"
HAS_TYPE_DEFINITION = "i=40"
HAS_ENCODING = "i=38"
HAS_PROPERTY = "i=46"
HAS_COMPONENT = "i=47"

INSTANCE_TAGS = {"UAObject", "UAVariable", "UAMethod", "UAView"}


def to_symbolic_name(name):
    """Port of NodeSetToModelDesign.ToSymbolicName."""
    if name in KEYWORDS:
        name = "_" + name
    out = []
    for ch in name:
        if not ch.isalnum():
            out.append("x" if not out else "_")
            continue
        if not out and ch.isdigit():
            out.append("n")
        out.append(ch)
    return "".join(out)


def browse_name_parts(browse_name):
    """Splits 'ns:Name' and applies ImportQualifiedName's <> rewrite to Name."""
    raw = browse_name or ""
    if ":" in raw:
        prefix, _, rest = raw.partition(":")
        if prefix.isdigit():
            raw_name = rest
        else:
            raw_name = raw
    else:
        raw_name = raw
    return raw_name, raw_name.replace("<", "_").replace(">", "_")


class NodeSet:
    def __init__(self, path):
        self.root = ET.parse(path).getroot()
        # A NodeSet writes reference types through the <Aliases> table, so every
        # ReferenceType attribute has to be resolved before it can be compared
        # with a NodeId.
        self.aliases = {}
        alias_table = self.root.find(NS + "Aliases")
        if alias_table is not None:
            for alias in alias_table:
                self.aliases[alias.get("Alias")] = (alias.text or "").strip()
        self.by_id = {}
        self.nodes = []
        for node in self.root:
            tag = node.tag.replace(NS, "")
            if not tag.startswith("UA"):
                continue
            node_id = node.get("NodeId")
            if node_id is None:
                continue
            self.by_id[node_id] = node
            self.nodes.append(node)
        self._normalize_encoding_symbolic_names()
        self._infer_missing_parents()
        self._cache = {}

    def tag(self, node):
        return node.tag.replace(NS, "")

    def refs(self, node):
        container = node.find(NS + "References")
        return list(container) if container is not None else []

    def resolve(self, reference_type):
        return self.aliases.get(reference_type, reference_type)

    def target(self, node, reference_type, is_forward=True):
        for ref in self.refs(node):
            if self.resolve(ref.get("ReferenceType")) != reference_type:
                continue
            forward = ref.get("IsForward", "true") != "false"
            if forward == is_forward:
                return (ref.text or "").strip()
        return None

    def _normalize_encoding_symbolic_names(self):
        """Port of the DefaultBinary / DefaultXml fixups in GetImportedSymbols."""
        for node in self.nodes:
            if self.target(node, HAS_TYPE_DEFINITION) != DATA_TYPE_ENCODING_TYPE:
                continue
            symbolic = node.get("SymbolicName")
            if not symbolic:
                _, name = browse_name_parts(node.get("BrowseName"))
                if name == "Default Binary":
                    node.set("SymbolicName", "DefaultBinary")
                elif name == "Default XML":
                    node.set("SymbolicName", "DefaultXml")
            elif symbolic == "DefaultXML":
                node.set("SymbolicName", "DefaultXml")

    def _infer_missing_parents(self):
        """Port of the inverse HasProperty/HasComponent parent inference."""
        for node in self.nodes:
            if self.tag(node) not in INSTANCE_TAGS:
                continue
            if node.get("ParentNodeId"):
                continue
            for ref in self.refs(node):
                if ref.get("IsForward", "true") != "false":
                    continue
                if self.resolve(ref.get("ReferenceType")) in (HAS_PROPERTY, HAS_COMPONENT):
                    node.set("ParentNodeId", (ref.text or "").strip())
                    break

    def namespace_index(self, node_id):
        if node_id and node_id.startswith("ns="):
            return int(node_id[3:node_id.index(";")])
        return 0

    def import_symbolic_name(self, node):
        """Port of NodeSetToModelDesign.ImportSymbolicName."""
        raw_name, rewritten = browse_name_parts(node.get("BrowseName"))
        symbolic = node.get("SymbolicName")
        if symbolic:
            if ":<" in (node.get("BrowseName") or "") and not symbolic.endswith("_Placeholder"):
                return symbolic + "_Placeholder"
            return symbolic
        if len(raw_name) > 2 and raw_name[0] == "<" and raw_name[-1] == ">":
            return to_symbolic_name(raw_name[1:-1]) + "_Placeholder"
        return to_symbolic_name(rewritten)

    def import_and_fix_symbolic_name(self, node):
        """Port of NodeSetToModelDesign.ImportAndFixSymbolicName."""
        name = self.import_symbolic_name(node)
        if self.target(node, HAS_TYPE_DEFINITION) != DATA_TYPE_ENCODING_TYPE:
            return name
        data_type_id = self.target(node, HAS_ENCODING, is_forward=False)
        if not data_type_id:
            encoding_id = node.get("NodeId")
            for candidate in self.nodes:
                if self.tag(candidate) != "UADataType":
                    continue
                for ref in self.refs(candidate):
                    if (self.resolve(ref.get("ReferenceType")) == HAS_ENCODING
                            and ref.get("IsForward", "true") != "false"
                            and (ref.text or "").strip() == encoding_id):
                        return f"{self.import_symbolic_name(candidate)}_Encoding_{name}"
            return name
        data_type = self.by_id.get(data_type_id)
        if data_type is not None:
            return f"{self.import_symbolic_name(data_type)}_Encoding_{name}"
        return name

    def build_symbolic_id(self, node):
        """Port of NodeSetToModelDesign.BuildSymbolicId."""
        node_id = node.get("NodeId")
        cached = self._cache.get(node_id)
        if cached is not None:
            return cached
        if self.tag(node) not in INSTANCE_TAGS:
            result = self.import_and_fix_symbolic_name(node)
        else:
            parent_id = node.get("ParentNodeId")
            if not parent_id:
                result = self.import_and_fix_symbolic_name(node)
            elif self.namespace_index(parent_id) != self.namespace_index(node_id):
                result = self.import_and_fix_symbolic_name(node)
            else:
                parent = self.by_id.get(parent_id)
                if parent is None:
                    raise SystemExit(
                        f"Parent node ({parent_id}) for {node_id} not found.")
                result = f"{self.build_symbolic_id(parent)}_{self.import_symbolic_name(node)}"
        self._cache[node_id] = result
        return result

    def symbolic_ids(self):
        """Port of GetImportedSymbols, including its collision disambiguation."""
        assigned = {}
        taken = set()
        for node in self.nodes:
            symbolic_id = self.build_symbolic_id(node)
            while symbolic_id in taken:
                symbolic_id = f"{symbolic_id}_{node.get('NodeId').split('i=')[-1]}"
            taken.add(symbolic_id)
            assigned[node.get("NodeId")] = symbolic_id
        return assigned


def read_upstream(path):
    """Yields (symbolic name, numeric id, node class) from a published table.

    The OPC Foundation does not publish these tables in one shape. Most are
    comma separated as `SymbolicName,NodeId,NodeClass`; PADIM's is tab
    separated with an `ID<TAB>Browsename<TAB>Node Class` header, i.e. the
    identifier comes first. Both are accepted here rather than being
    hand-converted, so re-vendoring a model stays a single command.
    """
    with open(path, encoding="utf-8-sig", newline="") as handle:
        text = handle.read()

    delimiter = "\t" if "\t" in text.split("\n")[0] else ","
    identifier_first = False

    for record in csv.reader(text.splitlines(), delimiter=delimiter):
        if len(record) != 3:
            continue
        first, second, third = (field.strip() for field in record)
        lowered = first.lower()
        if lowered in ("symbolicname", "id", "browsename"):
            identifier_first = lowered == "id"
            continue
        if identifier_first:
            numeric, name, node_class = first, second, third
        else:
            name, numeric, node_class = first, second, third
        if not numeric.isdigit():
            continue
        yield name, int(numeric), node_class


def main():
    if len(sys.argv) != 4:
        print(__doc__)
        return 2

    nodeset_path, upstream_path, out_path = sys.argv[1:4]
    nodeset = NodeSet(nodeset_path)
    target_uri = nodeset.root.find(NS + "Models").find(NS + "Model").get("ModelUri")
    uris = [u.text for u in nodeset.root.find(NS + "NamespaceUris")]
    target_index = uris.index(target_uri) + 1

    symbolic_ids = nodeset.symbolic_ids()
    by_numeric = {}
    for node_id, symbolic in symbolic_ids.items():
        prefix = f"ns={target_index};i="
        if node_id.startswith(prefix):
            numeric = int(node_id[len(prefix):])
            by_numeric[numeric] = (symbolic, nodeset.tag(node_id and nodeset.by_id[node_id])[2:])

    rows, renamed, defects = [], 0, []
    for name, numeric, node_class in read_upstream(upstream_path):
        if numeric not in by_numeric:
            defects.append(f"  id {numeric} ({name}) is not in the NodeSet")
            continue
        derived, derived_class = by_numeric[numeric]
        if derived_class != node_class:
            defects.append(
                f"  id {numeric} ({name}): NodeSet says {derived_class}, table says {node_class}")
        if derived != name:
            renamed += 1
        rows.append((derived, numeric, derived_class))

    covered = {numeric for _, numeric, _ in rows}
    missing = sorted(set(by_numeric) - covered)

    with open(out_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("SymbolicName,NodeId,NodeClass\n")
        for name, numeric, node_class in rows:
            handle.write(f"{name},{numeric},{node_class}\n")

    print(f"wrote {out_path}: {len(rows)} row(s), {renamed} renamed, "
          f"{len(missing)} NodeSet node(s) not in the upstream table")
    if missing:
        print("  not in table:", ", ".join(str(m) for m in missing[:20]))
    for defect in defects:
        print("DEFECT" + defect)
    return 1 if defects else 0


if __name__ == "__main__":
    sys.exit(main())
