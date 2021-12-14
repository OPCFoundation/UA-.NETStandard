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

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;
using static Opc.Ua.Utils;

namespace Opc.Ua
{
    /// <summary>
    /// A generic interface class for event source logging 
    /// </summary>
    public interface IOpcUaEventSource
    {
        /// <summary>
        /// Write a critical message to the event log.
        /// </summary>
        void Critical(string message);

        /// <summary>
        /// Write a error message to the event log.
        /// </summary>
        void Error(string message);

        /// <summary>
        /// Write a warning message to the event log.
        /// </summary>
        void Warning(string message);

        /// <summary>
        /// Write a informational message to the event log.
        /// </summary>
        void Info(string message);

        /// <summary>
        /// Write a trace message to the event log.
        /// </summary>
        void Trace(string message);

        /// <summary>
        /// Write a debug message to the event log.
        /// </summary>
        void Debug(string message);
    }

    /// <summary>
    /// Event source for high performance logging.
    /// </summary>
    [EventSource(Name = "OPC-UA-Core")]
    internal class OpcUaCoreEventSource : EventSource, IOpcUaEventSource
    {
        private const int TraceId = 1;
        private const int DebugId = TraceId + 1;
        private const int InfoId = DebugId + 1;
        private const int WarningId = InfoId + 1;
        private const int ErrorId = WarningId + 1;
        private const int CriticalId = ErrorId + 1;
        private const int ExceptionId = CriticalId + 1;
        private const int SecurityId = ExceptionId + 1;
        private const int ServiceResultExceptionId = SecurityId + 1;

        /// <summary>
        /// The core event ids.
        /// </summary>
        private const int ServiceCallId = ServiceResultExceptionId + 1;
        private const int ServiceCompletedId = ServiceCallId + 1;
        private const int ServiceCompletedBadId = ServiceCompletedId + 1;
        private const int SubscriptionStateId = ServiceCompletedBadId + 1;
        private const int SendResponseId = SubscriptionStateId + 1;
        private const int ServiceFaultId = SendResponseId + 1;

        /// <summary>
        /// The core messages.
        /// </summary>
        private const string ServiceCallMessage = "{0} Called. RequestHandle={1}, PendingRequestCount={2}";
        private const string ServiceCompletedMessage = "{0} Completed. RequestHandle={1}, PendingRequestCount={2}";
        private const string ServiceCompletedBadMessage = "{0} Completed. RequestHandle={1}, PendingRequestCount={3}, StatusCode={2}";
        private const string SendResponseMessage = "ChannelId {0}: SendResponse {1}";
        private const string ServiceFaultMessage = "Service Fault Occured. Reason={0}";

        /// <summary>
        /// The Core ILogger event Ids used for event messages, when calling back to ILogger.
        /// </summary>
        private readonly EventId ServiceCallEventId = new EventId(TraceMasks.Service, nameof(ServiceCall));
        private readonly EventId ServiceCompletedEventId = new EventId(TraceMasks.Service, nameof(ServiceCompleted));
        private readonly EventId ServiceCompletedBadEventId = new EventId(TraceMasks.Service, nameof(ServiceCompletedBad));
        private readonly EventId SendResponseEventId = new EventId(TraceMasks.ServiceDetail, nameof(SendResponse));
        private readonly EventId ServiceFaultEventId = new EventId(TraceMasks.Service, nameof(ServiceFault));

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

        /// <inheritdoc/>
        [Event(CriticalId, Message = null, Level = EventLevel.Critical, Keywords = Keywords.Trace)]
        public void Critical(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(CriticalId, message);
            }
        }

