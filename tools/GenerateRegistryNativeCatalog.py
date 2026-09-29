# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
# See http://opcfoundation.org/License/MIT/1.00/
"""Export the OPC 30450 Endpoint Registry native catalog as static C# mapping tables.

This is an explicit development-time generator. Normal stack builds use the checked-in output and
need neither Python nor the specification checkout. The generator imports the specification's own
catalog (extras/endpoint-registry/tools/native_catalog.py), so every field, source member, base type,
selector family and map shape comes from the reference implementation instead of hand-written tables.

Usage (from the stack root):
    python tools/GenerateRegistryNativeCatalog.py --spec-root <specification checkout>
    python tools/GenerateRegistryNativeCatalog.py --spec-root <checkout> --check
A checkout with uncommitted model, extras or source changes is refused unless --allow-uncommitted is
given; the outputs then carry an explicit "uncommittedSource": true marking.
"""
from __future__ import annotations

import argparse
import csv
from decimal import Decimal
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path

STACK_ROOT = Path(__file__).resolve().parents[1]
CATALOG_OUTPUT = Path("src/Opc.Ua.EndpointRegistry/EndpointRegistryNativeCatalog.g.cs")
COVERAGE_OUTPUT = Path("tests/Opc.Ua.EndpointRegistry.Tests/EndpointRegistryNativeCoverage.g.cs")
FIXTURE_OUTPUT = Path("tests/Opc.Ua.EndpointRegistry.Tests/Assets/native-fixtures.json")
PROVENANCE_OUTPUT = Path("tools/registry-native-catalog.json")
PINNED_NODE_IDS = Path("src/Opc.Ua.EndpointRegistry/Model/Opc.Ua.EndpointRegistry.NodeIds.csv")
PINNED_NODESET = Path("src/Opc.Ua.EndpointRegistry/Model/Opc.Ua.EndpointRegistry.NodeSet2.xml")
SPEC_COVERAGE = Path("extras/endpoint-registry/models/native-field-coverage.json")
SPEC_MODEL = Path("model/Opc.Ua.EndpointRegistry.NodeSet2.xml")
REPOSITORY = "OPCF-Members/OPC30450-CloudInitiative"
# Specification example documents and representation vectors replayed through the reference projection.
EXAMPLES = "extras/endpoint-registry/examples/"
VECTORS = "extras/endpoint-registry/tools/vectors/"
VECTOR_TYPES = {"endpoint": "EndpointDataType", "message": "MessageDefinitionDataType",
                "group": "MessageGroupDataType", "registry": "EndpointRegistryDocumentDataType"}

XREG = "http://opcfoundation.org/UA/xRegistry/"
SCHEMA = "http://opcfoundation.org/UA/SchemaRegistry/"
ENDPOINT = "http://opcfoundation.org/UA/EndpointRegistry/"
NAMESPACES = {XREG: "Opc.Ua.XRegistry", SCHEMA: "Opc.Ua.SchemaRegistry", ENDPOINT: "Opc.Ua.EndpointRegistry"}
# Value-family and record base types that the stack mapping engine declares itself.
BUILT_IN = frozenset((
    "RegistryValueDataType", "RegistryNullValueDataType", "RegistryBooleanValueDataType",
    "RegistryStringValueDataType", "RegistryNumberValueDataType", "RegistryArrayValueDataType",
    "RegistryObjectValueDataType", "RegistryMemberDataType", "RegistryRecordDataType"))
RECORD_CORE_TYPES = frozenset(("String", "Boolean"))
# Non-ASCII characters whose Python str.upper()/str.casefold() is pure ASCII. The stack comparison
# (RegistryNativeCatalog) applies exactly these mappings, which is sufficient because every selector
# key is ASCII. The generator refuses a Python Unicode database that disagrees.
UPPER_TO_ASCII = {0xDF: "SS", 0x131: "I", 0x17F: "S", 0xFB00: "FF", 0xFB01: "FI", 0xFB02: "FL",
                  0xFB03: "FFI", 0xFB04: "FFL", 0xFB05: "ST", 0xFB06: "ST"}
