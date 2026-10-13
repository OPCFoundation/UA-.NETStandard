#!/usr/bin/env python3
# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
#
# OPC Foundation MIT License 1.00
#
# Permission is hereby granted, free of charge, to any person obtaining a copy
# of this software and associated documentation files (the "Software"), to deal
# in the Software without restriction, including without limitation the rights
# to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the Software is
# furnished to do so, subject to the following conditions:
#
# The above copyright notice and this permission notice shall be included in
# all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
# IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
# FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
# AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
# LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
# FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
# IN THE SOFTWARE.
#
# The complete license agreement can be found here:
# http://opcfoundation.org/License/MIT/1.00/
"""Validate pinned model assets and the independently authored interface.

The default check needs repository files only. --reference permits a read-only
private comparison with a separately obtained NodeSet. The reference is never
copied, serialized, used to generate model content, or required by the build.
Only structural/encoding fingerprints are retained.
"""

import argparse
import base64
import copy
import csv
import hashlib
import io
import json
from pathlib import Path
import runpy
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
MODEL = ROOT / "src" / "Opc.Ua.PlasticsRubber" / "Model"
NS = "{http://opcfoundation.org/UA/2011/03/UANodeSet.xsd}"
IGNORED = {"DisplayName", "Description", "Extensions"}
SCHEMA_IGNORED = {"Documentation", "annotation", "documentation"}


