# OPC UA WoT export tool

This .NET 10 global tool reads OPC UA NodeSet2 files. It uses
`WotNodeSetConverter` to write W3C WoT Thing Models and linked support files.

```powershell
dotnet tool install --global OPCFoundation.NetStandard.Opc.Ua.Wot.Exporter --prerelease
opcua-wot-export C:\models
opcua-wot-export C:\models C:\export
opcua-wot-export C:\models\Opc.Ua.Di.NodeSet2.xml
```

The source path is required. The default output is `out` in the source
folder. An explicit output path is relative to the current folder.
The tool reads `*NodeSet2.xml` files in all source subfolders.
It does not follow directory links.

Each source has its own output folder. The folder has the relative source
path, with the XML file name as its last part. This prevents name conflicts
between source files. Keep the generated file names. Links use these names.
Some generated files have no `.json` suffix. Their content is still JSON.
The JSON `@type`, not the file suffix, tells you if a file is a Thing Model.
The converter can also emit support documents for instances and native
source records. DataType and ReferenceType Nodes can have their own Thing
Models. The tool keeps all linked documents and warns about support files.

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