FOLD_TO_ASCII = {0xDF: "ss", 0x17F: "s", 0x1E9E: "ss", 0x212A: "k", 0xFB00: "ff", 0xFB01: "fi",
                 0xFB02: "fl", 0xFB03: "ffi", 0xFB04: "ffl", 0xFB05: "st", 0xFB06: "st"}


def fail(message):
    raise SystemExit("error: " + message)


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(root, *args):
    result = subprocess.run(["git", "-C", str(root), *args], capture_output=True, text=True, check=False)
    if result.returncode != 0:
        fail("git " + " ".join(args) + " failed: " + result.stderr.strip())
    return result.stdout


def check_unicode_tables():
    upper = {cp: chr(cp).upper() for cp in range(0x80, 0x110000)
             if chr(cp).upper() != chr(cp) and chr(cp).upper().isascii()}
    fold = {cp: chr(cp).casefold() for cp in range(0x80, 0x110000)
            if chr(cp).casefold() != chr(cp) and chr(cp).casefold().isascii()}
    if upper != UPPER_TO_ASCII or fold != FOLD_TO_ASCII:
        fail("the Python Unicode database maps other non-ASCII characters to ASCII; review "
             "RegistryNativeCatalog selector comparison before regenerating")


def load_catalog(spec_root):
    for relative in ("extras/endpoint-registry/tools", "extras/_common", "extras/schema-registry/tools"):
        sys.path.insert(0, str(spec_root / relative))
    sys.dont_write_bytecode = True
    from native_catalog import native_catalog  # noqa: E402  pylint: disable=import-outside-toplevel
    from registry_model import canonical_selector  # noqa: E402  pylint: disable=import-outside-toplevel
    catalog = native_catalog()
    modules = {}
    for module in list(sys.modules.values()):
        location = getattr(module, "__file__", None)
        if not location:
            continue
        path = Path(location).resolve()
        try:
            relative = path.relative_to(spec_root)
        except ValueError:
            continue
        modules[relative.as_posix()] = sha256(path)
    return catalog, canonical_selector, dict(sorted(modules.items()))


def comparison(selector):
    if selector in ("protocol", "envelope"):
        return "UpperCase"
    return "CaseFold" if selector == "dataschemaformat" else "Ordinal"


def aliases(selector, keys, canonical_selector):
    """Protocol shorthands (for example MQTT) that the specification canonicalizes to a declared key."""
    if selector != "protocol":
        return []
    result = {}
    for key in keys:
        if canonical_selector(key, selector) != key.upper():
            fail("protocol selector canonicalization changed for key " + key)
        prefix = key.split("/", 1)[0]
        target = canonical_selector(prefix, selector)
        if target != prefix.upper():
            result[prefix.upper()] = target
    return sorted(result.items())


def cs(text):
    escaped = []
    for ch in text:
        if ch in ('"', "\\"):
            escaped.append("\\" + ch)
        elif " " <= ch <= "~":
            escaped.append(ch)
        else:
            escaped.append("\\u%04x" % ord(ch))
    return '"' + "".join(escaped) + '"'


def shape_code(shape):
    if not shape:
        return "null"
    code = "new RegistrySourceShape(" + cs(shape.get("type", "any")) + ")"
    extra = []
    if "item" in shape:
        extra.append("Item = " + shape_code(shape["item"]))
    if shape.get("attributes"):
        members = ", ".join("new RegistrySourceShapeMember(" + cs(name) + ", " + shape_code(item) + ")"
                            for name, item in shape["attributes"].items())
        extra.append("Attributes = [" + members + "]")
    return code + (" { " + ", ".join(extra) + " }" if extra else "")


def type_id(catalog, name):
    return "global::" + NAMESPACES[catalog.types[name].namespace] + ".DataTypeIds." + name