def digest(value):
    """Hash canonical structural facts, not XML spelling or formatting."""
    text = json.dumps(value, ensure_ascii=True, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def schema_fact(data):
    """Compare field order, wire types, inheritance and XML occurrence rules."""
    namespaces = dict(value for _, value in ET.iterparse(io.BytesIO(data), events=["start-ns"]))
    root = ET.fromstring(data)

    def fact(element):
        if element.tag.split("}")[-1] in SCHEMA_IGNORED:
            return None
        attributes = {}
        for key, value in element.attrib.items():
            if key in {"TypeName", "BaseType", "SourceType", "type", "base", "ref"} and ":" in value:
                prefix, name = value.split(":", 1)
                value = f"{{{namespaces[prefix]}}}{name}"
            attributes[key] = value
        children = [result for child in element if (result := fact(child)) is not None]
        if element is root:
            children.sort(key=lambda child: json.dumps(child, sort_keys=True))
        return [element.tag, attributes, (element.text or "").strip(), children]

    return fact(root)


def interface_fact(root):
    """Capture all node/definition/value/reference facts and model headers."""
    aliases = {
        alias.get("Alias"): alias.text
        for alias in root.findall(NS + "Aliases/" + NS + "Alias")
    }

    def fact(element, dictionary=False):
        local = element.tag.split("}")[-1]
        if local in IGNORED:
            return None
        attributes = {
            key: aliases.get(value, value) if key in {"DataType", "ReferenceType"} else value
            for key, value in element.attrib.items()
        }
        text = (element.text or "").strip()
        if local == "ByteString" and dictionary:
            return schema_fact(base64.b64decode(text))
        children = [
            result for child in element
            if (result := fact(child, dictionary)) is not None
        ]
        if local == "References":
            children.sort(key=lambda child: json.dumps(child, sort_keys=True))
        return [element.tag, attributes, text, children]

    nodes = {
        node.get("NodeId"): fact(node, node.get("NodeId") in {"ns=1;i=6002", "ns=1;i=6004"})
        for node in root if node.get("NodeId")
    }
    return {
        "namespaces": fact(root.find(NS + "NamespaceUris")),
        "models": fact(root.find(NS + "Models")),
        "nodes": nodes,
    }


def fingerprints(facts):
    """Separate the address-space and encoding-dictionary drift guards."""
    return {
        "interfaceSha256": digest(facts),
        "binaryDictionarySha256": digest(facts["nodes"]["ns=1;i=6002"]),
        "xmlDictionarySha256": digest(facts["nodes"]["ns=1;i=6004"]),
        "nodeCount": len(facts["nodes"]),
    }


def validate_assets():
    """Check exact input hashes, identifier coverage and declared dependency edges."""
    author = runpy.run_path(str(MODEL / "author-interface-model.py"))
    document, identifiers = author["build_model"]()
    assert document == (MODEL / "Opc.Ua.PlasticsRubber.NodeSet2.xml").read_bytes()
    assert identifiers == (MODEL / "Opc.Ua.PlasticsRubber.NodeIds.csv").read_bytes()
    print("PASS common-model authoring is reproducible from the MIT basis only")
    manifest = json.loads((MODEL / "companion-models.json").read_text(encoding="utf-8"))
    assert len(manifest["models"]) == 14
    assert len({model["modelUri"] for model in manifest["models"]}) == 14
    derive = runpy.run_path(str(ROOT / "tools" / "nodesets" / "derive-identifier-table.py"))
    for model in manifest["models"]:
        directory = ROOT / "src" / model["project"] / "Model"
        xml = directory / (model["project"] + ".NodeSet2.xml")
        guard = directory / (model["project"] + ".NodeIds.csv")
        assert hashlib.sha256(xml.read_bytes()).hexdigest() == model["nodeSet"]["sha256"], model["project"]
        assert hashlib.sha256(guard.read_bytes()).hexdigest() == model["identifierGuard"]["sha256"], model["project"]
        imported = derive["NodeSet"](xml)
        symbols = imported.symbolic_ids()
        expected = {
            (symbol, node_id.split("i=")[-1], imported.tag(imported.by_id[node_id])[2:])
            for node_id, symbol in symbols.items() if node_id.startswith("ns=1;i=")
        }
        with guard.open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.reader(stream))
        assert rows[0] == ["SymbolicName", "NodeId", "NodeClass"]
        assert len(rows[1:]) == len(expected)
        assert {tuple(row) for row in rows[1:]} == expected, model["project"]
        declared = imported.root.find(NS + "Models/" + NS + "Model")
        assert declared.get("ModelUri") == model["modelUri"]
        assert declared.get("Version") == model["version"]
        assert declared.get("PublicationDate") == model["publicationDate"]
        assert [dict(child.attrib) for child in declared] == model["requiredModels"]
        assert sum(model["nodeCounts"].values()) == len(imported.nodes)
        project = ET.parse(directory.parent / (model["project"] + ".csproj")).getroot()
        notices = [
            item for item in project.findall("ItemGroup/None")
            if item.get("PackagePath") == "licenses\\CompanionModels.NOTICE.txt"
        ]
        assert len(notices) == 1 and notices[0].get("Pack") == "true", model["project"]
        notice_path = notices[0].get("Include", notices[0].get("Update"))
        assert directory.parent.joinpath(*notice_path.split("\\")).is_file(), model["project"]
        references = {
            ref.get("Include").split("\\")[-1].removesuffix(".csproj")
            for ref in project.findall("ItemGroup/ProjectReference")
        }
        assert set(model["projectDependencies"]).issubset(references)
        upstream = model["upstreamIdentifiers"]
        if upstream is not None:
            data = (directory / (model["project"] + ".Upstream.NodeIds.csv")).read_bytes()
            assert hashlib.sha256(data).hexdigest() == upstream["sha256"]
            published = set()
            actual = {int(row[1]): row for row in expected}
            exceptions = []
            for symbolic, numeric, node_class in derive["read_upstream"](
                    directory / (model["project"] + ".Upstream.NodeIds.csv")):
                published.add(numeric)
                if numeric not in actual:
                    exceptions.append({
                        "kind": "published-table-row-absent-from-nodeset",
                        "symbolicName": symbolic, "identifier": numeric, "nodeClass": node_class,
                    })
                else:
                    assert actual[numeric][2] == node_class
            for numeric in sorted(set(actual) - published):
                row = actual[numeric]
                exceptions.append({
                    "kind": "nodeset-node-absent-from-published-table", "identifier": numeric,
                    "symbolicName": row[0], "nodeClass": row[2],
                })
            assert exceptions == model["upstreamIdentifierDiscrepancies"]
        else:
            basis = model["nodeSet"]["basis"]
            assert hashlib.sha256((MODEL / basis["localPath"]).read_bytes()).hexdigest() == basis["sha256"]
        print(f"PASS {model['project']}: {len(imported.nodes)} nodes and complete identifier guard")


