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

#pragma warning disable RCS1229 // Use async/await when necessary - this path is synchronous; ValueTask wraps work for future async I/O
        public static ValueTask SaveAsync(
            SubscriptionManager manager,
            Stream stream,
            IServiceMessageContext messageContext,
            IEnumerable<ISubscription>? subscriptions,
            CancellationToken ct = default)
#pragma warning restore RCS1229
        {
            ct.ThrowIfCancellationRequested();
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

            // Capture a snapshot per selected subscription. Default =
            // all subscriptions managed by this instance. Snapshot is
            // taken from the concrete subscription type (not on the
            // ISubscription interface).
            IEnumerable<ISubscription> selected =
                subscriptions ?? manager.Items;
            var snapshots = new List<SubscriptionStateSnapshot>();
            foreach (ISubscription s in selected)
            {
                if (s is Subscription concrete)
                {
                    snapshots.Add(concrete.Snapshot());
                }
            }

            using var encoder = new BinaryEncoder(stream, messageContext, true);
            encoder.WriteByteString(null, s_magic);
            encoder.WriteUInt16(null, kFormatVersion);
            encoder.WriteStringArray(null, messageContext.NamespaceUris.ToArrayOf());
            encoder.WriteStringArray(null, messageContext.ServerUris.ToArrayOf());
            encoder.WriteInt32(null, snapshots.Count);

            int index = 0;
            foreach (SubscriptionStateSnapshot snapshot in snapshots)
            {
                WriteSnapshot(encoder, snapshot, index++);
            }
            return default;
        }

        public static async ValueTask<IReadOnlyList<ISubscription>> LoadAsync(
            SubscriptionManager manager,
            Stream stream,
            IServiceMessageContext messageContext,
            Func<string, ISubscriptionNotificationHandler> handlerFactory,
            bool transferSubscriptions,
            CancellationToken ct)
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
                (string syntheticName, SubscriptionStateSnapshot state) =
                    ReadSnapshot(decoder);
                ISubscriptionNotificationHandler handler =
                    handlerFactory(syntheticName);
                ISubscription subscription = await manager.RestoreAsync(
                    handler, state, transferSubscriptions, ct)
                    .ConfigureAwait(false);
                restored.Add(subscription);
            }
            return restored;
        }

        private static void WriteSnapshot(BinaryEncoder encoder,
            SubscriptionStateSnapshot snapshot, int index)
        {
            string syntheticName = index.ToString(CultureInfo.InvariantCulture);
            encoder.WriteString(null, syntheticName);
            encoder.WriteUInt32(null, snapshot.ServerId);
            ArrayOf<uint> available = snapshot.AvailableSequenceNumbers.IsNull
                ? Array.Empty<uint>().ToArrayOf()
                : snapshot.AvailableSequenceNumbers;
            encoder.WriteUInt32Array(null, available);
            WriteSubscriptionOptions(encoder, snapshot.Options);

            int itemCount = snapshot.MonitoredItems.IsNull
                ? 0
                : snapshot.MonitoredItems.Count;
            encoder.WriteInt32(null, itemCount);
            if (itemCount > 0)
            {
                foreach (MonitoredItemStateSnapshot item in snapshot.MonitoredItems)
                {
                    WriteMonitoredItemSnapshot(encoder, item);
                }
            }
        }

        private static (string Name, SubscriptionStateSnapshot Snapshot) ReadSnapshot(
            BinaryDecoder decoder)
        {
            string? name = decoder.ReadString(null);
            uint serverId = decoder.ReadUInt32(null);
            ArrayOf<uint> available = decoder.ReadUInt32Array(null);
            SubscriptionOptions options = ReadSubscriptionOptions(decoder);

            int itemCount = decoder.ReadInt32(null);
            var items = new MonitoredItemStateSnapshot[itemCount];
            for (int i = 0; i < itemCount; i++)
            {
                items[i] = ReadMonitoredItemSnapshot(decoder);
            }

            return (name ?? string.Empty, new SubscriptionStateSnapshot
            {
                Options = options,
                ServerId = serverId,
                AvailableSequenceNumbers = available,
                MonitoredItems = items.ToArrayOf()
            });
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

        private static void WriteMonitoredItemSnapshot(BinaryEncoder encoder,
            MonitoredItemStateSnapshot item)
        {
            encoder.WriteString(null, item.Name);
            encoder.WriteUInt32(null, item.ClientHandle);
            encoder.WriteUInt32(null, item.ServerId);
            encoder.WriteUInt32(null, item.TriggeringItemClientHandle);
            ArrayOf<uint> triggered = item.TriggeredItemClientHandles.IsNull
                ? Array.Empty<uint>().ToArrayOf()
                : item.TriggeredItemClientHandles;
            encoder.WriteUInt32Array(null, triggered);
            WriteMonitoredItemOptions(encoder, item.Options);
        }

        private static MonitoredItemStateSnapshot ReadMonitoredItemSnapshot(
            BinaryDecoder decoder)
        {
            string? name = decoder.ReadString(null);
            uint clientHandle = decoder.ReadUInt32(null);
            uint serverId = decoder.ReadUInt32(null);
            uint triggeringHandle = decoder.ReadUInt32(null);
            ArrayOf<uint> triggered = decoder.ReadUInt32Array(null);
            MonitoredItems.MonitoredItemOptions options = ReadMonitoredItemOptions(decoder);
            return new MonitoredItemStateSnapshot
            {
                Name = name ?? string.Empty,
                Options = options,
                ClientHandle = clientHandle,
                ServerId = serverId,
                TriggeringItemClientHandle = triggeringHandle,
                TriggeredItemClientHandles = triggered
            };
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

        private static void WriteMonitoredItemOptions(BinaryEncoder encoder,
            MonitoredItems.MonitoredItemOptions options)
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
            ExtensionObject filterEo = options.Filter == null
                ? ExtensionObject.Null
                : new ExtensionObject(options.Filter);
            encoder.WriteExtensionObject(null, filterEo);
            encoder.WriteUInt32(null, options.QueueSize);
            encoder.WriteBoolean(null, options.DiscardOldest);
            encoder.WriteBoolean(null, options.AutoSetQueueSize);
        }

        private static MonitoredItems.MonitoredItemOptions ReadMonitoredItemOptions(BinaryDecoder decoder)
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

            return new MonitoredItems.MonitoredItemOptions
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
