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
    /// Tests for the BinaryEncoder/BinaryDecoder based serialization in
    /// <see cref="SubscriptionStore"/> and
    /// <see cref="DurableMonitoredItemQueueFactory"/>.
    /// </summary>
    [TestFixture]
    public class DurableSubscriptionSerializationTests
    {
        private ITelemetryContext m_telemetry;
        internal static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void RoundTripEmptySubscription()
        {
            StoredSubscription original = CreateMinimalSubscription(id: 42);

            StoredSubscription result = RoundTripSubscription(original);

            Assert.That(result.Id, Is.EqualTo(42u));
            Assert.That(result.IsDurable, Is.True);
            Assert.That(result.PublishingInterval, Is.EqualTo(1000.0));
            Assert.That(result.Priority, Is.EqualTo((byte)5));
            Assert.That(result.MonitoredItems, Is.Not.Null);
            Assert.That(result.MonitoredItems.Count(), Is.EqualTo(0));
            Assert.That(result.SentMessages, Is.Not.Null);
            Assert.That(result.SentMessages.Count, Is.EqualTo(0));
        }

        [Test]
        public void RoundTripSubscriptionWithMonitoredItems()
        {
            StoredSubscription original = CreateMinimalSubscription(id: 100);
            original.MonitoredItems = new List<IStoredMonitoredItem>
            {
                CreateMonitoredItem(id: 1, subscriptionId: 100),
                CreateMonitoredItem(id: 2, subscriptionId: 100),
                CreateMonitoredItem(id: 3, subscriptionId: 100)
            };

            StoredSubscription result = RoundTripSubscription(original);

            Assert.That(result.Id, Is.EqualTo(100u));
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
            StoredSubscription original = CreateMinimalSubscription(id: 1);
            StoredMonitoredItem mi = CreateMonitoredItem(id: 7, subscriptionId: 1);
            mi.NodeId = new NodeId("TestNode", 2);
            mi.AttributeId = Attributes.Value;
            mi.QueueSize = 10;
            mi.SamplingInterval = 500.0;
            mi.DiscardOldest = true;
            mi.MonitoringMode = MonitoringMode.Reporting;
            mi.IsDurable = true;
            mi.LastValue = new DataValue(
                new Variant(42),
                StatusCodes.Good,
                DateTime.UtcNow);
            original.MonitoredItems = new List<IStoredMonitoredItem> { mi };

            StoredSubscription result = RoundTripSubscription(original);

            var restored = result.MonitoredItems.First() as StoredMonitoredItem;
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Id, Is.EqualTo(7u));
            Assert.That(restored.NodeId, Is.EqualTo(new NodeId("TestNode", 2)));
            Assert.That(restored.AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(restored.QueueSize, Is.EqualTo(10u));
            Assert.That(restored.SamplingInterval, Is.EqualTo(500.0));
            Assert.That(restored.DiscardOldest, Is.True);
            Assert.That(restored.MonitoringMode,
                Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(restored.IsDurable, Is.True);
            Assert.That(restored.LastValue, Is.Not.Null);
            Assert.That(
                ((int)restored.LastValue.WrappedValue),
                Is.EqualTo(42));
        }

        [Test]
        public void RoundTripSubscriptionWithUserIdentityToken()
        {
            using IDisposable scope =
                AmbientMessageContext.SetScopedContext(m_telemetry);

            StoredSubscription original = CreateMinimalSubscription(id: 55);
            original.UserIdentityToken = new UserNameIdentityToken
            {
                PolicyId = "username_policy",
                UserName = "testuser"
            };

            StoredSubscription result = RoundTripSubscription(original);

            Assert.That(result.UserIdentityToken, Is.Not.Null);
            Assert.That(
                result.UserIdentityToken,
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

            List<StoredSubscription> results = RoundTripSubscriptions(subs);

            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results[0].Id, Is.EqualTo(1u));
            Assert.That(results[1].Id, Is.EqualTo(2u));
            Assert.That(results[2].Id, Is.EqualTo(3u));
            Assert.That(
                results[0].MonitoredItems.Count(), Is.EqualTo(2));
            Assert.That(
                results[1].MonitoredItems.Count(), Is.EqualTo(1));
            Assert.That(
                results[2].MonitoredItems.Count(), Is.EqualTo(0));
        }

        [Test]
        public void RoundTripStorableDataChangeQueue()
        {
            var original = new StorableDataChangeQueue
            {
                IsDurable = true,
                MonitoredItemId = 42,
                ItemsInQueue = 5,
                QueueSize = 100
            };

            StorableDataChangeQueue result = RoundTripDataChangeQueue(original);

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
            var original = new StorableDataChangeQueue
            {
                IsDurable = true,
                MonitoredItemId = 42,
                ItemsInQueue = 2,
                QueueSize = 100,
                EnqueueBatch = batch,
                DataChangeBatches = new List<DataChangeBatch> { batch }
            };

            StorableDataChangeQueue result = RoundTripDataChangeQueue(original);

            Assert.That(result.EnqueueBatch, Is.Not.Null);
            Assert.That(result.EnqueueBatch.Values, Has.Count.EqualTo(2));
            Assert.That(
                ((int)result.EnqueueBatch.Values[0].Item1.WrappedValue),
                Is.EqualTo(1));
            Assert.That(result.EnqueueBatch.Values[1].Item2, Is.Not.Null);
            Assert.That(result.DataChangeBatches, Has.Count.EqualTo(1));
        }

        [Test]
        public void RoundTripStorableEventQueue()
        {
            var original = new StorableEventQueue
            {
                IsDurable = true,
                MonitoredItemId = 99,
                QueueSize = 50
            };

            StorableEventQueue result = RoundTripEventQueue(original);

            Assert.That(result.IsDurable, Is.True);
            Assert.That(result.MonitoredItemId, Is.EqualTo(99u));
            Assert.That(result.QueueSize, Is.EqualTo(50u));
            Assert.That(result.EnqueueBatch, Is.Null);
            Assert.That(result.DequeueBatch, Is.Null);
        }

        private static StoredSubscription CreateMinimalSubscription(uint id)
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

        private static IServiceMessageContext GetContext()
        {
            return AmbientMessageContext.CurrentContext
                ?? new ServiceMessageContext(s_telemetry);
        }

        private static StoredSubscription RoundTripSubscription(
            StoredSubscription original)
        {
            List<StoredSubscription> list = RoundTripSubscriptions(
                new List<StoredSubscription> { original });
            return list.Single();
        }

        private static List<StoredSubscription> RoundTripSubscriptions(
            List<StoredSubscription> originals)
        {
            IServiceMessageContext context = GetContext();
            using var stream = new MemoryStream();

            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteStringArray(
                    null, context.NamespaceUris.ToArrayOf());
                encoder.WriteStringArray(
                    null, context.ServerUris.ToArrayOf());

                encoder.WriteInt32(null, originals.Count);
                foreach (StoredSubscription sub in originals)
                {
                    EncodeSubscription(encoder, sub);
                }
            }

            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);

            ArrayOf<string> nsUris = decoder.ReadStringArray(null);
            ArrayOf<string> serverUris = decoder.ReadStringArray(null);
            decoder.SetMappingTables(
                new NamespaceTable(nsUris.Memory.ToArray()),
                new StringTable(serverUris.Memory.ToArray()));

            int count = decoder.ReadInt32(null);
            var results = new List<StoredSubscription>(count);
            for (int i = 0; i < count; i++)
            {
                results.Add(DecodeSubscription(decoder));
            }
            return results;
        }

        private static StorableDataChangeQueue RoundTripDataChangeQueue(
            StorableDataChangeQueue original)
        {
            IServiceMessageContext context = GetContext();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteStringArray(
                    null, context.NamespaceUris.ToArrayOf());
                encoder.WriteStringArray(
                    null, context.ServerUris.ToArrayOf());
                EncodeDataChangeQueue(encoder, original);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            ArrayOf<string> nsUris = decoder.ReadStringArray(null);
            ArrayOf<string> serverUris = decoder.ReadStringArray(null);
            decoder.SetMappingTables(
                new NamespaceTable(nsUris.Memory.ToArray()),
                new StringTable(serverUris.Memory.ToArray()));
            return DecodeDataChangeQueue(decoder);
        }

        private static StorableEventQueue RoundTripEventQueue(
            StorableEventQueue original)
        {
            IServiceMessageContext context = GetContext();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteStringArray(
                    null, context.NamespaceUris.ToArrayOf());
                encoder.WriteStringArray(
                    null, context.ServerUris.ToArrayOf());
                EncodeEventQueue(encoder, original);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            ArrayOf<string> nsUris = decoder.ReadStringArray(null);
            ArrayOf<string> serverUris = decoder.ReadStringArray(null);
            decoder.SetMappingTables(
                new NamespaceTable(nsUris.Memory.ToArray()),
                new StringTable(serverUris.Memory.ToArray()));
            return DecodeEventQueue(decoder);
        }

        private static void EncodeSubscription(
            BinaryEncoder encoder, StoredSubscription sub)
        {
            encoder.WriteUInt32(null, sub.Id);
            encoder.WriteBoolean(null, sub.IsDurable);
            encoder.WriteUInt32(null, sub.LifetimeCounter);
            encoder.WriteUInt32(null, sub.MaxLifetimeCount);
            encoder.WriteUInt32(null, sub.MaxKeepaliveCount);
            encoder.WriteUInt32(null, sub.MaxMessageCount);
            encoder.WriteUInt32(null, sub.MaxNotificationsPerPublish);
            encoder.WriteDouble(null, sub.PublishingInterval);
            encoder.WriteByte(null, sub.Priority);
            encoder.WriteInt32(null, sub.LastSentMessage);
            encoder.WriteUInt32(null, sub.SequenceNumber);
            encoder.WriteExtensionObject(null,
                sub.UserIdentityToken != null
                    ? new ExtensionObject(sub.UserIdentityToken)
                    : ExtensionObject.Null);
            var sentMsgs = sub.SentMessages?
                .Select(m => new ExtensionObject(m)).ToArray() ?? [];
            encoder.WriteExtensionObjectArray(null,
                new ArrayOf<ExtensionObject>(sentMsgs));
            List<StoredMonitoredItem> items = sub.MonitoredItems?
                .Cast<StoredMonitoredItem>().ToList() ?? [];
            encoder.WriteInt32(null, items.Count);
            foreach (StoredMonitoredItem item in items)
            {
                EncodeMonitoredItem(encoder, item);
            }
        }

        private static StoredSubscription DecodeSubscription(
            BinaryDecoder decoder)
        {
            var sub = new StoredSubscription
            {
                Id = decoder.ReadUInt32(null),
                IsDurable = decoder.ReadBoolean(null),
                LifetimeCounter = decoder.ReadUInt32(null),
                MaxLifetimeCount = decoder.ReadUInt32(null),
                MaxKeepaliveCount = decoder.ReadUInt32(null),
                MaxMessageCount = decoder.ReadUInt32(null),
                MaxNotificationsPerPublish = decoder.ReadUInt32(null),
                PublishingInterval = decoder.ReadDouble(null),
                Priority = decoder.ReadByte(null),
                LastSentMessage = decoder.ReadInt32(null),
                SequenceNumber = decoder.ReadUInt32(null),
            };
            ExtensionObject tokenEo = decoder.ReadExtensionObject(null);
            if (!tokenEo.IsNull &&
                tokenEo.TryGetEncodeable(out IEncodeable tokenBody))
            {
                sub.UserIdentityToken = tokenBody as UserIdentityToken;
            }
            ArrayOf<ExtensionObject> sentMsgEos = decoder.ReadExtensionObjectArray(null);
            sub.SentMessages = [];
            if (!sentMsgEos.IsNull)
            {
                foreach (ExtensionObject eo in sentMsgEos.Memory.ToArray())
                {
                    if (!eo.IsNull &&
                        eo.TryGetEncodeable(out IEncodeable e) &&
                        e is NotificationMessage nm)
                    {
                        sub.SentMessages.Add(nm);
                    }
                }
            }
            int itemCount = decoder.ReadInt32(null);
            var items = new List<IStoredMonitoredItem>(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                items.Add(DecodeMonitoredItem(decoder));
            }
            sub.MonitoredItems = items;
            return sub;
        }

        private static void EncodeMonitoredItem(
            BinaryEncoder encoder, StoredMonitoredItem item)
        {
            encoder.WriteBoolean(null, item.IsRestored);
            encoder.WriteUInt32(null, item.SubscriptionId);
            encoder.WriteUInt32(null, item.Id);
            encoder.WriteInt32(null, item.TypeMask);
            encoder.WriteNodeId(null, item.NodeId);
            encoder.WriteUInt32(null, item.AttributeId);
            encoder.WriteString(null, item.IndexRange);
            encoder.WriteQualifiedName(null, item.Encoding);
            encoder.WriteEnumerated(null, item.DiagnosticsMasks);
            encoder.WriteEnumerated(null, item.TimestampsToReturn);
            encoder.WriteUInt32(null, item.ClientHandle);
            encoder.WriteEnumerated(null, item.MonitoringMode);
            encoder.WriteExtensionObject(null,
                item.OriginalFilter != null
                    ? new ExtensionObject(item.OriginalFilter)
                    : ExtensionObject.Null);
            encoder.WriteExtensionObject(null,
                item.FilterToUse != null
                    ? new ExtensionObject(item.FilterToUse)
                    : ExtensionObject.Null);
            encoder.WriteDouble(null, item.Range);
            encoder.WriteDouble(null, item.SamplingInterval);
            encoder.WriteUInt32(null, item.QueueSize);
            encoder.WriteBoolean(null, item.DiscardOldest);
            encoder.WriteInt32(null, item.SourceSamplingInterval);
            encoder.WriteBoolean(null, item.AlwaysReportUpdates);
            encoder.WriteBoolean(null, item.IsDurable);
            encoder.WriteDataValue(null, item.LastValue);
            encoder.WriteStatusCode(null,
                item.LastError?.StatusCode ?? StatusCodes.Good);
            encoder.WriteString(null, item.ParsedIndexRange.ToString());
        }

        private static StoredMonitoredItem DecodeMonitoredItem(
            BinaryDecoder decoder)
        {
            var item = new StoredMonitoredItem
            {
                IsRestored = decoder.ReadBoolean(null),
                SubscriptionId = decoder.ReadUInt32(null),
                Id = decoder.ReadUInt32(null),
                TypeMask = decoder.ReadInt32(null),
                NodeId = decoder.ReadNodeId(null),
                AttributeId = decoder.ReadUInt32(null),
                IndexRange = decoder.ReadString(null),
                Encoding = decoder.ReadQualifiedName(null),
                DiagnosticsMasks = decoder.ReadEnumerated<DiagnosticsMasks>(null),
                TimestampsToReturn =
                    decoder.ReadEnumerated<TimestampsToReturn>(null),
                ClientHandle = decoder.ReadUInt32(null),
                MonitoringMode = decoder.ReadEnumerated<MonitoringMode>(null),
            };
            ExtensionObject origEo = decoder.ReadExtensionObject(null);
            if (!origEo.IsNull &&
                origEo.TryGetEncodeable(out IEncodeable origBody))
            {
                item.OriginalFilter = origBody as MonitoringFilter;
            }
            ExtensionObject filterEo = decoder.ReadExtensionObject(null);
            if (!filterEo.IsNull &&
                filterEo.TryGetEncodeable(out IEncodeable filterBody))
            {
                item.FilterToUse = filterBody as MonitoringFilter;
            }
            item.Range = decoder.ReadDouble(null);
            item.SamplingInterval = decoder.ReadDouble(null);
            item.QueueSize = decoder.ReadUInt32(null);
            item.DiscardOldest = decoder.ReadBoolean(null);
            item.SourceSamplingInterval = decoder.ReadInt32(null);
            item.AlwaysReportUpdates = decoder.ReadBoolean(null);
            item.IsDurable = decoder.ReadBoolean(null);
            item.LastValue = decoder.ReadDataValue(null);
            StatusCode sc = decoder.ReadStatusCode(null);
            item.LastError = sc == StatusCodes.Good
                ? null : new ServiceResult(sc);
            string rangeStr = decoder.ReadString(null);
            item.ParsedIndexRange = string.IsNullOrEmpty(rangeStr)
                ? NumericRange.Null : NumericRange.Parse(rangeStr);
            return item;
        }

        private static void EncodeDataChangeQueue(
            BinaryEncoder encoder, StorableDataChangeQueue q)
        {
            encoder.WriteBoolean(null, q.IsDurable);
            encoder.WriteUInt32(null, q.MonitoredItemId);
            encoder.WriteInt32(null, q.ItemsInQueue);
            encoder.WriteUInt32(null, q.QueueSize);
            EncodeDataChangeBatch(encoder, q.EnqueueBatch);
            EncodeDataChangeBatch(encoder, q.DequeueBatch);
            int batchCount = q.DataChangeBatches?.Count ?? 0;
            encoder.WriteInt32(null, batchCount);
            if (q.DataChangeBatches != null)
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EncodeDataChangeBatch(encoder, q.DataChangeBatches[i]);
                }
            }
        }

        private static StorableDataChangeQueue DecodeDataChangeQueue(
            BinaryDecoder decoder)
        {
            var q = new StorableDataChangeQueue
            {
                IsDurable = decoder.ReadBoolean(null),
                MonitoredItemId = decoder.ReadUInt32(null),
                ItemsInQueue = decoder.ReadInt32(null),
                QueueSize = decoder.ReadUInt32(null),
                EnqueueBatch = DecodeDataChangeBatch(decoder),
                DequeueBatch = DecodeDataChangeBatch(decoder),
            };
            int batchCount = decoder.ReadInt32(null);
            q.DataChangeBatches = new List<DataChangeBatch>(batchCount);
            for (int i = 0; i < batchCount; i++)
            {
                q.DataChangeBatches.Add(DecodeDataChangeBatch(decoder));
            }
            return q;
        }

        private static void EncodeDataChangeBatch(
            BinaryEncoder encoder, DataChangeBatch batch)
        {
            bool has = batch != null;
            encoder.WriteBoolean(null, has);
            if (!has)
            {
                return;
            }
            encoder.WriteGuid(null, batch.Id);
            encoder.WriteUInt32(null, batch.BatchSize);
            encoder.WriteUInt32(null, batch.MonitoredItemId);
            encoder.WriteBoolean(null, batch.IsPersisted);
            int count = batch.Values?.Count ?? 0;
            encoder.WriteInt32(null, count);
            if (batch.Values != null)
            {
                for (int i = 0; i < count; i++)
                {
                    encoder.WriteDataValue(null, batch.Values[i].Item1);
                    encoder.WriteStatusCode(null,
                        batch.Values[i].Item2?.StatusCode
                            ?? StatusCodes.Good);
                }
            }
        }

        private static DataChangeBatch DecodeDataChangeBatch(
            BinaryDecoder decoder)
        {
            bool has = decoder.ReadBoolean(null);
            if (!has)
            {
                return null;
            }
            decoder.ReadGuid(null);
            uint batchSize = decoder.ReadUInt32(null);
            uint monItemId = decoder.ReadUInt32(null);
            bool isPersisted = decoder.ReadBoolean(null);
            int count = decoder.ReadInt32(null);
            var values = new List<(DataValue, ServiceResult)>(count);
            for (int i = 0; i < count; i++)
            {
                DataValue dv = decoder.ReadDataValue(null);
                StatusCode sc = decoder.ReadStatusCode(null);
                ServiceResult sr = sc == StatusCodes.Good
                    ? null : new ServiceResult(sc);
                values.Add((dv, sr));
            }
            var batch = new DataChangeBatch(values, batchSize, monItemId);
            if (isPersisted)
            {
                batch.SetPersisted();
                batch.Restore(values);
            }
            return batch;
        }

        private static void EncodeEventQueue(
            BinaryEncoder encoder, StorableEventQueue q)
        {
            encoder.WriteBoolean(null, q.IsDurable);
            encoder.WriteUInt32(null, q.MonitoredItemId);
            encoder.WriteUInt32(null, q.QueueSize);
            EncodeEventBatch(encoder, q.EnqueueBatch);
            EncodeEventBatch(encoder, q.DequeueBatch);
            int batchCount = q.EventBatches?.Count ?? 0;
            encoder.WriteInt32(null, batchCount);
            if (q.EventBatches != null)
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EncodeEventBatch(encoder, q.EventBatches[i]);
                }
            }
        }

        private static StorableEventQueue DecodeEventQueue(
            BinaryDecoder decoder)
        {
            var q = new StorableEventQueue
            {
                IsDurable = decoder.ReadBoolean(null),
                MonitoredItemId = decoder.ReadUInt32(null),
                QueueSize = decoder.ReadUInt32(null),
                EnqueueBatch = DecodeEventBatch(decoder),
                DequeueBatch = DecodeEventBatch(decoder),
            };
            int batchCount = decoder.ReadInt32(null);
            q.EventBatches = new List<EventBatch>(batchCount);
            for (int i = 0; i < batchCount; i++)
            {
                q.EventBatches.Add(DecodeEventBatch(decoder));
            }
            return q;
        }

        private static void EncodeEventBatch(
            BinaryEncoder encoder, EventBatch batch)
        {
            bool has = batch != null;
            encoder.WriteBoolean(null, has);
            if (!has)
            {
                return;
            }
            encoder.WriteGuid(null, batch.Id);
            encoder.WriteUInt32(null, batch.BatchSize);
            encoder.WriteUInt32(null, batch.MonitoredItemId);
            encoder.WriteBoolean(null, batch.IsPersisted);
            int count = batch.Events?.Count ?? 0;
            encoder.WriteInt32(null, count);
            if (batch.Events != null)
            {
                for (int i = 0; i < count; i++)
                {
                    encoder.WriteEncodeableAsExtensionObject(
                        null, batch.Events[i]);
                }
            }
        }

        private static EventBatch DecodeEventBatch(
            BinaryDecoder decoder)
        {
            bool has = decoder.ReadBoolean(null);
            if (!has)
            {
                return null;
            }
            decoder.ReadGuid(null);
            uint batchSize = decoder.ReadUInt32(null);
            uint monItemId = decoder.ReadUInt32(null);
            decoder.ReadBoolean(null);
            int count = decoder.ReadInt32(null);
            var events = new List<EventFieldList>(count);
            for (int i = 0; i < count; i++)
            {
                ExtensionObject eo = decoder.ReadExtensionObject(null);
                if (!eo.IsNull &&
                    eo.TryGetEncodeable(out IEncodeable e) &&
                    e is EventFieldList efl)
                {
                    events.Add(efl);
                }
            }
            return new EventBatch(events, batchSize, monItemId);
        }
    }
}
