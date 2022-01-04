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
    [EventSource(Name = "OPC-UA-Core", Guid = "AC8BB021-ADD5-4D14-BB94-1E55D98AA080")]
    internal sealed class OpcUaCoreEventSource : EventSource, IOpcUaEventSource
    {
        private const int TraceId = 1;
        private const int DebugId = TraceId + 1;
        private const int InfoId = DebugId + 1;
        private const int WarningId = InfoId + 1;
        private const int ErrorId = WarningId + 1;
        private const int CriticalId = ErrorId + 1;
        private const int ExceptionId = CriticalId + 1;
        private const int ServiceResultExceptionId = ExceptionId + 1;

        /// <summary>
        /// The core event ids.
        /// </summary>
        private const int ServiceCallStartId = ServiceResultExceptionId + 1;
        private const int ServiceCallStopId = ServiceCallStartId + 1;
        private const int ServiceCallBadStopId = ServiceCallStopId + 1;
        private const int SubscriptionStateId = ServiceCallBadStopId + 1;
        private const int SendResponseId = SubscriptionStateId + 1;
        private const int ServiceFaultId = SendResponseId + 1;

        /// <summary>
        /// The core messages.
        /// </summary>
        private const string ExceptionMessage = "Exception: {0}";
        private const string ServiceResultExceptionMessage = "ServiceResultException: {0} {1}";
        private const string ServiceCallStartMessage = "{0} Called. RequestHandle={1}, PendingRequestCount={2}";
        private const string ServiceCallStopMessage = "{0} Completed. RequestHandle={1}, PendingRequestCount={2}";
        private const string ServiceCallBadStopMessage = "{0} Completed. RequestHandle={1}, PendingRequestCount={3}, StatusCode={2}";
        private const string SendResponseMessage = "ChannelId {0}: SendResponse {1}";
        private const string ServiceFaultMessage = "Service Fault Occured. Reason={0}";

        /// <summary>
        /// The Core ILogger event Ids used for event messages, when calling back to ILogger.
        /// </summary>
        private readonly EventId ServiceCallStartEventId = new EventId(TraceMasks.Service, nameof(ServiceCallStart));
        private readonly EventId ServiceCallStopEventId = new EventId(TraceMasks.Service, nameof(ServiceCallStop));
        private readonly EventId ServiceCallBadStopEventId = new EventId(TraceMasks.Service, nameof(ServiceCallBadStop));
        private readonly EventId SendResponseEventId = new EventId(TraceMasks.Service, nameof(SendResponse));
        private readonly EventId ServiceFaultEventId = new EventId(TraceMasks.Service, nameof(ServiceFault));

        /// <summary>
        /// 
        /// </summary>
        public static class Tasks
        {
            /// <summary>
            /// Service Call Activity.
            /// </summary>
            public const EventTask ServiceCallTask = (EventTask)1;
        }

        /// <inheritdoc/>
        [Event(CriticalId, Level = EventLevel.Critical)]
        public void Critical(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(CriticalId, message);
            }
        }

        /// <inheritdoc/>
        [Event(ErrorId, Level = EventLevel.Error)]
        public void Error(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(ErrorId, message);
            }
        }

        /// <inheritdoc/>
        [Event(WarningId, Level = EventLevel.Warning)]
        public void Warning(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(WarningId, message);
            }
        }

        /// <inheritdoc/>
        [Event(TraceId, Level = EventLevel.Verbose)]
        public void Trace(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(TraceId, message);
            }
        }

        /// <inheritdoc/>
        [Event(InfoId, Level = EventLevel.Informational)]
        public void Info(string message)
        {
            if (IsEnabled())
            {
                WriteEvent(InfoId, message);
            }
        }

        /// <inheritdoc/>
        [Event(DebugId, Level = EventLevel.Verbose)]
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
        [Event(ExceptionId, Message = ExceptionMessage, Level = EventLevel.Error)]
        public void Exception(string message)
        {
            WriteEvent(ExceptionId, message);
        }

        /// <summary>
        /// A service result exception message.
        /// </summary>
        [Event(ServiceResultExceptionId, Message = ServiceResultExceptionMessage, Level = EventLevel.Error)]
        public void ServiceResultException(int statusCode, string message)
        {
            WriteEvent(ServiceResultExceptionId, statusCode, message);
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
            else
            {
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
        }

        //************************************************************************************************************

        /// <summary>
        /// A server service call message.
        /// </summary>
        [Event(ServiceCallStartId, Message = ServiceCallStartMessage, Level = EventLevel.Verbose, Task = Tasks.ServiceCallTask)]
        public void ServiceCallStart(string serviceName, int requestHandle, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCallStartId, serviceName, requestHandle, pendingRequestCount);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(ServiceCallStartEventId, ServiceCallStartMessage, serviceName, requestHandle, pendingRequestCount);
            }
        }

        /// <summary>
        /// The server service completed message.
        /// </summary>
        [Event(ServiceCallStopId, Message = ServiceCallStopMessage, Level = EventLevel.Verbose, Task = Tasks.ServiceCallTask)]
        public void ServiceCallStop(string serviceName, int requestHandle, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCallStopId, serviceName, requestHandle, pendingRequestCount);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(ServiceCallStopEventId, ServiceCallStopMessage, serviceName, requestHandle, pendingRequestCount);
            }
        }

        /// <summary>
        /// A service message completed with a bad status code.
        /// </summary>
        [Event(ServiceCallBadStopId, Message = ServiceCallBadStopMessage, Level = EventLevel.Warning, Task = Tasks.ServiceCallTask)]
        public void ServiceCallBadStop(string serviceName, int requestHandle, int statusCode, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCallBadStopId, serviceName, requestHandle, statusCode, pendingRequestCount);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(ServiceCallBadStopEventId, ServiceCallBadStopMessage, serviceName, requestHandle, statusCode, pendingRequestCount);
            }
        }

        /// <summary>
        /// A service fault message.
        /// </summary>
        [Event(ServiceFaultId, Message = ServiceFaultMessage, Level = EventLevel.Error)]
        public void ServiceFault(int statusCode)
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
        /// The send response of a server channel.
        /// </summary>
        [Event(SendResponseId, Message = SendResponseMessage, Level = EventLevel.Verbose)]
        public void SendResponse(int channelId, int requestId)
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