def validate_detector(root):
    """Prove that the comparison ignores prose but detects functional changes."""
    expected = digest(interface_fact(root))
    changed = copy.deepcopy(root)
    node = next(node for node in changed if node.get("NodeId") == "ns=1;i=1070")
    node.find(NS + "DisplayName").text = "Independent display label"
    ET.SubElement(node, NS + "Description").text = "Independent description"
    assert digest(interface_fact(changed)) == expected
    node.find(NS + "References/" + NS + "Reference").text = "i=58"
    assert digest(interface_fact(changed)) != expected
    changed = copy.deepcopy(root)
    node = next(node for node in changed if node.get("NodeId") == "ns=1;i=3003")
    node.find(NS + "Definition/" + NS + "Field").set("ValueRank", "-1")
    assert digest(interface_fact(changed)) != expected
    changed = copy.deepcopy(root)
    node = next(node for node in changed if node.get("NodeId") == "ns=1;i=6002")
    blob = node.find(NS + "Value")[0]
    schema = ET.fromstring(base64.b64decode(blob.text))
    structure = next(item for item in schema if item.get("Name") == "ClassifiedActiveErrorDataType")
    fields = list(structure)
    structure.remove(fields[-1])
    structure.insert(0, fields[-1])
    # QName-valued attributes still need the declarations in the original schema.
    schema.set("xmlns:tns", "http://opcfoundation.org/UA/PlasticsRubber/GeneralTypes/")
    schema.set("xmlns:ua", "http://opcfoundation.org/UA/")
    schema.set("xmlns:opc", "http://opcfoundation.org/BinarySchema/")
    blob.text = base64.b64encode(ET.tostring(schema)).decode("ascii")
    assert digest(interface_fact(changed)) != expected
    print("PASS comparison detector: prose ignored; references, ranks and wire order guarded")


def main():
    """Validate public assets; optionally perform a non-redistributive comparison."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", type=Path)
    args = parser.parse_args()
    root = ET.parse(MODEL / "Opc.Ua.PlasticsRubber.NodeSet2.xml").getroot()
    facts = interface_fact(root)
    actual = fingerprints(facts)
    if args.reference:
        reference_facts = interface_fact(ET.parse(args.reference).getroot())
        if facts != reference_facts:
            differences = sorted(
                key for key in facts["nodes"].keys() | reference_facts["nodes"].keys()
                if facts["nodes"].get(key) != reference_facts["nodes"].get(key))
            print("Different functional node IDs:", differences)
            print("Model headers equal:", facts["models"] == reference_facts["models"])
            for key in ("ns=1;i=6002", "ns=1;i=6004"):
                if key not in differences:
                    continue
                left = facts["nodes"][key][-1][-1][-1][0]
                right = reference_facts["nodes"][key][-1][-1][-1][0]
                left_types = {
                    (child[0], child[1].get("Name", child[1].get("name"))): child for child in left[-1]}
                right_types = {
                    (child[0], child[1].get("Name", child[1].get("name"))): child for child in right[-1]}
                print("Dictionary declaration differences:", key, [
                    name for name in left_types.keys() | right_types.keys()
                    if left_types.get(name) != right_types.get(name)])
            raise SystemExit("The independently authored implementation is not structurally equivalent.")
        print("PASS read-only comparison: every node, model header and dictionary encoding agrees")
        print("Reference SHA-256:", hashlib.sha256(args.reference.read_bytes()).hexdigest())
        print(json.dumps(actual, indent=2))
    else:
        expected = json.loads((Path(__file__).parent / "interface-signature.json").read_text(encoding="utf-8"))
        assert actual == expected["fingerprints"], "Authored interface fingerprint drifted"
        print("PASS persisted common-model interface and dictionary fingerprints")
    validate_detector(root)
    validate_assets()


if __name__ == "__main__":
    main()
