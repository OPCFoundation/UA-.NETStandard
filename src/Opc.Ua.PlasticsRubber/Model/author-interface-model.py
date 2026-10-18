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
"""Build an independently maintained interface model from the MIT 1.02 basis.

Only the licensed basis is an input. The 1.03 interface changes below are
authored from functional node/field/reference facts; no 1.03 NodeSet, prose,
identifier table, or dictionary blob is consumed. Both dictionaries are
generated afresh from the resulting DataType definitions.

Run from any directory: python author-interface-model.py
"""

import argparse
import base64
import hashlib
import io
from pathlib import Path
import runpy
import xml.etree.ElementTree as ET


MODEL_URI = "http://opcfoundation.org/UA/PlasticsRubber/GeneralTypes/"
UA_URI = "http://opcfoundation.org/UA/"
NODESET = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
TYPES = "http://opcfoundation.org/UA/2008/02/Types.xsd"
BINARY = "http://opcfoundation.org/BinarySchema/"
XSD = "http://www.w3.org/2001/XMLSchema"
BASIS_HASH = "5fe4469bf64ebab5a828d2f888dca85e985cf2d48286029d37565c09e4ac9ba8"
DIRECTORY = Path(__file__).resolve().parent
BASIS = DIRECTORY / "Basis" / "Opc.Ua.PlasticsRubber.GeneralTypes.1.02.NodeSet2.xml"
OUTPUT = DIRECTORY / "Opc.Ua.PlasticsRubber.NodeSet2.xml"
IDENTIFIERS = DIRECTORY / "Opc.Ua.PlasticsRubber.NodeIds.csv"


def sub(parent, name, attributes=None, text=None, namespace=NODESET):
    """Append one XML element with explicit namespace and attributes."""
    element = ET.SubElement(parent, f"{{{namespace}}}{name}", attributes or {})
    element.text = text
    return element


def reference(node, kind, target, forward=True):
    """Add an OPC UA reference without allocating or changing identifiers."""
    references = node.find(f"{{{NODESET}}}References")
    if references is None:
        references = sub(node, "References")
    attributes = {"ReferenceType": kind}
    if not forward:
        attributes["IsForward"] = "false"
    sub(references, "Reference", attributes, target)


def new_node(root, kind, identifier, browse_name, **attributes):
    """Author a node with a short functional display label, not copied prose."""
    attributes.update(NodeId=f"ns=1;i={identifier}", BrowseName=browse_name)
    node = sub(root, kind, attributes)
    sub(node, "DisplayName", text=browse_name.removeprefix("1:"))
    return node


def variable(root, identifier, browse_name, data_type, parent, type_id, rule=None, **attributes):
    """Author a variable declaration and its inverse parent reference."""
    node = new_node(
        root, "UAVariable", identifier, browse_name,
        DataType=data_type, ParentNodeId=f"ns=1;i={parent}", **attributes)
    kind = "HasComponent" if browse_name.startswith("1:Status") or parent in (6002, 6004) else "HasProperty"
    reference(node, kind, f"ns=1;i={parent}", False)
    reference(node, "HasTypeDefinition", f"i={type_id}")
    if rule is not None:
        reference(node, "HasModellingRule", f"i={rule}")
    return node


def value(node, kind, text):
    """Set a scalar value, replacing rather than preserving stale content."""
    existing = node.find(f"{{{NODESET}}}Value")
    if existing is not None:
        node.remove(existing)
    sub(sub(node, "Value"), kind, text=text, namespace=TYPES)


