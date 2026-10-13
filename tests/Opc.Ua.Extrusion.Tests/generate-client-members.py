# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
# http://opcfoundation.org/License/MIT/1.00/

"""Generate typed member extensions from the licensed, unmodified extrusion models.

No wire types, service implementations, or normative prose are copied or synthesized.
Existing generated proxies and datatype codecs remain the model authority.
Run with --check to verify the committed accessors and member evidence.
"""

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
UA = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
Q = {"u": UA}
PARTS = [
    ("Extrusion", 1),
    ("Extrusion.Line", 2),
    ("Extrusion.Extruder", 3),
    ("Extrusion.HaulOff", 4),
    ("Extrusion.MeltPump", 5),
    ("Extrusion.Filter", 6),
    ("Extrusion.Die", 7),
    ("Extrusion.Pelletizer", 8),
    ("Extrusion.Cutter", 9),
    ("Extrusion.Calibrator", 10),
    ("Extrusion.Corrugator", 11),
    ("Extrusion.Calender", 12),
]
BUILTINS = {
    1: "bool", 2: "sbyte", 3: "byte", 4: "short", 5: "ushort",
    6: "int", 7: "uint", 8: "long", 9: "ulong", 10: "float",
    11: "double", 12: "string", 13: "global::Opc.Ua.DateTimeUtc",
    14: "global::Opc.Ua.Uuid", 15: "global::Opc.Ua.ByteString",
    16: "global::Opc.Ua.XmlElement", 17: "global::Opc.Ua.NodeId",
    18: "global::Opc.Ua.ExpandedNodeId", 19: "global::Opc.Ua.StatusCode",
    20: "global::Opc.Ua.QualifiedName", 21: "global::Opc.Ua.LocalizedText",
    22: "global::Opc.Ua.ExtensionObject", 23: "global::Opc.Ua.DataValue",
    24: "global::Opc.Ua.Variant", 26: "global::Opc.Ua.Variant",
    27: "global::Opc.Ua.Variant", 28: "global::Opc.Ua.Variant",
    29: "int", 50: "float", 290: "double", 294: "global::Opc.Ua.DateTimeUtc",
    295: "string", 296: "string", 887: "global::Opc.Ua.EUInformation",
    884: "global::Opc.Ua.Range",
}
ALIASES = {
    "Boolean": 1, "SByte": 2, "Byte": 3, "Int16": 4, "UInt16": 5,
    "Int32": 6, "UInt32": 7, "Int64": 8, "UInt64": 9, "Float": 10,
    "Double": 11, "String": 12, "DateTime": 13, "Guid": 14,
    "ByteString": 15, "XmlElement": 16, "NodeId": 17, "ExpandedNodeId": 18,
    "StatusCode": 19, "QualifiedName": 20, "LocalizedText": 21,
    "Structure": 22, "DataValue": 23, "BaseDataType": 24, "Number": 26,
    "Integer": 27, "UInteger": 28, "Enumeration": 29, "Duration": 290,
    "UtcTime": 294, "LocaleId": 295, "EUInformation": 887, "Range": 884,
}


def local(value):
    return value.split(":", 1)[-1]


def identifier(value):
    return re.sub(r"[^a-zA-Z0-9_]", "", value.replace("<Nr>", ""))


def load_model(name):
    project = "Opc.Ua." + name
    path = ROOT / "src" / project / "Model" / (project + ".NodeSet2.xml")
    root = ET.parse(path).getroot()
    uris = ["http://opcfoundation.org/UA/"]
    uris.extend(x.text for x in root.find("u:NamespaceUris", Q))
    return {
        "project": project, "prefix": "global::" + project, "uris": uris,
        "nodes": {x.get("NodeId"): x for x in root if x.get("NodeId")},
        "aliases": {x.get("Alias"): x.text for x in root.find("u:Aliases", Q)},
        "uri": root.find("u:Models/u:Model", Q).get("ModelUri"),
    }


