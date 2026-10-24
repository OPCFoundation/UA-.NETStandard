# Copyright (c) OPC Foundation. Licensed under the MIT License.
"""Fail-closed native source, export, RID, dependency and package verification. Stdlib only."""

import argparse
import hashlib
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent
PIN = "99a8c71d396b718a922f5108abe9c453c2671905"
OVERLAY = "ea50f349e4a46bf979d8f1d48190ca832eca3c27f886c1aa48ad09a0707591da"
RIDS = {"win-x64": "opcua_http3.dll", "linux-x64": "libopcua_http3.so"}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def load(relative):
    return json.loads((ROOT / relative).read_text(encoding="utf-8-sig"))


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8", newline="\n")


def source():
    inputs = load("build/source-inputs.json")
    patch = load("patches/manifest.json")
    if inputs["commit"] != PIN or patch["upstream_commit"] != PIN:
        raise ValueError("Unreviewed HTTP3 source pin")
    if inputs["overlay_sha256"] != OVERLAY or patch["overlay_sha256"] != OVERLAY:
        raise ValueError("Unreviewed receive overlay")
    if sha((ROOT / "patches/http3-resource-bounds.patch").read_bytes()) != patch["review_diff_sha256"]:
        raise ValueError("Resource review patch changed")
    expected = {item["path"]: item for item in inputs["files"]}
    actual = {path.relative_to(ROOT / "upstream").as_posix()
              for path in (ROOT / "upstream").rglob("*") if path.is_file()}
    if actual != set(expected) | {"Cargo.toml"}:
        raise ValueError("Upstream source file allowlist changed")
    for name, item in expected.items():
        data = (ROOT / "upstream" / name).read_bytes()
        if len(data) != item["bytes"] or sha(data) != item["sha256"]:
            raise ValueError(f"Upstream source bytes changed: {name}")
    if len(patch["files"]) != 8:
        raise ValueError("Expected exact eight-file overlay")
    for item in patch["files"]:
        if expected[item["path"]]["sha256"] != item["patched_sha256"]:
            raise ValueError("Source snapshot does not match reviewed overlay")
    for item in load("notices/manifest.json")["files"]:
        if sha((ROOT / item["path"]).read_bytes()) != item["sha256"]:
            raise ValueError(f"Notice bytes changed: {item['path']}")
    print(f"PASS source: {len(expected)} upstream files; exact eight-file overlay and notice hashes.")


def c_string(data, offset):
    return data[offset:data.index(0, offset)].decode("ascii")


def pe(data):
    header = struct.unpack_from("<I", data, 0x3C)[0]
    if data[header:header + 4] != b"PE\0\0" or struct.unpack_from("<H", data, header + 4)[0] != 0x8664:
        raise ValueError("Expected win-x64 PE")
    sections = struct.unpack_from("<H", data, header + 6)[0]
    optional_size = struct.unpack_from("<H", data, header + 20)[0]
    optional = header + 24
    if struct.unpack_from("<H", data, optional)[0] != 0x20B:
        raise ValueError("Expected PE32+")
    mapping = []
    for index in range(sections):
        start = optional + optional_size + index * 40
        size, rva, raw_size, raw = struct.unpack_from("<IIII", data, start + 8)
        mapping.append((rva, max(size, raw_size), raw))

    def offset(rva):
        for begin, size, raw in mapping:
            if begin <= rva < begin + size:
                return raw + rva - begin
        raise ValueError("Unmapped PE RVA")

    exports_rva, exports_size, import_rva = struct.unpack_from("<III", data, optional + 112)
    exports_at = offset(exports_rva)
    functions, names, function_table, name_table, ordinal_table = struct.unpack_from("<IIIII", data, exports_at + 20)
    if functions != names:
        raise ValueError("Ordinal-only native exports are forbidden")
    exports = []
    for index in range(names):
        name_rva = struct.unpack_from("<I", data, offset(name_table) + index * 4)[0]
        ordinal = struct.unpack_from("<H", data, offset(ordinal_table) + index * 2)[0]
        function = struct.unpack_from("<I", data, offset(function_table) + ordinal * 4)[0]
        if exports_rva <= function < exports_rva + exports_size:
            raise ValueError("Forwarded native exports are forbidden")
        exports.append(c_string(data, offset(name_rva)))
    imports = []
    if import_rva:
        descriptor = offset(import_rva)
        while any(data[descriptor:descriptor + 20]):
            name = struct.unpack_from("<I", data, descriptor + 12)[0]
            imports.append(c_string(data, offset(name)))
            descriptor += 20
    allowed = {"KERNEL32.dll", "ntdll.dll", "bcryptprimitives.dll", "api-ms-win-core-synch-l1-2-0.dll"}
    if not set(imports) <= allowed:
        raise ValueError(f"Unapproved DLL imports: {set(imports) - allowed}")
    return {"format": "PE32+/AMD64", "exports": sorted(exports), "needed": sorted(imports), "crt": "static"}