def author_interface(root):
    """Implement all thirteen additions and nine changes to the 1.02 model."""
    nodes = {node.get("NodeId"): node for node in root if node.get("NodeId")}
    model = root.find(f"{{{NODESET}}}Models/{{{NODESET}}}Model")
    model.set("Version", "1.03")
    model.set("PublicationDate", "2021-05-10T12:00:00Z")
    for required in model:
        if required.get("ModelUri") == UA_URI:
            required.set("Version", "1.04.7")
            required.set("PublicationDate", "2020-07-15T00:00:00Z")
        elif required.get("ModelUri") == UA_URI + "DI/":
            required.set("Version", "1.02.2")
            required.set("PublicationDate", "2020-06-02T00:00:00Z")

    # The published interface reassigns 6711; retaining its old engineering-unit
    # property would leave a dangling reference on ActualTemperature.
    root.remove(nodes["ns=1;i=6711"])
    temperature_refs = nodes["ns=1;i=6017"].find(f"{{{NODESET}}}References")
    for item in list(temperature_refs):
        if item.text == "ns=1;i=6711":
            temperature_refs.remove(item)

    for parent, kind, child in (
        (1056, "HasComponent", 6711),
        (1068, "HasProperty", 6721),
        (5054, "HasProperty", 6722),
        (6002, "HasComponent", 6716),
        (6004, "HasComponent", 6717),
    ):
        reference(nodes[f"ns=1;i={parent}"], kind, f"ns=1;i={child}")

    alarm = new_node(root, "UAObjectType", 1070, "1:MonitoredParameterAlarmType")
    reference(alarm, "HasSubtype", "ns=1;i=1052", False)
    reference(alarm, "HasComponent", "ns=1;i=6718")

    classified = new_node(root, "UADataType", 3003, "1:ClassifiedActiveErrorDataType")
    reference(classified, "HasSubtype", "ns=1;i=3028", False)
    reference(classified, "HasEncoding", "ns=1;i=5065")
    reference(classified, "HasEncoding", "ns=1;i=5066")
    definition = sub(classified, "Definition", {"Name": "1:ClassifiedActiveErrorDataType"})
    sub(definition, "Field", {
        "Name": "SourceNodes", "DataType": "NodeId", "ValueRank": "1", "ArrayDimensions": "0"})
    sub(definition, "Field", {"Name": "Classification", "DataType": "UInt16"})

    for identifier, name, symbolic, description in (
        (5065, "Default Binary", "DefaultBinary", 6716),
        (5066, "Default XML", "DefaultXml", 6717),
    ):
        encoding = new_node(root, "UAObject", identifier, name, SymbolicName=symbolic)
        reference(encoding, "HasEncoding", "ns=1;i=3003", False)
        reference(encoding, "HasDescription", f"ns=1;i={description}")
        reference(encoding, "HasTypeDefinition", "i=76")

    for identifier, parent, rule, enums, text in (
        (6711, 1056, 80, 6714, 6715),
        (6718, 1070, 78, 6719, 6720),
    ):
        status = variable(root, identifier, "1:Status", "UInt16", parent, 11238, rule)
        reference(status, "HasProperty", f"ns=1;i={enums}")
        reference(status, "HasProperty", f"ns=1;i={text}")
        variable(
            root, enums, "EnumValues", "EnumValueType", identifier, 68, 78,
            ValueRank="1", ArrayDimensions="0")
        variable(root, text, "ValueAsText", "LocalizedText", identifier, 68, 78)

    for identifier, parent, encoding, content in (
        (6716, 6002, 5065, "ClassifiedActiveErrorDataType"),
        (6717, 6004, 5066, "//xs:element[@name='ClassifiedActiveErrorDataType']"),
    ):
        description = variable(root, identifier, "1:ClassifiedActiveErrorDataType", "String", parent, 69)
        reference(description, "HasDescription", f"ns=1;i={encoding}", False)
        value(description, "String", content)

    variable(root, 6721, "1:Position", "String", 1068, 68, 80)
    variable(root, 6722, "1:Position", "String", 5054, 68, 80)
    value(nodes["ns=1;i=6420"], "DateTime", "2021-05-10T12:00:00Z")
    value(nodes["ns=1;i=6441"], "String", "1.03")


def type_definitions(root):
    """Resolve declared type names and inheritance from the authored NodeSet."""
    types = {}
    for node in root.findall(f"{{{NODESET}}}UADataType"):
        definition = node.find(f"{{{NODESET}}}Definition")
        if definition is None:
            raise ValueError(f"Missing DataType definition: {node.get('NodeId')}")
        parent = next(
            ref.text for ref in node.findall(f"{{{NODESET}}}References/{{{NODESET}}}Reference")
            if ref.get("ReferenceType") == "HasSubtype" and ref.get("IsForward") == "false")
        types[node.get("NodeId")] = {
            "name": node.get("BrowseName").removeprefix("1:"),
            "parent": parent,
            "fields": [dict(field.attrib) for field in definition],
            "enum": all("Value" in field.attrib for field in definition),
        }
    return types


