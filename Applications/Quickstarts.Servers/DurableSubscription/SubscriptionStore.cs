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
    public class SubscriptionStore : ISubscriptionStore
    {
        private static readonly string s_storage_path = Path.Combine(
            Environment.CurrentDirectory,
            "Durable Subscriptions");

        private const string kFilename = "subscriptionsStore.bin";
        private readonly DurableMonitoredItemQueueFactory m_durableMonitoredItemQueueFactory;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
        private readonly IServiceMessageContext m_messageContext;

        public SubscriptionStore(IServerInternal server)
        {
            m_logger = server.Telemetry.CreateLogger<SubscriptionStore>();
            m_telemetry = server.Telemetry;
            m_messageContext = server.MessageContext;
            m_durableMonitoredItemQueueFactory = server
                .MonitoredItemQueueFactory as DurableMonitoredItemQueueFactory;
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
                using (FileStream fileStream = File.Create(
                    Path.Combine(s_storage_path, kFilename)))
                using (var encoder = new BinaryEncoder(
                    fileStream, m_messageContext, true))
                {
                    encoder.WriteStringArray(
                        null, m_messageContext.NamespaceUris.ToArrayOf());
                    encoder.WriteStringArray(
                        null, m_messageContext.ServerUris.ToArrayOf());

                    encoder.WriteInt32(null, subs.Count);
                    foreach (StoredSubscription sub in subs)
                    {
                        EncodeSubscription(encoder, sub);
                    }
                }

                if (m_durableMonitoredItemQueueFactory != null)
                {
                    IEnumerable<uint> ids = subscriptions
                        .SelectMany(s => s.MonitoredItems
                            .Select(m => m.Id));
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
            string filePath = Path.Combine(s_storage_path, kFilename);
            try
            {
                if (File.Exists(filePath))
                {
                    List<IStoredSubscription> result;
                    using (FileStream fileStream = File.OpenRead(filePath))
                    using (var decoder = new BinaryDecoder(
                        fileStream, m_messageContext, true))
                    {
                        ArrayOf<string> nsUris = decoder.ReadStringArray(null);
                        ArrayOf<string> serverUris = decoder.ReadStringArray(null);
                        decoder.SetMappingTables(
                            new NamespaceTable(nsUris.Memory.ToArray()),
                            new StringTable(serverUris.Memory.ToArray()));

                        int count = decoder.ReadInt32(null);
                        result = new List<IStoredSubscription>(count);
                        for (int i = 0; i < count; i++)
                        {
                            result.Add(DecodeSubscription(decoder));
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
            string filePath = Path.Combine(s_storage_path, kFilename);

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

        public static void EncodeSubscription(
            BinaryEncoder encoder, StoredSubscription subscription)
        {
            encoder.WriteUInt32(null, subscription.Id);
            encoder.WriteBoolean(null, subscription.IsDurable);
            encoder.WriteUInt32(null, subscription.LifetimeCounter);
            encoder.WriteUInt32(null, subscription.MaxLifetimeCount);
            encoder.WriteUInt32(null, subscription.MaxKeepaliveCount);
            encoder.WriteUInt32(null, subscription.MaxMessageCount);
            encoder.WriteUInt32(null, subscription.MaxNotificationsPerPublish);
            encoder.WriteDouble(null, subscription.PublishingInterval);
            encoder.WriteByte(null, subscription.Priority);
            encoder.WriteInt32(null, subscription.LastSentMessage);
            encoder.WriteUInt32(null, subscription.SequenceNumber);

            encoder.WriteExtensionObject(null,
                subscription.UserIdentityToken != null
                    ? new ExtensionObject(subscription.UserIdentityToken)
                    : ExtensionObject.Null);

            ExtensionObject[] sentMsgs = subscription.SentMessages?
                .Select(m => new ExtensionObject(m)).ToArray() ??
                [];
            encoder.WriteExtensionObjectArray(null,
                new ArrayOf<ExtensionObject>(sentMsgs));

            List<StoredMonitoredItem> items = subscription.MonitoredItems?
                .Cast<StoredMonitoredItem>().ToList() ??
                [];
            encoder.WriteInt32(null, items.Count);
            foreach (StoredMonitoredItem item in items)
            {
                EncodeMonitoredItem(encoder, item);
            }
        }

        internal static void EncodeMonitoredItem(
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

        public static StoredSubscription DecodeSubscription(BinaryDecoder decoder)
        {
            var subscription = new StoredSubscription
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
                SequenceNumber = decoder.ReadUInt32(null)
            };

            ExtensionObject tokenEo = decoder.ReadExtensionObject(null);
            if (!tokenEo.IsNull && tokenEo.TryGetValue(out IEncodeable tokenBody))
            {
                subscription.UserIdentityToken = tokenBody as UserIdentityToken;
            }

            ArrayOf<ExtensionObject> sentMsgEos =
                decoder.ReadExtensionObjectArray(null);
            var sentList = new List<NotificationMessage>();
            if (!sentMsgEos.IsNull)
            {
                foreach (ExtensionObject eo in sentMsgEos.Memory.ToArray())
                {
                    if (!eo.IsNull &&
                        eo.TryGetValue(out IEncodeable e) &&
                        e is NotificationMessage nm)
                    {
                        sentList.Add(nm);
                    }
                }
            }
            subscription.SentMessages = sentList;

            int itemCount = decoder.ReadInt32(null);
            var items = new List<IStoredMonitoredItem>(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                items.Add(DecodeMonitoredItem(decoder));
            }
            subscription.MonitoredItems = items;
            return subscription;
        }

        internal static StoredMonitoredItem DecodeMonitoredItem(BinaryDecoder decoder)
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
                TimestampsToReturn = decoder.ReadEnumerated<TimestampsToReturn>(null),
                ClientHandle = decoder.ReadUInt32(null),
                MonitoringMode = decoder.ReadEnumerated<MonitoringMode>(null)
            };

            ExtensionObject origFilterEo = decoder.ReadExtensionObject(null);
            if (!origFilterEo.IsNull && origFilterEo.TryGetValue(out IEncodeable origBody))
            {
                item.OriginalFilter = origBody as MonitoringFilter;
            }

            ExtensionObject filterEo = decoder.ReadExtensionObject(null);
            if (!filterEo.IsNull && filterEo.TryGetValue(out IEncodeable filterBody))
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

            StatusCode lastErrorStatus = decoder.ReadStatusCode(null);
            item.LastError = lastErrorStatus == StatusCodes.Good
                ? null : new ServiceResult(lastErrorStatus);

            string rangeStr = decoder.ReadString(null);
            item.ParsedIndexRange = string.IsNullOrEmpty(rangeStr)
                ? NumericRange.Null : NumericRange.Parse(rangeStr);

            return item;
        }
    }
}