        /// <inheritdoc/>
        [Event(ErrorId, Message = null, Level = EventLevel.Error, Keywords = Keywords.Trace)]
        public void Error(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(ErrorId, message);
            }
        }

        /// <inheritdoc/>
        [Event(WarningId, Message = null, Level = EventLevel.Warning, Keywords = Keywords.Trace)]
        public void Warning(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(WarningId, message);
            }
        }

        /// <inheritdoc/>
        [Event(TraceId, Message = null, Level = EventLevel.Verbose, Keywords = Keywords.Trace)]
        public void Trace(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(TraceId, message);
            }
        }

        /// <inheritdoc/>
        [Event(InfoId, Message = null, Level = EventLevel.Informational, Keywords = Keywords.Trace)]
        public void Info(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(InfoId, message);
            }
        }

        /// <inheritdoc/>
        [Event(DebugId, Message = null, Level = EventLevel.Verbose, Keywords = Keywords.Trace)]
        public void Debug(string message)
        {
#if DEBUG
            if (IsEnabled())
            {
                WriteEvent(DebugId, message);
            }
#endif
        }

        /// <summary>
        /// Log a critical message.
        /// </summary>
        [NonEvent]
        public void Critical(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Critical(Utils.Format(format, args));
            }
            else
            {
                Utils.LogCritical(TraceMasks.Error, format, false, args);
            }
        }

        /// <summary>
        /// Log an error message.
        /// </summary>
        [NonEvent]
        public void Error(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Error(Utils.Format(format, args));
            }
            else
            {
                Utils.LogError(format, args);
            }
        }

        /// <summary>
        /// Log information message.
        /// </summary>
        [NonEvent]
        public void Info(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Info(Utils.Format(format, args));
            }
            else
            {
                Utils.LogInfo(format, args);
            }
        }

        /// <summary>
        /// Log a warning message.
        /// </summary>
        [NonEvent]
        public void Warning(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Warning(Utils.Format(format, args));
            }
            else
            {
                Utils.LogWarning(format, args);
            }
        }

        /// <summary>
        /// Log a Trace message.
        /// </summary>
        [NonEvent]
        public void Trace(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Trace(Utils.Format(format, args));
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(format, args);
            }
        }

        /// <summary>
        /// Log a trace message.
        /// </summary>
        [NonEvent]
        public void Trace(int mask, string format, params object[] args)
        {
            if (IsEnabled())
            {
                Trace(Utils.Format(format, args));
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(mask, format, args);
            }
        }

        /// <summary>
        /// Log a debug messug.
        /// </summary>
        [NonEvent]
        [Conditional("DEBUG")]
        public void Debug(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Debug(Utils.Format(format, args));
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Debug))
            {
                Utils.LogDebug(format, args);
            }
        }

        /// <summary>
        /// Log an exception with just a message.
        /// </summary>
        /// <param name="message">The exception message.</param>
        [Event(ExceptionId, Message = "Exception: {0}", Level = EventLevel.Error, Keywords = Keywords.Exception)]
        public void Exception(string message)
        {
            WriteEvent(ExceptionId, message);
        }

        /// <summary>
        /// Log an exception.
        /// </summary>
        [NonEvent]
        public void Exception(Exception ex, string format, params object[] args)
        {
            if (IsEnabled())
            {
                var message = Utils.TraceExceptionMessage(ex, format, args).ToString();
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
                Utils.LogError(ex, format, args);
            }
        }

        /// <summary>
        /// A service result exception message.
        /// </summary>
        [Event(ServiceResultExceptionId, Message = "ServiceResultException: {0} {1}", Level = EventLevel.Error, Keywords = Keywords.Trace)]
        public void ServiceResultException(int statusCode, string message)
        {
            WriteEvent(ServiceResultExceptionId, statusCode, message);
        }

        /// <summary>
        /// Log a ILogger log message with exception on EventSource.
        /// </summary>
        [NonEvent]
        public void LogLog(LogLevel logLevel, EventId eventId, Exception exception, string message, params object[] args)
        {
            if (exception != null)
            {
                Exception(exception, message, args);
                return;
            }
            LogLog(logLevel, eventId, message, args);
        }

        /// <summary>
        /// Log a ILogger log message with EventSource.
        /// </summary>
        [NonEvent]
        public void LogLog(LogLevel logLevel, EventId eventId, string message, params object[] args)
        {
            if (args.Length == 0)
            {
                switch (logLevel)
                {
                    case LogLevel.Trace: Trace(message); break;
                    case LogLevel.Debug: Debug(message); break;
                    case LogLevel.Information: Info(message); break;
                    case LogLevel.Warning: Warning(message); break;
                    case LogLevel.Error: Error(message); break;
                    case LogLevel.Critical: Critical(message); break;
                }
            }
            switch (logLevel)
            {
                case LogLevel.Trace: Trace(message, args); break;
                case LogLevel.Debug: Debug(message, args); break;
                case LogLevel.Information: Info(message, args); break;
                case LogLevel.Warning: Warning(message, args); break;
                case LogLevel.Error: Error(message, args); break;
                case LogLevel.Critical: Critical(message, args); break;
            }
        }

        //************************************************************************************************************

        /// <summary>
        /// A server service call message.
        /// </summary>
        [Event(ServiceCallId, Message = ServiceCallMessage, Level = EventLevel.Informational, Keywords = Keywords.Service)]
        public void ServiceCall(string serviceName, uint requestHandle, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCallId, serviceName, requestHandle, pendingRequestCount);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(ServiceCallEventId, ServiceCallMessage, serviceName, requestHandle, pendingRequestCount);
            }
        }

        /// <summary>
        /// The server service completed message.
        /// </summary>
        [Event(ServiceCompletedId, Message = ServiceCompletedMessage, Level = EventLevel.Informational, Keywords = Keywords.Service)]
        public void ServiceCompleted(string serviceName, uint requestHandle, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCompletedId, serviceName, requestHandle, pendingRequestCount);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(ServiceCompletedEventId, ServiceCompletedMessage, serviceName, requestHandle, pendingRequestCount);
            }
        }

        /// <summary>
        /// A service message completed with a bad status code.
        /// </summary>
        [Event(ServiceCompletedBadId, Message = ServiceCompletedBadMessage, Level = EventLevel.Error, Keywords = Keywords.Service)]
        public void ServiceCompletedBad(string serviceName, uint requestHandle, uint statusCode, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCompletedBadId, serviceName, requestHandle, statusCode, pendingRequestCount);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(ServiceCompletedBadEventId, ServiceCompletedBadMessage, serviceName, requestHandle, statusCode, pendingRequestCount);
            }
        }

        /// <summary>
        /// A service fault message.
        /// </summary>
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
        /// The send response.
        /// </summary>
        [Event(SendResponseId, Message = SendResponseMessage, Level = EventLevel.Verbose, Keywords = Keywords.Service)]
        public void SendResponse(uint channelId, uint requestId)
        {
            if (IsEnabled())
            {
                WriteEvent(SendResponseId, channelId, requestId);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(SendResponseEventId, SendResponseMessage, channelId, requestId);
            }
        }

    }
}
