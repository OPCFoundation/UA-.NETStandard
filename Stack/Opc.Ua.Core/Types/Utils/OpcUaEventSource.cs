/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;
using static Opc.Ua.Utils;

namespace Opc.Ua
{
    /// <summary>
    /// TODO is this interface needed?
    /// </summary>
    public interface IOpcUaEventSource
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        void Critical(string message);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        void Error(string message);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        void Warning(string message);

        /// <summary>
        /// 
        /// </summary>
        void Trace(string message);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        void Debug(string message);
    }

    /// <summary>
    /// Event source for high performance logging.
    /// </summary>
    [EventSource(Name = "OPC-UA-Core")]
    public class OpcUaEventSource : EventSource, IOpcUaEventSource
    {
        /// <summary>
        /// 
        /// </summary>
        public static OpcUaEventSource Log = new OpcUaEventSource();

        private const int CriticalId = 1;
        private const int ErrorId = CriticalId + 1;
        private const int WarningId = ErrorId + 1;
        private const int TraceId = WarningId + 1;
        private const int DebugId = TraceId + 1;
        private const int ExceptionId = DebugId + 1;
        private const int SecurityId = ExceptionId + 1;
        private const int ServiceResultExceptionId = SecurityId + 1;

        private const int ServiceCallId = ServiceResultExceptionId + 1;
        private const int ServiceCompletedId = ServiceCallId + 1;
        private const int ServiceCompletedBadId = ServiceCompletedId + 1;
        private const int ServiceFaultId = ServiceCompletedBadId + 1;
        private const int ServerCallId = ServiceFaultId + 1;

        /// <summary>
        /// The messages used in event messages.
        /// </summary>
        private const string ServiceCallMessage = "{0} Called. RequestHandle={1}, PendingRequestCount={2}";
        private const string ServiceCompletedMessage = "{0} Completed. RequestHandle={1}, PendingRequestCount={2}";
        private const string ServiceCompletedBadMessage = "{0} Completed. RequestHandle={1}, PendingRequestCount={3}, StatusCode={2}";
        private const string ServiceFaultMessage = "Service Fault Occured. Reason={0}";
        private const string ServerCallMessage = "Service Call={0}";

        /// <summary>
        /// The ILogger event Ids used for event messages.
        /// </summary>
        private readonly EventId ServiceCallEventId = new EventId(TraceMasks.Client | TraceMasks.Service, nameof(ServiceCall));
        private readonly EventId ServiceCompletedEventId = new EventId(TraceMasks.Client | TraceMasks.Service, nameof(ServiceCompleted));
        private readonly EventId ServiceCompletedBadEventId = new EventId(TraceMasks.Client | TraceMasks.Service, nameof(ServiceCompletedBad));
        private readonly EventId ServiceFaultEventId = new EventId(TraceMasks.Server | TraceMasks.Service, nameof(ServiceFault));
        private readonly EventId ServerCallEventId = new EventId(TraceMasks.Server | TraceMasks.Service, nameof(ServerCall));

        /// <summary>
        /// 
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
        }

        /// <summary>
        /// 
        /// </summary>
        public static class Tasks
        {
            //public const EventTask Page = (EventTask)1;
            //public const EventTask DBQuery = (EventTask)2;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        [Event(CriticalId, Message = null, Level = EventLevel.Critical, Keywords = Keywords.Trace)]
        public void Critical(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(CriticalId, message);
            }
            else
            {
                Utils.LogCritical(message, false);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        [Event(ErrorId, Message = null, Level = EventLevel.Error, Keywords = Keywords.Trace)]
        public void Error(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(ErrorId, message);
            }
            else
            {
                Utils.LogError(message, false);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        [Event(WarningId, Message = null, Level = EventLevel.Warning, Keywords = Keywords.Trace)]
        public void Warning(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(WarningId, message);
            }
            else
            {
                Utils.LogWarning(message, false);
            }
        }

        /// <summary>
        /// Generic Trace output.
        /// </summary>
        [Event(TraceId, Message = null, Level = EventLevel.Informational, Keywords = Keywords.Trace)]
        public void Trace(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(TraceId, message);
            }
            else
            {
                Utils.LogTrace(message, false);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        [Event(DebugId, Message = null, Level = EventLevel.Informational, Keywords = Keywords.Trace)]
        public void Debug(string message)
        {
#if DEBUG
            if (IsEnabled())
            {
                WriteEvent(DebugId, message);
            }
            else
            {
                Utils.LogDebug(message, false);
            }
#endif
        }

        /// <summary>
        /// 
        /// </summary>
        [NonEvent]
        public void Critical(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Critical(string.Format(format, args));
            }
            else
            {
                Utils.LogCritical(TraceMasks.Error, format, false, args);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        [NonEvent]
        public void Error(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Error(string.Format(format, args));
            }
            else
            {
                Utils.LogError(format, args);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        [NonEvent]
        public void Warning(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Warning(string.Format(format, args));
            }
            else
            {
                Utils.LogWarning(format, args);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        [NonEvent]
        public void Trace(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Trace(string.Format(format, args));
            }
            else
            {
                Utils.LogTrace(format, args);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        [NonEvent]
        public void Trace(int mask, string format, params object[] args)
        {
            if (IsEnabled())
            {
                Trace(string.Format(format, args));
            }
            else
            {
                Utils.LogTrace(mask, format, args);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        [NonEvent]
        [Conditional("DEBUG")]
        public void Debug(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Debug(String.Format(format, args));
            }
            else
            {
                Utils.LogDebug(format, args);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        [Event(ExceptionId, Message = null, Level = EventLevel.Error, Keywords = Keywords.Exception)]
        public void Exception(string message)
        {
            WriteEvent(ExceptionId, message);
        }

        /// <summary>
        /// 
        /// </summary>
        [NonEvent]
        public void Exception(Exception ex, string format, params object[] args)
        {
            var message = Utils.TraceExceptionMessage(ex, format, args).ToString();

            if (IsEnabled())
            {
                if (ex is ServiceResultException sre)
                {
                    ServiceResultException((int)sre.StatusCode, message);
                }
                else
                {
                    Exception(message);
                }
            }
            else
            {
                // trace message.
                Utils.LogError(ex, message);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        [Event(ServiceResultExceptionId, Message = "ServiceResultException: {0} {1}", Level = EventLevel.Error, Keywords = Keywords.Trace)]
        public void ServiceResultException(int statusCode, string message)
        {
            WriteEvent(ServiceResultExceptionId, statusCode, message);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serviceName"></param>
        /// <param name="requestHandle"></param>
        /// <param name="pendingRequestCount"></param>
        [Event(ServiceCallId, Message = ServiceCallMessage, Level = EventLevel.Informational, Keywords = Keywords.Service)]
        public void ServiceCall(string serviceName, uint requestHandle, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCallId, serviceName, requestHandle, pendingRequestCount);
            }
            else
            {
                Utils.LogTrace(ServiceCallEventId, ServiceCallMessage, serviceName, requestHandle, pendingRequestCount);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serviceName"></param>
        /// <param name="requestHandle"></param>
        /// <param name="pendingRequestCount"></param>
        [Event(ServiceCompletedId, Message = ServiceCompletedMessage, Level = EventLevel.Informational, Keywords = Keywords.Service)]
        public void ServiceCompleted(string serviceName, uint requestHandle, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCompletedId, serviceName, requestHandle, pendingRequestCount);
            }
            else
            {
                Utils.LogTrace(ServiceCompletedEventId, ServiceCompletedMessage, serviceName, requestHandle, pendingRequestCount);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serviceName"></param>
        /// <param name="requestHandle"></param>
        /// <param name="statusCode"></param>
        /// <param name="pendingRequestCount"></param>
        [Event(ServiceCompletedBadId, Message = ServiceCompletedBadMessage, Level = EventLevel.Error, Keywords = Keywords.Service)]
        public void ServiceCompletedBad(string serviceName, uint requestHandle, uint statusCode, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCompletedBadId, serviceName, requestHandle, statusCode, pendingRequestCount);
            }
            else
            {
                Utils.LogTrace(ServiceCompletedBadEventId, ServiceCompletedBadMessage, serviceName, requestHandle, statusCode, pendingRequestCount);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="statusCode"></param>
        [Event(ServiceFaultId, Message = ServiceFaultMessage, Level = EventLevel.Error, Keywords = Keywords.Service)]
        public void ServiceFault(uint statusCode)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceFaultId, statusCode);
            }
            else
            {
                Utils.LogWarning(ServiceFaultEventId, ServiceFaultMessage, statusCode);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="requestType"></param>
        /// <param name="requestId"></param>
        [Event(ServerCallId, Message = ServerCallMessage, Level = EventLevel.Informational, Keywords = Keywords.Service)]
        public void ServerCall(string requestType, uint requestId)
        {
            if (IsEnabled())
            {
                WriteEvent(ServerCallId, requestType, requestId);
            }
            else
            {
                Utils.LogTrace(ServerCallEventId, ServerCallMessage, false, requestType, requestId);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        [Event(SecurityId, Message = null, Level = EventLevel.LogAlways, Keywords = Keywords.Security)]
        public void Security(string message)
        {
            WriteEvent(SecurityId, message);
        }

        /// <summary>
        /// 
        /// </summary>
        [NonEvent]
        public void Security(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Security(String.Format(format, args));
            }
            else
            {
                Utils.LogInformation(TraceMasks.Security, format, false, args);
            }
        }
    }
}
