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

// UdpDiscoveryPublisher and UdpPubSubConnection are part of the legacy
// 1.04 PubSub stack. Suppress the obsolete-API diagnostic in this file.
#pragma warning disable UA0023
#pragma warning disable CS0618

using System;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transports
{
    /// <summary>
    /// Unit tests for <see cref="UdpDiscoveryPublisher"/>: covers the
    /// in-memory construction path, delegate-property assignment, and
    /// the kMinimumResponseInterval constant — all without opening any
    /// real UDP sockets.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class UdpDiscoveryPublisherTests
    {
        [Test]
        public void Constructor_CreatesInstanceWithoutThrowingOrOpeningSockets()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoveryPublisher publisher = NewPublisher(app);
            Assert.That(publisher, Is.Not.Null);
        }

        [Test]
        public void Constructor_WithExplicitTimeProvider_DoesNotThrow()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "udp-pub-timeprovider",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.1:4840"
                })
            };
            var conn = new UdpPubSubConnection(app, connCfg, telemetry);

            // Pass an explicit TimeProvider — should not throw
            UdpDiscoveryPublisher publisher =
                new UdpDiscoveryPublisher(conn, telemetry, TimeProvider.System);

            Assert.That(publisher, Is.Not.Null);
        }

        [Test]
        public void GetPublisherEndpoints_DefaultIsNull()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoveryPublisher publisher = NewPublisher(app);

            Assert.That(publisher.GetPublisherEndpoints, Is.Null);
        }

        [Test]
        public void GetPublisherEndpoints_CanBeAssigned()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoveryPublisher publisher = NewPublisher(app);

            GetPublisherEndpointsEventHandler handler = () => [];
            publisher.GetPublisherEndpoints = handler;

            Assert.That(publisher.GetPublisherEndpoints, Is.SameAs(handler));
        }

        [Test]
        public void GetPublisherEndpoints_CanBeReassigned()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoveryPublisher publisher = NewPublisher(app);

            GetPublisherEndpointsEventHandler handlerA = () => [];
            GetPublisherEndpointsEventHandler handlerB = () => [];
            publisher.GetPublisherEndpoints = handlerA;
            publisher.GetPublisherEndpoints = handlerB;

            Assert.That(publisher.GetPublisherEndpoints, Is.SameAs(handlerB));
        }

        [Test]
        public void GetPublisherEndpoints_CanBeClearedToNull()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoveryPublisher publisher = NewPublisher(app);

            publisher.GetPublisherEndpoints = () => [];
            publisher.GetPublisherEndpoints = null;

            Assert.That(publisher.GetPublisherEndpoints, Is.Null);
        }

        [Test]
        public void GetDataSetWriterIds_DefaultIsNull()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoveryPublisher publisher = NewPublisher(app);

            Assert.That(publisher.GetDataSetWriterIds, Is.Null);
        }

        [Test]
        public void GetDataSetWriterIds_CanBeAssigned()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoveryPublisher publisher = NewPublisher(app);

            GetDataSetWriterIdsEventHandler handler = _ => [];
            publisher.GetDataSetWriterIds = handler;

            Assert.That(publisher.GetDataSetWriterIds, Is.SameAs(handler));
        }

        [Test]
        public void GetDataSetWriterIds_CanBeClearedToNull()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoveryPublisher publisher = NewPublisher(app);

            publisher.GetDataSetWriterIds = _ => [];
            publisher.GetDataSetWriterIds = null;

            Assert.That(publisher.GetDataSetWriterIds, Is.Null);
        }

        [Test]
        public void KMinimumResponseInterval_IsFiveHundredMilliseconds()
        {
            // The constant is 500 ms — verify it matches expected throttling
            // behaviour documented in the class.
            FieldInfo? field = typeof(UdpDiscoveryPublisher).GetField(
                "kMinimumResponseInterval",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            int value = (int)field!.GetValue(null)!;
            Assert.That(value, Is.EqualTo(500));
        }

        [Test]
        public void Constructor_SetsDiscoveryNetworkAddressEndPoint()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoveryPublisher publisher = NewPublisher(app);

            // The base Initialize() resolves the default discovery URL
            // to a non-null IPEndPoint.
            Assert.That(publisher.DiscoveryNetworkAddressEndPoint, Is.Not.Null);
        }

        private static UdpDiscoveryPublisher NewPublisher(UaPubSubApplication app)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "udp-pub-test",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.1:4840"
                })
            };
            var conn = new UdpPubSubConnection(app, connCfg, telemetry);
            return new UdpDiscoveryPublisher(conn, telemetry);
        }
    }
}
