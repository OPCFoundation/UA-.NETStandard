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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Transport capability for discovery announcements that use a
    /// transport-specific well-known destination instead of the data
    /// message destination.
    /// </summary>
    public interface IPubSubDiscoveryAnnouncementTransport
    {
        /// <summary>
        /// Periodic discovery announcement rate in milliseconds.
        /// A value of zero disables cyclic announcements.
        /// </summary>
        uint DiscoveryAnnounceRate { get; }

        /// <summary>
        /// Sends one already encoded discovery announcement to the
        /// transport-defined discovery destination.
        /// </summary>
        /// <param name="payload">Encoded NetworkMessage payload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SendDiscoveryAnnouncementAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default);
    }
}
