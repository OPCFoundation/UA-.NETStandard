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
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using OpcRcw.Comn;
using OpcRcw.Da;
using Opc.Ua;
using Opc.Ua.Com;
using Opc.Ua.Com.Client;
using Opc.Ua.Server;

namespace Opc.Ua.Com.Client
{    
    /// <summary>
    /// Stores a request to subscribe to the attributes of an HDA item.
    /// </summary>
    public class HdaSubscribeAttributeRequest
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="HdaSubscribeAttributeRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        public HdaSubscribeAttributeRequest(string itemId)
        {
            m_itemId = itemId;
        }
        #endregion

        #region Public Attributes
        /// <summary>
        /// Gets the item id.
        /// </summary>
        /// <value>The item id.</value>
        public string ItemId
        {
            get { return m_itemId; }
        }

        /// <summary>
        /// Gets or sets the server handle.
        /// </summary>
        /// <value>The server handle.</value>
        public int? ServerHandle
        {
            get { return m_serverHandle; }
            set { m_serverHandle = value; }
        }

        /// <summary>
        /// Gets the monitored items.
        /// </summary>
        /// <value>The monitored items.</value>
        public List<MonitoredItem> MonitoredItems
        {
            get { return m_monitoredItems; }
        }

        /// <summary>
        /// Gets the sampling interval.
        /// </summary>
        /// <value>The sampling interval.</value>
        public int SamplingInterval
        {
            get { return m_samplingInterval; }
        }

