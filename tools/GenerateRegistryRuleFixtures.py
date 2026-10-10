# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
"""Record Endpoint Registry rule fixtures from the normative Python validators.

The recorder is development-time only. It imports the specification checkout read-only, records vector
cases, harvests in-process unittest calls through wrapping proxies, then asks Python for the expected
outcome of deterministic single-step mutations of valid validation inputs.
"""
from __future__ import annotations

import argparse
import copy
from decimal import Decimal
import functools
import hashlib
import importlib
import json
import subprocess
import sys
import types
import unittest
from pathlib import Path

STACK_ROOT = Path(__file__).resolve().parents[1]
OUTPUT = STACK_ROOT / "tests/Opc.Ua.EndpointRegistry.Tests/Assets/rule-fixtures.json"
VECTORS = [
    "extras/endpoint-registry/tools/vectors/message-metadata.json",
    "extras/endpoint-registry/tools/vectors/metadata.json",
]
TEST_MODULES = ["test_message_rules", "test_endpoint_rules", "test_media_rules", "test_extension_contract"]
WRAPPED = {
    "message_rules": ["validate_message", "validate_group", "validate_container", "validate_registry", "materialize_message", "overlay_message"],
    "endpoint_rules": ["validate_endpoint", "validate_registry", "effective_options", "project_options"],
    "media_rules": ["validate_media", "media_options", "native_configuration_requirements"],
    "extension_contract": ["validate_definition", "project_definition", "compose_extensions", "configuration_requirements"],
}
REPLAYABLE = {
    "message_rules.validate_message", "message_rules.validate_group", "message_rules.validate_container", "message_rules.validate_registry",
    "endpoint_rules.validate_endpoint", "endpoint_rules.validate_registry", "media_rules.validate_media",
}
VALIDATION = set(REPLAYABLE)
MAX_MUTATIONS_PER_DOCUMENT = 160
MAX_MUTATIONS_TOTAL = 5000


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
    if isinstance(value, int):
        return str(value)
    if isinstance(value, Decimal):
        return format(value, "f") if value == value.to_integral_value() else str(value)
    if isinstance(value, list):
        return "[" + ",".join(exact_json(item) for item in value) + "]"
    if isinstance(value, dict):
        return "{" + ",".join(exact_json(str(key)) + ":" + exact_json(item) for key, item in value.items()) + "}"
    raise TypeError(type(value).__name__)


def serializable(value):
    if value is None or type(value) in (bool, str, int, Decimal):
        return value
    if type(value) is float:
        return Decimal(repr(value))
    if type(value) is bytes:
        return {"$bytes": value.hex()}
    if type(value) is list or type(value) is tuple:
        return [serializable(item) for item in value]
    if type(value) is dict:
        result = {}
        for key, item in value.items():
            if type(key) is not str:
                raise TypeError("non-string key")
            result[key] = serializable(item)
        return result
    raise TypeError(type(value).__name__)


def canon(value) -> str:
    return exact_json(serializable(value))


def outcome_of(call, args, kwargs):
    try:
        result = call(*copy.deepcopy(args), **copy.deepcopy(kwargs))
        try:
            return {"valid": True, "return": serializable(result)}
        except TypeError as exc:
            return {"valid": True, "returnUnsupported": type(result).__name__, "skipReplay": True, "skipReason": "non-serializable return: " + str(exc)}
    except Exception as error:  # ContractError-like objects carry code/path/detail.
        code = getattr(error, "code", type(error).__name__)
        path = getattr(error, "path", "")
        detail = getattr(error, "detail", str(error))
        outcome = {"valid": False, "code": str(code), "path": str(path), "detail": str(detail)}
        if not hasattr(error, "code"):
            outcome["skipReplay"] = True
            outcome["skipReason"] = "non-ContractError exception from Python: " + type(error).__name__
        return outcome


def make_case(case_id, source, function, args, kwargs, outcome, replay=True):
    item = {
        "id": case_id,
        "source": source,
        "function": function,
        "args": serializable(args),
        "kwargs": serializable(kwargs),
        **outcome,
    }
    if item["args"] and isinstance(item["args"][0], dict):
        item["document"] = item["args"][0]
    if not replay or function not in REPLAYABLE or outcome.get("skipReplay"):
        item["skipReplay"] = True
        item.setdefault("skipReason", "no C# replay implementation for " + function)
    return item