def factory(catalog, name):
    if catalog.types[name].abstract:
        return "null"
    return "static () => new global::" + NAMESPACES[catalog.types[name].namespace] + "." + name + "()"


def exported_types(catalog):
    """Endpoint records, maps and map entries plus the schema-content family used by DataSchema."""
    maps = {name for name in catalog.maps}
    entries = {entry for entry, _ in catalog.maps.values()}
    result = []
    for native in catalog.types.values():
        if native.namespace != ENDPOINT:
            continue
        if catalog.is_subtype(native.name, "RegistryRecordDataType") or native.name in maps | entries:
            result.append(native.name)
    schema_root = "SchemaContentDataType"
    family = catalog.choices[schema_root]
    adapters = [schema_root, *family[1].values(), family[2]]
    # Other published subtypes, such as the IPC Arrow view, reach the adapter instead of an unknown-type error.
    adapters += sorted(name for name in catalog.types
                       if name not in adapters and catalog.is_subtype(name, schema_root))
    return result, adapters


def check_types(catalog, names):
    for name in names:
        native = catalog.types[name]
        if native.name in BUILT_IN:
            fail("an exported type redeclares a built-in engine type: " + name)
        for field in native.fields:
            if field.datatype in catalog.types or field.datatype in RECORD_CORE_TYPES:
                continue
            fail(f"{name}.{field.name} uses an unsupported record field DataType {field.datatype}")


def check_pinned_model(catalog, names, stack_root):
    pinned = {}
    with (stack_root / PINNED_NODE_IDS).open(encoding="utf-8", newline="") as stream:
        for row in csv.reader(stream):
            if len(row) == 3 and row[2] == "DataType":
                pinned[row[0]] = int(row[1])
    for name in names:
        native = catalog.types[name]
        if native.namespace == ENDPOINT and pinned.get(name) != native.identifier:
            fail(f"the pinned stack model does not allocate {name} as i={native.identifier}")


def coverage_rows(catalog, spec_root):
    live = [row for row in catalog.coverage if row["owner"] == ENDPOINT]
    committed = json.loads((spec_root / SPEC_COVERAGE).read_text(encoding="utf-8"))
    committed = [row for row in committed if row["owner"] == ENDPOINT]
    return live, live == committed


def emit_catalog(catalog, names, adapter_types, canonical_selector, provenance):
    lines = [
        "// <auto-generated>",
        "// Generated by tools/GenerateRegistryNativeCatalog.py. Do not edit; regenerate from the pinned",
        "// specification catalog instead. Provenance (also recorded in tools/registry-native-catalog.json):",
        f"//   repository: {REPOSITORY}",
        f"//   commit: {provenance['commit']}",
        f"//   uncommittedSource: {str(provenance['uncommittedSource']).lower()}",
        f"//   catalog: {provenance['catalogSha256']}",
        f"//   Endpoint coverage rows: {provenance['coverageRows']} (committed ledger matches: "
        f"{str(provenance['coverageMatchesCommittedLedger']).lower()})",
        "// </auto-generated>",
        "",
        "using Opc.Ua.XRegistry;",
        "",
        "namespace Opc.Ua.EndpointRegistry",
        "{",
        "    public static partial class EndpointRegistryNativeCatalog",
        "    {",
        "        private static void AddGeneratedTypes(RegistryNativeCatalog.Builder builder)",
        "        {",
    ]
    for name in adapter_types:
        native = catalog.types[name]
        lines.append(f"            builder.AddType(Adapter({cs(name)}, {type_id(catalog, name)}, "
                     f"{cs(native.base)}, {str(native.abstract).lower()}, {factory(catalog, name)}));")
    for name in names:
        native = catalog.types[name]
        lines.append(f"            builder.AddType(Native({cs(name)}, {type_id(catalog, name)}, {cs(native.base)}, "
                     f"{str(native.abstract).lower()}, {factory(catalog, name)}")
        for field in native.fields:
            shape = catalog.shapes.get((name, field.name))
            lines.append(f"                , Field({cs(field.name)}, {cs(field.datatype)}, {cs(field.source)}, "
                         f"{str(field.array).lower()}, {str(field.subtypes).lower()}, {shape_code(shape)})")
        lines.append("            ));")
    for map_name in sorted(m for m in catalog.maps if m in names):
        entry, value = catalog.maps[map_name]
        shape = catalog.shapes.get((entry, "Value"))
        lines.append(f"            builder.AddMap(new RegistryNativeMapDescriptor({cs(map_name)}, {cs(entry)}, "
                     f"{cs(value)}) {{ ValueShape = {shape_code(shape)} }});")
    for root in catalog.choices:
        if root not in names and root not in adapter_types:
            continue
        selector, choices, unknown = catalog.choices[root]
        alias_code = ", ".join(f"new RegistrySelectorAlias({cs(a)}, {cs(k)})"
                               for a, k in aliases(selector, list(choices), canonical_selector))
        choice_code = ", ".join(f"new RegistryNativeChoice({cs(k)}, {cs(v)})" for k, v in choices.items())
        lines.append(f"            builder.AddChoiceFamily(new RegistryNativeChoiceFamily({cs(root)}, {cs(selector)}, "
                     f"{cs(unknown)})")
        lines.append("            {")
        lines.append(f"                Comparison = RegistrySelectorComparison.{comparison(selector)},")
        lines.append(f"                Aliases = [{alias_code}],")
        lines.append(f"                Choices = [{choice_code}]")
        lines.append("            });")
    lines += ["        }", "    }", "}", ""]
    return "\r\n".join(lines)


