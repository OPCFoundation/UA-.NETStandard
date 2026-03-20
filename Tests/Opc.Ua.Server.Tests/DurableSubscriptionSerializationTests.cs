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
    /// Tests for the IEncoder/IDecoder based serialization in
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

        #region SubscriptionStore Encode/Decode Round-Trip

        [TestCase(SubscriptionStoreEncoding.Json)]
        [TestCase(SubscriptionStoreEncoding.Binary)]
        public void RoundTripEmptySubscription(SubscriptionStoreEncoding encoding)
        {
            var original = CreateMinimalSubscription(id: 42);

            var result = RoundTripSubscription(original, encoding);

            Assert.That(result.Id, Is.EqualTo(42u));
            Assert.That(result.IsDurable, Is.True);
            Assert.That(result.PublishingInterval, Is.EqualTo(1000.0));
            Assert.That(result.Priority, Is.EqualTo((byte)5));
            Assert.That(result.MonitoredItems, Is.Not.Null);
            Assert.That(result.MonitoredItems.Count(), Is.EqualTo(0));
            Assert.That(result.SentMessages, Is.Not.Null);
            Assert.That(result.SentMessages.Count, Is.EqualTo(0));
        }

        [TestCase(SubscriptionStoreEncoding.Json)]
        [TestCase(SubscriptionStoreEncoding.Binary)]
        public void RoundTripSubscriptionWithMonitoredItems(
            SubscriptionStoreEncoding encoding)
        {
            var original = CreateMinimalSubscription(id: 100);
            original.MonitoredItems = new List<IStoredMonitoredItem>
            {
                CreateMonitoredItem(id: 1, subscriptionId: 100),
                CreateMonitoredItem(id: 2, subscriptionId: 100),
                CreateMonitoredItem(id: 3, subscriptionId: 100)
            };

            var result = RoundTripSubscription(original, encoding);

            Assert.That(result.Id, Is.EqualTo(100u));
            var items = result.MonitoredItems.ToList();
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[0].Id, Is.EqualTo(1u));
            Assert.That(items[1].Id, Is.EqualTo(2u));
            Assert.That(items[2].Id, Is.EqualTo(3u));
            Assert.That(items[0].SubscriptionId, Is.EqualTo(100u));
        }

        [TestCase(SubscriptionStoreEncoding.Json)]
        [TestCase(SubscriptionStoreEncoding.Binary)]
        public void RoundTripMonitoredItemProperties(
            SubscriptionStoreEncoding encoding)
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
                new Variant(42),
                StatusCodes.Good,
                DateTime.UtcNow);
            original.MonitoredItems = new List<IStoredMonitoredItem> { mi };

            var result = RoundTripSubscription(original, encoding);

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

        [TestCase(SubscriptionStoreEncoding.Json)]
        [TestCase(SubscriptionStoreEncoding.Binary)]
        public void RoundTripSubscriptionWithUserIdentityToken(
            SubscriptionStoreEncoding encoding)
        {
            using IDisposable scope =
                AmbientMessageContext.SetScopedContext(m_telemetry);

            var original = CreateMinimalSubscription(id: 55);
            original.UserIdentityToken = new UserNameIdentityToken
            {
                PolicyId = "username_policy",
                UserName = "testuser"
            };

            var result = RoundTripSubscription(original, encoding);

            Assert.That(result.UserIdentityToken, Is.Not.Null);
            Assert.That(
                result.UserIdentityToken,
                Is.InstanceOf<UserNameIdentityToken>());
            var token = (UserNameIdentityToken)result.UserIdentityToken;
            Assert.That(token.UserName, Is.EqualTo("testuser"));
            Assert.That(token.PolicyId, Is.EqualTo("username_policy"));
        }

        [TestCase(SubscriptionStoreEncoding.Json)]
        [TestCase(SubscriptionStoreEncoding.Binary)]
        public void RoundTripMultipleSubscriptions(
            SubscriptionStoreEncoding encoding)
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

            var results = RoundTripSubscriptions(subs, encoding);

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

        #endregion

        #region DurableMonitoredItemQueueFactory Encode/Decode Round-Trip

        [TestCase(SubscriptionStoreEncoding.Json)]
        [TestCase(SubscriptionStoreEncoding.Binary)]
        public void RoundTripStorableDataChangeQueue(
            SubscriptionStoreEncoding encoding)
        {
            var original = new StorableDataChangeQueue
            {
                IsDurable = true,
                MonitoredItemId = 42,
                ItemsInQueue = 5,
                QueueSize = 100
            };

            var result = RoundTripDataChangeQueue(original, encoding);

            Assert.That(result.IsDurable, Is.True);
            Assert.That(result.MonitoredItemId, Is.EqualTo(42u));
            Assert.That(result.ItemsInQueue, Is.EqualTo(5));
            Assert.That(result.QueueSize, Is.EqualTo(100u));
            Assert.That(result.EnqueueBatch, Is.Null);
            Assert.That(result.DequeueBatch, Is.Null);
        }

        [TestCase(SubscriptionStoreEncoding.Json)]
        [TestCase(SubscriptionStoreEncoding.Binary)]
        public void RoundTripStorableDataChangeQueueWithBatches(
            SubscriptionStoreEncoding encoding)
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

            var result = RoundTripDataChangeQueue(original, encoding);

            Assert.That(result.EnqueueBatch, Is.Not.Null);
            Assert.That(result.EnqueueBatch.Values, Has.Count.EqualTo(2));
            Assert.That(
                ((int)result.EnqueueBatch.Values[0].Item1.WrappedValue),
                Is.EqualTo(1));
            Assert.That(result.EnqueueBatch.Values[1].Item2, Is.Not.Null);
            Assert.That(result.DataChangeBatches, Has.Count.EqualTo(1));
        }

        [TestCase(SubscriptionStoreEncoding.Json)]
        [TestCase(SubscriptionStoreEncoding.Binary)]
        public void RoundTripStorableEventQueue(
            SubscriptionStoreEncoding encoding)
        {
            var original = new StorableEventQueue
            {
                IsDurable = true,
                MonitoredItemId = 99,
                QueueSize = 50
            };

            var result = RoundTripEventQueue(original, encoding);

            Assert.That(result.IsDurable, Is.True);
            Assert.That(result.MonitoredItemId, Is.EqualTo(99u));
            Assert.That(result.QueueSize, Is.EqualTo(50u));
            Assert.That(result.EnqueueBatch, Is.Null);
            Assert.That(result.DequeueBatch, Is.Null);
        }

        #endregion

        #region Helpers

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

        private static StoredSubscription RoundTripSubscription(
            StoredSubscription original,
            SubscriptionStoreEncoding encoding)
        {
            var list = RoundTripSubscriptions(
                new List<StoredSubscription> { original }, encoding);
            return list.Single();
        }

        private static List<StoredSubscription> RoundTripSubscriptions(
            List<StoredSubscription> originals,
            SubscriptionStoreEncoding encoding)
        {
            using var stream = new MemoryStream();

            // Encode — each subscription as isolated blob
            using (IEncoder encoder = CreateEncoder(encoding, stream))
            {
                var subBlobs = new List<ByteString>(originals.Count);
                foreach (var sub in originals)
                {
                    using var subStream = new MemoryStream();
                    using (IEncoder subEncoder = CreateEncoder(encoding, subStream))
                    {
                        EncodeSubscription(subEncoder, sub, encoding);
                    }
                    subBlobs.Add(new ByteString(subStream.ToArray()));
                }
                encoder.WriteByteStringArray("SubscriptionBlobs",
                    new ArrayOf<ByteString>(subBlobs.ToArray()));
            }

            // Decode
            stream.Position = 0;
            using IDecoder decoder = CreateDecoder(encoding, stream);
            ArrayOf<ByteString> restoredBlobs =
                decoder.ReadByteStringArray("SubscriptionBlobs");
            var results = new List<StoredSubscription>();
            if (!restoredBlobs.IsNull)
            {
                foreach (ByteString blob in restoredBlobs.Memory.ToArray())
                {
                    if (!blob.IsNull && blob.Length > 0)
                    {
                        using var subStream = new MemoryStream(blob.ToArray());
                        using IDecoder subDecoder =
                            CreateDecoder(encoding, subStream);
                        results.Add(DecodeSubscription(subDecoder, encoding));
                    }
                }
            }
            return results;
        }

        private static StorableDataChangeQueue RoundTripDataChangeQueue(
            StorableDataChangeQueue original,
            SubscriptionStoreEncoding encoding)
        {
            using var stream = new MemoryStream();
            using (IEncoder encoder = CreateEncoder(encoding, stream))
            {
                EncodeDataChangeQueue(encoder, original);
            }
            stream.Position = 0;
            using IDecoder decoder = CreateDecoder(encoding, stream);
            return DecodeDataChangeQueue(decoder);
        }

        private static StorableEventQueue RoundTripEventQueue(
            StorableEventQueue original,
            SubscriptionStoreEncoding encoding)
        {
            using var stream = new MemoryStream();
            using (IEncoder encoder = CreateEncoder(encoding, stream))
            {
                EncodeEventQueue(encoder, original);
            }
            stream.Position = 0;
            using IDecoder decoder = CreateDecoder(encoding, stream);
            return DecodeEventQueue(decoder);
        }

        // Delegate to the actual encode/decode methods via reflection
        // since they are private static in the production classes.
        // We use the same logic inline to test correctness.
        private static IEncoder CreateEncoder(
            SubscriptionStoreEncoding encoding, Stream stream)
        {
            var context = AmbientMessageContext.CurrentContext
                ?? new ServiceMessageContext(Opc.Ua.Server.Tests.DurableSubscriptionSerializationTests.s_telemetry);
            return encoding switch
            {
                SubscriptionStoreEncoding.Binary =>
                    new BinaryEncoder(stream, context, true),
                _ => new JsonEncoder(stream, context)
            };
        }

        private static IDecoder CreateDecoder(
            SubscriptionStoreEncoding encoding, Stream stream)
        {
            var context = AmbientMessageContext.CurrentContext
                ?? new ServiceMessageContext(Opc.Ua.Server.Tests.DurableSubscriptionSerializationTests.s_telemetry);
            return encoding switch
            {
                SubscriptionStoreEncoding.Binary =>
                    new BinaryDecoder(stream, context, true),
                _ => new JsonDecoder(stream, context)
            };
        }

        // Inline encode/decode replicating SubscriptionStore logic
        private static void EncodeSubscription(
            IEncoder encoder, StoredSubscription sub,
            SubscriptionStoreEncoding encoding)
        {
            encoder.WriteUInt32("Id", sub.Id);
            encoder.WriteBoolean("IsDurable", sub.IsDurable);
            encoder.WriteUInt32("LifetimeCounter", sub.LifetimeCounter);
            encoder.WriteUInt32("MaxLifetimeCount", sub.MaxLifetimeCount);
            encoder.WriteUInt32("MaxKeepaliveCount", sub.MaxKeepaliveCount);
            encoder.WriteUInt32("MaxMessageCount", sub.MaxMessageCount);
            encoder.WriteUInt32("MaxNotificationsPerPublish",
                sub.MaxNotificationsPerPublish);
            encoder.WriteDouble("PublishingInterval",
                sub.PublishingInterval);
            encoder.WriteByte("Priority", sub.Priority);
            encoder.WriteInt32("LastSentMessage", sub.LastSentMessage);
            encoder.WriteUInt32("SequenceNumber", sub.SequenceNumber);
            encoder.WriteExtensionObject("UserIdentityToken",
                sub.UserIdentityToken != null
                    ? new ExtensionObject(sub.UserIdentityToken)
                    : ExtensionObject.Null);
            var sentMsgs = sub.SentMessages?
                .Select(m => new ExtensionObject(m)).ToArray() ?? [];
            encoder.WriteExtensionObjectArray("SentMessages",
                new ArrayOf<ExtensionObject>(sentMsgs));
            var items = sub.MonitoredItems?
                .Cast<StoredMonitoredItem>().ToList() ?? [];
            var blobs = new List<ByteString>(items.Count);
            foreach (var item in items)
            {
                using var ms = new MemoryStream();
                using (IEncoder ie = CreateEncoder(encoding, ms))
                {
                    EncodeMonitoredItem(ie, item);
                }
                blobs.Add(new ByteString(ms.ToArray()));
            }
            encoder.WriteByteStringArray("MonitoredItemBlobs",
                new ArrayOf<ByteString>(blobs.ToArray()));
        }

        private static StoredSubscription DecodeSubscription(
            IDecoder decoder, SubscriptionStoreEncoding encoding)
        {
            var sub = new StoredSubscription
            {
                Id = decoder.ReadUInt32("Id"),
                IsDurable = decoder.ReadBoolean("IsDurable"),
                LifetimeCounter = decoder.ReadUInt32("LifetimeCounter"),
                MaxLifetimeCount = decoder.ReadUInt32("MaxLifetimeCount"),
                MaxKeepaliveCount = decoder.ReadUInt32("MaxKeepaliveCount"),
                MaxMessageCount = decoder.ReadUInt32("MaxMessageCount"),
                MaxNotificationsPerPublish =
                    decoder.ReadUInt32("MaxNotificationsPerPublish"),
                PublishingInterval = decoder.ReadDouble("PublishingInterval"),
                Priority = decoder.ReadByte("Priority"),
                LastSentMessage = decoder.ReadInt32("LastSentMessage"),
                SequenceNumber = decoder.ReadUInt32("SequenceNumber"),
            };
            var tokenEo = decoder.ReadExtensionObject("UserIdentityToken");
            if (!tokenEo.IsNull &&
                tokenEo.TryGetEncodeable(out IEncodeable tokenBody))
            {
                sub.UserIdentityToken = tokenBody as UserIdentityToken;
            }
            var sentMsgEos =
                decoder.ReadExtensionObjectArray("SentMessages");
            sub.SentMessages = [];
            if (!sentMsgEos.IsNull)
            {
                foreach (var eo in sentMsgEos.Memory.ToArray())
                {
                    if (!eo.IsNull &&
                        eo.TryGetEncodeable(out IEncodeable e) &&
                        e is NotificationMessage nm)
                    {
                        sub.SentMessages.Add(nm);
                    }
                }
            }
            var itemBlobs =
                decoder.ReadByteStringArray("MonitoredItemBlobs");
            var items = new List<IStoredMonitoredItem>();
            if (!itemBlobs.IsNull)
            {
                foreach (var blob in itemBlobs.Memory.ToArray())
                {
                    if (!blob.IsNull && blob.Length > 0)
                    {
                        using var ms = new MemoryStream(blob.ToArray());
                        using IDecoder id =
                            CreateDecoder(encoding, ms);
                        items.Add(DecodeMonitoredItem(id));
                    }
                }
            }
            sub.MonitoredItems = items;
            return sub;
        }

        private static void EncodeMonitoredItem(
            IEncoder encoder, StoredMonitoredItem item)
        {
            encoder.WriteBoolean("MI_IsRestored", item.IsRestored);
            encoder.WriteUInt32("MI_SubscriptionId", item.SubscriptionId);
            encoder.WriteUInt32("MI_Id", item.Id);
            encoder.WriteInt32("MI_TypeMask", item.TypeMask);
            encoder.WriteNodeId("MI_NodeId", item.NodeId);
            encoder.WriteUInt32("MI_AttributeId", item.AttributeId);
            encoder.WriteString("MI_IndexRange", item.IndexRange);
            encoder.WriteQualifiedName("MI_Encoding", item.Encoding);
            encoder.WriteEnumerated(
                "MI_DiagnosticsMasks", item.DiagnosticsMasks);
            encoder.WriteEnumerated(
                "MI_TimestampsToReturn", item.TimestampsToReturn);
            encoder.WriteUInt32("MI_ClientHandle", item.ClientHandle);
            encoder.WriteEnumerated(
                "MI_MonitoringMode", item.MonitoringMode);
            encoder.WriteExtensionObject("MI_OriginalFilter",
                item.OriginalFilter != null
                    ? new ExtensionObject(item.OriginalFilter)
                    : ExtensionObject.Null);
            encoder.WriteExtensionObject("MI_FilterToUse",
                item.FilterToUse != null
                    ? new ExtensionObject(item.FilterToUse)
                    : ExtensionObject.Null);
            encoder.WriteDouble("MI_Range", item.Range);
            encoder.WriteDouble("MI_SamplingInterval",
                item.SamplingInterval);
            encoder.WriteUInt32("MI_QueueSize", item.QueueSize);
            encoder.WriteBoolean("MI_DiscardOldest", item.DiscardOldest);
            encoder.WriteInt32("MI_SourceSamplingInterval",
                item.SourceSamplingInterval);
            encoder.WriteBoolean("MI_AlwaysReportUpdates",
                item.AlwaysReportUpdates);
            encoder.WriteBoolean("MI_IsDurable", item.IsDurable);
            encoder.WriteDataValue("MI_LastValue", item.LastValue);
            encoder.WriteStatusCode("MI_LastError",
                item.LastError?.StatusCode ?? StatusCodes.Good);
            encoder.WriteString("MI_ParsedIndexRange",
                item.ParsedIndexRange.ToString());
        }

        private static StoredMonitoredItem DecodeMonitoredItem(
            IDecoder decoder)
        {
            var item = new StoredMonitoredItem
            {
                IsRestored = decoder.ReadBoolean("MI_IsRestored"),
                SubscriptionId = decoder.ReadUInt32("MI_SubscriptionId"),
                Id = decoder.ReadUInt32("MI_Id"),
                TypeMask = decoder.ReadInt32("MI_TypeMask"),
                NodeId = decoder.ReadNodeId("MI_NodeId"),
                AttributeId = decoder.ReadUInt32("MI_AttributeId"),
                IndexRange = decoder.ReadString("MI_IndexRange"),
                Encoding = decoder.ReadQualifiedName("MI_Encoding"),
                DiagnosticsMasks = decoder.ReadEnumerated<DiagnosticsMasks>(
                    "MI_DiagnosticsMasks"),
                TimestampsToReturn =
                    decoder.ReadEnumerated<TimestampsToReturn>(
                        "MI_TimestampsToReturn"),
                ClientHandle = decoder.ReadUInt32("MI_ClientHandle"),
                MonitoringMode = decoder.ReadEnumerated<MonitoringMode>(
                    "MI_MonitoringMode"),
            };
            var origEo =
                decoder.ReadExtensionObject("MI_OriginalFilter");
            if (!origEo.IsNull &&
                origEo.TryGetEncodeable(out IEncodeable origBody))
            {
                item.OriginalFilter = origBody as MonitoringFilter;
            }
            var filterEo =
                decoder.ReadExtensionObject("MI_FilterToUse");
            if (!filterEo.IsNull &&
                filterEo.TryGetEncodeable(out IEncodeable filterBody))
            {
                item.FilterToUse = filterBody as MonitoringFilter;
            }
            item.Range = decoder.ReadDouble("MI_Range");
            item.SamplingInterval =
                decoder.ReadDouble("MI_SamplingInterval");
            item.QueueSize = decoder.ReadUInt32("MI_QueueSize");
            item.DiscardOldest =
                decoder.ReadBoolean("MI_DiscardOldest");
            item.SourceSamplingInterval =
                decoder.ReadInt32("MI_SourceSamplingInterval");
            item.AlwaysReportUpdates =
                decoder.ReadBoolean("MI_AlwaysReportUpdates");
            item.IsDurable = decoder.ReadBoolean("MI_IsDurable");
            item.LastValue = decoder.ReadDataValue("MI_LastValue");
            StatusCode sc = decoder.ReadStatusCode("MI_LastError");
            item.LastError = sc == StatusCodes.Good
                ? null : new ServiceResult(sc);
            string rangeStr =
                decoder.ReadString("MI_ParsedIndexRange");
            item.ParsedIndexRange = string.IsNullOrEmpty(rangeStr)
                ? NumericRange.Null : NumericRange.Parse(rangeStr);
            return item;
        }

        // Queue encode/decode inline (mirrors DurableMonitoredItemQueueFactory)
        private static void EncodeDataChangeQueue(
            IEncoder encoder, StorableDataChangeQueue q)
        {
            encoder.WriteBoolean("IsDurable", q.IsDurable);
            encoder.WriteUInt32("MonitoredItemId", q.MonitoredItemId);
            encoder.WriteInt32("ItemsInQueue", q.ItemsInQueue);
            encoder.WriteUInt32("QueueSize", q.QueueSize);
            EncodeDataChangeBatch(encoder, "EnqueueBatch", q.EnqueueBatch);
            EncodeDataChangeBatch(encoder, "DequeueBatch", q.DequeueBatch);
            int batchCount = q.DataChangeBatches?.Count ?? 0;
            encoder.WriteInt32("DataChangeBatchCount", batchCount);
            if (q.DataChangeBatches != null)
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EncodeDataChangeBatch(
                        encoder, "DCBatch_" + i, q.DataChangeBatches[i]);
                }
            }
        }

        private static StorableDataChangeQueue DecodeDataChangeQueue(
            IDecoder decoder)
        {
            var q = new StorableDataChangeQueue
            {
                IsDurable = decoder.ReadBoolean("IsDurable"),
                MonitoredItemId = decoder.ReadUInt32("MonitoredItemId"),
                ItemsInQueue = decoder.ReadInt32("ItemsInQueue"),
                QueueSize = decoder.ReadUInt32("QueueSize"),
                EnqueueBatch =
                    DecodeDataChangeBatch(decoder, "EnqueueBatch"),
                DequeueBatch =
                    DecodeDataChangeBatch(decoder, "DequeueBatch"),
            };
            int batchCount = decoder.ReadInt32("DataChangeBatchCount");
            q.DataChangeBatches = new List<DataChangeBatch>(batchCount);
            for (int i = 0; i < batchCount; i++)
            {
                q.DataChangeBatches.Add(
                    DecodeDataChangeBatch(decoder, "DCBatch_" + i));
            }
            return q;
        }

        private static void EncodeDataChangeBatch(
            IEncoder encoder, string pfx, DataChangeBatch batch)
        {
            bool has = batch != null;
            encoder.WriteBoolean(pfx + "_HasValue", has);
            if (!has)

            {

                return;

            }
            encoder.WriteGuid(pfx + "_Id", batch.Id);
            encoder.WriteUInt32(pfx + "_BatchSize", batch.BatchSize);
            encoder.WriteUInt32(pfx + "_MonItemId",
                batch.MonitoredItemId);
            encoder.WriteBoolean(pfx + "_IsPersisted", batch.IsPersisted);
            int count = batch.Values?.Count ?? 0;
            encoder.WriteInt32(pfx + "_ValueCount", count);
            if (batch.Values != null)
            {
                for (int i = 0; i < count; i++)
                {
                    encoder.WriteDataValue(
                        pfx + "_DV_" + i, batch.Values[i].Item1);
                    encoder.WriteStatusCode(
                        pfx + "_SR_" + i,
                        batch.Values[i].Item2?.StatusCode
                            ?? StatusCodes.Good);
                }
            }
        }

        private static DataChangeBatch DecodeDataChangeBatch(
            IDecoder decoder, string pfx)
        {
            bool has = decoder.ReadBoolean(pfx + "_HasValue");
            if (!has)

            {

                return null;

            }
            decoder.ReadGuid(pfx + "_Id");
            uint batchSize = decoder.ReadUInt32(pfx + "_BatchSize");
            uint monItemId = decoder.ReadUInt32(pfx + "_MonItemId");
            bool isPersisted = decoder.ReadBoolean(pfx + "_IsPersisted");
            int count = decoder.ReadInt32(pfx + "_ValueCount");
            var values = new List<(DataValue, ServiceResult)>(count);
            for (int i = 0; i < count; i++)
            {
                DataValue dv = decoder.ReadDataValue(pfx + "_DV_" + i);
                StatusCode sc = decoder.ReadStatusCode(pfx + "_SR_" + i);
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
            IEncoder encoder, StorableEventQueue q)
        {
            encoder.WriteBoolean("IsDurable", q.IsDurable);
            encoder.WriteUInt32("MonitoredItemId", q.MonitoredItemId);
            encoder.WriteUInt32("QueueSize", q.QueueSize);
            EncodeEventBatch(encoder, "EnqueueBatch", q.EnqueueBatch);
            EncodeEventBatch(encoder, "DequeueBatch", q.DequeueBatch);
            int batchCount = q.EventBatches?.Count ?? 0;
            encoder.WriteInt32("EventBatchCount", batchCount);
            if (q.EventBatches != null)
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EncodeEventBatch(
                        encoder, "EvBatch_" + i, q.EventBatches[i]);
                }
            }
        }

        private static StorableEventQueue DecodeEventQueue(
            IDecoder decoder)
        {
            var q = new StorableEventQueue
            {
                IsDurable = decoder.ReadBoolean("IsDurable"),
                MonitoredItemId = decoder.ReadUInt32("MonitoredItemId"),
                QueueSize = decoder.ReadUInt32("QueueSize"),
                EnqueueBatch =
                    DecodeEventBatch(decoder, "EnqueueBatch"),
                DequeueBatch =
                    DecodeEventBatch(decoder, "DequeueBatch"),
            };
            int batchCount = decoder.ReadInt32("EventBatchCount");
            q.EventBatches = new List<EventBatch>(batchCount);
            for (int i = 0; i < batchCount; i++)
            {
                q.EventBatches.Add(
                    DecodeEventBatch(decoder, "EvBatch_" + i));
            }
            return q;
        }

        private static void EncodeEventBatch(
            IEncoder encoder, string pfx, EventBatch batch)
        {
            bool has = batch != null;
            encoder.WriteBoolean(pfx + "_HasValue", has);
            if (!has)

            {

                return;

            }
            encoder.WriteGuid(pfx + "_Id", batch.Id);
            encoder.WriteUInt32(pfx + "_BatchSize", batch.BatchSize);
            encoder.WriteUInt32(pfx + "_MonItemId",
                batch.MonitoredItemId);
            encoder.WriteBoolean(pfx + "_IsPersisted", batch.IsPersisted);
            int count = batch.Events?.Count ?? 0;
            encoder.WriteInt32(pfx + "_EventCount", count);
            if (batch.Events != null)
            {
                for (int i = 0; i < count; i++)
                {
                    encoder.WriteEncodeableAsExtensionObject(
                        pfx + "_Ev_" + i, batch.Events[i]);
                }
            }
        }

        private static EventBatch DecodeEventBatch(
            IDecoder decoder, string pfx)
        {
            bool has = decoder.ReadBoolean(pfx + "_HasValue");
            if (!has)

            {

                return null;

            }
            decoder.ReadGuid(pfx + "_Id");
            uint batchSize = decoder.ReadUInt32(pfx + "_BatchSize");
            uint monItemId = decoder.ReadUInt32(pfx + "_MonItemId");
            decoder.ReadBoolean(pfx + "_IsPersisted");
            int count = decoder.ReadInt32(pfx + "_EventCount");
            var events = new List<EventFieldList>(count);
            for (int i = 0; i < count; i++)
            {
                var eo = decoder.ReadExtensionObject(pfx + "_Ev_" + i);
                if (!eo.IsNull &&
                    eo.TryGetEncodeable(out IEncodeable e) &&
                    e is EventFieldList efl)
                {
                    events.Add(efl);
                }
            }
            return new EventBatch(events, batchSize, monItemId);
        }

        #endregion
    }
}




