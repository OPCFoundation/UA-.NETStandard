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
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// Used as underlying tracing object for event processing.
    /// </summary>
    public class Tracing
    {
        #region Private Members
        private static object m_syncRoot = new Object();
        private static Tracing s_instance;
        #endregion Private Members

        #region Singleton Instance
        /// <summary>
        /// Private constructor.
        /// </summary>
        private Tracing()
        { }

        /// <summary>
        /// Public Singleton Instance getter.
        /// </summary>
        public static Tracing Instance
        {
            get
            {
                if (s_instance == null)
                {
                    lock (m_syncRoot)
                    {
                        if (s_instance == null)
                        {
                            s_instance = new Tracing();
                        }
                    }
                }
                return s_instance;
            }
        }
        #endregion Singleton Instance

        #region Public Events
        /// <summary>
        /// Occurs when a trace call is made.
        /// </summary>
        public event EventHandler<TraceEventArgs> TraceEventHandler;
        #endregion Public Events

        #region Internal Members
        internal void RaiseTraceEvent(TraceEventArgs eventArgs)
        {
            if (TraceEventHandler != null)
            {
                try
                {
                    TraceEventHandler(this, eventArgs);
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "Exception invoking Trace Event Handler", true, null);
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// The event listener.
    /// </summary>
    public class OpcUaCoreEventListener : EventListener
    {
        /// <summary>
        /// 
        /// </summary>
        public OpcUaCoreEventListener()
        {
#if !NETSTANDARD2_0
            this.EventSourceCreated += OpcUaCoreEventListener_EventSourceCreated;
            this.EventWritten += OpcUaCoreEventListener_EventWritten;
#endif
        }

#if NETSTANDARD2_0
        /// <summary>
        /// 
        /// </summary>
        /// <param name="eventSource"></param>
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            OnEventSourceCreated_Internal(eventSource);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="eventData"></param>
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            OpcUaCoreEventListener_EventWritten(null, eventData);
        }
#endif

        private void OpcUaCoreEventListener_EventWritten(object sender, EventWrittenEventArgs ev)
        {
            if (ev.EventId == 1)
            {
                Utils.Trace(null, Utils.TraceMasks.Information, ev.Payload[0] as string, false);
            }
        }

#if !NETSTANDARD2_0
        private void OpcUaCoreEventListener_EventSourceCreated(
            object sender,
            EventSourceCreatedEventArgs ev)
        {
            OnEventSourceCreated_Internal(ev.EventSource);
        }
#endif

        private void OnEventSourceCreated_Internal(EventSource eventSource)
        {
            if (eventSource != null)
            {
                Console.WriteLine("Source: {0} Guid: {1}", eventSource.Name, eventSource.Guid);

                if (eventSource.Name.StartsWith("OPC-UA"))
                {
                    this.EnableEvents(eventSource, EventLevel.Verbose);
                }
            }
        }
    }

    /// <summary>
    /// The 'AllInOne' event source.
    /// </summary>
    [EventSource(Name = "OPC-UA-Core")]
    public sealed partial class OpcUaCoreEventSource : EventSource
    {
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
        [Event(1, Message = null, Level = EventLevel.Informational, Keywords = Keywords.Trace)]
        public void Trace(string message)
        {
            WriteEvent(1, message);
        }

        /// <summary>
        /// 
        /// </summary>
        [NonEvent]
        public void Trace(string format, params object[] args)
        {
            if (IsEnabled())
            {
                Trace(String.Format(format, args));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        [Event(2, Message = "Exception: {0}", Level = EventLevel.Error, Keywords = Keywords.Exception)]
        public void Exception(string message)
        {
            WriteEvent(2, message);
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
                if ((Utils.TraceMask & Utils.TraceMasks.StackTrace) != 0)
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
            if (sre != null)
            {
                ServiceResultException((int)sre.StatusCode, result);
            }
            else
            {
                Exception(result);
            }

            // trace message.
            Utils.Trace(ex, Utils.TraceMasks.Error, result, false, null);
        }

        /// <summary>
        /// 
        /// </summary>
        [Event(3, Message = "ServiceResultException: {0} {1}", Level = EventLevel.Error, Keywords = Keywords.Trace)]
        public void ServiceResultException(int statusCode, string message)
        {
            WriteEvent(3, statusCode, message);
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
        /// <summary>
        /// 
        /// </summary>
        public static OpcUaCoreEventSource Log = new OpcUaCoreEventSource();
    }

}
