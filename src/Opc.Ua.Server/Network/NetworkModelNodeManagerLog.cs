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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Network
{
    /// <summary>
    /// Source-generated logging methods for the network model node manager.
    /// </summary>
    internal static partial class NetworkModelNodeManagerLog
    {
        /// <summary>
        /// Records a provider read failure without implying that unavailable values are healthy.
        /// </summary>
        [LoggerMessage(
            EventId = ServerEventIds.NetworkModelNodeManager,
            Level = LogLevel.Error,
            Message = "The network model provider read failed; no healthy defaults were published.")]
        internal static partial void ProviderReadFailed(ILogger logger, Exception exception);

        /// <summary>
        /// Records a provider-watch failure before the manager retries the watch.
        /// </summary>
        [LoggerMessage(
            EventId = ServerEventIds.NetworkModelNodeManager + 1,
            Level = LogLevel.Error,
            Message = "The network model provider watch failed; published values were marked bad " +
                "and the watch will be retried.")]
        internal static partial void ProviderWatchFailed(ILogger logger, Exception exception);

        /// <summary>
        /// Records a newer provider revision rejected because its acquisition timestamp regressed.
        /// </summary>
        [LoggerMessage(
            EventId = ServerEventIds.NetworkModelNodeManager + 2,
            Level = LogLevel.Warning,
            Message = "Rejected network model revision {Revision} acquired at {Timestamp:o}; it is older " +
                "than applied revision {AppliedRevision} acquired at {AppliedTimestamp:o}.")]
        internal static partial void StaleSnapshotRejected(
            ILogger logger,
            long revision,
            DateTime timestamp,
            long appliedRevision,
            DateTime appliedTimestamp);

        /// <summary>
        /// Records observation ending before the server canceled it.
        /// </summary>
        [LoggerMessage(
            EventId = ServerEventIds.NetworkModelNodeManager + 3,
            Level = LogLevel.Warning,
            Message = "The network model provider watch ended before cancellation; retained values " +
                "were marked bad and the watch will be retried.")]
        internal static partial void ProviderWatchCompleted(ILogger logger);
    }
}