def elf(data):
    if data[:6] != b"\x7fELF\x02\x01" or struct.unpack_from("<H", data, 18)[0] != 62:
        raise ValueError("Expected little-endian ELF64 x86-64")
    start = struct.unpack_from("<Q", data, 40)[0]
    width, count, names_index = struct.unpack_from("<HHH", data, 58)
    sections = [struct.unpack_from("<IIQQQQIIQQ", data, start + index * width) for index in range(count)]

    def section_data(section):
        return data[section[4]:section[4] + section[5]]

    names = section_data(sections[names_index])
    by_name = {c_string(names, section[0]): section for section in sections if section[0]}
    symbols = by_name[".dynsym"]
    strings = section_data(sections[symbols[6]])
    exports = []
    for offset in range(symbols[4], symbols[4] + symbols[5], symbols[9]):
        name, info, visibility, index, _, _ = struct.unpack_from("<IBBHQQ", data, offset)
        if name and index and info >> 4 in (1, 2) and visibility & 3 == 0:
            exports.append(c_string(strings, name))
    dynamic = by_name[".dynamic"]
    strings = section_data(sections[dynamic[6]])
    needed = []
    for offset in range(dynamic[4], dynamic[4] + dynamic[5], dynamic[9]):
        tag, value = struct.unpack_from("<qQ", data, offset)
        if tag == 1:
            needed.append(c_string(strings, value))
        if tag in (14, 15, 29):
            raise ValueError("SONAME/RPATH/RUNPATH not approved for this asset")
    allowed = {"libgcc_s.so.1", "libc.so.6", "ld-linux-x86-64.so.2"}
    if not set(needed) <= allowed:
        raise ValueError(f"Unapproved ELF dependencies: {set(needed) - allowed}")
    versions = []
    requirements = by_name.get(".gnu.version_r")
    if requirements:
        strings = section_data(sections[requirements[6]])
        cursor = requirements[4]
        while True:
            _, auxiliary_count, _, auxiliary, following = struct.unpack_from("<HHIII", data, cursor)
            item = cursor + auxiliary
            for _ in range(auxiliary_count):
                _, _, _, name, next_item = struct.unpack_from("<IHHII", data, item)
                versions.append(c_string(strings, name))
                item += next_item
            if not following:
                break
            cursor += following
    glibc = [tuple(map(int, name[6:].split("."))) for name in versions if name.startswith("GLIBC_")]
    floor = max(glibc, default=(0,))
    if floor > (2, 34):
        raise ValueError(f"New glibc floor requires review: {floor}")
    return {"format": "ELF64/x86-64", "exports": sorted(exports), "needed": sorted(needed),
            "symbol_versions": sorted(set(versions)), "observed_glibc_floor": ".".join(map(str, floor)),
            "declared_glibc_floor": "2.34", "minimum_host_execution_validated": False}


def source_digest():
    paths = [ROOT / name for name in ("Cargo.toml", "Cargo.lock", "upstream/Cargo.toml",
                                      "build/source-inputs.json", "build/public-inputs.json")]
    paths.extend((ROOT / "src").glob("*.rs"))
    records = [{"path": path.relative_to(ROOT).as_posix(), "sha256": sha(path.read_bytes())}
               for path in sorted(paths, key=lambda value: value.relative_to(ROOT).as_posix())]
    return sha(json.dumps(records, sort_keys=True, separators=(",", ":")).encode()), records


def binary(rid):
    source()
    path = ROOT / "artifacts" / "runtimes" / rid / "native" / RIDS[rid]
    data = path.read_bytes()
    inspected = pe(data) if rid == "win-x64" else elf(data)
    if inspected["exports"] != sorted(load("build/exports.json")):
        raise ValueError(f"Production export allowlist differs: {inspected['exports']}")
    digest, records = source_digest()
    receipt = {
        "schema_version": 1, "rid": rid, "asset": path.relative_to(ROOT / "artifacts").as_posix(),
        "sha256": sha(data), "bytes": len(data), "abi": 1, "operation_revision": 3,
        "source_digest": digest, "source_inputs": records, "inspection": inspected,
        "public_tool_inputs": "build/public-inputs.json", "distribution_approved": False,
        "remaining_release_gates": ["signing", "final-hash product SCA/VEX", "advertised minimum-host execution"],
    }
    write(ROOT / "artifacts" / "receipts" / f"{rid}.json", receipt)
    sbom(rid, receipt)
    print(f"PASS binary: {rid}; nine production-only exports; {receipt['sha256']}")
    return receipt


