/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Stores the current set of MonitoredItems for a Node.
    /// </summary>
    /// <remarks>
    /// An instance of this object is created the first time a MonitoredItem is
    /// created for any attribute of a Node. The object is deleted when the last
    /// MonitoredItem is deleted.
    /// </remarks>
    public class MonitoredNode2
    {
        #region Public Interface
        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoredNode2"/> class.
        /// </summary>
        /// <param name="nodeManager">The node manager.</param>
        /// <param name="node">The node.</param>
        public MonitoredNode2(CustomNodeManager2 nodeManager, NodeState node)
        {
            NodeManager = nodeManager;
            Node = node;
        }

        /// <summary>
        /// Gets or sets the NodeManager which the MonitoredNode belongs to.
        /// </summary>
        public CustomNodeManager2 NodeManager
        {
            get { return m_nodeManager; }
            set { m_nodeManager = value; }
        }

        /// <summary>
        /// Gets or sets the Node being monitored.
        /// </summary>
        public NodeState Node
        {
            get { return m_node; }
            set { m_node = value; }
        }

        /// <summary>
        /// Gets the current list of data change MonitoredItems.
        /// </summary>
        public ConcurrentDictionary<uint, MonitoredItem> DataChangeMonitoredItems
        {
            get { return m_dataChangeMonitoredItems; }
            private set { m_dataChangeMonitoredItems = value; }
        }

        /// <summary>
        /// Gets the current list of event MonitoredItems.
        /// </summary>
        public ConcurrentDictionary<uint, IEventMonitoredItem> EventMonitoredItems
        {
            get { return m_eventMonitoredItems; }
            private set { m_eventMonitoredItems = value; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance has monitored items.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance has monitored items; otherwise, <c>false</c>.
        /// </value>
        public bool HasMonitoredItems => DataChangeMonitoredItems?.Count > 0 || EventMonitoredItems?.Count > 0;

        /// <summary>
        /// Adds the specified data change monitored item.
        /// </summary>
        /// <param name="datachangeItem">The monitored item.</param>
        public void Add(MonitoredItem datachangeItem)
        {
            if (DataChangeMonitoredItems == null)
            {
                DataChangeMonitoredItems = new ConcurrentDictionary<uint, MonitoredItem>();
                Node.OnStateChanged = OnMonitoredNodeChanged;
            }

            DataChangeMonitoredItems.TryAdd(datachangeItem.Id, datachangeItem);
        }

        /// <summary>
        /// Removes the specified data change monitored item.
        /// </summary>
        /// <param name="datachangeItem">The monitored item.</param>
        public void Remove(MonitoredItem datachangeItem)
        {
            DataChangeMonitoredItems.TryRemove(datachangeItem.Id, out _);

            if (DataChangeMonitoredItems.Count == 0)
            {
                DataChangeMonitoredItems = null;
                Node.OnStateChanged = null;
            }
        }

        /// <summary>
        /// Adds the specified event monitored item.
        /// </summary>
        /// <param name="eventItem">The monitored item.</param>
        public void Add(IEventMonitoredItem eventItem)
        {
            if (EventMonitoredItems == null)
            {
                EventMonitoredItems = new ConcurrentDictionary<uint, IEventMonitoredItem>();
                Node.OnReportEvent = OnReportEvent;
            }

            EventMonitoredItems.TryAdd(eventItem.Id, eventItem);
        }

        /// <summary>
        /// Removes the specified event monitored item.
        /// </summary>
        /// <param name="eventItem">The monitored item.</param>
        public void Remove(IEventMonitoredItem eventItem)
        {
            EventMonitoredItems.TryRemove(eventItem.Id, out _);

            if (EventMonitoredItems.Count == 0)
            {
                EventMonitoredItems = null;
                Node.OnReportEvent = null;
            }
        }

        /// <summary>
        /// Called when a Node produces an event.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="node">The affected node.</param>
        /// <param name="e">The event.</param>
        public void OnReportEvent(ISystemContext context, NodeState node, IFilterTarget e)
        {
            Parallel.ForEach(EventMonitoredItems?.Values, (monitoredItem) => {
                #region  Filter out audit events in case the Server_Auditing values is false or the channel is not encrypted
                if (e is AuditEventState)
                {
                    // check Server.Auditing flag and skip if false
                    if (!NodeManager.Server.Auditing)
                    {
                        return;
                    }
                    else
                    {
                        // check if channel is not encrypted and skip if so
                        if (monitoredItem?.Session?.EndpointDescription?.SecurityMode != MessageSecurityMode.SignAndEncrypt &&
                            monitoredItem?.Session?.EndpointDescription?.TransportProfileUri != Profiles.HttpsBinaryTransport)
                        {
                            return;
                        }
                    }
                }
                #endregion

                // validate if the monitored item has the required role permissions to receive the event
                ServiceResult validationResult = NodeManager.ValidateEventRolePermissions(monitoredItem, e);

                if (ServiceResult.IsBad(validationResult))
                {
                    // skip event reporting for EventType without permissions
                    return;
                }

                // enqueue event
                if (context?.SessionId != null && monitoredItem?.Session?.Id?.Identifier != null)
                {
                    if (monitoredItem.Session.Id.Identifier.Equals(context.SessionId.Identifier))
                    {
                        monitoredItem?.QueueEvent(e);
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    monitoredItem?.QueueEvent(e);
                }
            });
        }

        /// <summary>
        /// Called when the state of a Node changes.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="node">The affected node.</param>
        /// <param name="changes">The mask indicating what changes have occurred.</param>
        public void OnMonitoredNodeChanged(ISystemContext context, NodeState node, NodeStateChangeMasks changes)
        {
            //ensure DataChange is enqueued for all MIs before the next DataChange can start enqueueing
            lock (m_lock)
            {
                Parallel.ForEach(DataChangeMonitoredItems?.Values, (monitoredItem) => {
                    if (monitoredItem.AttributeId == Attributes.Value && (changes & NodeStateChangeMasks.Value) != 0)
                    {
                        // validate if the monitored item has the required role permissions to read the value
                        ServiceResult validationResult = NodeManager.ValidateRolePermissions(new OperationContext(monitoredItem), node.NodeId, PermissionType.Read);

                        if (ServiceResult.IsBad(validationResult))
                        {
                            // skip if the monitored item does not have permission to read
                            return;
                        }

                        QueueValue(context, node, monitoredItem);
                        return;
                    }

                    if (monitoredItem.AttributeId != Attributes.Value && (changes & NodeStateChangeMasks.NonValue) != 0)
                    {
                        QueueValue(context, node, monitoredItem);
                        return;
                    }
                });
            }
        }

        /// <summary>
        /// Reads the value of an attribute and reports it to the MonitoredItem.
        /// </summary>
        public void QueueValue(
            ISystemContext context,
            NodeState node,
            MonitoredItem monitoredItem)
        {
            DataValue value = new DataValue {
                Value = null,
                ServerTimestamp = DateTime.UtcNow,
                SourceTimestamp = DateTime.MinValue,
                StatusCode = StatusCodes.Good
            };

            ServiceResult error = node.ReadAttribute(
                context,
                monitoredItem.AttributeId,
                monitoredItem.IndexRange,
                monitoredItem.DataEncoding,
                value);

            if (ServiceResult.IsBad(error))
            {
                value = null;
            }

            monitoredItem.QueueValue(value, error);
        }
        #endregion

        #region Private Fields
        private CustomNodeManager2 m_nodeManager;
        private readonly object m_lock = new object();
        private NodeState m_node;
        private ConcurrentDictionary<uint, MonitoredItem> m_dataChangeMonitoredItems;
        private ConcurrentDictionary<uint, IEventMonitoredItem> m_eventMonitoredItems;
        #endregion
    }
}
