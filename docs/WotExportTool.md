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
```

The source path is required. The output path is optional.
The default output is `out` in the input folder. For a file input, it is
`out` in the file's parent folder. An explicit relative output path is
relative to the current folder.

The command finds `*NodeSet2.xml` files in all source subfolders.
The file name check is not case-sensitive. The command does not follow
directory links or read the output folder as input.
An output folder that contains the source folder is not allowed.

## Output

Each input file gets a separate output folder. For example:

```text
Input:  C:\models\DI\Opc.Ua.Di.NodeSet2.xml
Output: C:\models\out\DI\Opc.Ua.Di.NodeSet2.xml\model.TM.json
```

The existing converter chooses the other document names and links.
Some generated names have no `.json` suffix. Their content is still JSON.
Keep the names and all support files. The JSON `@type` is the type claim;
the `.TM.json` suffix alone is not a type claim.
An Object instance can need a Thing Description instead of a Thing Model.
The tool reports these support files with a warning.

The command uses the asynchronous document-set export API.
That API checks that the result can preserve the source facts.
It can add a native projection or a preservation envelope when the readable
mapping is not complete. The command does not change the export vocabulary,
create network endpoints, or fetch required models from the network.
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

```powershell
dotnet build tools\Opc.Ua.Wot.Exporter\Opc.Ua.Wot.Exporter.csproj -c Release -p:CustomTestTarget=net10.0
dotnet test tests\Opc.Ua.Wot.Exporter.Tests\Opc.Ua.Wot.Exporter.Tests.csproj -c Release -p:CustomTestTarget=net10.0
dotnet pack tools\Opc.Ua.Wot.Exporter\Opc.Ua.Wot.Exporter.csproj -c Release -p:CustomTestTarget=net10.0 -o .\artifacts\packages
dotnet tool install --tool-path .\artifacts\tools --add-source .\artifacts\packages OPCFoundation.NetStandard.Opc.Ua.Wot.Exporter --prerelease
```

The tool has no Debug package. It uses the same package ID for each Release
version. The executable uses dependency injection to create the command.
Tests can also construct the command with separate output and error writers.
