<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# NodeSet2 to WoT export tool

The `opcua-wot-export` command reads OPC UA NodeSet2 XML files. It writes
W3C WoT Thing Models with the existing
[`WotNodeSetConverter`](WotNodeSetConversion.md). It requires .NET 10.

## Install and run

For a local package, use [Build and test](#build-and-test).
If the package is on your configured feed, install it with:

```powershell
dotnet tool install --global OPCFoundation.NetStandard.Opc.Ua.Wot.Exporter --prerelease
opcua-wot-export C:\models
opcua-wot-export C:\models C:\export
opcua-wot-export C:\models\Opc.Ua.Di.NodeSet2.xml
opcua-wot-export C:\models C:\export --registry-base-url https://registry.example.com --group-id opcua
opcua-wot-export C:\models C:\export --metadata C:\publisher.json --identifier-safe-keys
```

The source path is required. The output path is optional.
The default output is `out` in the input folder. For a file input, it is
`out` in the file's parent folder. An explicit relative output path is
relative to the current folder.

`--registry-base-url` sets the registry URL. Its default is
`https://opcfoundation.org`. Set the `OPCUA_WOT_REGISTRY_BASE_URL`
environment variable to override this default. The `--registry-base-url`
option takes precedence over the environment variable. An unset or empty
environment variable uses the default. The URL can include a path, but it must
use HTTP or HTTPS and must not include credentials, a query, or a fragment.
`--group-id` sets the Thing Model group ID. Its default is `opcua`.
Options can precede or follow the paths.

The command finds `*NodeSet2.xml` files in all source subfolders.
The file name check is not case-sensitive. The command does not follow
directory links or read the output folder as input.
An output folder that contains the source folder is not allowed.

All successfully read NodeSets form a local type-resolution context before export
starts. Include the required companion models and the OPC UA base NodeSet in
the source tree so event ancestry and method argument properties can be resolved
across namespace boundaries.
Their nodes remain in their own output folders; they are not added to a
dependent model's source partition. A single-file input supplies only that file
as the local context.

The command follows each method's argument-property references, including
references to properties in another NodeSet. It writes named `input` and
`output` schemas with explicit argument order. If an external method property
cannot be resolved or its argument value cannot be decoded, that source export
fails with a diagnostic. It does not treat missing metadata as zero arguments.

## Output

Each input file gets a separate output folder. For example:

```text
Input:  C:\models\DI\Opc.Ua.Di.NodeSet2.xml
Output: C:\models\out\DI\Opc.Ua.Di.NodeSet2.xml\model.TM.json
```

The existing converter chooses the other document names.
Some generated names have no `.json` suffix. Their content is still JSON.
Keep the names and all support files for local storage. The JSON `@type` is the type claim;
the `.TM.json` suffix alone is not a type claim.
An Object instance can need a Thing Description instead of a Thing Model.
The tool reports these support files with a warning.

Thing Models receive a stable `id` using the baseline catalogue convention:
reverse the hostname labels of the type node's namespace URI, append its
dot-separated URI path and local BrowseName, and prefix the result with `urn:`.
For example, ADI's `AccessorySlotStateMachineType` receives
`urn:org.opcfoundation.UA.ADI.AccessorySlotStateMachineType`.
Duplicate names within a namespace receive `_1`, `_2`, and subsequent suffixes
in NodeSet source order, including names used by instance nodes.
Existing document IDs are preserved. Titles remain the source DisplayNames;
`uav:id` remains the namespace-qualified native node identity.

### Names and publisher metadata

The exporter writes `uav:fullName` and `uav:typeName` independently of the
document `id`. Type roots carry the represented type name. Affordance names
identify their native declaring type and declaration path, not their output
key. Value schemas identify their DataType, including array element types.

Automatic names reverse the namespace's DNS host labels and append its path
segments and native BrowseName. The namespace must be an HTTP(S) URL with no
userinfo, explicit port, query, fragment, or trailing host dot. Each name
segment must match `[A-Za-z_][A-Za-z0-9_]*`. Invalid segments, ambiguous
declaration ownership, and name collisions require explicit mappings.
The exporter does not invent name suffixes to resolve these collisions.

Use `--metadata <file>` for explicit publisher facts. The UTF-8 JSON object
uses portable native NodeIds as keys:

```json
{
  "nsu=urn:plant:pump;i=1001": {
    "name": "com.example.plant.PumpType",
    "isStandalone": true
  },
  "nsu=urn:plant:pump;i=1010": {
    "name": "com.example.plant.PumpType.Speed",
    "eventContractType": ["com.example.plant.SpeedChanged.v1"]
  },
  "nsu=urn:plant:pump;i=1011": {
    "name": "com.example.plant.PumpType.Speed.EngineeringUnits",
    "unitCodeSystem": "http://unitsofmeasure.org",
    "unitCode": "m/s"
  }
}
```

Provide a `name` for every required identity whose name cannot be derived,
including custom DataTypes. Each `name` must be unambiguous in the input batch.
Metadata does not change document IDs or native identities.

`isStandalone` applies only to a non-event ObjectType Thing Model root.
Both `true` and `false` are explicit publisher assertions. Omission leaves
intent unspecified. Subtypes do not inherit this assertion. The converter
preserves it as residue on a round trip.

`eventContractType` applies only to the interaction with that native identity.
Its array must contain distinct contract type strings with a final `.vN`
segment, where `N` is a positive integer with no leading zero.
An ordinary NodeSet export does not assert a CloudEvents contract.

### Identifier-safe keys

`--identifier-safe-keys` selects identifier-safe keys in `properties`,
`actions`, and `events`. The default mode retains native local BrowseNames.
The selected mode prefers a nonempty native `SymbolicName`. Otherwise, it
uses the local BrowseName. It replaces each maximal run outside
`[A-Za-z0-9_]` with `_` and prefixes `A_` if the result does not start with
an ASCII letter.

Within each interaction map, canonical native identity order determines
collision suffixes `_2`, `_3`, and later values. Candidate names and source
pointers break ties. The exporter updates optionality, unit associations,
and schema or model references to use the emitted keys. Native BrowseNames,
identities, and literal payloads remain unchanged.

`tm:optional` contains complete-affordance RFC 6901 pointers for `Optional`,
`OptionalPlaceholder`, and `MandatoryPlaceholder` declarations. The native
modelling rule still controls placeholder cardinality.

### Payload schemas and engineering metadata

Ordinary WoT schemas describe Structure fields, inherited fields, required
members, arrays, enums, and Union alternatives. Native type annotations remain
authoritative. A schema that cannot be fully represented, such as a recursive
Structure or an unresolved type, produces a diagnostic with the document and
schema pointer. Include all required DataType models to reduce these gaps.

Concrete integer properties have exact storage bounds at their scalar or
array-item schemas. Resolved custom simple types use their concrete storage
type. Tighter authored constraints remain in place. An empty intersection
fails export. These added storage bounds do not apply to Method/Event schemas,
abstract numeric types, or nested Structure fields.

`uav:euRange` carries engineering limits separately from ordinary
`minimum` and `maximum`. Unknown native NaN endpoints are omitted. An infinite
endpoint produces a diagnostic and remains available in the native source.

`uav:withUnit` points to a separate string property in the same document.
That property declares `uav:unitCodeSystem` and uses a code from that
vocabulary, not localized display text. UN/CEFACT codes come from the packed
native UnitId. Use paired `unitCodeSystem` and `unitCode` metadata for other
vocabularies. An unknown vocabulary produces a diagnostic and no runtime unit
association. `uav:engineeringUnits` retains the native unit information.

The exporter creates no sampling endpoint. A runtime publisher must associate
each sample with the unit that applied to it. If that association is unavailable,
the consumer must treat the sample's unit as unknown.

Singular fallback titles, descriptions, and engineering-unit text retain their
selected source locale through a local context override. Only untagged source
text uses `"@language": null`.

### Registry references and versions

The registry resource ID is separate from the WoT `id` in the JSON document:

```text
resourceId = lowercase-hex(SHA-256(UTF-8(wot.id)))
```

The command keeps the WoT `id` unchanged. It uses the resource ID in links
to other exported Thing Models:

```text
{registryBaseUrl}/thingmodelgroups/{groupId}/thingmodels/{resourceId}/versions/{versionId}
```

The version comes from the target NodeSet's
`Models/Model[@ModelUri='<target namespace URI>']/@Version`.
It is the target namespace's own version, not the referring model's version
or a `RequiredModel` version constraint. Each Thing Model also receives
this value in `version.model`. Other members of `version` are preserved.
A missing, empty, or invalid version causes the affected source export to
fail before it writes files. The command does not invent a version.

For example, the model ID
`urn:org.opcfoundation.UA.ADI.AccessorySlotStateMachineType`
has the resource ID
`aacc8e0db1e57bb41a53019c162369a4f63680be6ee5c292d67f70f613274d26`.
If its namespace version is `42` and the registry base URL is
`https://registry.example.com`, a model that extends it has this link:

```json
{
  "links": [
    {
      "rel": "tm:extends",
      "href": "https://registry.example.com/thingmodelgroups/opcua/thingmodels/aacc8e0db1e57bb41a53019c162369a4f63680be6ee5c292d67f70f613274d26/versions/42",
      "type": "application/tm+json"
    }
  ]
}
```

The command resolves model targets across all successfully converted input
files before it writes links. It uses each target's final WoT `id`, including
an authored ID. References through `links`, `tm:ref`, and schema `$ref`
use registry URLs when they identify an exported Thing Model. JSON Pointer
fragments are preserved, including when a native string NodeId contains `#`.
For native string NodeIds, only `#/` starts a JSON Pointer fragment.
If that complete native target is not registered, the reference stays
unchanged; the command does not resolve a shorter prefix before another `#`.
References in `patternProperties` schema maps also use registry URLs.
Resolved parent Thing Models add a `tm:extends` link. The native
`ua:HasSupertype` link remains unchanged. An unavailable parent produces an
incomplete-inheritance diagnostic and retains the native link.

Fallback Thing Models for view-only or standalone-method NodeSets also
receive registry identities and namespace versions. Their native view or
method is preserved.

The first conversion pass retains model identity, version, key allocations,
and readable DataType definitions.
It disposes each source's generated documents before it converts the next
source. A second pass converts and writes one source at a time.
Generated JSON payloads do not accumulate across the input tree; the local
type resolver still retains the loaded NodeSets.
Conflicting model identities fail affected source exports before files are
written, including distinct native types that claim the same registry resource.

Links to local support documents keep their local file names.
Unresolved native references and unrelated external URLs remain unchanged.
Native node identities, preservation payloads, form URLs, and literal schema
values are not rewritten.

The command writes files, not registry resources. Publish each Thing Model
under its hashed resource ID and namespace version before consumers follow
its registry links. Keep support files at the locations their local links name.

The command uses the asynchronous document-set export API.
That API checks that the result can preserve the source facts.
It can add a native projection or a preservation envelope when the readable
mapping is not complete. The command applies the mapping rules above. It does not create network
endpoints or fetch required models from the network.
The output is not a certificate of full W3C or OPC UA conformance.
See [conversion limits and conformance](WotNodeSetConversion.md).
The command permits up to 100,000 linked documents, a depth of 128, and
512 MiB of generated JSON per source. It uses the same bounds when it
resolves these documents for the source-preservation check.

The command replaces each existing output file after its new content is
written. It does not delete old files. Use an empty output folder when you
need a clean document set. A stopped command or a write error can leave
an incomplete set.

## Progress and errors

The command prints the source file number, the source path, conversion
progress, and document counts. It writes warnings and errors to standard
error. It keeps the converter's diagnostic code and location.
If one file fails, the command tries the other files.
Press Ctrl+C to stop.

| Exit code | Meaning |
| --- | --- |
| `0` | All source files were exported. Warnings can be present. |
| `1` | A file, conversion, or output operation failed. |
| `2` | The arguments or the source path are not valid, or no input was found. |
| `130` | The command was stopped. |

## Build and test

From the repository root:

The exporter and its tests use .NET 10 by default. Set
`CustomTestTarget=net11.0` to build and test with .NET 11, as the PR CI
profile does. The .NET Framework profile excludes these projects.

```powershell
dotnet build tools\Opc.Ua.Wot.Exporter\Opc.Ua.Wot.Exporter.csproj -c Release -p:CustomTestTarget=net10.0
dotnet test tests\Opc.Ua.Wot.Exporter.Tests\Opc.Ua.Wot.Exporter.Tests.csproj -c Release -p:CustomTestTarget=net10.0
dotnet pack tools\Opc.Ua.Wot.Exporter\Opc.Ua.Wot.Exporter.csproj -c Release -p:CustomTestTarget=net10.0 -o .\artifacts\packages
dotnet tool install --tool-path .\artifacts\tools --add-source .\artifacts\packages OPCFoundation.NetStandard.Opc.Ua.Wot.Exporter --prerelease
```

The tool has no Debug package. It uses the same package ID for each Release
version. The executable uses dependency injection to create the command.
Tests can also construct the command with separate output and error writers.