        /// <summary>
        /// Gets the next update time.
        /// </summary>
        /// <value>The next update time.</value>
        public DateTime NextUpdateTime
        {
            get { return m_nextUpdateTime; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Adds the specified monitored item.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        public void Add(ServerSystemContext context, MonitoredItem monitoredItem)
        {
            if (monitoredItem == null)
            {
                return;
            }

            // save the monitored item.
            if (m_monitoredItems == null)
            {
                m_monitoredItems = new List<MonitoredItem>();
            }

            m_monitoredItems.Add(monitoredItem);
        }

        /// <summary>
        /// Modifies the request after the specified item changes.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        public void Modify(ServerSystemContext context, MonitoredItem monitoredItem)
        {
            // nothing to do.
        }

        /// <summary>
        /// Removes the specified monitored item.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        public void Remove(ServerSystemContext context, MonitoredItem monitoredItem)
        {
            // remove the monitored item.
            if (monitoredItem != null && m_monitoredItems != null)
            {
                for (int ii = 0; ii < m_monitoredItems.Count; ii++)
                {
                    if (Object.ReferenceEquals(m_monitoredItems[ii], monitoredItem))
                    {
                        m_monitoredItems.RemoveAt(ii);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Queues the value to the monitored item.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="request">The request.</param>
        /// <param name="initialUpdate">if set to <c>true</c> [initial update].</param>
        public void QueueValues(ServerSystemContext context, HdaReadRequest request, bool initialUpdate)
        {
            for (int ii = 0; ii < m_monitoredItems.Count; ii++)
            {
                QueueValue(context, request, m_monitoredItems[ii]);
            }

            if (!initialUpdate)
            {
                IncrementSampleTime();
            }
        }

        /// <summary>
        /// Updates the request after changes are completed.
        /// </summary>
        /// <param name="attributeSamplingInterval">The attribute sampling interval.</param>
        public void ChangesComplete(int attributeSamplingInterval)
        {
            // adjust the sampling interval on the monitored items.
            if (m_monitoredItems != null)
            {
                for (int ii = 0; ii < m_monitoredItems.Count; ii++)
                {
                    int samplingInterval = (int)m_monitoredItems[ii].SamplingInterval;

                    if (attributeSamplingInterval > samplingInterval)
                    {
                        m_monitoredItems[ii].SetSamplingInterval(attributeSamplingInterval);
                        continue;
                    }

                    if (samplingInterval%attributeSamplingInterval != 0)
                    {
                        int muliple = samplingInterval/attributeSamplingInterval + 1;
                        m_monitoredItems[ii].SetSamplingInterval(attributeSamplingInterval*muliple);
                    }
                }
            }

            UpdateSamplingInterval();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the request after adding/removing items.
        /// </summary>
        private void UpdateSamplingInterval()
        {
            double samplingInterval = Double.MaxValue;

            for (int ii = 0; ii < MonitoredItems.Count; ii++)
            {
                MonitoredItem monitoredItem = MonitoredItems[ii];

                // find the handle associated with the item.
                NodeHandle handle = monitoredItem.ManagerHandle as NodeHandle;

                if (handle == null)
                {
                    continue;
                }

                // ignore disabled items.
                if (monitoredItem.MonitoringMode == MonitoringMode.Disabled)
                {
                    continue;
                }

                // find the fastest sampling interval.
                if (samplingInterval > monitoredItem.SamplingInterval)
                {
                    samplingInterval = monitoredItem.SamplingInterval;
                }
            }

            // update values.
            if (samplingInterval != m_samplingInterval)
            {
                m_samplingInterval = (int)samplingInterval;
                m_nextUpdateTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Increments the sample time to the next interval.
        /// </summary>
        private void IncrementSampleTime()
        {
            // update next sample time.
            long now = DateTime.UtcNow.Ticks;
            long samplingInterval = (long)(m_samplingInterval*TimeSpan.TicksPerMillisecond);

            long nextSampleTime = 0;

            if (nextSampleTime > 0)
            {
                long delta = now - nextSampleTime;

                if (samplingInterval > 0 && delta >= 0)
                {
                    nextSampleTime += ((delta/samplingInterval)+1)*samplingInterval;
                }
            }

            // set sampling time based on current time.
            else
            {
                nextSampleTime = now + samplingInterval;
            }

            m_nextUpdateTime = new DateTime(nextSampleTime);
        }

        /// <summary>
        /// Queues the value to the monitored item.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="request">The request.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private void QueueValue(ServerSystemContext context, HdaReadRequest request, MonitoredItem monitoredItem)
        {
            NodeHandle handle = monitoredItem.ManagerHandle as NodeHandle;

            if (handle == null)
            {
                return;
            }

            ReadValueId nodeToRead = monitoredItem.GetReadValueId();
            DataValue value = new DataValue();
            ServiceResult error = null;

            HdaItemState item = handle.Node as HdaItemState;
            HdaAttributeState attribute = handle.Node as HdaAttributeState;

            if (item != null)
            {
                error = request.GetResult(context, item, nodeToRead, value, monitoredItem.DiagnosticsMasks);
            }
            else if (attribute != null)
            {
                error = request.GetResult(context, attribute, nodeToRead, value, monitoredItem.DiagnosticsMasks);
            }

            value.ServerTimestamp = DateTime.UtcNow;

            if (value.StatusCode != StatusCodes.BadNotFound)
            {
                monitoredItem.QueueValue(value, error);
            }
        }
        #endregion

        #region Private Fields
        private string m_itemId;
        private int? m_serverHandle;
        private List<MonitoredItem> m_monitoredItems;
        private int m_samplingInterval;
        private DateTime m_nextUpdateTime;
        #endregion
    }

    /// <summary>
    /// Manages the subscribe requests.
    /// </summary>
    internal class HdaSubscribeRequestManager : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="HdaSubscribeRequestManager"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="localeId">The locale to use.</param>
        /// <param name="configuration">The configuration.</param>
        public HdaSubscribeRequestManager(ServerSystemContext context, int localeId, ComHdaClientConfiguration configuration)
        {
            m_context = context;
            m_localeId = localeId;
            m_configuration = configuration;
            m_requests = new Dictionary<string, HdaSubscribeAttributeRequest>();
            m_monitoredItems = new Dictionary<uint, IMonitoredItem>();
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
            if (disposing)
            {
                lock (m_lock)
                {
                    Utils.SilentDispose(m_attributeScanTimer);
                    m_attributeScanTimer = null;
                }
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// The local used by the request manager.
        /// </summary>
        public int LocaleId
        {
            get { return m_localeId; }
        }

        /// <summary>
        /// Recreates the items after a disconnect.
        /// </summary>
        public void RecreateItems()
        {
            lock (m_lock)
            {
                m_requests.Clear();

                Utils.SilentDispose(m_attributeScanTimer);
                m_attributeScanTimer = null;

                IList<IMonitoredItem> monitoredItems = new List<IMonitoredItem>(m_monitoredItems.Values);
                m_monitoredItems.Clear();

                CreateItems(m_context, monitoredItems);
            }
        }

        /// <summary>
        /// Creates subscription requests for monitored items.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoredItems">The monitored items.</param>
        public void CreateItems(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            lock (m_lock)
            {
                if (monitoredItems != null)
                {
                    Dictionary<string,HdaSubscribeAttributeRequest> requests = new Dictionary<string, HdaSubscribeAttributeRequest>();

                    for (int ii = 0; ii < monitoredItems.Count; ii++)
                    {
                        MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                        if (monitoredItem == null)
                        {
                            continue;
                        }

                        NodeHandle handle = monitoredItem.ManagerHandle as NodeHandle;
                        
                        if (handle == null)
                        {
                            continue;
                        }

                        if (!HasExternalSource(handle.Node, monitoredItem.AttributeId))
                        {
                            continue;
                        }

                        HdaSubscribeAttributeRequest request = Add(handle.Node, monitoredItem);

                        if (request != null)
                        {
                            requests[request.ItemId] = request;
                        }
                    }

                    ApplyChanges(requests, true);
                }
            }
        }

        /// <summary>
        /// Modifies subscription requests for monitored items.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoredItems">The monitored items.</param>
        public void ModifyItems(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            lock (m_lock)
            {
                if (monitoredItems != null)
                {
                    Dictionary<string,HdaSubscribeAttributeRequest> requests = new Dictionary<string, HdaSubscribeAttributeRequest>();

                    for (int ii = 0; ii < monitoredItems.Count; ii++)
                    {
                        MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                        if (monitoredItem == null)
                        {
                            continue;
                        }

                        NodeHandle handle = monitoredItem.ManagerHandle as NodeHandle;

                        if (handle == null)
                        {
                            continue;
                        }

                        if (!HasExternalSource(handle.Node, monitoredItem.AttributeId))
                        {
                            continue;
                        }

                        HdaSubscribeAttributeRequest request = Modify(handle.Node, monitoredItem);

                        if (request != null)
                        {
                            requests[request.ItemId] = request;
                        }
                    }

                    ApplyChanges(requests, true);
                }
            }
        }

        /// <summary>
        /// Deletes subscription requests for monitored items.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoredItems">The monitored items.</param>
        public void DeleteItems(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            lock (m_lock)
            {
                if (monitoredItems != null)
                {
                    Dictionary<string,HdaSubscribeAttributeRequest> requests = new Dictionary<string, HdaSubscribeAttributeRequest>();

                    for (int ii = 0; ii < monitoredItems.Count; ii++)
                    {
                        MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                        if (monitoredItem == null)
                        {
                            continue;
                        }

                        NodeHandle handle = monitoredItem.ManagerHandle as NodeHandle;

                        if (handle == null)
                        {
                            continue;
                        }

                        if (!HasExternalSource(handle.Node, monitoredItem.AttributeId))
                        {
                            continue;
                        }

                        HdaSubscribeAttributeRequest request = Remove(handle.Node, monitoredItem);

                        if (request != null)
                        {
                            requests[request.ItemId] = request;
                        }
                    }

                    ApplyChanges(requests, false);
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Finds the item attributes request for the specifed item. 
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="create">if set to <c>true</c> a new request is created if it does not exist.</param>
        /// <returns>The subscribe attribute request.</returns>
        private HdaSubscribeAttributeRequest FindAttributeRequest(string itemId, bool create)
        {
            lock (m_lock)
            {
                HdaSubscribeAttributeRequest requests = null;

                if (!m_requests.TryGetValue(itemId, out requests))
                {
                    if (!create)
                    {
                        return null;
                    }

                    requests = new HdaSubscribeAttributeRequest(itemId);
                    m_requests.Add(itemId, requests);
                }

                return requests;
            }
        }

        /// <summary>
        /// Determines whether the attribute has an external source.
        /// </summary>
        private bool HasExternalSource(NodeState node, uint attributeId)
        {
            HdaItemState item = node as HdaItemState;

            if (item != null)
            {
                switch (attributeId)
                {
                    case Attributes.DataType:
                    case Attributes.ValueRank:
                    case Attributes.Description:
                    case Attributes.Historizing:
                    {
                        return true;
                    }
                }

                return false;
            }

            HdaAttributeState attribute = node as HdaAttributeState;

            if (attribute != null)
            {
                switch (attributeId)
                {
                    case Attributes.Value:
                    case Attributes.AccessLevel:
                    case Attributes.UserAccessLevel:
                    {
                        return true;
                    }
                }

                return false;
            }

            return false;
        }

        /// <summary>
        /// Adds the monitored item to the collection.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private HdaSubscribeAttributeRequest Add(NodeState source, MonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                m_monitoredItems.Add(monitoredItem.Id, monitoredItem);

                // get the HDA item id.
                string itemId = GetItemId(source);

                // create/update the subscribe request for the HDA item.
                if (itemId != null)
                {
                    HdaSubscribeAttributeRequest request = FindAttributeRequest(itemId, true);

                    if (request != null)
                    {
                        request.Add(m_context, monitoredItem);
                        return request;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Modifies the monitored item to the collection.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private HdaSubscribeAttributeRequest Modify(NodeState source, MonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                // get the HDA item id.
                string itemId = GetItemId(source);

                // update the subscribe request for the HDA item.
                if (itemId != null)
                {
                    HdaSubscribeAttributeRequest request = FindAttributeRequest(itemId, false);

                    if (request != null)
                    {
                        request.Modify(m_context, monitoredItem);
                        return request;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Removes the monitored item from the collection.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private HdaSubscribeAttributeRequest Remove(NodeState source, MonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                m_monitoredItems.Remove(monitoredItem.Id);

                // get the HDA item id.
                string itemId = GetItemId(source);

                // delete the subscribe request for the HDA item.
                if (itemId != null)
                {
                    HdaSubscribeAttributeRequest request = FindAttributeRequest(itemId, false);

                    if (request != null)
                    {
                        request.Remove(m_context, monitoredItem);
                        return request;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the HDA item id associated with the source.
        /// </summary>
        /// <param name="source">The source.</param>
        private string GetItemId(NodeState source)
        {
            HdaItemState item = source as HdaItemState;
            
            if (item != null)
            {
                return item.ItemId;
            }

            HdaAttributeState attribute = source as HdaAttributeState;

            if (attribute != null)
            {
                return attribute.ItemId;
            }

            return null;
        }

        /// <summary>
        /// Applies any changes to the subscriptions.
        /// </summary>
        private void ApplyChanges(Dictionary<string, HdaSubscribeAttributeRequest> requests, bool initialUpdateRequired)
        {
            List<HdaSubscribeAttributeRequest> requestsToRemove = null;

            lock (m_lock)
            {
                // update existing requests.
                foreach (KeyValuePair<string,HdaSubscribeAttributeRequest> entry in requests)
                {
                    HdaSubscribeAttributeRequest request = entry.Value;

                    // remove unused requests.
                    if (request.MonitoredItems == null || request.MonitoredItems.Count == 0)
                    {
                        if (requestsToRemove == null)
                        {
                            requestsToRemove = new List<HdaSubscribeAttributeRequest>();
                        }

                        requestsToRemove.Add(entry.Value);
                        continue;
                    }

                    request.ChangesComplete(m_configuration.AttributeSamplingInterval);
                }

                // remove deleted requests from dictionary.
                if (requestsToRemove != null)
                {
                    for (int ii = 0; ii < requestsToRemove.Count; ii++)
                    {
                        m_requests.Remove(requestsToRemove[ii].ItemId);
                    }
                }
                
                // check if the attribute scanner needs to be stopped/started.
                if (m_attributeScanTimer == null)
                {
                    if (m_requests.Count > 0)
                    {
                        m_attributeScanTimer = new Timer(OnScanAttributes, null, m_configuration.AttributeSamplingInterval, m_configuration.AttributeSamplingInterval);
                    }
                }
                else
                {
                    if (m_requests.Count == 0)
                    {
                        m_attributeScanTimer.Dispose();
                        m_attributeScanTimer = null;
                    }
                }
            }

            // release any unused handles.
            if (requestsToRemove != null)
            {
                ComHdaClientManager system = (ComHdaClientManager)m_context.SystemHandle;
                ComHdaClient client = (ComHdaClient)system.SelectClient(m_context, false);
                ReleaseHandles(client, requestsToRemove);
            }

            // send initial update.
            if (initialUpdateRequired)
            {
                DoScan(requests, true);
            }
        }

        /// <summary>
        /// Assigns the handles to the requests.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requests">The requests.</param>
        private void AssignHandles(ComHdaClient client, Dictionary<string, HdaSubscribeAttributeRequest> requests)
        {
            List<string> itemIds = null;

            lock (m_lock)
            {
                foreach (KeyValuePair<string,HdaSubscribeAttributeRequest> entry in requests)
                {
                    if (entry.Value.ServerHandle == null)
                    {
                        if (itemIds == null)
                        {
                            itemIds = new List<string>();
                        }

                        itemIds.Add(entry.Key);
                    }
                }
            }

            if (itemIds == null)
            {
                return;
            }

            HdaItem[] items = client.GetItems(itemIds.ToArray());

            lock (m_lock)
            {
                for (int ii = 0; ii < items.Length; ii++)
                {
                    HdaSubscribeAttributeRequest request = null;

                    if (!requests.TryGetValue(itemIds[ii], out request))
                    {
                        continue;
                    }

                    if (items[ii].Error < 0)
                    {
                        request.ServerHandle = null;
                        continue;
                    }

                    request.ServerHandle = items[ii].ServerHandle;
                }
            }
        }

        /// <summary>
        /// Releases the handles.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requests">The requests.</param>
        private void ReleaseHandles(ComHdaClient client, List<HdaSubscribeAttributeRequest> requests)
        {
            List<int> serverHandles = null;

            lock (m_lock)
            {
                for (int ii = 0; ii < requests.Count; ii++)
                {
                    if (requests[ii].ServerHandle != null)
                    {
                        if (serverHandles == null)
                        {
                            serverHandles = new List<int>();
                        }

                        serverHandles.Add(requests[ii].ServerHandle.Value);
                        requests[ii].ServerHandle = null;
                    }
                }
            }

            if (serverHandles == null)
            {
                return;
            }

            client.ReleaseItemHandles(serverHandles.ToArray());
        }
        
        /// <summary>
        /// Called when the scan attributes timer expires.
        /// </summary>
        private void DoScan(Dictionary<string, HdaSubscribeAttributeRequest> requests, bool initialUpdate)
        {
            try
            {
                ComHdaClientManager system = (ComHdaClientManager)m_context.SystemHandle;
                ComHdaClient client = (ComHdaClient)system.SelectClient(m_context, false);

                // ensure all requests have valid handles.
                AssignHandles(client, requests);

                // collect the list of attributes that need reading.
                HdaReadRequestCollection itemsToRead = new HdaReadRequestCollection();
                List<HdaSubscribeAttributeRequest> subscribeRequests = new List<HdaSubscribeAttributeRequest>();
                List<HdaReadRequest> readRequestsForSubscribeRequests = new List<HdaReadRequest>();

                lock (m_lock)
                {
                    DateTime now = DateTime.UtcNow;

                    foreach (KeyValuePair<string,HdaSubscribeAttributeRequest> entry in requests)
                    {
                        // check if it is time for an update.
                        if (!initialUpdate && entry.Value.NextUpdateTime > now)
                        {
                            continue;
                        }

                        // create a read request for each monitored item.
                        bool somethingToDo = false;

                        for (int ii = 0; ii < entry.Value.MonitoredItems.Count; ii++)
                        {
                            MonitoredItem monitoredItem = entry.Value.MonitoredItems[ii];

                            NodeHandle handle = monitoredItem.ManagerHandle as NodeHandle;

                            if (handle == null)
                            {
                                continue;
                            }

                            // check if item is not valid.
                            if (entry.Value.ServerHandle == null)
                            {
                                monitoredItem.QueueValue(null, StatusCodes.BadNodeIdUnknown);
                                continue;
                            }

                            ReadValueId valueToRead = monitoredItem.GetReadValueId();

                            bool queued = false;
                            StatusCode error = itemsToRead.Add(handle.Node, valueToRead, out queued);

                            if (StatusCode.IsBad(error))
                            {
                                monitoredItem.QueueValue(null, error);
                                continue;
                            }

                            if (!somethingToDo && queued)
                            {
                                // add server handle to read request.
                                HdaReadRequest request = (HdaReadRequest)valueToRead.Handle;
                                request.ServerHandle = entry.Value.ServerHandle.Value;

                                // save mapping between subscribe request and read requests.
                                subscribeRequests.Add(entry.Value);
                                readRequestsForSubscribeRequests.Add(request);
                                somethingToDo = true;
                            }
                        }
                    }

                    // check if nothing to do.
                    if (requests.Count == 0)
                    {
                        return;
                    }
                }
                
                // read the attributes from the server.
                client.Read(itemsToRead, true);

                // update the monitored items.
                lock (m_lock)
                {
                    for (int ii = 0; ii < subscribeRequests.Count; ii++)
                    {
                        subscribeRequests[ii].QueueValues(m_context, readRequestsForSubscribeRequests[ii], initialUpdate);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error scanning attributes in HDA COM server.");
            }
        }

        /// <summary>
        /// Called when the scan attributes timer expires.
        /// </summary>
        private void OnScanAttributes(object state)
        {
            DoScan(m_requests, false);
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private ComHdaClientConfiguration m_configuration;
        private int m_localeId;
        private Timer m_attributeScanTimer;
        private ServerSystemContext m_context;
        private Dictionary<uint,IMonitoredItem> m_monitoredItems;
        private Dictionary<string,HdaSubscribeAttributeRequest> m_requests;
        #endregion
    }
}
