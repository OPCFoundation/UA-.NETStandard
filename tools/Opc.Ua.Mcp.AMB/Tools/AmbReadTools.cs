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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.AMB;
using Opc.Ua.AMB.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Di;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Bounded AMB discovery, facet reads and observations.
    /// </summary>
    [McpServerToolType]
    public sealed class AmbReadTools
    {
        /// <summary>
        /// Initializes tools using the host's session-aware accessor.
        /// </summary>
        public AmbReadTools(AmbClientAccessor accessor)
        {
            m_accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        }

        /// <summary>
        /// Discovers unique local assets through the AMB alias categories.
        /// </summary>
        [McpServerTool(Name = "amb_discover_assets", ReadOnly = true, Destructive = false)]
        [Description("Discovers unique local AMB asset NodeIds. Start here for amb_read_asset. " +
            "Remote alias targets are not followed; amb_find_assets and amb_list_aliases report them. " +
            "Returns a live page, not a persistent snapshot.")]
        public Task<CallToolResult> DiscoverAssetsAsync(
            [Description("Zero-based live offset, 0..1000000.")] int offset = 0,
            [Description("Maximum items, 1..500.")] int maxResults = 100,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                AmbClient client = m_accessor.CreateClient(sessionName);
                return McpCompanionTools.Page(
                    await client.DiscoverAssetsAsync(token).ConfigureAwait(false),
                    AmbJson.Node, offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Searches an AMB alias category using the standard FindAlias pattern.
        /// </summary>
        [McpServerTool(Name = "amb_find_assets", ReadOnly = true, Destructive = false)]
        [Description("Searches AMB aliases with OPC UA FindAlias wildcards, for example urn:acme:%. " +
            "Returns aliases and ExpandedNodeId targets, including remote identities without connecting to them.")]
        public Task<CallToolResult> FindAssetsAsync(
            [Description("OPC 10000-17 alias pattern, for example %.")] string pattern,
            AmbAssetCategory category = AmbAssetCategory.Assets,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum items, 1..500.")] int maxResults = 100,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(pattern);
                AssetAliasCategory selected = AmbJson.Category(category);
                AmbClient client = m_accessor.CreateClient(sessionName);
                return McpCompanionTools.Page(
                    await client.FindAssetsAsync(pattern, selected, token).ConfigureAwait(false),
                    AmbJson.Alias, offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Browses AMB aliases without invoking FindAlias.
        /// </summary>
        [McpServerTool(Name = "amb_list_aliases", ReadOnly = true, Destructive = false)]
        [Description("Browses an AMB alias category without calling FindAlias. Returns a bounded live page " +
            "with alias names and ExpandedNodeId targets; remote targets are reported, never followed.")]
        public Task<CallToolResult> ListAliasesAsync(
            AmbAssetCategory category = AmbAssetCategory.ByProductInstanceUri,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum items, 1..500.")] int maxResults = 100,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                AssetAliasCategory selected = AmbJson.Category(category);
                AmbClient client = m_accessor.CreateClient(sessionName);
                return await McpCompanionTools.PageAsync(
                    client.EnumerateAssetsAsync(selected, token), AmbJson.Alias, offset, maxResults, token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads the category identity and current NodeVersion.
        /// </summary>
        [McpServerTool(Name = "amb_read_category", ReadOnly = true, Destructive = false)]
        [Description("Reads an AMB alias category's actual NodeId and NodeVersion. Compare versions around " +
            "live paging to detect changes. An absent AMB namespace is reported as supported=false.")]
        public Task<CallToolResult> ReadCategoryAsync(
            AmbAssetCategory category = AmbAssetCategory.Assets,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                AssetAliasCategory selected = AmbJson.Category(category);
                AmbClient client = m_accessor.CreateClient(sessionName, requireSupported: false);
                return new JsonObject
                {
                    ["supported"] = client.IsSupported,
                    ["category"] = category.ToString(),
                    ["nodeId"] = client.IsSupported ? client.GetCategoryId(selected).ToString() : null,
                    ["nodeVersion"] = client.IsSupported
                        ? await client.ReadNodeVersionAsync(selected, token).ConfigureAwait(false)
                        : null
                };
            }, ct);
        }

        /// <summary>
        /// Reads one explicitly selected facet of an asset.
        /// </summary>
        [McpServerTool(Name = "amb_read_asset", ReadOnly = true, Destructive = false)]
        [Description("Reads one AMB asset facet. HealthAlarms includes address-space conditions; RetainedAlarms " +
            "filters Retain=true. Maintenance is current activity state, not history. Collection facets and " +
            "each collection in Snapshot are paged independently using offset/maxResults. Documentation " +
            "includes the AddIn NodeId; links, alarms and locations include IDs for explicit follow-up tools. " +
            "No writes, acknowledgement, remote traversal or invented historical data.")]
        public Task<CallToolResult> ReadAssetAsync(
            [Description("Local asset NodeId returned by discovery.")] string assetNodeId,
            AmbAssetFacet facet = AmbAssetFacet.Snapshot,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum items per collection, 1..500.")] int maxResults = 100,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                AmbJson.RequireDefined(facet, nameof(facet));
                NodeId asset = AmbJson.ParseNodeId(assetNodeId);
                AmbClient client = m_accessor.CreateClient(sessionName);
                JsonNode? data = await ReadFacetAsync(client, asset, facet, offset, maxResults, token)
                    .ConfigureAwait(false);
                return new JsonObject
                {
                    ["assetNodeId"] = asset.ToString(),
                    ["facet"] = facet.ToString(),
                    ["data"] = data
                };
            }, ct);
        }

        /// <summary>
        /// Reads one of the two standardized location object trees.
        /// </summary>
        [McpServerTool(Name = "amb_browse_locations", ReadOnly = true, Destructive = false)]
        [Description("Browses hierarchical or operational AMB location objects, parents before children. " +
            "Returns paths and contained asset identities in a live page. Digital locations have no object tree.")]
        public Task<CallToolResult> BrowseLocationsAsync(
            AmbLocationKind kind = AmbLocationKind.Hierarchical,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum items, 1..500.")] int maxResults = 100,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                AmbJson.RequireDefined(kind, nameof(kind));
                AmbClient client = m_accessor.CreateClient(sessionName);
                AssetLocationKind selected = kind == AmbLocationKind.Hierarchical
                    ? AssetLocationKind.Hierarchical
                    : AssetLocationKind.Operational;
                return McpCompanionTools.Page(
                    await client.BrowseLocationsAsync(selected, token).ConfigureAwait(false),
                    AmbJson.LocationNode, offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Collects one finite AMB observation without owning the session's shared subscription.
        /// </summary>
        [McpServerTool(Name = "amb_observe", ReadOnly = true, Destructive = false)]
        [Description("Observes AMB asset-set, health-alarm or maintenance changes for a bounded interval. " +
            "Requires a managed session. Returns events and stoppedBy; releases its monitored-item enumerator. " +
            "No history or automatic acknowledgement. notifierNodeId applies only to health and maintenance.")]
        public Task<CallToolResult> ObserveAsync(
            AmbObservationKind kind,
            [Description("Alarm/maintenance notifier; omitted means the Server object.")] string? notifierNodeId = null,
            [Description("Observation window in milliseconds, 1..30000.")] int durationMs = 1000,
            [Description("Maximum notifications, 1..500.")] int maxItems = 100,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                AmbJson.RequireDefined(kind, nameof(kind));
                if (kind == AmbObservationKind.AssetSetChanges && notifierNodeId is not null)
                {
                    throw new ArgumentException("Asset-set changes use the Server notifier.", nameof(notifierNodeId));
                }
                AmbClient client = m_accessor.CreateClient(sessionName);
                IStreamingSubscription streaming = McpCompanionTools.GetStreaming(client.Session);
                NodeId notifier = notifierNodeId is null ? NodeId.Null : AmbJson.ParseNodeId(notifierNodeId);
                return kind switch
                {
                    AmbObservationKind.AssetSetChanges => await McpCompanionTools.ObserveAsync(
                        cancellation => client.ObserveAssetSetChangesAsync(
                            streaming, cancellationToken: cancellation),
                        change => new JsonObject
                        {
                            ["category"] = change.Category.ToString(),
                            ["verb"] = change.Verb
                        },
                        durationMs, maxItems, token).ConfigureAwait(false),
                    AmbObservationKind.HealthAlarms => await McpCompanionTools.ObserveAsync(
                        cancellation => client.ObserveHealthAlarmsAsync(
                            notifier, streaming, cancellationToken: cancellation),
                        alarm => AmbJson.Alarm(alarm, client.Session.MessageContext),
                        durationMs, maxItems, token).ConfigureAwait(false),
                    AmbObservationKind.Maintenance => await McpCompanionTools.ObserveAsync(
                        cancellation => client.ObserveMaintenanceAsync(
                            notifier, streaming, cancellationToken: cancellation),
                        activity => AmbJson.Maintenance(activity, client.Session.MessageContext),
                        durationMs, maxItems, token).ConfigureAwait(false),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
            }, ct);
        }

        /// <summary>
        /// Routes a finite read selector to the existing typed client capability.
        /// </summary>
        private static async ValueTask<JsonNode?> ReadFacetAsync(
            AmbClient client,
            NodeId asset,
            AmbAssetFacet facet,
            int offset,
            int maxResults,
            CancellationToken ct)
        {
            IServiceMessageContext context = client.Session.MessageContext;
            switch (facet)
            {
                case AmbAssetFacet.Identification:
                    return AmbJson.Identification(
                        await client.ReadIdentificationAsync(asset, ct).ConfigureAwait(false));
                case AmbAssetFacet.Health:
                    DeviceHealthEnumeration? health = await client.ReadDeviceHealthAsync(asset, ct)
                        .ConfigureAwait(false);
                    return new JsonObject
                    {
                        ["deviceHealth"] = health?.ToString(),
                        ["deviceHealthValue"] = (int?)health
                    };
                case AmbAssetFacet.HealthAlarms:
                case AmbAssetFacet.RetainedAlarms:
                    ArrayOf<AssetAlarmRecord> alarms = await client.ReadHealthAlarmsAsync(asset, ct)
                        .ConfigureAwait(false);
                    if (facet == AmbAssetFacet.RetainedAlarms)
                    {
                        var retained = new List<AssetAlarmRecord>();
                        foreach (AssetAlarmRecord alarm in alarms)
                        {
                            if (alarm.Retain)
                            {
                                retained.Add(alarm);
                            }
                        }
                        alarms = retained.ToArrayOf();
                    }
                    return McpCompanionTools.Page(
                        alarms, alarm => AmbJson.Alarm(alarm, context), offset, maxResults);
                case AmbAssetFacet.Maintenance:
                    return McpCompanionTools.Page(
                        await client.ReadMaintenanceActivitiesAsync(asset, ct).ConfigureAwait(false),
                        activity => AmbJson.Maintenance(activity, context), offset, maxResults);
                case AmbAssetFacet.Documentation:
                    return new JsonObject
                    {
                        ["documentationLinksNodeId"] = (await client.ResolveDocumentationLinksAsync(asset, ct)
                            .ConfigureAwait(false)).ToString(),
                        ["links"] = McpCompanionTools.Page(
                            await client.ReadDocumentationLinksAsync(asset, ct).ConfigureAwait(false),
                            AmbJson.Link, offset, maxResults)
                    };
                case AmbAssetFacet.Context:
                    return AmbJson.Context(await client.ReadContextAsync(asset, ct).ConfigureAwait(false), context);
                case AmbAssetFacet.Locations:
                    return McpCompanionTools.Page(
                        await client.ReadLocationsOfAssetAsync(asset, ct).ConfigureAwait(false),
                        AmbJson.Location, offset, maxResults);
                case AmbAssetFacet.Requirements:
                case AmbAssetFacet.Capabilities:
                    return McpCompanionTools.Page(
                        await client.ReadEntriesAsync(
                            asset,
                            facet == AmbAssetFacet.Requirements
                                ? AssetEntryFolder.Requirements
                                : AssetEntryFolder.Capabilities,
                            ct).ConfigureAwait(false),
                        entry => AmbJson.Entry(entry, context), offset, maxResults);
                case AmbAssetFacet.SubAssets:
                    return await McpCompanionTools.PageAsync(
                        client.EnumerateSubAssetsAsync(asset, ct),
                        node => JsonValue.Create(node.ToString()), offset, maxResults, ct).ConfigureAwait(false);
                case AmbAssetFacet.Relations:
                    return McpCompanionTools.Page(
                        await client.ReadRelationsAsync(asset, ct).ConfigureAwait(false),
                        AmbJson.Relation, offset, maxResults);
                case AmbAssetFacet.Snapshot:
                    return AmbJson.Snapshot(
                        await client.ReadAssetAsync(asset, ct).ConfigureAwait(false), context, offset, maxResults);
                default:
                    throw new ArgumentOutOfRangeException(nameof(facet));
            }
        }

        /// <summary>
        /// The per-invocation client accessor.
        /// </summary>
        private readonly AmbClientAccessor m_accessor;
    }
}
