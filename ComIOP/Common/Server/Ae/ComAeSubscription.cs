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
using System.Runtime.InteropServices;
using System.Threading;
using OpcRcw.Ae;
using OpcRcw.Comn;

namespace Opc.Ua.Com.Server.Ae
{
    /// <summary>
    /// Implements COM-AE Subscription class
    /// </summary>
    public class Subscription :
        ConnectionPointContainer,
        IDisposable,
        IOPCEventSubscriptionMgt,
        IOPCEventSubscriptionMgt2
    {
        /// <summary>
        /// Initializes the object with the default values
        /// </summary>
        /// <param name="server"></param>
        public Subscription(ComAeProxy server)
        {
            RegisterInterface(typeof(OpcRcw.Ae.IOPCEventSink).GUID);
            
            m_server = server;
            m_bCancelRefresh = false;
            m_ftLastUpdate = DateTime.Now;
            m_bActive = false;
            m_dwBufferTime = DefaultBufferTime;
            m_dwMaxSize = DefaultMaxCallbackSize;
            m_hClientSubscription = 0;
            m_dwEventType = Constants.ALL_EVENTS;
            m_dwLowSeverity = 1;
            m_dwHighSeverity = 1000;
            m_bCancelRefresh = false;
            m_subscription = null;
            m_MinimumKeepAliveTime = server.KeepAliveInterval;
        }

	    #region IDisposable Members
        /// <summary>
        /// The finializer implementation.
        /// </summary>
        ~Subscription()
        {
            Dispose(false);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            UnregisterInterface(typeof(OpcRcw.Ae.IOPCEventSink).GUID);
            
            // Remove monitored items added for this subscription
            if (m_AreaVector.Count > 0)
                m_server.RemoveMonitoredItems(m_AreaVector);

            if (m_server != null)
            {
                m_server.SubscriptionListRemove(this);
            }

            if (m_server != null)
            {
                ComUtils.ReleaseServer(m_server);
	            m_server = null;
            }
        }
        #endregion

        #region IOPCEventSubscriptionMgt Members

        /// <summary>
        /// Sets the filtering criteria to be used for the event subscription.
        /// </summary>
        /// <param name="dwEventType">Bit mask specifying which event types are of interest</param>
        /// <param name="dwNumCategories">Length of array of event categories. A length of 0 indicates all categories should be included in the filter.</param>
        /// <param name="pdwEventCategories">Array of event categories of interest.</param>
        /// <param name="dwLowSeverity">Lowest severity of interest (inclusive).</param>
        /// <param name="dwHighSeverity">Highest severity of interest (inclusive).</param>
        /// <param name="dwNumAreas">Length of array of areas. A length of 0 indicates all areas should be included in the filter.</param>
        /// <param name="pszAreaList">Array of process area strings of interest - only events or conditions in these areas will be reported.</param>
        /// <param name="dwNumSources">Length of array of event sources. A length of 0 indicates all sources should be included in the filter.</param>
        /// <param name="pszSourceList">Array of event sources of interest - only events from these sources will be reported.</param>
        public void SetFilter(int dwEventType, int dwNumCategories, int[] pdwEventCategories, int dwLowSeverity, int dwHighSeverity, int dwNumAreas, string[] pszAreaList, int dwNumSources, string[] pszSourceList)
        {
            try
            {
                if (dwEventType == 0 |
                    dwEventType > OpcRcw.Ae.Constants.ALL_EVENTS |
                    pdwEventCategories.Rank > 1 | dwNumCategories != pdwEventCategories.Length |
                    pszAreaList.Rank > 1 | dwNumAreas != pszAreaList.Length |
                    pszSourceList.Rank > 1 | dwNumSources != pszSourceList.Length |
                    dwLowSeverity < 1 | dwLowSeverity > 1000 | dwHighSeverity > 1000 | dwHighSeverity < 1 |
                    dwLowSeverity > dwHighSeverity)
                {
                    throw ComUtils.CreateComException("SetFilter", ResultIds.E_INVALIDARG);
                }

                if (dwNumCategories != 0)
                {
                    for (int i = 0; i < dwNumCategories; i++)
                    {
                        // Make sure we are passed a valid dwEventCategory
                        NodeId catNodeId = m_server.FindEventCatNodeId(pdwEventCategories[i]);
                        if (catNodeId == null)
                            throw ComUtils.CreateComException("SetFilter", ResultIds.E_INVALIDARG);
                    }
                }
                m_server.RemoveMonitoredItems(m_AreaVector);
                if (dwNumAreas != 0)
                {
                    List<string> szAreas = new List<string>();
                    for (int i = 0; i < dwNumAreas; i++)
                    {
                        szAreas.Add(pszAreaList[i]);
                    }
                    if (szAreas.Count > 0)
                        m_server.AddMonitoredItems(szAreas);
                }

                if (dwNumSources != 0)
                {
                    List<string> szSources = new List<string>();
                    for (int i = 0; i < dwNumSources; i++)
                    {
                         int wildCardLocation = 0;
                         if ((wildCardLocation = pszSourceList[i].IndexOfAny(new char[] { '*', '?', '#', '[', ']', '!', '-' })) != -1)
                         {
                             // The string contains wildcards
                             List<string> items = m_server.ProcessWildCardAreaName(pszSourceList[i], wildCardLocation);
                             if (items.Count == 0)
                             {
                                 throw ComUtils.CreateComException("SetFilter", ResultIds.E_INVALIDARG);
                             }
                             foreach (string item in items)
                                 szSources.Add(item);
                         }
                         else
                             szSources.Add(pszSourceList[i]);
                    }

                     //Translate the fully qualified source name to NodeId
                    BrowsePathResultCollection results = m_server.GetBrowseTargets(szSources);
                    for (int i = 0; i < dwNumSources; i++)
                    {
                        if (StatusCode.IsBad(results[i].StatusCode))
                        {
                            throw ComUtils.CreateComException("SetFilter", ResultIds.E_INVALIDARG);
                        }
                    }
                }
           
                m_dwEventType = dwEventType;
                m_dwLowSeverity = dwLowSeverity;
                m_dwHighSeverity = dwHighSeverity;
                m_EventCategoryVector.Clear();
                if (dwNumCategories != 0)
                {
                    for (int i = 0; i < dwNumCategories; i++)
                    {
                      m_EventCategoryVector.AddUnique(pdwEventCategories[i]);
                    }
                }

                m_server.RemoveMonitoredItems(m_AreaVector);
                m_AreaVector.Clear();
                if (dwNumAreas != 0)
                {
                    for (int i = 0; i < dwNumAreas; i++)
                    {
                        m_AreaVector.AddUnique(pszAreaList[i]);
                    }
                }

                m_SourceVector.Clear();
                if (dwNumSources != 0)
                {
                    for (int i = 0; i < dwNumSources; i++)
                    {
                        m_SourceVector.AddUnique(pszSourceList[i]);
                    }
                }

            }
            catch (COMException e)
            {
                throw ComUtils.CreateComException(e);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in SetFilter");
                throw ComUtils.CreateComException(e);
            }
        }

        /// <summary>
        /// Returns the filter currently in use for event subscriptions.
        /// </summary>
        /// <param name="pdwEventType">Bit map specifying which event types are of allowed through the filter</param>
        /// <param name="pdwNumCategories">Length of the event category array returned.</param>
        /// <param name="ppdwEventCategories">Array of event categories for the filter.</param>
        /// <param name="pdwLowSeverity">Lowest severity allowed through filter.</param>
        /// <param name="pdwHighSeverity">Highest severity allowed through filter.</param>
        /// <param name="pdwNumAreas">Length of the area list array returned.</param>
        /// <param name="ppszAreaList">List of process areas for the filter.</param>
        /// <param name="pdwNumSources">Length of the event source list returned.</param>
        /// <param name="ppszSourceList">List of sources for the filter.</param>
        public void GetFilter(out int pdwEventType, out int pdwNumCategories, out IntPtr ppdwEventCategories, out int pdwLowSeverity, out int pdwHighSeverity, out int pdwNumAreas, out IntPtr ppszAreaList, out int pdwNumSources, out IntPtr ppszSourceList)
        {
            pdwEventType = 0;
            pdwNumCategories = 0;
            ppdwEventCategories = IntPtr.Zero;
            pdwLowSeverity = 0;
            pdwHighSeverity = 0;
            pdwNumAreas = 0;
            ppszAreaList = IntPtr.Zero;
            pdwNumSources = 0;
            ppszSourceList = IntPtr.Zero;

            try
            {
                pdwEventType = m_dwEventType;
                pdwLowSeverity = m_dwLowSeverity;
                pdwHighSeverity = m_dwHighSeverity;
                if (m_EventCategoryVector.Count != 0)
                {
                    int[] EventCategoryIDs = m_EventCategoryVector.ToArray();
                    pdwNumCategories = m_EventCategoryVector.Count;
                    ppdwEventCategories = ComUtils.GetInt32s(EventCategoryIDs);
                }
                if (m_AreaVector.Count != 0)
                {
                    string[] Areas = m_AreaVector.ToArray();
                    pdwNumAreas = m_AreaVector.Count;
                    ppszAreaList = ComUtils.GetUnicodeStrings(Areas);
                }
                if (m_SourceVector.Count != 0)
                {
                    string[] Sources = m_SourceVector.ToArray();
                    pdwNumSources = m_SourceVector.Count;
                    ppszSourceList = ComUtils.GetUnicodeStrings(Sources);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in GetFilter");
                throw ComUtils.CreateComException(e);
            }
        }

        /// <summary>
        /// Sets the attributes to be returned with event notifications in the IOPCEventSink::OnEvent callback.
        /// </summary>
        /// <param name="dwEventCategory">The specific event category for which the list of attributes applies.</param>
        /// <param name="dwCount">The size of the attribute IDs array.</param>
        /// <param name="dwAttributeIDs">The list IDs of the attributes to return with event notifications for the event type and event category specified.</param>
        public void SelectReturnedAttributes(int dwEventCategory, int dwCount, int[] dwAttributeIDs)
        {
            try
            {

                if (dwCount < 0)
                {
                    throw ComUtils.CreateComException("SelectReturnedAttributes", ResultIds.E_INVALIDARG);
                }

                // Make sure we are passed a valid dwEventCategory
                NodeId catNodeId = m_server.FindEventCatNodeId(dwEventCategory);
                if (catNodeId == null)
                    throw ComUtils.CreateComException("SelectReturnedAttributes", ResultIds.E_INVALIDARG);

                // Check for valid attributeIds
                List<EventAttribute> attrs = m_server.GetEventAttributes(dwEventCategory);
                
                for (int i = 0; i < dwCount; i++)
                {
                    if (!CompareAttribID(attrs,dwAttributeIDs[i]))
                        throw ComUtils.CreateComException("SelectReturnedAttributes", ResultIds.E_INVALIDARG); ;
                 
                }
                lock (m_csData)
                {
                    ReturnedAttributeList ras = new ReturnedAttributeList();

                    for (int i = 0; i < dwCount; i++)
                        ras.AddUnique(dwAttributeIDs[i]);

                    m_ReturnedAttributes[dwEventCategory] = ras;
                }
            }
            catch (COMException e)
            {
                throw ComUtils.CreateComException(e);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in SelectReturnedAttributes");
                throw ComUtils.CreateComException(e);
            }
        }

        /// <summary>
        /// Retrieves the attributes which are currently specified to be returned with event notifications in the IOPCEventSink::OnEvent callback.
        /// </summary>
        /// <param name="dwEventCategory">The specific event category for which to retrieve the list of attributes.</param>
        /// <param name="pdwCount">The size of the attribute IDs array which is being returned. Is set to zero if no attributes are currently specified.</param>
        /// <param name="ppdwAttributeIDs">The list IDs of the attributes which are currently specified to be returned with event notifications for the event type and event category specified.</param>
        public void GetReturnedAttributes(int dwEventCategory, out int pdwCount, out IntPtr ppdwAttributeIDs)
        {
            pdwCount = 0;
            ppdwAttributeIDs = IntPtr.Zero;

            try
            {
                // Make sure we are passed a valid dwEventCategory
                NodeId catNodeId = m_server.FindEventCatNodeId(dwEventCategory);
                if (catNodeId == null)
                    throw ComUtils.CreateComException("SetFilter", ResultIds.E_INVALIDARG);

                lock (m_csData)
                {
                    ReturnedAttributeList ras;
                    if (m_ReturnedAttributes.TryGetValue(dwEventCategory, out ras))
                    {
                        pdwCount = ras.Count;
                        int[] attrIDs = ras.ToArray();

                        ppdwAttributeIDs = ComUtils.GetInt32s(attrIDs);
                    }
                }
            }
            catch (COMException e)
            {
                throw ComUtils.CreateComException(e);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in GetReturnedAttributes");
                throw ComUtils.CreateComException(e);
            }
        }

        /// <summary>
        /// Force a refresh for all active conditions and inactive, unacknowledged conditions whose event notifications match the filter of the event subscription.
        /// </summary>
        /// <param name="dwConnection">The OLE Connection number returned from IConnectionPoint::Advise. This is passed to help the server determine which OPC event sink to call when the request completes.</param>
        public void Refresh(int dwConnection)
        {
            try
            {
                if (m_RefreshID != 0 || m_RefreshQ.Count != 0) 
                    throw ComUtils.CreateComException("Refresh", ResultIds.E_BUSY);

                m_RefreshID = dwConnection;
                
                // Foe each source walk through all associated conditions. If the condition is "refreshable", i.e. Active or
                // inactive/unacknowledged, then create an event and push it on to the subscription's refresh queue
                SourceMap sourceMap = SourceMap.TheSourceMap;
                foreach (KeyValuePair<string, ConditionMap> kvp in sourceMap)
                {
                   string sourceName = kvp.Key;
                   ConditionMap conditionMap = kvp.Value;
                   foreach (KeyValuePair<string, OPCCondition> kvpCond in conditionMap)
                   {
                       string conditionName = kvpCond.Key;
                       OPCCondition cond = kvpCond.Value;
                      
                       if (cond.IsEnabled() && (cond.IsActive() || !cond.IsAcked()))
                       {
                           OnEventClass OEClass = new OnEventClass(sourceName, conditionName, cond);
                           if (MatchesFilter(OEClass))
                               m_RefreshQ.Enqueue(OEClass);
                       }
                    
                   }
                }

                if (m_RefreshQ.Count > 0)
                {
                   ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadWork), null);
                }
                else
                {
                    CancelRefresh(dwConnection);
                }


            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in Refresh");
                throw ComUtils.CreateComException(e);
            }
        }


