# Copyright (c) OPC Foundation. Licensed under the MIT License.
"""Focused source/asset/package allowlist regressions. Uses only the standard library."""

import importlib.util
from pathlib import Path
import shutil
import sys
import unittest
from unittest.mock import patch
import warnings
import xml.etree.ElementTree as ET
import zipfile

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1] / "src" / "Opc.Ua.Native.Http3"
PACKAGE = ROOT / "artifacts/packages/OPCFoundation.NetStandard.Opc.Ua.Native.Http3.0.0.0-native-validation.nupkg"
DEBUG_PACKAGE = ROOT / "artifacts/packages/OPCFoundation.NetStandard.Opc.Ua.Native.Http3.Debug.0.0.0-native-validation.nupkg"
OUTPUT = HERE / "artifacts/package-tests"


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


verify = module("verify", ROOT / "build/verify.py")
pack = module("native_http3_package", ROOT / "build/package.py")


class PackageTests(unittest.TestCase):
    """Checks that packaging cannot silently add proofs, platforms, or stale source receipts."""

    @classmethod
    def setUpClass(cls):
        OUTPUT.mkdir(parents=True, exist_ok=True)
        if not PACKAGE.is_file() or not DEBUG_PACKAGE.is_file():
            raise FileNotFoundError("Run Pack-Native.ps1 -ValidationOnly for both Release and Debug before packaging tests")

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(OUTPUT)

    def mutant(self, name, change):
        path = OUTPUT / name
        with zipfile.ZipFile(PACKAGE) as source, zipfile.ZipFile(path, "w") as destination:
            for entry in source.infolist():
                content = source.read(entry)
                replacement = change(entry.filename, content)
                if replacement is not None:
                    destination.writestr(entry.filename, replacement)
        return path

    def test_current_package_has_exact_allowed_source_and_two_native_assets(self):
        verify.package(PACKAGE)
        with zipfile.ZipFile(PACKAGE) as archive:
            binaries = [name for name in archive.namelist() if name.endswith((".dll", ".so", ".pdb", ".exe"))]
        self.assertEqual(sorted(binaries), [
            "runtimes/linux-x64/native/libopcua_http3.so", "runtimes/win-x64/native/opcua_http3.dll"])

    def test_debug_identity_is_distinct_and_retains_the_same_qualified_native_images(self):
        verify.package(DEBUG_PACKAGE)
        base_id = "OPCFoundation.NetStandard.Opc.Ua.Native.Http3"
        with zipfile.ZipFile(PACKAGE) as release, zipfile.ZipFile(DEBUG_PACKAGE) as debug:
            for archive, package_id in [(release, base_id), (debug, base_id + ".Debug")]:
                manifests = [name for name in archive.namelist() if name.endswith(".nuspec")]
                self.assertEqual(manifests, [package_id + ".nuspec"])
                metadata = ET.fromstring(archive.read(manifests[0]))
                self.assertEqual(metadata.findtext("{*}metadata/{*}id"), package_id)
                self.assertFalse(any(name.startswith("lib/") for name in archive.namelist()))
            for rid, filename in verify.RIDS.items():
                location = f"runtimes/{rid}/native/{filename}"
                self.assertEqual(release.read(location), debug.read(location))

    def test_missing_extra_and_unknown_package_manifests_are_rejected(self):
        manifest = "OPCFoundation.NetStandard.Opc.Ua.Native.Http3.nuspec"
        for replacement in [None, "unexpected.nuspec", "OPCFoundation.NetStandard.Opc.Ua.Native.Http3.Debug.nuspec"]:
            with self.subTest(replacement=replacement):
                retain_original = replacement is not None and replacement.endswith(".Debug.nuspec")
                path = self.mutant(
                    "manifest.nupkg", lambda name, data: data if name != manifest or retain_original else None)
                if replacement is not None:
                    with zipfile.ZipFile(path, "a") as archive:
                        archive.writestr(replacement, b"<package><metadata><id>unexpected</id></metadata></package>")
                with self.assertRaisesRegex(ValueError, "exactly one approved package manifest"):
                    verify.package(path)

    def test_manifest_package_identity_must_match_its_approved_filename(self):
        manifest = "OPCFoundation.NetStandard.Opc.Ua.Native.Http3.nuspec"
        for identity in [None, "OPCFoundation.NetStandard.Opc.Ua.Native.Http3.Debug", "unexpected"]:
            with self.subTest(identity=identity):
                def replace_identity(name, data):
                    if name != manifest:
                        return data
                    root = ET.fromstring(data)
                    metadata = root.find("{*}metadata")
                    node = metadata.find("{*}id")
                    if identity is None:
                        metadata.remove(node)
                    else:
                        node.text = identity
                    return ET.tostring(root, encoding="utf-8")

                path = self.mutant("identity.nupkg", replace_identity)
                with self.assertRaisesRegex(ValueError, "identity does not match"):
                    verify.package(path)

    def test_fixture_and_unsupported_rid_files_are_rejected(self):
        for extra in ["runtimes/win-x64/native/proof.dll", "runtimes/linux-arm64/native/libopcua_http3.so",
                      "private/member-spec.md", "identities/test.key"]:
            path = self.mutant("extra.nupkg", lambda _, data: data)
            with zipfile.ZipFile(path, "a") as archive:
                archive.writestr(extra, b"not allowed")
            with self.assertRaisesRegex(ValueError, "not explicitly allowed"):
                verify.package(path)

    def test_payload_hash_changes_are_rejected(self):
        path = self.mutant("tampered.nupkg", lambda name, data: data + b"x" if name.endswith(".dll") else data)
        with self.assertRaisesRegex(ValueError, "hash mismatch"):
            verify.package(path)

    def test_missing_license_or_sbom_is_rejected(self):
        for omitted in ["LICENSE.txt", "sbom/linux-x64.cdx.json", "notices/microsoft/Windows-SDK-10.0.26100.0-license.rtf"]:
            path = self.mutant("missing.nupkg", lambda name, data: None if name == omitted else data)
            with self.assertRaisesRegex(ValueError, "files missing"):
                verify.package(path)

    def test_duplicate_entries_are_rejected_even_when_identical(self):
        path = self.mutant("duplicate.nupkg", lambda _, data: data)
        with zipfile.ZipFile(path, "a") as archive, warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            archive.writestr("LICENSE.txt", (ROOT / "LICENSE.txt").read_bytes())
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            verify.package(path)

    def test_stale_compilation_receipts_cannot_relabel_existing_binaries(self):
        original = verify.load

        def altered(relative):
            value = original(relative)
            if relative == "artifacts/receipts/win-x64.json":
                value["source_digest"] = "0" * 64
            return value

        with patch.object(verify, "load", side_effect=altered):
            with self.assertRaisesRegex(ValueError, "Stale binary/source receipt"):
                pack.entries()

    def test_both_actual_libraries_expose_only_the_production_abi(self):
        expected = sorted(verify.load("build/exports.json"))
        self.assertEqual(len(expected), 9)
        for rid, filename in verify.RIDS.items():
            content = (ROOT / "artifacts/runtimes" / rid / "native" / filename).read_bytes()
            result = verify.pe(content) if rid == "win-x64" else verify.elf(content)
            self.assertEqual(result["exports"], expected)
            if rid == "linux-x64":
                self.assertEqual(result["declared_glibc_floor"], "2.34")
                self.assertFalse(result["minimum_host_execution_validated"])
        self.assertTrue(all(name.startswith("opcua_http3_") for name in expected))


if __name__ == "__main__":
    unittest.main(verbosity=2)
