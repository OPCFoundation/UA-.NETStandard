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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Source-generated log messages for the data channel engine.
    /// </summary>
    internal static partial class DataChannelLog
    {
        /// <summary>
        /// Records a bounded state transition without resource or grant material.
        /// </summary>
        [LoggerMessage(EventId = CoreEventIds.DataChannel + 0, Level = LogLevel.Debug,
            Message = "DataChannel {ChannelId}: entered {State} with {Result}.")]
        public static partial void FlowStateChanged(
            this ILogger logger,
            uint channelId,
            DataChannelState state,
            DataChannelResult result);

        /// <summary>
        /// Records a carrier exception without interpreting it as successful flow delivery.
        /// </summary>
        [LoggerMessage(EventId = CoreEventIds.DataChannelManager + 0, Level = LogLevel.Warning,
            Message = "The data channel carrier operation failed.")]
        public static partial void FlowCarrierFailure(this ILogger logger, Exception exception);

        /// <summary>
        /// Records a failing local observer without changing protocol outcomes.
        /// </summary>
        [LoggerMessage(EventId = CoreEventIds.DataChannelManager + 1, Level = LogLevel.Warning,
            Message = "A data channel state observer failed.")]
        public static partial void FlowObserverFailure(this ILogger logger, Exception exception);

        /// <summary>
        /// Records a provider failure independently of authorization denial.
        /// </summary>
        [LoggerMessage(EventId = CoreEventIds.DataChannelManager + 2, Level = LogLevel.Warning,
            Message = "The data channel authorization provider failed.")]
        public static partial void FlowAuthorizationFailure(this ILogger logger, Exception exception);
    }
}
