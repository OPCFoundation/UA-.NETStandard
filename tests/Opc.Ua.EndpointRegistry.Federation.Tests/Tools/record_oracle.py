#!/usr/bin/env python3
"""Record independent OPC 30455 public oracle calls; never derive expected results from C#.

Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
OPC Foundation MIT License 1.00, http://opcfoundation.org/License/MIT/1.00/
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import unittest

sys.dont_write_bytecode = True
parser = argparse.ArgumentParser()
parser.add_argument("--spec-root", required=True, type=Path)
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
root = args.spec_root.resolve()
for directory in (
    root / "extras" / "_common",
    root / "extras" / "schema-registry" / "tools",
    root / "extras" / "endpoint-registry" / "tools",
):
    sys.path.insert(0, str(directory))

import registry_federation as federation
import message_resolution as resolution
import test_message_resolution as tests
from registry_metadata import dump_json

original_resolve = resolution.resolve_message
original_select = federation.select_metadata_reference
calls = []
selections = []
current_test = "deterministic-mutation"


def serial(value):
    if isinstance(value, bytes):
        return value.decode("utf-8")
    if isinstance(value, dict):
        return {name: serial(item) for name, item in value.items()}
    if isinstance(value, (tuple, list)):
        return [serial(item) for item in value]
    return value


def record_select(reference, bindings, **kwargs):
    case = {
        "id": f"select-{len(selections):04d}",
        "test": current_test,
        "reference": serial(copy.deepcopy(reference)),
        "bindings": serial(copy.deepcopy(bindings)),
        "arguments": serial(copy.deepcopy(kwargs)),
    }
    try:
        result = original_select(reference, bindings, **kwargs)
        case["expected"] = serial(result)
        return result
    except Exception as error:
        case["error"] = str(error)
        raise
    finally:
        selections.append(case)


def record_resolve(reference, inputs, **kwargs):
    copied = {name: copy.deepcopy(value) for name, value in kwargs.items() if name != "schema_provider"}
    providers = copied.pop("provider_records")
    schema = kwargs.get("schema_provider")
    schema_calls = []
    case = {
        "id": f"resolve-{len(calls):04d}",
        "test": current_test,
        "reference": reference,
        "inputs": serial(copy.deepcopy(inputs)),
        "localOrigin": serial(copied["local_origin"]),
        "bindings": serial(copied["trusted_bindings"]),
        "providers": [{"key": serial(key), "record": serial(value)} for key, value in providers.items()],
        "hasSchemaProvider": schema is not None,
    }

    def wrapped_schema(message, origin):
        observed = {"origin": serial(origin)}
        try:
            result = schema(message, origin)
            observed["complete"] = True
            return result
        except Exception as error:
            observed["code"] = getattr(error, "code", "E_REFERENCE_INVALID")
            observed["path"] = getattr(error, "path", "Reference")
            observed["detail"] = getattr(error, "detail", str(error))
            raise
        finally:
            schema_calls.append(observed)

    actual = original_resolve(reference, inputs, **{
        **kwargs, **({"schema_provider": wrapped_schema} if schema is not None else {})
    })
    expected = copy.deepcopy(actual)
    if "MaterializedMetadata" in expected:
        expected["MaterializedMetadata"] = dump_json(expected["MaterializedMetadata"]).decode("utf-8")
    case["schemaCalls"] = schema_calls
    case["expected"] = serial(expected)
    calls.append(case)
    return actual


federation.select_metadata_reference = record_select
resolution.select_metadata_reference = record_select
tests.select_metadata_reference = record_select
resolution.resolve_message = record_resolve
tests.resolve_message = record_resolve


class RecorderResult(unittest.TextTestResult):
    def startTest(self, test):
        global current_test
        current_test = test.id()
        super().startTest(test)


suite = unittest.defaultTestLoader.loadTestsFromModule(tests)
result = unittest.TextTestRunner(resultclass=RecorderResult, verbosity=1).run(suite)
if not result.wasSuccessful():
    raise SystemExit("Independent source tests failed; fixtures were not updated.")

# Harvest binding tests as source validation; PubSub implementation/replay belongs to its separate workstream.
import test_message_binding
binding_result = unittest.TextTestRunner(verbosity=1).run(
    unittest.defaultTestLoader.loadTestsFromModule(test_message_binding))
if not binding_result.wasSuccessful():
    raise SystemExit("Independent Message binding tests failed.")

mutations = {
    "node-class": lambda c: c["record"]["Session"]["Target"].update(NodeClass="Variable"),
    "document": lambda c: c["record"]["Session"]["Target"].update(HasDocument=True),
    "max-versions": lambda c: c["record"]["Session"]["Target"].update(MaxVersions=2),
    "missing-target-type": lambda c: c["record"]["Session"]["Target"]["TypeAncestors"].pop(),
    "missing-root-type": lambda c: c["record"]["Session"]["RegistryRoot"]["TypeAncestors"].pop(),
    "application": lambda c: c["record"]["Session"].update(ApplicationUri="urn:example:wrong"),
    "wrong-owner": lambda c: c["record"]["Session"]["Target"]["RegistryNodeId"].update(id="wrong"),
    "wrong-target": lambda c: c["record"]["Session"]["Target"]["NodeId"].update(id="wrong"),
    "wrong-role": lambda c: c["record"]["Session"]["Target"].update(Role="ExactVersion"),
    "wrong-xid": lambda c: c["record"]["Session"]["Target"].update(Xid="/messagegroups/other/messages/m"),
    "wrong-version": lambda c: c["record"]["Session"]["Target"].update(VersionId=""),
    "epoch-zero": lambda c: c["record"].update(Epoch=0),
    "raw-epoch": lambda c: c["metadata"].update(epoch=4),
    "raw-xid": lambda c: c["metadata"].update(xid="/messagegroups/other/messages/m"),
    "raw-version": lambda c: c["metadata"].update(versionid="other"),
    "source-spelling": lambda c: c["metadata"].update(basemessage="urn:example:base"),
}
for name, mutate in mutations.items():
    current_test = "deterministic-mutation:" + name
    case = tests.fixture()
    case["bindings"] = copy.deepcopy(case["bindings"])
    mutate(case)
    case["record"]["RawMetadata"] = dump_json(case["metadata"])
    record_resolve(case["reference"]["Xid"], {"references": [tests.request(case)]},
                   local_origin=case["reference"]["OriginRegistry"], trusted_bindings=case["bindings"],
                   provider_records={tests.key_for(case): case["record"]})

paths = [
    "source/endpoint-registry/spec.md", "source/xregistry/spec.md",
    "extras/_common/registry_federation.py",
    "extras/endpoint-registry/tools/message_resolution.py",
    "extras/endpoint-registry/tools/native_resolution.py",
    "extras/endpoint-registry/tools/test_message_resolution.py",
    "extras/endpoint-registry/tools/test_message_binding.py",
    "extras/endpoint-registry/tools/message_rules.py",
]
document = {
    "format": "OPC30455.FederationOracle/1",
    "provenance": {
        "sourceCommit": subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip(),
        "sha256": {path: hashlib.sha256((root / path).read_bytes()).hexdigest() for path in paths},
        "recordedSourceTests": result.testsRun,
        "recordedBindingTests": binding_result.testsRun,
        "deterministicMutations": len(mutations),
    },
    "resolutionCalls": calls,
    "selectionCalls": selections,
}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Recorded {len(calls)} resolution and {len(selections)} metadata selection calls into {args.output}")
