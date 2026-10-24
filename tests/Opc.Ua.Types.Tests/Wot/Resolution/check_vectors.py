#!/usr/bin/env python3
# SPDX-License-Identifier: Apache-2.0
"""Verify canonical bytes, validate vector shapes, and execute spec sections 1.3/5.4."""

import hashlib
import json
import re
import sys
from functools import total_ordering
from pathlib import Path

HERE = Path(__file__).resolve().parent
ORIGIN = "https://registry.example"
SEMVER_TEXT = (
    r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
    r"(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)"
    r"(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?"
)
SEMVER = re.compile(SEMVER_TEXT, re.ASCII)
WITH_BUILD = re.compile(SEMVER_TEXT + r"\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*", re.ASCII)
GENERATED = re.compile(r"0\.0\.0-[1-9][0-9]*", re.ASCII)
CORE_ID = re.compile(r"[A-Za-z0-9_][A-Za-z0-9_.~:@-]{0,127}", re.ASCII)


class OutcomeError(Exception):
    pass


@total_ordering
class Version:
    """SemVer precedence: numeric core, release above prerelease, numeric identifiers."""

    def __init__(self, text):
        match = SEMVER.fullmatch(text)
        if match is None or len(text) > 128:
            raise OutcomeError("invalid-version")
        self.core = tuple(int(match[i]) for i in (1, 2, 3))
        self.pre = match[4].split(".") if match[4] is not None else []

    def __eq__(self, other):
        return self.core == other.core and self.pre == other.pre

    def __lt__(self, other):
        if self.core != other.core:
            return self.core < other.core
        if not self.pre or not other.pre:
            return bool(self.pre) and not other.pre
        for left, right in zip(self.pre, other.pre):
            if left == right:
                continue
            if left.isdigit() and right.isdigit():
                return int(left) < int(right)
            if left.isdigit() != right.isdigit():
                return left.isdigit()
            return left < right
        return len(self.pre) < len(other.pre)


def floor_for(case):
    reference = case["reference"]
    floor = reference.get("minVersion")
    document = case.get("document", {})
    if reference["kind"] == "type" and document:
        links = document["typeLinks"]
        model = document.get("version.model")
        if model is not None and len(links) > 1:
            if any("minVersion" not in link for link in links):
                raise OutcomeError("ambiguous-floor")
        # The reference is a normalized projection of this exact link.
        matches = [link for link in links if link["target"] == reference["target"]]
        if len(matches) != 1 or matches[0].get("minVersion") != floor:
            raise ValueError("reference must project one unambiguous document type link")
        for link in links:
            if "minVersion" in link:
                validate_floor(link["minVersion"])
        if floor is None and len(links) == 1:
            floor = model
    return validate_floor(floor) if floor is not None else None


def validate_floor(text):
    version = Version(text)
    if GENERATED.fullmatch(text):
        raise OutcomeError("invalid-version")
    return version


def resource_for(case, target):
    matches = [resource for resource in case["registry"] if resource["wotid"] == target]
    if not matches:
        raise OutcomeError("not-found")
    if len(matches) != 1:
        raise ValueError("vectors must scope each wotid to one Resource")
    return matches[0]


def resolve(case):
    """Execute spec 5.4.2 in its stated order; origin is resolver context."""
    resource = resource_for(case, case["reference"]["target"])
    versions = resource["versions"]
    pin = case.get("pin", {})
    if "digest" in pin:
        eligible = [v for v in versions if v["contentdigest"] == pin["digest"]]
        if not eligible:
            raise OutcomeError("pin-mismatch")
        selected = max(eligible, key=lambda v: Version(v["versionid"]))
    elif "versionid" in pin:
        selected = next((v for v in versions if v["versionid"] == pin["versionid"]), None)
        if selected is None:
            raise OutcomeError("not-found")
    else:
        floor = floor_for(case)
        if resource["defaultversionsticky"]:
            selected = next((v for v in versions if v["versionid"] == resource["defaultversionid"]), None)
            if selected is None:
                raise OutcomeError("not-found")
        else:
            eligible = [v for v in versions if not Version(v["versionid"]).pre
                        or case.get("allowPrerelease", False) or (floor is not None and floor.pre)]
            if not eligible:
                raise OutcomeError("unsatisfied")
            selected = max(eligible, key=lambda v: Version(v["versionid"]))
        if floor is not None and Version(selected["versionid"]) < floor:
            raise OutcomeError("unsatisfied")
    return {"wotid": resource["wotid"], "versionid": selected["versionid"],
            "contentdigest": selected["contentdigest"], "origin": ORIGIN}


