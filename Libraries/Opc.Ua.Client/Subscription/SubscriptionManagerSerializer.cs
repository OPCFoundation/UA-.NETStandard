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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2MonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItem;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Save / Load support for the V2 <see cref="SubscriptionManager"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The on-wire format is the OPC UA <see cref="BinaryEncoder"/> format
    /// (same encoder as the classic <c>Session.Save</c>). The stream
    /// starts with a header that captures the message context's namespace
    /// and server URI tables so the snapshot is portable across sessions
    /// whose tables index URIs in different positions.
    /// </para>
    /// <para>
    /// Each subscription is captured as a snapshot of its <em>current
    /// options</em> (read via the internal <see cref="Subscription.Options"/>
    /// surface) plus the server-side subscription id, available
    /// sequence numbers (so an immediate take-over via TransferSubscriptions
    /// can republish gaps), and the list of monitored items. Each item
    /// snapshot captures the value of <see cref="MonitoredItemOptions"/>
    /// at the time of save (<em>not</em> the live
    /// <see cref="IOptionsMonitor{T}"/> wrapper) — rehydration wraps the
    /// loaded options in a fresh <see cref="OptionsMonitor{T}"/> per item.
    /// </para>
    /// </remarks>
    internal static class SubscriptionManagerSerializer
    {
        /// <summary>
        /// Format identifier. Increment when the layout changes; older
        /// snapshots are rejected with a clear error.
        /// </summary>
        private const ushort kFormatVersion = 1;

        /// <summary>
        /// Magic bytes prefix so a wrong stream type fails fast instead
        /// of producing garbage decoded values.
        /// </summary>
        private static readonly byte[] s_magic = "UA2S"u8.ToArray();

        public static void Save(SubscriptionManager manager, Stream stream,
            IServiceMessageContext messageContext,
            IEnumerable<ISubscription>? subscriptions)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (messageContext == null)
            {
                throw new ArgumentNullException(nameof(messageContext));
            }

            // Resolve which subscriptions to write. Default = all subscriptions
            // managed by this instance (so callers don't enumerate themselves).
            List<Subscription> selected;
            if (subscriptions == null)
            {
                selected = manager.Items.OfType<Subscription>().ToList();
            }
            else
            {
                selected = subscriptions.OfType<Subscription>().ToList();
            }

            using var encoder = new BinaryEncoder(stream, messageContext, true);
            encoder.WriteByteString(null, s_magic);
            encoder.WriteUInt16(null, kFormatVersion);
            encoder.WriteStringArray(null, messageContext.NamespaceUris.ToArrayOf());
            encoder.WriteStringArray(null, messageContext.ServerUris.ToArrayOf());
            encoder.WriteInt32(null, selected.Count);

            int index = 0;
            foreach (Subscription subscription in selected)
            {
                WriteSubscription(encoder, subscription, index++);
            }
        }

        public static async ValueTask<IReadOnlyList<ISubscription>> LoadAsync(
            SubscriptionManager manager, Stream stream,
            IServiceMessageContext messageContext,
            Func<string, ISubscriptionNotificationHandler> handlerFactory,
            bool transferSubscriptions, CancellationToken ct)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (messageContext == null)
            {
                throw new ArgumentNullException(nameof(messageContext));
            }
            if (handlerFactory == null)
            {
                throw new ArgumentNullException(nameof(handlerFactory));
            }
            if (transferSubscriptions)
            {
                // The V2 add-then-set-id path is incompatible with the
                // V2 state machine's debug assertion that the queued
                // CreateMonitoredItem request's ClientHandle matches
                // the item's ClientHandle (see
                // MonitoredItem.SetCreateResult). A safe transfer path
                // requires a new "load with state" entry point on the
                // manager that creates the V2 instance without queuing
                // CreateMonitoredItem requests and then drives an
                // explicit TransferSubscriptions call. Tracked in
                // plans/26-v2-subscription-parity.md.
                throw new NotImplementedException(
                    "V2 ISubscriptionManager.LoadAsync(transferSubscriptions: true) " +
                    "is not yet implemented. Use transferSubscriptions: false to " +
                    "re-create subscriptions on the new session with fresh " +
                    "server-side ids.");
            }

            using var decoder = new BinaryDecoder(stream, messageContext, true);
            ByteString magic = decoder.ReadByteString(null);
            if (magic.IsNull || !magic.Memory.Span.SequenceEqual(s_magic))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "Stream does not start with the V2 subscription manager " +
                    "save magic prefix.");
            }
            ushort version = decoder.ReadUInt16(null);
            if (version != kFormatVersion)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    string.Format(CultureInfo.InvariantCulture,
                        "Unsupported V2 subscription manager save format version: " +
                        "got {0}, expected {1}.", version, kFormatVersion));
            }

            ArrayOf<string?> nsUris = decoder.ReadStringArray(null);
            ArrayOf<string?> serverUris = decoder.ReadStringArray(null);
            decoder.SetMappingTables(
                nsUris.IsNull
                    ? new NamespaceTable()
                    : new NamespaceTable(nsUris.Memory.ToArray()!),
                serverUris.IsNull
                    ? new StringTable()
                    : new StringTable(serverUris.Memory.ToArray()!));

            int count = decoder.ReadInt32(null);
            if (count <= 0)
            {
                return [];
            }

            var restored = new List<ISubscription>(count);
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                ISubscription subscription = ReadSubscription(decoder, manager,
                    handlerFactory, transferSubscriptions);
                restored.Add(subscription);
            }
            await Task.CompletedTask.ConfigureAwait(false);
            return restored;
        }

        private static void WriteSubscription(BinaryEncoder encoder,
            Subscription subscription, int index)
        {
            string syntheticName = index.ToString(CultureInfo.InvariantCulture);
            encoder.WriteString(null, syntheticName);
            encoder.WriteUInt32(null, subscription.Id);

            IReadOnlyList<uint>? available = subscription.AvailableInRetransmissionQueue;
            encoder.WriteUInt32Array(null, (available?.ToArray() ?? []).ToArrayOf());

            SubscriptionOptions opts = subscription.Options;
            WriteSubscriptionOptions(encoder, opts);

            List<IMonitoredItem> items = [.. subscription.MonitoredItems.Items];
            encoder.WriteInt32(null, items.Count);
            foreach (IMonitoredItem item in items)
            {
                WriteMonitoredItem(encoder, item);
            }
        }

        private static void WriteSubscriptionOptions(BinaryEncoder encoder,
            SubscriptionOptions options)
        {
            encoder.WriteBoolean(null, options.Disabled);
            encoder.WriteUInt32(null, options.KeepAliveCount);
            encoder.WriteUInt32(null, options.LifetimeCount);
            encoder.WriteByte(null, options.Priority);
            encoder.WriteInt64(null, options.PublishingInterval.Ticks);
            encoder.WriteBoolean(null, options.PublishingEnabled);
            encoder.WriteUInt32(null, options.MaxNotificationsPerPublish);
            encoder.WriteInt64(null, options.MinLifetimeInterval.Ticks);
        }

        private static void WriteMonitoredItem(BinaryEncoder encoder, IMonitoredItem item)
        {
            encoder.WriteString(null, item.Name);
            encoder.WriteUInt32(null, item.ClientHandle);
            encoder.WriteUInt32(null, item.ServerId);
            encoder.WriteUInt32(null, item.TriggeringItemClientHandle);

            IReadOnlyCollection<uint> triggered = item.TriggeredItemClientHandles;
            encoder.WriteUInt32Array(null, triggered.ToArray().ToArrayOf());

            // Snapshot the live options *value* — not the IOptionsMonitor
            // wrapper. The current options aren't on IMonitoredItem; cast
            // to the internal type which exposes them.
            V2MonitoredItemOptions opts;
            if (item is V2MonitoredItem internalItem)
            {
                opts = internalItem.Options.CurrentValue;
            }
            else
            {
                throw new InvalidOperationException(
                    "Cannot snapshot non-internal IMonitoredItem implementation.");
            }
            WriteMonitoredItemOptions(encoder, opts);
        }

        private static void WriteMonitoredItemOptions(BinaryEncoder encoder,
            V2MonitoredItemOptions options)
        {
            encoder.WriteUInt32(null, options.Order);
            encoder.WriteNodeId(null, options.StartNodeId.IsNull ? NodeId.Null : options.StartNodeId);
            encoder.WriteInt32(null, (int)options.TimestampsToReturn);
            encoder.WriteUInt32(null, options.AttributeId);
            encoder.WriteString(null, options.IndexRange);
            QualifiedName encoding = options.Encoding.HasValue && !options.Encoding.Value.IsNull
                ? options.Encoding.Value
                : QualifiedName.Null;
            encoder.WriteQualifiedName(null, encoding);
            encoder.WriteInt32(null, (int)options.MonitoringMode);
            encoder.WriteInt64(null, options.SamplingInterval.Ticks);
            // MonitoringFilter is an IEncodeable abstract; wrap in
            // ExtensionObject (handles null automatically).
            ExtensionObject filterEo = options.Filter == null
                ? ExtensionObject.Null
                : new ExtensionObject(options.Filter);
            encoder.WriteExtensionObject(null, filterEo);
            encoder.WriteUInt32(null, options.QueueSize);
            encoder.WriteBoolean(null, options.DiscardOldest);
            encoder.WriteBoolean(null, options.AutoSetQueueSize);
        }

        private static ISubscription ReadSubscription(BinaryDecoder decoder,
            SubscriptionManager manager,
            Func<string, ISubscriptionNotificationHandler> handlerFactory,
            bool transferSubscriptions)
        {
            string? name = decoder.ReadString(null);
            // Server-side subscription id and available sequence numbers
            // are preserved in the format for forward-compat with a
            // future transferSubscriptions:true implementation. They are
            // currently read-and-discard.
            _ = decoder.ReadUInt32(null);
            _ = decoder.ReadUInt32Array(null);
            SubscriptionOptions options = ReadSubscriptionOptions(decoder);

            ISubscriptionNotificationHandler handler = handlerFactory(
                name ?? string.Empty);

            ISubscription added = manager.Add(handler,
                new OptionsMonitor<SubscriptionOptions>(options));

            int itemCount = decoder.ReadInt32(null);
            for (int i = 0; i < itemCount; i++)
            {
                ReadMonitoredItem(decoder, added, transferSubscriptions);
            }
            return added;
        }

        private static SubscriptionOptions ReadSubscriptionOptions(BinaryDecoder decoder)
        {
            bool disabled = decoder.ReadBoolean(null);
            uint keepAlive = decoder.ReadUInt32(null);
            uint lifetime = decoder.ReadUInt32(null);
            byte priority = decoder.ReadByte(null);
            long publishTicks = decoder.ReadInt64(null);
            bool publishing = decoder.ReadBoolean(null);
            uint maxNotif = decoder.ReadUInt32(null);
            long minLifetimeTicks = decoder.ReadInt64(null);
            return new SubscriptionOptions
            {
                Disabled = disabled,
                KeepAliveCount = keepAlive,
                LifetimeCount = lifetime,
                Priority = priority,
                PublishingInterval = TimeSpan.FromTicks(publishTicks),
                PublishingEnabled = publishing,
                MaxNotificationsPerPublish = maxNotif,
                MinLifetimeInterval = TimeSpan.FromTicks(minLifetimeTicks)
            };
        }

        private static void ReadMonitoredItem(BinaryDecoder decoder,
            ISubscription subscription, bool transferSubscriptions)
        {
            string? name = decoder.ReadString(null);
            // Per-item client + server ids and triggering links are
            // preserved in the format for forward-compat with a future
            // transferSubscriptions:true implementation. They are
            // currently read-and-discard so the V2 state machine can
            // re-create the item from scratch with fresh handles.
            _ = decoder.ReadUInt32(null);
            _ = decoder.ReadUInt32(null);
            _ = decoder.ReadUInt32(null);
            _ = decoder.ReadUInt32Array(null);
            V2MonitoredItemOptions options = ReadMonitoredItemOptions(decoder);
            _ = transferSubscriptions;

            subscription.MonitoredItems.TryAdd(name ?? string.Empty,
                new OptionsMonitor<V2MonitoredItemOptions>(options), out _);
        }

        private static V2MonitoredItemOptions ReadMonitoredItemOptions(BinaryDecoder decoder)
        {
            uint order = decoder.ReadUInt32(null);
            NodeId startNodeId = decoder.ReadNodeId(null);
            var ttr = (TimestampsToReturn)decoder.ReadInt32(null);
            uint attributeId = decoder.ReadUInt32(null);
            string? indexRange = decoder.ReadString(null);
            QualifiedName encoding = decoder.ReadQualifiedName(null);
            var mode = (MonitoringMode)decoder.ReadInt32(null);
            long samplingTicks = decoder.ReadInt64(null);
            ExtensionObject filterEo = decoder.ReadExtensionObject(null);
            uint queueSize = decoder.ReadUInt32(null);
            bool discardOldest = decoder.ReadBoolean(null);
            bool autoSetQueueSize = decoder.ReadBoolean(null);

            MonitoringFilter? filter = null;
            if (!filterEo.IsNull && filterEo.TryGetValue(out MonitoringFilter? mf))
            {
                filter = mf;
            }

            return new V2MonitoredItemOptions
            {
                Order = order,
                StartNodeId = startNodeId,
                TimestampsToReturn = ttr,
                AttributeId = attributeId,
                IndexRange = indexRange,
                Encoding = encoding.IsNull ? null : encoding,
                MonitoringMode = mode,
                SamplingInterval = TimeSpan.FromTicks(samplingTicks),
                Filter = filter,
                QueueSize = queueSize,
                DiscardOldest = discardOldest,
                AutoSetQueueSize = autoSetQueueSize
            };
        }
    }
}
