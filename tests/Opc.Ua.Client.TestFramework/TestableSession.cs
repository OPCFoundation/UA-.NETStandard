/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Runtime.Serialization;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.TestFramework
{
    /// <summary>
    /// Namespaces
    /// </summary>
    public static class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUaClient namespace (.NET code namespace is 'Opc.Ua.Client').
        /// </summary>
        public const string OpcUaClient = "http://opcfoundation.org/UA/Client/Types.xsd";
    }

    /// <summary>
    /// A subclass of a session for testing purposes, e.g. to override some implementations.
    /// </summary>
    public class TestableSession : Session
    {
        /// <inheritdoc/>
        public TestableSession(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            Certificate clientCertificate,
            ArrayOf<EndpointDescription> availableEndpoints = default,
            ArrayOf<string> discoveryProfileUris = default,
            ISubscriptionEngineFactory? engineFactory = null)
            : base(
                channel,
                configuration,
                endpoint,
                clientCertificate,
                null,
                availableEndpoints,
                discoveryProfileUris,
                engineFactory)
        {
        }

        /// <inheritdoc/>
        public TestableSession(
            ITransportChannel channel,
            Session template,
            bool copyEventHandlers)
            : base(
                  channel,
                  template,
                  copyEventHandlers)
        {
        }

        /// <summary>
        /// The timespan offset to be used to modify the request header timestamp.
        /// </summary>
        [DataMember]
        public TimeSpan TimestampOffset { get; set; } = new TimeSpan(0);

        /// <inheritdoc/>
        protected override void UpdateRequestHeader(
            IServiceRequest request,
            bool useDefaults,
            string serviceName)
        {
            base.UpdateRequestHeader(request, useDefaults, serviceName);
            request.RequestHeader.Timestamp += TimestampOffset;
        }

        /// <inheritdoc/>
        public override Session CloneSession(ITransportChannel channel, bool copyEventHandlers)
        {
            return new TestableSession(channel, this, copyEventHandlers)
            {
                TimestampOffset = TimestampOffset
            };
        }

        /// <inheritdoc/>
        protected override Subscription CreateSubscription(SubscriptionOptions? options)
        {
            return new TestableSubscription(MessageContext.Telemetry, options);
        }

        /// <inheritdoc/>
        public override void Snapshot(out SessionState state)
        {
            base.Snapshot(out state);
            state = new TestableSessionState(state)
            {
                TimestampOffset = TimestampOffset
            };
        }

        /// <inheritdoc/>
        public override void Restore(SessionState state)
        {
            if (state is TestableSessionState s)
            {
                TimestampOffset = s.TimestampOffset;
            }
            base.Restore(state);
        }
    }

    /// <summary>
    /// Testable session state
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaClient)]
    [KnownType(typeof(MonitoredItemState))]
    [KnownType(typeof(SubscriptionState))]
    public record class TestableSessionState : SessionState
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public TestableSessionState()
        {
        }

        /// <summary>
        /// Create a new instance of the <see cref="TestableSessionState"/> class.
        /// </summary>
        /// <param name="state"></param>
        public TestableSessionState(SessionState state)
            : base(state)
        {
        }

        /// <summary>
        /// The timespan offset to be used to modify the request header timestamp.
        /// </summary>
        [DataMember]
        public TimeSpan TimestampOffset { get; set; } = new TimeSpan(0);
    }

    /// <summary>
    /// A subclass of the subscription for testing purposes.
    /// </summary>
    public class TestableSubscription : Subscription
    {
        /// <summary>
        /// Constructs a new instance of the <see cref="TestableSubscription"/> class.
        /// </summary>
        public TestableSubscription(ITelemetryContext telemetry, SubscriptionOptions? options = null)
            : base(telemetry, options)
        {
        }

        /// <summary>
        /// Constructs a new instance of the <see cref="TestableSubscription"/> class.
        /// </summary>
        public TestableSubscription(Subscription template, bool copyEventHandlers = false)
            : base(template, copyEventHandlers)
        {
        }

        /// <inheritdoc/>
        public override Subscription CloneSubscription(bool copyEventHandlers)
        {
            return new TestableSubscription(this, copyEventHandlers);
        }

        /// <inheritdoc/>
        protected override MonitoredItem CreateMonitoredItem(MonitoredItemOptions? options)
        {
            return new TestableMonitoredItem(Telemetry!, options);
        }
    }

    /// <summary>
    /// A subclass of a monitored item for testing purposes.
    /// </summary>
    public class TestableMonitoredItem : MonitoredItem
    {
        /// <summary>
        /// Constructs a new instance of the <see cref="TestableMonitoredItem"/> class.
        /// </summary>
        public TestableMonitoredItem(ITelemetryContext telemetry, MonitoredItemOptions? options = null)
            : base(telemetry, options)
        {
        }

        /// <summary>
        /// Constructs a new instance of the <see cref="TestableMonitoredItem"/> class.
        /// </summary>
        public TestableMonitoredItem(MonitoredItem template)
            : this(template, false, false)
        {
        }

        /// <summary>
        /// Constructs a new instance of the <see cref="TestableMonitoredItem"/> class.
        /// </summary>
        public TestableMonitoredItem(
            MonitoredItem template,
            bool copyEventHandlers,
            bool copyClientHandle)
            : base(template, copyEventHandlers, copyClientHandle)
        {
        }

        /// <inheritdoc/>
        public override MonitoredItem CloneMonitoredItem(
            bool copyEventHandlers,
            bool copyClientHandle)
        {
            return new TestableMonitoredItem(this, copyEventHandlers, copyClientHandle);
        }
    }
}
