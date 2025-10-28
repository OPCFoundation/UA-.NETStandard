/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Opc.Ua.Client
{
    [JsonSerializable(typeof(MonitoredItemOptions))]
    [JsonSerializable(typeof(MonitoredItemState))]
    internal partial class MonitoredItemOptionsContext : JsonSerializerContext;

    /// <summary>
    /// Serializable options for a client monitored item.
    /// <para>
    /// These options map to parameters used when creating and modifying monitored
    /// items within a subscription. See OPC UA Part4 v1.05 Sections 5.13 and 5.14
    /// (Subscription / MonitoredItem Service Sets):
    /// https://reference.opcfoundation.org/Core/Part4/v105/docs/5.13 and
    /// https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.
    /// </para>
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    [KnownType(typeof(DataChangeFilter))]
    [KnownType(typeof(EventFilter))]
    [KnownType(typeof(AggregateFilter))]
    public record class MonitoredItemOptions
    {
        /// <summary>
        /// Local human readable display name used by the client for logging and
        /// diagnostics of the monitored item. This is not the DisplayName attribute
        /// of the target node and is not sent in the CreateMonitoredItems request.
        /// Choose a name that reflects the business purpose (e.g. "Tank1.Level").
        /// <para>Spec Context: Client side metadata.</para>
        /// <para>Reference: Part4 Section5.13.</para>
        /// </summary>
        [DataMember(Order = 1)]
        public string DisplayName { get; init; } = "MonitoredItem";

        /// <summary>
        /// Starting <c>NodeId</c> used with <c>RelativePath</c> to resolve the
        /// target node. If <c>RelativePath</c> is null the StartNodeId is the node
        /// directly monitored. This corresponds to the <c>nodeId</c> when constructed
        /// after path resolution. <c>NodeId.Null</c> indicates it is not set yet.
        /// <para>Spec: ReadValueId.nodeId.</para>
        /// <para>Reference: Part4 Section5.13.</para>
        /// </summary>
        [DataMember(Order = 2)]
        public NodeId StartNodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// A relative browse path (client side string form) from <c>StartNodeId</c>
        /// to the final target node or attribute to monitor. When supplied the client
        /// resolves it to a NodeId using Browse / TranslateBrowsePaths services before
        /// creating the monitored item. Null means no path; monitor the StartNodeId
        /// directly.
        /// </summary>
        [DataMember(Order = 3)]
        public string? RelativePath { get; init; }

        /// <summary>
        /// The expected NodeClass of the target node (Variable, Object, etc.).
        /// Primarily used for client validation after resolving the NodeId.
        /// Monitoring Variables yields DataChange notifications; monitoring Events
        /// uses Object / View and an EventFilter. Ensuring the correct NodeClass
        /// helps avoid invalid monitored item creation requests.
        /// </summary>
        [DataMember(Order = 4)]
        public NodeClass NodeClass { get; init; } = NodeClass.Variable;

        /// <summary>
        /// The AttributeId to monitor on the target node. For data changes this
        /// is typically <c>Attributes.Value</c>; for Events the attribute may be
        /// <c>Attributes.EventNotifier</c>; for other use cases StatusCode or Timestamp
        /// attributes may be monitored. Maps to <c>ReadValueId.AttributeId</c> in
        /// the service request.
        /// </summary>
        [DataMember(Order = 5)]
        public uint AttributeId { get; init; } = Attributes.Value;

        /// <summary>
        /// IndexRange selecting a subset of an array or matrix value (e.g. "0:9"
        /// for first10 elements). If null the entire value is monitored. This
        /// corresponds to <c>ReadValueId.indexRange</c> and is applied by the
        /// server when generating notifications, reducing bandwidth for large arrays.
        /// </summary>
        [DataMember(Order = 6)]
        public string? IndexRange { get; init; }

        /// <summary>
        /// Requested data encoding (QualifiedName) for complex values (e.g.
        /// specific DataTypeEncoding). This maps to <c>ReadValueId.dataEncoding</c>.
        /// Use <c>QualifiedName.Null</c> for default encoding. Ensures notifications
        /// are serialized in a form understood by the client.
        /// </summary>
        [DataMember(Order = 7)]
        public QualifiedName Encoding { get; init; } = QualifiedName.Null;

        /// <summary>
        /// Requested <c>MonitoringMode</c> for the item: Disabled (no sampling),
        /// Sampling (server samples but does not queue notifications), Reporting
        /// (samples and queues notifications for Publish). This value may be changed
        /// later via SetMonitoringMode. Default is Reporting for typical data
        /// collection.
        /// </summary>
        [DataMember(Order = 8)]
        public MonitoringMode MonitoringMode { get; init; } = MonitoringMode.Reporting;

        /// <summary>
        /// Requested <c>samplingInterval</c> (ms) for the server's data sampling
        /// of the underlying value. A value of -1 requests the server default.
        /// The actual revised interval may differ; use the created MonitoredItem's
        /// server-revised value for final timing. Very small intervals increase
        /// load; very large intervals reduce data freshness.
        /// </summary>
        [DataMember(Order = 9)]
        public int SamplingInterval { get; init; } = -1;

        /// <summary>
        /// Optional server side filter controlling which data changes or events
        /// generate notifications. For Variables use <c>DataChangeFilter</c> or
        /// <c>AggregateFilter</c>; for Events use <c>EventFilter</c>. Null means
        /// no additional filtering beyond sampling and queueing. Proper filters
        /// reduce bandwidth and client processing.
        /// <para>Spec: MonitoringFilter types (DataChangeFilter, EventFilter,
        /// AggregateFilter).</para>
        /// <para>Reference: Part4 Section5.13.</para>
        /// </summary>
        [DataMember(Order = 10)]
        public MonitoringFilter? Filter { get; init; }

        /// <summary>
        /// Requested <c>queueSize</c> specifying the maximum number of notifications
        /// the server retains for this item between Publish responses.0 (or 1
        /// depending on server) means minimal queue.
        /// Larger queues reduce risk of data loss during short client delays but
        /// increase memory usage. Maps to <c>requestedParameters.queueSize</c>.
        /// The server may revise.
        /// </summary>
        [DataMember(Order = 11)]
        public uint QueueSize { get; init; } = 0;

        /// <summary>
        /// <c>discardOldest</c> policy: if true the server discards the oldest
        /// entry when the queue is full and a new notification arrives; if false
        /// it discards the newest notification. The usual recommendation is true
        /// for streaming latest-value scenarios; false preserves the earliest
        /// samples for batch integrity.
        /// </summary>
        [DataMember(Order = 12)]
        public bool DiscardOldest { get; init; } = true;
    }
}
