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

        internal MonitoredItemCreateDecisionKind Kind { get; }

        internal ServiceResult? Error { get; }

        internal MonitoredItemFactory? Factory { get; }

        internal bool QueueInitialValue { get; }

        private static readonly MonitoredItemCreateDecision s_default = new(
            MonitoredItemCreateDecisionKind.Default,
            null,
            null,
            false);
    }

    internal enum MonitoredItemCreateDecisionKind
    {
        Default,
        Refuse,
        Custom
    }
}
