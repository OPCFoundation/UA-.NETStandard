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

"""Regression checks for pinned RSE model adoption, independent of C# generation."""

import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

import sync_contract as contract_tool


class ContractAdoptionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.snapshot = contract_tool.read_snapshot()

    def test_pinned_snapshot_is_complete(self):
        self.assertEqual(len(self.snapshot), 4)
        for name, (_, expected) in contract_tool.FILES.items():
            self.assertEqual(hashlib.sha256(self.snapshot[name]).hexdigest(), expected, name)
        contract_tool.validate_model(self.snapshot)

    def test_crlf_export_is_accepted_without_content_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name, content in self.snapshot.items():
                (root / name).write_bytes(content.replace(b"\n", b"\r\n"))
            self.assertEqual(contract_tool.read_snapshot(root), self.snapshot)

    def test_rejects_changed_committed_file(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name, content in self.snapshot.items():
                (root / name).write_bytes(content)
            for name, content in self.snapshot.items():
                with self.subTest(name=name):
                    (root / name).write_bytes(content + b" ")
                    with self.assertRaisesRegex(ValueError, "does not match pinned Core commit"):
                        contract_tool.read_snapshot(root)
                    (root / name).write_bytes(content)

    def test_rejects_namespace_zero_definition(self):
        root = self.xml()
        root.find("n:UADataType", contract_tool.XML_NAMESPACES).set("NodeId", "i=1200000")
        with self.assertRaisesRegex(ValueError, "Non-RSE definition"):
            contract_tool.validate_model(self.with_xml(root))

    def test_rejects_changed_namespace_uri(self):
        root = self.xml()
        root.find("n:NamespaceUris/n:Uri", contract_tool.XML_NAMESPACES).text = "urn:wrong-model"
        with self.assertRaisesRegex(ValueError, "Expected one RSE namespace"):
            contract_tool.validate_model(self.with_xml(root))

    def test_rejects_noncanonical_code_generation_namespace(self):
        for namespace in ("Opc.Ua.Rse", "Opc.Ua.RSE", ""):
            with self.subTest(namespace=namespace):
                data = json.loads(self.snapshot["model-contract.json"])
                data["codeGenerationNamespace"] = namespace
                snapshot = self.snapshot | {"model-contract.json": json.dumps(data).encode("utf-8")}
                with self.assertRaisesRegex(ValueError, "Unexpected public C# namespace"):
                    contract_tool.validate_model(snapshot)

    def test_standard_model_does_not_define_retired_wire_contract(self):
        legacy = ("SubmitService", "GetServiceResult", "ControlServiceExecution")
        names = set(legacy)
        data = json.loads(self.snapshot["model-contract.json"])
        names.update(item["name"] for category in ("enums", "structures", "objectTypes") for item in data[category])
        for name in ("UA Core Services.xml", "StandardTypes.xml"):
            root = ET.parse(contract_tool.DESIGN / name).getroot()
            actual = {element.get("Name") or element.get("SymbolicName") for element in root}
            self.assertFalse(actual & names, name)
        for line in (contract_tool.DESIGN / "StandardTypes.csv").read_text(encoding="utf-8").splitlines():
            identifier = int(line.split(",")[1])
            self.assertFalse(1_200_000 <= identifier < 1_220_000, line)

    def test_rejects_identifier_sidecar_drift(self):
        name = "Opc.Ua.RSE.NodeIds.csv"
        original = self.snapshot[name].decode("utf-8")
        for changed in (
            original.replace("ServiceOperationState,1,DataType", "ServiceOperationState,2,DataType"),
            original.replace("ServiceOperationState,1,DataType", "ServiceOperationState,1,Object"),
            original.replace("ServiceOperationState,1,DataType\n", ""),
            original.replace("ServiceOperationOutcome,3,DataType", "ServiceOperationState,3,DataType"),
            original.replace("RecoverableExecutionManager_Capabilities,71", "Capabilities,71"),
        ):
            with self.subTest(changed=changed.splitlines()[:3]):
                snapshot = self.snapshot | {name: changed.encode("utf-8")}
                with self.assertRaises(ValueError):
                    contract_tool.validate_model(snapshot)

    def test_rejects_changed_field_order(self):
        root = self.xml()
        definition = root.find("n:UADataType[@NodeId='ns=1;i=11']/n:Definition", contract_tool.XML_NAMESPACES)
        first = definition[0]
        definition.remove(first)
        definition.insert(1, first)
        with self.assertRaisesRegex(ValueError, "Field order/type mismatch"):
            contract_tool.validate_model(self.with_xml(root))

    def test_rejects_wrapper_header_even_when_json_and_xml_agree(self):
        root = self.xml()
        data = json.loads(self.snapshot["model-contract.json"])
        data["structures"][0]["fields"].append({"name": "Header", "type": "RequestHeader"})
        definition = root.find("n:UADataType[@NodeId='ns=1;i=11']/n:Definition", contract_tool.XML_NAMESPACES)
        ET.SubElement(definition, f'{{{contract_tool.XML_NAMESPACES["n"]}}}Field', Name="Header", DataType="i=389")
        snapshot = self.with_xml(root) | {"model-contract.json": json.dumps(data).encode("utf-8")}
        with self.assertRaisesRegex(ValueError, "must not introduce wrapper headers"):
            contract_tool.validate_model(snapshot)

    def test_rejects_missing_control_method(self):
        data = json.loads(self.snapshot["model-contract.json"])
        data["methods"].pop()
        snapshot = self.snapshot | {"model-contract.json": json.dumps(data).encode("utf-8")}
        with self.assertRaisesRegex(ValueError, "identity inventories differ|five distinct manager Methods"):
            contract_tool.validate_model(snapshot)

    def test_rejects_method_argument_shape_changes(self):
        for identity in (75, 76, 78, 79, 81, 82, 84, 85, 87, 88):
            for change in ("rank", "type", "missing", "extra"):
                with self.subTest(identity=identity, change=change):
                    root = self.xml()
                    body = root.find(
                        f"n:UAVariable[@NodeId='ns=1;i={identity}']/"
                        "n:Value/u:ListOfExtensionObject/u:ExtensionObject/u:Body",
                        contract_tool.XML_NAMESPACES,
                    )
                    argument = body[0]
                    if change == "rank":
                        argument.find("u:ValueRank", contract_tool.XML_NAMESPACES).text = "1"
                    elif change == "type":
                        argument.find("u:DataType/u:Identifier", contract_tool.XML_NAMESPACES).text = "i=22"
                    elif change == "missing":
                        body.remove(argument)
                    else:
                        body.append(copy.deepcopy(argument))
                    with self.assertRaises(ValueError):
                        contract_tool.validate_model(self.with_xml(root))

    def test_rejects_obsolete_conformance_names(self):
        model = json.loads(self.snapshot["model-contract.json"])
        revision = json.loads(self.snapshot["revision-contract.json"])
        for data in (model, revision):
            data["conformanceUnits"] = [name.replace("RSE-", "ASE-") for name in data["conformanceUnits"]]
        snapshot = self.snapshot | {
            "model-contract.json": json.dumps(model).encode("utf-8"),
            "revision-contract.json": json.dumps(revision).encode("utf-8"),
        }
        with self.assertRaisesRegex(ValueError, "Inconsistent RSE conformance identifiers"):
            contract_tool.validate_model(snapshot)

    def test_rejects_changed_assertion_identifier(self):
        for replacement in ("ASE3-049", "RSE3-050"):
            with self.subTest(replacement=replacement):
                revision = json.loads(self.snapshot["revision-contract.json"])
                revision["assertionIds"][48] = replacement
                snapshot = self.snapshot | {"revision-contract.json": json.dumps(revision).encode("utf-8")}
                with self.assertRaisesRegex(ValueError, "all seventy numeric suffixes"):
                    contract_tool.validate_model(snapshot)

    def test_rejected_import_does_not_change_any_destination(self):
        with tempfile.TemporaryDirectory() as directory:
            root, source, files = self.temporary_paths(directory)
            for destination, _ in files.values():
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(b"unchanged")
            (source / "revision-contract.json").write_bytes(b"not the committed final file")
            with patch.multiple(contract_tool, ROOT=root, FILES=files):
                with self.assertRaisesRegex(ValueError, "does not match pinned Core commit"):
                    contract_tool.import_snapshot(source)
            for destination, _ in files.values():
                self.assertEqual(destination.read_bytes(), b"unchanged")

    def test_import_writes_exact_bytes_once(self):
        with tempfile.TemporaryDirectory() as directory:
            root, source, files = self.temporary_paths(directory)
            with patch.multiple(contract_tool, ROOT=root, FILES=files):
                self.assertEqual(len(contract_tool.import_snapshot(source)), 4)
                for name, (destination, _) in files.items():
                    self.assertEqual(destination.read_bytes(), self.snapshot[name])
                timestamps = {name: path.stat().st_mtime_ns for name, (path, _) in files.items()}
                self.assertEqual(contract_tool.import_snapshot(source), [])
                self.assertEqual(timestamps, {name: path.stat().st_mtime_ns for name, (path, _) in files.items()})

    def xml(self):
        return ET.fromstring(self.snapshot["Opc.Ua.RSE.NodeSet2.xml"])

    def with_xml(self, root):
        return self.snapshot | {"Opc.Ua.RSE.NodeSet2.xml": ET.tostring(root, encoding="utf-8")}

    def temporary_paths(self, directory):
        root = Path(directory)
        source = root / "source"
        source.mkdir()
        for name, content in self.snapshot.items():
            (source / name).write_bytes(content)
        files = {
            name: (root / "destination" / name, expected)
            for name, (_, expected) in contract_tool.FILES.items()
        }
        return root, source, files


if __name__ == "__main__":
    unittest.main()
