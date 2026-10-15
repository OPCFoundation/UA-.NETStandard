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

using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// [DEBUG-G10-RF1] Temporary event id offsets for the Opc.Ua.WotCon.Client assembly.
    /// Remove together with every [DEBUG-G10-RF1] marker before the final repair.
    /// </summary>
    internal static class WotConClientEventIds
    {
        public const int WotRegistryClientDiag = 0;
    }
}

namespace Opc.Ua.WotCon.Client
{
    /// <summary>
    /// [DEBUG-G10-RF1] Holds temporary source-generated client refresh timing log messages.
    /// </summary>
    internal static partial class WotRegistryClientDiagLog
    {
        /// <summary>
        /// Logs the wall time of the document upload phase of a bulk load.
        /// </summary>
        [LoggerMessage(EventId = WotConClientEventIds.WotRegistryClientDiag + 0, Level = LogLevel.Information,
            Message = "[DEBUG-G10-RF1] client upload documents={Documents} uploadMs={UploadMs}")]
        public static partial void ClientUploadPhase(this ILogger logger, int documents, long uploadMs);

        /// <summary>
        /// Logs the wall time and configured timeouts of one client Refresh call.
        /// </summary>
        [LoggerMessage(EventId = WotConClientEventIds.WotRegistryClientDiag + 1, Level = LogLevel.Information,
            Message = "[DEBUG-G10-RF1] client refresh callMs={CallMs} operationTimeoutMs={OperationTimeout} " +
                "defaultTimeoutHintMs={TimeoutHint} completed={Completed}")]
        public static partial void ClientRefreshCall(
            this ILogger logger,
            long callMs,
            int operationTimeout,
            int timeoutHint,
            bool completed);
    }
}
