/* Copyright (c) 1996-2021 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
    [EventSource(Name = "OPC-UA-Server")]
    internal class OpcUaServerEventSource : EventSource
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
        /// The keywords used for this event source.
        /// </summary>
        public static class Keywords
        {
            /// <summary>
            /// Trace
            /// </summary>
            public const EventKeywords Trace = (EventKeywords)1;
            /// <summary>
            /// Diagnostic
            /// </summary>
            public const EventKeywords Diagnostic = (EventKeywords)2;
            /// <summary>
            /// Error
            /// </summary>
            public const EventKeywords Exception = (EventKeywords)4;
            /// <summary>
            /// Service
            /// </summary>
            public const EventKeywords Service = (EventKeywords)8;
            /// <summary>
            /// Service
            /// </summary>
            public const EventKeywords Security = (EventKeywords)16;
            /// <summary>
            /// Service
            /// </summary>
            public const EventKeywords Session = (EventKeywords)32;
            /// <summary>
            /// Service
            /// </summary>
            public const EventKeywords Subscription = (EventKeywords)64;
        }

        /// <summary>
        /// The send response.
        /// </summary>
        [Event(SendResponseId, Message = SendResponseMessage, Level = EventLevel.Verbose, Keywords = Keywords.Service)]
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
        [Event(ServerCallId, Message = ServerCallMessage, Level = EventLevel.Informational, Keywords = Keywords.Service)]
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
        [Event(SessionStateId, Message = SessionStateMessage, Level = EventLevel.Informational, Keywords = Keywords.Session)]
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
        [Event(MonitoredItemReadyId, Message = MonitoredItemReadyMessage, Level = EventLevel.Verbose, Keywords = Keywords.Session)]
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
        public void WriteValueRange(Variant wrappedValue, string range)
        {
            if ((TraceMask & TraceMasks.OperationDetail) != 0)
            {
                if (IsEnabled())
                {
                    //WriteEvent();
                }
                else if (Logger.IsEnabled(LogLevel.Trace))
                {
                    LogTrace("WRITE: Value={0} Range={1}", wrappedValue, range);
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