def emit_coverage(rows, provenance):
    lines = [
        "// <auto-generated>",
        "// Generated by tools/GenerateRegistryNativeCatalog.py from the Endpoint rows of the specification's",
        "// native-field coverage ledger. Do not edit.",
        f"//   commit: {provenance['commit']}",
        f"//   uncommittedSource: {str(provenance['uncommittedSource']).lower()}",
        "// </auto-generated>",
        "",
        "namespace Opc.Ua.EndpointRegistry.Tests",
        "{",
        "    internal static partial class EndpointRegistryNativeCoverage",
        "    {",
        "        public static readonly (string Path, string Type, string Field, string SourceType, "
        "string NativeType, bool IsArray)[] Rows =",
        "        [",
    ]
    for row in rows:
        lines.append(f"            ({cs(row['path'])}, {cs(row['type'])}, {cs(row['field'])}, "
                     f"{cs(row.get('sourceType', ''))}, {cs(row.get('nativeType', ''))}, "
                     f"{str(bool(row.get('array', False))).lower()}),")
    lines += ["        ];", "    }", "}", ""]
    return "\r\n".join(lines)


def exact_json(value):
    """Serialize without losing numeric form, member order or Unicode (Decimal-aware)."""
    if value is None:
        return "null"
    if value is True:
        return "true"
    if value is False:
        return "false"
    if type(value) is int:
        return str(value)
    if type(value) is Decimal:
        if not value.is_finite():
            fail("a fixture number is not finite")
        return str(value)
    if type(value) is str:
        return json.dumps(value, ensure_ascii=True)
    if type(value) is list:
        return "[" + ",".join(exact_json(item) for item in value) + "]"
    if type(value) is dict:
        return "{" + ",".join(json.dumps(name, ensure_ascii=True) + ":" + exact_json(item)
                              for name, item in value.items()) + "}"
    fail("unsupported fixture value: " + type(value).__name__)
    return ""


def render_native(value):
    """A language-neutral rendering of a reference native value: type, ordered fields, bytes as hex."""
    from registry_native_values import NativeStructure  # pylint: disable=import-outside-toplevel
    if isinstance(value, NativeStructure):
        return {"$type": value.type_name, "fields": [[name, render_native(item)] for name, item in value.fields]}
    if type(value) is tuple:
        return [render_native(item) for item in value]
    if type(value) is bytes:
        return {"$bytes": value.hex()}
    if value is None or type(value) in (bool, int, str):
        return value
    fail("unsupported native value in the reference projection: " + type(value).__name__)
    return None


