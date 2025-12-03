/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics.Tracing;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The local EventSource for the server library.
    /// </summary>
    public static partial class ServerUtils
    {
        /// <summary>
        /// The EventSource log interface.
        /// </summary>
        internal static OpcUaServerEventSource EventLog { get; } = new OpcUaServerEventSource();
    }

    /// <summary>
    /// Event source for high performance logging.
    /// </summary>
    [EventSource(Name = "OPC-UA-Server", Guid = "86FF2AAB-8FF6-46CB-8CE3-E0211950B30C")]
    internal sealed class OpcUaServerEventSource : EventSource
    {
        /// <summary>
        /// client event ids
        /// </summary>
        private const int kSendResponseId = 1;
        private const int kServerCallId = kSendResponseId + 1;
        private const int kSessionStateId = kServerCallId + 1;
        private const int kMonitoredItemReadyId = kSessionStateId + 1;

        /// <summary>
        /// A server call message, called from ServerCallNative. Do not call directly..
        /// </summary>
        [Event(
            kServerCallId,
            Message = "Server Call={0}, Id={1}",
            Level = EventLevel.Informational)]
        public void ServerCall(RequestType requestType, uint requestId)
        {
            if (IsEnabled())
            {
                string requestTypeString = Enum.GetName(
#if !NET8_0_OR_GREATER
                    typeof(RequestType),
#endif
                    requestType);
                WriteEvent(kServerCallId, requestTypeString, requestId);
            }
        }

        /// <summary>
        /// The state of the session.
        /// </summary>
        [Event(
            kSessionStateId,
            Message = "Session {0}, Id={1}, Name={2}, ChannelId={3}, User={4}",
            Level = EventLevel.Informational)]
        public void SessionState(
            string context,
            string sessionId,
            string sessionName,
            string secureChannelId,
            string identity)
        {
            if (IsEnabled())
            {
                WriteEvent(
                    kSessionStateId,
                    context,
                    sessionId,
                    sessionName,
                    secureChannelId,
                    identity);
            }
        }

        /// <summary>
        /// The state of the server session.
        /// </summary>
        [Event(
            kMonitoredItemReadyId,
            Message = "IsReadyToPublish[{0}] {1}",
            Level = EventLevel.Verbose)]
        public void MonitoredItemReady(uint id, string state)
        {
            if (IsEnabled())
            {
                WriteEvent(kMonitoredItemReadyId, id, state);
            }
        }
    }
}
