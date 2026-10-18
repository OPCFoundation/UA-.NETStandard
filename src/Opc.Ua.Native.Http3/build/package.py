# Copyright (c) OPC Foundation. Licensed under the MIT License.
"""Generate an exact-file NuGet manifest from verified production assets; never use globs in NuGet."""

import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET

import verify

ROOT = verify.ROOT
STATIC = (
    "Cargo.toml", "Cargo.lock", "LICENSE.txt", "rustfmt.toml", "ABI.md", "THIRD-PARTY-NOTICES.txt",
    "src/lib.rs", "src/config.rs", "src/error.rs", "src/ffi.rs", "src/send.rs",
    "upstream/Cargo.toml", "patches/manifest.json", "patches/http3-resource-bounds.patch",
    "include/opcua_http3.h", "Build-Native.ps1", "Build-Linux.ps1", "Pack-Native.ps1",
    "Opc.Ua.Native.Http3.csproj", "NugetREADME.md", "global.json",
    "build/public-inputs.json", "build/source-inputs.json", "build/runtime-components.json",
    "build/primary-sources.json", "build/exports.json", "build/verify.py", "build/package.py",
    "build/link-capture.rs", "build/Containerfile.linux", "notices/manifest.json",
)


def entries():
    verify.source()
    result = {}

    def add(relative, destination):
        path = ROOT / relative
        if not path.resolve().is_relative_to(ROOT) or not path.is_file():
            raise ValueError(f"Invalid source path: {relative}")
        if destination in result:
            raise ValueError(f"Duplicate package target: {destination}")
        result[destination] = path

    digest, _ = verify.source_digest()
    for rid, filename in verify.RIDS.items():
        receipt = verify.load(f"artifacts/receipts/{rid}.json")
        asset = f"runtimes/{rid}/native/{filename}"
        data = (ROOT / "artifacts" / asset).read_bytes()
        if receipt["sha256"] != verify.sha(data) or receipt["source_digest"] != digest:
            raise ValueError(f"Stale binary/source receipt: {rid}; rebuild, do not relabel old binaries")
        inspection = verify.pe(data) if rid == "win-x64" else verify.elf(data)
        if inspection["exports"] != sorted(verify.load("build/exports.json")):
            raise ValueError("Unapproved native export set")
        sbom = verify.load(f"artifacts/sbom/{rid}.cdx.json")
        hashes = sbom["components"][0]["hashes"]
        if hashes != [{"alg": "SHA-256", "content": receipt["sha256"]}]:
            raise ValueError(f"SBOM is not bound to the current asset: {rid}")
        add(f"artifacts/{asset}", asset)
        add(f"artifacts/receipts/{rid}.json", f"receipts/{rid}.json")
        add(f"artifacts/sbom/{rid}.cdx.json", f"sbom/{rid}.cdx.json")
    add("LICENSE.txt", "LICENSE.txt")
    add("NugetREADME.md", "NugetREADME.md")
    add("THIRD-PARTY-NOTICES.txt", "THIRD-PARTY-NOTICES.txt")
    add("ABI.md", "docs/ABI.md")
    add("include/opcua_http3.h", "include/opcua_http3.h")
    for name in STATIC:
        add(name, "native-src/" + name)
    for item in verify.load("build/source-inputs.json")["files"]:
        add("upstream/" + item["path"], "native-src/upstream/" + item["path"])
    for item in verify.load("notices/manifest.json")["files"]:
        add(item["path"], item["path"])
        add(item["path"], "native-src/" + item["path"])
    for destination in result:
        if destination.endswith((".pdb", ".exe", ".pfx", ".pem", ".key", ".snk")):
            raise ValueError(f"Forbidden payload: {destination}")
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True)
    parser.add_argument("--configuration", choices=["Release", "Debug"], default="Release")
    args = parser.parse_args()
    files = entries()
    package = ET.Element("package")
    metadata = ET.SubElement(package, "metadata")
    for tag, text in {
        "id": "OPCFoundation.NetStandard.Opc.Ua.Native.Http3" + (".Debug" if args.configuration == "Debug" else ""),
        "version": args.version,
        "authors": "OPC Foundation",
        "description": "Optional native HTTP3/WebTransport draft16 Sans-I/O backend. QUIC/TLS is supplied separately.",
        "requireLicenseAcceptance": "true",
        "readme": "NugetREADME.md",
        "projectUrl": "https://github.com/OPCFoundation/UA-.NETStandard",
        "tags": "OPCFoundation OPC UA native HTTP3 WebTransport",
    }.items():
        ET.SubElement(metadata, tag).text = text
    ET.SubElement(metadata, "license", {"type": "file"}).text = "LICENSE.txt"
    ET.SubElement(metadata, "repository", {
        "type": "git", "url": "https://github.com/OPCFoundation/UA-.NETStandard",
    })
    file_nodes = ET.SubElement(package, "files")
    for destination, source in sorted(files.items()):
        ET.SubElement(file_nodes, "file", {"src": str(source), "target": destination})
    ET.indent(package)
    ET.ElementTree(package).write(
        ROOT / "artifacts" / f"native-http3.{args.configuration}.nuspec", encoding="utf-8", xml_declaration=True)
    verify.write(ROOT / "artifacts" / "package-files.json",
                 {destination: verify.sha(source.read_bytes()) for destination, source in sorted(files.items())})
    print(f"PASS package manifest: {len(files)} individually hash-bound files, no wildcard/native dependency imports.")


if __name__ == "__main__":
    main()
