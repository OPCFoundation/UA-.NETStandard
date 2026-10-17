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

"""Synchronize the public experimental contract with authoritative stack generator inputs."""

import argparse
import json
from pathlib import Path
from xml.sax.saxutils import escape, quoteattr


ROOT = Path(__file__).resolve().parents[2]
DESIGN = ROOT / "tools" / "Opc.Ua.SourceGeneration.Core" / "Design"
BEGIN = "  <!-- BEGIN generated experimental Async Services 0.2.0 -->"
END = "  <!-- END generated experimental Async Services 0.2.0 -->"
CATEGORY = 'Category="RuntimeOptional" ReleaseStatus="Draft"'
BUILTIN_TYPES = {
    "Boolean", "UInt32", "UInt64", "String", "Guid", "ByteString", "NodeId",
    "StatusCode", "LocalizedText",
}


def documentation(text, indent=4, element="Documentation"):
    return f'{" " * indent}<opc:{element}>{escape(text)}</opc:{element}>'


def field_type(name):
    if name == "Structure":
        return "s0:ExtensionObject"
    return f"s0:{name}" if name in BUILTIN_TYPES else name


def service_dictionary(contract):
    lines = [BEGIN]
    for enum in contract["enums"]:
        lines.append(
            f'  <opc:EnumeratedType Name={quoteattr(enum["name"])} NotInAddressSpace="true" {CATEGORY}>'
        )
        lines.append(documentation(enum["description"]))
        for value in enum["values"]:
            lines.append(f'    <opc:Value Name={quoteattr(value["name"])} Value="{value["value"]}">')
            lines.append(documentation(value["description"], 6))
            lines.append("    </opc:Value>")
        lines.append("  </opc:EnumeratedType>")
        lines.append("")

    services = contract["services"]
    messages = {service[key] for service in services for key in ("request", "response")}
    structures = {item["name"]: item for item in contract["structures"]}
    for item in contract["structures"]:
        if item["name"] in messages:
            continue
        lines.append(f'  <opc:ComplexType Name={quoteattr(item["name"])} NotInAddressSpace="true" {CATEGORY}>')
        lines.append(documentation(item["description"]))
        for field in item["fields"]:
            lines.append(
                f'    <opc:Field Name={quoteattr(field["name"])} DataType={quoteattr(field_type(field["type"]))}>'
            )
            lines.append(documentation(field["description"], 6))
            lines.append("    </opc:Field>")
        lines.append("  </opc:ComplexType>")
        lines.append("")

    for service in services:
        lines.append(
            f'  <opc:ServiceType Name={quoteattr(service["name"])} '
            'Category="RuntimeOptional,AsyncOnly" ReleaseStatus="Draft">'
        )
        for direction in ("Request", "Response"):
            lines.append(f"    <opc:{direction}>")
            for field in structures[service[direction.lower()]]["fields"]:
                lines.append(
                    f'      <opc:Parameter Name={quoteattr(field["name"])} '
                    f'DataType={quoteattr(field_type(field["type"]))}>'
                )
                lines.append(documentation(field["description"], 8))
                lines.append("      </opc:Parameter>")
            lines.append(f"    </opc:{direction}>")
        lines.append("  </opc:ServiceType>")
        lines.append("")
    lines.append(END)
    return "\n".join(lines)


def model_dictionary(contract):
    base_types = {"i=58": "BaseObjectType", "i=2069": "AuditSessionEventType"}
    lines = [BEGIN]
    for item in contract["objectTypes"]:
        base = base_types[item["baseType"]]
        lines.append(
            f'  <opc:ObjectType SymbolicName={quoteattr(item["name"])} '
            f'BaseType="ua:{base}" {CATEGORY}>'
        )
        lines.append(documentation(item["description"], element="Description"))
        lines.append("    <opc:Children>")
        for member in item["members"]:
            element = "Property" if member["reference"] == "HasProperty" else "Variable"
            rank = ' ValueRank="Array"' if member.get("valueRank") == 1 else ""
            lines.append(
                f'      <opc:{element} SymbolicName={quoteattr(member["name"])} '
                f'DataType="ua:{member["type"]}" ModellingRule="Mandatory"{rank}>'
            )
            lines.append(documentation(member["description"], 8, "Description"))
            lines.append(f"      </opc:{element}>")
        lines.append("    </opc:Children>")
        lines.append("  </opc:ObjectType>")
        lines.append("")
    for item in contract["structures"]:
        lines.extend([
            f'  <opc:Object SymbolicName="{item["name"]}_Encoding_DefaultJson" '
            f'TypeDefinition="ua:DataTypeEncodingType" {CATEGORY}>',
            "    <opc:BrowseName>Default JSON</opc:BrowseName>",
            "    <opc:References>",
            '      <opc:Reference IsInverse="true">',
            "        <opc:ReferenceType>ua:HasEncoding</opc:ReferenceType>",
            f'        <opc:TargetId>ua:{item["name"]}</opc:TargetId>',
            "      </opc:Reference>",
            "    </opc:References>",
            "  </opc:Object>",
            "",
        ])
    lines.append(END)
    return "\n".join(lines)


