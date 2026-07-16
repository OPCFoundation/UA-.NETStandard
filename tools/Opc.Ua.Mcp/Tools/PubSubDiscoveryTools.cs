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
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools that exercise OPC UA PubSub discovery (Part 14 §7.2.4.6):
    /// they send a discovery request from the active in-process PubSub runtime
    /// and collect the publisher responses.
    /// </summary>
    [McpServerToolType]
    public sealed class PubSubDiscoveryTools
    {
        /// <summary>
        /// Requests DataSetMetaData discovery from PubSub publishers.
        /// </summary>
        [McpServerTool(Name = "pubsub_discover_metadata")]
        [Description("Send a PubSub DataSetMetaData discovery request and collect publisher responses.")]
        public static async Task<PubSubDiscoverySummary> DiscoverMetaDataAsync(
            PubSubRuntimeManager manager,
            [Description("DataSetWriterIds to query; empty queries all.")] ushort[]? dataSetWriterIds = null,
            [Description("Collection window in milliseconds.")] int timeoutMs = 2000,
            CancellationToken ct = default)
        {
            PubSubDiscoveryResult result = await RequestAsync(
                manager, UadpDiscoveryType.DataSetMetaData, dataSetWriterIds, timeoutMs, ct)
                .ConfigureAwait(false);
            return Summarize(result);
        }

        /// <summary>
        /// Requests DataSetWriterConfiguration discovery from PubSub publishers.
        /// </summary>
        [McpServerTool(Name = "pubsub_discover_writer_config")]
        [Description("Send a PubSub DataSetWriterConfiguration discovery request and collect publisher responses.")]
        public static async Task<PubSubDiscoverySummary> DiscoverWriterConfigurationAsync(
            PubSubRuntimeManager manager,
            [Description("DataSetWriterIds to query; empty queries all.")] ushort[]? dataSetWriterIds = null,
            [Description("Collection window in milliseconds.")] int timeoutMs = 2000,
            CancellationToken ct = default)
        {
            PubSubDiscoveryResult result = await RequestAsync(
                manager, UadpDiscoveryType.DataSetWriterConfiguration, dataSetWriterIds, timeoutMs, ct)
                .ConfigureAwait(false);
            return Summarize(result);
        }

        /// <summary>
        /// Requests PublisherEndpoints discovery from PubSub publishers.
        /// </summary>
        [McpServerTool(Name = "pubsub_discover_publisher_endpoints")]
        [Description("Send a PubSub PublisherEndpoints discovery request and collect publisher responses.")]
        public static async Task<PubSubDiscoverySummary> DiscoverPublisherEndpointsAsync(
            PubSubRuntimeManager manager,
            [Description("Collection window in milliseconds.")] int timeoutMs = 2000,
            CancellationToken ct = default)
        {
            PubSubDiscoveryResult result = await RequestAsync(
                manager, UadpDiscoveryType.PublisherEndpoints, dataSetWriterIds: null, timeoutMs, ct)
                .ConfigureAwait(false);
            return Summarize(result);
        }

        private static async Task<PubSubDiscoveryResult> RequestAsync(
            PubSubRuntimeManager manager,
            UadpDiscoveryType discoveryType,
            ushort[]? dataSetWriterIds,
            int timeoutMs,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(manager);
            var request = new PubSubDiscoveryRequest
            {
                DiscoveryType = discoveryType,
                DataSetWriterIds = dataSetWriterIds is null ? [] : [.. dataSetWriterIds]
            };
            var timeout = TimeSpan.FromMilliseconds(timeoutMs <= 0 ? 2000 : timeoutMs);
            return await manager.RequestDiscoveryAsync(request, timeout, ct).ConfigureAwait(false);
        }

        private static PubSubDiscoverySummary Summarize(PubSubDiscoveryResult result)
        {
            var metaData = new PubSubDiscoveredMetaData[result.DataSetMetaDataEntries.Count];
            for (int i = 0; i < result.DataSetMetaDataEntries.Count; i++)
            {
                PubSubDataSetMetaDataDiscoveryResult entry = result.DataSetMetaDataEntries[i];
                metaData[i] = new PubSubDiscoveredMetaData(
                    entry.PublisherId.ToString(),
                    entry.WriterGroupId,
                    entry.DataSetWriterId,
                    entry.StatusCode.ToString(),
                    entry.DataSetMetaData?.Name,
                    (entry.DataSetMetaData?.Fields.Count) ?? 0);
            }

            var writerConfigs = new PubSubDiscoveredWriterConfig[result.WriterConfigurations.Count];
            for (int i = 0; i < result.WriterConfigurations.Count; i++)
            {
                PubSubDataSetWriterConfigurationDiscoveryResult entry = result.WriterConfigurations[i];
                writerConfigs[i] = new PubSubDiscoveredWriterConfig(
                    entry.PublisherId.ToString(),
                    entry.WriterGroupId,
                    entry.DataSetWriterIds,
                    entry.StatusCode.ToString());
            }

            string[] endpoints = new string[result.PublisherEndpoints.Count];
            for (int i = 0; i < result.PublisherEndpoints.Count; i++)
            {
                endpoints[i] = result.PublisherEndpoints[i].EndpointUrl ?? string.Empty;
            }

            return new PubSubDiscoverySummary([.. metaData], [.. writerConfigs], [.. endpoints]);
        }
    }

    /// <summary>
    /// One discovered DataSetMetaData entry.
    /// </summary>
    public sealed record PubSubDiscoveredMetaData(
        string PublisherId,
        ushort WriterGroupId,
        ushort DataSetWriterId,
        string StatusCode,
        string? Name,
        int FieldCount);

    /// <summary>
    /// One discovered DataSetWriterConfiguration entry.
    /// </summary>
    public sealed record PubSubDiscoveredWriterConfig(
        string PublisherId,
        ushort WriterGroupId,
        ArrayOf<ushort> DataSetWriterIds,
        string StatusCode);

    /// <summary>
    /// Aggregated PubSub discovery result returned to MCP callers.
    /// </summary>
    public sealed record PubSubDiscoverySummary(
        ArrayOf<PubSubDiscoveredMetaData> MetaData,
        ArrayOf<PubSubDiscoveredWriterConfig> WriterConfigurations,
        ArrayOf<string> PublisherEndpointUrls);
}