        /// <summary>
        /// Cancels a refresh in progress for the event subscription.
        /// </summary>
        /// <param name="dwConnection">The OLE Connection number returned from IConnectionPoint::Advise. This is passed to help the server determine which OPC event sink to call when the request completes.</param>
        public void CancelRefresh(int dwConnection)
        {
            if (dwConnection == 0)
            {
                throw ComUtils.CreateComException("CancelRefresh", ResultIds.E_INVALIDARG);
            }

            if (dwConnection != m_RefreshID)
            {
                throw ComUtils.CreateComException("CancelRefresh", ResultIds.E_FAIL);
            }

            m_bCancelRefresh = true;
            ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadWork), null);
        }

        /// <summary>
        /// Get the current state of the subscription.
        /// </summary>
        /// <param name="pbActive">The current active state of the subscription.</param>
        /// <param name="pdwBufferTime">The current buffer time configured for event notification.</param>
        /// <param name="pdwMaxSize">The current max number of events that will be sent in a single IOPCEventSink::OnEvent callback.</param>
        /// <param name="phClientSubscription">The client supplied subscription handle</param>
        public void GetState(out int pbActive, out int pdwBufferTime, out int pdwMaxSize, out int phClientSubscription)
        {
            pbActive = 0;
            pdwBufferTime = 0;
            pdwMaxSize = 0;
            phClientSubscription = 0;

            try
            {
                pbActive = m_bActive ? 1 : 0;
                pdwBufferTime = m_dwBufferTime;
                pdwMaxSize = m_dwMaxSize;
                phClientSubscription = m_hClientSubscription;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in GetState");
                throw ComUtils.CreateComException(e);
            }
        }

        /// <summary>
        /// Client can set various properties of the event subscription.
        /// </summary>
        /// <param name="pbActive">TRUE (non-zero) to activate the subscription. FALSE (0) to deactivate the subscription.</param>
        /// <param name="pdwBufferTime">New buffer time requested for the subscription by the client.</param>
        /// <param name="pdwMaxSize">New maximum number of event notifications to send with a single IOPCEventSink::OnEvent callback.</param>
        /// <param name="hClientSubscription">Client supplied handle for the subscription.</param>
        /// <param name="pdwRevisedBufferTime">The buffer time that the server is actually providing, which may differ from dwBufferTime.</param>
        /// <param name="pdwRevisedMaxSize">The maximum number of events that the server will actually be sending in a single IOPCEventSink::OnEvent callback, which may differ from dwMaxSize.</param>
        public void SetState(
            IntPtr  pbActive, 
            IntPtr  pdwBufferTime, 
            IntPtr  pdwMaxSize, 
            int     hClientSubscription, 
            out int pdwRevisedBufferTime, 
            out int pdwRevisedMaxSize)
        {
            pdwRevisedBufferTime = m_dwBufferTime;
            pdwRevisedMaxSize = m_dwMaxSize;

            try
            {
                int dwBufferTime = 0;
                int dwMaxSize = 0;

                if (pbActive != IntPtr.Zero)
                    m_bActive = Marshal.ReadInt32(pbActive) == 0 ? false : true;

                    m_hClientSubscription = hClientSubscription;

                // Set revised max size to the smaller of (1)requested size or (2)default max
                if (pdwMaxSize != IntPtr.Zero)
                {
                    dwMaxSize = Marshal.ReadInt32(pdwMaxSize);
                    dwMaxSize = dwMaxSize > 0 ? dwMaxSize : DefaultMaxCallbackSize;
                    m_dwMaxSize = (dwMaxSize < DefaultMaxCallbackSize ? dwMaxSize : DefaultMaxCallbackSize);
                    pdwRevisedMaxSize = m_dwMaxSize;
                }

                // Set revised buffer time to the larger of (1)requested time or (2) default time
                if (pdwBufferTime != IntPtr.Zero)
                {
                    dwBufferTime = Marshal.ReadInt32(pdwBufferTime);
                    dwBufferTime = dwBufferTime > 0 ? dwBufferTime : DefaultBufferTime;
                    m_dwBufferTime = (dwBufferTime > DefaultBufferTime ? dwBufferTime : DefaultBufferTime);
                    pdwRevisedBufferTime = m_dwBufferTime;
                }
                
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in SetState");
                throw ComUtils.CreateComException(e);
            }

        }


        #endregion

        #region IOPCEventSubscriptionMgt2 Members
        /// <summary>
        /// Returns the currently active keep-alive time for the subscription.  
        /// </summary>
        public void GetKeepAlive(out int pdwKeepAliveTime)
        {
            pdwKeepAliveTime = 0;
            lock (m_csData)
            {
                try
                {
                    pdwKeepAliveTime = m_KeepAliveTime;
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e);
                }
            }
        }

        /// <summary>
        /// Sets the keep-alive time for a subscription to cause the server to provide 
        /// client callbacks on the subscription when there are no new events to report.
        /// </summary>
        /// <param name="dwKeepAliveTime">The maximum amount of time (in milliseconds) the client expects to receive a new subscription callback since the last subscription callback. A value of zero indicates the client does not wish to receive any keep-alive callbacks.</param>
        /// <param name="pdwRevisedKeepAliveTime">The KeepAliveTime the server is actually providing.</param>
        public void SetKeepAlive(
            int dwKeepAliveTime,
            out int pdwRevisedKeepAliveTime)
        {
            pdwRevisedKeepAliveTime = 0;
            
            lock (m_csData)
            {
                if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);
                if (dwKeepAliveTime == 0)
                    return;

                lock (m_csData)
                {
                    try
                    {
                        // The only keep alive interval supported is that of the underlying UA subscription
                        m_KeepAliveTime = m_MinimumKeepAliveTime;
                        pdwRevisedKeepAliveTime = m_KeepAliveTime;
                    }
                    catch (Exception e)
                    {
                        throw ComUtils.CreateComException(e);
                    }
                }
            }
        } 
        #endregion

        /// <summary>
        /// Determines whether the newly received event matches the filter applied to this
        /// subscription.
        /// </summary>
        /// <param name="pOnEventClass"></param>
        /// <returns></returns>
        private bool MatchesFilter(OnEventClass pOnEventClass)
        {
            bool FilterMatches = true;

            try
            {
                if (System.Convert.ToBoolean(m_dwEventType & pOnEventClass.InternalOES.dwEventType) == false)
                    FilterMatches = false;

                if (m_EventCategoryVector.Count != 0)
                    if (m_EventCategoryVector.Contains(pOnEventClass.InternalOES.dwEventCategory) == false)
                        FilterMatches = false;

                if (pOnEventClass.InternalOES.dwSeverity < m_dwLowSeverity | pOnEventClass.InternalOES.dwSeverity > m_dwHighSeverity)
                    FilterMatches = false;

                if (m_AreaVector.Count != 0)
                {
                    if (pOnEventClass.EventAttributes.Count > 0)
                    {
                        string[] areas = (string[])pOnEventClass.EventAttributes[1];
                        foreach (string area in areas)
                        {
                            if (m_AreaVector.Contains(area) == true)
                            {
                                FilterMatches = true;
                                break;
                            }
                            FilterMatches = false;
                        }
                    }
                }

                if (m_SourceVector.Count != 0)
                    if (m_SourceVector.Contains(pOnEventClass.InternalOES.szSource) == false)
                        FilterMatches = false;

            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in MatchesFilter");
                FilterMatches = false;
            }
            return FilterMatches;
        }

        /// <summary>
        /// If the subscription filter allows the event then enqueue the event
        /// and assign a threadpool worker to handle the subscription callback
        /// </summary>
        /// <param name="pOnEventClass"></param>
        public void ProcessNewEvent(OnEventClass pOnEventClass)
        {
            lock (m_csData)
            {
                try
                {
                    if (m_bActive == true)
                    {
                        // compare to my filter, if it matches
                        if (MatchesFilter(pOnEventClass))
                            m_OnEventQ.Enqueue(pOnEventClass);

                        // Just a keep-alive event?
                        else if ((pOnEventClass.InternalOES.szSource == null) && (pOnEventClass.InternalOES.szConditionName == null))
                            m_KeepAliveQ.EnqueueIfEmpty(pOnEventClass);

                        // Even if this event was filtered out, check to see if it 
                        // is time to send anything that is already in my queue.
                        if (m_OnEventQ.Count > 0)
                        {
                            TimeSpan bufferTime = TimeSpan.FromMilliseconds(m_dwBufferTime);
                            DateTime lastUpdate = m_LastUpdateTime;
                            DateTime nextUpdate = lastUpdate + bufferTime;

                            if (m_OnEventQ.Count >= m_dwMaxSize || nextUpdate <= DateTime.Now)
                                ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadWork), null);
                        }

                        // No "real events" to send ... is it time to send a keep alive (keepalive of 0 means disabled) ?
                        else if ((m_KeepAliveQ.Count > 0) && (m_KeepAliveTime != 0))
                        {
                            TimeSpan bufferTime = TimeSpan.FromMilliseconds(m_KeepAliveTime);
                            DateTime lastUpdate = m_LastUpdateTime;
                            if (DateTime.Now >= (lastUpdate + bufferTime))
                                ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadWork), null);
                        }
                    }
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error in ProcessNewEvent");
                }
            }
        }

        /// <summary>
        /// Initializes a COM-patible ONEVENTSTRUCT ready to be sent to subscribing clients
        /// </summary>
        /// <param name="oes"></param>
        /// <param name="pOnEventClass"></param>
        private void CopyToOnEventStruct(ref ONEVENTSTRUCT oes, OnEventClass pOnEventClass)
        {
            try
            {
                oes = pOnEventClass.InternalOES;

                ReturnedAttributeList attrIds;
                if (m_ReturnedAttributes.TryGetValue(oes.dwEventCategory, out attrIds) == true)
                {
                    int retAttrCount = attrIds.Count;
                    int i = 0;
                    object[] attrVals = new object[retAttrCount];

                    foreach (int attrId in attrIds)
                        attrVals[i++] = pOnEventClass.EventAttributes[attrId];

                    oes.dwNumEventAttrs = retAttrCount;
                    oes.pEventAttributes = ComUtils.GetVARIANTs(attrVals, false);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in CopyToOnEventStruct");
            }
        }

        /// <summary>
        /// Called on a threadpool thread, SendEvents pops waiting event notifications from the
        /// subscription queue (subject to MaxSize) and invokes the client callback
        /// </summary>
        /// <param name="q"></param>
        /// <param name="bRefresh"></param>
        /// <param name="bKeepAlive"></param>
        public void SendEvents(EventItemQueue<OnEventClass> q, bool bRefresh, bool bKeepAlive)
        {
            int dwCount = 0;
            int hClientSubscription = 0;
            bool bLastRefresh = false;
            ONEVENTSTRUCT[] events;

            lock (m_csData)
            {
                hClientSubscription = m_hClientSubscription;
                dwCount = Math.Min(q.Count, m_dwMaxSize);
                bLastRefresh = (dwCount == q.Count) && bRefresh;
                events = new ONEVENTSTRUCT[dwCount];

                for (int i = 0; i < dwCount; i++)
                    CopyToOnEventStruct(ref events[i], q.Dequeue());

            }

            bool bUpdated = false;

            try
            {
                lock (m_lock)
                {
                    object callback = GetCallback(typeof(IOPCEventSink).GUID);

                    if (callback == null)
                    {
                        return;
                    }

                    if (dwCount > 0) // don't update time for a keep alive callback
                    {
                        m_LastUpdateTime = DateTime.Now;
                        bUpdated = true;
                    }

                    if (bRefresh && bLastRefresh)
                        m_RefreshID = 0;

                    ((IOPCEventSink)callback).OnEvent(
                        hClientSubscription,
                        bRefresh ? 1 : 0,
                        bLastRefresh ? 1 : 0,
                        bKeepAlive ? 0 : dwCount,
                        events);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in OnEvent callback.");
            }
            finally
            {
                for (int i = 0; i < dwCount; i++)
                    Marshal.FreeCoTaskMem(events[i].pEventAttributes);
            }

            if (m_server != null && bUpdated)
                m_server.LastUpdateTime = m_LastUpdateTime;
        }

        /// <summary>
        /// Clears the refresh related state from the subscription and generates a callback
        /// where bRefresh==true and bLastRefresh==true
        /// </summary>
        private void SendCancelRefresh()
        {
            lock (m_csData)
            {
                m_RefreshQ.Clear();
                m_bCancelRefresh = false;
                m_RefreshID = 0;
            }

            // Create an empty event -- dwCount will be set to zero anyway
            ONEVENTSTRUCT[] emptyEvent = new ONEVENTSTRUCT[1];
            CopyToOnEventStruct(ref emptyEvent[0], new OnEventClass(null, null, new OPCCondition()));

            try
            {
                lock (m_lock)
                {
                    object callback = GetCallback(typeof(IOPCEventSink).GUID);

                    if (callback == null)
                    {
                        return;
                    }

                    ((IOPCEventSink)callback).OnEvent(m_hClientSubscription, 1, 1, 0, emptyEvent);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in OnEvent callback.");
            }
            finally
            {
                Marshal.FreeCoTaskMem(emptyEvent[0].pEventAttributes);
            }

        }

        /// <summary>
        /// Called from threadpool worker, ThreadWork examines the subscription's queues and
        /// initiates callback processing where events are queued and waiting.  ThreadWork
        /// also services Refresh cancel requests
        /// </summary>
        /// <param name="obj"></param>
        public void ThreadWork(object obj)
        {
            bool bCancelRefresh = false;
            int dwRefreshQSize = 0;
            int dwEventQSize = 0;

            lock (m_csData)
            {
                dwEventQSize = m_OnEventQ.Count;

                bCancelRefresh = m_bCancelRefresh;
                dwRefreshQSize = m_RefreshQ.Count;
            }

            // new events have priority over refresh events.

            if (bCancelRefresh)
                SendCancelRefresh();

            if (dwEventQSize > 0)
                SendEvents(m_OnEventQ, false, false);

            if (dwRefreshQSize > 0)
                SendEvents(m_RefreshQ, true, false);

            if (!bCancelRefresh && dwEventQSize == 0 && dwRefreshQSize == 0)
                SendEvents(m_KeepAliveQ, false, true);  // keep-alive callback

            lock (m_csData)
            {
                if (m_OnEventQ.Count > 0 || m_RefreshQ.Count > 0)
                    ThreadPool.QueueUserWorkItem(ThreadWork);
            }
        }

        /// <summary>
        /// Subscription is active
        /// </summary>
        public bool Active
        {
            get { return m_bActive; }
        }

        /// <summary>
        /// Access/assign the UA subscription
        /// </summary>
        public Opc.Ua.Client.Subscription UaSubscription
        {
            get { return m_subscription; }
            set { m_subscription = value; }
        }
        
        /// <summary>
        /// iterates through the list of attributes and returns true when a matching attribute is found
        /// Caller: SelectReturnedAttributes()
        /// </summary>
        private bool CompareAttribID(List<EventAttribute> attrs, int id1)
        {
            foreach (EventAttribute attrib in attrs)
            {
                if (attrib.AttributeID == id1)
                    return true;
            }
            return false;
        }

        #region Private Member
        private object m_lock = new object();
        private object m_csData = new object();
        private ComAeProxy m_server = null;
        private bool m_bActive;
        private int m_dwBufferTime;
        private int m_dwMaxSize;
        private int m_hClientSubscription;
        private int m_KeepAliveTime = 0;  // Initially disabled
        private int m_MinimumKeepAliveTime = 1000;
        private DateTime m_LastUpdateTime = DateTime.MinValue;
        private const int DefaultMaxCallbackSize = 1000;
        private const int DefaultBufferTime = 1000;

        private EventItemQueue<OnEventClass> m_OnEventQ = new EventItemQueue<OnEventClass>();
        private EventItemQueue<OnEventClass> m_RefreshQ = new EventItemQueue<OnEventClass>();
        private EventItemQueue<OnEventClass> m_KeepAliveQ = new EventItemQueue<OnEventClass>();
        private DateTime m_ftLastUpdate;

        // data members for Set/Get Filter
        private int m_dwEventType;
        private UniqueList<int> m_EventCategoryVector = new UniqueList<int>();
        private UniqueList<string> m_AreaVector = new UniqueList<string>();
        private UniqueList<string> m_SourceVector = new UniqueList<string>();
        private int m_dwLowSeverity;
        private int m_dwHighSeverity;

        // data member for SelectReturnAttributes()
        private Dictionary<int, ReturnedAttributeList> m_ReturnedAttributes = new Dictionary<int, ReturnedAttributeList>();

        // data member flag for Refresh/CancelRefresh;
        private bool m_bCancelRefresh;
        private int m_RefreshID = 0;

        private Opc.Ua.Client.Subscription m_subscription;
        #endregion
    }

    /// <summary>
    /// List containing the client requested set of attribute IDs for any one event category
    /// </summary>
    public class ReturnedAttributeList : UniqueList<int>
    {
    }
    

}
