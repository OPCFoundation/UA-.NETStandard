# Copyright (c) OPC Foundation. Licensed under the MIT License.
"""Exact Release/Debug nuspec identity checks; no native build or disk fixture is needed."""
import io
import sys
import unittest
import warnings
import zipfile
from pathlib import Path

BUILD = Path(__file__).resolve().parents[3] / "src" / "Opc.Ua.Native.QuicGo" / "build"
sys.path.insert(0, str(BUILD))
from verify_package import package_identity

BASE = "OPCFoundation.NetStandard.Opc.Ua.Native.QuicGo"


def specification(package_id):
    return ('<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
            f'<metadata><id>{package_id}</id><version>2.0.0</version></metadata></package>')


def identify(entries):
    memory = io.BytesIO()
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", UserWarning)
        with zipfile.ZipFile(memory, "w") as archive:
            for name, contents in entries:
                archive.writestr(name, contents)
    memory.seek(0)
    with zipfile.ZipFile(memory) as archive:
        return package_identity(archive)


class PackageIdentityTests(unittest.TestCase):
    def test_release_identity(self):
        self.assertEqual(identify([(BASE + ".nuspec", specification(BASE))]), (BASE + ".nuspec", BASE))

    def test_debug_identity(self):
        name = BASE + ".Debug"
        self.assertEqual(identify([(name + ".nuspec", specification(name))]), (name + ".nuspec", name))

    def test_missing_nuspec_is_rejected(self):
        with self.assertRaises(ValueError):
            identify([("NugetREADME.md", "not a nuspec")])

    def test_both_variants_are_rejected(self):
        with self.assertRaises(ValueError):
            identify([(BASE + ".nuspec", specification(BASE)),
                      (BASE + ".Debug.nuspec", specification(BASE + ".Debug"))])

    def test_duplicate_nuspec_entry_is_rejected(self):
        with self.assertRaises(ValueError):
            identify([(BASE + ".nuspec", specification(BASE)), (BASE + ".nuspec", specification(BASE))])

    def test_mismatched_actual_id_is_rejected(self):
        with self.assertRaises(ValueError):
            identify([(BASE + ".nuspec", specification(BASE + ".Debug"))])

    def test_unknown_id_is_rejected(self):
        with self.assertRaises(ValueError):
            identify([("Unexpected.nuspec", specification("Unexpected"))])

    def test_nested_nuspec_is_rejected(self):
        with self.assertRaises(ValueError):
            identify([("nested/" + BASE + ".nuspec", specification(BASE))])

    def test_duplicate_metadata_id_is_rejected(self):
        with self.assertRaises(ValueError):
            identify([(BASE + ".nuspec", f"<package><metadata><id>{BASE}</id><id>{BASE}</id></metadata></package>")])


if __name__ == "__main__":
    unittest.main(verbosity=2)
