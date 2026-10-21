# ========================================================================
# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
#
# OPC Foundation MIT License 1.00
#
# Permission is hereby granted, free of charge, to any person
# obtaining a copy of this software and associated documentation
# files (the "Software"), to deal in the Software without
# restriction, including without limitation the rights to use,
# copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the
# Software is furnished to do so, subject to the following
# conditions:
#
# The above copyright notice and this permission notice shall be
# included in all copies or substantial portions of the Software.
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
# EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
# OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
# NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
# HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
# WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
# FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
# OTHER DEALINGS IN THE SOFTWARE.
#
# The complete license agreement can be found here:
# http://opcfoundation.org/License/MIT/1.00/
# ========================================================================

"""Import or verify the pinned experimental RSE model without changing its namespace."""

import argparse
import csv
import hashlib
import io
import json
from pathlib import Path
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
DESIGN = ROOT / "tools" / "Opc.Ua.SourceGeneration.Core" / "Design"
MODEL = ROOT / "src" / "Opc.Ua.Core" / "AsyncServices" / "Model"
SOURCE_COMMIT = "533587684928669b0cd183fc9ab61b5b0730a576"
MODEL_URI = "http://opcfoundation.org/UA/RSE/"
REVISION = "0.4.0"
XML_NAMESPACES = {
    "n": "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd",
    "u": "http://opcfoundation.org/UA/2008/02/Types.xsd",
}
FILES = {
    "model-contract.json": (
        DESIGN / "AsyncServices.model-contract.json",
        "f84345ba67b070723a37eec66c35cb2dcb7b22b698fa78d9b0f94f55ce52c75f",
    ),
    "Opc.Ua.RSE.NodeSet2.xml": (
        MODEL / "Opc.Ua.RSE.NodeSet2.xml",
        "36083b885c033206b22f754fb53b7491b868757a1de1df97c7b459345fa49904",
    ),
    "Opc.Ua.RSE.NodeIds.csv": (
        MODEL / "Opc.Ua.RSE.NodeIds.csv",
        "6449c0f65179f91de1148ae5425ffd93f081270aa7d1b670fbc274e1f7dda14c",
    ),
    "revision-contract.json": (
        ROOT / "tools" / "AsyncServices" / "revision-contract.json",
        "2485bfcd47cbfe9b3a983cd689e74b7781edcf70ef58d91daabdad878a994045",
    ),
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_snapshot(source_directory=None):
    """Validate the entire input set before allowing any repository writes."""
    snapshot = {}
    for name, (destination, expected_hash) in FILES.items():
        path = source_directory / name if source_directory is not None else destination
        content = path.read_bytes().replace(b"\r\n", b"\n")
        require(
            hashlib.sha256(content).hexdigest() == expected_hash,
            f"{name} does not match pinned Core commit {SOURCE_COMMIT}.",
        )
        snapshot[name] = content
    validate_model(snapshot)
    return snapshot


def identifier_labels(contract, nodes):
    """Compose CSV labels independently of the NodeSet's local SymbolicName metadata."""
    labels = {}

    def add(identity, label):
        require(identity not in labels, f"Duplicate contract identity {identity}.")
        labels[identity] = label

    for enum in contract["enums"]:
        add(enum["id"], enum["name"])
        add(enum["enumStringsId"], enum["name"] + "_EnumStrings")
    for structure in contract["structures"]:
        add(structure["id"], structure["name"])
        for encoding, identity in structure["encodings"].items():
            add(identity, structure["name"] + "_Encoding_" + encoding)
    for object_type in contract["objectTypes"]:
        add(object_type["id"], object_type["name"])
        for member in object_type["members"]:
            add(member["id"], object_type["name"] + "_" + member["name"])
    instances = {instance["id"]: instance for instance in contract["instances"]}

    def instance_label(instance, ancestors=()):
        require(instance["id"] not in ancestors, "Cycle in the contract instance hierarchy.")
        parent = instance["parent"]
        if not parent.startswith("ns=1;i="):
            return instance["name"]
        parent_id = int(parent.removeprefix("ns=1;i="))
        require(parent_id in instances, "Missing instance parent in the contract.")
        return instance_label(instances[parent_id], ancestors + (instance["id"],)) + "_" + instance["name"]

    for instance in instances.values():
        add(instance["id"], instance_label(instance))
    for method in contract["methods"]:
        label = method["owner"] + "_" + method["name"]
        add(method["id"], label)
        add(method["inputArgumentsId"], label + "_InputArguments")
        add(method["outputArgumentsId"], label + "_OutputArguments")
    metadata = contract["namespaceMetadata"]
    add(metadata["id"], "RseNamespace")
    for identity in metadata["propertyIds"]:
        browse_name = nodes[identity].get("BrowseName", "")
        require(browse_name.startswith("0:"), "NamespaceMetadata properties retain their Core BrowseNames.")
        add(identity, "RseNamespace_" + browse_name.removeprefix("0:"))
    return labels


def validate_model(snapshot):
    """Cross-check namespace-local identities and the ordinary Call argument binding."""
    contract = json.loads(snapshot["model-contract.json"])
    revision = json.loads(snapshot["revision-contract.json"])
    root = ET.fromstring(snapshot["Opc.Ua.RSE.NodeSet2.xml"])
    require(contract["revision"] == REVISION, "Unexpected contract revision.")
    require(contract["namespaceUri"] == MODEL_URI, "RSE must not use namespace zero.")
    require(contract["codeGenerationNamespace"] == "Opc.Ua", "Unexpected public C# namespace.")
    require(contract["namespaceIndex"] == 1, "Unexpected local source namespace index.")
    require(
        [element.text for element in root.findall("n:NamespaceUris/n:Uri", XML_NAMESPACES)] == [MODEL_URI],
        "Expected one RSE namespace; Core remains implicit namespace zero.",
    )
    models = root.findall("n:Models/n:Model", XML_NAMESPACES)
    require(len(models) == 1 and models[0].get("ModelUri") == MODEL_URI, "Expected one primary RSE model.")
    require(models[0].get("Version") == REVISION, "Model revision differs from the contract.")
    require(revision["nodeCount"] == 96 and revision["typeCount"] == 16, "Unexpected model inventory.")
    require(
        revision["assertionIds"] == [f"RSE3-{index:03d}" for index in range(1, 71)],
        "The RSE assertion identifiers must preserve all seventy numeric suffixes.",
    )
    require(
        revision["conformanceUnits"] == contract["conformanceUnits"]
        and all(unit.startswith("RSE-") for unit in revision["conformanceUnits"]),
        "Inconsistent RSE conformance identifiers.",
    )

    nodes = {}
    for node in root:
        identity = node.get("NodeId")
        if identity is None:
            continue
        require(identity.startswith("ns=1;i="), f"Non-RSE definition: {identity}.")
        local_id = int(identity.removeprefix("ns=1;i="))
        require(local_id not in nodes, f"Duplicate NodeId: {identity}.")
        nodes[local_id] = node
    require(set(nodes) == set(range(1, 97)), "Expected exactly the contiguous RSE local IDs 1..96.")

    rows = list(csv.reader(io.StringIO(snapshot["Opc.Ua.RSE.NodeIds.csv"].decode("utf-8"))))
    require(len(rows) == 96 and all(len(row) == 3 for row in rows), "Expected 96 complete identifier rows.")
    require(len({row[0] for row in rows}) == 96, "Duplicate symbolic name in the identifier sidecar.")
    require([int(row[1]) for row in rows] == list(range(1, 97)), "Identifier sidecar is not contiguous.")
    labels = identifier_labels(contract, nodes)
    require(set(labels) == set(nodes), "Contract and NodeSet identity inventories differ.")
    for name, identity, kind in rows:
        node = nodes[int(identity)]
        require(
            labels[int(identity)] == name and node.tag == f'{{{XML_NAMESPACES["n"]}}}UA{kind}',
            f"NodeSet/CSV mismatch for {name}.",
        )

    types = {
        item["name"]: f'ns=1;i={item["id"]}'
        for category in ("enums", "structures", "objectTypes")
        for item in contract[category]
    }
    require(len(types) == 16, "Unexpected contract type inventory.")
    field_types = contract["builtins"] | types
    for structure in contract["structures"]:
        actual = nodes[structure["id"]].findall("n:Definition/n:Field", XML_NAMESPACES)
        expected = [(field["name"], field_types[field["type"]]) for field in structure["fields"]]
        require(
            [(field.get("Name"), field.get("DataType")) for field in actual] == expected,
            f'Field order/type mismatch for {structure["name"]}.',
        )
        require(
            not any(field["type"] in ("RequestHeader", "ResponseHeader") for field in structure["fields"]),
            "Manager envelopes must not introduce wrapper headers.",
        )

    require(
        [method["name"] for method in contract["methods"]] ==
        ["Submit", "GetResult", "Acknowledge", "Cancel", "Abandon"],
        "Expected five distinct manager Methods.",
    )
    for method in contract["methods"]:
        node = nodes[method["id"]]
        require(
            node.get("ParentNodeId") == types[method["owner"]]
            and node.get("BrowseName") == "1:" + method["name"],
            f'Incorrect owner or qualified BrowseName for {method["name"]}.',
        )
        for direction, argument_name in (("input", "Request"), ("output", "Response")):
            arguments = nodes[method[direction + "ArgumentsId"]].findall(
                "n:Value/u:ListOfExtensionObject/u:ExtensionObject/u:Body/u:Argument", XML_NAMESPACES
            )
            require(len(arguments) == 1, f'{method["name"]} must have exactly one {direction} argument.')
            argument = arguments[0]
            type_name = method[argument_name.lower()]
            require(
                argument.findtext("u:Name", namespaces=XML_NAMESPACES) == argument_name
                and argument.findtext("u:ValueRank", namespaces=XML_NAMESPACES) == "-1"
                and argument.findtext("u:DataType/u:Identifier", namespaces=XML_NAMESPACES) == types[type_name],
                f'Incorrect scalar {direction} binding for {method["name"]}.',
            )
    require(
        nodes[71].get("ParentNodeId") == "ns=1;i=73" and nodes[71].get("BrowseName") == "1:Capabilities",
        "Capabilities must be an RSE-qualified child of the manager.",
    )
    require(nodes[89].get("SymbolicName") == "RseNamespace", "NamespaceMetadata must remain in the primary model.")


def import_snapshot(source_directory):
    snapshot = read_snapshot(source_directory)
    changed = []
    for name, content in snapshot.items():
        destination, _ = FILES[name]
        if destination.exists() and destination.read_bytes().replace(b"\r\n", b"\n") == content:
            continue
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(content)
        changed.append(str(destination.relative_to(ROOT)))
    return changed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="Verify all pinned inputs without writing files.")
    mode.add_argument(
        "--import-from", type=Path, metavar="DIRECTORY",
        help="Import the four verified files from a local committed-source export; never fetch a moving branch.",
    )
    args = parser.parse_args()
    if args.import_from is not None:
        changed = import_snapshot(args.import_from)
        print("Updated: " + ", ".join(changed) if changed else "Pinned model inputs already agree.")
    else:
        read_snapshot()
    print(f"RSE {REVISION}: Core {SOURCE_COMMIT}; 96 namespace-local Nodes, 16 types and five Methods verified.")


if __name__ == "__main__":
    main()
