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
 *
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
using System.IO;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;
using Opc.Ua.Redundancy;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Covers the encoding of a session handoff into a protected record and back.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    public sealed class SessionHandoffCodecTests
    {
        /// <summary>
        /// The subscriptions of a decoded handoff keep the namespace indexes of the session that
        /// stored it, because they are decoded with the namespace and server tables stored in the
        /// record instead of being mapped onto a context that does not know those namespaces.
        /// </summary>
        [Test]
        public void TryUnprotectKeepsTheNamespaceIndexesOfTheSubscriptions()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.Factory.Builder.AddOpcUaClientDataTypes();
            messageContext.NamespaceUris = new NamespaceTable(
                [Namespaces.OpcUa, "urn:test:first", "urn:test:second"]);
            messageContext.ServerUris = new StringTable(["urn:test:server"]);
            var startNodeId = new NodeId("Temp", 2);
            var handoff = new SessionHandoff
            {
                Session = new SessionConfiguration
                {
                    SessionName = "handoff",
                    SessionId = new NodeId("session", 1),
                    AuthenticationToken = new NodeId("token", 1),
                    ConfiguredEndpoint = SessionChannelHarness.CreateEndpoint()
                },
                RevisedSessionTimeout = 1500,
                Subscriptions =
                [
                    new SubscriptionStateSnapshot
                    {
                        ServerId = 7,
                        MonitoredItems =
                        [
                            new MonitoredItemStateSnapshot
                            {
                                Name = "temperature",
                                ServerId = 9,
                                StartNodeId = startNodeId,
                                AttributeId = Attributes.Value
                            }
                        ]
                    }
                ]
            };
            ByteString context = RecordProtectionContext.Create("client-replica-session", "client-replica/session");

            ByteString record = SessionHandoffCodec.Protect(
                handoff,
                NullRecordProtector.Instance,
                context,
                messageContext);
            bool decoded = SessionHandoffCodec.TryUnprotect(
                record,
                NullRecordProtector.Instance,
                context,
                telemetry,
                out SessionHandoff? restored);

            Assert.That(decoded, Is.True);
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Subscriptions.Count, Is.EqualTo(1));
            MonitoredItemStateSnapshot item = restored.Subscriptions[0].MonitoredItems[0];
            Assert.Multiple(() =>
            {
                Assert.That(item.StartNodeId.NamespaceIndex, Is.EqualTo(2));
                Assert.That(item.StartNodeId, Is.EqualTo(startNodeId));
                Assert.That(restored.Session.AuthenticationToken, Is.EqualTo(new NodeId("token", 1)));
                Assert.That(restored.RevisedSessionTimeout, Is.EqualTo(1500));
            });
        }

        /// <summary>
        /// Runtime values revised by the server survive the protected handoff encoding without
        /// changing the public subscription snapshot wire shape.
        /// </summary>
        [Test]
        public void TryUnprotectRestoresServerRevisedSubscriptionAndMonitoredItemSettings()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.Factory.Builder.AddOpcUaClientDataTypes();
            var filterResult = new EventFilterResult();
            var handoff = new SessionHandoff
            {
                Session = new SessionConfiguration
                {
                    SessionName = "handoff",
                    SessionId = new NodeId("session", 1),
                    AuthenticationToken = new NodeId("token", 1),
                    ConfiguredEndpoint = SessionChannelHarness.CreateEndpoint()
                },
                RevisedSessionTimeout = 1500,
                Subscriptions =
                [
                    new SubscriptionStateSnapshot
                    {
                        ServerId = 7,
                        HasRevisedSettings = true,
                        RevisedPublishingInterval = TimeSpan.FromMilliseconds(850),
                        RevisedKeepAliveCount = 12,
                        RevisedLifetimeCount = 36,
                        RevisedPriority = 4,
                        RevisedMaxNotificationsPerPublish = 24,
                        RevisedPublishingEnabled = false,
                        MonitoredItems =
                        [
                            new MonitoredItemStateSnapshot
                            {
                                Name = "temperature",
                                ServerId = 9,
                                HasRevisedSettings = true,
                                RevisedSamplingInterval = TimeSpan.FromMilliseconds(125),
                                RevisedQueueSize = 17,
                                RevisedFilterResult = filterResult
                            }
                        ]
                    }
                ]
            };
            ByteString context = RecordProtectionContext.Create(
                "client-replica-session",
                "client-replica/session");

            ByteString record = SessionHandoffCodec.Protect(
                handoff,
                NullRecordProtector.Instance,
                context,
                messageContext);
            bool decoded = SessionHandoffCodec.TryUnprotect(
                record,
                NullRecordProtector.Instance,
                context,
                telemetry,
                out SessionHandoff? restored);

            Assert.That(decoded, Is.True);
            Assert.That(restored, Is.Not.Null);
            SubscriptionStateSnapshot subscription = restored!.Subscriptions[0];
            MonitoredItemStateSnapshot item = subscription.MonitoredItems[0];
            Assert.Multiple(() =>
            {
                Assert.That(subscription.HasRevisedSettings, Is.True);
                Assert.That(subscription.RevisedPublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(850)));
                Assert.That(subscription.RevisedKeepAliveCount, Is.EqualTo(12));
                Assert.That(subscription.RevisedLifetimeCount, Is.EqualTo(36));
                Assert.That(subscription.RevisedPriority, Is.EqualTo(4));
                Assert.That(subscription.RevisedMaxNotificationsPerPublish, Is.EqualTo(24));
                Assert.That(subscription.RevisedPublishingEnabled, Is.False);
                Assert.That(item.HasRevisedSettings, Is.True);
                Assert.That(item.RevisedSamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
                Assert.That(item.RevisedQueueSize, Is.EqualTo(17));
                Assert.That(item.RevisedFilterResult, Is.TypeOf<EventFilterResult>());
            });
        }

        /// <summary>
        /// Version-one handoffs remain readable after adding separately encoded runtime settings.
        /// </summary>
        [Test]
        public void TryUnprotectReadsVersionOneHandoffs()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.Factory.Builder.AddOpcUaClientDataTypes();
            var session = new SessionConfiguration
            {
                SessionName = "legacy",
                SessionId = new NodeId("session", 1),
                AuthenticationToken = new NodeId("token", 1),
                ConfiguredEndpoint = SessionChannelHarness.CreateEndpoint()
            };
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, messageContext, true))
            {
                encoder.WriteInt32(null, 0x48535541);
                encoder.WriteInt32(null, 1);
                using var configuration = new MemoryStream();
                SnapshotCodec.SaveConfiguration(configuration, messageContext, session);
                encoder.WriteByteString(null, new ByteString(configuration.ToArray()));
                encoder.WriteDouble(null, 1500);
                encoder.WriteStringArray(null, messageContext.NamespaceUris.ToArrayOf());
                encoder.WriteStringArray(null, messageContext.ServerUris.ToArrayOf());
                encoder.WriteInt32(null, 0);
            }
            ByteString context = RecordProtectionContext.Create(
                "client-replica-session",
                "client-replica/session");

            bool decoded = SessionHandoffCodec.TryUnprotect(
                new ByteString(stream.ToArray()),
                NullRecordProtector.Instance,
                context,
                telemetry,
                out SessionHandoff? restored);

            Assert.That(decoded, Is.True);
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Session.AuthenticationToken, Is.EqualTo(session.AuthenticationToken));
            Assert.That(restored.Subscriptions, Is.Empty);
        }
    }
}
