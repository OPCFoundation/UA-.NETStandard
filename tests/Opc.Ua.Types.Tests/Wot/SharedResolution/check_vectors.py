#!/usr/bin/env python3
# SPDX-License-Identifier: Apache-2.0
"""Validate and execute representative examples of spec sections 1.3/5.4."""

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


def compare_numeric(left, right):
    if len(left) != len(right):
        return -1 if len(left) < len(right) else 1
    return (left > right) - (left < right)


def increment_numeric(value):
    digits = list(value)
    for index in range(len(digits) - 1, -1, -1):
        if digits[index] != "9":
            digits[index] = chr(ord(digits[index]) + 1)
            return "".join(digits)
        digits[index] = "0"
    return "1" + "".join(digits)


@total_ordering
class Version:
    """SemVer precedence: numeric core, release above prerelease, numeric identifiers."""

    def __init__(self, text):
        match = SEMVER.fullmatch(text)
        if match is None:
            raise OutcomeError("invalid-version")
        self.core = tuple(match[i] for i in (1, 2, 3))
        self.pre = match[4].split(".") if match[4] is not None else []

    def __eq__(self, other):
        return self.core == other.core and self.pre == other.pre

    def __lt__(self, other):
        if self.core != other.core:
            for left, right in zip(self.core, other.core):
                if left != right:
                    return compare_numeric(left, right) < 0
        if not self.pre or not other.pre:
            return bool(self.pre) and not other.pre
        for left, right in zip(self.pre, other.pre):
            if left == right:
                continue
            if left.isdigit() and right.isdigit():
                return compare_numeric(left, right) < 0
            if left.isdigit() != right.isdigit():
                return left.isdigit()
            return left < right
        return len(self.pre) < len(other.pre)


TD_REFERENCE_KINDS = {"td-extends", "td-subthing", "td-ref"}


def floor_for(case):
    reference = case["reference"]
    floor = reference.get("minVersion")
    is_td = reference["kind"] in TD_REFERENCE_KINDS
    expected_term = "td:minVersion" if is_td else "tm:minVersion"
    if "floorTerm" in reference and not ("minVersion" in reference and reference["floorTerm"] == expected_term):
        raise OutcomeError("invalid-floor")
    document = case.get("document", {})
    if reference["kind"] == "type" and document:
        links = document["typeLinks"]
        model = document.get("version.model")
        if model is not None and len(links) > 1 and any("minVersion" not in link for link in links):
            raise OutcomeError("ambiguous-floor")
        # The reference is a normalized projection of this exact link.
        matches = [link for link in links if link["target"] == reference["target"]]
        if len(matches) != 1 or matches[0].get("minVersion") != floor:
            raise ValueError("reference must project one unambiguous document type link")
        for link in links:
            if "minVersion" in link:
                validate_floor(link["minVersion"])
        effective = validate_floor(floor) if floor is not None else None
        if model is not None:
            implicit = validate_floor(model)
            if effective is None or effective < implicit:
                effective = implicit
        return effective
    return validate_floor(floor) if floor is not None else None


def parse_version_id(text):
    if not CORE_ID.fullmatch(text):
        raise OutcomeError("invalid-version")
    return Version(text)


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
    resource = matches[0]
    for version in resource["versions"]:
        parse_version_id(version["versionid"])
    return resource


def resolve_local(case):
    """Check a document-local reference against its containing revision."""
    reference = case["reference"]
    if "pin" in case or reference["kind"] not in ("ref", "td-ref"):
        raise ValueError("a local reference is an unpinned tm:ref or td:ref")
    document = case["document"]
    resource = resource_for(case, reference["target"])
    selected = next(
        (v for v in resource["versions"] if v["versionid"] == document["versionid"]),
        None,
    )
    if selected is None:
        raise ValueError("the containing revision must be a stored Version")
    floor = floor_for(case)
    if floor is not None:
        is_td = reference["kind"] == "td-ref"
        authored = document.get("version.instance" if is_td else "version.model")
        if authored is None:
            raise OutcomeError("unsatisfied")
        if WITH_BUILD.fullmatch(authored):
            raise OutcomeError("invalid-version")
        version = parse_version_id(authored)
        if version < floor or (
            is_td
            and compare_numeric(version.core[0], increment_numeric(floor.core[0])) >= 0
        ):
            raise OutcomeError("unsatisfied")
    return {
        "wotid": resource["wotid"],
        "versionid": selected["versionid"],
        "contentdigest": selected["contentdigest"],
        "origin": ORIGIN,
    }


