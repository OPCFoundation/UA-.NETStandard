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

using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.XRegistry.Protocol
{
    /// <summary>
    /// Optional best-effort invalidation hints. A hint always requires a complete
    /// authoritative scan; it is never an inventory, operation outcome or replay log.
    /// Disposal of the enumerator releases its subscription. Connection/model loss
    /// ends the stream explicitly so a host can re-inspect and subscribe again.
    /// </summary>
    public interface IXRegistryChangeFeed
    {
        /// <summary>
        /// Watches one authenticated scope without forwarding caller credentials.
        /// Hosts retain periodic polling even when this stream is active.
        /// </summary>
        IAsyncEnumerable<XRegistryChangeHint> WatchAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A coalescible invalidation. Path is informational and may be absent when
    /// a queue overflow, reconnect or unclassified notification requires repair.
    /// </summary>
    public sealed record XRegistryChangeHint(string? Path, string Reason)
    {
        /// <summary>
        /// Hints cannot authorize incremental absence or deletion.
        /// </summary>
        public bool RequiresFullInventory => true;
    }
}
