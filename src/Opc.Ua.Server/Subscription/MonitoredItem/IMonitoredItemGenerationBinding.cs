/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server
{
    /// <summary>
    /// Keeps subscription membership on the exact generation currently bound to an item.
    /// </summary>
    internal interface IMonitoredItemGenerationBinding
    {
        /// <summary>
        /// Gets whether the item has no usable source or requires client-side recreation.
        /// </summary>
        bool IsSourceUnavailable { get; }

        /// <summary>
        /// Gets whether the source node has been reported lost.
        /// </summary>
        bool IsDeleted { get; }

        /// <summary>
        /// Gets whether the client must create a new item instead of rebinding this one.
        /// </summary>
        bool IsTerminal { get; }

        /// <summary>
        /// Gets whether the client has disposed the item and released its queues.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Gets whether subscription removal or disposal permanently forbids further adoption.
        /// </summary>
        bool IsRemoved { get; }

        /// <summary>
        /// Gets the revision of the item's native source binding.
        /// </summary>
        long SourceRevision { get; }

        /// <summary>
        /// Captures the current native source identity under the item's existing lock.
        /// </summary>
        /// <returns>The source generation, handle and incarnation observed together.</returns>
        MonitoredItemSourceSnapshot CaptureSource();

        /// <summary>
        /// Commits a native source only if no other binding was installed during preparation.
        /// </summary>
        /// <param name="sourceRevision">The revision captured before source preparation.</param>
        /// <param name="nodeManager">The new native owner.</param>
        /// <param name="managerHandle">The prepared source handle.</param>
        /// <param name="boundRevision">The revision of the committed binding.</param>
        /// <returns>Whether the prepared source was still current and committed.</returns>
        bool TryBindSource(
            long sourceRevision,
            IAsyncNodeManager nodeManager,
            object managerHandle,
            out long boundRevision);

        /// <summary>
        /// Clears a failed registration without clearing a newer source binding.
        /// </summary>
        /// <param name="sourceRevision">The revision owned by the failed registration.</param>
        /// <returns>Whether the source revision was still current.</returns>
        bool TryClearSource(long sourceRevision);

        /// <summary>
        /// Reports a failed handoff only after native release and while its source incarnation is still current.
        /// </summary>
        /// <param name="sourceRevision">The revision captured before the handoff attempted adoption.</param>
        /// <param name="terminal">Whether the item requires client-side recreation.</param>
        /// <returns>Whether the failed handoff still owned the item's source state.</returns>
        bool TryInvalidateSource(long sourceRevision, bool terminal);

        /// <summary>
        /// Clears a failed native release from the exact retired generation and invalidates the item terminally.
        /// </summary>
        /// <param name="sourceRevision">The source revision whose release failed.</param>
        /// <param name="generation">The exact drained generation being retired.</param>
        /// <returns>Whether this still owned source binding was invalidated.</returns>
        bool TryInvalidateRetiredSource(long sourceRevision, NodeManagerGenerationReference generation);

        /// <summary>
        /// Reports the lost node once and optionally forbids future adoption.
        /// </summary>
        /// <param name="terminal">Whether the item requires client-side recreation.</param>
        void Invalidate(bool terminal);

        /// <summary>
        /// Reschedules an already-lost node's status after a client monitoring-mode or transfer request.
        /// </summary>
        void EnsureLostNodeNotification();

        /// <summary>
        /// Claims the item after its subscription has added it.
        /// </summary>
        /// <param name="subscription">The subscription claiming the item.</param>
        void BindSubscription(ISubscription subscription);

        /// <summary>
        /// Releases the item when its owning subscription removes it.
        /// </summary>
        /// <param name="subscription">The subscription releasing the item.</param>
        void UnbindSubscription(ISubscription subscription);
    }

    /// <summary>
    /// Identifies one native source without retaining mutable half-released item state.
    /// </summary>
    /// <param name="Generation">The source generation, when the item is bound.</param>
    /// <param name="Handle">The native source handle, when supported by the base managers.</param>
    /// <param name="Revision">The item's binding incarnation.</param>
    internal readonly record struct MonitoredItemSourceSnapshot(
        NodeManagerGenerationReference? Generation,
        NodeHandle? Handle,
        long Revision);
}