def resolve(case):
    """Execute spec 5.4.2 in its stated order; origin is resolver context."""
    if case["reference"].get("local", False):
        return resolve_local(case)
    resource = resource_for(case, case["reference"]["target"])
    versions = resource["versions"]
    pin = case.get("pin", {})
    if "digest" in pin:
        eligible = [v for v in versions if v["contentdigest"] == pin["digest"]]
        if not eligible:
            raise OutcomeError("pin-mismatch")
        selected = max(eligible, key=lambda v: parse_version_id(v["versionid"]))
    elif "versionid" in pin:
        selected = next((v for v in versions if v["versionid"] == pin["versionid"]), None)
        if selected is None:
            raise OutcomeError("not-found")
    else:
        floor = floor_for(case)
        is_td = case["reference"]["kind"] in TD_REFERENCE_KINDS
        upper = (
            Version(f"{increment_numeric(floor.core[0])}.0.0")
            if is_td and floor is not None
            else None
        )
        if resource["defaultversionsticky"]:
            selected = next((v for v in versions if v["versionid"] == resource["defaultversionid"]), None)
            if selected is None:
                raise OutcomeError("not-found")
            selected_version = parse_version_id(selected["versionid"])
            if floor is not None and (
                selected_version < floor
                or (
                    upper is not None
                    and compare_numeric(selected_version.core[0], upper.core[0]) >= 0
                )
            ):
                raise OutcomeError("unsatisfied")
        else:
            eligible = []
            for version in versions:
                candidate = parse_version_id(version["versionid"])
                if candidate.pre and not case.get("allowPrerelease", False) and not (floor is not None and floor.pre):
                    continue
                if floor is not None and candidate < floor:
                    continue
                if upper is not None and compare_numeric(candidate.core[0], upper.core[0]) >= 0:
                    continue
                eligible.append(version)
            if not eligible:
                raise OutcomeError("unsatisfied")
            selected = max(eligible, key=lambda v: parse_version_id(v["versionid"]))
        selected_version = parse_version_id(selected["versionid"])
        if floor is not None and selected_version < floor:
            raise OutcomeError("unsatisfied")
        if upper is not None and compare_numeric(selected_version.core[0], upper.core[0]) >= 0:
            raise OutcomeError("unsatisfied")
    return {"wotid": resource["wotid"], "versionid": selected["versionid"],
            "contentdigest": selected["contentdigest"], "origin": ORIGIN}


def publish(case):
    """Assign a TM or TD Version id and compute Core's post-publication default."""
    operation = case["publish"]
    resource = resource_for(case, operation["target"])
    supplied = operation.get("versionid")
    if supplied is not None and (not CORE_ID.fullmatch(supplied) or supplied in ("null", "request")):
        raise OutcomeError("invalid-version")
    is_td = operation.get("targetKind", "tm") == "td"
    document_version = operation.get("version.instance" if is_td else "version.model")
    if document_version is not None and WITH_BUILD.fullmatch(document_version):
        raise OutcomeError("invalid-version")
    if document_version is not None and SEMVER.fullmatch(document_version):
        parse_version_id(document_version)
        assigned, origin = document_version, "authored"
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
        default = max([v["versionid"] for v in resource["versions"]] + [assigned],
                      key=parse_version_id)
    return {"assigned": {"versionid": assigned, "versionorigin": origin, "defaultversionid": default}}


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate key: {key}")
        result[key] = value
    return result


def load_vectors():
    cases = json.loads((HERE / "vectors.json").read_text(encoding="utf-8"),
                       object_pairs_hook=_unique_object)
    if not isinstance(cases, dict) or not cases:
        raise ValueError("expected a nonempty vector collection")
    return cases


def check_case(name, case):
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
        raise AssertionError(f"{name}: expected {case['expect']}, got {actual}")


def main():
    cases = load_vectors()
    schema = json.loads((HERE / "schema.json").read_text(encoding="utf-8"))
    try:
        from jsonschema import Draft202012Validator
    except ImportError:
        validator = None
        print("SKIP JSON Schema validation (jsonschema is unavailable)")
    else:
        Draft202012Validator.check_schema(schema)
        validator = Draft202012Validator(schema)
        validator.validate(cases)
        print("PASS JSON Schema definition (validation enabled)")
    for name, case in sorted(cases.items()):
        check_case(name, case)
        print(f"PASS {name}")
    print(f"PASS {len(cases)} vectors")


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, ValueError, KeyError) as error:
        print(f"FAIL {error}", file=sys.stderr)
        sys.exit(1)
