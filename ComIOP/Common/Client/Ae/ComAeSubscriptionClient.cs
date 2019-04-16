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
using System.Xml;
using System.IO;
using System.Reflection;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using OpcRcw.Ae;
using OpcRcw.Comn;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Browses areas and sources in the AE server.
    /// </summary>
    internal class ComAeSubscriptionClient : ComObject
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComAeSubscriptionClient"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="configuration">The configuration.</param>
        /// <param name="cache">The cache for known types.</param>
        /// <param name="namespaceIndex">The namespace index for the event types.</param>
        /// <param name="manager">The manager.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        public ComAeSubscriptionClient(
            ServerSystemContext context,
            ComAeClientConfiguration configuration,
            AeTypeCache cache,
            ushort namespaceIndex,
            ComAeClientManager manager,
            MonitoredItem monitoredItem)
        {
            m_defaultContext = context;
            m_separators = configuration.SeperatorChars;
            m_cache = cache;
            m_namespaceIndex = namespaceIndex;
            m_manager = manager;
            m_refreshComplete = new ManualResetEvent(false);
            m_monitoredItems = new List<MonitoredItem>();
            m_monitoredItems.Add(monitoredItem);
            m_qualifiedName = null;
            m_isSource = false;

            NodeHandle handle = monitoredItem.ManagerHandle as NodeHandle;
            AeAreaState area = handle.Node as AeAreaState;

            if (area != null)
            {
                m_qualifiedName = area.QualifiedName;
                m_isSource = false;
            }
            else
            {
                AeSourceState source = handle.Node as AeSourceState;

                if (source != null)
                {
                    m_qualifiedName = source.QualifiedName;
                    m_isSource = true;
                }
            }
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Utils.SilentDispose(m_callback);
                m_callback = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Releases all references to the server.
        /// </summary>
        protected override void ReleaseServer()
        {
            Utils.SilentDispose(m_callback);
            m_callback = null;
            base.ReleaseServer();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// The locale id for the subscription.
        /// </summary>
        public int LocaleId { get; set; }

        /// <summary>
        /// Creates the subscription.
        /// </summary>
        public void Create()
        {
            ComAeClient client = m_manager.SelectClient(m_defaultContext, false);

            // release existing server.
            if (this.Unknown != null)
            {
                ReleaseServer();
            }

            // create the subscription.
            this.Unknown = client.CreateEventSubscription();
            
            // select the attributes.
            foreach (KeyValuePair<int,int[]> entry in m_cache.Attributes)
            {
                SelectReturnedAttributes(entry.Key, entry.Value);
            }

            // set the filter.
            SetFilter(m_qualifiedName, m_isSource);
            
            // set up callback.
            try
            {
                m_callback = new ComAeEventSink(this);
            }
            catch (Exception e)
            {
                Utils.Trace("Could not establish event callback.", e);
            }
        }

        /// <summary>
        /// Deletes the subscription.
        /// </summary>
        public void Delete()
        {
            if (this.Unknown != null)
            {
                ReleaseServer();
            }
        }

        /// <summary>
        /// Refreshes the conditions monitored by the subscription.
        /// </summary>
        /// <param name="events">The events.</param>
        public void Refresh(List<IFilterTarget> events)
        {
            // ensure multiple refreshes cannot occur simutaneously.
            try
            {
                lock (m_refreshLock)
                {
                    // save the list for updates.
                    lock (m_callbackLock)
                    {
                        m_refreshEvents = events;
                        m_refreshComplete.Reset();
                    }

                    if (m_callback != null)
                    {
                        if (!Refresh())
                        {
                            return;
                        }

                        if (!m_refreshComplete.WaitOne(30000))
                        {
                            CancelRefresh();
                        }
                    }
                }
            }
            finally
            {
                // all done.
                lock (m_callbackLock)
                {
                    m_refreshEvents = null;
                }
            }
        }

        /// <summary>
        /// Adds an item to a subscription.
        /// </summary>
        public void AddItem(MonitoredItem monitoredItem)
        {
            lock (m_monitoredItems)
            {
                m_monitoredItems.Add(monitoredItem);
            }
        }

        /// <summary>
        /// Removes an item from the subscription.
        /// </summary>
        public int RemoveItem(MonitoredItem monitoredItem)
        {
            lock (m_monitoredItems)
            {
                for (int ii = 0; ii < m_monitoredItems.Count; ii++)
                {
                    if (m_monitoredItems[ii].Id == monitoredItem.Id)
                    {
                        m_monitoredItems.RemoveAt(ii);
                        break;
                    }
                }

                return m_monitoredItems.Count;
            }
        }

        /// <summary>
        /// Called when events arrive from the server.
        /// </summary>
        /// <param name="events">The events.</param>
        public void OnEvent(ONEVENTSTRUCT[] events)
        {
            for (int ii = 0; ii < events.Length; ii++)
            {
                BaseEventState e = DispatchEvent(events[ii]);

                if (e != null)
                {
                    lock (m_monitoredItems)
                    {
                        for (int jj = 0; jj < m_monitoredItems.Count; jj++)
                        {
                            m_monitoredItems[jj].QueueEvent(e);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Dispatches the event.
        /// </summary>
        private BaseEventState DispatchEvent(ONEVENTSTRUCT e)
        { 
            NodeId typeId = AeParsedNodeId.Construct(e.dwEventType, e.dwEventCategory, e.szConditionName, m_namespaceIndex);
            
            // find the type.
            AeEventTypeState eventType = m_cache.FindType(m_defaultContext, typeId);

            if (eventType == null)
            {
                return null;
            }

            // create a new instance.
            BaseEventState instance = m_cache.CreateInstance(m_defaultContext, eventType);

            if (instance == null)
            {
                return null;
            }

            // fill in fields.
            UpdateBaseEvent(instance, eventType.EventType, e);

            if (instance is AuditEventState)
            {
                UpdateAuditEvent((AuditEventState)instance, eventType.EventType, e);
            }

            if (instance is AlarmConditionState)
            {
                UpdateAlarm((AlarmConditionState)instance, eventType.EventType, e);
            }

            if (instance is ExclusiveLimitAlarmState)
            {
                UpdateExclusiveLimitAlarm((ExclusiveLimitAlarmState)instance, eventType.EventType, e);
            }

            else if (instance is NonExclusiveLimitAlarmState)
            {
                UpdateNonExclusiveLimitAlarm((NonExclusiveLimitAlarmState)instance, eventType.EventType, e);
            }
            
            // process attributes.
            bool ackCommentFound = false;
            object[] values = ComUtils.GetVARIANTs(ref e.pEventAttributes, e.dwNumEventAttrs, false);

            for (int ii = 0; ii < eventType.EventType.Attributes.Count; ii++)
            {
                EventAttribute attribute = eventType.EventType.Attributes[ii];

                if (ii >= e.dwNumEventAttrs)
                {
                    continue;
                }

                if (!ackCommentFound && AeTypeCache.IsKnownName(attribute.Description, "ACK COMMENT"))
                {
                    ConditionState condition = instance as ConditionState;

                    if (condition != null)
                    {
                        condition.Comment = new ConditionVariableState<LocalizedText>(condition);
                        condition.Comment.BrowseName = Opc.Ua.BrowseNames.Comment;
                        condition.Comment.Value = new LocalizedText(values[ii] as string);
                    }

                    ackCommentFound = true;
                    continue;
                }

                PropertyState property = new PropertyState(instance);

                property.SymbolicName = attribute.Description;
                property.BrowseName = new QualifiedName(property.SymbolicName, m_namespaceIndex);
                property.Value = values[ii];

                instance.AddChild(property);
            }

            return instance;
        }

        /// <summary>
        /// Updates the base event.
        /// </summary>
        private void UpdateBaseEvent(BaseEventState instance, EventType eventType, ONEVENTSTRUCT e)
        {
            BinaryEncoder encoder = new BinaryEncoder(ServiceMessageContext.GlobalContext);

            encoder.WriteString(null, e.szSource);
            encoder.WriteString(null, e.szConditionName);
            encoder.WriteInt32(null, e.ftActiveTime.dwHighDateTime);
            encoder.WriteInt32(null, e.ftActiveTime.dwLowDateTime);
            encoder.WriteInt32(null, e.dwCookie);

            byte[] eventId = encoder.CloseAndReturnBuffer();
            NodeId eventTypeId = AeParsedNodeId.Construct(e.dwEventType, e.dwEventCategory, e.szConditionName, m_namespaceIndex);
            NodeId sourceNode = AeModelUtils.ConstructIdForSource(e.szSource, null, m_namespaceIndex);
            string sourceName = e.szSource;
            DateTime time = ComUtils.GetDateTime(e.ftTime);
            DateTime receiveTime = DateTime.UtcNow;
            LocalizedText message = e.szMessage;
            ushort severity = (ushort)e.dwSeverity;

            instance.TypeDefinitionId = eventTypeId;
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.EventId, eventId, false);
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.EventType, eventTypeId, false);
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.SourceNode, sourceNode, false);
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.SourceName, sourceName, false);
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.Time, time, false);
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.ReceiveTime, receiveTime, false);
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.Message, message, false);
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.Severity, severity, false);
        }

        /// <summary>
        /// Updates the audit event.
        /// </summary>
        private void UpdateAuditEvent(AuditEventState instance, EventType eventType, ONEVENTSTRUCT e)
        {
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.ActionTimeStamp, instance.Time.Value, false);
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.Status, true, false);
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.ServerId, m_defaultContext.NamespaceUris.GetString(m_namespaceIndex), false);
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.ClientUserId, e.szActorID, false);
        }

        /// <summary>
        /// Updates the condition event.
        /// </summary>
        private void UpdateAlarm(AlarmConditionState instance, EventType eventType, ONEVENTSTRUCT e)
        {
            instance.NodeId = AeParsedNodeId.ConstructIdForCondition(e.szSource, e.dwEventCategory, e.szConditionName, m_namespaceIndex);

            // find the condition class.
            NodeId classId = AeParsedNodeId.Construct(e.dwEventType, e.dwEventCategory, null, m_namespaceIndex);
            AeEventTypeState conditionClassType = m_cache.FindType(m_defaultContext, classId);

            if (conditionClassType != null)
            {
                instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.ConditionClassId, classId, false);
                instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.ConditionClassName, conditionClassType.EventType.Description, false);
            }
            else
            {
                instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.ConditionClassId, Opc.Ua.ObjectTypeIds.BaseConditionClassType, false);
                instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.ConditionClassName, "BaseConditionClass", false);
            }

            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.ConditionName, e.szConditionName, false); ;
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.ClientUserId, e.szActorID, false);
            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.Quality, ComUtils.GetQualityCode(e.wQuality), false);

            bool acknowledged = (e.wNewState & Constants.CONDITION_ACKED) != 0;
            bool active = (e.wNewState & Constants.CONDITION_ACTIVE) != 0;
            bool enabled = (e.wNewState & Constants.CONDITION_ENABLED) != 0;
            bool retain = enabled & (active || !acknowledged);

            LocalizedText effectiveDisplayName = ConditionStateNames.Inactive;

            if (!enabled)
            {
                effectiveDisplayName = ConditionStateNames.Disabled;
            }
            else if (!acknowledged)
            {
                effectiveDisplayName = ConditionStateNames.Unacknowledged;
            }
            else if (active)
            {
                effectiveDisplayName = ConditionStateNames.Active;
            }

            instance.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.Retain, true, false);

            instance.EnabledState = new TwoStateVariableState(instance);
            instance.EnabledState.BrowseName = Opc.Ua.BrowseNames.EnabledState;
            instance.EnabledState.Value = new LocalizedText((enabled) ? ConditionStateNames.Enabled : ConditionStateNames.Disabled);
            instance.EnabledState.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.Id, enabled, false);
            instance.EnabledState.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.EffectiveDisplayName, effectiveDisplayName, false);

            instance.AckedState = new TwoStateVariableState(instance);
            instance.AckedState.BrowseName = Opc.Ua.BrowseNames.AckedState;
            instance.AckedState.Value = new LocalizedText((acknowledged) ? ConditionStateNames.Acknowledged : ConditionStateNames.Unacknowledged);
            instance.AckedState.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.Id, acknowledged, false);

            instance.ActiveState = new TwoStateVariableState(instance);
            instance.ActiveState.BrowseName = Opc.Ua.BrowseNames.ActiveState;
            instance.ActiveState.Value = new LocalizedText((active) ? ConditionStateNames.Active : ConditionStateNames.Inactive);
            instance.ActiveState.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.Id, active, false);
            instance.ActiveState.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.TransitionTime, ComUtils.GetDateTime(e.ftActiveTime), false);

            if (!String.IsNullOrEmpty(e.szSubconditionName))
            {
                instance.ActiveState.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.EffectiveDisplayName, e.szSubconditionName, false);
            }
        }
                
        /// <summary>
        /// Updates the exclusive limit alarm event.
        /// </summary>
        private void UpdateExclusiveLimitAlarm(ExclusiveLimitAlarmState instance, EventType eventType, ONEVENTSTRUCT e)
        {
            NodeId state = null;
            string text = null;

            if (AeTypeCache.IsKnownName(e.szSubconditionName, "HI HI"))
            {
                state = Opc.Ua.ObjectIds.ExclusiveLimitStateMachineType_HighHigh;
                text = ConditionStateNames.HighHighActive;
            }

            if (AeTypeCache.IsKnownName(e.szSubconditionName, "HI"))
            {
                state = Opc.Ua.ObjectIds.ExclusiveLimitStateMachineType_High;
                text = ConditionStateNames.HighActive;
            }

            if (AeTypeCache.IsKnownName(e.szSubconditionName, "LO"))
            {
                state = Opc.Ua.ObjectIds.ExclusiveLimitStateMachineType_Low;
                text = ConditionStateNames.LowActive;
            }

            if (AeTypeCache.IsKnownName(e.szSubconditionName, "LO LO"))
            {
                state = Opc.Ua.ObjectIds.ExclusiveLimitStateMachineType_LowLow;
                text = ConditionStateNames.LowLowActive;
            }
          
            instance.LimitState = new ExclusiveLimitStateMachineState(instance);
            instance.LimitState.BrowseName = Opc.Ua.BrowseNames.LimitState;
            instance.LimitState.CurrentState = new FiniteStateVariableState(instance.LimitState);
            instance.LimitState.CurrentState.BrowseName = Opc.Ua.BrowseNames.CurrentState;
            instance.LimitState.CurrentState.Value = text;

            instance.LimitState.CurrentState.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.Id, state, false);
        }

        /// <summary>
        /// Updates the non-exclusive limit event.
        /// </summary>
        private void UpdateNonExclusiveLimitAlarm(NonExclusiveLimitAlarmState instance, EventType eventType, ONEVENTSTRUCT e)
        {
            bool active = (e.wNewState & Constants.CONDITION_ACTIVE) != 0;
            TwoStateVariableState state = null;

            if (AeTypeCache.IsKnownName(e.szConditionName, "HI HI"))
            {
                instance.HighHighState = state = new TwoStateVariableState(instance);
                instance.HighHighState.BrowseName = Opc.Ua.BrowseNames.HighHighState;
            }

            else if (AeTypeCache.IsKnownName(e.szSubconditionName, "HI") || AeTypeCache.IsKnownName(e.szSubconditionName, "DV HI"))
            {
                instance.HighState = state = new TwoStateVariableState(instance);
                instance.HighState.BrowseName = Opc.Ua.BrowseNames.HighState;
            }

            else if (AeTypeCache.IsKnownName(e.szSubconditionName, "LO") || AeTypeCache.IsKnownName(e.szSubconditionName, "DV LO"))
            {
                instance.LowState = state = new TwoStateVariableState(instance);
                instance.LowState.BrowseName = Opc.Ua.BrowseNames.LowState;
            }

            else if (AeTypeCache.IsKnownName(e.szSubconditionName, "LO LO"))
            {
                instance.LowLowState = state = new TwoStateVariableState(instance);
                instance.LowLowState.BrowseName = Opc.Ua.BrowseNames.LowLowState;
            }

            if (state != null)
            {
                state.Value = new LocalizedText((active) ? ConditionStateNames.Active : ConditionStateNames.Inactive);
                state.SetChildValue(m_defaultContext, Opc.Ua.BrowseNames.Id, active, false);
            }
        }

        /// <summary>
        /// Called when refresh events arrive from the server.
        /// </summary>
        /// <param name="events">The events.</param>
        /// <param name="lastRefresh">if set to <c>true</c> if the refresh is complete.</param>
        public void OnRefresh(ONEVENTSTRUCT[] events, bool lastRefresh)
        {
            lock (m_callbackLock)
            {
                // check if refresh was abandoned.
                if (m_refreshEvents == null)
                {
                    return;
                }

                // dispatch events.
                for (int ii = 0; ii < events.Length; ii++)
                {
                    BaseEventState e = DispatchEvent(events[ii]);

                    if (e != null)
                    {
                        m_refreshEvents.Add(e);
                    }
                }

                // signal end of refresh.
                if (lastRefresh)
                {
                    m_refreshComplete.Set();
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Refreshes all conditions.
        /// </summary>
        /// <returns></returns>
        private bool Refresh()
        {
            string methodName = "IOPCEventSubscriptionMgt.Refresh";

            try
            {
                IOPCEventSubscriptionMgt server = BeginComCall<IOPCEventSubscriptionMgt>(methodName, true);
                server.Refresh(m_callback.Cookie);
                return true;
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_BUSY, ResultIds.E_NOTIMPL))
                {
                    ComCallError(methodName, e);
                }

                return false;
            }
            finally
            {
                EndComCall(methodName);
            }
        }

        /// <summary>
        /// Cancels the refresh for all conditions.
        /// </summary>
        /// <returns></returns>
        private bool CancelRefresh()
        {
            string methodName = "IOPCEventSubscriptionMgt.CancelRefresh";

            try
            {
                IOPCEventSubscriptionMgt server = BeginComCall<IOPCEventSubscriptionMgt>(methodName, true);
                server.CancelRefresh(m_callback.Cookie);
                return true;
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_FAIL, ResultIds.E_INVALIDARG, ResultIds.E_NOTIMPL))
                {
                    ComCallError(methodName, e);
                }

                return false;
            }
            finally
            {
                EndComCall(methodName);
            }
        }

        /// <summary>
        /// Selects the returned attributes.
        /// </summary>
        /// <param name="categoryId">The category id.</param>
        /// <param name="attributeIds">The attribute ids.</param>
        /// <returns></returns>
        private bool SelectReturnedAttributes(int categoryId, int[] attributeIds)
        {
            string methodName = "IOPCEventSubscriptionMgt.SelectReturnedAttributes";

            try
            {
                IOPCEventSubscriptionMgt server = BeginComCall<IOPCEventSubscriptionMgt>(methodName, true);
                server.SelectReturnedAttributes(categoryId, attributeIds.Length, attributeIds);
                return true;
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_FAIL))
                {
                    ComCallError(methodName, e);
                }

                return false;
            }
            finally
            {
                EndComCall(methodName);
            }
        }

        /// <summary>
        /// Sets the filter.
        /// </summary>
        /// <param name="qualifiedName">Name of the qualified.</param>
        /// <param name="isSource">if set to <c>true</c> if filtering by source.</param>
        /// <returns></returns>
        private bool SetFilter(string qualifiedName, bool isSource)
        {
            string methodName = "IOPCEventSubscriptionMgt.SetFilter";

            try
            {
                IOPCEventSubscriptionMgt server = BeginComCall<IOPCEventSubscriptionMgt>(methodName, true);

                string[] filter = null;

                if (!String.IsNullOrEmpty(qualifiedName))
                {
                    filter = new string[] { qualifiedName };
                    
                    // need to use a wild card to include sub-areas.
                    if (!String.IsNullOrEmpty(m_separators))
                    {
                        filter = new string[m_separators.Length+1];
                        filter[0] = qualifiedName;

                        for (int ii = 0; ii < m_separators.Length; ii++)
                        {
                            filter[ii + 1] = Utils.Format("{0}{1}*", qualifiedName, m_separators[ii]);
                        }
                    }
                }
                else
                {
                    filter = new string[0];
                }

                server.SetFilter(
                    Constants.ALL_EVENTS,
                    0,
                    new int[0],
                    1,
                    1000,
                    (isSource)?0:filter.Length,
                    filter,
                    (!isSource)?0:filter.Length,
                    filter);

                return true;
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_FAIL))
                {
                    ComCallError(methodName, e);
                }

                return false;
            }
            finally
            {
                EndComCall(methodName);
            }
        }
        #endregion

        #region Private Fields
        private ServerSystemContext m_defaultContext;
        private string m_separators;
        private AeTypeCache m_cache;
        private ushort m_namespaceIndex;
        private ComAeClientManager m_manager;
        private string m_qualifiedName;
        private bool m_isSource;
        private ComAeEventSink m_callback;
        private List<MonitoredItem> m_monitoredItems;
        private object m_refreshLock = new object();
        private object m_callbackLock = new object();
        private ManualResetEvent m_refreshComplete;
        private List<IFilterTarget> m_refreshEvents;
        #endregion
    }
}
