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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional support for a bounded final-value observation through the normal subscription Publish service.
    /// </summary>
    /// <remarks>
    /// Implementations must identify the actual final notification, not merely a queue being drained.
    /// They must clean up registrations on every exit and release obligations when monitoring or the owning
    /// session ends. This capability is deliberately not required of existing custom monitored items.
    /// </remarks>
    public interface IMonitoredItemValuePublication
    {
        /// <summary>
        /// Queues a final sample and waits for its inclusion in an actual Publish response.
        /// </summary>
        /// <param name="session">The session whose permissions and identity were used to read the sample.</param>
        /// <param name="value">The final, per-subscriber sample, including its range and data encoding.</param>
        /// <param name="timeout">A positive, finite publication deadline.</param>
        /// <param name="cancellationToken">Cancels the observation and releases its registration.</param>
        /// <remarks>
        /// Final sampling bypasses data-change suppression, but not permissions, monitoring mode, publishing
        /// mode, queue limits, or Publish message limits. Concurrent samples must not overwrite the final sample.
        /// Aggregation or an unsupported publication pipeline must fail explicitly. A competing finalization
        /// must not supersede a pending one. Completion is not a transport-send or acknowledgement guarantee.
        /// </remarks>
        ValueTask WaitForValuePublicationAsync(
            ISession session,
            in DataValue value,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);
    }
}
