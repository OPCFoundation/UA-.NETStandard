/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;


namespace Opc.Ua.Server
{
    /// <summary>
    /// The interface that a server exposes to objects that it contains.
    /// </summary>
    public static class ServerUtils
    {
        private enum EventType
        {
            WriteValue,
            CreateItem,
            ModifyItem,
            QueueValue,
            FilterValue,
            DiscardValue,
            PublishValue
        }

        private class Event
        {
            public DateTime Timestamp;
            public EventType EventType;
            public NodeId NodeId;
            public uint ServerHandle;
            public DataValue Value;
            public MonitoringParameters Parameters;
            public MonitoringMode MonitoringMode;
        }

        private static Queue<Event> m_events = new Queue<Event>();
        private static bool m_eventsEnabled;

        /// <summary>
        /// Whether event queuing is enabled.
        /// </summary>
        public static bool EventsEnabled
        {
            get { return m_eventsEnabled; }
            
            set 
            {
                if (m_eventsEnabled != value)
                {
                    if (!value)
                    {
                        lock (m_events)
                        {
                            m_events.Clear();
                        }
                    }
                }

                m_eventsEnabled = value; 
            }
        }

        /// <summary>
        /// Reports a value written.
        /// </summary>
        public static void ReportWriteValue(NodeId nodeId, DataValue value, StatusCode error)
        {
            if (!m_eventsEnabled)
            {
                return;
            }

            lock (m_events)
            {
                Event e = new Event();
                e.EventType = EventType.WriteValue;
                e.NodeId = nodeId;
                e.ServerHandle = 0;
                e.Timestamp = HiResClock.UtcNow;
                e.Value = value;
                e.Parameters = null;
                e.MonitoringMode = MonitoringMode.Disabled;

                if (StatusCode.IsBad(error))
                {
                    e.Value = new DataValue(error);
                    e.Value.WrappedValue = value.WrappedValue;
                }

                m_events.Enqueue(e);
            }
        }

        /// <summary>
        /// Reports a value queued.
        /// </summary>
        public static void ReportQueuedValue(NodeId nodeId, uint serverHandle, DataValue value)
        {
            if (!m_eventsEnabled)
            {
                return;
            }

            lock (m_events)
            {
                Event e = new Event();
                e.EventType = EventType.QueueValue;
                e.NodeId = nodeId;
                e.ServerHandle = serverHandle;
                e.Timestamp = HiResClock.UtcNow;
                e.Value = value;
                e.Parameters = null;
                e.MonitoringMode = MonitoringMode.Disabled;
                m_events.Enqueue(e);
            }
        }

        /// <summary>
        /// Reports a value excluded by the filter.
        /// </summary>
        public static void ReportFilteredValue(NodeId nodeId, uint serverHandle, DataValue value)
        {
            if (!m_eventsEnabled)
            {
                return;
            }

            lock (m_events)
            {
                Event e = new Event();
                e.EventType = EventType.FilterValue;
                e.NodeId = nodeId;
                e.ServerHandle = serverHandle;
                e.Timestamp = HiResClock.UtcNow;
                e.Value = value;
                e.Parameters = null;
                e.MonitoringMode = MonitoringMode.Disabled;
                m_events.Enqueue(e);
            }
        }

        /// <summary>
        /// Reports a value discarded because of queue overflow.
        /// </summary>
        public static void ReportDiscardedValue(NodeId nodeId, uint serverHandle, DataValue value)
        {
            if (!m_eventsEnabled)
            {
                return;
            }

            lock (m_events)
            {
                Event e = new Event();
                e.EventType = EventType.DiscardValue;
                e.NodeId = nodeId;
                e.ServerHandle = serverHandle;
                e.Timestamp = HiResClock.UtcNow;
                e.Value = value;
                e.Parameters = null;
                e.MonitoringMode = MonitoringMode.Disabled;
                m_events.Enqueue(e);
            }
        }

        /// <summary>
        /// Reports a value published.
        /// </summary>
        public static void ReportPublishValue(NodeId nodeId, uint serverHandle, DataValue value)
        {
            if (!m_eventsEnabled)
            {
                return;
            }

            lock (m_events)
            {
                Event e = new Event();
                e.EventType = EventType.PublishValue;
                e.NodeId = nodeId;
                e.ServerHandle = serverHandle;
                e.Timestamp = HiResClock.UtcNow;
                e.Value = value;
                e.Parameters = null;
                e.MonitoringMode = MonitoringMode.Disabled;
                m_events.Enqueue(e);
            }
        }

        /// <summary>
        /// Reports a new monitored item.
        /// </summary>
        public static void ReportCreateMonitoredItem(
            NodeId nodeId, 
            uint serverHandle,
            double samplingInterval,
            uint queueSize,
            bool discardOldest,
            MonitoringFilter filter,
            MonitoringMode monitoringMode)
        {
            if (!m_eventsEnabled)
            {
                return;
            }

            lock (m_events)
            {
                Event e = new Event();
                e.EventType = EventType.CreateItem;
                e.NodeId = nodeId;
                e.ServerHandle = serverHandle;
                e.Timestamp = HiResClock.UtcNow;
                e.Value = null;
                e.Parameters = new MonitoringParameters();
                e.Parameters.SamplingInterval = samplingInterval;
                e.Parameters.QueueSize = queueSize;
                e.Parameters.DiscardOldest = discardOldest;
                e.Parameters.Filter = new ExtensionObject(filter);
                e.MonitoringMode = monitoringMode;
                m_events.Enqueue(e);
            }
        }

