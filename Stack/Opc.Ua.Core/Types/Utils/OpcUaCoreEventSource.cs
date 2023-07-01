/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using static Opc.Ua.Utils;

namespace Opc.Ua
{
    /// <summary>
    /// Event source for high performance logging.
    /// </summary>
    [EventSource(Name = "OPC-UA-Core", Guid = "753029BC-A4AA-4440-8668-290D0692A72B")]
    internal sealed class OpcUaCoreEventSource : EventSource, ILogger
    {
        #region Definitions
        private const int TraceId = 1;
        private const int DebugId = TraceId + 1;
        private const int InfoId = DebugId + 1;
        private const int WarningId = InfoId + 1;
        private const int ErrorId = WarningId + 1;
        private const int CriticalId = ErrorId + 1;

        /// <summary>
        /// The core event ids.
        /// </summary>
        private const int ServiceCallStartId = CriticalId + 3;
        private const int ServiceCallStopId = ServiceCallStartId + 1;
        private const int ServiceCallBadStopId = ServiceCallStopId + 1;
        private const int SubscriptionStateId = ServiceCallBadStopId + 1;
        private const int SendResponseId = SubscriptionStateId + 1;
        private const int ServiceFaultId = SendResponseId + 1;

        /// <summary>
        /// The core messages.
        /// </summary>
        private const string ServiceCallStartMessage = "{0} Called. RequestHandle={1}, PendingRequestCount={2}";
        private const string ServiceCallStopMessage = "{0} Completed. RequestHandle={1}, PendingRequestCount={2}";
        private const string ServiceCallBadStopMessage = "{0} Completed. RequestHandle={1}, PendingRequestCount={2}, StatusCode={3}";
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
        /// The task definitions.
        /// </summary>
        public static class Tasks
        {
            /// <summary>
            /// Service Call Activity.
            /// </summary>
            public const EventTask ServiceCallTask = (EventTask)1;
        }

        /// <summary>
        /// The keywords for message filters.
        /// </summary>
        public static class Keywords
        {
            /// <summary>
            /// Turns on the 'FormatMessage' event when ILogger.Log() is called.  It gives the formatted string version of the information.
            /// </summary>
            public const EventKeywords FormattedMessage = (EventKeywords)1;
            /// <summary>
            /// Services events.
            /// </summary>
            public const EventKeywords Services = (EventKeywords)2;
        }
        #endregion

        #region ILogger Messages 
        /// <inheritdoc/>
        [Event(CriticalId, Keywords = Keywords.FormattedMessage, Level = EventLevel.Critical)]
        internal void Critical(int eventId, string eventName, string message)
        {
            WriteFormattedMessage(CriticalId, eventId, eventName, message);
        }

        /// <inheritdoc/>
        [Event(ErrorId, Keywords = Keywords.FormattedMessage, Level = EventLevel.Error)]
        internal void Error(int eventId, string eventName, string message)
        {
            WriteFormattedMessage(ErrorId, eventId, eventName, message);
        }

        /// <inheritdoc/>
        [Event(WarningId, Keywords = Keywords.FormattedMessage, Level = EventLevel.Warning)]
        internal void Warning(int eventId, string eventName, string message)
        {
            WriteFormattedMessage(WarningId, eventId, eventName, message);
        }

        /// <inheritdoc/>
        [Event(TraceId, Keywords = Keywords.FormattedMessage, Level = EventLevel.Verbose)]
        internal void Trace(int eventId, string eventName, string message)
        {
            WriteFormattedMessage(TraceId, eventId, eventName, message);
        }

        /// <inheritdoc/>
        [Event(InfoId, Keywords = Keywords.FormattedMessage, Level = EventLevel.Informational)]
        internal void Info(int eventId, string eventName, string message)
        {
            WriteFormattedMessage(InfoId, eventId, eventName, message);
        }

        /// <inheritdoc/>
        [Event(DebugId, Keywords = Keywords.FormattedMessage, Level = EventLevel.Verbose)]
        internal void Debug(int eventId, string eventName, string message)
        {
#if DEBUG
            WriteFormattedMessage(DebugId, eventId, eventName, message);
#endif
        }
        #endregion

