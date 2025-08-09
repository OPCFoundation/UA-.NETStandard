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
        public CustomNodeManager2 NodeManager { get; set; }

        /// <summary>
        /// Gets or sets the Node being monitored.
        /// </summary>
        public NodeState Node { get; set; }

        /// <summary>
        /// Gets the current list of data change MonitoredItems.
        /// </summary>
        public List<IDataChangeMonitoredItem2> DataChangeMonitoredItems { get; private set; }

        /// <summary>
        /// Gets the current list of event MonitoredItems.
        /// </summary>
        public List<IEventMonitoredItem> EventMonitoredItems { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance has monitored items.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance has monitored items; otherwise, <c>false</c>.
        /// </value>
        public bool HasMonitoredItems
        {
            get
            {
                if (DataChangeMonitoredItems != null && DataChangeMonitoredItems.Count > 0)
                {
                    return true;
                }

                if (EventMonitoredItems != null && EventMonitoredItems.Count > 0)
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Adds the specified data change monitored item.
        /// </summary>
        /// <param name="datachangeItem">The monitored item.</param>
        public void Add(IDataChangeMonitoredItem2 datachangeItem)
        {
            if (DataChangeMonitoredItems == null)
            {
                DataChangeMonitoredItems = [];
                Node.OnStateChanged = OnMonitoredNodeChanged;
            }

            DataChangeMonitoredItems.Add(datachangeItem);
        }

        /// <summary>
        /// Removes the specified data change monitored item.
        /// </summary>
        /// <param name="datachangeItem">The monitored item.</param>
        public void Remove(IDataChangeMonitoredItem2 datachangeItem)
        {
            for (int ii = 0; ii < DataChangeMonitoredItems.Count; ii++)
            {
                if (ReferenceEquals(DataChangeMonitoredItems[ii], datachangeItem))
                {
                    DataChangeMonitoredItems.RemoveAt(ii);

                    // Remove the cached context for the monitored item
                    m_contextCache.TryRemove(datachangeItem.Id, out _);

                    break;
                }
            }

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
                EventMonitoredItems = [];
                Node.OnReportEvent = OnReportEvent;
            }

            EventMonitoredItems.Add(eventItem);
        }

        /// <summary>
        /// Removes the specified event monitored item.
        /// </summary>
        /// <param name="eventItem">The monitored item.</param>
        public void Remove(IEventMonitoredItem eventItem)
        {
            for (int ii = 0; ii < EventMonitoredItems.Count; ii++)
            {
                if (ReferenceEquals(EventMonitoredItems[ii], eventItem))
                {
                    EventMonitoredItems.RemoveAt(ii);
                    break;
                }
            }

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
            var eventMonitoredItems = new List<IEventMonitoredItem>();

            lock (NodeManager.Lock)
            {
                if (EventMonitoredItems == null)
                {
                    return;
                }

                for (int ii = 0; ii < EventMonitoredItems.Count; ii++)
                {
                    IEventMonitoredItem monitoredItem = EventMonitoredItems[ii];
                    // enqueue event for role permission validation
                    eventMonitoredItems.Add(monitoredItem);
                }
            }

            for (int ii = 0; ii < eventMonitoredItems.Count; ii++)
            {
                IEventMonitoredItem monitoredItem = eventMonitoredItems[ii];

                if (e is AuditEventState)
                {
                    // check Server.Auditing flag and skip if false
                    if (!NodeManager.Server.Auditing)
                    {
                        continue;
                    }
                    else
                    {
                        // check if channel is not encrypted and skip if so
                        if (monitoredItem?.Session?.EndpointDescription?.SecurityMode != MessageSecurityMode.SignAndEncrypt &&
                            monitoredItem?.Session?.EndpointDescription?.TransportProfileUri != Profiles.HttpsBinaryTransport)
                        {
                            continue;
                        }
                    }
                }

                // validate if the monitored item has the required role permissions to receive the event
                ServiceResult validationResult = NodeManager.ValidateEventRolePermissions(monitoredItem, e);

                if (ServiceResult.IsBad(validationResult))
                {
                    // skip event reporting for EventType without permissions
                    continue;
                }

                lock (NodeManager.Lock)
                {
                    // enqueue event
                    if (context?.SessionId != null && monitoredItem?.Session?.Id?.Identifier != null)
                    {
                        if (monitoredItem.Session.Id.Identifier.Equals(context.SessionId.Identifier))
                        {
                            monitoredItem?.QueueEvent(e);
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        monitoredItem?.QueueEvent(e);
                    }
                }
            }
        }

        /// <summary>
        /// Called when the state of a Node changes.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="node">The affected node.</param>
        /// <param name="changes">The mask indicating what changes have occurred.</param>
        public void OnMonitoredNodeChanged(ISystemContext context, NodeState node, NodeStateChangeMasks changes)
        {
            lock (NodeManager.Lock)
            {
                if (DataChangeMonitoredItems == null)
                {
                    return;
                }

                for (int ii = 0; ii < DataChangeMonitoredItems.Count; ii++)
                {
                    IDataChangeMonitoredItem2 monitoredItem = DataChangeMonitoredItems[ii];

                    if (monitoredItem.AttributeId == Attributes.Value && (changes & NodeStateChangeMasks.Value) != 0)
                    {
                        // validate if the monitored item has the required role permissions to read the value
                        ServiceResult validationResult = NodeManager.ValidateRolePermissions(new OperationContext(monitoredItem), node.NodeId, PermissionType.Read);

                        if (ServiceResult.IsBad(validationResult))
                        {
                            // skip if the monitored item does not have permission to read
                            continue;
                        }

                        QueueValue(context, node, monitoredItem);
                        continue;
                    }

                    if (monitoredItem.AttributeId != Attributes.Value && (changes & NodeStateChangeMasks.NonValue) != 0)
                    {
                        QueueValue(context, node, monitoredItem);
                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// Reads the value of an attribute and reports it to the MonitoredItem.
        /// </summary>
        public void QueueValue(
            ISystemContext context,
            NodeState node,
            IDataChangeMonitoredItem2 monitoredItem)
        {
            var value = new DataValue();

            value.Value = null;
            value.ServerTimestamp = DateTime.UtcNow;
            value.SourceTimestamp = DateTime.MinValue;
            value.StatusCode = StatusCodes.Good;

            ISystemContext contextToUse = context;

            if (context is ServerSystemContext systemContext)
            {
                contextToUse = GetOrCreateContext(systemContext, monitoredItem);
            }

            ServiceResult error = node.ReadAttribute(
                contextToUse,
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

        /// <summary>
        /// Gets or creates a cached context for the monitored item.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <returns>The cached or newly created context.</returns>
        private ServerSystemContext GetOrCreateContext(ServerSystemContext context, IDataChangeMonitoredItem2 monitoredItem)
        {
            uint monitoredItemId = monitoredItem.Id;
            int currentTicks = HiResClock.TickCount;

            // Check if the context already exists in the cache
            if (m_contextCache.TryGetValue(monitoredItemId, out (ServerSystemContext Context, int CreatedAtTicks) cachedEntry))
            {
                // Check if the session or user identity has changed or the entry has expired
                if (cachedEntry.Context.OperationContext.Session != monitoredItem.Session
                    || cachedEntry.Context.OperationContext.UserIdentity != monitoredItem.EffectiveIdentity
                    || (currentTicks - cachedEntry.CreatedAtTicks) > m_cacheLifetimeTicks)
                {
                    ServerSystemContext updatedContext = context.Copy(new OperationContext(monitoredItem));
                    m_contextCache[monitoredItemId] = (updatedContext, currentTicks);

                    return updatedContext;
                }
                // return cached entry
                else
                {
                    return cachedEntry.Context;
                }
            }

            // Create a new context and add it to the cache
            ServerSystemContext newContext = context.Copy(new OperationContext(monitoredItem));
            m_contextCache.TryAdd(monitoredItemId, (newContext, currentTicks));

            return newContext;
        }

        private readonly ConcurrentDictionary<uint, (ServerSystemContext Context, int CreatedAtTicks)> m_contextCache = new();
        private readonly int m_cacheLifetimeTicks = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
    }
}