def identifiers(contract):
    rows = []
    for enum in contract["enums"]:
        rows.extend([
            (enum["name"], enum["id"], "DataType"),
            (enum["name"] + "_EnumStrings", enum["enumStringsId"], "Variable"),
        ])
    for item in contract["structures"]:
        rows.append((item["name"], item["id"], "DataType"))
        rows.extend(
            (item["name"] + "_Encoding_" + name, node_id, "Object")
            for name, node_id in item["encodings"].items()
        )
    for item in contract["objectTypes"]:
        rows.append((item["name"], item["id"], "ObjectType"))
        rows.extend(
            (item["name"] + "_" + member["name"], member["id"], "Variable")
            for member in item["members"]
        )
    rows.extend((item["name"], item["id"], "Object") for item in contract["instances"])
    if len({row[0] for row in rows}) != len(rows) or len({row[1] for row in rows}) != len(rows):
        raise ValueError("The contract contains duplicate names or numeric identifiers.")
    return rows


def xml_update(path, closing, content):
    text = path.read_text(encoding="utf-8-sig")
    if BEGIN in text:
        before, tail = text.split(BEGIN, 1)
        _, after = tail.split(END, 1)
        return before + content + after
    if text.count(closing) != 1:
        raise ValueError(f"Expected exactly one closing root element in {path.name}.")
    return text.replace(closing, content + "\n" + closing)


def csv_update(contract):
    rows = identifiers(contract)
    names = {row[0] for row in rows}
    used = {row[1] for row in rows}
    retained = []
    for line in (DESIGN / "StandardTypes.csv").read_text(encoding="utf-8-sig").splitlines():
        columns = line.split(",")
        if columns[0] in names:
            continue
        if len(columns) >= 2 and columns[1].isdigit() and int(columns[1]) in used:
            raise ValueError(f"Provisional identifier collides with existing allocation: {line}")
        retained.append(line)
    retained.extend(f"{name},{node_id},{kind}" for name, node_id, kind in sorted(rows, key=lambda row: row[1]))
    return "\n".join(retained) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Reject drift without writing files.")
    args = parser.parse_args()
    contract = json.loads((DESIGN / "AsyncServices.model-contract.json").read_text(encoding="utf-8"))
    if contract["revision"] != "0.2.0" or contract["namespaceUri"] != "http://opcfoundation.org/UA/":
        raise ValueError("This synchronizer requires the reviewed namespace-zero 0.2.0 contract.")
    outputs = {
        DESIGN / "UA Core Services.xml": xml_update(
            DESIGN / "UA Core Services.xml", "</opc:TypeDictionary>", service_dictionary(contract)
        ),
        DESIGN / "StandardTypes.xml": xml_update(
            DESIGN / "StandardTypes.xml", "</opc:ModelDesign>", model_dictionary(contract)
        ),
        DESIGN / "StandardTypes.csv": csv_update(contract),
    }
    changed = []
    for path, text in outputs.items():
        if path.read_text(encoding="utf-8-sig") == text:
            continue
        changed.append(path.name)
        if not args.check:
            encoding = "utf-8-sig" if path.read_bytes().startswith(b"\xef\xbb\xbf") else "utf-8"
            path.write_text(text, encoding=encoding, newline="\r\n")
    if changed and args.check:
        raise SystemExit("Contract drift: " + ", ".join(changed))
    print(f'Experimental 0.2.0 contract: {len(identifiers(contract))} provisional allocations; '
          + ("updated " + ", ".join(changed) if changed else "generator inputs agree."))


if __name__ == "__main__":
    main()