        #region ILogger Interface
        [NonEvent]
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled())
            {
                return;
            }

            string message = null;

            // Log the formatted message
            if (IsEnabled(EventLevel.Informational, Keywords.FormattedMessage))
            {
                message = formatter(state, exception);
                switch (logLevel)
                {
                    case LogLevel.Trace: Trace(eventId.Id, eventId.Name, message); break;
                    case LogLevel.Debug: Debug(eventId.Id, eventId.Name, message); break;
                    case LogLevel.Information: Info(eventId.Id, eventId.Name, message); break;
                    case LogLevel.Warning: Warning(eventId.Id, eventId.Name, message); break;
                    case LogLevel.Error: Error(eventId.Id, eventId.Name, message); break;
                    case LogLevel.Critical: Critical(eventId.Id, eventId.Name, message); break;
                }
            }
        }

        [NonEvent]
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && IsEnabled();

        [NonEvent]
        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        /// <summary>
        /// The helper to write the formatted message as event.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="eventId"></param>
        /// <param name="EventName"></param>
        /// <param name="FormattedMessage"></param>
        [NonEvent]
        internal void WriteFormattedMessage(int id, int eventId, string EventName, string FormattedMessage)
        {
            if (IsEnabled())
            {
                EventName = EventName ?? "";
                FormattedMessage = FormattedMessage ?? "";
                WriteEvent(id, eventId, EventName, FormattedMessage);
            }
        }


        [NonEvent]
        private LogLevel GetDefaultLevel()
        {
            EventKeywords allMessageKeywords = Keywords.FormattedMessage;

            if (IsEnabled(EventLevel.Verbose, allMessageKeywords))
            {
                return LogLevel.Trace;
            }

            if (IsEnabled(EventLevel.Informational, allMessageKeywords))
            {
                return LogLevel.Information;
            }

            if (IsEnabled(EventLevel.Warning, allMessageKeywords))
            {
                return LogLevel.Warning;
            }

            if (IsEnabled(EventLevel.Error, allMessageKeywords))
            {
                return LogLevel.Error;
            }

            return LogLevel.Critical;
        }
        #endregion

        #region Service Events
        /// <summary>
        /// A server service call message.
        /// </summary>
        [Event(ServiceCallStartId, Keywords = Keywords.Services, Message = ServiceCallStartMessage, Level = EventLevel.Verbose, Task = Tasks.ServiceCallTask)]
        public void ServiceCallStart(string serviceName, int requestHandle, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCallStartId, serviceName, requestHandle, pendingRequestCount);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.Log(LogLevel.Trace, ServiceCallStartEventId, ServiceCallStartMessage, serviceName, requestHandle, pendingRequestCount);
            }
        }

        /// <summary>
        /// The server service completed message.
        /// </summary>
        [Event(ServiceCallStopId, Keywords = Keywords.Services, Message = ServiceCallStopMessage, Level = EventLevel.Verbose, Task = Tasks.ServiceCallTask)]
        public void ServiceCallStop(string serviceName, int requestHandle, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCallStopId, serviceName, requestHandle, pendingRequestCount);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.Log(LogLevel.Trace, ServiceCallStopEventId, ServiceCallStopMessage, serviceName, requestHandle, pendingRequestCount);
            }
        }

        /// <summary>
        /// A service message completed with a bad status code.
        /// </summary>
        [Event(ServiceCallBadStopId, Keywords = Keywords.Services, Message = ServiceCallBadStopMessage, Level = EventLevel.Warning, Task = Tasks.ServiceCallTask)]
        public void ServiceCallBadStop(string serviceName, int requestHandle, int statusCode, int pendingRequestCount)
        {
            if (IsEnabled())
            {
                WriteEvent(ServiceCallBadStopId, serviceName, requestHandle, pendingRequestCount, statusCode);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.Log(LogLevel.Trace, ServiceCallBadStopEventId, ServiceCallBadStopMessage, serviceName, requestHandle, pendingRequestCount, statusCode);
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
                Utils.Log(LogLevel.Trace, SendResponseEventId, SendResponseMessage, channelId, requestId);
            }
        }
        #endregion
    }
}
