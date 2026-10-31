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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Stores a <see cref="SessionHandoff"/> as a protected record. A handoff carries the
    /// secrets of a server session, so it is only ever written through an
    /// <see cref="IRecordProtector"/>.
    /// </summary>
    public static class SessionHandoffCodec
    {
        /// <summary>
        /// Encodes and protects a handoff.
        /// </summary>
        /// <param name="handoff">The handoff to store.</param>
        /// <param name="protector">The record protector.</param>
        /// <param name="context">The protection context of the record.</param>
        /// <param name="messageContext">The message context whose namespace tables the
        /// subscriptions of the handoff refer to.</param>
        /// <returns>The protected record.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handoff"/>,
        /// <paramref name="protector"/> or <paramref name="messageContext"/> is <c>null</c>.</exception>
        public static ByteString Protect(
            SessionHandoff handoff,
            IRecordProtector protector,
            ByteString context,
            IServiceMessageContext messageContext)
        {
            if (handoff == null)
            {
                throw new ArgumentNullException(nameof(handoff));
            }
            if (protector == null)
            {
                throw new ArgumentNullException(nameof(protector));
            }
            if (messageContext == null)
            {
                throw new ArgumentNullException(nameof(messageContext));
            }
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, messageContext, true))
            {
                encoder.WriteInt32(null, kMagic);
                encoder.WriteInt32(null, kVersion);
                using (var configuration = new MemoryStream())
                {
                    SnapshotCodec.SaveConfiguration(configuration, messageContext, handoff.Session);
                    encoder.WriteByteString(null, new ByteString(configuration.ToArray()));
                }
                encoder.WriteDouble(null, handoff.RevisedSessionTimeout);
                encoder.WriteStringArray(null, messageContext.NamespaceUris.ToArrayOf());
                encoder.WriteStringArray(null, messageContext.ServerUris.ToArrayOf());
                encoder.WriteInt32(null, handoff.Subscriptions.Count);
                for (int i = 0; i < handoff.Subscriptions.Count; i++)
                {
                    encoder.WriteEncodeable(null, handoff.Subscriptions[i]);
                }
                for (int i = 0; i < handoff.Subscriptions.Count; i++)
                {
                    SubscriptionStateSnapshot subscription = handoff.Subscriptions[i];
                    encoder.WriteBoolean(null, subscription.HasRevisedSettings);
                    if (subscription.HasRevisedSettings)
                    {
                        encoder.WriteDouble(null, subscription.RevisedPublishingInterval.TotalMilliseconds);
                        encoder.WriteUInt32(null, subscription.RevisedKeepAliveCount);
                        encoder.WriteUInt32(null, subscription.RevisedLifetimeCount);
                        encoder.WriteByte(null, subscription.RevisedPriority);
                        encoder.WriteUInt32(null, subscription.RevisedMaxNotificationsPerPublish);
                        encoder.WriteBoolean(null, subscription.RevisedPublishingEnabled);
                    }
                    encoder.WriteInt32(null, subscription.MonitoredItems.Count);
                    for (int j = 0; j < subscription.MonitoredItems.Count; j++)
                    {
                        MonitoredItemStateSnapshot item = subscription.MonitoredItems[j];
                        encoder.WriteBoolean(null, item.HasRevisedSettings);
                        if (item.HasRevisedSettings)
                        {
                            encoder.WriteDouble(null, item.RevisedSamplingInterval.TotalMilliseconds);
                            encoder.WriteUInt32(null, item.RevisedQueueSize);
                            encoder.WriteExtensionObject(
                                null,
                                item.RevisedFilterResult == null
                                    ? ExtensionObject.Null
                                    : new ExtensionObject(item.RevisedFilterResult));
                        }
                    }
                }
            }
            return protector.Protect(context, new ByteString(stream.ToArray()));
        }

        /// <summary>
        /// Unprotects and decodes a handoff stored by <see cref="Protect"/>.
        /// </summary>
        /// <param name="record">The protected record.</param>
        /// <param name="protector">The record protector.</param>
        /// <param name="context">The protection context of the record.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="handoff">The decoded handoff.</param>
        /// <returns>False when the record cannot be unprotected or is empty.</returns>
        /// <exception cref="ServiceResultException">The record is malformed.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="protector"/> or
        /// <paramref name="telemetry"/> is <c>null</c>.</exception>
        public static bool TryUnprotect(
            ByteString record,
            IRecordProtector protector,
            ByteString context,
            ITelemetryContext telemetry,
            [NotNullWhen(true)] out SessionHandoff? handoff)
        {
            if (protector == null)
            {
                throw new ArgumentNullException(nameof(protector));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            handoff = null;
            if (!protector.TryUnprotect(context, record, out ByteString plaintext) || plaintext.IsEmpty)
            {
                return false;
            }
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.Factory.Builder.AddOpcUaClientDataTypes();
            using var stream = new MemoryStream(plaintext.ToArray(), writable: false);
            using var decoder = new BinaryDecoder(stream, messageContext, true);
            int magic = decoder.ReadInt32(null);
            int version = decoder.ReadInt32(null);
            if (magic != kMagic || version < kVersion1 || version > kVersion)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "The record is not a supported session handoff.");
            }
            ByteString configurationBytes = decoder.ReadByteString(null);
            SessionConfiguration configuration;
            using (var configurationStream = new MemoryStream(configurationBytes.ToArray(), writable: false))
            {
                configuration = SessionConfiguration.Create(configurationStream, telemetry)
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "The session handoff has no session.");
            }
            double timeout = decoder.ReadDouble(null);
            ArrayOf<string?> namespaces = decoder.ReadStringArray(null);
            ArrayOf<string?> servers = decoder.ReadStringArray(null);
            // The subscriptions were encoded with these tables, so they are decoded with the
            // same tables: a mapping onto the fresh context would map every namespace it does
            // not know to an invalid index.
            messageContext.NamespaceUris =
                namespaces.IsNull ? new NamespaceTable() : new NamespaceTable(namespaces.Memory.ToArray()!);
            messageContext.ServerUris =
                servers.IsNull ? new StringTable() : new StringTable(servers.Memory.ToArray()!);
            int count = decoder.ReadInt32(null);
            if (count < 0 || (messageContext.MaxArrayLength > 0 && count > messageContext.MaxArrayLength))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "The subscription count of the session handoff is invalid.");
            }
            var subscriptions = new List<SubscriptionStateSnapshot>();
            for (int i = 0; i < count; i++)
            {
                subscriptions.Add(decoder.ReadEncodeable<SubscriptionStateSnapshot>(null));
            }
            if (version >= kVersion)
            {
                for (int i = 0; i < subscriptions.Count; i++)
                {
                    SubscriptionStateSnapshot subscription = subscriptions[i];
                    bool hasRevisedSettings = decoder.ReadBoolean(null);
                    TimeSpan revisedPublishingInterval = TimeSpan.Zero;
                    uint revisedKeepAliveCount = 0;
                    uint revisedLifetimeCount = 0;
                    byte revisedPriority = 0;
                    uint revisedMaxNotificationsPerPublish = 0;
                    bool revisedPublishingEnabled = false;
                    if (hasRevisedSettings)
                    {
                        revisedPublishingInterval = TimeSpan.FromMilliseconds(decoder.ReadDouble(null));
                        revisedKeepAliveCount = decoder.ReadUInt32(null);
                        revisedLifetimeCount = decoder.ReadUInt32(null);
                        revisedPriority = decoder.ReadByte(null);
                        revisedMaxNotificationsPerPublish = decoder.ReadUInt32(null);
                        revisedPublishingEnabled = decoder.ReadBoolean(null);
                    }
                    int itemCount = decoder.ReadInt32(null);
                    if (itemCount != subscription.MonitoredItems.Count)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "The monitored item count in the handoff runtime state does not match the snapshot.");
                    }
                    var items = new MonitoredItemStateSnapshot[itemCount];
                    for (int j = 0; j < itemCount; j++)
                    {
                        MonitoredItemStateSnapshot item = subscription.MonitoredItems[j];
                        bool hasItemSettings = decoder.ReadBoolean(null);
                        TimeSpan revisedSamplingInterval = TimeSpan.Zero;
                        uint revisedQueueSize = 0;
                        MonitoringFilterResult? revisedFilterResult = null;
                        if (hasItemSettings)
                        {
                            revisedSamplingInterval = TimeSpan.FromMilliseconds(decoder.ReadDouble(null));
                            revisedQueueSize = decoder.ReadUInt32(null);
                            ExtensionObject filterResult = decoder.ReadExtensionObject(null);
                            if (!filterResult.IsNull &&
                                filterResult.TryGetValue(out IEncodeable? filterBody) &&
                                filterBody is MonitoringFilterResult result)
                            {
                                revisedFilterResult = result;
                            }
                            else if (!filterResult.IsNull)
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadDecodingError,
                                    "The monitored item filter result in the handoff is invalid.");
                            }
                        }
                        items[j] = item with
                        {
                            HasRevisedSettings = hasItemSettings,
                            RevisedSamplingInterval = revisedSamplingInterval,
                            RevisedQueueSize = revisedQueueSize,
                            RevisedFilterResult = revisedFilterResult
                        };
                    }
                    subscriptions[i] = subscription with
                    {
                        HasRevisedSettings = hasRevisedSettings,
                        RevisedPublishingInterval = revisedPublishingInterval,
                        RevisedKeepAliveCount = revisedKeepAliveCount,
                        RevisedLifetimeCount = revisedLifetimeCount,
                        RevisedPriority = revisedPriority,
                        RevisedMaxNotificationsPerPublish = revisedMaxNotificationsPerPublish,
                        RevisedPublishingEnabled = revisedPublishingEnabled,
                        MonitoredItems = items
                    };
                }
            }
            handoff = new SessionHandoff
            {
                Session = configuration,
                RevisedSessionTimeout = timeout,
                Subscriptions = subscriptions.ToArray()
            };
            return true;
        }

        /// <summary>
        /// Identifies a session handoff record.
        /// </summary>
        private const int kMagic = 0x48535541;

        /// <summary>
        /// The version of the record format.
        /// </summary>
        private const int kVersion1 = 1;

        /// <summary>
        /// The current record format version.
        /// </summary>
        private const int kVersion = 2;
    }
}
