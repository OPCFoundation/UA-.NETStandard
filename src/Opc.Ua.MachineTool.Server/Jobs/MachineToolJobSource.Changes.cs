/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolJobSource
    {
        /// <inheritdoc/>
        public IAsyncEnumerable<Isa95JobStatusNotificationV2> SubscribeAsync(
            CancellationToken cancellationToken = default) =>
            SubscribeCoreAsync(m_engine.SubscribeAsync, static notification => notification with
            {
                JobOrder = CoreUtils.Clone(notification.JobOrder)!,
                JobResponse = CoreUtils.Clone(notification.JobResponse)!,
                State = CoreUtils.Clone(notification.State)
            }, ChangeKind.Status, cancellationToken);

        /// <inheritdoc/>
        public IAsyncEnumerable<Isa95JobOrderCatalogChange> SubscribeCatalogChangesAsync(
            CancellationToken cancellationToken = default) =>
            SubscribeCoreAsync(m_engine.SubscribeCatalogChangesAsync,
                static change => change with { Order = CoreUtils.Clone(change.Order) },
                ChangeKind.Catalog, cancellationToken);

        /// <inheritdoc/>
        public IAsyncEnumerable<Isa95JobResponseChange> SubscribeResponseChangesAsync(
            CancellationToken cancellationToken = default) =>
            SubscribeCoreAsync(m_engine.SubscribeResponseChangesAsync, static change => change,
                ChangeKind.Response, cancellationToken);

        /// <summary>
        /// Uses independent existing engine subscriptions and only adds bounded admission accounting.
        /// The gate barrier keeps an engine event private until its matching legacy revision has committed.
        /// </summary>
        /// <typeparam name="T">The existing engine notification type.</typeparam>
        private async IAsyncEnumerable<T> SubscribeCoreAsync<T>(
            Func<CancellationToken, IAsyncEnumerable<T>> subscribe,
            Func<T, T> copy,
            ChangeKind kind,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            IAsyncEnumerator<T> iterator;
            ValueTask<bool> pending;
            var subscriber = new ChangeSubscriber(kind);
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                lock (m_admission)
                {
                    if (m_subscribers.Count >= m_options.MaximumSubscribers)
                    {
                        throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                            "The configured independent subscriber limit has been reached.");
                    }
                    iterator = subscribe(cancellationToken).GetAsyncEnumerator(cancellationToken);
                    pending = iterator.MoveNextAsync();
                    m_subscribers.Add(subscriber);
                }
            }
            finally
            {
                Exit();
            }
            try
            {
                while (await pending.ConfigureAwait(false))
                {
                    T item;
                    if (!await TryEnterDeliveryAsync().ConfigureAwait(false))
                    {
                        yield break;
                    }
                    try
                    {
                        lock (m_admission)
                        {
                            subscriber.Pending--;
                        }
                        item = copy(iterator.Current);
                    }
                    finally
                    {
                        Exit();
                    }
                    yield return item;
                    pending = iterator.MoveNextAsync();
                }
            }
            finally
            {
                await iterator.DisposeAsync().ConfigureAwait(false);
                lock (m_admission)
                {
                    m_subscribers.Remove(subscriber);
                }
            }
        }

        /// <summary>
        /// Lets a pending engine iterator finish normally when source disposal prevents another delivery lease.
        /// </summary>
        private async ValueTask<bool> TryEnterDeliveryAsync()
        {
            try
            {
                await EnterAsync(CancellationToken.None).ConfigureAwait(false);
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Reserves room before invoking the engine, including both final and removed cancellation observations.
        /// </summary>
        private bool HasCapacity(ChangeKind changes, int revisions)
        {
            if (m_pending.Count > m_options.MaximumPendingRevisions - revisions ||
                m_capture.Snapshot.Revision > ulong.MaxValue - (ulong)revisions)
            {
                return false;
            }
            lock (m_admission)
            {
                foreach (ChangeSubscriber subscriber in m_subscribers)
                {
                    if ((changes & subscriber.Kind) != 0 && subscriber.Pending >= m_options.MaximumPendingChanges)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Applies the same admission bounds to explicit non-protocol observations.
        /// </summary>
        private void RequireCapacity(ChangeKind changes)
        {
            if (!HasCapacity(changes, 1))
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable,
                    "A revision or subscriber backlog is full; publish pending observations before retrying.");
            }
        }

        /// <summary>
        /// Records exactly the notifications the existing engine emits for an accepted operation.
        /// </summary>
        private void AccountChanges(ChangeKind changes)
        {
            lock (m_admission)
            {
                foreach (ChangeSubscriber subscriber in m_subscribers)
                {
                    if ((changes & subscriber.Kind) != 0)
                    {
                        subscriber.Pending++;
                    }
                }
            }
        }

        /// <summary>Admission counters only; the existing engine retains and broadcasts the actual events.</summary>
        private readonly List<ChangeSubscriber> m_subscribers = [];

        /// <summary>
        /// Existing engine streams affected by one accepted mutation.
        /// </summary>
        [Flags]
        private enum ChangeKind
        {
            /// <summary>No engine notification.</summary>
            None = 0,

            /// <summary>A status event.</summary>
            Status = 1,

            /// <summary>An update or removal.</summary>
            Catalog = 2,

            /// <summary>An explicit response.</summary>
            Response = 4
        }

        /// <summary>
        /// One independent subscriber's bounded admission count.
        /// </summary>
        private sealed class ChangeSubscriber(ChangeKind kind)
        {
            /// <summary>
            /// Gets the existing stream to which this subscriber belongs.
            /// </summary>
            public ChangeKind Kind { get; } = kind;

            /// <summary>
            /// Gets or sets the number of committed notifications not handed to this subscriber.
            /// </summary>
            public int Pending { get; set; }
        }
    }
}