def expanded(model, text):
    text = model["aliases"].get(text, text)
    if text in ALIASES:
        return ("http://opcfoundation.org/UA/", ALIASES[text])
    match = re.fullmatch(r"(?:ns=(\d+);)?i=(\d+)", text)
    if not match:
        raise ValueError("Unresolved identifier: " + text)
    return (model["uris"][int(match.group(1) or 0)], int(match.group(2)))


def browse(model, node):
    name = node.get("BrowseName")
    if ":" in name:
        index, name = name.split(":", 1)
        return model["uris"][int(index)], name
    return model["uris"][0], name


def refs(node, kind):
    return [
        reference for reference in node.find("u:References", Q)
        if reference.get("ReferenceType") == kind
        and reference.get("IsForward") != "false"
    ]


def member_type(model, node, index):
    key = expanded(model, node.get("DataType", "BaseDataType"))
    if key[0] == model["uris"][0]:
        scalar = BUILTINS[key[1]]
        structure = key[1] in (887, 884)
    else:
        owner, datatype = index[key]
        scalar = owner["prefix"] + "." + identifier(local(datatype.get("BrowseName")))
        fields = datatype.find("u:Definition", Q)
        structure = fields is not None and any("Value" not in field.attrib for field in fields)
    rank = int(node.get("ValueRank", "-1"))
    if rank in (-3, -2, 0) or rank > 1:
        return ("global::Opc.Ua.Variant", False)
    if rank == 1:
        scalar = "global::Opc.Ua.ArrayOf<" + scalar + ">"
    return scalar, structure