def same_document(first, second):
    """Type-exact document equality; Object member order is not significant."""
    if type(first) is not type(second):
        return False
    if type(first) is dict:
        return first.keys() == second.keys() and all(same_document(first[k], second[k]) for k in first)
    if type(first) is list:
        return len(first) == len(second) and all(same_document(a, b) for a, b in zip(first, second))
    if type(first) is Decimal:
        return first.as_tuple() == second.as_tuple()
    return first == second


def fixture_cases(spec_root):
    """(id, source, sha256, declared DataType, document) for every replayed specification document."""
    def load(relative):
        path = spec_root / relative
        return json.loads(path.read_text(encoding="utf-8"), parse_float=Decimal), relative, sha256(path)

    cases = []
    for name in ("messaging-registry.json", "media-registry.json", "messages/registry.json"):
        document, source, digest = load(EXAMPLES + name)
        cases.append(("example:" + name, source, digest, "EndpointRegistryDocumentDataType", document))
    document, source, digest = load(EXAMPLES + "abstract-template.json")
    cases.append(("example:abstract-template.json", source, digest, "EndpointDataType", document))
    protocols, source, digest = load(EXAMPLES + "messages/protocols.json")
    for key, message in protocols.items():
        cases.append(("example:messages/protocols.json#" + key, source, digest, "MessageDefinitionDataType", message))
    overlay, source, digest = load(EXAMPLES + "messages/base-overlay.json")
    for xid, message in overlay["definitions"].items():
        cases.append(("example:messages/base-overlay.json#" + xid, source, digest, "MessageDefinitionDataType",
                      message))
    cases.append(("example:messages/base-overlay.json#expected", source, digest, "MessageDefinitionDataType",
                  overlay["expected"]))
    for path in sorted((spec_root / EXAMPLES / "pubsub").glob("*.json")):
        document, source, digest = load(path.relative_to(spec_root).as_posix())
        for key, declared in (("producerEndpoint", "EndpointDataType"), ("consumerEndpoint", "EndpointDataType"),
                              ("message", "MessageDefinitionDataType"), ("derivedEndpoint", "EndpointDataType")):
            if key in document:
                cases.append((f"example:pubsub/{path.name}#{key}", source, digest, declared, document[key]))
    for name, identifier, content in (("metadata.json", "name", "input"), ("message-metadata.json", "id", "value")):
        vectors, source, digest = load(VECTORS + name)
        for case in vectors["cases"]:
            declared = VECTOR_TYPES.get(case["kind"])
            if declared is not None:
                cases.append((f"vector:{name}#{case[identifier]}", source, digest, declared, case[content]))
    return cases


def reference_fixtures(catalog, spec_root, provenance):
    from registry_native_values import project_record, restore_record  # pylint: disable=import-outside-toplevel
    lines = []
    for identifier, source, digest, declared, document in fixture_cases(spec_root):
        case = {"id": identifier, "source": source, "sha256": digest, "type": declared, "document": document}
        try:
            native = project_record(document, declared, catalog)
        except ValueError as error:
            case.update(representable=False, error=str(error))
        else:
            case.update(representable=True, native=render_native(native),
                        restoresExactly=same_document(restore_record(native, catalog), document))
        lines.append(exact_json(case))
    header = {"format": "RegistryNativeFixtures/1.0", "repository": REPOSITORY, "commit": provenance["commit"],
              "uncommittedSource": provenance["uncommittedSource"]}
    return "{\n\"header\":" + exact_json(header) + ",\n\"cases\":[\n" + ",\n".join(lines) + "\n]}\n", len(lines)


