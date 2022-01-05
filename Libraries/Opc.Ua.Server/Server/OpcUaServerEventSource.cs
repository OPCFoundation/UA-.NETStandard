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

using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;
using static Opc.Ua.Utils;

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
        // client event ids
        private const int SendResponseId = 1;
        private const int ServerCallId = SendResponseId + 1;
        private const int SessionStateId = ServerCallId + 1;
        private const int MonitoredItemReadyId = SessionStateId + 1;

        /// <summary>
        /// The server messages used in event messages.
        /// </summary>
        private const string SendResponseMessage = "ChannelId {0}: SendResponse {1}";
        private const string ServerCallMessage = "Server Call={0}, Id={1}";
        private const string SessionStateMessage = "Session {0}, Id={1}, Name={2}, ChannelId={3}, User={4}";
        private const string MonitoredItemReadyMessage = "IsReadyToPublish[{0}] {1}";

        /// <summary>
        /// The Server ILogger event Ids used for event messages, when calling back to ILogger.
        /// </summary>
        private readonly EventId SendResponseEventId = new EventId(TraceMasks.ServiceDetail, nameof(SendResponse));
        private readonly EventId ServerCallEventId = new EventId(TraceMasks.ServiceDetail, nameof(ServerCall));
        private readonly EventId SessionStateMessageEventId = new EventId(TraceMasks.Information, nameof(SessionState));
        private readonly EventId MonitoredItemReadyEventId = new EventId(TraceMasks.OperationDetail, nameof(MonitoredItemReady));


        /// <summary>
        /// The send response.
        /// </summary>
        [Event(SendResponseId, Message = SendResponseMessage, Level = EventLevel.Verbose)]
        public void SendResponse(uint channelId, uint requestId)
        {
            if (IsEnabled())
            {
                WriteEvent(SendResponseId, channelId, requestId);
            }
            else if ((TraceMask & TraceMasks.ServiceDetail) != 0 &&
                Logger.IsEnabled(LogLevel.Trace))
            {
                LogTrace(SendResponseEventId, SendResponseMessage, channelId, requestId);
            }
        }

        /// <summary>
        /// A server call message.
        /// </summary>
        [Event(ServerCallId, Message = ServerCallMessage, Level = EventLevel.Informational)]
        public void ServerCall(string requestType, uint requestId)
        {
            if (IsEnabled())
            {
                WriteEvent(ServerCallId, requestType, requestId);
            }
            else if ((TraceMask & TraceMasks.ServiceDetail) != 0 &&
                Logger.IsEnabled(LogLevel.Trace))
            {
                LogTrace(ServerCallEventId, ServerCallMessage, requestType, requestId);
            }
        }

        /// <summary>
        /// The state of the session.
        /// </summary>
        [Event(SessionStateId, Message = SessionStateMessage, Level = EventLevel.Informational)]
        public void SessionState(string context, string sessionId, string sessionName, string secureChannelId, string identity)
        {
            if (IsEnabled())
            {
                WriteEvent(SessionStateId, context, sessionId, sessionName, secureChannelId, identity);
            }
            else if (Logger.IsEnabled(LogLevel.Information))
            {
                LogInfo(SessionStateMessageEventId, SessionStateMessage, context, sessionId, sessionName, secureChannelId, identity);
            }
        }

        /// <summary>
        /// The state of the server session.
        /// </summary>
        [Event(MonitoredItemReadyId, Message = MonitoredItemReadyMessage, Level = EventLevel.Verbose)]
        public void MonitoredItemReady(uint id, string state)
        {
            if ((TraceMask & TraceMasks.OperationDetail) != 0)
            {
                if (IsEnabled())
                {
                    WriteEvent(MonitoredItemReadyId, id, state);
                }
                else if (Logger.IsEnabled(LogLevel.Trace))
                {
                    LogTrace(MonitoredItemReadyEventId, MonitoredItemReadyMessage, id, state);
                }
            }
        }

        /// <summary>
        /// Log a WriteValue.
        /// </summary>
        [NonEvent]
        public void WriteValueRange(NodeId nodeId, Variant wrappedValue, string range)
        {
            if ((TraceMask & TraceMasks.ServiceDetail) != 0)
            {
                if (IsEnabled())
                {
                    //WriteEvent();
                }
                else if (Logger.IsEnabled(LogLevel.Trace))
                {
                    LogTrace(TraceMasks.ServiceDetail, "WRITE: NodeId={0} Value={1} Range={2}", nodeId, wrappedValue, range);
                }
            }
        }

        /// <summary>
        /// Log a ReadValue.
        /// </summary>
        [NonEvent]
        public void ReadValueRange(NodeId nodeId, Variant wrappedValue, string range)
        {
            if ((TraceMask & TraceMasks.ServiceDetail) != 0)
            {
                if (IsEnabled())
                {
                    //WriteEvent();
                }
                else if (Logger.IsEnabled(LogLevel.Trace))
                {
                    LogTrace(TraceMasks.ServiceDetail, "READ: NodeId={0} Value={1} Range={2}", nodeId, wrappedValue, range);
                }
            }
        }

        /// <summary>
        /// Log a Queued Value.
        /// </summary>
        [NonEvent]
        public void EnqueueValue(Variant wrappedValue)
        {
            if ((TraceMask & TraceMasks.OperationDetail) != 0)
            {
                if (IsEnabled())
                {
                    //WriteEvent();
                }
                else if (Logger.IsEnabled(LogLevel.Trace))
                {
                    LogTrace("ENQUEUE VALUE: Value={0}", wrappedValue);
                }
            }
        }

        /// <summary>
        /// Log a Dequeued Value.
        /// </summary>
        [NonEvent]
        public void DequeueValue(Variant wrappedValue, StatusCode statusCode)
        {
            if ((TraceMask & TraceMasks.OperationDetail) != 0)
            {
                if (IsEnabled())
                {
                    //WriteEvent();
                }
                else if (Logger.IsEnabled(LogLevel.Trace))
                {
                    LogTrace("DEQUEUE VALUE: Value={0} CODE={1}<{1:X8}> OVERFLOW={2}", wrappedValue, statusCode.Code, statusCode.Overflow);
                }
            }
        }

        /// <summary>
        /// Log a Queued Value.
        /// </summary>
        [NonEvent]
        public void QueueValue(uint id, Variant wrappedValue, StatusCode statusCode)
        {
            if ((TraceMask & TraceMasks.OperationDetail) != 0)
            {
                if (IsEnabled())
                {
                    //WriteEvent();
                }
                else if (Logger.IsEnabled(LogLevel.Trace))
                {
                    LogTrace(TraceMasks.OperationDetail, "QUEUE VALUE[{0}]: Value={1} CODE={2}<{2:X8}> OVERFLOW={3}",
                        id, wrappedValue, statusCode.Code, statusCode.Overflow);
                }
            }
        }
    }
}
