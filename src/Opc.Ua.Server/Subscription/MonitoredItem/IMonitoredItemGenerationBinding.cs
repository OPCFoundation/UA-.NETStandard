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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Keeps subscription membership on the exact generation currently bound to an item.
    /// </summary>
    internal interface IMonitoredItemGenerationBinding
    {
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
        /// Reports the lost node once and optionally forbids future adoption.
        /// </summary>
        /// <param name="terminal">Whether the item requires client-side recreation.</param>
        void Invalidate(bool terminal);

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
