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
    /// Stores a request to subscribe to a COM DA item.
    /// </summary>
    internal class SubscribeItemRequest
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="SubscribeItemRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        public SubscribeItemRequest(string itemId)
        {
            m_itemId = itemId;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the item id.
        /// </summary>
        /// <value>The item id.</value>
        public string ItemId
        {
            get { return m_itemId; }
        }

        /// <summary>
        /// Gets or sets the group used to subscribe to the item.
        /// </summary>
        /// <value>The group.</value>
        public ComDaGroup Group
        {
            get { return m_group; }
            set {  m_group = value; }
        }

        /// <summary>
        /// Gets or sets the item within the DA group.
        /// </summary>
        /// <value>The group item.</value>
        public GroupItem GroupItem
        {
            get { return m_groupItem; }
            set {  m_groupItem = value; }
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
        /// Gets the deadband.
        /// </summary>
        /// <value>The deadband.</value>
        public float Deadband
        {
            get { return m_deadband; }
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="SubscribeItemRequest"/> is active.
        /// </summary>
        /// <value><c>true</c> if active; otherwise, <c>false</c>.</value>
        public bool Active
        {
            get { return m_active; }
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="SubscribeItemRequest"/> has changed.
        /// </summary>
        /// <value><c>true</c> if changed; otherwise, <c>false</c>.</value>
        public bool Changed
        {
            get { return m_changed; }
        }
        #endregion
        
        #region Public Methods
        /// <summary>
        /// Adds the specified monitored item.
        /// </summary>
        /// <param name="monitoredItem">The monitored item.</param>
        public void Add(MonitoredItem monitoredItem)
        {
            if (m_monitoredItems == null)
            {
                m_monitoredItems = new List<MonitoredItem>();
            }

            m_monitoredItems.Add(monitoredItem);
            m_changed = true;
        }

        /// <summary>
        /// Modifies request after the specified monitored item changed.
        /// </summary>
        /// <param name="monitoredItem">The monitored item.</param>
        public void Modify(MonitoredItem monitoredItem)
        {
            m_changed = true;
        }

        /// <summary>
        /// Removes the specified monitored item.
        /// </summary>
        /// <param name="monitoredItem">The monitored item.</param>
        public void Remove(MonitoredItem monitoredItem)
        {
            if (monitoredItem != null && m_monitoredItems != null)
            {
                for (int ii = 0; ii < m_monitoredItems.Count; ii++)
                {
                    if (Object.ReferenceEquals(m_monitoredItems[ii], monitoredItem))
                    {
                        m_monitoredItems.RemoveAt(ii);
                        m_changed = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Marks the groups changes as complete and updates the sampling interval.
        /// </summary>
        public void ChangesComplete()
        {
            if (m_group != null && m_monitoredItems != null)
            {
                this.m_changed = false;

                int actualSamplingInterval = m_group.ActualSamplingInterval;

                for (int ii = 0; ii < m_monitoredItems.Count; ii++)
                {
                    int samplingInterval = (int)m_monitoredItems[ii].SamplingInterval;

                    if (actualSamplingInterval > samplingInterval)
                    {
                        m_monitoredItems[ii].SetSamplingInterval(actualSamplingInterval);
                        continue;
                    }

                    if (samplingInterval%actualSamplingInterval != 0)
                    {
                        int muliple = samplingInterval/actualSamplingInterval + 1;
                        m_monitoredItems[ii].SetSamplingInterval(actualSamplingInterval*muliple);
                    }        
                }
            }
        }

        /// <summary>
        /// Updates this request after monitored items have been added/removed.
        /// </summary>
        public void Update()
        {
            double samplingInterval = Double.MaxValue;
            double deadband = Double.MaxValue;
            bool active = false;

            for (int ii = 0; ii < MonitoredItems.Count; ii++)
            {
                MonitoredItem monitoredItem = MonitoredItems[ii];

                // check if the item needs to be active.
                if (monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                {
                    active = true;
                }

                // find the fastest sampling interval.
                if (samplingInterval > monitoredItem.SamplingInterval)
                {
                    samplingInterval = monitoredItem.SamplingInterval;
                }

                // find the lowest deadband.
                DataChangeFilter filter = monitoredItem.Filter as DataChangeFilter;

                // no deadband means no deadband set on the OPC server.
                if (filter == null || filter.DeadbandType != (uint)DeadbandType.Percent)
                {
                    deadband = 0;
                    continue;
                }

                // must process deadbands locally if different deadbands requested.
                if (deadband != Double.MaxValue && deadband != filter.DeadbandValue)
                {
                    deadband = 0;
                    continue;
                }

                // set the deadband.
                if (deadband > filter.DeadbandValue)
                {
                    deadband = filter.DeadbandValue;
                }
            }

            // update values.
            m_samplingInterval = (int)samplingInterval;
            m_deadband = 0;

            if (deadband < Double.MaxValue)
            {
                m_deadband = (float)deadband;
            }

            // set a flag indicating that something has changed.
            m_changed = (samplingInterval != m_samplingInterval || deadband != m_deadband || active != m_active);
            m_active = active;
        }
        #endregion

        #region Private Fields
        private string m_itemId;
        private ComDaGroup m_group;
        private GroupItem m_groupItem;
        private List<MonitoredItem> m_monitoredItems;
        private int m_samplingInterval;
        private float m_deadband;
        private bool m_changed;
        private bool m_active;
        #endregion
    }
    
    /// <summary>
    /// Stores a request to subscribe to the properties of a COM DA item.
    /// </summary>
    public class SubscribePropertyRequest
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="SubscribePropertyRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        public SubscribePropertyRequest(string itemId)
        {
            m_itemId = itemId;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the item id.
        /// </summary>
        /// <value>The item id.</value>
        public string ItemId
        {
            get { return m_itemId; }
        }

        /// <summary>
        /// Gets the property ids.
        /// </summary>
        /// <value>The property ids.</value>
        public int[] PropertyIds
        {
            get { return m_propertyIds; }
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
        /// Updates the request after changes are completed.
        /// </summary>
        /// <param name="propertySamplingInterval">The property sampling interval.</param>
        public void ChangesComplete(int propertySamplingInterval)
        {
            // adjust the sampling interval on the monitored items.
            if (m_monitoredItems != null)
            {
                for (int ii = 0; ii < m_monitoredItems.Count; ii++)
                {
                    int samplingInterval = (int)m_monitoredItems[ii].SamplingInterval;

                    if (propertySamplingInterval > samplingInterval)
                    {
                        m_monitoredItems[ii].SetSamplingInterval(propertySamplingInterval);
                        continue;
                    }

                    if (samplingInterval%propertySamplingInterval != 0)
                    {
                        int muliple = samplingInterval/propertySamplingInterval + 1;
                        m_monitoredItems[ii].SetSamplingInterval(propertySamplingInterval*muliple);
                    }
                }
            }

            Update();
        }

        /// <summary>
        /// Queues the value to the monitored item.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="request">The request.</param>
        public void QueueValues(ServerSystemContext context, ReadRequest request)
        {
            for (int ii = 0; ii < m_monitoredItems.Count; ii++)
            {
                QueueValue(context, request, m_monitoredItems[ii]);
            }

            m_lastRequest = request;
            IncrementSampleTime();
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
        public void QueueValue(ServerSystemContext context, ReadRequest request, MonitoredItem monitoredItem)
        {
            NodeHandle handle = monitoredItem.ManagerHandle as NodeHandle;

            if (handle == null)
            {
                return;
            }

            ReadValueId nodeToRead = monitoredItem.GetReadValueId();
            DataValue value = new DataValue();
            ServiceResult error = null;
            
            // read item value.
            DaItemState item = handle.Node as DaItemState;
            DaPropertyState daProperty = handle.Node as DaPropertyState;
            PropertyState uaProperty = handle.Node as PropertyState;

            if (item != null)
            {
                error = request.GetResult(context, item, nodeToRead, value, monitoredItem.DiagnosticsMasks);
            }

            // read vendor defined property value.
            else if (daProperty != null)
            {
                error = request.GetResult(context, daProperty, nodeToRead, value, monitoredItem.DiagnosticsMasks);
            }

            // read UA defined property value.
            else if (uaProperty != null)
            {
                error = request.GetResult(context, uaProperty, nodeToRead, value, monitoredItem.DiagnosticsMasks);
            }

            value.ServerTimestamp = DateTime.UtcNow;

            if (value.StatusCode != StatusCodes.BadNotFound)
            {
                monitoredItem.QueueValue(value, error);
            }
            else
            {
                monitoredItem.QueueValue(value, StatusCodes.BadNodeIdUnknown);
            }
        }

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

            // queue the initial value.
            if (m_lastRequest != null)
            {
                QueueValue(context, m_lastRequest, monitoredItem);
            }
        }

        /// <summary>
        /// Modifies the request after the specified item changes.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        public void Modify(ServerSystemContext context, MonitoredItem monitoredItem)
        {
            // re-queue the last value.
            if (m_lastRequest != null)
            {
                QueueValue(context, m_lastRequest, monitoredItem);
            }
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
        /// Updates the request after adding/removing items.
        /// </summary>
        public void Update()
        {
            double samplingInterval = Double.MaxValue;
            List<int> propertyIds = new List<int>();

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
                
                // check for DA item.
                DaItemState item = handle.Node as DaItemState;

                if (item != null)
                {
                    int propertyId = DaItemAttributeToDaProperty(monitoredItem.AttributeId);

                    if (propertyId != -1 && !propertyIds.Contains(propertyId))
                    {
                        propertyIds.Add(propertyId);
                    }

                    continue;
                }
                
                // check for DA property.
                DaPropertyState daProperty = handle.Node as DaPropertyState;

                if (daProperty != null)
                {
                    if (!propertyIds.Contains(daProperty.PropertyId))
                    {
                        propertyIds.Add(daProperty.PropertyId);
                    }

                    continue;
                }
                
                // check for UA property.
                PropertyState uaProperty = handle.Node as PropertyState;

                if (uaProperty != null)
                {
                    int[] ids = UaPropertyToDaProperty(uaProperty.SymbolicName);

                    if (ids != null)
                    {
                        for (int jj = 0; jj < ids.Length; jj++)
                        {
                            if (!propertyIds.Contains(ids[jj]))
                            {
                                propertyIds.Add(ids[jj]);
                            }
                        }
                    }

                    continue;
                }
            }

            // update values.
            m_propertyIds = propertyIds.ToArray();
            m_samplingInterval = (int)samplingInterval;
            m_nextUpdateTime = DateTime.UtcNow;
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Maps a UA attribute id to a DA property id.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The DA property id. -1 if no mapping exists</returns>
        internal static int DaItemAttributeToDaProperty(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.Value:
                {
                    return Opc.Ua.Com.PropertyIds.Value;
                }

                case Attributes.Description:
                {
                    return  Opc.Ua.Com.PropertyIds.Description;
                }

                case Attributes.DataType:
                case Attributes.ValueRank:
                {
                    return  Opc.Ua.Com.PropertyIds.DataType;
                }

                case Attributes.AccessLevel:
                case Attributes.UserAccessLevel:
                {
                    return  Opc.Ua.Com.PropertyIds.AccessRights;
                }

                case Attributes.MinimumSamplingInterval:
                {
                    return  Opc.Ua.Com.PropertyIds.ScanRate;
                }
            }

            return -1;
        }

        /// <summary>
        /// Maps a UA property name to a set of DA property ids.
        /// </summary>
        /// <param name="symbolicName">The symbolic name for the property.</param>
        /// <returns>The list of matching DA property ids.</returns>
        internal static int[] UaPropertyToDaProperty(string symbolicName)
        {
            switch (symbolicName)
            {
                case Opc.Ua.BrowseNames.EURange: { return s_PropertyMapping_EURange; }
                case Opc.Ua.BrowseNames.InstrumentRange: { return s_PropertyMapping_InstrumentRange; }
                case Opc.Ua.BrowseNames.EngineeringUnits: { return s_PropertyMapping_EngineeringUnits; }
                case Opc.Ua.BrowseNames.EnumStrings: { return s_PropertyMapping_EnumStrings; }
                case Opc.Ua.BrowseNames.LocalTime: { return s_PropertyMapping_TimeZone; }
                case Opc.Ua.BrowseNames.TrueState: { return s_PropertyMapping_TrueState; }
                case Opc.Ua.BrowseNames.FalseState: { return s_PropertyMapping_FalseState; }
            }

            return null;
        }
        
        private static int[] s_PropertyMapping_EURange = new int[] {  Opc.Ua.Com.PropertyIds.HighEU,  Opc.Ua.Com.PropertyIds.LowEU };
        private static int[] s_PropertyMapping_InstrumentRange = new int[] {  Opc.Ua.Com.PropertyIds.HighIR,  Opc.Ua.Com.PropertyIds.LowIR };
        private static int[] s_PropertyMapping_EngineeringUnits = new int[] {  Opc.Ua.Com.PropertyIds.EngineeringUnits };
        private static int[] s_PropertyMapping_EnumStrings = new int[] {  Opc.Ua.Com.PropertyIds.EuInfo };
        private static int[] s_PropertyMapping_TimeZone = new int[] {  Opc.Ua.Com.PropertyIds.TimeZone };
        private static int[] s_PropertyMapping_TrueState = new int[] {  Opc.Ua.Com.PropertyIds.CloseLabel };
        private static int[] s_PropertyMapping_FalseState = new int[] {  Opc.Ua.Com.PropertyIds.OpenLabel };
        #endregion

        #region Private Fields
        private string m_itemId;
        private List<MonitoredItem> m_monitoredItems;
        private int m_samplingInterval;
        private int[] m_propertyIds;
        private ReadRequest m_lastRequest;
        private DateTime m_nextUpdateTime;
        #endregion
    }

    /// <summary>
    /// Stores a collection of write requests.
    /// </summary>
    internal class SubscribeRequestManager : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="SubscribeRequestManager"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="client">The COKM client wrapper.</param>
        /// <param name="propertySamplingInterval">The property sampling interval.</param>
        public SubscribeRequestManager(ServerSystemContext context, ComClient client, int propertySamplingInterval)
        {
            m_context = context;

            m_propertySamplingInterval = propertySamplingInterval;
            m_subscribedItems = new Dictionary<string, SubscribeItemRequest>();
            m_subscribedProperties = new Dictionary<string, SubscribePropertyRequest>();
            m_groups = new List<ComDaGroup>();
            m_monitoredItems = new Dictionary<uint, IMonitoredItem>();

            if (client == null)
            {
                LocaleId = ComUtils.LOCALE_SYSTEM_DEFAULT;
                UserIdentity = null;
                Key = String.Empty;
            }
            else
            {
                LocaleId = client.LocaleId;
                UserIdentity = client.UserIdentity;
                Key = client.Key;
            }
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
                    Utils.SilentDispose(m_propertyScanTimer);
                    m_propertyScanTimer = null;

                    for (int ii = 0; ii < m_groups.Count; ii++)
                    {
                        Utils.SilentDispose(m_groups[ii]);
                    }

                    m_groups.Clear();
                }
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The locale to use when creating the subscriptions.
        /// </summary>
        public int LocaleId { get; private set; }

        /// <summary>
        /// The user identity to use.
        /// </summary>
        public IUserIdentity UserIdentity { get; private set; }

        /// <summary>
        /// A key that combines the user identity and the locale.
        /// </summary>
        public string Key { get; private set; }

        /// <summary>
        /// Gets the item requests.
        /// </summary>
        /// <value>The item requests.</value>
        public List<SubscribeItemRequest> ItemRequests
        {
            get 
            {
                lock (m_lock)
                {
                    return new List<SubscribeItemRequest>(m_subscribedItems.Values);
                }
            }
        }

        /// <summary>
        /// Gets the item property requests.
        /// </summary>
        /// <value>The item property requests.</value>
        public List<SubscribePropertyRequest> PropertyRequests
        {
            get 
            {
                lock (m_lock)
                {
                    return new List<SubscribePropertyRequest>(m_subscribedProperties.Values);
                }
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Finds the item value request for the specifed item. 
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="create">if set to <c>true</c> a new request is created if it does not exist.</param>
        /// <returns>The subscribe item request.</returns>
        public SubscribeItemRequest FindItemRequest(string itemId, bool create)
        {
            lock (m_lock)
            {
                SubscribeItemRequest subscribedItem = null;

                if (!m_subscribedItems.TryGetValue(itemId, out subscribedItem))
                {
                    if (!create)
                    {
                        return null;
                    }

                    subscribedItem = new SubscribeItemRequest(itemId);
                    m_subscribedItems.Add(itemId, subscribedItem);
                }

                return subscribedItem;
            }
        }

        /// <summary>
        /// Finds the item properties request for the specifed item. 
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="create">if set to <c>true</c> a new request is created if it does not exist.</param>
        /// <returns>The subscribe property request.</returns>
        public SubscribePropertyRequest FindPropertyRequest(string itemId, bool create)
        {
            lock (m_lock)
            {
                SubscribePropertyRequest subscribedProperty = null;

                if (!m_subscribedProperties.TryGetValue(itemId, out subscribedProperty))
                {
                    if (!create)
                    {
                        return null;
                    }

                    subscribedProperty = new SubscribePropertyRequest(itemId);
                    m_subscribedProperties.Add(itemId, subscribedProperty);
                }

                return subscribedProperty;
            }
        }

        /// <summary>
        /// Recreates the items after a disconnect.
        /// </summary>
        public void RecreateItems()
        {
            lock (m_lock)
            {
                m_subscribedItems.Clear();
                m_subscribedProperties.Clear();

                Utils.SilentDispose(m_propertyScanTimer);
                m_propertyScanTimer = null;

                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    Utils.SilentDispose(m_groups[ii]);
                }

                m_groups.Clear();

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
            ComDaClientManager system = (ComDaClientManager)context.SystemHandle;
            ComDaClient client = (ComDaClient)system.GetLocalizedClient(this.UserIdentity, this.LocaleId);

            lock (m_lock)
            {
                if (monitoredItems != null)
                {
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

                        Add(handle.Node, monitoredItem);
                    }

                    ApplyGroupChanges(client);
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
            ComDaClientManager system = (ComDaClientManager)context.SystemHandle;
            ComDaClient client = (ComDaClient)system.GetLocalizedClient(this.UserIdentity, this.LocaleId);

            lock (m_lock)
            {
                if (monitoredItems != null)
                {
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

                        Modify(handle.Node, monitoredItem);
                    }

                    ApplyGroupChanges(client);
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
            ComDaClientManager system = (ComDaClientManager)context.SystemHandle;
            ComDaClient client = (ComDaClient)system.GetLocalizedClient(this.UserIdentity, this.LocaleId);

            lock (m_lock)
            {
                if (monitoredItems != null)
                {
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

                        Remove(handle.Node, monitoredItem);
                    }

                    ApplyGroupChanges(client);
                }
            }
        }

        /// <summary>
        /// Adds the monitored item to the collection.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        public void Add(NodeState source, MonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                m_monitoredItems.Add(monitoredItem.Id, monitoredItem);

                DaItemState item = source as DaItemState;

                if (item != null)
                {
                    Add(item, monitoredItem);
                    return;
                }

                DaPropertyState daProperty = source as DaPropertyState;

                if (daProperty != null)
                {
                    Add(daProperty, monitoredItem);
                    return;
                }

                PropertyState uaProperty = source as PropertyState;

                if (uaProperty != null)
                {
                    Add(uaProperty, monitoredItem);
                    return;
                }
            }
        }

        /// <summary>
        /// Modifies the monitored item to the collection.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        public void Modify(NodeState source, MonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                DaItemState item = source as DaItemState;

                if (item != null)
                {
                    Modify(item, monitoredItem);
                    return;
                }

                DaPropertyState daProperty = source as DaPropertyState;

                if (daProperty != null)
                {
                    Modify(daProperty, monitoredItem);
                    return;
                }

                PropertyState uaProperty = source as PropertyState;

                if (uaProperty != null)
                {
                    Modify(uaProperty, monitoredItem);
                    return;
                }
            }
        }

        /// <summary>
        /// Removes the monitored item from the collection.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        public void Remove(NodeState source, MonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                m_monitoredItems.Remove(monitoredItem.Id);

                DaItemState item = source as DaItemState;

                if (item != null)
                {
                    Remove(item, monitoredItem);
                    return;
                }

                DaPropertyState daProperty = source as DaPropertyState;

                if (daProperty != null)
                {
                    Remove(daProperty, monitoredItem);
                    return;
                }

                PropertyState uaProperty = source as PropertyState;

                if (uaProperty != null)
                {
                    Remove(uaProperty, monitoredItem);
                    return;
                }
            }
        }

        /// <summary>
        /// Removes the specified item request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>True of the request was removed.</returns>
        public bool Remove(SubscribeItemRequest request)
        {
            lock (m_lock)
            {
                if (request != null && request.ItemId != null)
                {
                    return m_subscribedItems.Remove(request.ItemId);
                }

                return false;
            }
        }

        /// <summary>
        /// Removes the specified property request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>True of the request was removed.</returns>
        public bool Remove(SubscribePropertyRequest request)
        {
            lock (m_lock)
            {
                if (request != null && request.ItemId != null)
                {
                    return m_subscribedProperties.Remove(request.ItemId);
                }

                return false;
            }
        }

        /// <summary>
        /// Assigns a request to a group.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="request">The request.</param>
        public void UpdateGroup(ComDaClient client, SubscribeItemRequest request)
        {
            lock (m_lock)
            {
                // check for empty request.
                if (request.MonitoredItems.Count == 0)
                {
                    return;
                }

                request.Update();

                // check if existing group can be used.
                if (request.Group != null)
                {
                    if (request.Group.ModifyItem(request.GroupItem, request.SamplingInterval, request.Deadband, request.Active))
                    {
                        if (request.GroupItem.Active != request.Active)
                        {
                            request.GroupItem.Active = request.Active;
                            request.GroupItem.ActiveChanged = true;
                        }

                        request.Group.SetMonitoredItems(request.GroupItem, request.MonitoredItems.ToArray());
                        return;
                    }
                }

                // clear link to existing group.
                request.Group = null;
                request.GroupItem = null;
                
                // assign to an existing group.
                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    ComDaGroup group = m_groups[ii];

                    request.GroupItem = group.CreateItem(request.ItemId, request.SamplingInterval, request.Deadband, request.Active);

                    if (request.GroupItem != null)
                    {
                        request.Group = group;
                        request.Group.SetMonitoredItems(request.GroupItem, request.MonitoredItems.ToArray());
                        return; 
                    }
                }

                // create a new group.
                request.Group = new ComDaGroup(client, true);
                request.GroupItem = request.Group.CreateItem(request.ItemId, request.SamplingInterval, request.Deadband, request.Active);
                request.Group.SetMonitoredItems(request.GroupItem, request.MonitoredItems.ToArray());
                m_groups.Add(request.Group);
            }
        }

        /// <summary>
        /// Applies any changes to the groups.
        /// </summary>
        public void ApplyGroupChanges(ComDaClient client)
        {
            List<ComDaGroup> groups = null;
            List<SubscribeItemRequest> items = null;

            lock (m_lock)
            {
                items = new List<SubscribeItemRequest>(m_subscribedItems.Values);

                // modify or remove items.
                for (int ii = 0; ii < items.Count; ii++)
                {
                    SubscribeItemRequest request = items[ii];

                    // remove unused requests.
                    if (request.MonitoredItems.Count == 0)
                    {
                        if (request.Group != null && request.GroupItem != null)
                        {
                            request.Group.RemoveItem(request.GroupItem);
                        }

                        Remove(request);
                        continue;
                    }
                }

                // add any new items to groups.
                for (int ii = 0; ii < items.Count; ii++)
                {
                    SubscribeItemRequest request = items[ii];

                    if (!request.Changed)
                    {
                        continue;
                    }

                    UpdateGroup(client, request);
                }

                groups = new List<ComDaGroup>(m_groups.Count);

                // update group on the server.
                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    if (m_groups[ii].ApplyChanges())
                    {
                        groups.Add(m_groups[ii]);
                    }
                    else
                    {
                        m_groups[ii].Delete();
                    }
                }

                m_groups = groups;

                // update request.
                for (int ii = 0; ii < items.Count; ii++)
                {
                    if (items[ii].Changed)
                    {
                        items[ii].ChangesComplete();
                    }
                }

                // modify or remove items.
                List<SubscribePropertyRequest> properties = new List<SubscribePropertyRequest>(m_subscribedProperties.Values);

                for (int ii = 0; ii < properties.Count; ii++)
                {
                    SubscribePropertyRequest request = properties[ii];

                    // remove unused requests.
                    if (request.MonitoredItems == null || request.MonitoredItems.Count == 0)
                    {
                        Remove(request);
                        continue;
                    }

                    request.ChangesComplete(m_propertySamplingInterval);
                }
                
                // check if the property scanner needs to be stopped/started.
                if (m_propertyScanTimer == null)
                {
                    if (m_subscribedProperties.Count > 0)
                    {
                        m_propertyScanTimer = new Timer(OnScanProperties, null, 0, m_propertySamplingInterval);
                    }
                }
                else
                {
                    if (m_subscribedProperties.Count == 0)
                    {
                        m_propertyScanTimer.Dispose();
                        m_propertyScanTimer = null;
                    }
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Called when the scan properties timer expires.
        /// </summary>
        private void OnScanProperties(object state)
        {
            try
            {
                ComDaClientManager system = (ComDaClientManager)m_context.SystemHandle;
                ComDaClient client = (ComDaClient)system.GetLocalizedClient(this.UserIdentity, this.LocaleId);

                // collect the list of properties that need reading.
                List<SubscribePropertyRequest> requests1 = new List<SubscribePropertyRequest>();
                List<ReadRequest> requests2 = new List<ReadRequest>();

                lock (m_lock)
                {
                    DateTime now = DateTime.UtcNow;

                    foreach (SubscribePropertyRequest request1 in m_subscribedProperties.Values)
                    {
                        if (request1.NextUpdateTime <= now)
                        {
                            requests1.Add(request1);
                            ReadRequest request2 = new ReadRequest(request1.ItemId);
                            request2.AddProperty(request1.PropertyIds);
                            requests2.Add(request2);
                        }
                    }
                }

                // read the properties from teh server.
                client.ReadPropertyValues(requests2);

                // update the monitored items.
                lock (m_lock)
                {
                    for (int ii = 0; ii < requests1.Count; ii++)
                    {
                        requests1[ii].QueueValues(m_context, requests2[ii]);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error scanning properties in DA COM server.");
            }
        }

        /// <summary>
        /// Adds a request for the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private void Add(DaItemState item, MonitoredItem monitoredItem)
        {
            if (monitoredItem.AttributeId == Attributes.Value)
            {
                SubscribeItemRequest request = FindItemRequest(item.ItemId, true);

                if (request != null)
                {
                    request.Add(monitoredItem);
                }
            }
            else
            {
                int propertyId = SubscribePropertyRequest.DaItemAttributeToDaProperty(monitoredItem.AttributeId);

                if (propertyId < 0)
                {
                    return;
                }

                SubscribePropertyRequest request = FindPropertyRequest(item.ItemId, true);

                if (request != null)
                {
                    request.Add(m_context, monitoredItem);
                }
            }
        }

        /// <summary>
        /// Adds the specified item property.
        /// </summary>
        /// <param name="item">The item property.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private void Add(DaPropertyState item, MonitoredItem monitoredItem)
        {
            if (monitoredItem.AttributeId != Attributes.Value)
            {
                return;
            }

            // check if the server supports access by item id for the property.
            if (!String.IsNullOrEmpty(item.Property.ItemId))
            {
                SubscribeItemRequest request = FindItemRequest(item.Property.ItemId, true);

                if (request != null)
                {
                    request.Add(monitoredItem);
                }
            }

            // must access with the property id.
            else
            {
                SubscribePropertyRequest request = FindPropertyRequest(item.ItemId, true);

                if (request != null)
                {
                    request.Add(m_context, monitoredItem);
                }
            }
        }

        /// <summary>
        /// Adds the specified UA property.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private void Add(PropertyState property, MonitoredItem monitoredItem)
        {
            if (monitoredItem.AttributeId != Attributes.Value)
            {
                return;
            }

            DaItemState item = property.Parent as DaItemState;

            if (item == null)
            {
                return;
            }

            int[] propertyIds = SubscribePropertyRequest.UaPropertyToDaProperty(property.SymbolicName);

            if (propertyIds == null)
            {
                return;
            }

            SubscribePropertyRequest request = FindPropertyRequest(item.ItemId, true);

            if (request != null)
            {
                request.Add(m_context, monitoredItem);
            }
        }

        /// <summary>
        /// Modifies a request for the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private void Modify(DaItemState item, MonitoredItem monitoredItem)
        {
            if (monitoredItem.AttributeId == Attributes.Value)
            {
                SubscribeItemRequest request = FindItemRequest(item.ItemId, false);

                if (request != null)
                {
                    request.Modify(monitoredItem);
                }
            }
            else
            {
                int propertyId = SubscribePropertyRequest.DaItemAttributeToDaProperty(monitoredItem.AttributeId);

                if (propertyId < 0)
                {
                    return;
                }

                SubscribePropertyRequest request = FindPropertyRequest(item.ItemId, false);

                if (request != null)
                {
                    request.Modify(m_context, monitoredItem);
                }
            }
        }

        /// <summary>
        /// Modifies the specified item property.
        /// </summary>
        /// <param name="item">The item property.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private void Modify(DaPropertyState item, MonitoredItem monitoredItem)
        {
            if (monitoredItem.AttributeId != Attributes.Value)
            {
                return;
            }

            // check if the server supports access by item id for the property.
            if (!String.IsNullOrEmpty(item.Property.ItemId))
            {
                SubscribeItemRequest request = FindItemRequest(item.Property.ItemId, false);

                if (request != null)
                {
                    request.Modify(monitoredItem);
                }
            }

            // must access with the property id.
            else
            {
                SubscribePropertyRequest request = FindPropertyRequest(item.ItemId, false);

                if (request != null)
                {
                    request.Modify(m_context, monitoredItem);
                }
            }
        }

        /// <summary>
        /// Modifies the specified UA property.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private void Modify(PropertyState property, MonitoredItem monitoredItem)
        {
            if (monitoredItem.AttributeId != Attributes.Value)
            {
                return;
            }

            DaItemState item = property.Parent as DaItemState;

            if (item == null)
            {
                return;
            }

            int[] propertyIds = SubscribePropertyRequest.UaPropertyToDaProperty(property.SymbolicName);

            if (propertyIds == null)
            {
                return;
            }

            SubscribePropertyRequest request = FindPropertyRequest(item.ItemId, false);

            if (request != null)
            {
                request.Modify(m_context, monitoredItem);
            }
        }

        /// <summary>
        /// Removes a request for the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private void Remove(DaItemState item, MonitoredItem monitoredItem)
        {
            if (monitoredItem.AttributeId == Attributes.Value)
            {
                SubscribeItemRequest request = FindItemRequest(item.ItemId, false);

                if (request != null)
                {
                    request.Remove(monitoredItem);
                }
            }
            else
            {
                int propertyId = SubscribePropertyRequest.DaItemAttributeToDaProperty(monitoredItem.AttributeId);

                if (propertyId < 0)
                {
                    return;
                }

                SubscribePropertyRequest request = FindPropertyRequest(item.ItemId, false);

                if (request != null)
                {
                    request.Remove(m_context, monitoredItem);

                    if (request.MonitoredItems.Count == 0)
                    {
                        m_subscribedProperties.Remove(item.ItemId);
                    }
                }
            }
        }

        /// <summary>
        /// Removes a request for the specified item property.
        /// </summary>
        /// <param name="item">The item property.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private void Remove(DaPropertyState item, MonitoredItem monitoredItem)
        {
            if (monitoredItem.AttributeId != Attributes.Value)
            {
                return;
            }

            // check if the server supports access by item id for the property.
            if (!String.IsNullOrEmpty(item.Property.ItemId))
            {
                SubscribeItemRequest request = FindItemRequest(item.Property.ItemId, false);

                if (request != null)
                {
                    request.Remove(monitoredItem);
                }
            }

            // must access with the property id.
            else
            {
                SubscribePropertyRequest request = FindPropertyRequest(item.ItemId, false);

                if (request != null)
                {
                    request.Remove(m_context, monitoredItem);

                    if (request.MonitoredItems.Count == 0)
                    {
                        m_subscribedProperties.Remove(item.ItemId);
                    }
                }
            }
        }

        /// <summary>
        /// Removes a request for the specified UA property.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        private void Remove(PropertyState property, MonitoredItem monitoredItem)
        {
            if (monitoredItem.AttributeId != Attributes.Value)
            {
                return;
            }

            DaItemState item = property.Parent as DaItemState;

            if (item == null)
            {
                return;
            }

            int[] propertyIds = SubscribePropertyRequest.UaPropertyToDaProperty(property.SymbolicName);

            if (propertyIds == null)
            {
                return;
            }

            SubscribePropertyRequest request = FindPropertyRequest(item.ItemId, false);

            if (request != null)
            {
                request.Remove(m_context, monitoredItem);

                if (request.MonitoredItems.Count == 0)
                {
                    m_subscribedProperties.Remove(item.ItemId);
                }
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private Dictionary<string,SubscribeItemRequest> m_subscribedItems;
        private Dictionary<string,SubscribePropertyRequest> m_subscribedProperties;
        private List<ComDaGroup> m_groups;
        private Timer m_propertyScanTimer;
        private int m_propertySamplingInterval;
        private ServerSystemContext m_context;
        private Dictionary<uint, IMonitoredItem> m_monitoredItems;
        #endregion
    }
}
