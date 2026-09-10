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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    /// <summary>
    /// Persists sample server subscriptions and their durable monitored-item queues to local files.
    /// </summary>
    public class SubscriptionStore : ISubscriptionStore
    {
        private static readonly string s_storage_path = Path.Combine(
            Environment.CurrentDirectory,
            "Durable Subscriptions");

        private const string kFilename = "subscriptionsStore.bin";
        private const uint kStoreMagic = 0x44535541;
        private const uint kStoreVersion = 1;
        private readonly DurableMonitoredItemQueueFactory? m_durableMonitoredItemQueueFactory;
        private readonly ILogger m_logger;
        private readonly IServiceMessageContext m_messageContext;

        /// <summary>
        /// Initializes the store with the server's message context, telemetry, and durable queue factory.
        /// </summary>
        public SubscriptionStore(IServerInternal server)
        {
            m_logger = server.Telemetry.CreateLogger<SubscriptionStore>();
            m_messageContext = server.MessageContext;
            m_durableMonitoredItemQueueFactory = server
                .MonitoredItemQueueFactory as DurableMonitoredItemQueueFactory;
        }

        /// <summary>
        /// Saves subscriptions and their available durable queues, reporting whether persistence succeeded.
        /// </summary>
        public ValueTask<bool> StoreSubscriptionsAsync(
            IEnumerable<IStoredSubscription> subscriptions,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(StoreSubscriptionsCore(subscriptions));
        }

        private bool StoreSubscriptionsCore(IEnumerable<IStoredSubscription> subscriptions)
        {
            try
            {
                if (!Directory.Exists(s_storage_path))
                {
                    Directory.CreateDirectory(s_storage_path);
                }

                var subs = subscriptions.Cast<StoredSubscription>().ToList();
                // Validate identities before File.Create can truncate an existing store.
                foreach (StoredSubscription sub in subs)
                {
                    _ = SanitizeUserIdentityToken(sub.UserIdentityToken);
                }

                using (FileStream fileStream = File.Create(
                    Path.Combine(s_storage_path, kFilename)))
                using (var encoder = new BinaryEncoder(
                    fileStream, m_messageContext, true))
                {
                    WriteStoreHeader(encoder);
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
                m_logger.FailedToStoreSubscriptions(ex);
            }
            return false;
        }

        /// <summary>
        /// Restores subscriptions from the local store and removes the successfully read subscription file.
        /// </summary>
        public ValueTask<RestoreSubscriptionResult> RestoreSubscriptionsAsync(
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<RestoreSubscriptionResult>(RestoreSubscriptionsCore());
        }

        private RestoreSubscriptionResult RestoreSubscriptionsCore()
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
                        uint version = ValidateStoreHeader(decoder);
                        ArrayOf<string> nsUris = decoder.ReadStringArray(null)!;
                        ArrayOf<string> serverUris = decoder.ReadStringArray(null)!;
                        decoder.SetMappingTables(
                            new NamespaceTable(nsUris.Memory.ToArray()),
                            new StringTable(serverUris.Memory.ToArray()));

                        int count = decoder.ReadInt32(null);
                        result = new List<IStoredSubscription>(count);
                        for (int i = 0; i < count; i++)
                        {
                            result.Add(DecodeSubscription(decoder, version));
                        }
                    }

                    File.Delete(filePath);
                    return new RestoreSubscriptionResult(true, result);
                }
            }
            catch (Exception ex)
            {
                m_logger.FailedToRestoreSubscriptions(ex);
            }

            return new RestoreSubscriptionResult(false, null);
        }

        /// <summary>
        /// Restores a monitored item's persisted data-change queue through the durable queue factory.
        /// </summary>
        public IDataChangeMonitoredItemQueue RestoreDataChangeMonitoredItemQueue(
            uint monitoredItemId)
        {
            return m_durableMonitoredItemQueueFactory?.RestoreDataChangeQueue(
                monitoredItemId,
                s_storage_path)!;
        }

        /// <summary>
        /// Restores a monitored item's persisted event queue through the durable queue factory.
        /// </summary>
        public IEventMonitoredItemQueue RestoreEventMonitoredItemQueue(uint monitoredItemId)
        {
            return m_durableMonitoredItemQueueFactory?.RestoreEventQueue(
                monitoredItemId,
                s_storage_path)!;
        }

        /// <summary>
        /// Returns the restored data-change queue, or null when no durable queue factory is available.
        /// </summary>
        public ValueTask<IDataChangeMonitoredItemQueue?> RestoreDataChangeMonitoredItemQueueAsync(
            uint monitoredItemId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<IDataChangeMonitoredItemQueue?>(
                RestoreDataChangeMonitoredItemQueue(monitoredItemId));
        }

        /// <summary>
        /// Returns the restored event queue, or null when no durable queue factory is available.
        /// </summary>
        public ValueTask<IEventMonitoredItemQueue?> RestoreEventMonitoredItemQueueAsync(
            uint monitoredItemId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<IEventMonitoredItemQueue?>(
                RestoreEventMonitoredItemQueue(monitoredItemId));
        }

        /// <summary>
        /// Cleans up persisted subscription and queue files after subscription restoration completes.
        /// </summary>
        public ValueTask OnSubscriptionRestoreCompleteAsync(
            Dictionary<uint, ArrayOf<uint>> createdSubscriptions,
            CancellationToken cancellationToken = default)
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
                    m_logger.FailedToCleanupStoredSubscriptionFiles(ex);
                }
            }
            // remove old batches & queues
            if (m_durableMonitoredItemQueueFactory != null)
            {
                IEnumerable<uint> ids = createdSubscriptions.SelectMany(s => s.Value.Memory.ToArray());
                m_durableMonitoredItemQueueFactory.CleanStoredQueues(s_storage_path, ids);
            }
            return default;
        }

        /// <summary>
        /// Writes the durable subscription store's format marker and version.
        /// </summary>
        internal static void WriteStoreHeader(BinaryEncoder encoder)
        {
            encoder.WriteUInt32(null, kStoreMagic);
            encoder.WriteUInt32(null, kStoreVersion);
        }

        /// <summary>
        /// Validates the store's format marker and returns its supported serialization version.
        /// </summary>
        /// <exception cref="InvalidDataException">The header or serialization version is not supported.</exception>
        internal static uint ValidateStoreHeader(BinaryDecoder decoder)
        {
            uint magic = decoder.ReadUInt32(null);
            if (magic != kStoreMagic)
            {
                throw new InvalidDataException(
                    "The durable subscription store has an invalid header or uses the " +
                    "legacy unsafe format.");
            }

            uint version = decoder.ReadUInt32(null);
            ValidateStoreVersion(version);
            return version;
        }

        /// <summary>
        /// Encodes subscription state, a credential-safe identity, sent messages, and monitored items.
        /// </summary>
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

            UserIdentityToken? sanitizedIdentityToken =
                SanitizeUserIdentityToken(subscription.UserIdentityToken);
            encoder.WriteExtensionObject(null,
                sanitizedIdentityToken != null
                    ? new ExtensionObject(sanitizedIdentityToken)
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

        /// <summary>
        /// Encodes the monitored-item settings and last sampled value retained for restoration.
        /// </summary>
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

        /// <summary>
        /// Decodes subscription state and monitored items using the specified store version.
        /// </summary>
        public static StoredSubscription DecodeSubscription(
            BinaryDecoder decoder,
            uint version = kStoreVersion)
        {
            ValidateStoreVersion(version);
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
            if (!tokenEo.IsNull && tokenEo.TryGetValue(out IEncodeable? tokenBody))
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
                        eo.TryGetValue(out IEncodeable? e) &&
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

        /// <summary>
        /// Copies supported identity tokens without passwords and rejects unsafe token types.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// An issued or unsupported identity-token type cannot be persisted safely.
        /// </exception>
        internal static UserIdentityToken? SanitizeUserIdentityToken(
            UserIdentityToken? identityToken)
        {
            return identityToken switch
            {
                null => null,
                AnonymousIdentityToken anonymous => new AnonymousIdentityToken
                {
                    PolicyId = anonymous.PolicyId
                },
                UserNameIdentityToken userName => new UserNameIdentityToken
                {
                    PolicyId = userName.PolicyId,
                    UserName = userName.UserName,
                    Password = default,
                    EncryptionAlgorithm = null
                },
                X509IdentityToken x509 => new X509IdentityToken
                {
                    PolicyId = x509.PolicyId,
                    CertificateData = x509.CertificateData
                },
                IssuedIdentityToken => throw new NotSupportedException(
                    "Durable subscriptions owned by issued-token identities cannot be " +
                    "persisted without storing bearer credentials."),
                _ => throw new NotSupportedException(
                    $"User identity token type '{identityToken.GetType().Name}' is not safe " +
                    "for durable subscription persistence.")
            };
        }

        /// <summary>
        /// Decodes persisted monitored-item state in the current store format.
        /// </summary>
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
                IndexRange = decoder.ReadString(null)!,
                Encoding = decoder.ReadQualifiedName(null),
                DiagnosticsMasks = decoder.ReadEnumerated<DiagnosticsMasks>(null),
                TimestampsToReturn = decoder.ReadEnumerated<TimestampsToReturn>(null),
                ClientHandle = decoder.ReadUInt32(null),
                MonitoringMode = decoder.ReadEnumerated<MonitoringMode>(null)
            };

            ExtensionObject origFilterEo = decoder.ReadExtensionObject(null);
            if (!origFilterEo.IsNull &&
                origFilterEo.TryGetValue(out IEncodeable? origBody) &&
                origBody is MonitoringFilter origFilter)
            {
                item.OriginalFilter = origFilter;
            }

            ExtensionObject filterEo = decoder.ReadExtensionObject(null);
            if (!filterEo.IsNull &&
                filterEo.TryGetValue(out IEncodeable? filterBody) &&
                filterBody is MonitoringFilter filterToUse)
            {
                item.FilterToUse = filterToUse;
            }

            item.Range = decoder.ReadDouble(null);
            item.SamplingInterval = decoder.ReadDouble(null);
            item.QueueSize = decoder.ReadUInt32(null);
            item.DiscardOldest = decoder.ReadBoolean(null);
            item.SourceSamplingInterval = decoder.ReadInt32(null);
            item.AlwaysReportUpdates = decoder.ReadBoolean(null);
            item.IsDurable = decoder.ReadBoolean(null);
            item.LastValue = decoder.ReadDataValue(null)!;

            StatusCode lastErrorStatus = decoder.ReadStatusCode(null);
            item.LastError = lastErrorStatus == StatusCodes.Good
                ? null! : new ServiceResult(lastErrorStatus);

            string? rangeStr = decoder.ReadString(null);
            item.ParsedIndexRange = string.IsNullOrEmpty(rangeStr)
                ? NumericRange.Null : NumericRange.Parse(rangeStr!);
            return item;
        }

        /// <exception cref="InvalidDataException">The supplied store version is not supported.</exception>
        private static void ValidateStoreVersion(uint version)
        {
            if (version != kStoreVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported durable subscription store version {version}.");
            }
        }
    }

    /// <summary>
    /// Defines log messages for durable subscription persistence and restoration failures.
    /// </summary>
    internal static partial class SubscriptionStoreLog
    {
        /// <summary>
        /// Logs a failure to persist subscriptions or their queues.
        /// </summary>
        [LoggerMessage(
            EventId = QuickstartsServersEventIds.SubscriptionStore + 0, Level = LogLevel.Warning,
            Message = "Failed to store subscriptions")]
        public static partial void FailedToStoreSubscriptions(this ILogger logger, Exception exception);

        /// <summary>
        /// Logs a failure to restore persisted subscriptions.
        /// </summary>
        [LoggerMessage(
            EventId = QuickstartsServersEventIds.SubscriptionStore + 1, Level = LogLevel.Warning,
            Message = "Failed to restore subscriptions")]
        public static partial void FailedToRestoreSubscriptions(this ILogger logger, Exception exception);

        /// <summary>
        /// Logs a failure to remove a persisted subscription file after restoration.
        /// </summary>
        [LoggerMessage(
            EventId = QuickstartsServersEventIds.SubscriptionStore + 2, Level = LogLevel.Warning,
            Message = "Failed to cleanup files for stored subscsription")]
        public static partial void FailedToCleanupStoredSubscriptionFiles(this ILogger logger, Exception exception);
    }
}
