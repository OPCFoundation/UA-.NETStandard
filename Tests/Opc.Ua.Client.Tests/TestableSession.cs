/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Client.Tests
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
            X509Certificate2 clientCertificate,
            EndpointDescriptionCollection availableEndpoints = null,
            StringCollection discoveryProfileUris = null)
            : base(
                channel,
                configuration,
                endpoint,
                clientCertificate,
                null,
                availableEndpoints,
                discoveryProfileUris)
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
        protected override Subscription CreateSubscription(SubscriptionOptions options)
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
        public TestableSubscription(ITelemetryContext telemetry, SubscriptionOptions options = null)
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
        protected override MonitoredItem CreateMonitoredItem(MonitoredItemOptions options)
        {
            return new TestableMonitoredItem(Telemetry, options);
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
        public TestableMonitoredItem(ITelemetryContext telemetry, MonitoredItemOptions options = null)
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