        /// <summary>
        /// Reports a modified monitored item.
        /// </summary>
        public static void ReportModifyMonitoredItem(
            NodeId nodeId,
            uint serverHandle,
            double samplingInterval,
            uint queueSize,
            bool discardOldest,
            MonitoringFilter filter,
            MonitoringMode monitoringMode)
        {
            if (!m_eventsEnabled)
            {
                return;
            }

            lock (m_events)
            {
                Event e = new Event();
                e.EventType = EventType.ModifyItem;
                e.NodeId = nodeId;
                e.ServerHandle = serverHandle;
                e.Timestamp = HiResClock.UtcNow;
                e.Value = null;
                e.Parameters = new MonitoringParameters();
                e.Parameters.SamplingInterval = samplingInterval;
                e.Parameters.QueueSize = queueSize;
                e.Parameters.DiscardOldest = discardOldest;
                e.Parameters.Filter = new ExtensionObject(filter);
                e.MonitoringMode = monitoringMode;
                m_events.Enqueue(e);
            }
        }

        #region Error and Diagnostics
        /// <summary>
        /// Fills in the diagnostic information after an error.
        /// </summary>
        public static uint CreateError(
            uint                     code, 
            OperationContext         context, 
            DiagnosticInfoCollection diagnosticInfos, 
            int                      index)
        {
            ServiceResult error = new ServiceResult(code);
            
            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfos[index] = new DiagnosticInfo(error, context.DiagnosticsMask, false, context.StringTable);
            }

            return error.Code;
        }
        
        /// <summary>
        /// Fills in the diagnostic information after an error.
        /// </summary>
        public static bool CreateError(
            uint                      code,  
            StatusCodeCollection      results,
            DiagnosticInfoCollection  diagnosticInfos, 
            OperationContext          context)
        {
            ServiceResult error = new ServiceResult(code);
            results.Add(error.Code);
            
            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfos.Add(new DiagnosticInfo(error, context.DiagnosticsMask, false, context.StringTable));
                return true;
            }

            return false;
        }
        
        /// <summary>
        /// Fills in the diagnostic information after an error.
        /// </summary>
        public static bool CreateError(
            uint                     code,  
            StatusCodeCollection     results,
            DiagnosticInfoCollection diagnosticInfos, 
            int                      index,
            OperationContext         context)
        {
            ServiceResult error = new ServiceResult(code);
            results[index] = error.Code;
            
            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfos[index] = new DiagnosticInfo(error, context.DiagnosticsMask, false, context.StringTable);
                return true;
            }

            return false;
        }
        
        /// <summary>
        /// Creates a place holder in the lists for the results.
        /// </summary>
        public static void CreateSuccess(
            StatusCodeCollection     results,
            DiagnosticInfoCollection diagnosticInfos,
            OperationContext         context)
        {
            results.Add(StatusCodes.Good);
            
            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
            {
                diagnosticInfos.Add(null);
            }
        }
        
        /// <summary>
        /// Creates a collection of diagnostics from a set of errors.
        /// </summary>
        public static DiagnosticInfoCollection CreateDiagnosticInfoCollection(
            OperationContext     context,
            IList<ServiceResult> errors)
        {
            // all done if no diagnostics requested.
            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) == 0)
            {
                return null;
            }
            
            // create diagnostics.
            DiagnosticInfoCollection results = new DiagnosticInfoCollection(errors.Count);

            foreach (ServiceResult error in errors)
            {
                if (ServiceResult.IsBad(error))
                {
                    results.Add(new DiagnosticInfo(error, context.DiagnosticsMask, false, context.StringTable));
                }
                else
                {
                    results.Add(null);
                }
            }

            return results;
        }
        
        /// <summary>
        /// Creates a collection of status codes and diagnostics from a set of errors.
        /// </summary>
        public static StatusCodeCollection CreateStatusCodeCollection(
            OperationContext             context,
            IList<ServiceResult>         errors, 
            out DiagnosticInfoCollection diagnosticInfos)
        {
            diagnosticInfos = null;

            bool noErrors = true;
            StatusCodeCollection results = new StatusCodeCollection(errors.Count);

            foreach (ServiceResult error in errors)
            {
                if (ServiceResult.IsBad(error))
                {
                    results.Add(error.Code);
                    noErrors = false;
                }
                else
                {
                    results.Add(StatusCodes.Good);
                }
            }

            // only generate diagnostics if errors exist.
            if (noErrors)
            {
                diagnosticInfos = CreateDiagnosticInfoCollection(context, errors);
            }
            
            return results;
        }

        /// <summary>
        /// Creates the diagnostic info and translates any strings.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="context">The context containing the string stable.</param>
        /// <param name="error">The error to translate.</param>
        /// <returns>The diagnostics with references to the strings in the context string table.</returns>
        public static DiagnosticInfo CreateDiagnosticInfo(
            IServerInternal  server,
            OperationContext context,
            ServiceResult    error)
        {
            if (error == null)
            {
                return null;
            }

            ServiceResult translatedError = error;

            if ((context.DiagnosticsMask & DiagnosticsMasks.LocalizedText) != 0)
            {
                translatedError = server.ResourceManager.Translate(context.PreferredLocales, error);
            }

            DiagnosticInfo diagnosticInfo = new DiagnosticInfo(
                translatedError, 
                context.DiagnosticsMask, 
                false, 
                context.StringTable);

            return diagnosticInfo;
        }
        #endregion
    }
}
