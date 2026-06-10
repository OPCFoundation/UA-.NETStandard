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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Save / Load support for the V2 <see cref="SubscriptionManager"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The on-wire format is the OPC UA <see cref="BinaryEncoder"/> format.
    /// Each subscription is captured as a
    /// <see cref="SubscriptionStateSnapshot"/> — an
    /// <see cref="IEncodeable"/> emitted by the
    /// <see cref="DataTypeAttribute"/> source generator — and written
    /// directly via <see cref="IEncoder.WriteEncodeable{T}(string, T)"/>.
    /// The reader instantiates <see cref="SubscriptionStateSnapshot"/>
    /// statically by type (no <see cref="ExpandedNodeId"/> lookup is
    /// required because the wire schema is implicit in the call site).
    /// Future schema evolution is handled by adding new optional fields
    /// to the snapshot record — they are encoded only when set and
    /// recognized via <see cref="IDecoder.HasField(string)"/> by the
    /// reader.
    /// </para>
    /// <para>
    /// The stream still preserves the source session's namespace and
    /// server URI tables so the snapshot is portable across sessions
    /// whose tables index URIs at different positions; the loader
    /// remaps those indices into the target session's tables before
    /// decoding each snapshot's <c>NodeId</c> fields.
    /// </para>
    /// </remarks>
    internal static class SubscriptionManagerSerializer
    {
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
            // all subscriptions managed by this instance. The public
            // surface is the LogicalSubscription wrapper, which may
            // own multiple partitions — emit one snapshot per
            // partition so a later LoadAsync can regroup via
            // SubscriptionStateSnapshot.LogicalGroupId. The older
            // concrete Subscription path is retained for tests /
            // direct usage.
            IEnumerable<ISubscription> selected =
                subscriptions ?? manager.Items;
            var snapshots = new List<SubscriptionStateSnapshot>();
            foreach (ISubscription s in selected)
            {
                if (s is LogicalSubscription logical)
                {
                    snapshots.AddRange(logical.SnapshotAllPartitions());
                }
                else if (s is Subscription concrete)
                {
                    snapshots.Add(concrete.Snapshot());
                }
            }

            using var encoder = new BinaryEncoder(stream, messageContext, true);
            encoder.WriteStringArray(null, messageContext.NamespaceUris.ToArrayOf());
            encoder.WriteStringArray(null, messageContext.ServerUris.ToArrayOf());
            encoder.WriteInt32(null, snapshots.Count);
            foreach (SubscriptionStateSnapshot snapshot in snapshots)
            {
                // Write the snapshot directly as an IEncodeable.
                // Schema identity is statically encoded by the call site
                // (we always read back a SubscriptionStateSnapshot); schema
                // evolution is handled by adding new optional fields with
                // CanOmitFields-aware encoding.
                encoder.WriteEncodeable(null, snapshot);
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

            // Read every snapshot off the wire first; we group by
            // LogicalGroupId before restoring so multi-partition
            // wrappers come back as one logical subscription.
            var raw = new List<SubscriptionStateSnapshot>(count);
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                raw.Add(decoder.ReadEncodeable<SubscriptionStateSnapshot>(null));
            }

            var restored = new List<ISubscription>();
            int handlerIndex = 0;

            // 1) Standalone snapshots (LogicalGroupId == null) keep
            //    today's behavior — one wrapper per snapshot. This
            //    covers V1 snapshot files where LogicalGroupId did
            //    not exist on the wire (decodes as null).
            // 2) Grouped snapshots are validated as a contiguous
            //    0..N-1 PartitionIndex sequence with exactly one
            //    primary and the same LogicalGroupId. Any
            //    discrepancy throws BadDecodingError so a corrupted
            //    snapshot fails loudly rather than silently
            //    fragmenting state.
            var groups = new Dictionary<string, List<SubscriptionStateSnapshot>>(
                StringComparer.Ordinal);
            var standalone = new List<SubscriptionStateSnapshot>();
            foreach (SubscriptionStateSnapshot snap in raw)
            {
                if (string.IsNullOrEmpty(snap.LogicalGroupId))
                {
                    standalone.Add(snap);
                }
                else
                {
                    if (!groups.TryGetValue(snap.LogicalGroupId,
                        out List<SubscriptionStateSnapshot>? bucket))
                    {
                        bucket = [];
                        groups[snap.LogicalGroupId] = bucket;
                    }
                    bucket.Add(snap);
                }
            }

            foreach (SubscriptionStateSnapshot snap in standalone)
            {
                ct.ThrowIfCancellationRequested();
                string syntheticName = handlerIndex.ToString(
                    CultureInfo.InvariantCulture);
                handlerIndex++;
                ISubscription subscription = await manager.RestoreAsync(
                    handlerFactory(syntheticName),
                    snap,
                    transferSubscriptions,
                    ct).ConfigureAwait(false);
                restored.Add(subscription);
            }
            foreach (KeyValuePair<string, List<SubscriptionStateSnapshot>> entry in groups)
            {
                ct.ThrowIfCancellationRequested();
                List<SubscriptionStateSnapshot> ordered =
                    ValidateAndSortGroup(entry.Key, entry.Value);
                string syntheticName = handlerIndex.ToString(
                    CultureInfo.InvariantCulture);
                handlerIndex++;
                ISubscription subscription = await manager.RestoreGroupAsync(
                    handlerFactory(syntheticName),
                    ordered,
                    transferSubscriptions,
                    ct).ConfigureAwait(false);
                restored.Add(subscription);
            }
            return restored;
        }

        /// <summary>
        /// Validate that <paramref name="bucket"/> forms a coherent
        /// multi-partition snapshot group: every snapshot must carry
        /// the same <paramref name="groupId"/>, the
        /// <c>PartitionIndex</c> values must be a contiguous
        /// <c>0..N-1</c> sequence with no duplicates and exactly one
        /// primary (<c>PartitionIndex == 0</c>). Returns the
        /// snapshots sorted by <c>PartitionIndex</c>.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadDecodingError"/> when the group
        /// is malformed.
        /// </exception>
        private static List<SubscriptionStateSnapshot> ValidateAndSortGroup(
            string groupId,
            List<SubscriptionStateSnapshot> bucket)
        {
            bucket.Sort(static (a, b) => a.PartitionIndex.CompareTo(b.PartitionIndex));

            for (int i = 0; i < bucket.Count; i++)
            {
                if (bucket[i].PartitionIndex != i)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Multi-partition snapshot group '{0}' has " +
                        "non-contiguous or duplicated PartitionIndex " +
                        "values (expected {1} at position {1}, got {2}).",
                        groupId, i, bucket[i].PartitionIndex);
                }
            }
            return bucket;
        }
    }
}