def make_dictionaries(root):
    """Generate BSD and XSD from definitions, including inherited wire fields."""
    types = type_definitions(root)
    local_names = {entry["name"] for entry in types.values()}
    aliases = {
        alias.get("Alias"): alias.text
        for alias in root.findall(f"{{{NODESET}}}Aliases/{{{NODESET}}}Alias")
    }

    def name_of(field):
        name = field.get("DataType", "BaseDataType")
        resolved = aliases.get(name, name)
        if resolved in types:
            return types[resolved]["name"]
        return name

    def inherited_fields(entry):
        parent = types.get(entry["parent"])
        if parent is not None:
            for field, source in inherited_fields(parent):
                yield field, source or parent["name"]
        for field in entry["fields"]:
            yield field, None

    ET.register_namespace("opc", BINARY)
    binary = ET.Element(f"{{{BINARY}}}TypeDictionary", {
        "DefaultByteOrder": "LittleEndian", "TargetNamespace": MODEL_URI,
        "xmlns:tns": MODEL_URI, "xmlns:ua": UA_URI,
    })
    sub(binary, "Import", {"Namespace": UA_URI}, namespace=BINARY)
    binary_builtins = {
        "String": "opc:CharArray", "Duration": "opc:Double",
        "BaseDataType": "ua:Variant", "LocalizedText": "ua:LocalizedText",
        "EUInformation": "ua:EUInformation", "NodeId": "ua:NodeId",
    }
    for entry in types.values():
        if entry["enum"]:
            enum = sub(binary, "EnumeratedType", {
                "Name": entry["name"], "LengthInBits": "32"}, namespace=BINARY)
            for field in entry["fields"]:
                sub(enum, "EnumeratedValue", {
                    "Name": field["Name"], "Value": field["Value"]}, namespace=BINARY)
            continue
        parent = types.get(entry["parent"])
        structure = sub(binary, "StructuredType", {
            "Name": entry["name"],
            "BaseType": f"tns:{parent['name']}" if parent else "ua:ExtensionObject",
        }, namespace=BINARY)
        for field, source in inherited_fields(entry):
            name = name_of(field)
            field_type = f"tns:{name}" if name in local_names else binary_builtins.get(name, f"opc:{name}")
            attributes = {"Name": field["Name"], "TypeName": field_type}
            if source:
                attributes["SourceType"] = f"tns:{source}"
            if field.get("ValueRank") == "1":
                length = {"Name": f"NoOf{field['Name']}", "TypeName": "opc:Int32"}
                if source:
                    length["SourceType"] = f"tns:{source}"
                sub(structure, "Field", length, namespace=BINARY)
                attributes["LengthField"] = length["Name"]
            sub(structure, "Field", attributes, namespace=BINARY)

    ET.register_namespace("xs", XSD)
    schema = ET.Element(f"{{{XSD}}}schema", {
        "elementFormDefault": "qualified", "targetNamespace": MODEL_URI + "Types.xsd",
        "xmlns:tns": MODEL_URI + "Types.xsd", "xmlns:ua": TYPES,
    })
    sub(schema, "import", {"namespace": TYPES}, namespace=XSD)
    xml_builtins = {
        "String": "xs:string", "Boolean": "xs:boolean", "UInt16": "xs:unsignedShort",
        "UInt32": "xs:unsignedInt", "UInt64": "xs:unsignedLong", "Double": "xs:double",
        "Duration": "xs:double", "DateTime": "xs:dateTime", "BaseDataType": "ua:Variant",
    }
    for entry in types.values():
        name = entry["name"]
        if entry["enum"]:
            simple = sub(schema, "simpleType", {"name": name}, namespace=XSD)
            restriction = sub(simple, "restriction", {"base": "xs:string"}, namespace=XSD)
            for field in entry["fields"]:
                sub(restriction, "enumeration", {
                    "value": f"{field['Name']}_{field['Value']}"}, namespace=XSD)
        else:
            structure = sub(schema, "complexType", {"name": name}, namespace=XSD)
            parent = types.get(entry["parent"])
            if parent:
                content = sub(structure, "complexContent", {"mixed": "false"}, namespace=XSD)
                structure = sub(content, "extension", {"base": f"tns:{parent['name']}"}, namespace=XSD)
            sequence = sub(structure, "sequence", namespace=XSD)
            for field in entry["fields"]:
                field_name = name_of(field)
                if field.get("ValueRank") == "1":
                    prefix = "tns" if field_name in local_names else "ua"
                    field_type = f"{prefix}:ListOf{field_name}"
                elif field_name in local_names:
                    field_type = f"tns:{field_name}"
                else:
                    field_type = xml_builtins.get(field_name, f"ua:{field_name}")
                sub(sequence, "element", {
                    "name": field["Name"], "type": field_type, "minOccurs": "0", "maxOccurs": "1",
                }, namespace=XSD)
        sub(schema, "element", {"name": name, "type": f"tns:{name}"}, namespace=XSD)
        array_name = f"ListOf{name}"
        array = sub(schema, "complexType", {"name": array_name}, namespace=XSD)
        sequence = sub(array, "sequence", namespace=XSD)
        sub(sequence, "element", {
            "name": name, "type": f"tns:{name}", "minOccurs": "0",
            "maxOccurs": "unbounded", "nillable": "true",
        }, namespace=XSD)
        sub(schema, "element", {
            "name": array_name, "type": f"tns:{array_name}", "nillable": "true",
        }, namespace=XSD)

    for identifier, dictionary in ((6002, binary), (6004, schema)):
        ET.indent(dictionary, space="  ")
        encoded = base64.b64encode(ET.tostring(dictionary, encoding="utf-8")).decode("ascii")
        node = next(node for node in root if node.get("NodeId") == f"ns=1;i={identifier}")
        value(node, "ByteString", encoded)