def generate():
    models = [load_model(name) for name, _ in PARTS]
    common = load_model("PlasticsRubber")
    index = {}
    for model in [common] + models:
        for key, node in model["nodes"].items():
            index[expanded(model, key)] = (model, node)
    header = (ROOT / "src" / "Opc.Ua.Extrusion.Server" / "ExtrusionContracts.cs").read_text(
        encoding="utf-8").split("using System;", 1)[0]
    lines = [
        header.rstrip(), "", "// <auto-generated />",
        "// Regenerate with tests\\Opc.Ua.Extrusion.Tests\\generate-client-members.py.",
        "#nullable enable", "", "namespace Opc.Ua.Extrusion.Client", "{",
        "    /// <summary>",
        "    /// Model-derived variable and placeholder accessors over the existing generated proxies.",
        "    /// </summary>",
        "    public static class ExtrusionClientMembers", "    {",
    ]
    manifest = []
    accessor_count = 0
    placeholder_count = 0
    for model, (_, part) in zip(models, PARTS):
        for node in model["nodes"].values():
            if node.tag != "{" + UA + "}UAObjectType":
                continue
            typename = identifier(local(node.get("BrowseName")))
            receiver = model["prefix"] + "." + typename + "Client"
            for reference in node.find("u:References", Q):
                if (reference.get("ReferenceType") not in ("HasProperty", "HasComponent", "HasAddIn")
                        or reference.get("IsForward") == "false"):
                    continue
                child = model["nodes"].get(reference.text)
                if child is None:
                    continue
                namespace, name = browse(model, child)
                rules = refs(child, "HasModellingRule")
                rule = rules[0].text if rules else ""
                kind = child.tag.rsplit("}", 1)[-1]
                if rule not in ("i=78", "i=80", "i=11508", "i=11510"):
                    continue
                manifest.append({
                    "part": part, "namespaceUri": model["uri"], "type": typename,
                    "memberNamespaceUri": namespace, "member": name, "nodeClass": kind,
                    "modellingRule": rule, "nodeId": child.get("NodeId"),
                    "implementation": "ExtrusionNodeMaterializer; typed projection or common binding",
                    "test": "ExtrusionModelCoverageTests",
                })
                if kind == "UAVariable":
                    cstype, structure = member_type(model, child, index)
                    writable = (int(child.get("AccessLevel", "1")) & 2) != 0
                    encoder = "global::Opc.Ua.Variant.FromStructure" if structure else "global::Opc.Ua.Variant.From"
                    lines.extend([
                        "        /// <summary>",
                        "        /// Accesses the declared " + name + " value and its quality/source timestamps.",
                        "        /// </summary>",
                        "        public static ExtrusionVariable<",
                        "            " + cstype + ">",
                        "            " + identifier(name) + "Variable(",
                        "            this " + receiver + " proxy)",
                        "        {",
                        "            return new ExtrusionVariable<",
                        "                " + cstype + ">(",
                        "                proxy,",
                        "                \"" + namespace + "\",",
                        "                \"" + name + "\",",
                        "                " + str(writable).lower() + ",",
                        "                static (value, context) =>",
                    ])
                    if cstype == "global::Opc.Ua.Variant":
                        lines.append("                    value,")
                    else:
                        lines.extend([
                            "                    value.TryGetValue(",
                            "                        out " + cstype + " decoded" + (", context)" if structure else ")"),
                            "                        ? decoded",
                            "                        : throw new global::Opc.Ua.ServiceResultException(",
                            "                            global::Opc.Ua.StatusCodes.BadTypeMismatch),",
                        ])
                    lines.extend([
                        "                static value => " + encoder + "(value));",
                        "        }", "",
                    ])
                    accessor_count += 1
                elif kind == "UAObject" and rule in ("i=11508", "i=11510"):
                    definitions = refs(child, "HasTypeDefinition")
                    if len(definitions) != 1:
                        raise ValueError("Placeholder lacks exactly one type: " + name)
                    key = expanded(model, definitions[0].text)
                    if key not in index:
                        if key == ("http://opcfoundation.org/UA/", 58):
                            owner_prefix, target_type = "global::Opc.Ua", "BaseObjectType"
                        else:
                            raise ValueError("Unresolved placeholder type: " + str(key))
                    else:
                        owner, target = index[key]
                        owner_prefix = owner["prefix"]
                        target_type = identifier(local(target.get("BrowseName")))
                    proxy_type = owner_prefix + "." + target_type + "Client"
                    lines.extend([
                        "        /// <summary>",
                        "        /// Enumerates real " + name.replace("<", "&lt;").replace(">", "&gt;") + " instances.",
                        "        /// </summary>",
                        "        public static global::System.Collections.Generic.IAsyncEnumerable<",
                        "            " + proxy_type + ">",
                        "            Enumerate" + identifier(name).rstrip("_") + "Async(",
                        "            this " + receiver + " proxy,",
                        "            global::System.Threading.CancellationToken cancellationToken = default)",
                        "        {",
                        "            return ExtrusionClientNodes.EnumerateAsync(",
                        "                proxy, " + owner_prefix + ".ObjectTypeIds." + target_type + ",",
                        "                static (session, id, telemetry) =>",
                        "                    new " + proxy_type + "(session, id, telemetry),",
                        "                cancellationToken);",
                        "        }", "",
                    ])
                    placeholder_count += 1
    lines.extend(["    }", "}", ""])
    source = "\n".join(lines).replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8")
    evidence = {
        "source": "Licensed OPC 40084 v2.00 model declarations; generated, not specification prose.",
        "parts": list(range(1, 13)), "typedVariableAccessors": accessor_count,
        "typedPlaceholderEnumerators": placeholder_count, "members": manifest,
    }
    return source, (json.dumps(evidence, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source, manifest = generate()
    outputs = {
        ROOT / "src" / "Opc.Ua.Extrusion.Client" / "ExtrusionClientMembers.g.cs": source,
        ROOT / "tests" / "Opc.Ua.Extrusion.Tests" / "feature-matrix.json": manifest,
    }
    for path, expected in outputs.items():
        if args.check:
            if not path.exists() or path.read_bytes() != expected:
                print("Generated extrusion interface is stale: " + str(path.relative_to(ROOT)), file=sys.stderr)
                return 1
        else:
            path.write_bytes(expected)
    print("Extrusion typed accessors and complete direct-member matrix are current.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
