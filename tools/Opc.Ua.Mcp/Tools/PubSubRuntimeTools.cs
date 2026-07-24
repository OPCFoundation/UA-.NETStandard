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

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for an in-process OPC UA PubSub publisher or subscriber.
    /// </summary>
    [McpServerToolType]
    public sealed class PubSubRuntimeTools
    {
        /// <summary>
        /// Starts an in-process UDP/UADP publisher.
        /// </summary>
        [McpServerTool(Name = "pubsub_runtime_start_publisher")]
        [Description("Starts an in-process OPC UA PubSub UDP/UADP publisher. fieldSpec uses name:type pairs. " +
            "Returns a PubSubRuntimeStatus describing the running publisher (isRunning, mode, endpoint, " +
            "publisherId, writerGroupId).")]
        public static async Task<PubSubRuntimeStatus> StartPublisherAsync(
            PubSubRuntimeManager manager,
            [Description("UDP endpoint URL, for example opc.udp://239.0.0.1:4840")] string udpUrl,
            [Description("PublisherId to place in NetworkMessage headers")] ushort publisherId,
            [Description("WriterGroupId to place in NetworkMessage group headers")] ushort writerGroupId,
            [Description("Optional DataSet field spec as name:type pairs separated by ';'")] string? fieldSpec = null,
            CancellationToken ct = default)
        {
            return await manager.StartPublisherAsync(
                udpUrl,
                publisherId,
                writerGroupId,
                fieldSpec,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts an in-process UDP/UADP subscriber.
        /// </summary>
        [McpServerTool(Name = "pubsub_runtime_start_subscriber")]
        [Description("Starts an in-process OPC UA PubSub UDP/UADP subscriber and buffers received DataSets for " +
            "later retrieval with pubsub_runtime_read_received. Returns a PubSubRuntimeStatus describing the " +
            "running subscriber (isRunning, mode, endpoint, bufferedDataSetCount).")]
        public static async Task<PubSubRuntimeStatus> StartSubscriberAsync(
            PubSubRuntimeManager manager,
            [Description("UDP endpoint URL, for example opc.udp://239.0.0.1:4840")] string udpUrl,
            [Description("PublisherId filter")] ushort publisherId,
            [Description("WriterGroupId filter")] ushort writerGroupId,
            [Description("Optional DataSet field spec as name:type pairs separated by ';'")] string? fieldSpec = null,
            CancellationToken ct = default)
        {
            return await manager.StartSubscriberAsync(
                udpUrl,
                publisherId,
                writerGroupId,
                fieldSpec,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the active publisher's DataSet fields.
        /// </summary>
        [McpServerTool(Name = "pubsub_runtime_publish")]
        [Description("Updates active publisher fields. Use JSON object text or name=value pairs separated by ';'. " +
            "Returns a PubSubRuntimePublishResult describing the fields that were published.")]
        public static async Task<PubSubRuntimePublishResult> PublishAsync(
            PubSubRuntimeManager manager,
            [Description("Field values as JSON object text or name=value pairs")] string fieldValues,
            CancellationToken ct = default)
        {
            return await manager.PublishAsync(fieldValues, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads buffered DataSets from the active subscriber.
        /// </summary>
        [McpServerTool(Name = "pubsub_runtime_read_received")]
        [Description("Reads the DataSets received and buffered by the active in-process PubSub subscriber since " +
            "the last read (set clear=true to also empty the buffer afterward). Returns an array of " +
            "PubSubReceivedDataSet; empty if no subscriber is running or nothing has been received yet.")]
        public static async Task<ArrayOf<PubSubReceivedDataSet>> ReadReceivedAsync(
            PubSubRuntimeManager manager,
            [Description("Clear the receive buffer after reading")] bool clear = false,
            CancellationToken ct = default)
        {
            return await manager.ReadReceivedAsync(clear, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reports the in-process PubSub runtime status.
        /// </summary>
        [McpServerTool(Name = "pubsub_runtime_status")]
        [Description("Reports whether the in-process PubSub runtime is running as a publisher or subscriber. " +
            "Returns a PubSubRuntimeStatus (isRunning, mode: 'Publisher'/'Subscriber', endpoint, and related fields).")]
        public static async Task<PubSubRuntimeStatus> StatusAsync(
            PubSubRuntimeManager manager,
            CancellationToken ct = default)
        {
            return await manager.StatusAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the in-process PubSub runtime.
        /// </summary>
        [McpServerTool(Name = "pubsub_runtime_stop")]
        [Description("Stops and disposes the active in-process PubSub publisher or subscriber. Returns a " +
            "PubSubRuntimeStatus with isRunning:false.")]
        public static async Task<PubSubRuntimeStatus> StopAsync(
            PubSubRuntimeManager manager,
            CancellationToken ct = default)
        {
            return await manager.StopAsync(ct).ConfigureAwait(false);
        }
    }
}
