# Copyright (c) OPC Foundation. Licensed under the MIT License.
"""Verify actual PE/ELF exports and produce a path-free native asset receipt."""
import argparse
import hashlib
import json
import struct
from pathlib import Path


def cstring(data, offset):
    return data[offset:data.index(0, offset)].decode("ascii")


def pe_exports(data):
    pe = struct.unpack_from("<I", data, 60)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        raise ValueError("Invalid PE signature")
    machine, sections = struct.unpack_from("<HH", data, pe + 4)
    optional_size = struct.unpack_from("<H", data, pe + 20)[0]
    optional = pe + 24
    if machine != 0x8664 or struct.unpack_from("<H", data, optional)[0] != 0x20b:
        raise ValueError("Only PE x64 is accepted")
    table = optional + optional_size

    def offset(rva):
        for i in range(sections):
            size, address, raw_size, raw = struct.unpack_from("<IIII", data, table + 40 * i + 8)
            if address <= rva < address + max(size, raw_size):
                return raw + rva - address
        raise ValueError(f"Unmapped PE RVA {rva:x}")

    export_rva = struct.unpack_from("<I", data, optional + 112)[0]
    directory = offset(export_rva)
    count = struct.unpack_from("<I", data, directory + 24)[0]
    names = offset(struct.unpack_from("<I", data, directory + 32)[0])
    return {cstring(data, offset(struct.unpack_from("<I", data, names + 4 * i)[0])) for i in range(count)}


def elf_exports(data):
    if data[:6] != b"\x7fELF\x02\x01" or struct.unpack_from("<H", data, 18)[0] != 62:
        raise ValueError("Only ELF little-endian x64 is accepted")
    table = struct.unpack_from("<Q", data, 40)[0]
    size, count = struct.unpack_from("<HH", data, 58)
    sections = [struct.unpack_from("<IIQQQQIIQQ", data, table + i * size) for i in range(count)]
    result = set()
    for section in sections:
        if section[1] != 11:
            continue
        strings = sections[section[6]][4]
        for offset in range(section[4], section[4] + section[5], section[9]):
            name, info, visibility, index, _, _ = struct.unpack_from("<IBBHQQ", data, offset)
            if index != 0 and info >> 4 in (1, 2) and visibility & 3 == 0 and name:
                result.add(cstring(data, strings + name))
    return result


def verify(asset, rid, output):
    root = Path(__file__).resolve().parent.parent
    policy = json.loads((root / "build" / "exports.json").read_text())
    data = asset.read_bytes()
    exports = pe_exports(data) if rid == "win-x64" else elf_exports(data)
    required = set(policy["production_exports"])
    permitted = required | set(policy["go_runtime_exports"])
    if required - exports or exports - permitted:
        raise ValueError(f"Export allowlist mismatch: missing={required - exports}, extra={exports - permitted}")
    sources = {}
    for folder, patterns in (("native", ("*.go", "go.mod", "go.sum", "include/*.h", "include/*.map")),
                             ("build", ("*.py", "*.ps1", "*.json", "*.map"))):
        for pattern in patterns:
            for path in sorted((root / folder).glob(pattern)):
                sources[path.relative_to(root).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    receipt = {
        "schema": 1, "abi": policy["abi"], "rid": rid, "asset": asset.name,
        "operation_revision": policy.get("operation_revision", 1),
        "sha256": hashlib.sha256(data).hexdigest(), "exports": sorted(exports),
        "pins": json.loads((root / "build" / "native-pins.json").read_text()),
        "sources": sources, "flags": ["-buildmode=c-shared", "-trimpath", "-buildvcs=false", "-buildid="],
        "platform_link_policy": "PE exact export allowlist" if rid == "win-x64" else "ELF exact export version script",
        "distribution_status": "unsigned-development; final-product signing, SCA, SBOM and minimum-host gate required"
    }
    output.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    asset.with_name(asset.name + ".sha256").write_text(receipt["sha256"] + "\n", encoding="ascii")
    print(f"PASS {rid}: {len(exports)} reviewed exports, SHA256 {receipt['sha256']}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset", type=Path, required=True)
    parser.add_argument("--rid", choices=["win-x64", "linux-x64"], required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    verify(args.asset, args.rid, args.receipt)
