/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Serialization;

using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for exporting OPC UA server address spaces to NodeSet2 XML format.
    /// </summary>
    [McpServerToolType]
    public sealed class NodeSetExportTools
    {
        private const int kMaxBrowseDepth = 128;

        /// <summary>
        /// Export the address space of a connected OPC UA server to NodeSet2 XML.
        /// </summary>
        [McpServerTool(Name = "ExportNodeSet")]
        [Description("Export the address space of a connected OPC UA server to a NodeSet2 XML file. " +
            "Browses recursively from a starting node, collects all nodes, and writes them as " +
            "standard NodeSet2 XML. The file can be used for documentation, analysis, or import " +
            "into other tools. Returns the file path and node count.")]
        public static async Task<string> ExportNodeSetAsync(
            OpcUaSessionManager sessionManager,
            [Description("File path to write the NodeSet2 XML to (e.g. 'C:\\export\\server.xml'). " +
                "Directory will be created if it doesn't exist.")] string filePath,
            [Description("Starting node ID for the export (default: 'i=84' = Root). " +
                "Use 'i=85' for Objects folder only.")] string startingNodeId = "i=84",
            [Description("Export mode: 'Default' (schema-only, no values) or 'Complete' " +
                "(includes runtime values and user context). Default: 'Default'")] string exportMode = "Default",
            [Description("Whether to include the starting node itself (default: true)")] bool includeStartNode = true,
            [Description("Whether to filter out OPC UA base type nodes from namespace 0 " +
                "(default: false — include all)")] bool filterBaseTypes = false,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                var stopwatch = Stopwatch.StartNew();

                // Collect all nodes via NodeCache
                IList<INode> nodes = await FetchAllNodesAsync(
                    session, OpcUaJsonHelper.ParseNodeId(startingNodeId),
                    includeStartNode, filterBaseTypes, ct).ConfigureAwait(false);

                NodeSetExportOptions options = exportMode.Equals(
                    "Complete", StringComparison.OrdinalIgnoreCase)
                    ? NodeSetExportOptions.Complete
                    : NodeSetExportOptions.Default;

                // Ensure directory exists
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Export
                using var outputStream = new FileStream(filePath, FileMode.Create);
                var systemContext = new SystemContext(sessionManager.Telemetry)
                {
                    NamespaceUris = session.NamespaceUris,
                    ServerUris = session.ServerUris,
                };

                CoreClientUtils.ExportNodesToNodeSet2(
                    systemContext, nodes, outputStream, options);

                stopwatch.Stop();
                long fileSize = new FileInfo(filePath).Length;

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["filePath"] = filePath,
                    ["nodeCount"] = nodes.Count,
                    ["fileSizeBytes"] = fileSize,
                    ["exportMode"] = exportMode,
                    ["durationMs"] = stopwatch.ElapsedMilliseconds,
                    ["namespaces"] = session.NamespaceUris.ToArray()
                        .Select((uri, idx) => new Dictionary<string, object?>
                        {
                            ["index"] = idx,
                            ["uri"] = uri,
                        }).ToList(),
                });
            }
            catch (ServiceResultException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = ex.StatusCode.ToString(),
                    ["message"] = ex.Message,
                });
            }
        }

        /// <summary>
        /// Export the address space split into separate NodeSet2 XML files per namespace.
        /// </summary>
        [McpServerTool(Name = "ExportNodeSetPerNamespace")]
        [Description("Export the address space split into separate NodeSet2 XML files, one per namespace. " +
            "Useful for exporting companion specifications individually. Skips the OPC UA base " +
            "namespace (ns=0) by default. Returns a list of exported files.")]
        public static async Task<string> ExportNodeSetPerNamespaceAsync(
            OpcUaSessionManager sessionManager,
            [Description("Output directory where NodeSet2 XML files will be created")] string outputDirectory,
            [Description("Starting node ID (default: 'i=84' = Root)")] string startingNodeId = "i=84",
            [Description("Optional list of namespace URIs to include. If omitted, all non-base " +
                "namespaces are exported.")] string[]? namespaceFilter = null,
            [Description("Export mode: 'Default' or 'Complete' (default: 'Default')")] string exportMode = "Default",
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                var stopwatch = Stopwatch.StartNew();

                // Collect all nodes
                IList<INode> nodes = await FetchAllNodesAsync(
                    session, OpcUaJsonHelper.ParseNodeId(startingNodeId),
                    true, false, ct).ConfigureAwait(false);

                NodeSetExportOptions options = exportMode.Equals(
                    "Complete", StringComparison.OrdinalIgnoreCase)
                    ? NodeSetExportOptions.Complete
                    : NodeSetExportOptions.Default;

                Directory.CreateDirectory(outputDirectory);

                // Build namespace filter set
                HashSet<string>? targetSet = namespaceFilter is { Length: > 0 }
                    ? new HashSet<string>(namespaceFilter, StringComparer.OrdinalIgnoreCase)
                    : null;

                // Group nodes by namespace
                var nodesByNamespace = nodes
                    .Where(node => node.NodeId.NamespaceIndex > 0)
                    .GroupBy(node => node.NodeId.NamespaceIndex)
                    .Where(group =>
                    {
                        string nsUri = session.NamespaceUris.GetString(group.Key);
                        if (string.IsNullOrEmpty(nsUri))
                        {
                            return false;
                        }
                        if (targetSet != null)
                        {
                            return targetSet.Contains(nsUri);
                        }
                        return !string.Equals(nsUri, Namespaces.OpcUa, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToDictionary(g => g.Key, g => (IList<INode>)g.ToList());

                var exportedFiles = new List<Dictionary<string, object?>>();

                foreach (KeyValuePair<ushort, IList<INode>> kvp in nodesByNamespace)
                {
                    ct.ThrowIfCancellationRequested();

                    string nsUri = session.NamespaceUris.GetString(kvp.Key);
                    string fileName = CreateSafeFileName(nsUri, kvp.Key);
                    string filePath = Path.Combine(outputDirectory, fileName);

                    using var outputStream = new FileStream(filePath, FileMode.Create);
                    var systemContext = new SystemContext(sessionManager.Telemetry)
                    {
                        NamespaceUris = session.NamespaceUris,
                        ServerUris = session.ServerUris,
                    };

                    CoreClientUtils.ExportNodesToNodeSet2(
                        systemContext, kvp.Value, outputStream, options);

                    exportedFiles.Add(new Dictionary<string, object?>
                    {
                        ["namespaceUri"] = nsUri,
                        ["namespaceIndex"] = kvp.Key,
                        ["filePath"] = filePath,
                        ["nodeCount"] = kvp.Value.Count,
                        ["fileSizeBytes"] = new FileInfo(filePath).Length,
                    });
                }

                stopwatch.Stop();

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["outputDirectory"] = outputDirectory,
                    ["totalNodeCount"] = nodes.Count,
                    ["exportedNamespaces"] = exportedFiles.Count,
                    ["durationMs"] = stopwatch.ElapsedMilliseconds,
                    ["files"] = exportedFiles,
                });
            }
            catch (ServiceResultException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = ex.StatusCode.ToString(),
                    ["message"] = ex.Message,
                });
            }
        }

        /// <summary>
        /// Recursively fetches all nodes from the server starting at the given node
        /// using the session's NodeCache.
        /// </summary>
        private static async Task<IList<INode>> FetchAllNodesAsync(
            ISession session,
            NodeId startingNode,
            bool addRootNode,
            bool filterBaseTypes,
            CancellationToken ct)
        {
            var nodeDictionary = new Dictionary<ExpandedNodeId, INode>();
            ArrayOf<NodeId> referenceTypes = [ReferenceTypeIds.HierarchicalReferences];
            ArrayOf<ExpandedNodeId> nodesToBrowse = [startingNode];

            // Clear NodeCache to ensure fresh fetch from server
            session.NodeCache.Clear();

            // Pre-populate reference types
            await FetchReferenceIdTypesAsync(session, ct).ConfigureAwait(false);

            if (addRootNode)
            {
                INode? rootNode = await session.NodeCache.FindAsync(startingNode, ct)
                    .ConfigureAwait(false);
                if (rootNode != null)
                {
                    nodeDictionary[rootNode.NodeId] = rootNode;
                }
            }

            int depth = 0;
            while (nodesToBrowse.Count > 0 && depth < kMaxBrowseDepth)
            {
                ct.ThrowIfCancellationRequested();
                depth++;

                ArrayOf<INode> response = await session.NodeCache
                    .FindReferencesAsync(nodesToBrowse, referenceTypes, false, true, ct)
                    .ConfigureAwait(false);

                var nextNodesToBrowse = new List<ExpandedNodeId>();
                foreach (INode node in response)
                {
                    if (!nodeDictionary.ContainsKey(node.NodeId))
                    {
                        if (filterBaseTypes && node.NodeId.NamespaceIndex == 0)
                        {
                            continue;
                        }

                        nodeDictionary[node.NodeId] = node;
                        nextNodesToBrowse.Add(node.NodeId);
                    }
                }

                nodesToBrowse = nextNodesToBrowse.ToArray();
            }

            return nodeDictionary.Values.ToList();
        }

        /// <summary>
        /// Pre-loads reference type hierarchy into the NodeCache.
        /// </summary>
        private static async Task FetchReferenceIdTypesAsync(
            ISession session, CancellationToken ct)
        {
            ArrayOf<ExpandedNodeId> referenceNodes = [ReferenceTypeIds.References];
            ArrayOf<NodeId> referenceTypes = [ReferenceTypeIds.HasSubtype];

            for (int depth = 0; depth < 10; depth++)
            {
                ArrayOf<INode> response = await session.NodeCache
                    .FindReferencesAsync(referenceNodes, referenceTypes, false, true, ct)
                    .ConfigureAwait(false);

                if (response.Count == 0)
                {
                    break;
                }

                referenceNodes = response.ToArray()!
                    .Select(n => n.NodeId)
                    .ToArray();
            }
        }

        /// <summary>
        /// Creates a safe filename from a namespace URI.
        /// </summary>
        private static string CreateSafeFileName(string namespaceUri, ushort namespaceIndex)
        {
            string fileName = namespaceUri
                .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("urn:", string.Empty, StringComparison.OrdinalIgnoreCase);

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            fileName = fileName
                .Replace('/', '_')
                .Replace('\\', '_')
                .Replace(':', '_')
                .TrimEnd('_');

            if (fileName.Length > 200)
            {
                fileName = fileName[..200];
            }

            return $"{fileName}_ns{namespaceIndex}.xml";
        }
    }
}
