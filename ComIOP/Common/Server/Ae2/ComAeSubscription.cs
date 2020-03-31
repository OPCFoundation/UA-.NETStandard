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
using OpcRcw.Ae;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// A base class for classes that implement an OPC COM specification.
    /// </summary>
    public class ComAe2Subscription : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComHdaBrowser"/> class.
        /// </summary>
        public ComAe2Subscription(
            ComAe2Proxy server, 
            ComAe2ProxyConfiguration configuration,
            ComAeNamespaceMapper mapper,
            ComAe2Browser browser, 
            AeConditionManager conditionManager)
        {
            m_server = server;
            m_configuration = configuration;
            m_mapper = mapper;
            m_browser = browser;
            m_conditionManager = conditionManager;
            m_filter = new AeEventFilter(m_mapper);
            m_queue = new Queue<AeEvent>();
            m_notifiers = new NodeIdDictionary<MonitoredItem>();
            m_sourceNodes = new List<NodeId>();

            // set a default filters.
            m_filter.SetFilter(Constants.ALL_EVENTS, 0, UInt16.MaxValue, null, null); 
            UpdateAreaFilter(null);
            UpdateSourceFilter(null);
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
            if (!m_disposed)
            {
                if (disposing)
                {
                    lock (m_lock)
                    {
                        if (m_subscription != null)
                        {
                            m_subscription.Dispose();
                            m_subscription = null;
                        }

                        if (m_callbackTimer != null)
                        {
                            m_callbackTimer.Dispose();
                            m_callbackTimer = null;
                        }
                    }

                    m_notifiers = null;
                    m_browser = null;
                    m_mapper = null;
                    m_callback = null;
                    m_server = null;
                }

                m_disposed = true;
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
        /// Gets the current active state.
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// Gets the maximum callback size.
        /// </summary>
        public uint MaxSize { get; set; }

        /// <summary>
        /// Gets the buffer time.
        /// </summary>
        public uint BufferTime { get; set; }

        /// <summary>
        /// Gets the client handle.
        /// </summary>
        public uint ClientHandle { get; set; }

        /// <summary>
        /// Gets the keep alive.
        /// </summary>
        public uint KeepAlive { get; private set; }

        /// <summary>
        /// Gets the last update time.
        /// </summary>
        public DateTime LastUpdateTime 
        { 
            get 
            {
                DateTime lastUpdateTime;

                lock (m_lock)
                {
                    lastUpdateTime = m_lastUpdateTime;
                }

                return lastUpdateTime;
            }
        }
        
        /// <summary>
        /// Gets the actual maximum callback size.
        /// </summary>
        public uint ActualMaxSize
        {
            get
            {
                if (m_subscription != null)
                {
                    return m_subscription.MaxNotificationsPerPublish;
                }

                return MaxSize;
            }
        }

        /// <summary>
        /// Gets the actual buffer time.
        /// </summary>
        public uint ActualBufferTime
        {
            get
            {
                if (m_subscription != null)
                {
                    return (uint)m_subscription.CurrentPublishingInterval;
                }

                return (uint)Math.Max(BufferTime, 1000);
            }
        }

        /// <summary>
        /// Sets the keep alive rate.
        /// </summary>
        public uint SetKeepAlive(uint keepAlive)
        {
            lock (m_lock)
            {
                if (keepAlive > 0)
                {
                    uint sampleTime = ActualBufferTime;
                    uint ratio = (uint)Math.Truncate(((double)keepAlive)/sampleTime);
                    keepAlive = Math.Max(sampleTime*ratio, sampleTime);
                }

                KeepAlive = keepAlive;
                m_keepAliveCount = 1;
            }

            return KeepAlive;
        }
        
        /// <summary>
        /// Sets the callback.
        /// </summary>
        /// <param name="callback">The callback.</param>
        public void SetCallback(IComAeEventCallback callback)
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                if (m_callback != null)
                {
                    m_callback.Dispose();
                    m_callback = null;
                }

                m_callback = callback;
            }
        }

        /// <summary>
        /// Applies any changes to the subscription.
        /// </summary>
        public void ApplyChanges()
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                if (m_subscription == null)
                {
                    CreateSubscription();
                }
                else
                {
                    ModifySubscription();
                }

                if (m_callbackTimer != null)
                {
                    m_callbackTimer.Dispose();
                    m_callbackTimer = null;
                }
                
                if (Active)
                {
                    // adjust keep alive to reflect current settings.
                    SetKeepAlive(KeepAlive);

                    // start a callabck thread.
                    m_callbackTimer = new Timer(DoProcessQueue, null, 0, (long)ActualBufferTime);
                }
            }
        }

        /// <summary>
        /// Updates the object after a reconnect.
        /// </summary>
        public void OnSessionReconected(Session session)
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                foreach (Subscription subscription in session.Subscriptions)
                {
                    if (Object.ReferenceEquals(this, subscription.Handle))
                    {
                        m_subscription = subscription;
                        m_notifiers = new NodeIdDictionary<MonitoredItem>();

                        foreach (MonitoredItem monitoredItem in subscription.MonitoredItems)
                        {
                            m_notifiers[monitoredItem.StartNodeId] = monitoredItem;
                        }

                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Sets the event filter.
        /// </summary>
        public void SetFilter(
            int eventTypes,
            ushort lowSeverity,
            ushort highSeverity,
            uint[] categoryIds,
            string[] areas,
            string[] sources)
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                // validate event types.
                if (eventTypes <= 0 || eventTypes > 0x7)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }

                // validate severity.
                if (lowSeverity == 0 || highSeverity > 1000 || lowSeverity > highSeverity)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }

                // validate categories.
                if (categoryIds != null)
                {
                    for (int ii = 0; ii < categoryIds.Length; ii++)
                    {
                        if (m_mapper.GetCategory(categoryIds[ii]) == null)
                        {
                            throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                        }
                    }
                }

                // validate areas.
                List<NodeId> areasToUse = new List<NodeId>();

                if (areas != null)
                {
                    for (int ii = 0; ii < areas.Length; ii++)
                    {
                        List<NodeId> areaIds = m_browser.SearchByQualifiedName(areas[ii], true);

                        if (areaIds.Count == 0)
                        {
                            throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                        }

                        areasToUse.AddRange(areaIds);
                    }
                }

                // validate sources.
                List<NodeId> sourcesToUse = new List<NodeId>();

                if (sources != null)
                {
                    for (int ii = 0; ii < sources.Length; ii++)
                    {
                        List<NodeId> sourceIds = m_browser.SearchByQualifiedName(sources[ii], false);

                        if (sourceIds.Count == 0)
                        {
                            throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                        }

                        sourcesToUse.AddRange(sourceIds);
                    }
                }

                m_areas = areas;
                m_sources = sources;

                UpdateAreaFilter(areasToUse);
                UpdateSourceFilter(sourcesToUse);
                m_filter.SetFilter(eventTypes, lowSeverity, highSeverity, categoryIds, m_sourceNodes);
            }
        }

        /// <summary>
        /// Updates the list of area filters.
        /// </summary>
        private void UpdateAreaFilter(List<NodeId> areas)
        {
            // check if monitoring all events.
            if (areas == null || areas.Count == 0)
            {
                MonitoredItem monitoredItem = null;

                if (!m_notifiers.TryGetValue(Opc.Ua.ObjectIds.Server, out monitoredItem))
                {
                    monitoredItem = CreateMonitoredItem(Opc.Ua.ObjectIds.Server);
                }

                m_notifiers.Clear();
                m_notifiers[Opc.Ua.ObjectIds.Server] = monitoredItem;
                return;
            }

            // build table of areas to monitor.
            NodeIdDictionary<MonitoredItem> notifiers = new NodeIdDictionary<MonitoredItem>();

            // map all of the area search strings onto NodeIds for notifiers.
            for (int ii = 0; ii < areas.Count; ii++)
            {
                NodeId areaId = areas[ii];

                // check for existing item.
                MonitoredItem monitoredItem = null;

                if (m_notifiers.TryGetValue(areaId, out monitoredItem))
                {
                    notifiers[areaId] = monitoredItem;
                    continue;
                }

                // check for new item.
                if (!notifiers.ContainsKey(areaId))
                {
                    notifiers[areaId] = CreateMonitoredItem(areaId);
                }
            }

            // mark unused items for deletion.
            foreach (MonitoredItem monitoredItem in m_notifiers.Values)
            {
                if (!notifiers.ContainsKey(monitoredItem.StartNodeId))
                {
                    m_subscription.RemoveItem(monitoredItem);
                }
            }

            m_notifiers = notifiers;
        }

        /// <summary>
        /// Updates the list of source filters.
        /// </summary>
        private void UpdateSourceFilter(List<NodeId> sources)
        {
            m_sourceNodes.Clear();

            if (sources != null && sources.Count > 0)
            {
                for (int ii = 0; ii < sources.Count; ii++)
                {
                    NodeId sourceId = sources[ii];

                    if (!m_sourceNodes.Contains(sourceId))
                    {
                        m_sourceNodes.Add(sourceId);
                    }
                }
            }
        }

        /// <summary>
        /// Creates a new monitored item and adds it to the subscription.
        /// </summary>
        private MonitoredItem CreateMonitoredItem(NodeId notifierId)
        {
            MonitoredItem monitoredItem = new MonitoredItem();

            monitoredItem.DisplayName = null;
            monitoredItem.StartNodeId = notifierId;
            monitoredItem.RelativePath = null;
            monitoredItem.NodeClass = NodeClass.Object;
            monitoredItem.AttributeId = Attributes.EventNotifier;
            monitoredItem.IndexRange = null;
            monitoredItem.Encoding = null;
            monitoredItem.MonitoringMode = MonitoringMode.Reporting;
            monitoredItem.SamplingInterval = 0;
            monitoredItem.QueueSize = UInt32.MaxValue;
            monitoredItem.DiscardOldest = true;

            return monitoredItem;
        }

        /// <summary>
        /// Get the event filter parameters.
        /// </summary>
        public void GetFilter(
            out int eventTypes,
            out ushort lowSeverity,
            out ushort highSeverity,
            out uint[] categoryIds,
            out string[] areas,
            out string[] sources)
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                eventTypes = m_filter.EventTypes;
                lowSeverity = m_filter.LowSeverity;
                highSeverity = m_filter.HighSeverity;
                categoryIds = m_filter.RequestedCategoryIds;
                areas = m_areas;
                sources = m_sources;
            }
        }

        /// <summary>
        /// Selects the attributes for the event category.
        /// </summary>
        public void SelectAttributes(
            uint categoryId,
            uint[] attributeIds)
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                if (!m_filter.SelectAttributes(categoryId, attributeIds))
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }
            }
        }

        /// <summary>
        /// Returns the currently selected attributes.
        /// </summary>
        public uint[] GetSelectedAttributes(uint categoryId)
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                if (m_mapper.GetCategory(categoryId) == null)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }
                
                uint[] attributeIds = null;

                if (m_filter.RequestedAttributeIds != null)
                {
                    m_filter.RequestedAttributeIds.TryGetValue(categoryId, out attributeIds);
                }

                return attributeIds;
            }
        }

        /// <summary>
        /// Initiates of refresh of all conditions.
        /// </summary>
        public void Refresh()
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                if (m_refreshQueue != null)
                {
                    throw ComUtils.CreateComException(ResultIds.E_BUSY);
                }

                m_refreshQueue = new Queue<AeEvent>();

                // turn on publishing first.
                if (!Active)
                {
                    m_subscription.SetPublishingMode(true);
                    List<MonitoredItem> itemsToUpdate = new List<MonitoredItem>(m_notifiers.Values);
                    m_subscription.SetMonitoringMode(MonitoringMode.Reporting, itemsToUpdate);
                }

                m_subscription.ConditionRefresh();
            }
        }

        /// <summary>
        /// Cancels a previous refresh.
        /// </summary>
        public void CancelRefresh()
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                if (m_refreshQueue == null)
                {
                    throw ComUtils.CreateComException(ResultIds.E_FAIL);
                }

                m_refreshQueue = null;

                // send an empty callback.
                ThreadPool.QueueUserWorkItem(DoRefresh, null);

                // turn off publishing when done.
                if (!Active)
                {
                    m_subscription.SetPublishingMode(false);
                    List<MonitoredItem> itemsToUpdate = new List<MonitoredItem>(m_notifiers.Values);
                    m_subscription.SetMonitoringMode(MonitoringMode.Disabled, itemsToUpdate);
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates the subscription.
        /// </summary>
        private void CreateSubscription()
        {
            if (m_subscription != null)
            {
                m_subscription.Dispose();
                m_subscription = null;
            }

            // get the current session.
            Session session = m_server.Session;

            if (session == null)
            {
                return;
            }

            // create the subscription.
            m_subscription = new Subscription();
            m_subscription.PublishingEnabled = Active;
            m_subscription.PublishingInterval = (int)Math.Max(BufferTime, 1000);
            m_subscription.KeepAliveCount = (uint)Math.Max(Math.Ceiling(((double)KeepAlive)/m_subscription.PublishingInterval), 10);
            m_subscription.LifetimeCount = m_subscription.KeepAliveCount*3;
            m_subscription.MaxNotificationsPerPublish = MaxSize;
            m_subscription.TimestampsToReturn = TimestampsToReturn.Neither;
            m_subscription.Priority = 0;
            m_subscription.FastEventCallback = OnEventNotification;
            m_subscription.DisableMonitoredItemCache = true;

            session.AddSubscription(m_subscription);
            m_subscription.Create();

            // update the monitored items.
            EventFilter filter = m_filter.GetFilter();
            MonitoringMode monitoringMode = (Active)?MonitoringMode.Reporting:MonitoringMode.Disabled;

            foreach (MonitoredItem monitoredItem in m_notifiers.Values)
            {
                monitoredItem.Filter = filter;
                monitoredItem.MonitoringMode = monitoringMode;
                m_subscription.AddItem(monitoredItem);
            }

            m_subscription.ApplyChanges();
        }

        /// <summary>
        /// Modifies a subscription.
        /// </summary>
        private void ModifySubscription()
        {
            // turn off publishing first.
            if (!Active && m_subscription.CurrentPublishingEnabled)
            {
                m_subscription.SetPublishingMode(false);
            }

            m_subscription.PublishingInterval = (int)Math.Max(BufferTime, 1000);
            m_subscription.KeepAliveCount = (uint)Math.Max(Math.Ceiling(((double)KeepAlive)/m_subscription.PublishingInterval), 10);
            m_subscription.LifetimeCount = m_subscription.KeepAliveCount * 3;
            m_subscription.MaxNotificationsPerPublish = MaxSize;
            m_subscription.TimestampsToReturn = TimestampsToReturn.Neither;
            m_subscription.Priority = 0;
            m_subscription.Modify();

            // update the monitored items.
            EventFilter filter = m_filter.GetFilter();
            MonitoringMode monitoringMode = (Active)?MonitoringMode.Reporting:MonitoringMode.Disabled;

            List<MonitoredItem> itemsToUpdate = new List<MonitoredItem>();

            foreach (MonitoredItem monitoredItem in m_notifiers.Values)
            {
                monitoredItem.Filter = filter;

                if (!monitoredItem.Created)
                {
                    monitoredItem.MonitoringMode = monitoringMode;
                    m_subscription.AddItem(monitoredItem);
                }
                else
                {
                    if (monitoredItem.MonitoringMode != monitoringMode)
                    {
                        itemsToUpdate.Add(monitoredItem);
                    }
                }
            }

            m_subscription.ApplyChanges();
            m_subscription.SetMonitoringMode(monitoringMode, itemsToUpdate);

            // turn on publishing last.
            if (Active && !m_subscription.CurrentPublishingEnabled)
            {
                m_subscription.SetPublishingMode(true);
            }
        }

        private void OnEventNotification(Subscription subscription, EventNotificationList notification, IList<string> stringTable)
        {
            try
            {
                // check if disposed.
                if (m_disposed)
                {
                    return;
                }

                // check if session still active.
                Session session = m_server.Session;

                if (session == null || !session.Connected)
                {
                    return;
                }

                // check if events are being reported.
                if (m_callback == null)
                {
                    return;
                }

                lock (m_lock)
                {
                    foreach (EventFieldList e in notification.Events)
                    {
                        // translate the notification and send the response.
                        AeEvent e2 = m_filter.TranslateNotification(m_server.Session, e);

                        if (e2 != null)
                        {
                            // check if refresh has started.
                            if (e2.EventType == Opc.Ua.ObjectTypeIds.RefreshStartEventType)
                            {
                                m_refreshInProgress = true;
                                continue;
                            }

                            // check if refresh has ended.
                            if (e2.EventType == Opc.Ua.ObjectTypeIds.RefreshEndEventType)
                            {
                                m_refreshInProgress = false;

                                // turn off publishing if the subscription is not active,
                                if (!Active)
                                {
                                    m_subscription.SetPublishingMode(false);
                                    List<MonitoredItem> itemsToUpdate = new List<MonitoredItem>(m_notifiers.Values);
                                    m_subscription.SetMonitoringMode(MonitoringMode.Disabled, itemsToUpdate);
                                }

                                if (m_refreshQueue != null)
                                {
                                    ThreadPool.QueueUserWorkItem(DoRefresh, m_refreshQueue);
                                }

                                continue;
                            }

                            // cache any conditions requiring acknowledgement.
                            m_conditionManager.ProcessEvent(e2);

                            // queue on refresh.
                            if (m_refreshInProgress)
                            {
                                if (m_refreshQueue != null)
                                {
                                    m_refreshQueue.Enqueue(e2);
                                }

                                continue;
                            }

                            // queue the event.
                            if (Active)
                            {
                                lock (m_queue)
                                {
                                    m_queue.Enqueue(e2);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Error processing event callback.");
            }
        }

        /// <summary>
        /// Sends the queued events in blocks.
        /// </summary>
        private void SendInBlocks(IComAeEventCallback callback, Queue<AeEvent> events, uint blockSize, bool refreshFlag)
        {
            if (callback == null)
            {
                return;
            }

            if (blockSize == 0)
            {
                blockSize = (uint)events.Count;
            }

            while (events.Count > 0)
            {
                List<ONEVENTSTRUCT> eventsInBlock = new List<ONEVENTSTRUCT>();

                try
                {
                    for (int ii = 0; ii < blockSize && events.Count > 0; ii++)
                    {
                        // warning - Translate() allocates unmanaged memory that is deleted in the finally block.
                        eventsInBlock.Add(Translate(events.Dequeue()));
                    }

                    // invoke callback.
                    callback.OnEvent(
                        ClientHandle,
                        refreshFlag,
                        (refreshFlag)?events.Count == 0:false,
                        eventsInBlock.ToArray());
                }
                finally
                {
                    // must deallocate attributes on exit.
                    for (int ii = 0; ii < eventsInBlock.Count; ii++)
                    {
                        IntPtr pAttributes = eventsInBlock[ii].pEventAttributes;
                        ComUtils.GetVARIANTs(ref pAttributes, eventsInBlock[ii].dwNumEventAttrs, true);
                    }
                }
            }
        }

        /// <summary>
        /// Cslled to send refresh events to client.
        /// </summary>
        private void DoRefresh(object state)
        {
            try
            {
                // check if disposed.
                if (m_disposed)
                {
                    return;
                }

                // send a dummy callback after a cancel.
                if (state == null)
                {
                    IComAeEventCallback callback = m_callback;

                    if (callback != null)
                    {
                        callback.OnEvent(
                            ClientHandle,
                            true,
                            true,
                            null);
                    }

                    return;
                }

                Queue<AeEvent> eventsToSend = null;

                lock (m_lock)
                {
                    // check if refresh has been cancelled.
                    if (!Object.ReferenceEquals(state, m_refreshQueue))
                    {
                        return;
                    }

                    // clear the queue to indicate operation is complete.
                    eventsToSend = m_refreshQueue;
                    m_refreshQueue = null;
                }

                // send the events to client.
                SendInBlocks(m_callback, eventsToSend, MaxSize, true);
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Error processing refresh callback.");
            }
        }

        /// <summary>
        /// Called send events to client.
        /// </summary>
        private void DoProcessQueue(object state)
        {
            Queue<AeEvent> events = new Queue<AeEvent>();

            try
            {
                // check if disposed.
                if (m_disposed)
                {
                    return;
                }

                // process the queue.
                lock (m_queue)
                {
                    while (m_queue.Count > 0)
                    {
                        events.Enqueue(m_queue.Dequeue());
                    }
                }
                
                // check if time for a keep alive.
                lock (m_lock)
                {
                    // check if time for keep alive.
                    if (events.Count == 0)
                    {
                        if (KeepAlive == 0 || m_lastUpdateTime.AddMilliseconds(KeepAlive*m_keepAliveCount) > DateTime.UtcNow)
                        {
                            return;
                        }
                    }

                    // no keep alives if not active.
                    if (!Active)
                    {
                        return;
                    }

                    // update counters.
                    if (events.Count > 0)
                    {
                        m_lastUpdateTime = DateTime.UtcNow;
                        m_keepAliveCount = 1;
                    }
                    else
                    {
                        m_keepAliveCount++;
                    }
                }

                // send a keep alive callback.
                if (events.Count == 0)
                {
                    IComAeEventCallback callback = m_callback;

                    if (callback != null)
                    {
                        callback.OnEvent(
                            ClientHandle,
                            false,
                            false,
                            null);
                    }

                    return;
                }

                // send the events to client.
                SendInBlocks(m_callback, events, MaxSize, false);
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Error processing event callback.");
            }
        }

        /// <summary>
        /// Marshals an event for return to the client.
        /// </summary>
        private OpcRcw.Ae.ONEVENTSTRUCT Translate(AeEvent e)
        {
            OpcRcw.Ae.ONEVENTSTRUCT e2 = new ONEVENTSTRUCT();

            e2.wNewState = 0;
            e2.wChangeMask = 0xFF;
            e2.szSource = e.SourceName;
            e2.wQuality = ComUtils.GetQualityCode(e.Quality);
            e2.dwEventType = e.Category.EventType;
            e2.dwEventCategory = (int)e.Category.LocalId;
            e2.bAckRequired = 0;
            e2.dwSeverity = e.Severity;
            e2.ftTime = ComUtils.GetFILETIME(e.Time);
            e2.szMessage = (e.Message != null) ? e.Message.Text : null;
            e2.szActorID = e.AuditUserId;
            e2.dwCookie = e.Cookie;

            if (e.AttributeValues != null && e.AttributeValues.Length > 0)
            {
                e2.dwNumEventAttrs = e.AttributeValues.Length;
                e2.pEventAttributes = ComUtils.GetVARIANTs(e.AttributeValues, true);
            }

            if ((e2.dwEventType & OpcRcw.Ae.Constants.CONDITION_EVENT) != 0)
            {
                e2.szConditionName = e.ConditionName;
                e2.ftActiveTime = ComUtils.GetFILETIME(e.ActiveTime);
                e2.bAckRequired = (e.AckedState)?0:1;

                // set the condition state.
                e2.wNewState = 0;

                if (e.EnabledState)
                {
                    e2.wNewState |= OpcRcw.Ae.Constants.CONDITION_ENABLED;
                }

                if (e.AckedState)
                {
                    e2.wNewState |= OpcRcw.Ae.Constants.CONDITION_ACKED;
                }

                if (e.ActiveState)
                {
                    e2.wNewState |= OpcRcw.Ae.Constants.CONDITION_ACTIVE;
                }

                // set the subcondition if available.
                if (!LocalizedText.IsNullOrEmpty(e.LowState))
                {
                    e2.szSubconditionName = e.LowState.Text;
                }

                if (!LocalizedText.IsNullOrEmpty(e.HighState))
                {
                    e2.szSubconditionName = e.HighState.Text;
                }

                if (!LocalizedText.IsNullOrEmpty(e.LowLowState))
                {
                    e2.szSubconditionName = e.LowLowState.Text;
                }

                if (!LocalizedText.IsNullOrEmpty(e.HighHighState))
                {
                    e2.szSubconditionName = e.HighHighState.Text;
                }

                if (!LocalizedText.IsNullOrEmpty(e.LimitState))
                {
                    e2.szSubconditionName = e.LimitState.Text;
                }
            }

            if (e2.szMessage == null) e2.szMessage = String.Empty;
            if (e2.szSource == null) e2.szSource = String.Empty;
            if (e2.szConditionName == null) e2.szConditionName = String.Empty;
            if (e2.szSubconditionName == null) e2.szSubconditionName = String.Empty;
            if (e2.szActorID == null) e2.szActorID = String.Empty;

            return e2;
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private bool m_disposed;
        private IntPtr m_handle;
        private ComAe2Proxy m_server;
        private ComAe2ProxyConfiguration m_configuration;
        private ComAeNamespaceMapper m_mapper;
        private ComAe2Browser m_browser;
        private AeConditionManager m_conditionManager;
        private IComAeEventCallback m_callback;
        private AeEventFilter m_filter;
        private string[] m_areas;
        private string[] m_sources;
        private NodeIdDictionary<MonitoredItem> m_notifiers;
        private List<NodeId> m_sourceNodes;
        private Subscription m_subscription;
        private Queue<AeEvent> m_queue;
        private bool m_refreshInProgress;
        private Queue<AeEvent> m_refreshQueue;
        private Timer m_callbackTimer;
        private DateTime m_lastUpdateTime;
        private uint m_keepAliveCount;
        #endregion
	}
}
