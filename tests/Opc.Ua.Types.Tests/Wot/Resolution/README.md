# WoT Registry Resolution Vectors

<!-- words: allowprerelease bytewise contentdigest gitattributes hashlib hexdigest iterdir minversion pathlib prereleases py submodel typelinks versionorigin workingdrafts wotid -->

These canonical fixtures exercise [specification Section 1.3](../../spec.md#13-versioning) and [Section 5.4](../../spec.md#54-versioned-reference-resolution). Each numbered JSON file is one independent case. Fixtures contain fake, syntactically valid `sha256:` digests followed by 64 lowercase hexadecimal characters; they identify fixture bytes symbolically and are not computed from a WoT document. `MANIFEST.sha256`, in contrast, hashes the actual committed bytes of every file in this directory except itself, including this README, the schema and the checker.

## Running the checker

From the repository root:

```sh
python workingdrafts/models/wot/tests/resolution/check_vectors.py
```

The checker verifies manifest coverage and hashes, requires LF and a final newline for every file, validates the schema and all vectors if `jsonschema` is installed, and executes the reference implementation against every expectation. Without `jsonschema` it explicitly reports the skipped shape check; semantic execution and manifest verification still run. Any failed check returns nonzero. The implementation uses only the Python standard library otherwise, and does not fetch schemas or documents.

## Format

The format is defined by [schema.json](schema.json), using JSON Schema draft 2020-12. Unknown fields are rejected. The top-level fields are:

| Field | Meaning |
|---|---|
| `description` | Human-readable purpose of the case. |
| `registry` | Array of scoped Resource snapshots. Each has `wotid`, `defaultversionsticky`, `defaultversionid` and `versions`. |
| `reference` | Resolution request: `kind` (`extends`, `submodel`, `ref` or `type`), `target` (target `wotid`), and OPTIONAL `minVersion`. |
| `document` | OPTIONAL TD context: OPTIONAL `version.model` and REQUIRED `typeLinks` array. Each type link has `target` and OPTIONAL `minVersion`. |
| `publish` | Write-side request instead of `reference`: `target`, OPTIONAL `versionid`, and OPTIONAL `version.model`. |
| `pin` | OPTIONAL caller pin, exactly one of `{ "digest": "sha256:..." }` or `{ "versionid": "1.0.0" }`. Only for resolution cases. |
| `allowPrerelease` | OPTIONAL caller boolean, default `false`. Only for resolution cases. |
| `expect` | Exactly one resolution `result`, publication `assigned`, or `error`, as defined below. |

A Version in `registry[].versions` has `versionid`, `versionorigin` (`authored` or `generated`), `contentdigest`, and OPTIONAL `version.model`. A Resource MAY additionally have `deprecated: { "alternative": "https://..." }`. Version ids in stored snapshots are valid SemVer without build metadata. `defaultversionid` MAY be null for an empty Resource awaiting publication. A missing sticky Version can be represented to test an unavailable selection; a conforming Core write would not create that state.

The dotted field name `version.model` is a literal JSON key in these normalized fixtures, corresponding to the nested WoT document member `version.model`. The fixture's `minVersion` is the normalized spelling of authored `tm:minVersion`. For `ref`, `target` is the document identifier after URI resolution and removal of the fragment; fragment traversal is outside these version-selection cases. No fixture transformation changes a stored document.

For a `type` reference, `document.typeLinks` lists all TD type links, and the request MUST project exactly one link by `target`, including its explicit `minVersion` if any. The list does not enumerate other link relations. A single link inherits `document["version.model"]` unless it has an explicit floor. Several links with a document model version require a floor on every link; without that model version they have no implicit floor. Multiple Resource matches are disambiguated by registry and Group before this fixture interface; a `wotid` occurs at most once in `registry`.

A successful resolution expects exactly:

```json
{
  "result": {
    "wotid": "urn:example:pump:v1",
    "versionid": "1.1.0",
    "contentdigest": "sha256:0000000000000000000000000000000000000000000000000000000000000002"
  }
}
```

The production resolver also returns `origin`. It is resolver context, not authored document data or a Resource attribute. This checker uses the fixed origin `https://registry.example` and checks it separately before comparing the portable three-field result above. Consumers of the vectors MUST also retain their actual registry origin in production resolution output.

A successful publication expects exactly:

```json
{
  "assigned": {
    "versionid": "1.1.0",
    "versionorigin": "authored",
    "defaultversionid": "1.1.0"
  }
}
```

`assigned.defaultversionid` is the Core default after publication, not the resolver's prerelease-filtered choice. Publication requests address an existing Resource snapshot; an empty snapshot represents creation. They test TM version assignment and default selection, not document compatibility validation or Core HTTP envelopes. Supplied invalid syntax is intentionally permitted by the input schema so the resolver or publisher, rather than schema validation, produces the expected domain error.

An error expectation is `{ "error": "<name>" }`. The exact names are `not-found`, `pin-mismatch`, `unsatisfied`, `ambiguous-floor`, `version-mismatch` and `invalid-version`. Pins bypass floors, sticky selection and prerelease filtering. Otherwise a sticky default is authoritative; without a sticky default the highest eligible SemVer wins. A selected Version below the floor fails without a downgrade. Generated `0.0.0-N` ids are never valid floors and are prereleases, so a generated-only Resource without opt-in, pin or sticky default is unsatisfied.

## Canonical bytes and maintenance

All files are UTF-8 without a byte-order mark, use LF and end in a newline. JSON files use two-space indentation. Vector filenames are stable and are listed in bytewise filename order in `MANIFEST.sha256`, whose lines are `<lowercase-sha256>  <filename>`. The repository's `.gitattributes` enforces LF for this directory. No timestamps or absolute local paths enter the fixtures or manifest.

After an intentional edit, regenerate the manifest from the repository root, then run the checker:

```python
from hashlib import sha256
from pathlib import Path

folder = Path("workingdrafts/models/wot/tests/resolution")
files = sorted((p for p in folder.iterdir() if p.is_file() and p.name != "MANIFEST.sha256"), key=lambda p: p.name)
text = "".join(f"{sha256(p.read_bytes()).hexdigest()}  {p.name}\n" for p in files)
(folder / "MANIFEST.sha256").write_text(text, encoding="ascii", newline="\n")
```

A semantic mutation test changes an expectation to another schema-valid value, regenerates the manifest so the semantic check is reached, and requires the checker to fail. Both the vector and manifest MUST then be restored byte-for-byte and the checker rerun. A separate byte mutation without rehashing MUST fail manifest verification.
