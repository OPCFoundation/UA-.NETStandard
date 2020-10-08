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
using System.Text;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Keeps track of the monitored items for a single node.
    /// </summary>
    [Obsolete("Class replaced by Opc.Ua.Server.MonitoredNode2")]
    public class MonitoredNode
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with the context for the node being monitored.
        /// </summary>
        public MonitoredNode(
            IServerInternal server,
            INodeManager nodeManager,
            NodeState node)
        {
            m_server = server;
            m_nodeManager = nodeManager;
            m_node = node;
        }
        #endregion
        
        #region Public Properties
        /// <summary>
        /// The server that the node belongs to.
        /// </summary>
        public IServerInternal Server
        {
            get { return m_server; }
        }

        /// <summary>
        /// The node manager that the node belongs to.
        /// </summary>
        public INodeManager NodeManager
        {
            get { return m_nodeManager; }
        }

        /// <summary>
        /// The node being monitored.
        /// </summary>
        public NodeState Node
        {
            get { return m_node; }
        }

        /// <summary>
        /// Whether the node has any active monitored items for the specified attribute.
        /// </summary>
        public bool IsMonitoringRequired(uint attributeId)
        {
            if (m_monitoredItems != null)
            {
                for (int ii = 0; ii < m_monitoredItems.Count; ii++)
                {
                    DataChangeMonitoredItem monitoredItem = m_monitoredItems[ii];

                    if (monitoredItem.AttributeId == attributeId && monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        #endregion
    
        #region Public Methods
        /// <summary>
        /// Creates a new data change monitored item.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="monitoredItemId">The unique identifier for the monitiored item.</param>
        /// <param name="attributeId">The attribute to monitor.</param>
        /// <param name="indexRange">The index range to use for array values.</param>
        /// <param name="dataEncoding">The data encoding to return for structured values.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks to use.</param>
        /// <param name="timestampsToReturn">The timestamps to return.</param>
        /// <param name="monitoringMode">The initial monitoring mode.</param>
        /// <param name="clientHandle">The handle assigned by the client.</param>
        /// <param name="samplingInterval">The sampling interval.</param>
        /// <param name="queueSize">The queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values when the queue overflows.</param>
        /// <param name="filter">The data change filter to use.</param>
        /// <param name="range">The range to use when evaluating a percentage deadband filter.</param>
        /// <param name="alwaysReportUpdates">Whether the monitored item should skip the check for a change in value.</param>
        /// <returns>The new monitored item.</returns>
        public DataChangeMonitoredItem CreateDataChangeItem(
            ISystemContext context,
            uint monitoredItemId,
            uint attributeId,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringMode monitoringMode,
            uint clientHandle,
            double samplingInterval,
            uint queueSize,
            bool discardOldest,
            DataChangeFilter filter,
            Range range,
            bool alwaysReportUpdates)
        {
            DataChangeMonitoredItem monitoredItem = new DataChangeMonitoredItem(
                this,
                monitoredItemId,
                attributeId,
                indexRange,
                dataEncoding,
                diagnosticsMasks,
                timestampsToReturn,
                monitoringMode,
                clientHandle,
                samplingInterval,
                queueSize,
                discardOldest,
                filter,
                range,
                alwaysReportUpdates);

            if (m_monitoredItems == null)
            {
                m_monitoredItems = new List<DataChangeMonitoredItem>();
                m_node.OnStateChanged = OnNodeChange;
            }

            m_monitoredItems.Add(monitoredItem);

            return monitoredItem;
        }
        
        /// <summary>
        /// Creates a new data change monitored item.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="monitoredItemId">The unique identifier for the monitiored item.</param>
        /// <param name="attributeId">The attribute to monitor.</param>
        /// <param name="indexRange">The index range to use for array values.</param>
        /// <param name="dataEncoding">The data encoding to return for structured values.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks to use.</param>
        /// <param name="timestampsToReturn">The timestamps to return.</param>
        /// <param name="monitoringMode">The initial monitoring mode.</param>
        /// <param name="clientHandle">The handle assigned by the client.</param>
        /// <param name="samplingInterval">The sampling interval.</param>
        /// <param name="alwaysReportUpdates">Whether the monitored item should skip the check for a change in value.</param>
        /// <returns>The new monitored item.</returns>
        public DataChangeMonitoredItem CreateDataChangeItem(
            ISystemContext context,
            uint monitoredItemId,
            uint attributeId,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringMode monitoringMode,
            uint clientHandle,
            double samplingInterval,
            bool alwaysReportUpdates)
        {
            return CreateDataChangeItem(
                context,
                monitoredItemId,
                attributeId,
                indexRange,
                dataEncoding,
                diagnosticsMasks,
                timestampsToReturn,
                monitoringMode,
                clientHandle,
                samplingInterval,
                0,
                false,
                null,
                null,
                alwaysReportUpdates);
        }

        /// <summary>
        /// Deletes the monitored item.
        /// </summary>
        public void DeleteItem(IMonitoredItem monitoredItem)
        {
            if (m_monitoredItems != null)
            {
                for (int ii = 0; ii < m_monitoredItems.Count; ii++)
                {
                    if (Object.ReferenceEquals(monitoredItem, m_monitoredItems[ii]))
                    {
                        m_monitoredItems.RemoveAt(ii);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Handles change events raised by the node.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="state">The node that raised the event.</param>
        /// <param name="masks">What caused the event to be raised</param>
        public void OnNodeChange(ISystemContext context, NodeState state, NodeStateChangeMasks masks)
        {
            if (m_monitoredItems != null)
            {
                for (int ii = 0; ii < m_monitoredItems.Count; ii++)
                {
                    DataChangeMonitoredItem monitoredItem = m_monitoredItems[ii];

                    // check if the node has been deleted.
                    if ((masks & NodeStateChangeMasks.Deleted) != 0)
                    {
                        monitoredItem.QueueValue(null, StatusCodes.BadNodeIdUnknown);
                        continue;
                    }

                    if (monitoredItem.AttributeId == Attributes.Value)
                    {
                        if ((masks & NodeStateChangeMasks.Value) != 0)
                        {
                            monitoredItem.ValueChanged(context);
                        }
                    }
                    else
                    {
                        if ((masks & NodeStateChangeMasks.NonValue) != 0)
                        {
                            monitoredItem.ValueChanged(context);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Subscribes to events produced by the node.
        /// </summary>
        public void SubscribeToEvents(ISystemContext context, IEventMonitoredItem eventSubscription)
        {
            if (m_eventSubscriptions == null)
            {
                m_eventSubscriptions = new List<IEventMonitoredItem>();
            }

            if (m_eventSubscriptions.Count == 0)
            {
                m_node.OnReportEvent = OnReportEvent;
                m_node.SetAreEventsMonitored(context, true, true);
            }
            
            for (int ii = 0; ii < m_eventSubscriptions.Count; ii++)
            {
                if (Object.ReferenceEquals(eventSubscription, m_eventSubscriptions[ii]))
                {
                    return;
                }
            }

            m_eventSubscriptions.Add(eventSubscription);
        }

        /// <summary>
        /// Unsubscribes to events produced by the node.
        /// </summary>
        public void UnsubscribeToEvents(ISystemContext context, IEventMonitoredItem eventSubscription)
        {
            if (m_eventSubscriptions != null)
            {
                for (int ii = 0; ii < m_eventSubscriptions.Count; ii++)
                {
                    if (Object.ReferenceEquals(eventSubscription, m_eventSubscriptions[ii]))
                    {
                        m_eventSubscriptions.RemoveAt(ii);

                        if (m_eventSubscriptions.Count == 0)
                        {
                            m_node.SetAreEventsMonitored(context, false, true);
                            m_node.OnReportEvent = null;
                        }

                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Handles events reported by the node.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="state">The node that raised the event.</param>
        /// <param name="e">The event to report.</param>
        public void OnReportEvent(ISystemContext context, NodeState state, IFilterTarget e)
        {
            if (m_eventSubscriptions != null)
            {
                for (int ii = 0; ii < m_eventSubscriptions.Count; ii++)
                {
                    m_eventSubscriptions[ii].QueueEvent(e);
                }
            }
        }

        /// <summary>
        /// Resends the events for any conditions belonging to the node or its children.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="monitoredItem">The item to refresh.</param>
        public void ConditionRefresh(
            ISystemContext context, 
            IEventMonitoredItem monitoredItem)
        {
            if (m_eventSubscriptions != null)
            {
                for (int ii = 0; ii < m_eventSubscriptions.Count; ii++)
                {
                    // only process items monitoring this node.
                    if (!Object.ReferenceEquals(monitoredItem, m_eventSubscriptions[ii]))
                    {
                        continue;
                    }
                    
                    // get the set of condition events for the node and its children.
                    List<IFilterTarget> events = new List<IFilterTarget>();
                    m_node.ConditionRefresh(context, events, true);

                    // report the events to the monitored item.
                    for (int jj = 0; jj < events.Count; jj++)
                    {
                        monitoredItem.QueueEvent(events[jj]);
                    }
                }
            }
        }
        #endregion

        #region Private Fields
        private IServerInternal m_server;
        private INodeManager m_nodeManager;
        private NodeState m_node;
        private List<IEventMonitoredItem> m_eventSubscriptions;
        private List<DataChangeMonitoredItem> m_monitoredItems;
        #endregion
    }
}
