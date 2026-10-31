# WoT Registry Resolution Vectors

<!-- words: allowprerelease bytewise contentdigest gitattributes hashlib hexdigest iterdir minversion pathlib prereleases py submodel typelinks versionorigin workingdrafts wotid -->

These 64 fixtures cover [specification Section 1.3](../../spec.md#13-versioning)
and [Section 5.4](../../spec.md#54-versioned-reference-resolution). Each
numbered JSON file describes one case. The `sha256:` values are syntactically
valid placeholders; they identify fixture values and are not computed from a
WoT document. `MANIFEST.sha256` hashes the committed bytes of every file in this
directory except itself, including this README, the schema and the checker.

## Running the checker

From the repository root:

```sh
python workingdrafts/models/wot/tests/resolution/check_vectors.py
```

The checker verifies manifest coverage and hashes, requires LF and a final
newline for each file, validates the schema and vectors when `jsonschema` is
installed, and runs the reference implementation against every expected
result. If `jsonschema` is absent, it reports the skipped shape check;
manifest verification and semantic execution still run. A failed check
returns a nonzero exit status. The checker uses only Python's standard
library for the remaining checks and fetches no schemas or documents.

## Format

The format is defined by [schema.json](schema.json), using JSON Schema draft
2020-12. Unknown fields are rejected. Top-level fields are:

| Field | Meaning |
|---|---|
| `description` | Human-readable purpose of the case. |
| `registry` | Scoped Resource snapshots with `wotid`, default selection and Versions. |
| `reference` | Resolution request: `kind`, target `wotid`, OPTIONAL `minVersion`, and OPTIONAL `floorTerm`. |
| `document` | OPTIONAL TD context for TM `rel:type` links: `version.model` and `typeLinks`. |
| `publish` | Publication request with `target`, OPTIONAL `targetKind`, `versionid`, `version.model`, and `version.instance`. |
| `pin` | Caller pin: exactly one of `{ "digest": "sha256:..." }` or `{ "versionid": "1.0.0" }`. |
| `allowPrerelease` | OPTIONAL caller boolean, default `false`. |
| `expect` | Exactly one resolution `result`, publication `assigned`, or `error`. |

The `reference.kind` values `extends`, `submodel`, `ref`, and `type` retain
their TM or type-link meanings. `td-extends`, `td-subthing`, and `td-ref`
represent TD-to-TD operators. `minVersion` is the normalized value of the
floor term. `floorTerm`, when present, records `tm:minVersion` or
`td:minVersion` so vectors can test a floor used with the wrong target kind;
when omitted, the expected term follows `kind`. Existing vectors omit both new
properties and remain valid. A `publish.targetKind` of `td` selects
`version.instance`; if absent, publication remains a TM case using
`version.model`.

A Version in `registry[].versions` has `versionid`, `versionorigin`
(`authored` or `generated`), `contentdigest`, and OPTIONAL `version.model` or
`version.instance`. A Resource MAY additionally have
`deprecated: { "alternative": "https://..." }`. Version ids in stored
snapshots are valid SemVer without build metadata. `defaultversionid` MAY be
null for an empty Resource awaiting publication. A missing sticky Version can
be represented to test an unavailable selection; a conforming Core write
would not create that state.

The dotted field names `version.model` and `version.instance` are literal JSON
keys in these normalized fixtures, corresponding to nested WoT document
members. For `ref`, `target` is the document identifier after URI resolution
and removal of the fragment; fragment traversal is outside these
version-selection cases. No fixture transformation changes a stored document.

For a `type` reference, `document.typeLinks` lists all TD type links, and the
request MUST project exactly one link by `target`, including its explicit
`minVersion` if any. A single link uses the greater of its explicit floor and
`document["version.model"]` when both are present. Several type links with a
TD `version.model` require explicit floors on every link; otherwise the result
is `ambiguous-floor`. Without `version.model`, each type link uses only its
own floor, if present. This rule does not apply to TD-to-TD references: they
have no implicit floor. A TD floor `X.Y.Z` selects versions greater than or
equal to the floor and less than `(X+1).0.0`; this also excludes prereleases
whose major is `X+1`. An unfloored TD reference can cross majors. A sticky
default outside that range is `unsatisfied`. Multiple Resource matches are
disambiguated by registry and Group before this fixture interface; a `wotid`
occurs at most once in `registry`.

A successful resolution expects the portable fields `wotid`, `versionid`,
and `contentdigest`. The production resolver also returns `origin`; it is
resolver context, not authored document data or a Resource attribute. The
checker uses the fixed origin `https://registry.example` and checks it before
comparing the portable result. Consumers MUST retain their actual registry
origin in production output.

A successful publication expects `versionid`, `versionorigin`, and
`defaultversionid`. The default is the Core default after publication, not the
resolver's prerelease-filtered choice. Publication requests address an
existing Resource snapshot; an empty snapshot represents creation. They test
TM or TD version assignment and default selection, not document compatibility
validation or Core HTTP envelopes. Supplied invalid syntax is intentionally
permitted by the input schema so the checker, rather than schema validation,
produces the expected domain error.

An error expectation is `{ "error": "<name>" }`. The names are
`not-found`, `pin-mismatch`, `unsatisfied`, `ambiguous-floor`, `invalid-floor`,
`version-mismatch`, and `invalid-version`. Pins bypass floors, sticky
selection, the TD major cap, and prerelease filtering. Otherwise, a sticky
default is authoritative and MUST be in range; without a sticky default the
highest eligible SemVer wins. Generated `0.0.0-N` ids are prereleases and are
never valid floors.

## TD-to-TD vectors (050–064)

| File | Case |
|---|---|
| `050-td-caret-within-major.json` | Select the highest eligible TD version under the next major. |
| `051-td-caret-excludes-next-major.json` | Exclude a candidate at the next-major boundary. |
| `052-td-unfloored-crosses-majors.json` | Allow an unfloored TD reference to cross majors. |
| `053-td-sticky-higher-major-unsatisfied.json` | Reject a sticky default in a higher major. |
| `054-td-floor-unmet.json` | Reject a TD target with no version meeting the floor. |
| `055-td-prerelease-floor.json` | Admit eligible prereleases within the caret range. |
| `056-td-ref-floor.json` | Apply the sibling floor on `td:ref`. |
| `057-td-subthing-floor.json` | Apply a major-zero floor on `td:subthing`. |
| `058-td-publish-authored-assignment.json` | Assign authored identity from `version.instance`. |
| `059-td-publish-generated-id.json` | Assign the lowest unused generated id. |
| `060-td-publish-version-mismatch.json` | Reject a supplied id that differs from `version.instance`. |
| `061-td-publish-build-metadata.json` | Reject build metadata in `version.instance`. |
| `062-td-minversion-on-tm-reference-invalid-floor.json` | Reject `td:minVersion` on a TM reference. |
| `063-tm-minversion-on-td-reference-invalid-floor.json` | Reject `tm:minVersion` on a TD reference. |
| `064-td-extends-floor.json` | Apply the floor on `td:extends`. |

## Canonical bytes and maintenance

All files are UTF-8 without a byte-order mark, use LF and end in a newline.
JSON files use two-space indentation. Vector filenames are stable and are
listed in bytewise filename order in `MANIFEST.sha256`, whose lines are
`<lowercase-sha256>  <filename>`. The repository's `.gitattributes` pins LF
in Windows checkouts because the manifest hashes exact bytes. No timestamps or
absolute local paths enter the fixtures or manifest.

After an intentional edit, regenerate the manifest from the repository root,
then run the checker:

```python
from hashlib import sha256
from pathlib import Path

folder = Path("workingdrafts/models/wot/tests/resolution")
files = sorted((p for p in folder.iterdir() if p.is_file() and p.name != "MANIFEST.sha256"), key=lambda p: p.name)
text = "".join(f"{sha256(p.read_bytes()).hexdigest()}  {p.name}\n" for p in files)
(folder / "MANIFEST.sha256").write_text(text, encoding="ascii", newline="\n")
```

A semantic mutation test changes an expectation to another schema-valid value,
regenerates the manifest so the semantic check is reached, and requires the
checker to fail. Both the vector and manifest MUST then be restored byte for
byte and the checker rerun. A separate byte mutation without rehashing MUST
fail manifest verification.
