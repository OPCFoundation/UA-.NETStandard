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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using Quickstarts.Servers;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Round-trip tests for the BinaryEncoder/BinaryDecoder based
    /// serialization in <see cref="SubscriptionStore"/> and
    /// <see cref="DurableMonitoredItemQueueFactory"/>.
    /// Tests call the internal encode/decode methods directly.
    /// </summary>
    [TestFixture]
    public class DurableSubscriptionSerializationTests
    {
        private IServiceMessageContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var telemetry = NUnitTelemetryContext.Create();
            m_context = new ServiceMessageContext(telemetry);
            m_context.NamespaceUris.GetIndexOrAppend(
                "urn:test:namespace1");
            m_context.NamespaceUris.GetIndexOrAppend(
                "urn:test:namespace2");
        }

        #region SubscriptionStore Round-Trip Tests

        [Test]
        public void RoundTripEmptySubscription()
        {
            StoredSubscription result =
                RoundTripSubscription(CreateMinimalSubscription(id: 42));

            Assert.That(result.Id, Is.EqualTo(42u));
            Assert.That(result.IsDurable, Is.True);
            Assert.That(result.PublishingInterval, Is.EqualTo(1000.0));
            Assert.That(result.Priority, Is.EqualTo((byte)5));
            Assert.That(result.MonitoredItems.Count(), Is.EqualTo(0));
            Assert.That(result.SentMessages, Has.Count.EqualTo(0));
        }

        [Test]
        public void RoundTripSubscriptionWithMonitoredItems()
        {
            var original = CreateMinimalSubscription(id: 100);
            original.MonitoredItems = new List<IStoredMonitoredItem>
            {
                CreateMonitoredItem(id: 1, subscriptionId: 100),
                CreateMonitoredItem(id: 2, subscriptionId: 100),
                CreateMonitoredItem(id: 3, subscriptionId: 100)
            };

            StoredSubscription result = RoundTripSubscription(original);

            var items = result.MonitoredItems.ToList();
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[0].Id, Is.EqualTo(1u));
            Assert.That(items[1].Id, Is.EqualTo(2u));
            Assert.That(items[2].Id, Is.EqualTo(3u));
            Assert.That(items[0].SubscriptionId, Is.EqualTo(100u));
        }

        [Test]
        public void RoundTripMonitoredItemProperties()
        {
            var original = CreateMinimalSubscription(id: 1);
            var mi = CreateMonitoredItem(id: 7, subscriptionId: 1);
            mi.NodeId = new NodeId("TestNode", 2);
            mi.AttributeId = Attributes.Value;
            mi.QueueSize = 10;
            mi.SamplingInterval = 500.0;
            mi.DiscardOldest = true;
            mi.MonitoringMode = MonitoringMode.Reporting;
            mi.IsDurable = true;
            mi.LastValue = new DataValue(
                new Variant(42), StatusCodes.Good, DateTime.UtcNow);
            original.MonitoredItems =
                new List<IStoredMonitoredItem> { mi };

            var restored = RoundTripSubscription(original)
                .MonitoredItems.First() as StoredMonitoredItem;

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Id, Is.EqualTo(7u));
            Assert.That(restored.NodeId,
                Is.EqualTo(new NodeId("TestNode", 2)));
            Assert.That(restored.AttributeId,
                Is.EqualTo(Attributes.Value));
            Assert.That(restored.QueueSize, Is.EqualTo(10u));
            Assert.That(restored.SamplingInterval, Is.EqualTo(500.0));
            Assert.That(restored.DiscardOldest, Is.True);
            Assert.That(restored.MonitoringMode,
                Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(restored.IsDurable, Is.True);
            Assert.That(restored.LastValue, Is.Not.Null);
            Assert.That((int)restored.LastValue.WrappedValue,
                Is.EqualTo(42));
        }

        [Test]
        public void RoundTripSubscriptionWithUserIdentityToken()
        {
            var original = CreateMinimalSubscription(id: 55);
            original.UserIdentityToken = new UserNameIdentityToken
            {
                PolicyId = "username_policy",
                UserName = "testuser"
            };

            var result = RoundTripSubscription(original);

            Assert.That(result.UserIdentityToken, Is.Not.Null);
            Assert.That(result.UserIdentityToken,
                Is.InstanceOf<UserNameIdentityToken>());
            var token = (UserNameIdentityToken)result.UserIdentityToken;
            Assert.That(token.UserName, Is.EqualTo("testuser"));
            Assert.That(token.PolicyId, Is.EqualTo("username_policy"));
        }

        [Test]
        public void RoundTripMultipleSubscriptions()
        {
            var subs = new List<StoredSubscription>
            {
                CreateMinimalSubscription(id: 1),
                CreateMinimalSubscription(id: 2),
                CreateMinimalSubscription(id: 3)
            };
            subs[0].MonitoredItems = new List<IStoredMonitoredItem>
            {
                CreateMonitoredItem(id: 10, subscriptionId: 1),
                CreateMonitoredItem(id: 11, subscriptionId: 1)
            };
            subs[1].MonitoredItems = new List<IStoredMonitoredItem>
            {
                CreateMonitoredItem(id: 20, subscriptionId: 2)
            };

            var results = RoundTripSubscriptions(subs);

            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results[0].Id, Is.EqualTo(1u));
            Assert.That(results[1].Id, Is.EqualTo(2u));
            Assert.That(results[2].Id, Is.EqualTo(3u));
            Assert.That(results[0].MonitoredItems.Count(),
                Is.EqualTo(2));
            Assert.That(results[1].MonitoredItems.Count(),
                Is.EqualTo(1));
            Assert.That(results[2].MonitoredItems.Count(),
                Is.EqualTo(0));
        }

        #endregion

        #region DurableMonitoredItemQueueFactory Round-Trip Tests

        [Test]
        public void RoundTripStorableDataChangeQueue()
        {
            var result = RoundTripDataChangeQueue(
                new StorableDataChangeQueue
                {
                    IsDurable = true,
                    MonitoredItemId = 42,
                    ItemsInQueue = 5,
                    QueueSize = 100
                });

            Assert.That(result.IsDurable, Is.True);
            Assert.That(result.MonitoredItemId, Is.EqualTo(42u));
            Assert.That(result.ItemsInQueue, Is.EqualTo(5));
            Assert.That(result.QueueSize, Is.EqualTo(100u));
            Assert.That(result.EnqueueBatch, Is.Null);
            Assert.That(result.DequeueBatch, Is.Null);
        }

        [Test]
        public void RoundTripStorableDataChangeQueueWithBatches()
        {
            var values = new List<(DataValue, ServiceResult)>
            {
                (new DataValue(new Variant(1), StatusCodes.Good), null),
                (new DataValue(new Variant("hello"), StatusCodes.Good),
                    new ServiceResult(StatusCodes.BadTimeout))
            };
            var batch = new DataChangeBatch(values, 10, 42);

            var result = RoundTripDataChangeQueue(
                new StorableDataChangeQueue
                {
                    IsDurable = true,
                    MonitoredItemId = 42,
                    ItemsInQueue = 2,
                    QueueSize = 100,
                    EnqueueBatch = batch,
                    DataChangeBatches =
                        new List<DataChangeBatch> { batch }
                });

            Assert.That(result.EnqueueBatch, Is.Not.Null);
            Assert.That(result.EnqueueBatch.Values,
                Has.Count.EqualTo(2));
            Assert.That(
                (int)result.EnqueueBatch.Values[0].Item1.WrappedValue,
                Is.EqualTo(1));
            Assert.That(result.EnqueueBatch.Values[1].Item2,
                Is.Not.Null);
            Assert.That(result.DataChangeBatches,
                Has.Count.EqualTo(1));
        }

        [Test]
        public void RoundTripStorableEventQueue()
        {
            var result = RoundTripEventQueue(
                new StorableEventQueue
                {
                    IsDurable = true,
                    MonitoredItemId = 99,
                    QueueSize = 50
                });

            Assert.That(result.IsDurable, Is.True);
            Assert.That(result.MonitoredItemId, Is.EqualTo(99u));
            Assert.That(result.QueueSize, Is.EqualTo(50u));
            Assert.That(result.EnqueueBatch, Is.Null);
            Assert.That(result.DequeueBatch, Is.Null);
        }

        #endregion

        #region Helpers

        private static StoredSubscription CreateMinimalSubscription(
            uint id)
        {
            return new StoredSubscription
            {
                Id = id,
                IsDurable = true,
                LifetimeCounter = 10,
                MaxLifetimeCount = 100,
                MaxKeepaliveCount = 10,
                MaxMessageCount = 5,
                MaxNotificationsPerPublish = 1000,
                PublishingInterval = 1000.0,
                Priority = 5,
                LastSentMessage = 0,
                SequenceNumber = 1,
                SentMessages = [],
                MonitoredItems = []
            };
        }

        private static StoredMonitoredItem CreateMonitoredItem(
            uint id, uint subscriptionId)
        {
            return new StoredMonitoredItem
            {
                Id = id,
                SubscriptionId = subscriptionId,
                NodeId = new NodeId(id, 0),
                AttributeId = Attributes.Value,
                QueueSize = 1,
                SamplingInterval = 1000.0,
                MonitoringMode = MonitoringMode.Reporting,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.None
            };
        }

        private StoredSubscription RoundTripSubscription(
            StoredSubscription original)
        {
            return RoundTripSubscriptions([original]).Single();
        }

        /// <summary>
        /// Encodes and decodes a list of subscriptions using
        /// the internal SubscriptionStore methods.
        /// </summary>
        private List<StoredSubscription> RoundTripSubscriptions(
            List<StoredSubscription> originals)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(
                stream, m_context, true))
            {
                encoder.WriteStringArray(
                    null, m_context.NamespaceUris.ToArrayOf());
                encoder.WriteStringArray(
                    null, m_context.ServerUris.ToArrayOf());
                encoder.WriteInt32(null, originals.Count);
                foreach (StoredSubscription sub in originals)
                {
                    SubscriptionStore.EncodeSubscription(
                        encoder, sub);
                }
            }

            stream.Position = 0;
            using var decoder = new BinaryDecoder(
                stream, m_context, true);
            ArrayOf<string> nsUris = decoder.ReadStringArray(null);
            ArrayOf<string> srvUris = decoder.ReadStringArray(null);
            decoder.SetMappingTables(
                new NamespaceTable(nsUris.Memory.ToArray()),
                new StringTable(srvUris.Memory.ToArray()));
            int count = decoder.ReadInt32(null);
            var results = new List<StoredSubscription>(count);
            for (int i = 0; i < count; i++)
            {
                results.Add(
                    SubscriptionStore.DecodeSubscription(decoder));
            }
            return results;
        }

        /// <summary>
        /// Encodes and decodes a StorableDataChangeQueue using
        /// the internal DurableMonitoredItemQueueFactory methods.
        /// </summary>
        private StorableDataChangeQueue RoundTripDataChangeQueue(
            StorableDataChangeQueue original)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(
                stream, m_context, true))
            {
                encoder.WriteStringArray(
                    null, m_context.NamespaceUris.ToArrayOf());
                encoder.WriteStringArray(
                    null, m_context.ServerUris.ToArrayOf());
                DurableMonitoredItemQueueFactory
                    .EncodeDataChangeQueue(encoder, original);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(
                stream, m_context, true);
            ArrayOf<string> nsUris = decoder.ReadStringArray(null);
            ArrayOf<string> srvUris = decoder.ReadStringArray(null);
            decoder.SetMappingTables(
                new NamespaceTable(nsUris.Memory.ToArray()),
                new StringTable(srvUris.Memory.ToArray()));
            return DurableMonitoredItemQueueFactory
                .DecodeDataChangeQueue(decoder);
        }

        /// <summary>
        /// Encodes and decodes a StorableEventQueue using
        /// the internal DurableMonitoredItemQueueFactory methods.
        /// </summary>
        private StorableEventQueue RoundTripEventQueue(
            StorableEventQueue original)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(
                stream, m_context, true))
            {
                encoder.WriteStringArray(
                    null, m_context.NamespaceUris.ToArrayOf());
                encoder.WriteStringArray(
                    null, m_context.ServerUris.ToArrayOf());
                DurableMonitoredItemQueueFactory
                    .EncodeEventQueue(encoder, original);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(
                stream, m_context, true);
            ArrayOf<string> nsUris = decoder.ReadStringArray(null);
            ArrayOf<string> srvUris = decoder.ReadStringArray(null);
            decoder.SetMappingTables(
                new NamespaceTable(nsUris.Memory.ToArray()),
                new StringTable(srvUris.Memory.ToArray()));
            return DurableMonitoredItemQueueFactory
                .DecodeEventQueue(decoder);
        }

        #endregion
    }
}