def build_model():
    """Produce the authored XML and independently derived identifier guard."""
    source = BASIS.read_bytes()
    if hashlib.sha256(source).hexdigest() != BASIS_HASH:
        raise ValueError("The MIT 1.02 basis does not match the pinned source.")
    text = source.decode("utf-8-sig")
    license_notice = text[text.index("<!--"):text.index("-->") + 3]
    if "OPC Foundation MIT License 1.00" not in license_notice:
        raise ValueError("The redistribution licence is missing.")
    for _, (prefix, uri) in ET.iterparse(BASIS, events=["start-ns"]):
        ET.register_namespace(prefix, uri)
    root = ET.fromstring(source)
    for parent in root.iter():
        for child in list(parent):
            if child.tag == f"{{{NODESET}}}Extensions":
                parent.remove(child)
    author_interface(root)
    make_dictionaries(root)
    nodes = [node for node in root if node.get("NodeId")]
    if len(nodes) != 961 or len({node.get("NodeId") for node in nodes}) != 961:
        raise ValueError("The authored model must contain exactly 961 distinct nodes.")
    ET.indent(root, space="  ")
    notice = (
        "<!-- Independently maintained implementation based on the MIT-licensed 1.02 model.\n"
        "     Functional 1.03 interface changes and both type dictionaries are authored here.\n"
        "     This is NOT the official 1.03 NodeSet. Version/publication metadata identifies\n"
        "     the implemented wire contract, not the provenance of this file.\n"
        "     See companion-models.json and ../NugetREADME.md. No certification is claimed. -->"
    )
    document = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + license_notice + "\n" + notice + "\n"
    document += ET.tostring(root, encoding="unicode") + "\n"
    document_bytes = document.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8")

    repository = DIRECTORY.parents[2]
    derive = runpy.run_path(str(repository / "tools" / "nodesets" / "derive-identifier-table.py"))
    imported = derive["NodeSet"](io.BytesIO(document_bytes))
    symbols = imported.symbolic_ids()
    rows = ["SymbolicName,NodeId,NodeClass"]
    for node in imported.nodes:
        node_id = node.get("NodeId")
        rows.append(f"{symbols[node_id]},{node_id.split('i=')[-1]},{imported.tag(node)[2:]}")
    return document_bytes, ("\r\n".join(rows) + "\r\n").encode("utf-8")


def main():
    """Rebuild or read-only verify the reproducible authored model artifacts."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Verify reproducibility without changing any file.")
    args = parser.parse_args()
    document, identifiers = build_model()
    for path, expected in ((OUTPUT, document), (IDENTIFIERS, identifiers)):
        if path.exists() and path.read_bytes() == expected:
            continue
        if args.check:
            raise ValueError(f"Authored output is not reproducible: {path}")
        path.write_bytes(expected)
    print("PASS reproducible authored model: 961 nodes and 961 independently derived identifiers.")


if __name__ == "__main__":
    main()
