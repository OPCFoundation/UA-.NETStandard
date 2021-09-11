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
using System.Globalization;
using System.Text;
using static Opc.Ua.Utils;

namespace Opc.Ua
{
    /// <summary>
    /// 
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
    /// The 'Common' event source.
    /// Every module includes a link to this class
    /// </summary>
    [EventSource(Name = "OPC-UA-Core")]
    public partial class OpcUaEventSource : EventSource, IOpcUaEventSource
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
        private const string ServerCallMessage = "Service Fault Occured. Reason={0}";

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
                Utils.Trace(TraceMasks.Error, message, false);
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
                Utils.Trace(TraceMasks.Error, message, false);
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
                Utils.Trace(TraceMasks.Information, message, false);
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
                Utils.Trace(TraceMasks.Information, message, false);
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
                Utils.Trace(TraceMasks.Information, message, false);
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
                Utils.Trace(TraceMasks.Error, format, false, args);
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
                Utils.Trace(TraceMasks.Error, format, false, args);
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
                Utils.Trace(TraceMasks.Information, format, false, args);
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
                Utils.Trace(TraceMasks.Information, format, false, args);
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
                Utils.Trace(mask, format, false, args);
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
                Utils.Trace(TraceMasks.Information, format, false, args);
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
            StringBuilder message = new StringBuilder();

            // format message.            
            if (args != null && args.Length > 0)
            {
                try
                {
                    message.AppendFormat(CultureInfo.InvariantCulture, format, args);
                    message.AppendLine();
                }
                catch (Exception)
                {
                    message.AppendLine(format);
                }
            }
            else
            {
                message.AppendLine(format);
            }

            // append exception information.
            ServiceResultException sre = null;
            if (ex != null)
            {
                sre = ex as ServiceResultException;

                if (sre != null)
                {
                    message.AppendFormat(CultureInfo.InvariantCulture, " {0} '{1}'", StatusCodes.GetBrowseName(sre.StatusCode), sre.Message);
                }
                else
                {
                    message.AppendFormat(CultureInfo.InvariantCulture, " {0} '{1}'", ex.GetType().Name, ex.Message);
                }
                message.AppendLine();

                // append stack trace.
                if ((TraceMask & TraceMasks.StackTrace) != 0)
                {
                    message.AppendLine();
                    message.AppendLine();
                    var separator = new String('=', 40);
                    message.AppendLine(separator);
                    message.AppendLine(new ServiceResult(ex).ToLongString());
                    message.AppendLine(separator);
                }
            }
            var result = message.ToString();

            if (IsEnabled())
            {
                if (sre != null)
                {
                    ServiceResultException((int)sre.StatusCode, result);
                }
                else
                {
                    Exception(result);
                }
            }
            else
            {
                // trace message.
                Utils.Trace(ex, TraceMasks.Error, result, false, null);
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
                Utils.Trace(TraceMasks.Information, ServiceCallMessage, false, serviceName, requestHandle, pendingRequestCount);
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
                Utils.Trace(TraceMasks.Information, ServiceCompletedMessage, false, serviceName, requestHandle, pendingRequestCount);
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
                Utils.Trace(TraceMasks.Information, ServiceCompletedBadMessage, false, serviceName, requestHandle, statusCode, pendingRequestCount);
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
                Utils.Trace(TraceMasks.Service, ServiceFaultMessage, statusCode);
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
                Utils.Trace(TraceMasks.Service, ServerCallMessage, false, requestType, requestId);
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
                Utils.Trace(TraceMasks.Security, format, false, args);
            }
        }

#if mist
        [Event(2, Message = "Starting up.", Keywords = Keywords.Perf, Level = EventLevel.Informational)]
        public void Startup() { WriteEvent(2); }

        [Event(3, Message = "loading page {1} activityID={0}", Opcode = EventOpcode.Start,
            Task = Tasks.Page, Keywords = Keywords.Page, Level = EventLevel.Informational)]
        public void PageStart(int ID, string url) { if (IsEnabled()) WriteEvent(3, ID, url); }

        [Event(4, Opcode = EventOpcode.Stop, Task = Tasks.Page, Keywords = Keywords.Page, Level = EventLevel.Informational)]
        public void PageStop(int ID) { if (IsEnabled()) WriteEvent(4, ID); }

        [Event(5, Opcode = EventOpcode.Start, Task = Tasks.DBQuery, Keywords = Keywords.DataBase, Level = EventLevel.Informational)]
        public void DBQueryStart(string sqlQuery) { WriteEvent(5, sqlQuery); }

        [Event(6, Opcode = EventOpcode.Stop, Task = Tasks.DBQuery, Keywords = Keywords.DataBase, Level = EventLevel.Informational)]
        public void DBQueryStop() { WriteEvent(6); }

        [Event(7, Level = EventLevel.Verbose, Keywords = Keywords.DataBase)]
        public void Mark(int ID) { if (IsEnabled()) WriteEvent(7, ID); }

        //[Event(8)]
        //public void LogColor(MyColor color) { WriteEvent(8, (int)color); }
#endif

        // Opc.Ua.Client
    }
}