def vector_cases(spec: Path, modules: dict[str, types.ModuleType]):
    cases = []
    for relative in VECTORS:
        vector = load_exact(spec / relative)
        for index, case in enumerate(vector["cases"]):
            document = case.get("value", case.get("input"))
            kind = case["kind"]
            name = case.get("id", case.get("name", str(index)))
            if kind == "message":
                function = "message_rules.validate_message"
            elif kind == "group":
                function = "message_rules.validate_group"
            elif kind == "media":
                function = "media_rules.validate_media"
            elif kind == "endpoint":
                function = "endpoint_rules.validate_endpoint"
            elif kind == "registry" and relative.endswith("message-metadata.json"):
                function = "message_rules.validate_registry"
            elif kind == "registry":
                function = "endpoint_rules.validate_registry"
            else:
                fail("unknown vector kind " + kind)
            module_name, func_name = function.split(".")
            outcome = outcome_of(getattr(modules[module_name], func_name), [document], {})
            cases.append(make_case(Path(relative).name + ":" + name, "vector", function, [document], {}, outcome))
    return cases


def install_wrappers(modules):
    originals = {}
    original_ids = {}
    records = []
    skipped = {}

    def record_skip(reason):
        skipped[reason] = skipped.get(reason, 0) + 1

    for module_name, names in WRAPPED.items():
        module = modules[module_name]
        for name in names:
            if not hasattr(module, name):
                continue
            original = getattr(module, name)
            function = module_name + "." + name
            originals[original] = function
            original_ids[id(original)] = function

            @functools.wraps(original)
            def wrapper(*args, __original=original, __function=function, **kwargs):
                try:
                    s_args = serializable(copy.deepcopy(args))
                    s_kwargs = serializable(copy.deepcopy(kwargs))
                except TypeError as exc:
                    record_skip(__function + ": non-serializable input: " + str(exc))
                    return __original(*args, **kwargs)
                try:
                    result = __original(*args, **kwargs)
                except Exception as error:
                    outcome = {"valid": False, "code": str(getattr(error, "code", type(error).__name__)),
                               "path": str(getattr(error, "path", "")), "detail": str(getattr(error, "detail", str(error)))}
                    if not hasattr(error, "code"):
                        outcome["skipReplay"] = True
                        outcome["skipReason"] = "non-ContractError exception from Python: " + type(error).__name__
                    records.append(make_case("harvest:" + __function + ":" + str(len(records)), "harvest", __function, s_args, s_kwargs, outcome))
                    raise
                try:
                    ret = serializable(copy.deepcopy(result))
                    outcome = {"valid": True, "return": ret}
                except TypeError as exc:
                    outcome = {"valid": True, "returnUnsupported": type(result).__name__, "skipReplay": True,
                               "skipReason": "non-serializable return: " + str(exc)}
                records.append(make_case("harvest:" + __function + ":" + str(len(records)), "harvest", __function, s_args, s_kwargs, outcome))
                return result

            setattr(module, name, wrapper)
    return originals, original_ids, records, skipped


def import_and_run_tests(spec: Path, modules):
    originals, original_ids, records, skipped = install_wrappers(modules)
    loaded = []
    for name in TEST_MODULES:
        try:
            loaded.append(importlib.import_module(name))
        except ModuleNotFoundError:
            skipped["missing test module: " + name] = skipped.get("missing test module: " + name, 0) + 1
    for test_module in loaded:
        for attr, value in list(vars(test_module).items()):
            if id(value) in original_ids:
                module_name, func_name = original_ids[id(value)].split(".")
                setattr(test_module, attr, getattr(modules[module_name], func_name))
    suite = unittest.TestSuite(unittest.defaultTestLoader.loadTestsFromModule(module) for module in loaded)
    result = unittest.TextTestRunner(stream=sys.stdout, verbosity=1).run(suite)
    if not result.wasSuccessful():
        fail("spec Python unit tests failed while harvesting")
    return records, skipped


def dedupe(cases):
    result = []
    seen = set()
    for case in cases:
        key = canon({k: v for k, v in case.items() if k not in ("id", "detail")})
        if key in seen:
            continue
        seen.add(key)
        case = copy.deepcopy(case)
        case["id"] = f"{case['source']}:{len(result):05d}"
        result.append(case)
    return result


def path_items(value, prefix=()):
    if isinstance(value, dict):
        yield prefix, value
        for key in sorted(value):
            yield from path_items(value[key], prefix + (key,))
    elif isinstance(value, list):
        yield prefix, value
        for index, item in enumerate(value):
            yield from path_items(item, prefix + (index,))
    else:
        yield prefix, value


def set_at(value, path, replacement, delete=False):
    result = copy.deepcopy(value)
    if not path:
        return replacement
    parent = result
    for part in path[:-1]:
        parent = parent[part]
    leaf = path[-1]
    if delete:
        if isinstance(parent, dict):
            parent.pop(leaf, None)
        elif isinstance(parent, list):
            parent.pop(leaf)
    else:
        parent[leaf] = copy.deepcopy(replacement)
    return result


