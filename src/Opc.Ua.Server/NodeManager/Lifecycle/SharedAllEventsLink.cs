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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Shares one physical all-events subscription of a resource between several exact owners, such as
    /// the facades that borrow one raw NodeManager.
    /// </summary>
    /// <remarks>
    /// The physical subscription is keyed only by the monitored item, so the first owner's raw release
    /// would otherwise remove it for every owner. Each owner instead holds an exact claim per item. The
    /// physical subscribe runs only for the first claim and the physical release only when the last claim
    /// is released, so a remaining owner never observes a delivery gap. A release by an owner without a
    /// claim succeeds without a physical call; that makes a retried release idempotent when the owner's
    /// facade reported failure after its link release had already succeeded. When the last physical
    /// release fails or throws, the releasing owner keeps a pending release that only that owner, or a
    /// new successful claim, can settle. Changes are serialized per link and the physical callback runs
    /// under that serialization; the callback must not re-enter the link. Owners must route their raw
    /// all-events changes through this link: a facade that calls the raw manager directly bypasses the
    /// claims and cannot be protected.
    /// </remarks>
    /// <typeparam name="TOwner">The exact owner identity, compared by reference.</typeparam>
    internal sealed class SharedAllEventsLink<TOwner> : IDisposable
        where TOwner : class
    {
        /// <summary>
        /// Creates a link over one physical all-events subscription callback.
        /// </summary>
        /// <param name="physicalChangeAsync">
        /// The raw callback that subscribes or releases the physical all-events subscription of an item.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="physicalChangeAsync"/> is null.</exception>
        internal SharedAllEventsLink(
            Func<IEventMonitoredItem, bool, CancellationToken, ValueTask<ServiceResult>> physicalChangeAsync)
        {
            m_physicalChangeAsync = physicalChangeAsync ??
                throw new ArgumentNullException(nameof(physicalChangeAsync));
        }

        /// <summary>
        /// Subscribes or releases one owner's claim on an item's physical all-events subscription.
        /// </summary>
        /// <param name="owner">The exact owner whose claim changes.</param>
        /// <param name="monitoredItem">The exact item instance.</param>
        /// <param name="unsubscribe">Whether the owner releases its claim.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// The physical result when a physical change was required; otherwise <see cref="ServiceResult.Good"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ObjectDisposedException">The link was disposed.</exception>
        internal async ValueTask<ServiceResult> ChangeAsync(
            TOwner owner,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe,
            CancellationToken cancellationToken = default)
        {
            if (owner is null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (monitoredItem is null)
            {
                throw new ArgumentNullException(nameof(monitoredItem));
            }

            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return unsubscribe
                    ? await ReleaseAsync(owner, monitoredItem, cancellationToken).ConfigureAwait(false)
                    : await ClaimAsync(owner, monitoredItem, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Returns whether an owner holds a claim on an item.
        /// </summary>
        /// <param name="owner">The exact owner.</param>
        /// <param name="monitoredItem">The exact item instance.</param>
        /// <returns><see langword="true"/> when the owner holds a claim.</returns>
        internal bool HasClaim(TOwner owner, IEventMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                return m_items.TryGetValue(monitoredItem, out ItemLink? link) &&
                    IndexOf(link.Claims, owner) >= 0;
            }
        }

        /// <summary>
        /// Returns whether an item's physical subscription is held by a claim or awaits a pending release.
        /// </summary>
        /// <param name="monitoredItem">The exact item instance.</param>
        /// <returns><see langword="true"/> when the physical subscription may still exist.</returns>
        internal bool IsPhysicallyHeld(IEventMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                return m_items.ContainsKey(monitoredItem);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        private async ValueTask<ServiceResult> ClaimAsync(
            TOwner owner,
            IEventMonitoredItem monitoredItem,
            CancellationToken cancellationToken)
        {
            ItemLink? link;
            lock (m_lock)
            {
                m_items.TryGetValue(monitoredItem, out link);
            }

            bool claimed = link is not null && IndexOf(link.Claims, owner) >= 0;
            if (link is not null && link.Claims.Count > 0 && !claimed)
            {
                AddClaim(link, owner);
                return ServiceResult.Good;
            }

            ServiceResult result;
            try
            {
                result = await m_physicalChangeAsync(monitoredItem, false, cancellationToken)
                    .ConfigureAwait(false) ?? ServiceResult.Good;
            }
            catch
            {
                if (link is null)
                {
                    lock (m_lock)
                    {
                        m_items[monitoredItem] = new ItemLink { PendingReleaseOwner = owner };
                    }
                }
                throw;
            }

            if (ServiceResult.IsBad(result))
            {
                return result;
            }

            if (link is null)
            {
                link = new ItemLink();
                lock (m_lock)
                {
                    m_items[monitoredItem] = link;
                }
            }
            link.PendingReleaseOwner = null;
            if (!claimed)
            {
                AddClaim(link, owner);
            }
            return result;
        }

        private async ValueTask<ServiceResult> ReleaseAsync(
            TOwner owner,
            IEventMonitoredItem monitoredItem,
            CancellationToken cancellationToken)
        {
            ItemLink? link;
            lock (m_lock)
            {
                m_items.TryGetValue(monitoredItem, out link);
            }
            if (link is null)
            {
                return ServiceResult.Good;
            }

            int index = IndexOf(link.Claims, owner);
            if (index < 0 && !ReferenceEquals(link.PendingReleaseOwner, owner))
            {
                return ServiceResult.Good;
            }
            if (index >= 0)
            {
                lock (m_lock)
                {
                    link.Claims.RemoveAt(index);
                }
                if (link.Claims.Count > 0)
                {
                    return ServiceResult.Good;
                }
            }

            link.PendingReleaseOwner = owner;
            ServiceResult result = await m_physicalChangeAsync(monitoredItem, true, cancellationToken)
                .ConfigureAwait(false) ?? ServiceResult.Good;
            if (ServiceResult.IsBad(result))
            {
                return result;
            }

            lock (m_lock)
            {
                m_items.Remove(monitoredItem);
            }
            return result;
        }

        private void AddClaim(ItemLink link, TOwner owner)
        {
            lock (m_lock)
            {
                link.Claims.Add(owner);
            }
        }

        private static int IndexOf(List<TOwner> claims, TOwner owner)
        {
            for (int ii = 0; ii < claims.Count; ii++)
            {
                if (ReferenceEquals(claims[ii], owner))
                {
                    return ii;
                }
            }
            return -1;
        }

        /// <summary>
        /// The claims and pending release of one item's physical subscription.
        /// </summary>
        private sealed class ItemLink
        {
            /// <summary>
            /// The exact owners that hold the physical subscription.
            /// </summary>
            public List<TOwner> Claims { get; } = [];

            /// <summary>
            /// The owner whose last physical release failed, threw, or whose first subscribe threw.
            /// </summary>
            public TOwner? PendingReleaseOwner { get; set; }
        }

        private readonly Func<IEventMonitoredItem, bool, CancellationToken, ValueTask<ServiceResult>>
            m_physicalChangeAsync;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Lock m_lock = new();
        private readonly Dictionary<IEventMonitoredItem, ItemLink> m_items = [];
    }
}
