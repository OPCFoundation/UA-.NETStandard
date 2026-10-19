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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Context supplied before the stack creates a data-change monitored item.
    /// </summary>
    public sealed class MonitoredItemCreateContext
    {
        /// <summary>
        /// Captures the request and subscription settings available to a monitored-item creation decision.
        /// </summary>
        /// <param name="systemContext">The server context used to create the monitored item.</param>
        /// <param name="handle">The node handle identifying the monitored-item source.</param>
        /// <param name="subscriptionId">The identifier of the owning subscription.</param>
        /// <param name="publishingInterval">The subscription publishing interval in milliseconds.</param>
        /// <param name="diagnosticsMasks">The diagnostics requested for monitored-item creation.</param>
        /// <param name="timestampsToReturn">The timestamp policy requested for monitored values.</param>
        /// <param name="request">The monitored-item creation request.</param>
        /// <param name="createDurable">Whether the monitored item belongs to a durable subscription.</param>
        internal MonitoredItemCreateContext(
            ServerSystemContext systemContext,
            NodeHandle handle,
            uint subscriptionId,
            double publishingInterval,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequest request,
            bool createDurable)
        {
            SystemContext = systemContext;
            Handle = handle;
            SubscriptionId = subscriptionId;
            PublishingInterval = publishingInterval;
            DiagnosticsMasks = diagnosticsMasks;
            TimestampsToReturn = timestampsToReturn;
            Request = request;
            CreateDurable = createDurable;
        }

        /// <summary>
        /// The system context for the create request.
        /// </summary>
        public ServerSystemContext SystemContext { get; }

        /// <summary>
        /// The validated node handle.
        /// </summary>
        public NodeHandle Handle { get; }

        /// <summary>
        /// The source node.
        /// </summary>
        public NodeState Source => Handle.Node;

        /// <summary>
        /// The subscription id.
        /// </summary>
        public uint SubscriptionId { get; }

        /// <summary>
        /// The subscription publishing interval.
        /// </summary>
        public double PublishingInterval { get; }

        /// <summary>
        /// The diagnostics mask.
        /// </summary>
        public DiagnosticsMasks DiagnosticsMasks { get; }

        /// <summary>
        /// The requested timestamps.
        /// </summary>
        public TimestampsToReturn TimestampsToReturn { get; }

        /// <summary>
        /// The original create request.
        /// </summary>
        public MonitoredItemCreateRequest Request { get; }

        /// <summary>
        /// Whether the item belongs to a durable subscription.
        /// </summary>
        public bool CreateDurable { get; }
    }

    /// <summary>
    /// Context supplied to a custom monitored-item factory after the stack
    /// revises and validates the request.
    /// </summary>
    public sealed class MonitoredItemFactoryContext
    {
        /// <summary>
        /// Captures validated settings and server resources for constructing a custom monitored item.
        /// </summary>
        /// <param name="server">The server providing monitored-item services.</param>
        /// <param name="nodeManager">The node manager associated with the operation.</param>
        /// <param name="systemContext">The server context used to create the monitored item.</param>
        /// <param name="handle">The node handle identifying the monitored-item source.</param>
        /// <param name="subscriptionId">The identifier of the owning subscription.</param>
        /// <param name="monitoredItemId">The identifier of the affected monitored item.</param>
        /// <param name="publishingInterval">The subscription publishing interval in milliseconds.</param>
        /// <param name="diagnosticsMasks">The diagnostics requested for monitored-item creation.</param>
        /// <param name="timestampsToReturn">The timestamp policy requested for monitored values.</param>
        /// <param name="request">The monitored-item creation request.</param>
        /// <param name="euRange">The engineering-unit range used when evaluating the monitored-item filter.</param>
        /// <param name="filter">The validated monitoring filter.</param>
        /// <param name="samplingInterval">The revised sampling interval in milliseconds.</param>
        /// <param name="queueSize">The revised monitored-item queue size.</param>
        /// <param name="createDurable">Whether the monitored item belongs to a durable subscription.</param>
        internal MonitoredItemFactoryContext(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            ServerSystemContext systemContext,
            NodeHandle handle,
            uint subscriptionId,
            uint monitoredItemId,
            double publishingInterval,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequest request,
            Range euRange,
            MonitoringFilter filter,
            double samplingInterval,
            uint queueSize,
            bool createDurable)
        {
            Server = server;
            NodeManager = nodeManager;
            SystemContext = systemContext;
            Handle = handle;
            SubscriptionId = subscriptionId;
            MonitoredItemId = monitoredItemId;
            PublishingInterval = publishingInterval;
            DiagnosticsMasks = diagnosticsMasks;
            TimestampsToReturn = timestampsToReturn;
            Request = request;
            EuRange = euRange;
            Filter = filter;
            SamplingInterval = samplingInterval;
            QueueSize = queueSize;
            CreateDurable = createDurable;
        }

        /// <summary>
        /// The owning server.
        /// </summary>
        public IServerInternal Server { get; }

        /// <summary>
        /// The owning node manager.
        /// </summary>
        public IAsyncNodeManager NodeManager { get; }

        /// <summary>
        /// The system context for the create request.
        /// </summary>
        public ServerSystemContext SystemContext { get; }

        /// <summary>
        /// The validated node handle.
        /// </summary>
        public NodeHandle Handle { get; }

        /// <summary>
        /// The source node.
        /// </summary>
        public NodeState Source => Handle.Node;

        /// <summary>
        /// The subscription id.
        /// </summary>
        public uint SubscriptionId { get; }

        /// <summary>
        /// The stack-allocated monitored-item id.
        /// </summary>
        public uint MonitoredItemId { get; }

        /// <summary>
        /// The subscription publishing interval.
        /// </summary>
        public double PublishingInterval { get; }

        /// <summary>
        /// The diagnostics mask.
        /// </summary>
        public DiagnosticsMasks DiagnosticsMasks { get; }

        /// <summary>
        /// The requested timestamps.
        /// </summary>
        public TimestampsToReturn TimestampsToReturn { get; }

        /// <summary>
        /// The original create request.
        /// </summary>
        public MonitoredItemCreateRequest Request { get; }

        /// <summary>
        /// The validated engineering-unit range.
        /// </summary>
        public Range EuRange { get; }

        /// <summary>
        /// The validated filter.
        /// </summary>
        public MonitoringFilter Filter { get; }

        /// <summary>
        /// The revised sampling interval.
        /// </summary>
        public double SamplingInterval { get; }

        /// <summary>
        /// The revised queue size.
        /// </summary>
        public uint QueueSize { get; }

        /// <summary>
        /// Whether the item belongs to a durable subscription.
        /// </summary>
        public bool CreateDurable { get; }

        /// <summary>
        /// Creates a standard monitored item whose values are supplied by its
        /// external source rather than by reading the monitored Node.
        /// </summary>
        /// <remarks>
        /// The source must queue its initial value and resume delivery when the
        /// item is re-enabled. Node changes and sampling groups do not read the
        /// Node's value on behalf of this item. Use this factory with
        /// <see cref="MonitoredItemCreateDecision.Use"/> and
        /// <c>queueInitialValue: false</c>.
        /// </remarks>
        public MonitoredItem CreatePushMonitoredItem()
        {
            return new MonitoredItem(
                Server,
                NodeManager,
                Handle,
                SubscriptionId,
                MonitoredItemId,
                Request.ItemToMonitor,
                DiagnosticsMasks,
                TimestampsToReturn,
                Request.MonitoringMode,
                Request.RequestedParameters.ClientHandle,
                Filter,
                Filter,
                EuRange,
                SamplingInterval,
                QueueSize,
                Request.RequestedParameters.DiscardOldest,
                sourceSamplingInterval: 0,
                CreateDurable)
            {
                UsesExternalValueSource = true
            };
        }
    }

    /// <summary>
    /// Creates a custom sampled data-change monitored item.
    /// </summary>
    public delegate ISampledDataChangeMonitoredItem MonitoredItemFactory(
        MonitoredItemFactoryContext context);

    /// <summary>
    /// Describes how the stack should handle a monitored-item create request.
    /// </summary>
    public sealed class MonitoredItemCreateDecision
    {
        private MonitoredItemCreateDecision(
            MonitoredItemCreateDecisionKind kind,
            ServiceResult? error,
            MonitoredItemFactory? factory,
            bool queueInitialValue)
        {
            Kind = kind;
            Error = error;
            Factory = factory;
            QueueInitialValue = queueInitialValue;
        }

        /// <summary>
        /// Uses the stack's default sampled monitored item.
        /// </summary>
        public static MonitoredItemCreateDecision UseDefault()
        {
            return s_default;
        }

        /// <summary>
        /// Refuses the request with <paramref name="error"/>.
        /// </summary>
        public static MonitoredItemCreateDecision Refuse(ServiceResult error)
        {
            return new MonitoredItemCreateDecision(
                MonitoredItemCreateDecisionKind.Refuse,
                error ?? throw new ArgumentNullException(nameof(error)),
                null,
                false);
        }

        /// <summary>
        /// Uses a custom item created and registered by the stack.
        /// </summary>
        public static MonitoredItemCreateDecision Use(
            MonitoredItemFactory factory,
            bool queueInitialValue = false)
        {
            return new MonitoredItemCreateDecision(
                MonitoredItemCreateDecisionKind.Custom,
                null,
                factory ?? throw new ArgumentNullException(nameof(factory)),
                queueInitialValue);
        }

        /// <summary>
        /// Gets whether creation uses the default factory, is refused, or uses a custom factory.
        /// </summary>
        internal MonitoredItemCreateDecisionKind Kind { get; }

        /// <summary>
        /// Gets the error returned when monitored-item creation is refused.
        /// </summary>
        internal ServiceResult? Error { get; }

        /// <summary>
        /// Gets the custom monitored-item factory selected by the decision.
        /// </summary>
        internal MonitoredItemFactory? Factory { get; }

        /// <summary>
        /// Gets whether the custom item should receive an initial value through the stack lifecycle.
        /// </summary>
        internal bool QueueInitialValue { get; }

        private static readonly MonitoredItemCreateDecision s_default = new(
            MonitoredItemCreateDecisionKind.Default,
            null,
            null,
            false);
    }

    /// <summary>
    /// Identifies the action selected by a monitored-item creation decision.
    /// </summary>
    internal enum MonitoredItemCreateDecisionKind
    {
        /// <summary>
        /// Uses the stack's default monitored-item creation path.
        /// </summary>
        Default,

        /// <summary>
        /// Refuses monitored-item creation with the supplied error.
        /// </summary>
        Refuse,

        /// <summary>
        /// Creates the monitored item through a custom factory.
        /// </summary>
        Custom
    }
}