def scalar_replacements(path, current):
    values = ["", "x", 0, -1, Decimal("1.5"), True, None, {}, []]
    name = str(path[-1]).lower() if path else ""
    if isinstance(current, str) and any(token in name for token in ("uri", "url", "self", "documentation", "endpoint", "address")):
        values += ["http://u:p@h/", "relative/path", "mqtt://h:99999"]
    if name in ("protocol", "envelope", "dataschemaformat"):
        values += [current.lower() if isinstance(current, str) else "mqtt", "Vendor/9", "MQTT", "CloudEvents/1.0"]
    return values


def mutations_for_document(document):
    emitted = 0
    for path, value in path_items(document):
        if emitted >= MAX_MUTATIONS_PER_DOCUMENT:
            return
        if path and isinstance(path[-1], str):
            yield "delete/" + "/".join(map(str, path)), set_at(document, path, None, delete=True)
            emitted += 1
            if emitted >= MAX_MUTATIONS_PER_DOCUMENT:
                return
        if not isinstance(value, (dict, list)):
            for replacement in scalar_replacements(path, value):
                yield "replace/" + "/".join(map(str, path)) + "/" + type(replacement).__name__, set_at(document, path, replacement)
                emitted += 1
                if emitted >= MAX_MUTATIONS_PER_DOCUMENT:
                    return
        elif isinstance(value, dict):
            mutated = set_at(document, path + ("x-extra",), "x") if path else {**copy.deepcopy(document), "x-extra": "x"}
            yield "extra/" + "/".join(map(str, path)), mutated
            emitted += 1
        elif isinstance(value, list) and value:
            mutated = copy.deepcopy(document)
            target = mutated
            for part in path:
                target = target[part]
            target.append(copy.deepcopy(target[-1]))
            yield "duplicate/" + "/".join(map(str, path)), mutated
            emitted += 1


def mutation_cases(base_cases, modules):
    cases = []
    seen_inputs = set()
    for base in base_cases:
        if len(cases) >= MAX_MUTATIONS_TOTAL:
            break
        if base.get("skipReplay") or not base.get("valid") or base["function"] not in VALIDATION:
            continue
        if not base.get("args") or not isinstance(base["args"][0], dict):
            continue
        key = canon({"function": base["function"], "args": base["args"], "kwargs": base.get("kwargs", {})})
        if key in seen_inputs:
            continue
        seen_inputs.add(key)
        module_name, func_name = base["function"].split(".")
        call = getattr(modules[module_name], func_name)
        for mut_id, document in mutations_for_document(base["args"][0]):
            if len(cases) >= MAX_MUTATIONS_TOTAL:
                break
            args = [document] + copy.deepcopy(base["args"][1:])
            kwargs = copy.deepcopy(base.get("kwargs", {}))
            outcome = outcome_of(call, args, kwargs)
            cases.append(make_case("mutation:" + mut_id, "mutation", base["function"], args, kwargs, outcome))
    return cases


def summarize(cases):
    counts = {}
    for case in cases:
        source = case["source"]
        function = case["function"]
        counts.setdefault(source, {})[function] = counts.setdefault(source, {}).get(function, 0) + 1
    return counts


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
    sys.dont_write_bytecode = True
    modules = {name: importlib.import_module(name) for name in WRAPPED}
    vectors = vector_cases(spec, modules)
    harvested, skipped = import_and_run_tests(spec, modules)
    base = dedupe(vectors + harvested)
    mutations = dedupe(mutation_cases(base, modules))
    cases = dedupe(base + mutations)
    header = {
        "format": "EndpointRegistryRuleFixtures/2.0",
        "repository": "OPCF-Members/OPC30450-CloudInitiative",
        "commit": git(spec, "rev-parse", "HEAD").strip(),
        "uncommittedSource": bool(status.strip()),
        "inputs": {relative: sha256(spec / relative) for relative in VECTORS},
        "counts": summarize(cases),
        "skippedInputs": skipped,
        "mutationLimit": {"perDocument": MAX_MUTATIONS_PER_DOCUMENT, "total": MAX_MUTATIONS_TOTAL},
    }
    output = {"header": header, "cases": cases}
    text = exact_json(output) + "\n"
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_text(encoding="utf-8") != text:
            fail(str(OUTPUT.relative_to(STACK_ROOT)) + " is out of date")
    else:
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT.write_text(text, encoding="utf-8", newline="\n")
    replay = sum(1 for case in cases if not case.get("skipReplay"))
    print(f"recorded {len(cases)} rule fixtures ({replay} replayable); skipped inputs: {sum(skipped.values())}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
