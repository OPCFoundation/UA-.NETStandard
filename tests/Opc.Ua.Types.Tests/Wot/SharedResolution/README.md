# WoT Registry Resolution Vectors

<!-- words: allowprerelease contentdigest minversion prereleases py pytest submodel typelinks versionorigin workingdrafts wotid -->
<!-- words: standalone -->

The checker exercises version assignment and reference resolution against the
reference implementation using 33 representative cases from
[vectors.json](vectors.json). The file is the canonical source for their stable
case IDs and literal expected outcomes, so independent implementations can
mirror each case without deriving its expected result from this checker.
[Section 1.3](../../spec.md#13-versioning) defines version assignment;
[Section 5.4](../../spec.md#54-versioned-reference-resolution) defines resolution.
The cases illustrate those rules; they do not exhaustively test the helper.
The `sha256:` values are syntactically valid placeholders, not digests of
WoT documents.

## Running the checker

From the repository root:

```sh
python workingdrafts/models/wot/tests/resolution/check_vectors.py
```

The script performs these checks:

1. It loads a nonempty collection and rejects duplicate JSON keys.
2. If the Python environment includes `jsonschema`, it validates the schema
   and vectors.
3. It runs the reference implementation against every expected result.

If `jsonschema` is unavailable, the script reports skipped shape validation
and still runs the semantic cases. Any failed check produces a nonzero exit
code. All checks other than schema validation use Python's standard library.
The script fetches neither schemas nor documents.

Normal pytest and the Windows/Linux example jobs also run each case by ID,
with schema validation REQUIRED:

```sh
python -B -m pytest workingdrafts/models/wot/tests/test_wot_examples.py -q
```

## Format

[schema.json](schema.json) defines the format with JSON Schema draft 2020-12
and rejects unknown fields. The document maps stable case IDs such as
`001-floor-met` to self-contained inputs and literal expected outcomes.
Case IDs are stable identifiers, not a contiguous sequence.
Each case has these fields:

| Field | Meaning |
|---|---|
| `description` | Explains what the case tests. |
| `registry` | Resource snapshots keyed by `wotid`, each with a default selection and its Versions. |
| `reference` | Resolution request with a `kind`, target `wotid`, and OPTIONAL `minVersion`, `floorTerm` and `local`. |
| `document` | OPTIONAL Thing Description (TD) context for Thing Model (TM) `rel:type` links, with `version.model` and `typeLinks`. For a local reference, the containing revision's `versionid` and its `version.model` or `version.instance`. |
| `publish` | Publication request with `target` and OPTIONAL `versionid`, `targetKind`, `version.model`, and `version.instance`. |
| `pin` | Caller pin: exactly one of `{ "digest": "sha256:..." }` or `{ "versionid": "1.0.0" }`. |
| `allowPrerelease` | OPTIONAL caller flag that allows prerelease selection. By default, its value is `false`. |
| `expect` | Exactly one resolution `result`, publication `assigned`, or `error`. |

The `reference.kind` field identifies the operator:

- `extends`, `submodel`, `ref`, and `type` identify TM or type-link operators.
- `td-extends`, `td-subthing`, and `td-ref` identify TD-to-TD operators.

`minVersion` holds the normalized floor. When present, `floorTerm` records
whether the input used `tm:minVersion` or `td:minVersion`, so the checker can
reject a term attached to the wrong target kind. If the caller omits both
properties, the checker uses the term expected for `kind`.

Publication uses `version.instance` when `publish.targetKind` is `"td"`.
Otherwise the request targets a TM and uses `version.model`.

Every entry in `registry[].versions` has `versionid`, `versionorigin`, and
`contentdigest`. The `versionorigin` field accepts `authored` or
`generated`. The entry also has OPTIONAL `version.model` or
`version.instance`. A Resource MAY include
`deprecated: { "alternative": "https://..." }`. Stored snapshots use valid
SemVer version ids without build metadata.
`defaultversionid` MAY be null for an empty Resource awaiting publication. A
vector can set the sticky default to a Version absent from `versions` to test
`not-found`. A conforming Core write would not create that state.

The JSON keys `version.model` and `version.instance` name nested members in
WoT documents. For `ref`, `target` holds the document identifier after URI
resolution and fragment removal. Fragment traversal is outside these
version-selection cases. Fixture checks do not transform stored documents.

For a `type` reference, `document.typeLinks` lists the TD's type links. The
request MUST project exactly one link by `target`, including its explicit
`minVersion` if any.

The checker applies these floor rules:

- With one type link, the checker uses the greater of its explicit floor and
  `version.model` when both are present.
- With several type links and `version.model`, every link needs an
  explicit floor. Otherwise, the checker returns `ambiguous-floor`.
- Without `version.model`, the checker uses each type link's own floor, if
  present.

TD-to-TD references do not inherit an implicit floor.

A `local: true` reference is a document-local `ref` or `td-ref`: its
URI-reference resolves against the containing document's base URI to that
document before its fragment is applied. Its target is the containing
revision named by `document.versionid`. The checker compares the floor with
that revision's `version.model` or `version.instance`, applying the TD major
cap to `td-ref`. A missing authored version, or one below the floor or cap,
returns `unsatisfied`. The checker never selects another stored Version, and
local references take no pin. The document context MUST include the
containing `versionid`.

A TD floor `X.Y.Z` sets the upper limit to `(X+1).0.0`, exclusive. Candidates
at or above that major, including prereleases, are out of range. An unfloored
TD reference can cross majors. If a sticky default falls outside the range,
the checker returns `unsatisfied`. Each fixture's `registry` array contains
at most one Resource per `wotid`. Resolve any matches across registries and
Groups before building the fixture.

A successful resolution returns the portable fields `wotid`, `versionid`
and `contentdigest`. The production resolver also returns `origin`, which
describes resolver context rather than authored document data or a Resource
attribute. The checker fixes this value at `https://registry.example` and
checks it before comparing the portable fields. In production output,
Consumers MUST retain the actual Registry origin.

A successful publication returns `versionid`, `versionorigin` and
`defaultversionid`. Each request targets an existing Resource snapshot; a
snapshot with no Versions represents creation. The cases assign TM or TD
Version ids and select Core's post-publication default, not the resolver's
prerelease-filtered choice. They do not test document compatibility validation
or Core HTTP envelopes. The input schema accepts syntactically invalid values
so the cases can check the corresponding domain errors.

An error expectation uses `{ "error": "<name>" }`. The name is one of:

- `not-found`
- `pin-mismatch`
- `unsatisfied`
- `ambiguous-floor`
- `invalid-floor`
- `version-mismatch`
- `invalid-version`

When a caller supplies a pin, the resolver ignores:

- Floors
- Sticky selection
- The TD major cap
- Prerelease filtering

Otherwise, a sticky default is authoritative and MUST be in range.
Without a sticky default, the highest eligible SemVer wins. Generated
`0.0.0-N` ids are prereleases and are never valid floors.

## Cases

### Selection

| ID | Behavior |
| --- | --- |
| `001-floor-met` | Select the highest eligible release. |
| `002-floor-unmet` | Fail rather than downgrade below the floor. |
| `008-sticky-above-floor` | Honor a sticky default despite a newer eligible release. |
| `009-prerelease-excluded` | Exclude prereleases without a floor or caller opt-in. |
| `017-deprecated-successor` | Do not follow a deprecated Resource's successor. |
| `020-generated-floor-rejected` | Reject a generated id as a floor. |
| `024-not-found` | Fail when the target Resource is absent. |
| `027-generated-opt-in` | Allow generated Versions on opt-in and order their numeric identifiers. |

### Pins

| ID | Behavior |
| --- | --- |
| `004-digest-pin` | Select exact older bytes by digest. |
| `005-digest-pin-mismatch` | Fail when the digest is absent. |
| `026-pin-overrides-floor-and-sticky` | A Version-id pin overrides the floor and sticky default. |
| `049-pin-bypasses-type-floor-derivation` | A pin bypasses type-link ambiguity and invalid implicit floors. |
| `067-digest-pin-duplicate-highest-version` | For matching bytes, select the highest SemVer Version. |

### Type-link floors

| ID | Behavior |
| --- | --- |
| `013-implicit-type-floor` | A single type link inherits the TD's model floor. |
| `015-multiple-types-partly-specified` | An omitted floor on another link causes ambiguity. |
| `043-single-type-greater-floor-sticky` | A sticky choice MUST meet the greater implicit floor. |
| `046-multiple-types-greater-model-floor` | The model floor raises every explicit link floor. |

### TD ranges

| ID | Behavior |
| --- | --- |
| `050-td-caret-within-major` | Select the highest eligible revision below the next major. |
| `052-td-unfloored-crosses-majors` | Allow an unfloored TD reference to cross majors. |
| `055-td-prerelease-floor` | Admit eligible prereleases but exclude the next major's prereleases. |
| `057-td-subthing-floor` | Cap a major-zero floor below 1.0.0. |
| `063-tm-minversion-on-td-reference-invalid-floor` | Reject a TM floor term on a TD reference. |

### Document-local references

| ID | Behavior |
| --- | --- |
| `064-local-tm-ref-uses-containing-revision` | Return the containing revision, not a newer stored Version. |
| `065-local-tm-ref-below-floor` | Fail when the containing TM is below the local floor. |
| `066-local-td-ref-next-major` | Apply the TD major cap to the containing TD. |
| `068-local-tm-ref-missing-authored-version` | Fail when a local TM floor has no authored model version to check. |
| `069-local-tm-ref-invalid-authored-version` | Reject a malformed local TM model version. |

### Assignment

| ID | Behavior |
| --- | --- |
| `012-late-patch-default` | A late patch does not replace a higher default. |
| `019-generated-skip-occupied` | Allocate the lowest unused generated id. |
| `036-publish-authored-assignment` | Assign a TM id from its authored model version. |
| `058-td-publish-authored-assignment` | Assign a TD id from its authored instance version. |
| `060-td-publish-version-mismatch` | Reject a supplied id that differs from the assignment. |
| `061-td-publish-build-metadata` | Reject build metadata in an authored version. |

## Maintenance and limits

Keep inputs and expected outcomes explicit when editing the collection.
Expected results MUST come from the specification, not from the resolver
being checked. Run the standalone checker and pytest after a change.
Files use UTF-8, LF and a final newline, with two-space JSON indentation.

These examples test resolver behavior, not source formatting. They omit many
error permutations, additional SemVer ordering cases and TD-generated
assignment. Stored-document digests, compatibility and deployed publication
are also outside their coverage.
