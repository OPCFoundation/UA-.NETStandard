# Client-based NodeSet Export

The OPC UA .NET Standard client library can export a server's address space
to a NodeSet2 XML file. Use the export to document or analyze an address
space, or to import it into another system.

## Contents

- [Overview](#overview)
- [Usage](#usage)
  - [Basic Example](#basic-example)
  - [Using the Console Reference Client](#using-the-console-reference-client)
- [Supported Node Types](#supported-node-types)
- [Features](#features)
  - [Node Attributes Exported](#node-attributes-exported)
  - [Namespace Handling](#namespace-handling)
  - [References](#references)
- [Limitations](#limitations)
  - [MCP server export paths](#mcp-server-export-paths)
- [API Reference](#api-reference)
  - [CoreClientUtils.ExportNodesToNodeSet2](#coreclientutilsexportnodestonodeset2)
  - [ClientSamples.ExportNodesToNodeSet2](#clientsamplesexportnodestonodeset2)
- [Related Topics](#related-topics)
- [Examples](#examples)
- [See Also](#see-also)

## Overview

The client-based NodeSet export feature enables:

- Browsing and collecting nodes from an OPC UA server
- Converting client-side node representations (`INode`) to server-side representations (`NodeState`)
- Exporting nodes to standard NodeSet2 XML format
- Round-tripping: exported NodeSets can be imported using existing import functionality

## Usage

### Basic Example

The following helper exports the direct children of the Server node from an
existing session. The caller owns the session and output stream. For connection
and trust setup, see [Getting started](GettingStarted.md); for recursive export,
see [Using the Console Reference Client](#using-the-console-reference-client).

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

/// <summary>
/// Exports nodes browsed through an existing client session.
/// </summary>
public static class NodeSetExportExample
{
    /// <summary>
    /// Exports the Server node's direct hierarchical children to NodeSet2 XML.
    /// </summary>
    public static async Task ExportServerChildrenAsync(
        ISession session,
        ITelemetryContext telemetry,
        Stream output,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(output);

        var browser = new Browser(session)
        {
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = 0
        };

        ArrayOf<ReferenceDescription> references =
            await browser.BrowseAsync(ObjectIds.Server, ct).ConfigureAwait(false);
        var nodes = new List<INode>();
        for (int i = 0; i < references.Count; i++)
        {
            INode? node = await session.NodeCache.FindAsync(references[i].NodeId, ct)
                .ConfigureAwait(false);
            if (node is not null)
            {
                nodes.Add(node);
            }
        }

        ct.ThrowIfCancellationRequested();
        var systemContext = new SystemContext(telemetry)
        {
            NamespaceUris = session.NamespaceUris,
            ServerUris = session.ServerUris
        };
        CoreClientUtils.ExportNodesToNodeSet2(systemContext, nodes, output);
    }
}
```

Browsing and node reads are asynchronous; the existing export helper writes
the collected nodes to XML synchronously.

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

The export functionality handles:

- Multiple namespaces
- Namespace URIs are included in the exported NodeSet
- NodeIds are mapped to namespace indices

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

### MCP server export paths

The MCP server's `ExportNodeSet` tools use a separate, per-user export root.
`OpcUaMcpOptions.NodeSetExportRoot` takes precedence over the
`OPCUA_MCP_EXPORT_ROOT` environment variable, followed by the per-user default.
Both relative and absolute output paths must remain inside that root. The MCP
writer rejects existing files, filesystem reparse points and unsafe Unix
directory permissions, and creates new output files exclusively rather than
truncating an existing destination.

Configure the root so no untrusted local account can replace its path
components. The managed path checks do not pin directory handles across the
entire operation, so a concurrently writable shared directory is not a
supported export root. Existing destinations must be moved or removed
explicitly before exporting again.

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
- [Console Reference Client](../samples/Reference/ConsoleReferenceClient/README.md)
- OPC UA NodeSet2 XML Schema specification

## Examples

For complete examples of using the NodeSet export functionality, see:

- `tests/Opc.Ua.Client.Tests/NodeSetExportTest.cs` - Unit tests demonstrating various export scenarios
- `samples/Reference/ConsoleReferenceClient/ClientSamples.cs` - Integration in the reference client

## See Also

- [OPC UA Specification Part 6 - Mappings](https://reference.opcfoundation.org/Core/Part6/v105/)
- NodeSet2 XML Schema: `src/Opc.Ua.Core/Schema/UANodeSet.xsd`