def publish(case):
    """Assign the TM Version id and compute Core's post-publication default."""
    operation = case["publish"]
    resource = resource_for(case, operation["target"])
    supplied = operation.get("versionid")
    if supplied is not None and (not CORE_ID.fullmatch(supplied) or supplied in ("null", "request")):
        raise OutcomeError("invalid-version")
    model = operation.get("version.model")
    if model is not None and WITH_BUILD.fullmatch(model):
        raise OutcomeError("invalid-version")
    if model is not None and SEMVER.fullmatch(model):
        Version(model)
        assigned, origin = model, "authored"
    else:
        occupied = {v["versionid"].lower() for v in resource["versions"]}
        number = 1
        while f"0.0.0-{number}" in occupied:
            number += 1
        assigned, origin = f"0.0.0-{number}", "generated"
    if supplied is not None and supplied != assigned:
        raise OutcomeError("version-mismatch")
    if resource["defaultversionsticky"]:
        default = resource["defaultversionid"]
    else:
        default = max([v["versionid"] for v in resource["versions"]] + [assigned], key=Version)
    return {"assigned": {"versionid": assigned, "versionorigin": origin, "defaultversionid": default}}


def verify_manifest():
    manifest = HERE / "MANIFEST.sha256"
    raw = manifest.read_bytes()
    if b"\r" in raw or not raw.endswith(b"\n"):
        raise ValueError("manifest must use LF and a final newline")
    entries = {}
    for line in raw.decode("ascii").splitlines():
        match = re.fullmatch(r"([0-9a-f]{64})  ([A-Za-z0-9_.-]+)", line)
        if match is None or match[2] in entries:
            raise ValueError(f"invalid or duplicate manifest entry: {line}")
        entries[match[2]] = match[1]
    files = {p.name: p for p in HERE.iterdir() if p.is_file() and p.name != manifest.name}
    if set(entries) != set(files):
        raise ValueError(f"manifest file set differs: {sorted(set(entries) ^ set(files))}")
    if list(entries) != sorted(entries):
        raise ValueError("manifest entries must be sorted by filename")
    for name, path in files.items():
        content = path.read_bytes()
        if b"\r" in content or not content.endswith(b"\n"):
            raise ValueError(f"{name}: expected LF bytes and a final newline")
        if hashlib.sha256(content).hexdigest() != entries[name]:
            raise ValueError(f"{name}: manifest digest mismatch")
    print(f"PASS manifest ({len(files)} files)")


def main():
    verify_manifest()
    schema = json.loads((HERE / "schema.json").read_text(encoding="utf-8"))
    try:
        from jsonschema import Draft202012Validator
    except ImportError:
        validator = None
        print("SKIP JSON Schema validation (jsonschema is unavailable)")
    else:
        Draft202012Validator.check_schema(schema)
        validator = Draft202012Validator(schema)
        print("PASS JSON Schema definition (validation enabled)")
    paths = sorted(HERE.glob("[0-9][0-9][0-9]-*.json"))
    if not paths:
        raise ValueError("no vectors found")
    for path in paths:
        case = json.loads(path.read_text(encoding="utf-8"))
        if validator is not None:
            validator.validate(case)
        try:
            if "publish" in case:
                actual = publish(case)
            else:
                result = resolve(case)
                if result.pop("origin") != ORIGIN:
                    raise AssertionError("resolver omitted registry origin")
                actual = {"result": result}
        except OutcomeError as error:
            actual = {"error": str(error)}
        if actual != case["expect"]:
            raise AssertionError(f"{path.name}: expected {case['expect']}, got {actual}")
        print(f"PASS {path.name}")
    print(f"PASS {len(paths)} vectors")


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, ValueError, KeyError) as error:
        print(f"FAIL {error}", file=sys.stderr)
        sys.exit(1)
