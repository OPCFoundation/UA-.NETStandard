<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# OPC UA WoT export tool

This .NET 10 global tool reads OPC UA NodeSet2 files. It uses
`WotNodeSetConverter` to write W3C WoT Thing Models and linked support files.

```powershell
dotnet tool install --global OPCFoundation.NetStandard.Opc.Ua.Wot.Exporter --prerelease
opcua-wot-export C:\models
opcua-wot-export C:\models C:\export
opcua-wot-export C:\models\Opc.Ua.Di.NodeSet2.xml
opcua-wot-export C:\models C:\export --registry-base-url https://registry.example.com --group-id opcua
```

The source path is required. The default output is `out` in the source
folder. An explicit output path is relative to the current folder.
The tool reads `*NodeSet2.xml` files in all source subfolders.
It does not follow directory links.

Each source has its own output folder. The folder has the relative source
path, with the XML file name as its last part. This prevents name conflicts
between source files. Keep the generated file names for local support links.
Some generated files have no `.json` suffix. Their content is still JSON.
The JSON `@type`, not the file suffix, tells you if a file is a Thing Model.
The converter can also emit support documents for instances and native
source records. DataType and ReferenceType Nodes can have their own Thing
Models. The tool keeps all linked documents and warns about support files.

Thing Models carry catalogue-compatible `id` URNs derived from the native
namespace URI and local BrowseName, for example
`urn:org.opcfoundation.UA.ADI.AccessorySlotStateMachineType`.
Titles and native `uav:id` identities are unchanged.

Links to exported Thing Models use this registry path:

```text
{registryBaseUrl}/thingmodelgroups/{groupId}/thingmodels/{resourceId}/versions/{versionId}
```

`resourceId` is the lowercase hexadecimal SHA-256 hash of the UTF-8 bytes
of the target's WoT `id`. The JSON document keeps its original `id`.
`versionId` comes from the target namespace's `Model Version` in NodeSet2 XML,
not the referring namespace or its `RequiredModel` version.
Thing Models also carry that version in `version.model`.
A missing or invalid version fails the affected source before files are written.

The default registry URL is `https://azureiotoperations.com` and the default
group ID is `opcua`. Use `--registry-base-url` and `--group-id` to change them.
JSON Pointer fragments are preserved. Links to support files remain local.
The tool does not rewrite native preservation payloads, form URLs, or literal
schema values. Include required NodeSets in the input to resolve their model
links. Unresolved native references remain unchanged.

Publish the generated Thing Models under their hashed registry resource IDs
and namespace versions. The tool does not upload documents or host URLs.

The tool prints progress, converter diagnostics, and a final count.
It checks source preservation with the existing asynchronous export API.
It does not fetch required models from the network. Missing declarations
can require a native projection or a source preservation envelope.
An export is not a claim of full W3C or OPC UA conformance.

Exit codes: `0` success, `1` export or file error, `2` invalid command or
missing input, `130` cancellation. Other source files are still processed
after a file error. Cancellation can leave a partial output set.
Existing generated files are replaced. Old files are not deleted.
Use an empty output folder for a clean export.
