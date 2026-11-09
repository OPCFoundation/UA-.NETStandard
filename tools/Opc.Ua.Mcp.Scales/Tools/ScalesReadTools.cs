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
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Scales;
using Opc.Ua.Scales.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Finite discovery, read and observation tools for OPC 40200 scales.
    /// </summary>
    [McpServerToolType]
    public sealed class ScalesReadTools
    {
        /// <summary>
        /// Initializes the tools with the injectable per-call client accessor.
        /// </summary>
        public ScalesReadTools(ScalesClientAccessor accessor)
        {
            m_accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        }

        /// <summary>
        /// Lists scales and systems, including vendor subtypes and system members.
        /// </summary>
        [McpServerTool(Name = "scales_list", ReadOnly = true, Destructive = false)]
        [Description("Discover OPC 40200 scales and scale systems. All searches DI DeviceSet and Machinery Machines " +
            "and deduplicates NodeIds; Under searches a folder; System searches a scale/system's SubDevices. " +
            "Returns kind, vendor typeDefinition and isScaleSystem. offset/maxResults page the live enumeration.")]
        public Task<CallToolResult> ListAsync(
            [Description("All, Under, or System.")] ScalesDiscoveryScope scope = ScalesDiscoveryScope.All,
            [Description("Required parent NodeId for Under or System.")] string? parentNodeId = null,
            [Description("Live offset, 0..1000000.")] int offset = 0,
            [Description("Page size, 1..500.")] int maxResults = 100,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ScalesClient client = m_accessor.CreateClient(sessionName);
                IAsyncEnumerable<ScaleEntry> source = scope switch
                {
                    ScalesDiscoveryScope.All => client.EnumerateScalesAsync(token),
                    ScalesDiscoveryScope.Under => client.EnumerateScalesUnderAsync(
                        ParseRequired(parentNodeId), token),
                    ScalesDiscoveryScope.System => client.EnumerateSystemScalesAsync(
                        ParseRequired(parentNodeId), token),
                    _ => throw new ArgumentOutOfRangeException(nameof(scope))
                };
                return await McpCompanionTools.PageAsync(source, ScalesJson.Entry, offset, maxResults, token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads one finite semantic facet, preserving absence, quality and engineering units.
        /// </summary>
        [McpServerTool(Name = "scales_read", ReadOnly = true, Destructive = false)]
        [Description("Read Identification, CurrentWeight, RegisteredWeight, WeighingRanges, AllowedEngineeringUnits, " +
            "Products, CurrentProducts, PackMLState or Snapshot. A missing WeightItem is available=false; " +
            "a published but never-written weight remains available=true with NaN and its raw status. " +
            "Products include actual DI lockNodeId for di_acquire_lock/di_release_lock (--profile scales,di); " +
            "this tool never acquires a lock. Snapshot reads sequentially, not atomically. Collection facets " +
            "and each Snapshot collection use the requested live page.")]
        public Task<CallToolResult> ReadAsync(
            [Description("Scale, scale system or module NodeId.")] string nodeId,
            [Description("Finite semantic read facet.")] ScalesReadFacet facet,
            [Description("Live collection offset, 0..1000000.")] int offset = 0,
            [Description("Maximum entries per collection, 1..500.")] int maxResults = 100,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ScalesClient client = m_accessor.CreateClient(sessionName);
                NodeId target = ParseRequired(nodeId);
                if (!Enum.IsDefined(facet))
                {
                    throw new ArgumentOutOfRangeException(nameof(facet));
                }
                var result = new JsonObject
                {
                    ["nodeId"] = target.ToString(),
                    ["facet"] = facet.ToString(),
                    ["consistency"] = "live"
                };
                if (facet == ScalesReadFacet.Snapshot)
                {
                    foreach (ScalesReadFacet part in Enum.GetValues<ScalesReadFacet>())
                    {
                        if (part != ScalesReadFacet.Snapshot)
                        {
                            result[part.ToString()] = await ReadFacetAsync(
                                client, target, part, offset, maxResults, token).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    result["value"] = await ReadFacetAsync(
                        client, target, facet, offset, maxResults, token).ConfigureAwait(false);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Observes weights or notifications for a bounded duration and count.
        /// </summary>
        [McpServerTool(Name = "scales_observe", ReadOnly = true, Destructive = false)]
        [Description("Collect finite Weight changes or OPC 40200 Notifications (events and alarms). " +
            "Stops after durationMs (1..30000) or maxItems (1..500), reports why and releases its monitored items. " +
            "Requires a ManagedSession; borrows rather than disposes its default subscription. " +
            "Weight notifications contain gross/net/tare, timestamp and quality; the full property set and units " +
            "are in scales_read CurrentWeight, not the stream. Does not acknowledge alarms or alter a scale.")]
        public Task<CallToolResult> ObserveAsync(
            [Description("Scale, system or notifier NodeId.")] string nodeId,
            [Description("Weight or Notifications.")] ScalesObservationFacet facet,
            [Description("Observation duration, 1..30000 milliseconds.")] int durationMs = 1000,
            [Description("Maximum notifications, 1..500.")] int maxItems = 100,
            [Description("Named session, or the sole active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ScalesClient client = m_accessor.CreateClient(sessionName);
                NodeId target = ParseRequired(nodeId);
                var streaming = McpCompanionTools.GetStreaming(client.Session);
                return facet switch
                {
                    ScalesObservationFacet.Weight => await McpCompanionTools.ObserveAsync(
                        cancellation => client.ObserveWeightAsync(
                            target, streaming, cancellationToken: cancellation),
                        value => ScalesJson.Weight(value, client.Session.MessageContext),
                        durationMs, maxItems, token).ConfigureAwait(false),
                    ScalesObservationFacet.Notifications => await McpCompanionTools.ObserveAsync(
                        cancellation => client.ObserveNotificationsAsync(
                            target, streaming, cancellationToken: cancellation),
                        ScalesJson.Notification,
                        durationMs, maxItems, token).ConfigureAwait(false),
                    _ => throw new ArgumentOutOfRangeException(nameof(facet))
                };
            }, ct);
        }

        /// <summary>
        /// Parses an explicitly required non-null NodeId.
        /// </summary>
        internal static NodeId ParseRequired(string? nodeId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
            NodeId result = OpcUaJsonHelper.ParseNodeId(nodeId);
            return result.IsNull
                ? throw new ArgumentException("A non-null NodeId is required.", nameof(nodeId))
                : result;
        }

        /// <summary>
        /// Reads one of the closed companion facets through the existing client.
        /// </summary>
        private static async ValueTask<JsonNode?> ReadFacetAsync(
            ScalesClient client,
            NodeId target,
            ScalesReadFacet facet,
            int offset,
            int maxResults,
            CancellationToken ct)
        {
            IServiceMessageContext context = client.Session.MessageContext;
            return facet switch
            {
                ScalesReadFacet.Identification => ScalesJson.Identification(
                    await client.ReadIdentificationAsync(target, ct).ConfigureAwait(false)),
                ScalesReadFacet.CurrentWeight => ScalesJson.Weight(
                    await client.TryReadCurrentWeightAsync(target, ct).ConfigureAwait(false), context),
                ScalesReadFacet.RegisteredWeight => ScalesJson.Weight(
                    await client.ReadRegisteredWeightAsync(target, ct).ConfigureAwait(false), context),
                ScalesReadFacet.WeighingRanges => McpCompanionTools.Page(
                    await client.ReadWeighingRangesAsync(target, ct).ConfigureAwait(false),
                    value => ScalesJson.Range(value, context), offset, maxResults),
                ScalesReadFacet.AllowedEngineeringUnits => McpCompanionTools.Page(
                    await client.ReadAllowedEngineeringUnitsAsync(target, ct).ConfigureAwait(false),
                    value => ScalesJson.Units(value, context), offset, maxResults),
                ScalesReadFacet.Products => await McpCompanionTools.PageAsync(
                    ReadProductsAsync(client, target, ct), value => value, offset, maxResults, ct)
                    .ConfigureAwait(false),
                ScalesReadFacet.CurrentProducts => McpCompanionTools.Page(
                    await client.ReadCurrentProductsAsync(target, ct).ConfigureAwait(false),
                    value => JsonValue.Create(value), offset, maxResults),
                ScalesReadFacet.PackMLState => JsonValue.Create(
                    await client.ReadPackMLStateAsync(target, ct).ConfigureAwait(false)),
                _ => throw new ArgumentOutOfRangeException(nameof(facet))
            };
        }

        /// <summary>
        /// Adds the actual product lock service NodeId without acquiring it.
        /// </summary>
        private static async IAsyncEnumerable<JsonObject> ReadProductsAsync(
            ScalesClient client,
            NodeId target,
            [EnumeratorCancellation] CancellationToken ct)
        {
            ArrayOf<ScaleProductInfo> products = await client.ReadProductsAsync(target, ct).ConfigureAwait(false);
            for (int index = 0; index < products.Count; index++)
            {
                ScaleProductInfo product = products[index];
                NodeId lockId = await ResolveProductLockAsync(client, product.NodeId, ct).ConfigureAwait(false);
                yield return ScalesJson.Product(product, lockId);
            }
        }

        /// <summary>
        /// Resolves the standard DI Lock child not exposed by the Scales facade.
        /// </summary>
        private static async ValueTask<NodeId> ResolveProductLockAsync(
            ScalesClient client,
            NodeId productId,
            CancellationToken ct)
        {
            int index = client.Session.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi);
            if (index < 0)
            {
                return NodeId.Null;
            }
            TranslateBrowsePathsToNodeIdsResponse response = await client.Session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                [
                    new BrowsePath
                    {
                        StartingNode = productId,
                        RelativePath = new RelativePath
                        {
                            Elements =
                            [
                                new RelativePathElement
                                {
                                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                                    IncludeSubtypes = true,
                                    TargetName = new QualifiedName(Di.BrowseNames.Lock, (ushort)index)
                                }
                            ]
                        }
                    }
                ],
                ct).ConfigureAwait(false);
            if (response.Results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError, "Missing product Lock browse result.");
            }
            BrowsePathResult result = response.Results[0];
            if (result.StatusCode == StatusCodes.BadNoMatch)
            {
                return NodeId.Null;
            }
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            if (result.Targets.Count == 0)
            {
                return NodeId.Null;
            }
            if (result.Targets.Count != 1 ||
                result.Targets[0].RemainingPathIndex != uint.MaxValue ||
                result.Targets[0].TargetId.ServerIndex != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "The product Lock must resolve to one complete local target.");
            }
            return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, client.Session.NamespaceUris);
        }

        private readonly ScalesClientAccessor m_accessor;
    }
}
