# Client-based NodeSet Export

The OPC UA .NET Standard stack provides the ability to export the address space from an OPC UA server to a NodeSet2 XML file using the client library. This allows you to browse a server's address space and save it to a file that can be used for documentation, analysis, or import into other systems.

## Overview

The client-based NodeSet export feature enables:

- Browsing and collecting nodes from an OPC UA server
- Converting client-side node representations (`INode`) to server-side representations (`NodeState`)
- Exporting nodes to standard NodeSet2 XML format
- Round-tripping: exported NodeSets can be imported using existing import functionality

## Usage

### Basic Example

```csharp
using System.IO;
using Opc.Ua;
using Opc.Ua.Client;

// Connect to server and create session
var session = await Session.CreateAsync(/*...*/);

// Browse to collect nodes
var browser = new Browser(session)
{
    BrowseDirection = BrowseDirection.Forward,
    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
    IncludeSubtypes = true,
    NodeClassMask = 0
};

var nodesToBrowse = new NodeIdCollection { ObjectIds.Server };
var allNodes = new List<INode>();

// Browse and collect nodes
ReferenceDescriptionCollection references = await browser.BrowseAsync(nodesToBrowse[0]);

foreach (ReferenceDescription reference in references)
{
    INode node = await session.NodeCache.FindAsync(reference.NodeId);
    if (node != null)
    {
        allNodes.Add(node);
    }
}

// Export to NodeSet2 file
using (var stream = new FileStream("exported-nodeset.xml", FileMode.Create))
{
    var systemContext = new SystemContext(telemetry)
    {
        NamespaceUris = session.NamespaceUris,
        ServerUris = session.ServerUris
    };

    CoreClientUtils.ExportNodesToNodeSet2(systemContext, allNodes, stream);
}
```

### Using the Console Reference Client

The console reference client (`ClientSamples` class) includes a helper method for exporting nodes:

```csharp
// Browse and collect nodes using the existing helper
IList<INode> nodes = await clientSamples.FetchAllNodesNodeCacheAsync(
    uaClient,
    ObjectIds.ObjectsFolder,  // Starting node
    fetchTree: true,           // Recursively browse the tree
    addRootNode: true,         // Include the root node
    filterUATypes: true,       // Filter out namespace 0 nodes
    clearNodeCache: true
);

// Export to NodeSet2 file
clientSamples.ExportNodesToNodeSet2(session, nodes, "address-space.xml");
```

## Supported Node Types

The export functionality supports all standard OPC UA node types:

- **Object nodes** - Objects and their properties
- **Variable nodes** - Variables with their values and data types
- **Method nodes** - Methods with their executable status
- **ObjectType nodes** - Object type definitions
- **VariableType nodes** - Variable type definitions
- **DataType nodes** - Data type definitions
- **ReferenceType nodes** - Reference type definitions
- **View nodes** - View definitions

## Features

### Node Attributes Exported

For each node, the following attributes are exported (where applicable):

- NodeId
- BrowseName
- DisplayName
- Description
- WriteMask and UserWriteMask
- References to other nodes
- Type-specific attributes (e.g., DataType for variables, EventNotifier for objects)

### Namespace Handling

The export functionality properly handles:

- Multiple namespaces
- Namespace URIs are included in the exported NodeSet
- NodeIds are correctly mapped to namespace indices

### References

All references between nodes are preserved in the export, including:

- Hierarchical references (e.g., HasComponent, HasProperty)
- Type definition references (e.g., HasTypeDefinition)
- Organizing references
- Custom reference types

## Limitations

- The export creates a snapshot of the address space at the time of browsing
- Dynamic values are captured as they exist at export time
- Not all server-specific internal state may be preserved
- Complex type definitions require the complex types system to be loaded

## API Reference

### CoreClientUtils.ExportNodesToNodeSet2

```csharp
public static void ExportNodesToNodeSet2(
    ISystemContext context,
    IList<INode> nodes,
    Stream outputStream)
```

**Parameters:**
- `context` - System context containing namespace and server URI information
- `nodes` - List of nodes to export
- `outputStream` - Stream to write the NodeSet2 XML to

**Exceptions:**
- `ArgumentNullException` - If any parameter is null

### ClientSamples.ExportNodesToNodeSet2

```csharp
public void ExportNodesToNodeSet2(
    ISession session,
    IList<INode> nodes,
    string filePath)
```

**Parameters:**
- `session` - Active session to use for namespace information
- `nodes` - List of nodes to export
- `filePath` - Path where the NodeSet2 XML file will be saved

## Related Topics

- [Working with ComplexTypes](ComplexTypes.md)
- [Console Reference Client](../Applications/ConsoleReferenceClient/README.md)
- OPC UA NodeSet2 XML Schema specification

## Examples

For complete examples of using the NodeSet export functionality, see:

- `Tests/Opc.Ua.Client.Tests/NodeSetExportTest.cs` - Unit tests demonstrating various export scenarios
- `Applications/ConsoleReferenceClient/ClientSamples.cs` - Integration in the reference client

## See Also

- [OPC UA Specification Part 6 - Mappings](https://reference.opcfoundation.org/Core/Part6/v105/)
- NodeSet2 XML Schema: `Stack/Opc.Ua.Core/Schema/UANodeSet.xsd`
