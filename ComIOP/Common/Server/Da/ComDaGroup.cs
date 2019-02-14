/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Opc.Ua.Client;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// A class that implements a COM DA group.
    /// </summary>
    public class ComDaGroup : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComDaGroup"/> class.
        /// </summary>
        /// <param name="manager">The group manager.</param>
        /// <param name="name">The name.</param>
        /// <param name="serverHandle">The server handle.</param>
        public ComDaGroup(ComDaGroupManager manager, string name, int serverHandle)
		{
            m_manager = manager;
            m_name = name;
            m_serverHandle = serverHandle;
            m_clientHandle = 0;
            m_active = true;
            m_enabled = true;
            m_updateRate = 0;
            m_deadband = 0;
            m_timeBias = 0;
            m_itemsByHandle = new Dictionary<int,ComDaGroupItem>();
            m_items = new List<ComDaGroupItem>();
            m_requests = new List<ComDaAsnycRequest>();
            m_keepAliveTime = 0;
            m_updateTimer = null;
            m_itemsByMonitoredItem = new Dictionary<uint, ComDaGroupItem>();
        }
        #endregion

        #region IDisposable Members        
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            m_disposed = true;
 
            if (disposing)
            {
                lock (m_lock)
                {
                    if (m_updateTimer != null)
                    {
                        m_updateTimer.Dispose();
                        m_updateTimer = null;
                    }

                    // release the callback.
                    Utils.SilentDispose(m_callback);
                    m_callback = null;

                    // clear all outstanding requests.
                    m_requests.Clear();
                }
            }
        }

        /// <summary>
        /// Throws if disposed.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the handle.
        /// </summary>
        /// <value>The handle.</value>
        public IntPtr Handle
        {
            get { return m_handle; }
            set { m_handle = value; }
        }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// Gets the server handle.
        /// </summary>
        /// <value>The server handle.</value>
        public int ServerHandle
        {
            get { return m_serverHandle; }
        }

        /// <summary>
        /// Gets or sets the client handle.
        /// </summary>
        /// <value>The client handle.</value>
        public int ClientHandle
        {
            get { return m_clientHandle; }
            set { m_clientHandle = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="ComDaGroup"/> is active.
        /// </summary>
        /// <value><c>true</c> if active; otherwise, <c>false</c>.</value>
        public bool Active
        {
            get { return m_active; }
            set { m_active = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="ComDaGroup"/> is enabled.
        /// </summary>
        /// <value><c>true</c> if enabled; otherwise, <c>false</c>.</value>
        public bool Enabled
        {
            get { return m_enabled; }
            set { m_enabled = value; }
        }

        /// <summary>
        /// Gets or sets the update rate.
        /// </summary>
        /// <value>The update rate.</value>
        public int UpdateRate
        {
            get { return m_updateRate; }
            set { m_updateRate = value; }
        }

        /// <summary>
        /// Gets or sets the deadband.
        /// </summary>
        /// <value>The deadband.</value>
        public float Deadband
        {
            get { return m_deadband; }
            set { m_deadband = value; }
        }

        /// <summary>
        /// Gets or sets the timebias.
        /// </summary>
        /// <value>The timebias.</value>
        public int TimeBias
        {
            get { return m_timeBias; }
            set { m_timeBias = value; }
        }

        /// <summary>
        /// Gets or sets the lcid.
        /// </summary>
        /// <value>The lcid.</value>
        public int Lcid
        {
            get { return m_lcid; }
            set { m_lcid = value; }
        }

        /// <summary>
        /// Gets the keep alive time.
        /// </summary>
        /// <value>The keep alive time.</value>
        public int KeepAliveTime
        {
            get { return m_keepAliveTime; }
        }

        /// <summary>
        /// Gets or sets the actual update rate.
        /// </summary>
        /// <value>The actual update rate.</value>
        public int ActualUpdateRate
        {
            get { return m_actualUpdateRate; }
            set { m_actualUpdateRate = value; }
        }

        /// <summary>
        /// Gets or sets the subscription.
        /// </summary>
        /// <value>The subscription.</value>
        public Subscription Subscription
        {
            get { return m_subscription; }
            
            set 
            { 
                m_subscription = value;
                m_subscription.FastDataChangeCallback = OnDataChange;
            }
        }
        
        /// <summary>
        /// Gets the items.
        /// </summary>
        /// <value>The items.</value>
        public Dictionary<int,ComDaGroupItem> Items
        {
            get { return m_itemsByHandle; }
        }

        /// <summary>
        /// Sets the name.
        /// </summary>
        /// <param name="groupName">Name of the group.</param>
        public void SetName(string groupName)
        {
            TraceState("SetName");
            ThrowIfDisposed();

            m_manager.SetGroupName(this, groupName);
        }

        /// <summary>
        /// Removes this group from the server.
        /// </summary>
        public void Remove()
        {
            TraceState("Remove");
            ThrowIfDisposed();

            m_manager.RemoveGroup(this);
        }

        /// <summary>
        /// Clones the group.
        /// </summary>
        /// <param name="groupName">Name of the new group.</param>
        public ComDaGroup Clone(string groupName)
        {
            TraceState("Clone", this.m_name, groupName);
            ThrowIfDisposed();

            // create the new group.
            ComDaGroup group = m_manager.AddGroup(
                groupName,
                false,
                m_actualUpdateRate,
                m_clientHandle,
                m_timeBias,
                m_deadband,
                m_lcid);

            lock (m_lock)
            {
                // add the items.
                for (int ii = 0; ii < m_items.Count; ii++)
                {
                    ComDaGroupItem item = m_items[ii].CloneItem(group);
                    group.m_itemsByHandle.Add(item.ServerHandle, item);
                    group.m_items.Add(item);
                }
            }

            // re-create the items.
            group.RecreateItems();

            // return clone.
            return group;
        }

        /// <summary>
        /// Recreates the items.
        /// </summary>
        public void RecreateItems()
        {
            TraceState("RecreateItems", this.m_name);
            ThrowIfDisposed();

            // create the items on the server.
            lock (m_lock)
            {
                // add the items to the subscription.
                for (int ii = 0; ii < m_items.Count; ii++)
                {
                    ComDaGroupItem item = m_items[ii];

                    // check if the item has a different subscription.
                    if (!Object.ReferenceEquals(item.MonitoredItem.Subscription, m_subscription))
                    {
                        // clone the item if it was attached to discarded subscription.
                        if (item.MonitoredItem.Subscription != null)
                        {
                            m_itemsByMonitoredItem.Remove(item.MonitoredItem.ClientHandle);

                            item.MonitoredItem = new MonitoredItem(item.MonitoredItem);
                        }
                        AddItemToSubscription(item);
                    }
                }

                // update the server.
                try
                {
                    m_subscription.ApplyChanges();
                }
                catch (Exception)
                {
                    // TBD
                }
            }
        }

        /// <summary>
        /// Sets the active state for the group,
        /// </summary>
        /// <param name="active">if set to <c>true</c> the group is activated.</param>
        public void SetActive(bool active)
        {
            TraceState("SetActive", this.m_serverHandle, active);
            ThrowIfDisposed();

            lock (m_lock)
            {
                // do nothing if no change.
                if (m_active == active)
                {
                    CheckUpdateTimerStatus();
                    return;
                }

                bool areUpdatesRequired = AreUpdatesRequired;

                // turn publishing on.
                try
                {
                    m_subscription.SetPublishingMode(active);
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e, ResultIds.E_FAIL);
                }

                m_active = active;

                // get net monitoring mode.
                MonitoringMode monitoringMode = MonitoringMode.Disabled;

                if (m_active)
                {
                    monitoringMode = MonitoringMode.Reporting;
                }

                // validate the handles.
                int activeItemCount = 0;
                List<MonitoredItem> monitoredItems = new List<MonitoredItem>();

                for (int ii = 0; ii < m_items.Count; ii++)
                {
                    ComDaGroupItem item = m_items[ii];

                    // check if the item is deactivated.
                    if (!item.Active)
                    {
                        continue;
                    }

                    activeItemCount++;

                    // flag the last set value for resending.
                    if (monitoringMode == MonitoringMode.Reporting && item.CacheEntry != null)
                    {
                        item.CacheEntry.Changed = true;
                    }

                    // nothing to do if already in the correct state.
                    if (item.MonitoredItem.Status.MonitoringMode == monitoringMode)
                    {
                        continue;
                    }

                    monitoredItems.Add(item.MonitoredItem);
                }

                // update the subscription.
                if (monitoredItems.Count > 0)
                {
                    try
                    {
                        m_subscription.SetMonitoringMode(monitoringMode, monitoredItems);
                    }
                    catch (Exception)
                    {
                        // ignore errors updating individual items.
                    }
                }

                // resend the contents of the cache.
                if (!areUpdatesRequired && AreUpdatesRequired)
                {
                    int cancelId = 0;
                    Refresh(0, 0, true, out cancelId);
                }

                // update timer status.
                CheckUpdateTimerStatus();
            }
        }

        /// <summary>
        /// Sets the enabled state for the group.
        /// </summary>
        /// <param name="enabled">if set to <c>true</c> the group is enabled.</param>
        public void SetEnabled(bool enabled)
        {
            TraceState("SetEnabled", enabled);
            ThrowIfDisposed();

            lock (m_lock)
            {
                bool areUpdatesRequired = AreUpdatesRequired;

                m_enabled = enabled;

                // resend the contents of the cache.
                if (!areUpdatesRequired && AreUpdatesRequired)
                {
                    int cancelId = 0;
                    Refresh(0, 0, true, out cancelId);
                }

                CheckUpdateTimerStatus();
            }
        }

        /// <summary>
        /// Sets the callback.
        /// </summary>
        /// <param name="callback">The callback.</param>
        public void SetCallback(IComDaGroupCallback callback)
        {
            TraceState("SetCallback", callback != null);
            ThrowIfDisposed();

            lock (m_lock)
            {
                bool areUpdatesRequired = AreUpdatesRequired;

                if (m_callback != null)
                {
                    m_callback.Dispose();
                    m_callback = null;
                }

                m_callback = callback;

                // resend the contents of the cache.
                if (!areUpdatesRequired && AreUpdatesRequired)
                {
                    int cancelId = 0;
                    Refresh(0, 0, true, out cancelId);
                }

                CheckUpdateTimerStatus();
            }
        }

        /// <summary>
        /// Sets the keep alive time.
        /// </summary>
        /// <param name="keepAliveTime">The keep alive time.</param>
        public int SetKeepAliveTime(int keepAliveTime)
        {
            TraceState("SetKeepAliveTime", keepAliveTime);
            ThrowIfDisposed();

            lock (m_lock)
            {
                if (keepAliveTime != 0 && keepAliveTime < m_actualUpdateRate)
                {
                    keepAliveTime = m_actualUpdateRate;
                }

                return m_keepAliveTime = keepAliveTime;
            }
        }

        /// <summary>
        /// Sets the update rate for the group,
        /// </summary>
        /// <param name="updateRate">The update rate.</param>
        /// <returns>The revised update rate.</returns>
        public int SetUpdateRate(int updateRate)
        {
            TraceState("SetUpdateRate", updateRate);
            ThrowIfDisposed();

            lock (m_lock)
            {
                // upate the publishing interval.
                m_subscription.PublishingInterval = updateRate/2;

                // modify the subscription.
                try
                {
                    m_subscription.Modify();
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e, ResultIds.E_FAIL);
                }

                int actualUpdateRate = (int)m_subscription.CurrentPublishingInterval*2;

                // restart timer.
                if (actualUpdateRate != m_actualUpdateRate)
                {
                    m_actualUpdateRate = actualUpdateRate;

                    // adjust keep alive.
                    if (m_keepAliveTime != 0 && m_keepAliveTime < m_actualUpdateRate)
                    {
                        m_keepAliveTime = m_actualUpdateRate;
                    }
                    
                    ScheduleNextUpdate();

                    if (m_updateTimer != null)
                    {
                        m_updateTimer.Dispose();
                        m_updateTimer = null;
                    }

                    CheckUpdateTimerStatus();
                }

                // update the filter for analog items that have not overriden the deadband.
                for (int ii = 0; ii < m_items.Count; ii++)
                {
                    if (m_items[ii].SamplingRate == -1)
                    {
                        m_items[ii].MonitoredItem.SamplingInterval = updateRate/2;
                    }
                }

                // update the items on the server.
                try
                {
                    m_subscription.ApplyChanges();
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e, ResultIds.E_FAIL);
                }

                // return the revised update rate.
                return m_actualUpdateRate;
            }
        }

        /// <summary>
        /// Sets the deadband for the group,
        /// </summary>
        /// <param name="deadband">The deadband.</param>
        public void SetDeadband(float deadband)
        {
            TraceState("SetDeadband", deadband);
            ThrowIfDisposed();

            lock (m_lock)
            {
                this.Deadband = deadband;

                // need the EU Info if the deadband is specified.
                m_manager.UpdateItemEuInfo(this, m_items);

                // update filters for all items.
                UpdateDeadbandFilters(m_items);

                // update the items on the server.
                try
                {
                    m_subscription.ApplyChanges();
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e, ResultIds.E_FAIL);
                }
            }
        }

        /// <summary>
        /// Creates the items.
        /// </summary>
        /// <param name="requests">The requests.</param>
        /// <param name="validateOnly">if set to <c>true</c> if the items only need to be validated.</param>
        public void CreateItems(ComDaCreateItemRequest[] requests, bool validateOnly)
        {
            TraceState("CreateItems", this.Name, this.Active, this.UpdateRate);
            ThrowIfDisposed();

            // validates the items.
            ComDaGroupItem[] items = m_manager.ValidateItems(this, requests);

            // check if nothing more to do.
            if (validateOnly)
            {
                return;
            }

            if (this.Deadband > 0)
            {
                // need the EU Info if the deadband is specified.
                m_manager.UpdateItemEuInfo(this, items);
            }

            // create the items on the server.
            lock (m_lock)
            {
                // update the filters.
                UpdateDeadbandFilters(items);

                // add the monitored items to the group's subscription.
                for (int ii = 0; ii < items.Length; ii++)
                {
                    ComDaGroupItem item = items[ii];

                    if (item != null)
                    {
                        AddItemToSubscription(item);
                    }
                }

                // update the server.
                try
                {
                    m_subscription.ApplyChanges();
                }
                catch (Exception e)
                {
                    // set fatal error for remaining items.
                    for (int ii = 0; ii < items.Length; ii++)
                    {
                        ComDaGroupItem item = items[ii];

                        if (items[ii] != null)
                        {
                            requests[ii].Error = ComUtils.GetErrorCode(e, ResultIds.E_FAIL);
                        }
                    }

                    return;
                }

                // build results.
                for (int ii = 0; ii < items.Length; ii++)
                {
                    // check for valid item.
                    ComDaGroupItem item = items[ii];

                    if (item == null)
                    {
                        continue;
                    }

                    // check for error.
                    ServiceResult error = item.MonitoredItem.Status.Error;

                    if (ServiceResult.IsBad(error))
                    {
                        RemoveItemFromSubscription(item);
                        requests[ii].Error = ComDaProxy.MapReadStatusToErrorCode(error.StatusCode);
                        continue;
                    }

                    TraceState(
                        "ItemCreated",
                        m_subscription.CurrentPublishingEnabled, 
                        m_subscription.CurrentPublishingInterval, 
                        item.MonitoredItem.Status.MonitoringMode, 
                        item.MonitoredItem.Status.SamplingInterval);

                    // save the item.
                    m_itemsByHandle.Add(item.ServerHandle, item);
                    m_items.Add(item);
                }
                
                // start the update timer.
                CheckUpdateTimerStatus();
            }
        }

        /// <summary>
        /// Deletes the items.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <returns>Any errors.</returns>
        public int[] DeleteItems(int[] serverHandles)
        {
            TraceState("DeleteItems", serverHandles.Length);
            ThrowIfDisposed();

            lock (m_lock)
            {
                // validate the handles.
                int[] results = new int[serverHandles.Length];
                ComDaGroupItem[] items = new ComDaGroupItem[serverHandles.Length];

                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        results[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    items[ii] = item;
                    RemoveItemFromSubscription(item);
                    m_itemsByHandle.Remove(serverHandles[ii]);
                    m_items.Remove(item);
                }

                // update the subscription.
                try
                {
                    m_subscription.ApplyChanges();
                }
                catch (Exception e)
                {
                    SetFatalError(items, results, e, ResultIds.E_FAIL);
                }

                // start the update timer.
                CheckUpdateTimerStatus();

                // return any errors.
                return results;
            }
        }

        /// <summary>
        /// Sets the active state for the items.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="active">if set to <c>true</c> the items are activated.</param>
        /// <returns>Any error codes.</returns>
        public int[] SetActive(int[] serverHandles, bool active)
        {
            TraceState("SetActive", serverHandles.Length, active);
            ThrowIfDisposed();

            lock (m_lock)
            {
                // get net monitoring mode.
                MonitoringMode monitoringMode = MonitoringMode.Disabled;

                if (m_active && active)
                {
                    monitoringMode = MonitoringMode.Reporting;
                }

                // validate the handles.
                int[] results = new int[serverHandles.Length];
                ComDaGroupItem[] items = new ComDaGroupItem[serverHandles.Length];
                List<MonitoredItem> monitoredItems = new List<MonitoredItem>();

                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        results[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    // nothing to do if already in the correct state.
                    if (item.MonitoredItem.Status.MonitoringMode == monitoringMode)
                    {
                        item.Active = active;
                        results[ii] = ResultIds.S_OK;
                        continue;
                    }

                    items[ii] = item;
                    monitoredItems.Add(item.MonitoredItem);
                }

                // update the subscription.
                try
                {
                    m_subscription.SetMonitoringMode(monitoringMode, monitoredItems);
                }
                catch (Exception e)
                {
                    SetFatalError(items, results, e, ResultIds.E_FAIL);
                }

                // update active state on success
                for (int ii = 0; ii < items.Length; ii++)
                {
                    if (items[ii] != null)
                    {
                        // clear the last sent value when deactivated.
                        // this prevents the change filter from suppressing
                        // updates if the item is reactivated and the value
                        // has not changed.
                        if (items[ii].Active && !active)
                        {
                            items[ii].LastSentValue = null;
                        }

                        items[ii].Active = active;
                    }
                }

                // return any errors.
                return results;
            }
        }

        /// <summary>
        /// Sets the client handles.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="clientHandles">The client handles.</param>
        /// <returns>Any errors.</returns>
        public int[] SetClientHandles(int[] serverHandles, int[] clientHandles)
        {
            TraceState("SetClientHandles", serverHandles.Length);
            ThrowIfDisposed();

            lock (m_lock)
            {
                int[] results = new int[serverHandles.Length];
                ComDaGroupItem[] items = new ComDaGroupItem[serverHandles.Length];

                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        results[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    item.ClientHandle = clientHandles[ii];
                    results[ii] = ResultIds.S_OK;
                }

                return results;
            }
        }

        /// <summary>
        /// Sets the data types.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="dataTypes">The data types.</param>
        /// <returns>Any errors.</returns>
        public int[] SetDataTypes(int[] serverHandles, short[] dataTypes)
        {
            TraceState("SetDataTypes", serverHandles.Length);
            ThrowIfDisposed();

            lock (m_lock)
            {
                int[] results = new int[serverHandles.Length];
                ComDaGroupItem[] items = new ComDaGroupItem[serverHandles.Length];

                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        results[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    // validate the datatype.
                    if (dataTypes[ii] != 0)
                    {
                        NodeId dataTypeId = ComUtils.GetDataTypeId(dataTypes[ii]);

                        if (NodeId.IsNull(dataTypeId))
                        {
                            results[ii] = ResultIds.E_BADTYPE;
                            continue;
                        }

                        bool reqTypeIsArray = (dataTypes[ii] & (short)VarEnum.VT_ARRAY) != 0;
                        bool actualTypeIsArray = (item.CanonicalDataType & (short)VarEnum.VT_ARRAY) != 0;

                        if (reqTypeIsArray != actualTypeIsArray)
                        {
                            results[ii] = ResultIds.E_BADTYPE;
                            continue;
                        }
                    }

                    item.RequestedDataType = dataTypes[ii];
                    results[ii] = ResultIds.S_OK;
                }

                return results;
            }
        }

        /// <summary>
        /// Gets the item attributes.
        /// </summary>
        /// <returns>The item attributes.</returns>
        public OpcRcw.Da.OPCITEMATTRIBUTES[] GetItemAttributes()
        {
            TraceState("GetItemAttributes", this.m_name);
            ThrowIfDisposed();
            
            lock (m_lock)
            {
                m_manager.UpdateItemEuInfo(this, m_items);

                OpcRcw.Da.OPCITEMATTRIBUTES[] attributes = new OpcRcw.Da.OPCITEMATTRIBUTES[m_items.Count];

                for (int ii = 0; ii < m_items.Count; ii++)
                {
                    ComDaGroupItem item = m_items[ii];

                    attributes[ii].szItemID = item.ItemId;
                    attributes[ii].szAccessPath = String.Empty;
                    attributes[ii].hServer = item.ServerHandle;
                    attributes[ii].hClient = item.ClientHandle;
                    attributes[ii].bActive = (item.Active)?1:0;
                    attributes[ii].vtRequestedDataType = item.RequestedDataType;
                    attributes[ii].vtCanonicalDataType = item.CanonicalDataType;
                    attributes[ii].dwAccessRights = item.AccessRights;
                    attributes[ii].dwBlobSize = 0;
                    attributes[ii].pBlob = IntPtr.Zero;
                    attributes[ii].dwEUType = (OpcRcw.Da.OPCEUTYPE)item.EuType;
                    attributes[ii].vEUInfo = item.EuInfo;
                }

                return attributes;
            }
        }

        /// <summary>
        /// Performs n synchronous read operation.
        /// </summary>
        /// <param name="maxAge">The max age.</param>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="isInitialRefresh">if set to <c>true</c> the read is being done as part of the initial refresh for active items.</param>
        /// <param name="clientHandles">The client handles (must be allocated by the caller).</param>
        /// <returns>Any errors.</returns>
        public DaValue[] SyncRead(uint maxAge, int[] serverHandles, bool isInitialRefresh, int[] clientHandles)
        {
            TraceState("SyncRead", maxAge, serverHandles.Length);
            ThrowIfDisposed();

            DaValue[] results = new DaValue[serverHandles.Length];
            List<ComDaGroupItem> items = new List<ComDaGroupItem>(serverHandles.Length);
            ReadValueIdCollection valuesToRead = new ReadValueIdCollection();

            lock (m_lock)
            {
                // validate items.
                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    results[ii] = new DaValue();

                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        results[ii].Error = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    if (clientHandles != null)
                    {
                        clientHandles[ii] = item.ClientHandle;
                    }

                    // check for cache read.
                    if (maxAge == UInt32.MaxValue)
                    {
                        // inactive items cannot be read from cache.
                        if (!item.Active || !m_active)
                        {
                            results[ii].Quality = OpcRcw.Da.Qualities.OPC_QUALITY_OUT_OF_SERVICE;
                            results[ii].Timestamp = DateTime.UtcNow;
                            continue;
                        }
                      
                        // check if waiting for initial data.
                        if (item.CacheEntry == null)
                        {
                            results[ii].Quality = OpcRcw.Da.Qualities.OPC_QUALITY_WAITING_FOR_INITIAL_DATA;
                            results[ii].Timestamp = DateTime.UtcNow;
                            continue;
                        }

                        // get the latest value from the cache.
                        item.CacheEntry.GetLatest(results[ii]);
                        UpdateReadResult(item, results[ii]);
                        continue;
                    }

                    // apply max age.
                    if (maxAge > 0)
                    {
                        if (item.CacheEntry != null)
                        {
                            if (item.CacheEntry.CacheTimestamp.AddMilliseconds(maxAge) > DateTime.UtcNow)
                            {
                                item.CacheEntry.GetLatest(results[ii]);
                                UpdateReadResult(item, results[ii]);
                                continue;
                            }
                        }
                    }

                    // schedule read from device.
                    ReadValueId valueToRead = new ReadValueId();

                    valueToRead.NodeId = item.NodeId;
                    valueToRead.AttributeId = Attributes.Value;
                    valueToRead.Handle = ii;

                    valuesToRead.Add(valueToRead);
                    items.Add(item);
                }
            }

            if (valuesToRead.Count  == 0)
            {
                return results;
            }

            // read the values from the server.
            DaValue[] remoteResults = m_manager.Read(valuesToRead);
            
            // copy results.
            for (int ii = 0; ii < valuesToRead.Count; ii++)
            {
                int index = (int)valuesToRead[ii].Handle;

                if (isInitialRefresh)
                {
                    UpdateCache(items[ii], remoteResults[ii], isInitialRefresh);
                }

                UpdateReadResult(items[ii], remoteResults[ii]);
                results[(int)valuesToRead[ii].Handle] = remoteResults[ii];
            }

            return results;
        }

        /// <summary>
        /// Updates the read result by converting the value to the requested data type.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="value">The value.</param>
        private void UpdateReadResult(ComDaGroupItem item, DaValue value)
        {
            if (value.Value == null || item.RequestedDataType == (short)VarEnum.VT_EMPTY)
            {
                return;
            }

            object convertedValue = null;
            
            int error = ComUtils.ChangeTypeForCOM(value.Value, (VarEnum)item.RequestedDataType, out convertedValue);

            if (error < 0)
            {
                value.Value = null;
                value.Error = error;
                return;
            }

            value.Value = convertedValue;
        }

        /// <summary>
        /// Updates the cache.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="value">The value.</param>
        /// <param name="isInitialRefresh">if set to <c>true</c> the change flag is set to false because the values are sent in the refresh.</param>
        private void UpdateCache(ComDaGroupItem item, DaValue value, bool isInitialRefresh)
        {
            lock (m_lock)
            {
                
                // get sampling rate.
                long now = HiResClock.UtcNow.Ticks;
                int samplingRate = item.ActualSamplingRate;

                if (item.SamplingRate == -1)
                {
                    samplingRate = m_actualUpdateRate;
                }

                // check existing cache contents.
                DaValue oldValue = null;
                DaCacheValue entry = null;

                if (item.CacheEntry != null)
                {
                    // do not update cache if a newer value exists.
                    if (item.CacheEntry.Timestamp >= value.Timestamp)
                    {
                        /*
                        TraceState(
                            "UpdateCache OLD VALUE RECEIVED", 
                            this.m_serverHandle, 
                            item.ServerHandle, 
                            new Variant(item.CacheEntry.Value),
                            item.CacheEntry.Timestamp.ToString("HH:mm:ss.fff"),
                            new Variant(value.Value),
                            value.Timestamp.ToString("HH:mm:ss.fff"));
                        */

                        return;
                    }

                    oldValue = item.CacheEntry;

                    // replace the newest value if sampling interval has not elasped.

                    if (!item.BufferEnabled || item.NextUpdateTime > now)
                    {
                        // TraceState("UpdateCache ENTRY REPLACED", this.m_serverHandle, item.ServerHandle, samplingRate);
                        entry = item.CacheEntry;
                    }
                }

                // create a new cache entry.
                if (entry == null)
                {
                    entry = new DaCacheValue();
                    entry.CacheTimestamp =  DateTime.UtcNow;

                    if (item.BufferEnabled)
                    {
                        entry.NextEntry = item.CacheEntry;
                        item.NextUpdateTime += samplingRate*TimeSpan.TicksPerMillisecond;

                        if (entry.NextEntry != null)
                        {
                            // TraceState("UpdateCache ENTRY BUFFERED", this.m_serverHandle, item.ServerHandle, samplingRate);
                        }
                    }

                    item.CacheEntry = entry;
                }

                // check if the value has changed.
                bool changed = !isInitialRefresh;

                if (oldValue != null)
                {
                    if (oldValue.Error == value.Error)
                    {
                        if (oldValue.Quality == value.Quality)
                        {
                            if (Utils.IsEqual(oldValue.Value, value.Value))
                            {
                                changed = false;
                            }
                        }
                    }
                }

                // save values.
                item.CacheEntry.Value = value.Value;
                item.CacheEntry.Quality = value.Quality;
                item.CacheEntry.Timestamp = value.Timestamp;
                item.CacheEntry.Error = value.Error;
                item.CacheEntry.Changed = changed;

                TraceState(
                    "UpdateCache COMPLETE",
                    this.m_serverHandle,
                    item.ServerHandle,
                    item.ClientHandle,
                    new Variant(value.Value),
                    value.Timestamp.ToString("HH:mm:ss.fff"),
                    item.CacheEntry.Changed);
            }
        }

        /// <summary>
        /// Starts an asynchronous read operation.
        /// </summary>
        /// <param name="maxAge">The max age.</param>
        /// <param name="transactionId">The transaction id.</param>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="cancelId">The cancel id.</param>
        /// <returns>Any errors.</returns>
        public int[] AsyncRead(uint maxAge, int transactionId, int[] serverHandles, out int cancelId)
        {
            TraceState("AsyncRead", maxAge, transactionId, serverHandles.Length);
            ThrowIfDisposed();

            cancelId = 0;

            lock (m_lock)
            {
                int[] results = new int[serverHandles.Length];
                List<ComDaGroupItem> items = new List<ComDaGroupItem>(serverHandles.Length);

                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        results[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    items.Add(item);
                }

                if (items.Count > 0)
                {
                    ComDaAsnycReadRequest request = new ComDaAsnycReadRequest();

                    request.CancelId = ++m_transactionCounter;
                    request.TransactionId = transactionId;
                    request.MaxAge = maxAge;
                    request.ServerHandles = new int[items.Count];
                    request.ClientHandles = new int[items.Count];

                    for (int ii = 0; ii < items.Count; ii++)
                    {
                        request.ServerHandles[ii] = items[ii].ServerHandle;
                        request.ClientHandles[ii] = items[ii].ClientHandle;
                    }

                    m_requests.Add(request);
                    cancelId = request.CancelId;

                    // create a thread to process the request.
                    Thread thread = new Thread(OnAsyncRead);
                    thread.IsBackground = true;
                    thread.Start(request);
                }

                return results;
            }
        }

        /// <summary>
        /// Performs n synchronous write operation.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="values">The values.</param>
        /// <returns>Any errors.</returns>
        public int[] SyncWrite(int[] serverHandles, DaValue[] values)
        {
            TraceState("SyncWrite", serverHandles.Length);
            ThrowIfDisposed();

            int[] results = new int[serverHandles.Length];
            WriteValueCollection valuesToWrite = new WriteValueCollection();

            lock (m_lock)
            {
                // validate items.
                DaValue convertedValue = new DaValue();

                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        results[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    // apply the COM type conversion.
                    DaValue requestedValue = values[ii];

                    if (requestedValue.Value == null)
                    {
                        results[ii] = ResultIds.E_BADTYPE;
                        continue;
                    }

                    if (item.CanonicalDataType != (short)VarEnum.VT_EMPTY)
                    {
                        object value = null;

                        int error = ComUtils.ChangeTypeForCOM(requestedValue.Value, (VarEnum)item.CanonicalDataType, out value);

                        if (error < 0)
                        {
                            results[ii] = error;
                            continue;
                        }

                        // could happen if there is a problem reading the datatype from the server.
                        if (requestedValue.Value == null)
                        {
                            results[ii] = ResultIds.E_BADTYPE;
                            continue;
                        }

                        // copy all of the attributes into the converted value.
                        convertedValue.Value = value;
                        convertedValue.Quality = requestedValue.Quality;
                        convertedValue.Timestamp = requestedValue.Timestamp;
                        convertedValue.Error = requestedValue.Error;

                        requestedValue = convertedValue;
                    }

                    WriteValue valueToWrite = new WriteValue();

                    valueToWrite.NodeId = item.NodeId;
                    valueToWrite.AttributeId = Attributes.Value;
                    valueToWrite.Handle = ii;

                    // convert value to UA data type.
                    try
                    {
                        valueToWrite.Value = m_manager.Mapper.GetRemoteDataValue(requestedValue, item.RemoteDataType);
                    }
                    catch (Exception e)
                    {
                        results[ii] = ComUtils.GetErrorCode(e, ResultIds.E_BADTYPE);
                        continue;
                    }

                    valuesToWrite.Add(valueToWrite);
                }
            }

            // check if nothing to do.
            if (valuesToWrite.Count  == 0)
            {
                return results;
            }

            // write the values to the server.
            int[] remoteResults = m_manager.Write(valuesToWrite);

            // copy results.
            for (int ii = 0; ii < valuesToWrite.Count; ii++)
            {
                results[(int)valuesToWrite[ii].Handle] = remoteResults[ii];
            }

            return results;
        }

        /// <summary>
        /// Starts an asynchronous write operation.
        /// </summary>
        /// <param name="transactionId">The transaction id.</param>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="values">The values.</param>
        /// <param name="cancelId">The cancel id.</param>
        /// <returns>Any errors.</returns>
        public int[] AsyncWrite(int transactionId, int[] serverHandles, DaValue[] values, out int cancelId)
        {
            TraceState("AsyncWrite", transactionId, serverHandles.Length);
            ThrowIfDisposed();

            cancelId = 0;

            lock (m_lock)
            {
                int[] results = new int[serverHandles.Length];
                List<ComDaGroupItem> items = new List<ComDaGroupItem>(serverHandles.Length);
                List<DaValue> valuesToWrite = new List<DaValue>(serverHandles.Length);

                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        results[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    items.Add(item);
                    valuesToWrite.Add(values[ii]);
                }

                if (items.Count > 0)
                {
                    ComDaAsnycWriteRequest request = new ComDaAsnycWriteRequest();

                    request.CancelId = ++m_transactionCounter;
                    request.TransactionId = transactionId;
                    request.ServerHandles = new int[items.Count];
                    request.ClientHandles = new int[items.Count];
                    request.Values = valuesToWrite.ToArray();

                    for (int ii = 0; ii < items.Count; ii++)
                    {
                        request.ServerHandles[ii] = items[ii].ServerHandle;
                        request.ClientHandles[ii] = items[ii].ClientHandle;
                    }

                    m_requests.Add(request);
                    cancelId = request.CancelId;

                    TraceState("AsyncWrite Queued", transactionId, request.CancelId);

                    // create a thread to process the request.
                    Thread thread = new Thread(OnAsyncWrite);
                    thread.IsBackground = true;
                    thread.Start(request);
                }

                return results;
            }
        }

        /// <summary>
        /// Starts an asynchronous refresh operation.
        /// </summary>
        /// <param name="maxAge">The max age.</param>
        /// <param name="transactionId">The transaction id.</param>
        /// <param name="cancelId">The cancel id.</param>
        public void Refresh(uint maxAge, int transactionId, out int cancelId)
        {
            Refresh(maxAge, transactionId, false, out cancelId);
        }

        /// <summary>
        /// Starts an asynchronous refresh operation.
        /// </summary>
        /// <param name="maxAge">The max age.</param>
        /// <param name="transactionId">The transaction id.</param>
        /// <param name="isFirstUpdate">True if this refresh is the first update after activation.</param>
        /// <param name="cancelId">The cancel id.</param>
        private void Refresh(uint maxAge, int transactionId, bool isFirstUpdate, out int cancelId)
        {
            TraceState("Refresh", maxAge);
            ThrowIfDisposed();

            cancelId = 0;

            lock (m_lock)
            {
                // no refresh in inactive groups.
                if (!m_active)
                {
                    throw ComUtils.CreateComException(ResultIds.E_FAIL);
                }

                // collect active items.
                List<ComDaGroupItem> items = new List<ComDaGroupItem>(m_items.Count);

                for (int ii = 0; ii < m_items.Count; ii++)
                {
                    ComDaGroupItem item = m_items[ii];

                    if (item.Active)
                    {
                        items.Add(item);
                    }
                }

                // no refresh if no active items.
                if (items.Count == 0)
                {
                    throw ComUtils.CreateComException(ResultIds.E_FAIL);
                }

                // create the request.
                ComDaAsnycReadRequest request = new ComDaAsnycReadRequest();

                request.CancelId = ++m_transactionCounter;
                request.TransactionId = transactionId;
                request.IsFirstUpdate = isFirstUpdate;
                request.MaxAge = maxAge;
                request.IsRefresh = true;
                request.ServerHandles = new int[items.Count];
                request.ClientHandles = new int[items.Count];

                for (int ii = 0; ii < items.Count; ii++)
                {
                    request.ServerHandles[ii] = items[ii].ServerHandle;
                    request.ClientHandles[ii] = items[ii].ClientHandle;
                }

                m_requests.Add(request);
                cancelId = request.CancelId;

                // create a thread to process the request.
                Thread thread = new Thread(OnAsyncRead);
                thread.IsBackground = true;
                thread.Start(request);
            }
        }

        /// <summary>
        /// Cancels an asynchronous operation.
        /// </summary>
        /// <param name="cancelId">The cancel id.</param>
        public bool Cancel(int cancelId)
        {
            TraceState("Cancel", cancelId);
            ThrowIfDisposed();

            lock (m_lock)
            {
                for (int ii = 0; ii < m_requests.Count; ii++)
                {
                    if (m_requests[ii].CancelId == cancelId)
                    {
                        if (!m_requests[ii].Cancelled)
                        {
                            m_requests[ii].Cancelled = true;
                            return true;
                        }

                        break;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Sets the deadband for the items.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="deadbands">The deadbands.</param>
        /// <returns>Any errors.</returns>
        public int[] SetItemDeadbands(int[] serverHandles, float[] deadbands)
        {
            TraceState("SetItemDeadbands", serverHandles.Length);
            ThrowIfDisposed();

            int[] errors = new int[serverHandles.Length];
            ComDaGroupItem[] items = new ComDaGroupItem[serverHandles.Length];
            int count = 0;

            lock (m_lock)
            {
                // update the filter for analog items that have not overriden the deadband.
                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        errors[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    if (item.EuType >= 0 && item.EuType != (int)OpcRcw.Da.OPCEUTYPE.OPC_ANALOG)
                    {
                        errors[ii] = ResultIds.E_DEADBANDNOTSUPPORTED;
                        continue;
                    }

                    if (deadbands[ii] < 0 || deadbands[ii] > 100)
                    {
                        errors[ii] = ResultIds.E_INVALIDARG;
                        continue;
                    }

                    item.Deadband = deadbands[ii];
                    items[ii] = item;
                    count++;
                }
            }

            if (count == 0)
            {
                return errors;
            }

            // need the EU Info if the deadband is specified.
            m_manager.UpdateItemEuInfo(this, items);

            lock (m_lock)
            {
                // need to check the EU type after updating the EU info.
                for (int ii = 0; ii < items.Length; ii++)
                {
                    ComDaGroupItem item = items[ii];

                    if (item != null && item.EuType != (int)OpcRcw.Da.OPCEUTYPE.OPC_ANALOG)
                    {
                        errors[ii] = ResultIds.E_DEADBANDNOTSUPPORTED;
                        items[ii] = null;
                        continue;
                    }
                }

                // update filters for selected items.
                UpdateDeadbandFilters(items);

                // update the items on the server.
                try
                {
                    m_subscription.ApplyChanges();
                }
                catch (Exception e)
                {
                    int error = ComUtils.GetErrorCode(e, ResultIds.E_FAIL);

                    for (int ii = 0; ii < items.Length; ii++)
                    {
                        if (items[ii] != null)
                        {
                            errors[ii] = error;
                        }
                    }

                    return errors;
                }

                // check results for monitored items.
                for (int ii = 0; ii < items.Length; ii++)
                {
                    ComDaGroupItem item = items[ii];

                    if (item == null)
                    {
                        continue;
                    }

                    if (ServiceResult.IsBad(item.MonitoredItem.Status.Error))
                    {
                        item.MonitoredItem.Filter = item.MonitoredItem.Status.Filter;
                        DataChangeFilter filter = item.MonitoredItem.Filter as DataChangeFilter;

                        if (filter != null)
                        {
                            item.Deadband = (float)filter.DeadbandValue;
                        }

                        errors[ii] = ResultIds.E_FAIL;
                        continue;
                    }
                }

                return errors;
            }
        }

        /// <summary>
        /// Gets the deadband for the items.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="deadbands">The deadbands.</param>
        /// <returns>Any errors.</returns>
        public int[] GetItemDeadbands(int[] serverHandles, float[] deadbands)
        {
            TraceState("GetItemDeadbands", serverHandles.Length);
            ThrowIfDisposed();

            int[] errors = new int[serverHandles.Length];
            ComDaGroupItem[] items = new ComDaGroupItem[serverHandles.Length];
            int count = 0;

            lock (m_lock)
            {
                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        errors[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    if (item.EuType >= 0 && item.EuType != (int)OpcRcw.Da.OPCEUTYPE.OPC_ANALOG)
                    {
                        errors[ii] = ResultIds.E_DEADBANDNOTSUPPORTED;
                        continue;
                    }

                    if (item.Deadband == -1)
                    {
                        errors[ii] = ResultIds.E_DEADBANDNOTSET;
                        continue;
                    }
                    
                    deadbands[ii] = item.Deadband;

                    item.Deadband = deadbands[ii];
                    items[ii] = item;
                    count++;
                }
            }

            if (count == 0)
            {
                return errors;
            }

            // need the EU Info if the deadband is specified.
            m_manager.UpdateItemEuInfo(this, items);

            lock (m_lock)
            {
                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = items[ii];

                    if (item != null)
                    {
                        if (item.EuType != (int)OpcRcw.Da.OPCEUTYPE.OPC_ANALOG)
                        {
                            errors[ii] = ResultIds.E_DEADBANDNOTSUPPORTED;
                            continue;
                        }

                        deadbands[ii] = item.Deadband;
                    }
                }
            }

            return errors;
        }

        /// <summary>
        /// Clears the deadbands for the items.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <returns>Any errors.</returns>
        public int[] ClearItemDeadbands(int[] serverHandles)
        {
            TraceState("ClearItemSamplingRates", serverHandles.Length);
            ThrowIfDisposed();

            int[] errors = new int[serverHandles.Length];
            ComDaGroupItem[] items = new ComDaGroupItem[serverHandles.Length];
            int count = 0;

            lock (m_lock)
            {
                // update the filter for analog items that have not overriden the deadband.
                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        errors[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    if (item.EuType >= 0 && item.EuType != (int)OpcRcw.Da.OPCEUTYPE.OPC_ANALOG)
                    {
                        errors[ii] = ResultIds.E_DEADBANDNOTSUPPORTED;
                        continue;
                    }

                    if (item.Deadband == -1)
                    {
                        errors[ii] = ResultIds.E_DEADBANDNOTSET;
                        continue;
                    }

                    items[ii] = item;
                    item.Deadband = -1;
                    count++;
                }
            }

            if (count == 0)
            {
                return errors;
            }

            // need the EU Info if the deadband is specified.
            m_manager.UpdateItemEuInfo(this, items);
            
            lock (m_lock)
            {
                // need to check the EU type after updating the EU info.
                for (int ii = 0; ii < items.Length; ii++)
                {
                    ComDaGroupItem item = items[ii];

                    if (item != null && item.EuType != (int)OpcRcw.Da.OPCEUTYPE.OPC_ANALOG)
                    {
                        errors[ii] = ResultIds.E_DEADBANDNOTSUPPORTED;
                        items[ii] = null;
                        continue;
                    }
                }

                UpdateDeadbandFilters(items);

                // update the items on the server.
                try
                {
                    m_subscription.ApplyChanges();
                }
                catch (Exception e)
                {
                    int error = ComUtils.GetErrorCode(e, ResultIds.E_FAIL);

                    for (int ii = 0; ii < items.Length; ii++)
                    {
                        if (items[ii] != null)
                        {
                            errors[ii] = error;
                        }
                    }

                    return errors;
                }

                return errors;
            }
        }

        /// <summary>
        /// Sets the sampling rate for the items.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="samplingRates">The sampling rates.</param>
        /// <param name="revisedSamplingRates">The revised sampling rates.</param>
        /// <returns>Any errors.</returns>
        public int[] SetItemSamplingRates(int[] serverHandles, int[] samplingRates, int[] revisedSamplingRates)
        {
            TraceState("SetItemSamplingRates", serverHandles.Length);
            ThrowIfDisposed();

            int[] errors = new int[serverHandles.Length];
            ComDaGroupItem[] items = new ComDaGroupItem[serverHandles.Length];

            lock (m_lock)
            {
                // update the filter for analog items that have not overriden the deadband.
                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        errors[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    item.MonitoredItem.SamplingInterval = (int)(samplingRates[ii]/2);
                    revisedSamplingRates[ii] = 0;
                    items[ii] = item;
                }

                // update the items on the server.
                try
                {
                    m_subscription.ApplyChanges();
                }
                catch (Exception e)
                {
                    int error = ComUtils.GetErrorCode(e, ResultIds.E_FAIL);

                    for (int ii = 0; ii < items.Length; ii++)
                    {
                        if (items[ii] != null)
                        {
                            errors[ii] = error;
                        }
                    }

                    return errors;
                }

                // check results for monitored items.
                for (int ii = 0; ii < items.Length; ii++)
                {
                    ComDaGroupItem item = items[ii];
                    
                    if (item == null)
                    {
                        continue;                    
                    }

                    if (ServiceResult.IsBad(item.MonitoredItem.Status.Error))
                    {
                        errors[ii] = ResultIds.E_FAIL;
                        continue;
                    }
                    
                    item.SamplingRate = samplingRates[ii];
                    item.ActualSamplingRate = (int)(items[ii].MonitoredItem.Status.SamplingInterval);

                    // if 1/2 the sampling rate is accepted then it is possible to guarantee the sampling rate.
                    if (item.ActualSamplingRate < item.SamplingRate)
                    {
                        item.ActualSamplingRate =  item.SamplingRate;
                    }

                    // can only guarantee a sampling rate of twice what the UA server can provide.
                    else
                    {
                        item.ActualSamplingRate *= 2;
                    }

                    revisedSamplingRates[ii] = item.ActualSamplingRate;
                    item.NextUpdateTime = HiResClock.UtcNow.Ticks + item.ActualSamplingRate*TimeSpan.TicksPerMillisecond;

                    if (revisedSamplingRates[ii] != samplingRates[ii])
                    {
                        errors[ii] = ResultIds.S_UNSUPPORTEDRATE;
                    }

                    TraceState(
                        "ItemSamplingUpdated",
                        this.ServerHandle,
                        item.ServerHandle,
                        item.ClientHandle,
                        m_subscription.CurrentPublishingEnabled,
                        m_subscription.CurrentPublishingInterval,
                        item.SamplingRate,
                        item.MonitoredItem.Status.SamplingInterval);
                }

                return errors;
            }
        }

        /// <summary>
        /// Gets the sampling rates for the items.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="samplingRates">The sampling rates.</param>
        /// <returns>Any errors.</returns>
        public int[] GetItemSamplingRates(int[] serverHandles, int[] samplingRates)
        {
            TraceState("GetItemSamplingRates", serverHandles.Length);
            ThrowIfDisposed();

            lock (m_lock)
            {
                int[] errors = new int[serverHandles.Length];

                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        errors[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    if (item.SamplingRate == -1)
                    {
                        errors[ii] = ResultIds.E_RATENOTSET;
                        continue;
                    }

                    samplingRates[ii] = item.ActualSamplingRate;
                }

                return errors;
            }
        }

        /// <summary>
        /// Clears the sampling rates for the items.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <returns>Any errors.</returns>
        public int[] ClearItemSamplingRates(int[] serverHandles)
        {
            TraceState("ClearItemSamplingRates", serverHandles.Length);
            ThrowIfDisposed();

            int[] errors = new int[serverHandles.Length];
            ComDaGroupItem[] items = new ComDaGroupItem[serverHandles.Length];

            lock (m_lock)
            {
                // update the filter for analog items that have not overriden the deadband.
                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        errors[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    if (item.SamplingRate == -1)
                    {
                        errors[ii] = ResultIds.E_RATENOTSET;
                        continue;
                    }
                    
                    item.MonitoredItem.SamplingInterval = (int)(this.UpdateRate/2);
                    items[ii] = item;
                }

                // update the items on the server.
                try
                {
                    m_subscription.ApplyChanges();
                }
                catch (Exception e)
                {
                    int error = ComUtils.GetErrorCode(e, ResultIds.E_FAIL);

                    for (int ii = 0; ii < items.Length; ii++)
                    {
                        if (items[ii] != null)
                        {
                            errors[ii] = error;
                        }
                    }

                    return errors;
                }

                // check results for monitored items.
                for (int ii = 0; ii < items.Length; ii++)
                {
                    ComDaGroupItem item = items[ii];

                    if (item == null)
                    {
                        continue;
                    }

                    if (ServiceResult.IsBad(item.MonitoredItem.Status.Error))
                    {
                        errors[ii] = ResultIds.E_FAIL;
                        continue;
                    }

                    item.SamplingRate = -1;
                    item.ActualSamplingRate = 0;
                    item.NextUpdateTime = -1;
                }

                return errors;
            }
        }

        /// <summary>
        /// Sets whether buffering is enabled for the items.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="bufferEnabled">Whether buffering is enabled.</param>
        /// <returns>Any errors.</returns>
        public int[] SetItemBufferEnabled(int[] serverHandles, int[] bufferEnabled)
        {
            TraceState("SetItemBufferEnabled", serverHandles.Length);
            ThrowIfDisposed();

            int[] errors = new int[serverHandles.Length];

            lock (m_lock)
            {
                // update the filter for analog items that have not overriden the deadband.
                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        errors[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    item.BufferEnabled = bufferEnabled[ii] != 0;
                }

                return errors;
            }
        }

        /// <summary>
        /// Gets whether buffering is enabled for the items.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <param name="bufferEnabled">Whether buffering is enabled.</param>
        /// <returns>Any errors.</returns>
        public int[] GetItemBufferEnabled(int[] serverHandles, int[] bufferEnabled)
        {
            TraceState("GetItemBufferEnabled", serverHandles.Length);
            ThrowIfDisposed();

            int[] errors = new int[serverHandles.Length];

            lock (m_lock)
            {
                // update the filter for analog items that have not overriden the deadband.
                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    ComDaGroupItem item = null;

                    if (!m_itemsByHandle.TryGetValue(serverHandles[ii], out item))
                    {
                        errors[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    bufferEnabled[ii] = (item.BufferEnabled)?1:0;
                }

                return errors;
            }
        }

        /// <summary>
        /// Updates the cache with quality.
        /// </summary>
        /// <param name="quality">The quality.</param>
        public void UpdateCacheWithQuality(short quality)
        {
            lock (m_lock)
            {
                DateTime now = DateTime.UtcNow;

                foreach (ComDaGroupItem item in m_itemsByHandle.Values)
                {
                    if (null == item.CacheEntry) continue;
                    DaValue value = new DaValue();
                    item.CacheEntry.GetLatest(value);
                    value.Quality = quality;
                    value.Timestamp = now;
                    UpdateCache(item, value, false);
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the deadband filters.
        /// </summary>
        /// <param name="items">The items.</param>
        public void UpdateDeadbandFilters(IList<ComDaGroupItem> items)
        {
            // update the monitored item filter.
            for (int ii = 0; ii < items.Count; ii++)
            {
                ComDaGroupItem item = items[ii];

                if (item == null)
                {
                    continue;
                }

                if (item.EuType != (int)OpcRcw.Da.OPCEUTYPE.OPC_ANALOG)
                {
                    item.MonitoredItem.Filter = null;
                    continue;
                }

                // do nothing if the filter is not set and is not required.
                if (item.MonitoredItem.Filter == null)
                {
                    if (this.Deadband <= 0 && item.Deadband <= 0)
                    {
                        continue;
                    }
                }

                DataChangeFilter filter = new DataChangeFilter();
                filter.Trigger = DataChangeTrigger.StatusValue;

                if (this.Deadband <= 0 && item.Deadband <= 0)
                {
                    filter.DeadbandType = (uint)DeadbandType.None;
                }
                else
                {
                    filter.DeadbandType = (uint)DeadbandType.Percent;
                    filter.DeadbandValue = this.Deadband;
                }

                if (item.Deadband >= 0)
                {
                    filter.DeadbandValue = item.Deadband;
                }

                item.MonitoredItem.Filter = filter;
            }
        }

        /// <summary>
        /// Gets the session associated with the group.
        /// </summary>
        /// <returns>The session.</returns>
        private Session GetSession()
        {
            if (m_subscription == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            return m_subscription.Session;
        }

        /// <summary>
        /// Sets the error for all items after an unexpected error occurs.
        /// </summary>
        /// <param name="itemsInRequest">The items in request.</param>
        /// <param name="results">The results.</param>
        /// <param name="e">The exception.</param>
        /// <param name="defaultError">The default error.</param>
        private void SetFatalError(ComDaGroupItem[] itemsInRequest, int[] results, Exception e, int defaultError)
        {
            int error = ComUtils.GetErrorCode(e, defaultError);

            for (int ii = 0; ii < itemsInRequest.Length; ii++)
            {
                if (itemsInRequest[ii] != null)
                {
                    results[ii] = error;
                }
            }
        }

        /// <summary>
        /// Checks the update timer status.
        /// </summary>
        private void CheckUpdateTimerStatus()
        {
            lock (m_lock)
            {
                if (AreUpdatesRequired)
                {
                    if (m_nextUpdateTime == -1)
                    {
                        ScheduleNextUpdate();
                    }

                    // check if it needs to be started.
                    if (m_updateTimer == null)
                    {
                        // mark all of the cached values for re sending.
                        for (int ii = 0; ii < m_items.Count; ii++)
                        {
                            if (m_items[ii].CacheEntry != null)
                            {
                                m_items[ii].CacheEntry.Changed = true;
                            }
                        }

                        m_updateTimer = new Timer(OnUpdate, null, 0, 50);
                    }

                    return;
                }

                // check if the timer needs to stopped.
                if (m_updateTimer != null)
                {
                    m_updateTimer.Dispose();
                    m_updateTimer = null;
                }

                m_nextUpdateTime = -1;
            }
        }

        /// <summary>
        /// Gets a value indicating whether updates are required.
        /// </summary>
        /// <value><c>true</c> if updates are required; otherwise, <c>false</c>.</value>
        private bool AreUpdatesRequired
        {
            get
            {
                if (m_disposed || !m_active || !m_enabled || m_callback == null)
                {
                    return false;
                }

                int activeItemCount = 0;

                for (int ii = 0; ii < m_items.Count; ii++)
                {
                    if (m_items[ii].MonitoredItem.Status.MonitoringMode == MonitoringMode.Reporting)
                    {
                        activeItemCount++;
                        break;
                    }
                }

                if (activeItemCount == 0 && m_keepAliveTime <= 0)
                {
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Schedules the next update.
        /// </summary>
        private void ScheduleNextUpdate()
        {
            long now = HiResClock.UtcNow.Ticks;

            if (m_nextUpdateTime > 0)
            {
                m_nextUpdateTime += m_actualUpdateRate*TimeSpan.TicksPerMillisecond;
            }
            else
            {
                m_nextUpdateTime = now + m_actualUpdateRate*TimeSpan.TicksPerMillisecond;
            }

            // Utils.Trace("NextUpdateTime={0:mm:ss.fff}, ActualUpdateRate={1}", new DateTime(m_nextUpdateTime), m_actualUpdateRate);

            for (int ii = 0; ii < m_items.Count; ii++)
            {
                // check if item is being sampled independently.
                if (m_items[ii].SamplingRate >= 0)
                {
                    // check if the update time has already passed.
                    if (m_items[ii].NextUpdateTime < now)
                    {
                        m_items[ii].NextUpdateTime = now + m_items[ii].ActualSamplingRate*TimeSpan.TicksPerMillisecond;
                    }
                }

                // turn off item level update time checks.
                else
                {
                    m_items[ii].NextUpdateTime = -1;
                }
            }
        }

        /// <summary>
        /// Called when a data change arrives.
        /// </summary>
        /// <param name="subscription">The subscription.</param>
        /// <param name="notification">The notification.</param>
        /// <param name="stringTable">The string table.</param>
        void OnDataChange(Subscription subscription, DataChangeNotification notification, IList<string> stringTable)
        {
            try
            {
                lock (m_lock)
                {
                    for (int jj = 0; jj < notification.MonitoredItems.Count; jj++)
                    {
                        uint clientHandle = notification.MonitoredItems[jj].ClientHandle;

                        // find item.
                        ComDaGroupItem item = null;

                        if (!m_itemsByMonitoredItem.TryGetValue(clientHandle, out item))
                        {
                            continue;
                        }

                        // convert data change.
                        DaValue result = m_manager.Mapper.GetLocalDataValue(notification.MonitoredItems[jj].Value);

                        // update cache.
                        UpdateCache(item, result, false);
                    }
                }
            }
            catch (Exception exception)
            {
                Utils.Trace("Unexpected error during CacheUpdate. {0}", exception.Message);
            }
        }

        /// <summary>
        /// Called when it is time to send an update.
        /// </summary>
        /// <param name="state">The state.</param>
        private void OnUpdate(object state)
        {
            try
            {
                IComDaGroupCallback callback = null;
                List<int> clientHandles = null;
                List<DaValue> values = null;
                long now = HiResClock.UtcNow.Ticks;

                lock (m_lock)
                {
                    // check if updates are required.
                    if (!AreUpdatesRequired || m_updateInProgress)
                    {
                        TraceState("OnUpdate Skipped");
                        return;
                    }

                    // check if enough time has elapsed.
                    if (m_nextUpdateTime == -1 || m_nextUpdateTime > (now + 50*TimeSpan.TicksPerMillisecond))
                    {             
                        return;
                    }

                    // Utils.Trace("NextUpdateTime={0:mm:ss.fff}, CurrentTime={1:mm:ss.fff}", new DateTime(m_nextUpdateTime), new DateTime(now));

                    // collect values to report.
                    for (int ii = 0; ii < m_items.Count; ii++)
                    {
                        ComDaGroupItem item = m_items[ii];

                        if (!item.Active)
                        {
                            continue;
                        }

                        if (item.CacheEntry == null || !item.CacheEntry.Changed)
                        {
                            continue;
                        }

                        // write buffered values first.
                        DaValue value = null;

                        if (item.CacheEntry.NextEntry != null)
                        {
                            Stack<DaCacheValue> stack = new Stack<DaCacheValue>();

                            for (DaCacheValue entry = item.CacheEntry; entry != null; entry = entry.NextEntry)
                            {
                                stack.Push(entry);
                            }

                            while (stack.Count > 1)
                            {
                                DaCacheValue entry = stack.Pop();

                                value = new DaValue();
                                entry.GetLatest(value);
                                UpdateReadResult(item, value);

                                if (item.LastSentValue != null)
                                {
                                    if (value.Quality == item.LastSentValue.Quality)
                                    {
                                        if (Utils.IsEqual(item.LastSentValue.Value, value.Value))
                                        {
                                            continue;
                                        }
                                    }
                                }

                                if (clientHandles == null)
                                {
                                    clientHandles = new List<int>();
                                    values = new List<DaValue>();
                                }

                                clientHandles.Add(item.ClientHandle);
                                values.Add(value);
                                item.LastSentValue = value;

                                /*
                                TraceState(
                                    "OnUpdate BUFFERED VALUE",
                                    this.m_serverHandle,
                                    item.ServerHandle,
                                    item.ClientHandle,
                                    new Variant(value.Value),
                                    value.Timestamp.ToString("HH:mm:ss.fff"),
                                    item.CacheEntry.Changed);
                                */
                            }

                            // clear cache.
                            item.CacheEntry.NextEntry = null;
                        }

                        // check if enough time has elapsed for this item (used if the sampling rate > update rate).
                        if (item.NextUpdateTime != -1 && item.NextUpdateTime > now)
                        {
                            continue;
                        }

                        // add latest values.
                        value = new DaValue();
                        item.CacheEntry.GetLatest(value);
                        UpdateReadResult(item, value);

                        if (item.LastSentValue != null)
                        {
                            if (value.Quality == item.LastSentValue.Quality)
                            {
                                if (Utils.IsEqual(item.LastSentValue.Value, value.Value))
                                {
                                    item.CacheEntry.Changed = false;
                                    continue;
                                }
                            }
                        }

                        if (clientHandles == null)
                        {
                            clientHandles = new List<int>();
                            values = new List<DaValue>();
                        }

                        clientHandles.Add(item.ClientHandle);
                        values.Add(value);
                        item.LastSentValue = value;

                        /*
                        TraceState(
                            "OnUpdate LATEST VALUE",
                            this.m_serverHandle,
                            item.ServerHandle,
                            item.ClientHandle,
                            new Variant(value.Value),
                            value.Timestamp.ToString("HH:mm:ss.fff"),
                            item.CacheEntry.Changed);
                        */

                        // clear change flag.
                        item.CacheEntry.Changed = false;
                    }

                    // nothing to report unless the keep alive expired.
                    if (clientHandles == null || clientHandles.Count == 0)
                    {
                        if (m_keepAliveTime == 0 || m_lastUpdateTime + m_keepAliveTime*TimeSpan.TicksPerMillisecond > now)
                        {
                            ScheduleNextUpdate();
                            return;
                        }
                    }

                    callback = m_callback;
                    m_updateInProgress = true;
                    m_lastUpdateTime = now;

                    // schedule next update.
                    ScheduleNextUpdate();
                }

                // send callback.
                try
                {
                    callback.ReadCompleted(
                        this.m_clientHandle,
                        false,
                        0,
                        0,
                        (clientHandles != null) ? clientHandles.ToArray() : new int[0],
                        (values != null) ? values.ToArray() : new DaValue[0]);

                    if (clientHandles.Count > 0)
                    {
                        m_manager.SetLastUpdateTime();
                    }
                    
                    /*
                    TraceState(
                        "OnUpdate Completed", 
                        this.m_serverHandle,
                        (values != null && values.Count > 0)?values[0].Value:"null", 
                        (m_nextUpdateTime-now)/TimeSpan.TicksPerMillisecond);
                    */
                }
                finally
                {
                    lock (m_lock)
                    {
                        m_updateInProgress = false;
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace("Unexpected error during GroupUpdate. {0}", e.Message);
            }
        }

        /// <summary>
        /// Called to process an asynchronous read.
        /// </summary>
        private void OnAsyncRead(object state)
        {
            try
            {
                TraceState("OnAsyncRead");

                // find the request.
                ComDaAsnycReadRequest request = null;
                IComDaGroupCallback callback = null;

                lock (m_lock)
                {
                    // check if dispoed.
                    if (m_disposed)
                    {
                        return;
                    }

                    // find request.
                    for (int ii = 0; ii < m_requests.Count; ii++)
                    {
                        if (Object.ReferenceEquals(state, m_requests[ii]))
                        {
                            request = (ComDaAsnycReadRequest)state;
                            m_requests.RemoveAt(ii);
                            break;
                        }
                    }

                    callback = m_callback;

                    // request must have been cancelled.
                    if (request == null || callback == null)
                    {
                        return;
                    }
                }
                
                // report that the cancel succeeded.
                if (request.Cancelled)
                {
                    callback.CancelSucceeded(m_clientHandle, request.TransactionId);
                    TraceState("OnAsyncRead Cancelled", request.TransactionId, request.CancelId);
                    return;                    
                }
                
                // check if the initial read after group is activated.
                int[] serverHandles = request.ServerHandles;
                List<ComDaGroupItem> itemsToUpdate = null;

                if (request.IsFirstUpdate)
                {
                    lock (m_lock)
                    {
                        // need to check if an update has already arrived.
                        itemsToUpdate = new List<ComDaGroupItem>(m_items.Count);

                        for (int ii = 0; ii < m_items.Count; ii++)
                        {
                            ComDaGroupItem item = m_items[ii];

                            if (item.Active)
                            {
                                itemsToUpdate.Add(item);
                            }
                        }

                        // nothing more to do.
                        if (itemsToUpdate.Count == 0)
                        {
                            return;
                        }

                        // revise the handles to read.
                        serverHandles = new int[itemsToUpdate.Count];

                        for (int ii = 0; ii < itemsToUpdate.Count; ii++)
                        {
                            serverHandles[ii] = itemsToUpdate[ii].ServerHandle;
                        }
                    }
                }

                // read values.
                DaValue[] results = SyncRead(request.MaxAge, serverHandles, request.IsRefresh, null);

                // update cache.
                if (request.IsFirstUpdate)
                {
                    lock (m_lock)
                    {
                        for (int ii = 0; ii < itemsToUpdate.Count; ii++)
                        {
                            ComDaGroupItem item = m_items[ii];
                            item.LastSentValue = results[ii];
                        }
                    }
                }

                // send callback.
                callback.ReadCompleted(
                    this.m_clientHandle,
                    request.IsRefresh,
                    request.CancelId,
                    request.TransactionId,
                    request.ClientHandles,
                    results);

                TraceState("OnAsyncRead Completed", request.TransactionId, request.CancelId);
            }
            catch (Exception e)
            {
                Utils.Trace("Unexpected error during AsyncRead. {0}", e.Message);
            }
        }

        /// <summary>
        /// Called to process an asynchronous write.
        /// </summary>
        private void OnAsyncWrite(object state)
        {
            try
            {
                TraceState("OnAsyncWrite");

                // find the request.
                ComDaAsnycWriteRequest request = null;
                IComDaGroupCallback callback = null;

                lock (m_lock)
                {
                    // check if dispoed.
                    if (m_disposed)
                    {
                        return;
                    }

                    // find request.
                    for (int ii = 0; ii < m_requests.Count; ii++)
                    {
                        if (Object.ReferenceEquals(state, m_requests[ii]))
                        {
                            request = (ComDaAsnycWriteRequest)state;
                            m_requests.RemoveAt(ii);
                            break;
                        }
                    }

                    callback = m_callback;

                    // request must have been cancelled.
                    if (request == null || callback == null)
                    {
                        return;
                    }
                }

                // report that the cancel succeeded.
                if (request.Cancelled)
                {
                    callback.CancelSucceeded(m_clientHandle, request.TransactionId);
                    TraceState("OnAsyncWrite Cancelled", request.TransactionId, request.CancelId);
                    return;
                }

                // write values.
                int[] results = SyncWrite(request.ServerHandles, request.Values);

                // send callback.
                callback.WriteCompleted(
                    this.m_clientHandle,
                    request.TransactionId,
                    request.ClientHandles,
                    results);

                TraceState("OnAsyncWrite Completed", request.TransactionId, request.CancelId);
            }
            catch (Exception e)
            {
                Utils.Trace("Unexpected error during AsyncWrite. {0}", e.Message);
            }
        }

        private void AddItemToSubscription(ComDaGroupItem item)
        {
            m_subscription.AddItem(item.MonitoredItem);
            m_itemsByMonitoredItem.Add(item.MonitoredItem.ClientHandle, item);
        }

        private void RemoveItemFromSubscription(ComDaGroupItem item)
        {
            m_itemsByMonitoredItem.Remove(item.MonitoredItem.ClientHandle);
            m_subscription.RemoveItem(item.MonitoredItem);
        }

        /// <summary>
        /// Dumps the current state of the browser.
        /// </summary>
        private void TraceState(string context, params object[] args)
        {
            #if TRACESTATE
            if ((Utils.TraceMask & Utils.TraceMasks.Information) == 0)
            {
                return;
            }

            StringBuilder buffer = new StringBuilder();

            buffer.AppendFormat("ComDaGroup::{0}", context);

            if (args != null)
            {
                buffer.Append("( ");

                for (int ii = 0; ii < args.Length; ii++)
                {
                    if (ii > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.Append(new Variant(args[ii]));
                }

                buffer.Append(" )");
            }

            Utils.Trace("{0}", buffer.ToString());
            #endif
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private bool m_disposed;
        private ComDaGroupManager m_manager;
        private IntPtr m_handle;
        private string m_name;
        private int m_serverHandle;
        private int m_clientHandle;
        private bool m_active;
        private bool m_enabled;
        private int m_updateRate;
        private float m_deadband;
        private int m_timeBias;
        private int m_lcid;
        private int m_actualUpdateRate;
        private Subscription m_subscription;
        private Dictionary<int,ComDaGroupItem> m_itemsByHandle;
        private List<ComDaGroupItem> m_items;
        private int m_transactionCounter = 1000;
        private List<ComDaAsnycRequest> m_requests;
        private int m_keepAliveTime;
        private IComDaGroupCallback m_callback;
        private long m_nextUpdateTime;
        private long m_lastUpdateTime;
        private Timer m_updateTimer;
        private bool m_updateInProgress;
        private Dictionary<uint, ComDaGroupItem> m_itemsByMonitoredItem;
        #endregion
	}
}
