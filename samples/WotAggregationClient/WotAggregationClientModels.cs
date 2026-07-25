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

using Opc.Ua;
using Opc.Ua.WotCon.Client;

namespace WotAggregationClient
{
    /// <summary>
    /// Configures the reusable aggregation loader and reader workflow.
    /// </summary>
    public sealed class WotAggregationClientOptions
    {
        /// <summary>
        /// Gets or sets the aggregation server endpoint.
        /// </summary>
        public string AggregationEndpoint { get; set; } =
            "opc.tcp://localhost:62550/WotAggregationServer";

        /// <summary>
        /// Gets or sets the Source A endpoint substituted into the Pump TD.
        /// </summary>
        public string SourceAEndpoint { get; set; } =
            "opc.tcp://localhost:62551/SourceA";

        /// <summary>
        /// Gets or sets the Source B endpoint substituted into the Pump TD.
        /// </summary>
        public string SourceBEndpoint { get; set; } =
            "opc.tcp://localhost:62552/SourceB";

        /// <summary>
        /// Gets or sets the OPC UA application name.
        /// </summary>
        public string ApplicationName { get; set; } = "WotAggregationClient";

        /// <summary>
        /// Gets or sets the isolated PKI root.
        /// </summary>
        public string? PkiRoot { get; set; }

        /// <summary>
        /// Gets or sets the directory containing documents.json and its documents.
        /// </summary>
        public string DocumentsDirectory { get; set; } =
            System.IO.Path.Combine(System.AppContext.BaseDirectory, "Documents");
    }

    /// <summary>
    /// Result of loading, refreshing, browsing and reading the aggregate Pump.
    /// </summary>
    public sealed class WotAggregationClientResult
    {
        /// <summary>
        /// Initializes a result.
        /// </summary>
        public WotAggregationClientResult(
            WotRegistryBulkLoadResult loadResult,
            ArrayOf<WotPumpBrowseNode> browsedNodes,
            ArrayOf<WotPumpValueResult> values)
        {
            LoadResult = loadResult;
            BrowsedNodes = browsedNodes;
            Values = values;
        }

        /// <summary>
        /// Gets the document load and refresh result.
        /// </summary>
        public WotRegistryBulkLoadResult LoadResult { get; }

        /// <summary>
        /// Gets the recursively browsed Pump nodes.
        /// </summary>
        public ArrayOf<WotPumpBrowseNode> BrowsedNodes { get; }

        /// <summary>
        /// Gets the values read from the materialized Pump.
        /// </summary>
        public ArrayOf<WotPumpValueResult> Values { get; }
    }

    /// <summary>
    /// Describes one node found while browsing the materialized Pump.
    /// </summary>
    public sealed class WotPumpBrowseNode
    {
        /// <summary>
        /// Initializes a browse result.
        /// </summary>
        public WotPumpBrowseNode(
            NodeId nodeId,
            QualifiedName browseName,
            LocalizedText displayName,
            NodeClass nodeClass)
        {
            NodeId = nodeId;
            BrowseName = browseName;
            DisplayName = displayName;
            NodeClass = nodeClass;
        }

        /// <summary>
        /// Gets the node id.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// Gets the browse name.
        /// </summary>
        public QualifiedName BrowseName { get; }

        /// <summary>
        /// Gets the display name.
        /// </summary>
        public LocalizedText DisplayName { get; }

        /// <summary>
        /// Gets the node class.
        /// </summary>
        public NodeClass NodeClass { get; }
    }

    /// <summary>
    /// Contains one materialized Pump value.
    /// </summary>
    public sealed class WotPumpValueResult
    {
        /// <summary>
        /// Initializes a value result.
        /// </summary>
        public WotPumpValueResult(
            string name,
            NodeId nodeId,
            StatusCode statusCode,
            Variant value)
        {
            Name = name;
            NodeId = nodeId;
            StatusCode = statusCode;
            Value = value;
        }

        /// <summary>
        /// Gets the stable property name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the materialized node id.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// Gets the read status.
        /// </summary>
        public StatusCode StatusCode { get; }

        /// <summary>
        /// Gets the read value.
        /// </summary>
        public Variant Value { get; }
    }
}
