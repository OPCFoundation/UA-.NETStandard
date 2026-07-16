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

namespace Opc.Ua
{
    /// <summary>
    /// A transport channel managed by an <see cref="IClientChannelManager"/>.
    /// The channel is reference-counted; disposing a managed channel
    /// releases the caller's lease on the underlying channel, which the
    /// manager keeps open as long as at least one participant lease
    /// remains active.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Service calls via <see cref="ITransportChannel.SendRequestAsync"/>
    /// are gated by the manager: requests block until
    /// <see cref="State"/> is <see cref="ChannelState.Ready"/>. The
    /// manager bypasses the gate for internal reactivation traffic that
    /// runs on behalf of a participant's
    /// <see cref="IReconnectParticipant.OnReconnectAsync"/> call.
    /// </para>
    /// <para>
    /// The <see cref="StateChanged"/> event is best-effort and may be
    /// raised on the manager's worker fiber. Handlers must not block,
    /// take locks held elsewhere, or call back into the manager;
    /// instead post work to a separate worker or set flags consumed
    /// elsewhere.
    /// </para>
    /// </remarks>
    public interface IManagedTransportChannel : ITransportChannel
    {
        /// <summary>
        /// Composite identity of the underlying channel. All
        /// participants on the same key share one transport channel.
        /// </summary>
        ManagedChannelKey Key { get; }

        /// <summary>
        /// Current lifecycle state of the channel.
        /// </summary>
        ChannelState State { get; }

        /// <summary>
        /// Owner manager instance. Use this to trigger an explicit
        /// reconnect or to fetch diagnostics; do not retain references
        /// across <see cref="IDisposable.Dispose"/>.
        /// </summary>
        IClientChannelManager Manager { get; }

        /// <summary>
        /// Best-effort notification when the channel state changes.
        /// </summary>
        /// <remarks>
        /// Handlers run on the manager's reconnect fiber. They must
        /// not block, acquire long-held locks, or re-enter the manager.
        /// </remarks>
        event Action<IManagedTransportChannel, ChannelStateChange>? StateChanged;
    }
}
