/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
    [JsonSerializable(typeof(SubscriptionOptions))]
    [JsonSerializable(typeof(SubscriptionState))]
    internal partial class SubscriptionOptionsContext : JsonSerializerContext;

    /// <summary>
    /// Serializable options for a subscription.
    /// <para>
    /// These client side options correspond to parameters used with the
    /// CreateSubscription / ModifySubscription and related services in the
    /// OPC UA Subscription Service Set. See OPC UA Part4 v1.05 Sections 5.13
    /// and 5.14: https://reference.opcfoundation.org/Core/Part4/v105/docs/5.13
    /// and https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.
    /// </para>
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public record class SubscriptionOptions
    {
        /// <summary>
        /// A human readable display name for the subscription instance used
        /// locally by the client for logging, diagnostics and UI. This value
        /// is not sent to the server as part of the service parameters;
        /// it is purely client side metadata. Choose a name that helps
        /// operators identify the purpose of the subscription (e.g.
        /// "ProcessValues", "Alarms").
        /// </summary>
        [DataMember(Order = 1)]
        public string DisplayName { get; init; } = "Subscription";

        /// <summary>
        /// Requested <c>publishingInterval</c> (ms) sent in CreateSubscription.
        /// It defines the cyclic interval at which the server evaluates the
        /// subscription for notifications and prepares the next Publish response.
        /// The server may revise this value; the effective revised value is
        /// available via the Subscription object after creation. A value &lt;=0
        /// falls back to the server default. Selecting too small an interval
        /// can increase CPU and network load.
        /// </summary>
        [DataMember(Order = 2)]
        public int PublishingInterval { get; init; }

        /// <summary>
        /// Requested <c>keepAliveCount</c> (number of publishing intervals)
        /// determining how many consecutive publishing cycles may pass with no
        /// notifications before the server sends a KeepAlive Publish response.
        /// Lower values mean the client gets more frequent confirmation that the
        /// subscription is alive even in quiet periods. The server may revise this.
        /// </summary>
        [DataMember(Order = 3)]
        public uint KeepAliveCount { get; init; }

        /// <summary>
        /// Requested <c>lifetimeCount</c> (number of publishing intervals)
        /// used by the server to detect client inactivity. If the server does
        /// not receive Publish requests for lifetimeCount consecutive intervals
        /// the subscription may be terminated. Must be larger than keepAliveCount
        /// (spec recommends at least3x). The server may revise this value.
        /// </summary>
        [DataMember(Order = 4)]
        public uint LifetimeCount { get; init; }

        /// <summary>
        /// Requested <c>maxNotificationsPerPublish</c> limiting the number
        /// of notifications returned in a single Publish response to prevent
        /// oversized messages.0 means server default (usually unlimited or some
        /// internal cap). The server may revise. Tune this to balance latency versus
        /// network packet size and memory consumption. Large bursts get split
        /// across multiple Publish responses if necessary.
        /// </summary>
        [DataMember(Order = 5)]
        public uint MaxNotificationsPerPublish { get; init; }

        /// <summary>
        /// Requested <c>publishingEnabled</c> state. If false the server creates
        /// the subscription but does not send notifications until it is later
        /// enabled with ModifySubscription or SetPublishingMode. Useful for
        /// staging monitored items.
        /// </summary>
        [DataMember(Order = 6)]
        public bool PublishingEnabled { get; init; }

        /// <summary>
        /// Requested <c>priority</c> hint allowing the server to schedule higher
        /// priority subscriptions first when resources are constrained. OPC UA
        /// servers may use this value as a relative ranking (higher = more important)
        /// but are not required to strictly enforce ordering.
        /// </summary>
        [DataMember(Order = 7)]
        public byte Priority { get; init; }

        /// <summary>
        /// Which timestamps (Source / Server / Both / Neither) the client
        /// requests in notifications. This maps to the <c>TimestampsToReturn</c> enum
        /// used in the MonitoredItem and Read services. The default of Both provides
        /// maximum context at the cost of a few extra bytes.
        /// </summary>
        [DataMember(Order = 8)]
        public TimestampsToReturn TimestampsToReturn { get; init; } = TimestampsToReturn.Both;

        /// <summary>
        /// Maximum number of Publish responses cached locally by the client for late
        /// processing or sequence gap recovery. This is a client side buffering control
        /// (not part of wire services). Larger values allow more tolerance to temporary
        /// processing delays; smaller values reduce memory usage.
        /// </summary>
        [DataMember(Order = 9)]
        public int MaxMessageCount { get; init; } = 10;

        /// <summary>
        /// A client side min interval (ms) used to derive a safe <c>lifetimeCount</c>
        /// when constructing subscription requests. Helps enforce policy such that
        /// lifetimeCount * publishingInterval is not below an application defined
        /// minimum lifetime.0 means disabled.
        /// </summary>
        [DataMember(Order = 12)]
        public uint MinLifetimeInterval { get; init; }

        /// <summary>
        /// When true the client disables its per-monitored-item value cache,
        /// potentially improving throughput for high frequency data changes at the
        /// cost of losing last-value lookups without application tracking. Use for
        /// streaming scenarios where each notification is processed once then discarded.
        /// </summary>
        [DataMember(Order = 13)]
        public bool DisableMonitoredItemCache { get; init; }

        /// <summary>
        /// When true incoming Publish responses are processed strictly sequentially
        /// in the order of their sequence numbers, even if multiple arrive concurrently.
        /// This can simplify application logic for ordering dependent processing
        /// (e.g. aggregate calculations) at the cost of reduced parallelism.
        /// </summary>
        [DataMember(Order = 14)]
        public bool SequentialPublishing { get; init; }

        /// <summary>
        /// When true the client will automatically issue Republish requests
        /// after a TransferSubscription or other recovery scenario to obtain any
        /// available lost sequence numbers and minimize data loss. This automates
        /// the gap recovery behavior defined for the Republish service.
        /// </summary>
        [DataMember(Name = "RepublishAfterTransfer", Order = 15)]
        public bool RepublishAfterTransfer { get; init; }

        /// <summary>
        /// The transferable subscription identifier (server assigned) used to
        /// reattach a subscription to a new session via TransferSubscriptions.
        /// 0 indicates no server transfer id known or the subscription is not
        /// currently transferable. Persisting this value allows restoring behavior
        /// after reconnect.
        /// </summary>
        [DataMember(Name = "TransferId", Order = 16)]
        public uint TransferId { get; init; }
    }
}