def sbom(rid, receipt):
    runtime = load("build/runtime-components.json")
    components = [
        {"type": "file", "name": RIDS[rid], "bom-ref": f"native:{rid}",
         "hashes": [{"alg": "SHA-256", "content": receipt["sha256"]}]},
        {"type": "library", "name": "shiguredo_http3", "version": "2026.1.0-canary.12",
         "bom-ref": "http3", "purl": "pkg:cargo/shiguredo_http3@2026.1.0-canary.12",
         "licenses": [{"license": {"id": "Apache-2.0"}}],
         "properties": [{"name": "upstream-commit", "value": PIN}, {"name": "receive-overlay-sha256", "value": OVERLAY}]},
        {"type": "library", "name": "Rust std/runtime", "version": "1.97.0", "bom-ref": "rust-runtime",
         "licenses": [{"expression": "(MIT OR Apache-2.0) AND Unicode-3.0"}],
         "properties": [{"name": "source-commit", "value": runtime["rust_commit"]}]},
        {"type": "library", "name": "compiler_builtins", "version": runtime["compiler_builtins"]["version"],
         "bom-ref": "compiler-builtins", "licenses": [{"expression": runtime["compiler_builtins"]["license"]}]},
    ]
    for item in runtime["stdlib_registry_components"]:
        components.append({
            "type": "library", "name": item["name"], "version": item["version"],
            "bom-ref": "stdlib:" + item["name"], "purl": f"pkg:cargo/{item['name']}@{item['version']}",
            "licenses": [{"expression": item["license"]}], "hashes": [{"alg": "SHA-256", "content": item["sha256"]}],
            "properties": [{"name": "selection", "value": "audited stdlib attribution superset; not per-RID retention proof"}],
        })
    if rid == "win-x64":
        components.append({"type": "library", "name": "Microsoft static CRT", "version": "14.51.36231",
            "bom-ref": "platform-runtime", "licenses": [{"license": {"name": "Licensed VS2026 Distributable Code and SDK10.0.26100.0 terms"}}]})
    else:
        components.extend([
            {"type": "library", "name": "GNU libgcc/startup", "bom-ref": "platform-runtime",
             "licenses": [{"expression": "GPL-3.0-or-later WITH GCC-exception-3.1"}]},
            {"type": "operating-system", "name": "glibc (OS-supplied, not bundled)", "version": ">=2.34",
             "bom-ref": "glibc", "licenses": [{"expression": "LGPL-2.1-or-later"}]},
        ])
    write(ROOT / "artifacts" / "sbom" / f"{rid}.cdx.json", {
        "bomFormat": "CycloneDX", "specVersion": "1.6", "version": 1,
        "metadata": {"component": {"type": "library", "name": "Opc.Ua.Native.Http3", "version": "0.1.0"},
                     "properties": [{"name": "distribution-approval", "value": "pending downstream release evidence"},
                                    {"name": "source-digest", "value": receipt["source_digest"]}]},
        "components": components,
        "dependencies": [{"ref": f"native:{rid}", "dependsOn": [item["bom-ref"] for item in components[1:]]}],
    })


def package(path):
    allowed = load("artifacts/package-files.json")
    with zipfile.ZipFile(path) as archive:
        names = [item.filename for item in archive.infolist() if not item.is_dir()]
        if len(names) != len(set(names)):
            raise ValueError("Duplicate package entries")
        package_ids = {
            "OPCFoundation.NetStandard.Opc.Ua.Native.Http3",
            "OPCFoundation.NetStandard.Opc.Ua.Native.Http3.Debug",
        }
        manifests = [name for name in names if name.endswith(".nuspec")]
        if len(manifests) != 1 or manifests[0] not in {name + ".nuspec" for name in package_ids}:
            raise ValueError("Expected exactly one approved package manifest")
        manifest = manifests[0]
        metadata = ET.fromstring(archive.read(manifest))
        if metadata.findtext("{*}metadata/{*}id") != manifest.removesuffix(".nuspec"):
            raise ValueError("Package identity does not match its manifest")
        for name in names:
            if name in allowed:
                if sha(archive.read(name)) != allowed[name]:
                    raise ValueError(f"Package hash mismatch: {name}")
            elif name == "[Content_Types].xml" or name == "_rels/.rels" or (
                name.startswith("package/services/metadata/core-properties/") and name.endswith(".psmdcp")
            ) or name == manifest:
                continue
            else:
                raise ValueError(f"Package file not explicitly allowed: {name}")
        if not set(allowed) <= set(names):
            raise ValueError(f"Package files missing: {set(allowed) - set(names)}")
        for rid, filename in RIDS.items():
            data = archive.read(f"runtimes/{rid}/native/{filename}")
            inspected = pe(data) if rid == "win-x64" else elf(data)
            if inspected["exports"] != sorted(load("build/exports.json")):
                raise ValueError("Packaged export set changed")
    print(f"PASS package: {len(allowed)} exact files, only win-x64/linux-x64 production assets.")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=["source", "binary", "package"])
    parser.add_argument("--rid", choices=RIDS)
    parser.add_argument("--package", type=Path)
    args = parser.parse_args()
    if args.action == "source":
        source()
    elif args.action == "binary" and args.rid:
        binary(args.rid)
    elif args.action == "package" and args.package:
        package(args.package)
    else:
        parser.error("binary needs --rid; package needs --package")


if __name__ == "__main__":
    main()
