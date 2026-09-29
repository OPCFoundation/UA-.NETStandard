# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
"""Record Endpoint Registry rule fixtures from the normative Python validators."""
from __future__ import annotations

import argparse
from decimal import Decimal
import hashlib
import json
import subprocess
import sys
from pathlib import Path

STACK_ROOT = Path(__file__).resolve().parents[1]
OUTPUT = STACK_ROOT / "tests/Opc.Ua.EndpointRegistry.Tests/Assets/rule-fixtures.json"
VECTORS = [
    "extras/endpoint-registry/tools/vectors/message-metadata.json",
    "extras/endpoint-registry/tools/vectors/metadata.json",
]


def fail(message: str) -> None:
    raise SystemExit("error: " + message)


def git(root: Path, *args: str) -> str:
    result = subprocess.run(["git", "-C", str(root), *args], capture_output=True, text=True, check=False)
    if result.returncode != 0:
        fail("git " + " ".join(args) + " failed: " + result.stderr.strip())
    return result.stdout


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_exact(path: Path):
    return json.loads(path.read_text(encoding="utf-8"), parse_float=Decimal)


def exact_json(value) -> str:
    if value is None:
        return "null"
    if value is True:
        return "true"
    if value is False:
        return "false"
    if isinstance(value, str):
        return json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    if isinstance(value, Decimal):
        return format(value, "f") if value == value.to_integral_value() else str(value)
    if isinstance(value, int):
        return str(value)
    if isinstance(value, list):
        return "[" + ",".join(exact_json(item) for item in value) + "]"
    if isinstance(value, dict):
        return "{" + ",".join(exact_json(str(key)) + ":" + exact_json(item) for key, item in value.items()) + "}"
    raise TypeError(type(value).__name__)


def record_call(case_id: str, function: str, document, call):
    try:
        call(document)
        outcome = {"valid": True}
    except Exception as error:  # ContractError has code/path/detail; unexpected exceptions are preserved.
        code = getattr(error, "code", type(error).__name__)
        path = getattr(error, "path", "")
        outcome = {"valid": False, "code": code, "path": path}
    return {"id": case_id, "function": function, "document": document, **outcome}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--spec-root", required=True)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--allow-uncommitted", action="store_true")
    args = parser.parse_args()
    spec = Path(args.spec_root).resolve()
    if not (spec / "extras/endpoint-registry/tools/message_rules.py").exists():
        fail("--spec-root does not point to the OPC 30450 specification checkout")
    status = git(spec, "status", "--short")
    if status.strip() and not args.allow_uncommitted:
        fail("spec checkout has uncommitted changes; use --allow-uncommitted to record them")
    for relative in ("extras/endpoint-registry/tools", "extras/_common"):
        sys.path.insert(0, str(spec / relative))
    import endpoint_rules  # pylint: disable=import-outside-toplevel
    import media_rules  # pylint: disable=import-outside-toplevel
    import message_rules  # pylint: disable=import-outside-toplevel

    cases = []
    for relative in VECTORS:
        vector = load_exact(spec / relative)
        for index, case in enumerate(vector["cases"]):
            document = case.get("value", case.get("input"))
            kind = case["kind"]
            name = case.get("id", case.get("name", str(index)))
            if kind == "message":
                function = "validate_message"
                call = message_rules.validate_message
            elif kind == "group":
                function = "validate_group"
                call = message_rules.validate_group
            elif kind == "media":
                function = "validate_media"
                call = media_rules.validate_media
            elif kind == "endpoint":
                function = "validate_endpoint"
                call = endpoint_rules.validate_endpoint
            elif kind == "registry" and relative.endswith("message-metadata.json"):
                function = "validate_message_registry"
                call = message_rules.validate_registry
            elif kind == "registry":
                function = "validate_registry"
                call = endpoint_rules.validate_registry
            else:
                fail("unknown vector kind " + kind)
            cases.append(record_call(Path(relative).name + ":" + name, function, document, call))

    header = {
        "format": "EndpointRegistryRuleFixtures/1.0",
        "repository": "OPCF-Members/OPC30450-CloudInitiative",
        "commit": git(spec, "rev-parse", "HEAD").strip(),
        "uncommittedSource": bool(status.strip()),
        "inputs": {relative: sha256(spec / relative) for relative in VECTORS},
        "harvestedPythonTests": False,
        "unsupported": [{"source": "python-unit-test-monkeypatch", "reason": "not recorded by this initial implementation"}],
    }
    output = {"header": header, "cases": cases}
    text = exact_json(output) + "\n"
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_text(encoding="utf-8") != text:
            fail(str(OUTPUT.relative_to(STACK_ROOT)) + " is out of date")
    else:
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT.write_text(text, encoding="utf-8", newline="\n")
    print(f"recorded {len(cases)} rule fixtures")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