def catalog_digest(catalog, names, adapter_types):
    payload = {
        "types": [[n, catalog.types[n].namespace, catalog.types[n].identifier, catalog.types[n].base,
                   catalog.types[n].abstract,
                   [[f.name, f.datatype, f.source, f.array, f.subtypes] for f in catalog.types[n].fields]]
                  for n in (*adapter_types, *names)],
        "maps": sorted([m, *catalog.maps[m]] for m in catalog.maps if m in names),
        "choices": [[root, catalog.choices[root][0], dict(catalog.choices[root][1]), catalog.choices[root][2]]
                    for root in catalog.choices if root in names or root in adapter_types],
        "shapes": sorted([k[0], k[1], json.dumps(v, sort_keys=True)] for k, v in catalog.shapes.items()
                         if k[0] in names),
    }
    return hashlib.sha256(json.dumps(payload, sort_keys=True).encode("utf-8")).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--spec-root", required=True, type=Path)
    parser.add_argument("--stack-root", default=STACK_ROOT, type=Path)
    parser.add_argument("--allow-uncommitted", action="store_true")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    spec_root, stack_root = args.spec_root.resolve(), args.stack_root.resolve()
    commit = git(spec_root, "rev-parse", "HEAD").strip()
    dirty = [line for line in git(spec_root, "status", "--porcelain", "--", "model", "extras", "source").splitlines()
             if line.strip()]
    if dirty and not args.allow_uncommitted:
        fail("the specification checkout has uncommitted changes; commit them or pass --allow-uncommitted")
    check_unicode_tables()
    catalog, canonical_selector, modules = load_catalog(spec_root)
    names, adapter_types = exported_types(catalog)
    check_types(catalog, names)
    check_pinned_model(catalog, names, stack_root)
    rows, ledger_matches = coverage_rows(catalog, spec_root)
    provenance = {
        "repository": REPOSITORY,
        "commit": commit,
        "uncommittedSource": bool(dirty),
        "uncommittedPaths": sorted(line[3:] for line in dirty),
        "catalogSha256": catalog_digest(catalog, names, adapter_types),
        "coverageRows": len(rows),
        "coverageMatchesCommittedLedger": ledger_matches,
        "exportedTypes": len(names) + len(adapter_types),
        "inputs": {
            **{"spec:" + name: digest for name, digest in modules.items()},
            "spec:" + SPEC_COVERAGE.as_posix(): sha256(spec_root / SPEC_COVERAGE),
            "spec:" + SPEC_MODEL.as_posix(): sha256(spec_root / SPEC_MODEL),
            "stack:" + PINNED_NODESET.as_posix(): sha256(stack_root / PINNED_NODESET),
            "stack:" + PINNED_NODE_IDS.as_posix(): sha256(stack_root / PINNED_NODE_IDS),
        },
        "outputs": [CATALOG_OUTPUT.as_posix(), COVERAGE_OUTPUT.as_posix(), FIXTURE_OUTPUT.as_posix()],
    }
    fixtures, fixture_count = reference_fixtures(catalog, spec_root, provenance)
    provenance["referenceFixtures"] = fixture_count
    outputs = {
        CATALOG_OUTPUT: emit_catalog(catalog, names, adapter_types, canonical_selector, provenance),
        COVERAGE_OUTPUT: emit_coverage(rows, provenance),
        FIXTURE_OUTPUT: fixtures,
        PROVENANCE_OUTPUT: json.dumps(provenance, indent=2) + "\n",
    }
    stale = []
    for relative, text in outputs.items():
        target = stack_root / relative
        if args.check:
            if not target.exists() or target.read_bytes().decode("utf-8") != text:
                stale.append(relative.as_posix())
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("w", encoding="utf-8", newline="") as stream:
            stream.write(text)
    if stale:
        fail("generated outputs differ from the pinned catalog: " + ", ".join(stale))
    print(f"Registry native catalog: {len(names) + len(adapter_types)} types, {len(rows)} Endpoint coverage rows, "
          f"{fixture_count} reference fixtures" + (" (checked)" if args.check else "")
          + (" [uncommitted source]" if dirty else ""))


if __name__ == "__main__":
    os.environ.setdefault("PYTHONDONTWRITEBYTECODE", "1")
    main()
