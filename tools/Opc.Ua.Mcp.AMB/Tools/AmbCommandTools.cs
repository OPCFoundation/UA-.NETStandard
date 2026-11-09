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
using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.AMB.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit AMB mutations; no read or observation tool invokes these operations.
    /// </summary>
    [McpServerToolType]
    public sealed class AmbCommandTools
    {
        /// <summary>
        /// Initializes the command tools.
        /// </summary>
        public AmbCommandTools(AmbClientAccessor accessor)
        {
            m_accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        }

        /// <summary>
        /// Changes or clears a configurable asset identifier.
        /// </summary>
        [McpServerTool(Name = "amb_write_asset_id", ReadOnly = false, Destructive = true)]
        [Description("Explicitly changes an asset's configurable AssetId and its alias-category membership. " +
            "An empty assetId clears it. Requires server authorization; does not change ProductInstanceUri.")]
        public Task<CallToolResult> WriteAssetIdAsync(
            [Description("Local asset NodeId from AMB discovery; e.g. ns=2;s=Asset1.")]
            string assetNodeId,
            [Description("New AssetId; the empty string explicitly clears it.")] string assetId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(assetId);
                NodeId asset = AmbJson.ParseNodeId(assetNodeId);
                AmbClient client = m_accessor.CreateClient(sessionName);
                await client.WriteAssetIdAsync(asset, assetId, token).ConfigureAwait(false);
                return new JsonObject
                {
                    ["succeeded"] = true,
                    ["assetNodeId"] = asset.ToString(),
                    ["assetId"] = assetId
                };
            }, ct);
        }

        /// <summary>
        /// Acknowledges the exact alarm or maintenance event supplied by the caller.
        /// </summary>
        [McpServerTool(Name = "amb_acknowledge", ReadOnly = false, Destructive = true)]
        [Description("Acknowledges one AMB health alarm or finished maintenance event using its conditionId " +
            "and exact base64 eventId from amb_read_asset or amb_observe. Does not clear or repair the fault.")]
        public Task<CallToolResult> AcknowledgeAsync(
            [Description("Condition NodeId from an alarm or maintenance event.")]
            string conditionId,
            [Description("Base64 EventId returned with the specific condition event.")] string eventId,
            [Description("Acknowledgement comment.")] string comment,
            [Description("Optional locale for the comment.")] string? locale = null,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
                ArgumentNullException.ThrowIfNull(comment);
                NodeId condition = AmbJson.ParseNodeId(conditionId);
                ByteString eventToken = ByteString.FromBase64(eventId);
                AmbClient client = m_accessor.CreateClient(sessionName);
                await client.AcknowledgeAsync(condition, eventToken, new LocalizedText(locale, comment), token)
                    .ConfigureAwait(false);
                return new JsonObject { ["succeeded"] = true, ["conditionId"] = condition.ToString() };
            }, ct);
        }

        /// <summary>
        /// Adds a persistent user documentation link.
        /// </summary>
        [McpServerTool(Name = "amb_add_documentation_link", ReadOnly = false, Destructive = true)]
        [Description("Adds a persistent user link to an asset's DocumentationLinks AddIn. This is an explicit " +
            "server mutation, not a URL fetch. Returns the new linkNodeId for later write/remove operations.")]
        public Task<CallToolResult> AddDocumentationLinkAsync(
            [Description("Local asset NodeId from AMB discovery; e.g. ns=2;s=Asset1.")]
            string assetNodeId,
            [Description("Documentation URI to store; never fetched by this tool.")] string uri,
            [Description("Name of the link variable, without a namespace prefix.")] string browseName,
            [Description("Server namespace index for browseName.")] ushort browseNameNamespaceIndex = 0,
            [Description("Optional human-readable name for the new documentation link.")]
            string? displayName = null,
            [Description("Optional localized description for the newly added documentation link.")]
            string? description = null,
            [Description("Optional locale for localized text; e.g. en-US.")]
            string? locale = null,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(uri);
                ArgumentException.ThrowIfNullOrWhiteSpace(browseName);
                NodeId asset = AmbJson.ParseNodeId(assetNodeId);
                AmbClient client = m_accessor.CreateClient(sessionName);
                NodeId link = await client.AddDocumentationLinkAsync(
                    asset, uri, new QualifiedName(browseName, browseNameNamespaceIndex),
                    new LocalizedText(locale, displayName), new LocalizedText(locale, description), token)
                    .ConfigureAwait(false);
                return new JsonObject
                {
                    ["succeeded"] = true,
                    ["assetNodeId"] = asset.ToString(),
                    ["linkNodeId"] = link.ToString()
                };
            }, ct);
        }

        /// <summary>
        /// Removes a previously added user link.
        /// </summary>
        [McpServerTool(Name = "amb_remove_documentation_link", ReadOnly = false, Destructive = true)]
        [Description("Permanently removes a user documentation link from the specified asset. Manufacturer " +
            "links cannot be removed by this method. Pass the linkNodeId returned by add or a documentation read.")]
        public Task<CallToolResult> RemoveDocumentationLinkAsync(
            [Description("Local asset NodeId from AMB discovery; e.g. ns=2;s=Asset1.")]
            string assetNodeId,
            [Description("Documentation-link variable NodeId returned by a read or add operation.")]
            string linkNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                NodeId asset = AmbJson.ParseNodeId(assetNodeId);
                NodeId link = AmbJson.ParseNodeId(linkNodeId);
                AmbClient client = m_accessor.CreateClient(sessionName);
                await client.RemoveDocumentationLinkAsync(asset, link, token).ConfigureAwait(false);
                return new JsonObject { ["succeeded"] = true, ["linkNodeId"] = link.ToString() };
            }, ct);
        }

        /// <summary>
        /// Changes an existing writable documentation URI.
        /// </summary>
        [McpServerTool(Name = "amb_write_documentation_link", ReadOnly = false, Destructive = true)]
        [Description("Explicitly writes the URI of an existing editable documentation link. The server " +
            "enforces user access. Does not add a link, fetch the URI, or remove the link.")]
        public Task<CallToolResult> WriteDocumentationLinkAsync(
            [Description("Documentation-link variable NodeId returned by a read or add operation.")]
            string linkNodeId,
            [Description("URI to store, or an empty string if the server permits clearing it.")] string uri,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(uri);
                NodeId link = AmbJson.ParseNodeId(linkNodeId);
                AmbClient client = m_accessor.CreateClient(sessionName);
                await client.WriteDocumentationLinkAsync(link, uri, token).ConfigureAwait(false);
                return new JsonObject { ["succeeded"] = true, ["linkNodeId"] = link.ToString(), ["uri"] = uri };
            }, ct);
        }

        /// <summary>
        /// The per-invocation client accessor.
        /// </summary>
        private readonly AmbClientAccessor m_accessor;
    }
}
