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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Source-generated log messages for the data channel engine.
    /// </summary>
    internal static partial class DataChannelLog
    {
        [LoggerMessage(EventId = CoreEventIds.DataChannelManager + 0, Level = LogLevel.Warning,
            Message = "DataChannel: A frame named ChannelId {ChannelId}, which is not open on this SecureChannel.")]
        public static partial void DataChannelManagerUnknownChannel(
            this ILogger logger,
            uint channelId);

        [LoggerMessage(EventId = CoreEventIds.DataChannelManager + 1, Level = LogLevel.Error,
            Message = "DataChannel: A connection level credit grant would overflow the window.")]
        public static partial void DataChannelManagerCreditOverflow(this ILogger logger);

        [LoggerMessage(EventId = CoreEventIds.DataChannelManager + 2, Level = LogLevel.Error,
            Message = "DataChannel: The scheduler round faulted.")]
        public static partial void DataChannelManagerSchedulerFault(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(EventId = CoreEventIds.DataChannel + 0, Level = LogLevel.Debug,
            Message = "DataChannel {ChannelId}: entered {State} with {Status}.")]
        public static partial void DataChannelStateChanged(
            this ILogger logger,
            uint channelId,
            DataChannelState state,
            StatusCode status);

        [LoggerMessage(EventId = CoreEventIds.DataChannel + 1, Level = LogLevel.Warning,
            Message = "DataChannel {ChannelId}: rejected a frame, resetting with {Status}.")]
        public static partial void DataChannelFrameRejected(
            this ILogger logger,
            uint channelId,
            StatusCode status);

        [LoggerMessage(EventId = CoreEventIds.DataChannel + 2, Level = LogLevel.Warning,
            Message = "DataChannel: a STR frame arrived but the data channel feature is not enabled.")]
        public static partial void DataChannelFeatureDisabled(this ILogger logger);
    }
}
