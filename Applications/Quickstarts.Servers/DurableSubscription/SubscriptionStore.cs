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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    /// <summary>
    /// Specifies the encoding format used by <see cref="SubscriptionStore"/>.
    /// </summary>
    public enum SubscriptionStoreEncoding
    {
        /// <summary>
        /// OPC UA JSON encoding.
        /// </summary>
        Json,

        /// <summary>
        /// OPC UA binary encoding.
        /// </summary>
        Binary
    }

    public class SubscriptionStore : ISubscriptionStore
    {
        private static readonly string s_storage_path = Path.Combine(
            Environment.CurrentDirectory,
            "Durable Subscriptions");

        private readonly SubscriptionStoreEncoding m_encoding;
        private readonly string m_filename;
        private readonly DurableMonitoredItemQueueFactory m_durableMonitoredItemQueueFactory;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
        private readonly IServiceMessageContext m_messageContext;

        public SubscriptionStore(
            IServerInternal server,
            SubscriptionStoreEncoding encoding = SubscriptionStoreEncoding.Json)
        {
            m_logger = server.Telemetry.CreateLogger<SubscriptionStore>();
            m_telemetry = server.Telemetry;
            m_messageContext = server.MessageContext;
            m_durableMonitoredItemQueueFactory = server
                .MonitoredItemQueueFactory as DurableMonitoredItemQueueFactory;
            m_encoding = encoding;
            m_filename = encoding switch
            {
                SubscriptionStoreEncoding.Binary => "subscriptionsStore.bin",
                _ => "subscriptionsStore.json"
            };
        }

        public bool StoreSubscriptions(IEnumerable<IStoredSubscription> subscriptions)
        {
            try
            {
                if (!Directory.Exists(s_storage_path))
                {
                    Directory.CreateDirectory(s_storage_path);
                }

                var subs = subscriptions.Cast<StoredSubscription>().ToList();
                using (FileStream fileStream = File.Create(Path.Combine(s_storage_path, m_filename)))
                using (IEncoder encoder = CreateEncoder(fileStream))
                {
                    var subBlobs = new List<ByteString>(subs.Count);
                    foreach (StoredSubscription sub in subs)
                    {
                        using var subStream = new MemoryStream();
                        using (IEncoder subEncoder = CreateEncoder(subStream))
                        {
                            EncodeSubscription(subEncoder, sub);
                        }
                        subBlobs.Add(new ByteString(subStream.ToArray()));
                    }
                    encoder.WriteByteStringArray("SubscriptionBlobs",
                        new ArrayOf<ByteString>(subBlobs.ToArray()));
                }

                if (m_durableMonitoredItemQueueFactory != null)
                {
                    IEnumerable<uint> ids = subscriptions.SelectMany(
                        s => s.MonitoredItems.Select(m => m.Id));
                    m_durableMonitoredItemQueueFactory.PersistQueues(ids, s_storage_path);
                }
                return true;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to store subscriptions");
            }
            return false;
        }

        public RestoreSubscriptionResult RestoreSubscriptions()
        {
            string filePath = Path.Combine(s_storage_path, m_filename);
            try
            {
                if (File.Exists(filePath))
                {
                    List<IStoredSubscription> result;
                    using (FileStream fileStream = File.OpenRead(filePath))
                    using (IDecoder decoder = CreateDecoder(fileStream))
                    {
                        ArrayOf<ByteString> subBlobs =
                            decoder.ReadByteStringArray("SubscriptionBlobs");
                        result = [];
                        if (!subBlobs.IsNull)
                        {
                            foreach (ByteString blob in subBlobs.Memory.ToArray())
                            {
                                if (!blob.IsNull && blob.Length > 0)
                                {
                                    using var subStream = new MemoryStream(blob.ToArray());
                                    using IDecoder subDecoder = CreateDecoder(subStream);
                                    result.Add(DecodeSubscription(subDecoder));
                                }
                            }
                        }
                    }

                    File.Delete(filePath);
                    return new RestoreSubscriptionResult(true, result);
                }
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to restore subscriptions");
            }

            return new RestoreSubscriptionResult(false, null);
        }

        public IDataChangeMonitoredItemQueue RestoreDataChangeMonitoredItemQueue(
            uint monitoredItemId)
        {
            return m_durableMonitoredItemQueueFactory?.RestoreDataChangeQueue(
                monitoredItemId,
                s_storage_path);
        }

        public IEventMonitoredItemQueue RestoreEventMonitoredItemQueue(uint monitoredItemId)
        {
            return m_durableMonitoredItemQueueFactory?.RestoreEventQueue(
                monitoredItemId,
                s_storage_path);
        }

        public void OnSubscriptionRestoreComplete(Dictionary<uint, ArrayOf<uint>> createdSubscriptions)
        {
            string filePath = Path.Combine(s_storage_path, m_filename);

            // remove old file
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Failed to cleanup files for stored subscsription");
                }
            }
            // remove old batches & queues
            if (m_durableMonitoredItemQueueFactory != null)
            {
                IEnumerable<uint> ids = createdSubscriptions.SelectMany(s => s.Value.ToArray());
                m_durableMonitoredItemQueueFactory.CleanStoredQueues(s_storage_path, ids);
            }
        }

        private void EncodeSubscription(IEncoder encoder, StoredSubscription subscription)
        {
            encoder.WriteUInt32("Id", subscription.Id);
            encoder.WriteBoolean("IsDurable", subscription.IsDurable);
            encoder.WriteUInt32("LifetimeCounter", subscription.LifetimeCounter);
            encoder.WriteUInt32("MaxLifetimeCount", subscription.MaxLifetimeCount);
            encoder.WriteUInt32("MaxKeepaliveCount", subscription.MaxKeepaliveCount);
            encoder.WriteUInt32("MaxMessageCount", subscription.MaxMessageCount);
            encoder.WriteUInt32("MaxNotificationsPerPublish",
                subscription.MaxNotificationsPerPublish);
            encoder.WriteDouble("PublishingInterval", subscription.PublishingInterval);
            encoder.WriteByte("Priority", subscription.Priority);
            encoder.WriteInt32("LastSentMessage", subscription.LastSentMessage);
            encoder.WriteUInt32("SequenceNumber", subscription.SequenceNumber);

            encoder.WriteExtensionObject("UserIdentityToken",
                subscription.UserIdentityToken != null
                    ? new ExtensionObject(subscription.UserIdentityToken)
                    : ExtensionObject.Null);

            ExtensionObject[] sentMsgs = subscription.SentMessages?
                .Select(m => new ExtensionObject(m)).ToArray() ??
                [];
            encoder.WriteExtensionObjectArray("SentMessages",
                new ArrayOf<ExtensionObject>(sentMsgs));

            List<StoredMonitoredItem> items = subscription.MonitoredItems?
                .Cast<StoredMonitoredItem>().ToList() ??
                [];
            var itemBlobs = new List<ByteString>(items.Count);
            foreach (StoredMonitoredItem item in items)
            {
                using var itemStream = new MemoryStream();
                using (IEncoder itemEncoder = CreateEncoder(itemStream))
                {
                    EncodeMonitoredItem(itemEncoder, item);
                }
                itemBlobs.Add(new ByteString(itemStream.ToArray()));
            }
            encoder.WriteByteStringArray("MonitoredItemBlobs",
                new ArrayOf<ByteString>(itemBlobs.ToArray()));
        }

        private void EncodeMonitoredItem(IEncoder encoder, StoredMonitoredItem item)
        {
            encoder.WriteBoolean("IsRestored", item.IsRestored);
            encoder.WriteUInt32("SubscriptionId", item.SubscriptionId);
            encoder.WriteUInt32("Id", item.Id);
            encoder.WriteInt32("TypeMask", item.TypeMask);
            encoder.WriteNodeId("NodeId", item.NodeId);
            encoder.WriteUInt32("AttributeId", item.AttributeId);
            encoder.WriteString("IndexRange", item.IndexRange);
            encoder.WriteQualifiedName("Encoding", item.Encoding);
            encoder.WriteEnumerated("DiagnosticsMasks", item.DiagnosticsMasks);
            encoder.WriteEnumerated("TimestampsToReturn", item.TimestampsToReturn);
            encoder.WriteUInt32("ClientHandle", item.ClientHandle);
            encoder.WriteEnumerated("MonitoringMode", item.MonitoringMode);
            encoder.WriteExtensionObject("OriginalFilter",
                item.OriginalFilter != null
                    ? new ExtensionObject(item.OriginalFilter)
                    : ExtensionObject.Null);
            encoder.WriteExtensionObject("FilterToUse",
                item.FilterToUse != null
                    ? new ExtensionObject(item.FilterToUse)
                    : ExtensionObject.Null);
            encoder.WriteDouble("Range", item.Range);
            encoder.WriteDouble("SamplingInterval", item.SamplingInterval);
            encoder.WriteUInt32("QueueSize", item.QueueSize);
            encoder.WriteBoolean("DiscardOldest", item.DiscardOldest);
            encoder.WriteInt32("SourceSamplingInterval", item.SourceSamplingInterval);
            encoder.WriteBoolean("AlwaysReportUpdates", item.AlwaysReportUpdates);
            encoder.WriteBoolean("IsDurable", item.IsDurable);
            encoder.WriteDataValue("LastValue", item.LastValue);
            encoder.WriteStatusCode("LastError",
                item.LastError?.StatusCode ?? StatusCodes.Good);
            encoder.WriteString("ParsedIndexRange", item.ParsedIndexRange.ToString());
        }

        private StoredSubscription DecodeSubscription(IDecoder decoder)
        {
            var subscription = new StoredSubscription
            {
                Id = decoder.ReadUInt32("Id"),
                IsDurable = decoder.ReadBoolean("IsDurable"),
                LifetimeCounter = decoder.ReadUInt32("LifetimeCounter"),
                MaxLifetimeCount = decoder.ReadUInt32("MaxLifetimeCount"),
                MaxKeepaliveCount = decoder.ReadUInt32("MaxKeepaliveCount"),
                MaxMessageCount = decoder.ReadUInt32("MaxMessageCount"),
                MaxNotificationsPerPublish = decoder.ReadUInt32("MaxNotificationsPerPublish"),
                PublishingInterval = decoder.ReadDouble("PublishingInterval"),
                Priority = decoder.ReadByte("Priority"),
                LastSentMessage = decoder.ReadInt32("LastSentMessage"),
                SequenceNumber = decoder.ReadUInt32("SequenceNumber"),
            };

            ExtensionObject tokenEo = decoder.ReadExtensionObject("UserIdentityToken");
            if (!tokenEo.IsNull && tokenEo.TryGetEncodeable(out IEncodeable tokenBody))
            {
                subscription.UserIdentityToken = tokenBody as UserIdentityToken;
            }

            ArrayOf<ExtensionObject> sentMsgEos =
                decoder.ReadExtensionObjectArray("SentMessages");
            var sentList = new List<NotificationMessage>();
            if (!sentMsgEos.IsNull)
            {
                foreach (ExtensionObject eo in sentMsgEos.Memory.ToArray())
                {
                    if (!eo.IsNull &&
                        eo.TryGetEncodeable(out IEncodeable e) &&
                        e is NotificationMessage nm)
                    {
                        sentList.Add(nm);
                    }
                }
            }
            subscription.SentMessages = sentList;

            ArrayOf<ByteString> itemBlobs =
                decoder.ReadByteStringArray("MonitoredItemBlobs");
            var items = new List<IStoredMonitoredItem>();
            if (!itemBlobs.IsNull)
            {
                foreach (ByteString blob in itemBlobs.Memory.ToArray())
                {
                    if (!blob.IsNull && blob.Length > 0)
                    {
                        using var itemStream = new MemoryStream(blob.ToArray());
                        using IDecoder itemDecoder =
                            CreateDecoder(itemStream);
                        items.Add(DecodeMonitoredItem(itemDecoder));
                    }
                }
            }
            subscription.MonitoredItems = items;
            return subscription;
        }

        private static StoredMonitoredItem DecodeMonitoredItem(IDecoder decoder)
        {
            var item = new StoredMonitoredItem
            {
                IsRestored = decoder.ReadBoolean("IsRestored"),
                SubscriptionId = decoder.ReadUInt32("SubscriptionId"),
                Id = decoder.ReadUInt32("Id"),
                TypeMask = decoder.ReadInt32("TypeMask"),
                NodeId = decoder.ReadNodeId("NodeId"),
                AttributeId = decoder.ReadUInt32("AttributeId"),
                IndexRange = decoder.ReadString("IndexRange"),
                Encoding = decoder.ReadQualifiedName("Encoding"),
                DiagnosticsMasks = decoder.ReadEnumerated<DiagnosticsMasks>("DiagnosticsMasks"),
                TimestampsToReturn = decoder.ReadEnumerated<TimestampsToReturn>("TimestampsToReturn"),
                ClientHandle = decoder.ReadUInt32("ClientHandle"),
                MonitoringMode = decoder.ReadEnumerated<MonitoringMode>("MonitoringMode"),
            };

            ExtensionObject origFilterEo = decoder.ReadExtensionObject("OriginalFilter");
            if (!origFilterEo.IsNull && origFilterEo.TryGetEncodeable(out IEncodeable origBody))
            {
                item.OriginalFilter = origBody as MonitoringFilter;
            }

            ExtensionObject filterEo = decoder.ReadExtensionObject("FilterToUse");
            if (!filterEo.IsNull && filterEo.TryGetEncodeable(out IEncodeable filterBody))
            {
                item.FilterToUse = filterBody as MonitoringFilter;
            }

            item.Range = decoder.ReadDouble("Range");
            item.SamplingInterval = decoder.ReadDouble("SamplingInterval");
            item.QueueSize = decoder.ReadUInt32("QueueSize");
            item.DiscardOldest = decoder.ReadBoolean("DiscardOldest");
            item.SourceSamplingInterval = decoder.ReadInt32("SourceSamplingInterval");
            item.AlwaysReportUpdates = decoder.ReadBoolean("AlwaysReportUpdates");
            item.IsDurable = decoder.ReadBoolean("IsDurable");
            item.LastValue = decoder.ReadDataValue("LastValue");

            StatusCode lastErrorStatus = decoder.ReadStatusCode("LastError");
            item.LastError = lastErrorStatus == StatusCodes.Good
                ? null : new ServiceResult(lastErrorStatus);

            string rangeStr = decoder.ReadString("ParsedIndexRange");
            item.ParsedIndexRange = string.IsNullOrEmpty(rangeStr)
                ? NumericRange.Null : NumericRange.Parse(rangeStr);

            return item;
        }

        private IEncoder CreateEncoder(Stream stream)
        {
            return m_encoding switch
            {
                SubscriptionStoreEncoding.Binary =>
                    new BinaryEncoder(stream, m_messageContext, true),
                _ => new JsonEncoder(stream, m_messageContext)
            };
        }

        private IDecoder CreateDecoder(Stream stream)
        {
            return m_encoding switch
            {
                SubscriptionStoreEncoding.Binary =>
                    new BinaryDecoder(stream, m_messageContext, true),
                _ => new JsonDecoder(stream, m_messageContext)
            };
        }
    }
}
