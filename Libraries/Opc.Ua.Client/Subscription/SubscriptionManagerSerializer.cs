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
            // all subscriptions managed by this instance. Snapshot is
            // taken from the concrete subscription type.
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

            var restored = new List<ISubscription>(count);
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                // Schema identity is implicit in the call: we always
                // read a SubscriptionStateSnapshot. Future schema evolution
                // happens by adding optional fields recognized via
                // HasField on the decoder side.
                SubscriptionStateSnapshot state =
                    decoder.ReadEncodeable<SubscriptionStateSnapshot>(null);
                string syntheticName = i.ToString(CultureInfo.InvariantCulture);
                ISubscriptionNotificationHandler handler =
                    handlerFactory(syntheticName);
                ISubscription subscription = await manager.RestoreAsync(
                    handler,
                    state,
                    transferSubscriptions,
                    ct).ConfigureAwait(false);
                restored.Add(subscription);
            }
            return restored;
        }
    }
}
