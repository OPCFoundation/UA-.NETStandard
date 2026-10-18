# Copyright (c) OPC Foundation. Licensed under the MIT License.
"""Fail closed on unexpected package files, native hashes, exports or source receipts."""
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

from verify_native import elf_exports, pe_exports

SOURCE_FILES = {
    "native/bridge.go", "native/credentials.go", "native/endpoints.go", "native/operations.go",
    "native/registry.go", "native/trace.go", "native/go.mod", "native/go.sum",
    "native/include/opcua_quic.h", "native/include/exports.map",
    "build/native-pins.json", "build/exports.json", "build/prepare_overlay.py",
    "build/verify_native.py", "build/Build-Native.ps1", "build/Pack-Native.ps1", "build/verify_package.py",
}

PACKAGE_IDS = {
    "OPCFoundation.NetStandard.Opc.Ua.Native.QuicGo",
    "OPCFoundation.NetStandard.Opc.Ua.Native.QuicGo.Debug",
}


def package_identity(package):
    nuspecs = [name for name in package.namelist() if name.endswith(".nuspec")]
    if len(nuspecs) != 1:
        raise ValueError("Exactly one Release or Debug nuspec is required")
    nuspec = nuspecs[0]
    document = ET.fromstring(package.read(nuspec))
    metadata = document.findall("{*}metadata")
    identifiers = metadata[0].findall("{*}id") if len(metadata) == 1 else []
    if len(identifiers) != 1:
        raise ValueError("Exactly one package metadata ID is required")
    package_id = identifiers[0].text
    if package_id not in PACKAGE_IDS or nuspec != package_id + ".nuspec":
        raise ValueError("Nuspec filename and actual Release/Debug package ID must match")
    return nuspec, package_id


def verify_inputs(assets):
    root = Path(__file__).resolve().parent.parent
    pins = json.loads((root / "build" / "native-pins.json").read_text())
    policy = json.loads((root / "build" / "exports.json").read_text())
    for rid, configuration in pins["rids"].items():
        folder = assets / rid
        receipt = json.loads((folder / "native-receipt.json").read_text())
        asset = folder / configuration["asset"]
        data = asset.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        assert receipt["sha256"] == digest == asset.with_name(asset.name + ".sha256").read_text().strip()
        assert receipt["rid"] == rid and receipt["abi"] == policy["abi"] and receipt["pins"] == pins
        assert receipt["operation_revision"] == policy["operation_revision"]
        assert set(receipt["sources"]) == SOURCE_FILES, "Native build input allowlist differs"
        for source, expected in receipt["sources"].items():
            assert hashlib.sha256((root / source).read_bytes()).hexdigest() == expected, (
                f"Stale build receipt: {source}; rebuild both native assets")
        exports = pe_exports(data) if rid == "win-x64" else elf_exports(data)
        assert sorted(exports) == receipt["exports"]
        assert set(policy["production_exports"]) <= exports
        assert exports <= set(policy["production_exports"] + policy["go_runtime_exports"])
    print("PASS package inputs: both native assets, current source/patch pins and export allowlists")


def verify(path):
    root = Path(__file__).resolve().parent.parent
    with zipfile.ZipFile(path) as package:
        nuspec, package_id = package_identity(package)
        names = set(package.namelist())
        allowed = {
            "_rels/.rels", "[Content_Types].xml", "LICENSE.txt", "NugetREADME.md", "images/logo.jpg",
            nuspec,
            "native/NATIVE_ABI.md", "native/THIRD-PARTY-NOTICES.txt", "native/source/observation.patch",
            "native/receipts/win-x64.json", "native/receipts/linux-x64.json",
        }
        allowed |= {"native/source/" + source + (".txt" if source.endswith(".ps1") else "") for source in SOURCE_FILES}
        allowed |= {"native/notices/" + source.name for source in (root / "notices").glob("*.txt")}
        for tfm in ("net8.0", "net9.0", "net10.0"):
            allowed |= {f"lib/{tfm}/Opc.Ua.Native.QuicGo.{ext}" for ext in ("dll", "xml")}
        for rid, asset in (("win-x64", "opcua_quic_go.dll"), ("linux-x64", "libopcua_quic_go.so")):
            location = f"runtimes/{rid}/native/{asset}"
            allowed |= {location, location + ".sha256"}
            receipt = json.loads(package.read(f"native/receipts/{rid}.json"))
            data = package.read(location)
            digest = hashlib.sha256(data).hexdigest()
            assert receipt["sha256"] == digest == package.read(location + ".sha256").decode().strip()
            assert receipt["rid"] == rid
            exports = pe_exports(data) if rid == "win-x64" else elf_exports(data)
            assert sorted(exports) == receipt["exports"]
            policy = json.loads(package.read("native/source/build/exports.json"))
            assert receipt["abi"] == policy["abi"]
            assert receipt["operation_revision"] == policy["operation_revision"]
            assert set(policy["production_exports"]) <= exports
            assert exports <= set(policy["production_exports"] + policy["go_runtime_exports"])
            for source, expected in receipt["sources"].items():
                packaged = "native/source/" + source + (".txt" if source.endswith(".ps1") else "")
                assert hashlib.sha256(package.read(packaged)).hexdigest() == expected, source
        unexpected = {name for name in names - allowed if not re.fullmatch(
            r"package/services/metadata/core-properties/[a-zA-Z0-9]+\.psmdcp", name)}
        assert not unexpected, f"Package file allowlist violated: {sorted(unexpected)}"
        assert not any(name.endswith((".pdb", ".key", ".pfx")) or "fixture" in name for name in names)
        assert b"Opc.Ua.Core" not in package.read(nuspec)
        assert len({name for name in names if name.endswith((".so", "_go.dll"))}) == 2
        print(f"PASS {path.name}: ID={package_id}; {len(names)} allowlisted files, "
              "two native hashes/exports/source receipts verified")


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--inputs":
        verify_inputs(Path(sys.argv[2]))
    else:
        verify(Path(sys.argv[1]))
