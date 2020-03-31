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
using OpcRcw.Da;

namespace Opc.Ua.Com.Server.Ae
{
    /// <summary>
    /// Processing of events received from the target UA server
    /// </summary>
    public class IncomingEventHandler
    {
        /// <summary>
        /// If the event type is CONDITION_EVENT then update any existing record of the condition and adjust
        /// state and change mask.
        /// </summary>
        /// <param name="EventNotification"></param>
        /// <param name="areas"></param>
        public void ProcessEventNotificationList(EventNotification EventNotification, string[] areas)
        {
            SourceMap sourceMap = SourceMap.TheSourceMap;
            try
            {
                lock (sourceMap)
                {
                    OPCCondition cond;
                    if (EventNotification.EventType == OpcRcw.Ae.Constants.CONDITION_EVENT)
                    {
                        ConditionMap conditionMap;
                        if (sourceMap.TryGetValue(EventNotification.SourceID, out conditionMap) == false)
                        {
                            conditionMap = new ConditionMap();
                            sourceMap.Add(EventNotification.SourceID, conditionMap);
                        }

                        if (conditionMap.TryGetValue(EventNotification.ConditionName, out cond) == false)
                        {
                            cond = new OPCCondition();
                            cond.EventType = EventNotification.EventType;
                            conditionMap.Add(EventNotification.ConditionName, cond);
                        }

                        ProcessCondition(EventNotification, areas, cond);

                        // When the condition has transitioned to Acked (if ack required) and inactive or disabled
                        // then remove it from the condition source/condition database
                        if ((!cond.IsActive() || !cond.IsEnabled()) && (cond.IsAcked() || !cond.AckRequired))
                            conditionMap.Remove(EventNotification.ConditionName);
                    }
                    else  // a tracking or simple event
                    {
                        cond = new OPCCondition();
                        cond.EventType = EventNotification.EventType;
                        ProcessCondition(EventNotification, areas, cond);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in ProcessEventNotificationList");
            }
        }

        /// <summary>
        /// Values of existing CONDITION events are updated the forwarded to TheGlobal for further processing
        /// per subscription per server instance
        /// </summary>
        /// <param name="EventNotification"></param>
        /// <param name="areas"></param>
        /// <param name="newCond"></param>
        private void ProcessCondition(EventNotification EventNotification, string[] areas, OPCCondition newCond)
        {
            try
            {
                newCond.ClearChangeMask();
                newCond.SetMessage(EventNotification.Message);
                newCond.SetSeverity(EventNotification.Severity);
                newCond.SetSubconditionName(EventNotification.SubConditionName);
                newCond.SetActive((EventNotification.NewState & OpcRcw.Ae.Constants.CONDITION_ACTIVE) != 0);

                // The UA server generates unique identifiers in the form of guids.  how to map these to COM-style cookies?
                // For now, just increment a static DWORD counter from the OPCCondition constructor and assign
                //newCond.Cookie = EventNotification.Cookie;

                newCond.SetTime(EventNotification.ActiveTime);
                newCond.EventId = EventNotification.EventId;
                newCond.EventCategory = EventNotification.EventCategory;
                newCond.SetQuality((short)EventNotification.Quality);
                newCond.ActorID = EventNotification.EventType == OpcRcw.Ae.Constants.TRACKING_EVENT ? EventNotification.ActorID : "";
                newCond.SetEnable((EventNotification.NewState & OpcRcw.Ae.Constants.CONDITION_ENABLED) != 0);
                newCond.SetIsAcked((EventNotification.NewState & OpcRcw.Ae.Constants.CONDITION_ACKED) != 0);
                newCond.SetAckRequired(EventNotification.AckRequired);
                newCond.ConditionId = EventNotification.ConditionId;
                newCond.AcknowledgeMethod = EventNotification.AcknowledgeMethod;

                OPCSubcondition subCond = new OPCSubcondition(newCond.SubconditionName, newCond.Message, "", newCond.Severity);
                newCond.push_back_subcondition(subCond);

                for (int i = 0; i < areas.Length; i++)
                    newCond.push_back_area(areas[i]);

                // Insert standard attribute "AckComment"
                newCond.push_back_attrval(Global.TheGlobal.StdAttrIds[0], newCond.AckComment);
                // Insert standard attribute "Areas"
                newCond.push_back_attrval(Global.TheGlobal.StdAttrIds[1], areas);

                foreach (KeyValuePair<int, object> kvp in EventNotification.EventAttributes)
                    newCond.push_back_attrval(kvp.Key, kvp.Value);

                if (newCond.IsEnabled())
                {
                    lock (Global.TheGlobal)
                    {
                        Global.TheGlobal.NotifyClients(EventNotification.SourceID, EventNotification.ConditionName, newCond);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in ProcessCondition");
            }

            // Utils.Trace("ProcessCondition - END Source: {0}, condition: {1}, conditionstate: {2}",
            // EventNotification.SourceID, EventNotification.ConditionName, newCond.NewState);
        }
    }

    /// <summary>
    /// Instances initialized as new "raw" UA events are received from the target UA server
    /// </summary>
    public class EventNotification
    {
        /// <summary>
        /// Source name
        /// </summary>
        public byte[] EventId
        {
            get { return m_eventId; }
            set { m_eventId = value; }
        }

        /// <summary>
        /// Source name
        /// </summary>
        public string SourceID
        {
            get { return m_sourceID; }
            set { m_sourceID = value; }
        }

        /// <summary>
        /// Time of event occurance
        /// </summary>
        public DateTime Time
        {
            get { return m_time; }
            set { m_time = value; }
        }
 
        /// <summary>
        /// Message associated with the event
        /// </summary>
        public string Message
        {
            get { return m_message; }
            set { m_message = value; }
        }

        /// <summary>
        /// Event type -- simple, tracking, condition
        /// </summary>
        public int EventType
        {
            get { return m_eventType; }
            set { m_eventType = value; }
        }

        /// <summary>
        /// Event Category
        /// </summary>
        public int EventCategory
        {
            get { return m_eventCategory; }
            set { m_eventCategory = value; }
        }
 
        /// <summary>
        /// Event Severity
        /// </summary>
        public int Severity
        {
            get { return m_severity; }
            set { m_severity = value; }
        }

        /// <summary>
        /// Condition name
        /// </summary>
        public string ConditionName
        {
            get { return m_conditionName; }
            set { m_conditionName = value; }
        }

        /// <summary>
        /// Name of last active subcondition
        /// </summary>
        public string SubConditionName
        {
            get { return m_subConditionName; }
            set { m_subConditionName = value; }
        }

        /// <summary>
        /// Change mask
        /// </summary>
        public int ChangeMask
        {
            get { return m_changeMask; }
            set { m_changeMask = value; }
        }

        /// <summary>
        /// New state
        /// </summary>
        public int NewState
        {
            get { return m_newState; }
            set { m_newState = value; }
        }

        /// <summary>
        /// Quality
        /// </summary>
        public short Quality
        {
            get { return m_quality; }
            set { m_quality = value; }
        }

        /// <summary>
        /// Acknowledgement required
        /// </summary>
        public bool AckRequired
        {
            get { return m_ackRequired; }
            set { m_ackRequired = value; }
        }

        /// <summary>
        /// Time of last transition to active
        /// </summary>
        public DateTime ActiveTime
        {
            get { return m_activeTime; }
            set { m_activeTime = value; }
        }

        /// <summary>
        /// Cookie
        /// </summary>
        public int Cookie
        {
            get { return m_cookie; }
            set { m_cookie = value; }
        }
 
        /// <summary>
        /// Actor ID
        /// </summary>
        public string ActorID
        {
            get { return m_actorID; }
            set { m_actorID = value; }
        }

        /// <summary>
        /// ConditionId
        /// </summary>
        public NodeId ConditionId
        {
            get { return m_ConditionId; }
            set { m_ConditionId = value; }
        }

        /// <summary>
        /// AcknowledgeMethod
        /// </summary>
        public NodeId AcknowledgeMethod
        {
            get { return m_AcknowledgeMethod; }
            set { m_AcknowledgeMethod = value; }
        }

        /// <summary>
        /// Associated event attribute IDs/Values
        /// </summary>
        public Dictionary<int, object> EventAttributes
        {
            get { return m_EventAttributes; }
            set { m_EventAttributes = value; }
        }

        private string m_sourceID = null;
        private DateTime m_time = DateTime.MinValue;
        private string m_message = null;
        private int m_eventType = OpcRcw.Ae.Constants.CONDITION_EVENT;
        private int m_eventCategory = 0;
        private byte[] m_eventId = null;
        private int m_severity = 1;
        private string m_conditionName = null;
        private string m_subConditionName = null;
        private int m_changeMask = 0;
        private int m_newState = 0;
        private short m_quality = (short)Qualities.OPC_QUALITY_GOOD;
        private bool m_ackRequired = false;
        private DateTime m_activeTime = DateTime.MinValue;
        private int m_cookie = 0;
        private string m_actorID = null;
        private NodeId m_ConditionId = null;
        private NodeId m_AcknowledgeMethod = null;
        private Dictionary<int, object> m_EventAttributes = new Dictionary<int, object>();
    }

}
