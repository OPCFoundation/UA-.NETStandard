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
using Opc.Ua;
using Opc.Ua.Identity;
using Opc.Ua.WotCon.Client;

namespace AggregationClient
{
    /// <summary>
    /// Configures the reusable aggregation loader and reader workflow.
    /// </summary>
    public sealed class AggregationClientOptions
    {
        /// <summary>
        /// Gets or sets the aggregation server endpoint.
        /// </summary>
        public string AggregationEndpoint { get; set; } =
            "opc.tcp://localhost:62550/AggregationServer";

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
        public string ApplicationName { get; set; } = "AggregationClient";

        /// <summary>
        /// Gets or sets the isolated PKI root.
        /// </summary>
        public string? PkiRoot { get; set; }

        /// <summary>
        /// Gets or sets whether untrusted server certificates may be accepted for development.
        /// Defaults to false; this does not select SecurityPolicy None.
        /// </summary>
        public bool AutoAcceptUntrustedCertificates { get; set; }

        /// <summary>
        /// Gets or sets whether to select unsecured endpoints for an isolated demonstration.
        /// Defaults to false, selecting SignAndEncrypt with Basic256Sha256; certificate trust is independent.
        /// </summary>
        public bool UseSecurityPolicyNone { get; set; }

        /// <summary>
        /// Gets or sets the authenticated identity provider used for registry management.
        /// Secrets are resolved by the provider, not stored in these options.
        /// </summary>
        public IClientIdentityProvider? IdentityProvider { get; set; }

        /// <summary>
        /// Gets or sets the directory containing documents.json and its documents.
        /// </summary>
        public string DocumentsDirectory { get; set; } =
            System.IO.Path.Combine(System.AppContext.BaseDirectory, "Documents");

        /// <summary>
        /// Gets or sets whether to run the explicit control and alarm demonstration.
        /// Defaults to false; enabling this also connects to both sources to verify forwarded methods and alarm state.
        /// </summary>
        public bool ExerciseControls { get; set; }
    }

    /// <summary>
    /// Result of loading, refreshing, browsing and reading the aggregate Pump.
    /// </summary>
    public sealed class AggregationClientResult
    {
        /// <summary>
        /// Captures the registry load result and browse/read results for a single materialized pump.
        /// </summary>
        public AggregationClientResult(
            WotRegistryBulkLoadResult loadResult,
            ArrayOf<WotPumpBrowseNode> browsedNodes,
            ArrayOf<WotPumpValueResult> values)
        {
            LoadResult = loadResult;
            BrowsedNodes = browsedNodes;
            Values = values;
        }

        /// <summary>
        /// Captures registry loading, per-pump results, and optional control round trips.
        /// Exposes the first pump's browse/read results through the single-pump properties when present.
        /// </summary>
        public AggregationClientResult(
            WotRegistryBulkLoadResult loadResult,
            ArrayOf<WotPumpResult> pumps,
            ArrayOf<WotPumpControlResult> controls = default)
        {
            LoadResult = loadResult;
            Pumps = pumps;
            Controls = controls;
            if (!pumps.IsEmpty)
            {
                BrowsedNodes = pumps[0].BrowsedNodes;
                Values = pumps[0].Values;
            }
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

        /// <summary>
        /// Gets the independently browsed and read pumps.
        /// </summary>
        public ArrayOf<WotPumpResult> Pumps { get; }

        /// <summary>
        /// Gets completed source-owned management and alarm round trips.
        /// </summary>
        public ArrayOf<WotPumpControlResult> Controls { get; }
    }

    /// <summary>
    /// Identifies a completed Start, Stop, Reset, Acknowledge and Confirm demonstration.
    /// </summary>
    public sealed class WotPumpControlResult
    {
        /// <summary>
        /// Records the pump and source whose management and alarm round trips completed.
        /// </summary>
        public WotPumpControlResult(string pumpName, string sourceName)
        {
            PumpName = pumpName ?? throw new ArgumentNullException(nameof(pumpName));
            SourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
        }

        /// <summary>
        /// Gets the pump whose local actions were exercised.
        /// </summary>
        public string PumpName { get; }

        /// <summary>
        /// Gets the independently observed upstream source.
        /// </summary>
        public string SourceName { get; }
    }

    /// <summary>
    /// Contains the model identity, browse results, and typed readings for one pump.
    /// </summary>
    public sealed class WotPumpResult
    {
        /// <summary>
        /// Captures a pump's discovered root, browsed nodes, and typed measurement results.
        /// </summary>
        public WotPumpResult(
            string name,
            NodeId rootNodeId,
            ArrayOf<WotPumpBrowseNode> browsedNodes,
            ArrayOf<WotPumpValueResult> values)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            RootNodeId = rootNodeId;
            BrowsedNodes = browsedNodes;
            Values = values;
        }

        /// <summary>
        /// Gets the stable pump name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the root discovered by browsing from Objects.
        /// </summary>
        public NodeId RootNodeId { get; }

        /// <summary>
        /// Gets the pump's objects, variables, methods, and nested properties.
        /// </summary>
        public ArrayOf<WotPumpBrowseNode> BrowsedNodes { get; }

        /// <summary>
        /// Gets the measurements, supervision signals, and identity values.
        /// </summary>
        public ArrayOf<WotPumpValueResult> Values { get; }
    }

    /// <summary>
    /// Describes one node found while browsing the materialized Pump.
    /// </summary>
    public sealed class WotPumpBrowseNode
    {
        /// <summary>
        /// Captures the identity, names, and node class returned by browsing a materialized pump.
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
        /// Captures one named pump property's node ID, read status, and typed value.
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
