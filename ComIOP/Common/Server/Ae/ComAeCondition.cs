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
using System.Runtime.InteropServices;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using OpcRcw.Ae;
using OpcRcw.Da;

namespace Opc.Ua.Com.Server.Ae
{
    /// <summary>
    /// Maps ConditionName to instance of OPCCondition class
    /// </summary>
    public class ConditionMap : Dictionary<string, OPCCondition>
    {
    }

    /// <summary>
    /// Maps event source name to conditionMAP.  One instance of SourceMap
    /// per process.
    /// </summary>
    public class SourceMap : Dictionary<string, ConditionMap>
    {
        private SourceMap() { }

        /// <summary>
        /// Accessor for static SourceMap instance
        /// </summary>
        public static SourceMap TheSourceMap
        {
            get { return theSourceMap; }
        }

        private static SourceMap theSourceMap = new SourceMap();
    }

    /// <summary>
    /// Specialized List which does not permit duplicate entries
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class UniqueList<T> : List<T>
    {
        /// <summary>
        /// Adds 'item' to list if item is not already present
        /// </summary>
        /// <param name="item"></param>
        public void AddUnique(T item)
        {
            if (!Contains(item))
                Add(item);
        }
    }

    /// <summary>
    /// Specialized Queue which never contains more than one item
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class EventItemQueue<T> : Queue<T>
    {
        /// <summary>
        /// Enqueues if empty.
        /// </summary>
        /// <param name="item">The item.</param>
        public void EnqueueIfEmpty(T item)
        {
            if (Count == 0)
                base.Enqueue(item);
        }
    }

    /// <summary>
    /// Struct representing an OPC Subcondition
    /// </summary>
    public struct OPCSubcondition
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="wszName"></param>
        public OPCSubcondition(string wszName)
        {
            m_wsName = wszName;
            m_wsMessage = "";
            m_wsDefinition = "";
            m_dwSeverity = 1;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="wszName"></param>
        /// <param name="wszMessage"></param>
        /// <param name="wszDefinition"></param>
        /// <param name="dwSeverity"></param>
        public OPCSubcondition(string wszName, string wszMessage, string wszDefinition, int dwSeverity)
        {
            m_wsName = wszName;
            m_wsMessage = wszMessage;
            m_wsDefinition = wszDefinition;
            m_dwSeverity = dwSeverity;
        }

        /// <summary>
        /// Name property
        /// </summary>
        public string Name
        {
            get { return m_wsName; }
        }

        /// <summary>
        /// Message property
        /// </summary>
        public string Message
        {
            get { return m_wsMessage; }
        }

        /// <summary>
        /// Subcondition definition property
        /// </summary>
        public string Definition
        {
            get { return m_wsDefinition; }
        }

        /// <summary>
        /// Subcondition Severity property
        /// </summary>
        public int Severity
        {
            get { return m_dwSeverity; }
        }

        /// <summary>
        /// Subcondition comparison operator
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(OPCSubcondition left, OPCSubcondition right)
        {
            return (left.m_wsName == right.m_wsName);
        }

        /// <summary>
        /// Subcondition comparison operator
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(OPCSubcondition left, OPCSubcondition right)
        {
            return (left.m_wsName != right.m_wsName);
        }

        /// <summary>
        /// Required override
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        /// <summary>
        /// Required override
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(Object obj)
        {
            if (obj == null || GetType() != obj.GetType()) return false;
            OPCSubcondition sub = (OPCSubcondition)obj;
            return (m_wsName == sub.m_wsName);
        }


        #region Private Members
        private string m_wsName;
        private string m_wsMessage;
        private string m_wsDefinition;
        private int m_dwSeverity;
        #endregion
    }

    /// <summary>
    /// OPCCondition class definition.  Instances of OPCCondition represent a unique source/condition pair
    /// and persist within the SourceMap until such time that the condition transitions to acked
    /// (if ACK is required) and inactive.  OPCCondition class instances are also used to forward Simple and
    /// Tracking event information but these instances do not persist within the SourceMap
    /// </summary>
    public class OPCCondition
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public OPCCondition()
        {
            ClearChangeMask();
            m_EventId = null;
            m_dwEventCategory = 0;
            m_dwSeverity = 0;
            m_dwEventType = 0;
            m_dwEventCategory = 0;
            m_dwSeverity = 0;
            m_bEnabled = true;
            m_wNewState = (short)(m_bEnabled ? OpcRcw.Ae.Constants.CONDITION_ENABLED : 0);
            m_wQuality = (short)Qualities.OPC_QUALITY_GOOD;
            m_bAckRequired = false;
            m_dwCookie = m_dwCookieCounter++;
            m_wsAckComment = "";
            m_wsActorID = "";
            m_bInitialEnableSet = false;
            m_bInitialQualitySet = false;
            m_bInitialAckSet = false;
            m_bInitialActiveSet = false;
            m_bInitialActiveTimeSet = false;
            m_ConditionId = null;
            m_AcknowledgeMethod = null;
            m_bAckPending = false;
            m_ftAckPendingStartTime = DateTime.MinValue;
        }

        /// <summary>
        /// ChangeMask Property
        /// </summary>
        public short ChangeMask
        {
            get { return m_wChangeMask; }
        }

        /// <summary>
        /// EventID Property
        /// </summary>
        public byte[] EventId
        {
            get { return m_EventId; }
            set { m_EventId = value; }
        }

        /// <summary>
        /// NewState property
        /// </summary>
        public short NewState
        {
            get { return m_wNewState; }
        }

        /// <summary>
        /// Time (of last change) property
        /// </summary>
        public DateTime Time
        {
            get { return m_ftTime; }
        }

        /// <summary>
        /// Message property
        /// </summary>
        public string Message
        {
            get { return m_wsMessage; }
        }

        /// <summary>
        /// EventType -- Simple, Tracking, Condition -- property
        /// </summary>
        public int EventType
        {
            get { return m_dwEventType; }
            set { m_dwEventType = value; }
        }

        /// <summary>
        /// Category property
        /// </summary>
        public int EventCategory
        {
            get { return m_dwEventCategory; }
            set { m_dwEventCategory = value; }
        }

        /// <summary>
        /// Severity property
        /// </summary>
        public int Severity
        {
            get { return m_dwSeverity; }
        }

        /// <summary>
        /// Currently active Subcondition name property
        /// </summary>
        public string SubconditionName
        {
            get { return m_wsSubconditionName; }
        }

        /// <summary>
        /// Quality property
        /// </summary>
        public short Quality
        {
            get { return m_wQuality; }
        }

        /// <summary>
        /// AckRequired property
        /// </summary>
        public bool AckRequired
        {
            get { return m_bAckRequired; }
        }

        /// <summary>
        /// ActiveTime property
        /// </summary>
        public DateTime ActiveTime
        {
            get { return m_ftActiveTime; }
        }

        /// <summary>
        /// Time of last active subcondition
        /// </summary>
        public DateTime SubconditionTime
        {
            get { return m_ftSubconditionTime; }
        }

        /// <summary>
        /// Time of last acknowledgement
        /// </summary>
        public DateTime LastAckTime
        {
            get { return m_ftLastAckTime; }
        }

        /// <summary>
        /// Time the condition transitioned to inactive
        /// </summary>
        public DateTime RTNTime
        {
            get { return m_ftRTNTime; }
        }

        /// <summary>
        /// Cookie property
        /// </summary>
        public int Cookie
        {
            get { return m_dwCookie; }
            set { m_dwCookie = value; }
        }

        /// <summary>
        /// ActorID property
        /// </summary>
        public string ActorID
        {
            get { return m_wsActorID; }
            set { m_wsActorID = value; }
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
        /// AckComment property
        /// </summary>
        public string AckComment
        {
            get { return m_wsAckComment; }
        }

        /// <summary>
        /// Event attributes property
        /// </summary>
        public Dictionary<int, object> EventAttributes
        {
            get { return m_EventAttributes; }
        }

        /// <summary>
        /// Condition is enabled
        /// </summary>
        public bool Enabled
        {
            get { return m_bEnabled; }
        }

        /// <summary>
        /// Returns list of associated areas associated
        /// </summary>
        public List<string> Areas
        {
            get { return m_Areas; }
        }

        /// <summary>
        /// Returns list of associated subconditions
        /// </summary>
        public List<OPCSubcondition> Subconditions
        {
            get { return m_Subconditions; }
        }
        
        /// <summary>
        /// Is the active bit set in NewState ?
        /// </summary>
        /// <returns></returns>
        public bool IsActive()
        {
            return ((m_wNewState & OpcRcw.Ae.Constants.CONDITION_ACTIVE) !=0);
        }

        /// <summary>
        /// Called from AckCondition -- includes special handling for the case when
        /// The ack has been forwarded to the target UA server but confirmation of the
        /// ack has not yet been received.  In this case we will want wait for a max
        /// period of time ('ftAckWaitTime'), just in case the ack from the target is
        /// on its way. dateValue.ToString("hh:mm:ss.fff tt");                        
        /// </summary>
        /// <returns></returns>
        public bool IsAckedOrWaiting(double AckWaitTime)
        {
            //  Utils.Trace("IsAckedOrWaiting (AckWaitTime: {0}) => BEGIN", AckWaitTime);
            if ((m_wNewState & OpcRcw.Ae.Constants.CONDITION_ACKED) != 0)
            {
                // Condition acknowledgement event has been received from the server
                // Utils.Trace("IsAckedOrWaiting: Condition is ACKED - return true");
                return true;
            }
            else if (m_bAckPending == false)
            {
                // Set a flag indicating that we are waiting for the ack confirmation from the server
                m_bAckPending = true;
                m_ftAckPendingStartTime = DateTime.Now;
                // Utils.Trace("IsAckedOrWaiting: set bAckPending == true, AckPendingStartTime == {0}", m_ftAckPendingStartTime.ToString("hh:mm:ss.fff"));
                return false;
            }
            else
            {
                DateTime waitUntil = m_ftAckPendingStartTime + new TimeSpan((long)AckWaitTime * TimeSpan.TicksPerMillisecond); 
                m_bAckPending = false;
                // Wait out the remaining "wait for ack confirmation" period
                // If we haven't received the condition ack event from the server
                // after this period then return false
                Utils.Trace("IsAckedOrWaiting: bAckPending is true, waitUntil -> {0}, Now -> {1}", waitUntil.ToString("hh:mm:ss.fff"), DateTime.Now.ToString("hh:mm:ss.fff"));
                while (DateTime.Now < waitUntil)
                {
                    System.Threading.Thread.Sleep(100);
                    if ((m_wNewState & OpcRcw.Ae.Constants.CONDITION_ACKED) != 0)
                        break;
                }

                Utils.Trace("IsAckedOrWaiting: set bAckPending == false, Now -> {0}", DateTime.Now.ToString("hh:mm:ss.fff"));
                return ((m_wNewState & OpcRcw.Ae.Constants.CONDITION_ACKED) != 0);  
            }
        }

        /// <summary>
        /// Is the Acked bit set in NewState ?
        /// </summary>
        /// <returns></returns>
        public bool IsAcked()
        {
            return ((m_wNewState & OpcRcw.Ae.Constants.CONDITION_ACKED) != 0);
        }

        /// <summary>
        /// Is the enabled bit set in NewState ?
        /// </summary>
        /// <returns></returns>
        public bool IsEnabled()
        {
            return ((m_wNewState & OpcRcw.Ae.Constants.CONDITION_ENABLED) !=0);
        }

        /// <summary>
        /// Number of attributes
        /// </summary>
        /// <returns></returns>
        public int sizeEventAttributes()
        {
            return m_EventAttributes.Count;
        }

        /// <summary>
        /// Reset the change mask
        /// </summary>
        public void ClearChangeMask()
        {
            m_wChangeMask = 0;
        }

        /*
        public bool IsEffectivelyEnabled();
        public bool AreAreasEffectivelyEnabled();
        public bool UpdateEffectiveState();
        */

        /// <summary>
        /// Adjusts the Enable bit of the NewState and ChangeMask 
        /// </summary>
        /// <param name="bEnable"></param>
        public void SetEnable(bool bEnable)
        {
            if ((bEnable != IsEnabled()) || !m_bInitialEnableSet)
            {
                if (bEnable)
                    m_wNewState |= OpcRcw.Ae.Constants.CONDITION_ENABLED;
                else
                    m_wNewState &= ~OpcRcw.Ae.Constants.CONDITION_ENABLED;

                m_wChangeMask |= OpcRcw.Ae.Constants.CHANGE_ENABLE_STATE;
                m_bInitialEnableSet = true;
            }
            //	UpdateEffectiveState();
        }

        /// <summary>
        /// Adjusts the Active bit of the NewState and ChangeMask
        /// </summary>
        /// <param name="bActive"></param>
        public void SetActive(bool bActive)
        {
            if ((bActive != IsActive()) || !m_bInitialActiveSet)
            {
                m_wChangeMask |= OpcRcw.Ae.Constants.CHANGE_ACTIVE_STATE;

                if (bActive)   // transitioned into alarm
                    m_wNewState |= OpcRcw.Ae.Constants.CONDITION_ACTIVE;
                else  // return to normal
                    m_wNewState &= ~OpcRcw.Ae.Constants.CONDITION_ACTIVE;

                m_bInitialActiveSet = true;
            }
        }
        
        /// <summary>
        /// Adjusts the Acked bit of the NewState and ChangeMask.  Unlike method 'Ack' above this method is
        /// not called as a direct result of AckCondition.  Rather, events received from the target UA server may
        /// have become acknowledged out of band (e.g., via acknowledgement from a UA client).
        /// </summary>
        /// <param name="bIsAcked"></param>
        public void SetIsAcked(bool bIsAcked)
        {
            if ((bIsAcked != IsAcked()) || !m_bInitialAckSet)
            {
                m_wChangeMask |= OpcRcw.Ae.Constants.CHANGE_ACK_STATE;

                if (bIsAcked)   // transitioned into acked
                {
                    m_wNewState |= OpcRcw.Ae.Constants.CONDITION_ACKED;
                    m_bAckRequired = false;
                    m_ftLastAckTime = m_ftTime;
                    m_bAckPending = false;
                }
                else  // return to unacked
                    m_wNewState &= ~OpcRcw.Ae.Constants.CONDITION_ACKED;

                m_bInitialAckSet = true;
            }
        }


        /// <summary>
        /// Time of last state change
        /// </summary>
        /// <param name="ftTime"></param>
        public void SetTime(DateTime ftTime)
        {
            m_ftTime = ftTime;
            if (IsActive() || !m_bInitialActiveTimeSet)
            {
                if ((m_wChangeMask & OpcRcw.Ae.Constants.CHANGE_ACTIVE_STATE) !=0)
                {
                    m_ftActiveTime = m_ftTime;
                    m_ftSubconditionTime = m_ftTime;
                }
                else if ((m_wChangeMask & OpcRcw.Ae.Constants.CHANGE_SUBCONDITION) !=0)
                    m_ftSubconditionTime = m_ftTime;

                m_bInitialActiveTimeSet = true;
            }
            else
            {
                if ((m_wChangeMask & OpcRcw.Ae.Constants.CHANGE_ACTIVE_STATE) !=0)
                    m_ftRTNTime = m_ftTime; 
            }
        }


        /// <summary>
        /// Time of last state change is now
        /// </summary>
        public void SetTime()  // time of event is now
        {
            DateTime ftNow = DateTime.Now;
            SetTime(ftNow);
        }

        /// <summary>
        /// Message has changed
        /// </summary>
        /// <param name="wszString"></param>
        public void SetMessage(string wszString)
        {
            if (m_wsMessage != wszString)
            {
                m_wsMessage = wszString;
                m_wChangeMask |= OpcRcw.Ae.Constants.CHANGE_MESSAGE;
            }
        }

        /// <summary>
        /// Set severity and adjust the ChangeMask
        /// </summary>
        /// <param name="dwSeverity"></param>
        public void SetSeverity(int dwSeverity)
        {
            if (dwSeverity != m_dwSeverity)
            {
                m_dwSeverity = dwSeverity;
                m_wChangeMask |= OpcRcw.Ae.Constants.CHANGE_SEVERITY;
            }
        }

        /// <summary>
        /// Set the currently active subcondition name and adjust the ChangeMask
        /// </summary>
        /// <param name="wszSubconditionName"></param>
        public void SetSubconditionName(string wszSubconditionName)
        {
            if (m_wsSubconditionName != wszSubconditionName)
            {
                m_wsSubconditionName = wszSubconditionName;
                m_wChangeMask |= OpcRcw.Ae.Constants.CHANGE_SUBCONDITION;
            }
        }


        /// <summary>
        /// Set the quality and adjust the ChangeMask
        /// </summary>
        /// <param name="wQuality"></param>
        public void SetQuality(short wQuality)
        {
            if ((m_wQuality != wQuality) || !m_bInitialQualitySet)
            {
                m_wQuality = wQuality;
                m_wChangeMask |= OpcRcw.Ae.Constants.CHANGE_QUALITY;
                m_bInitialQualitySet = true;
            }
        }

        /// <summary>
        /// Apply the AckRequired setting when transitioning to Active
        /// </summary>
        /// <param name="bAckReq"></param>
        public void SetAckRequired(bool bAckReq)
        {
            m_bAckRequired = bAckReq;
        }

        /// <summary>
        /// Inserts attribute ID/Value pair into the attribute values map
        /// </summary>
        /// <param name="dwAttrID"></param>
        /// <param name="AnEventAttribute"></param>
        public void push_back_attrval(int dwAttrID, object AnEventAttribute)
        {
            object value = null;

            if (m_EventAttributes.TryGetValue(dwAttrID, out value))
            {
                // TODO: Need to perform a "real" comparison on the attribute value --
                // this just compares pointers and will always evaluate true
                if (value != AnEventAttribute)
                {
                    m_EventAttributes[dwAttrID] = AnEventAttribute;
                    m_wChangeMask |= OpcRcw.Ae.Constants.CHANGE_ATTRIBUTE;
                }
            }
            else
            {
                m_EventAttributes[dwAttrID] = AnEventAttribute;
                m_wChangeMask |= OpcRcw.Ae.Constants.CHANGE_ATTRIBUTE;
            }
        }

        /// <summary>
        /// Deletes the contents of the attribute values map
        /// </summary>
        public void clear()
        {
            m_EventAttributes.Clear();
        }

        /// <summary>
        /// Inserts an area name into the areas list
        /// </summary>
        /// <param name="sArea"></param>
        public void push_back_area(string sArea)
        {
            m_Areas.AddUnique(sArea);
        }

        /// <summary>
        /// Insertas a subcondition into the subconditions list
        /// </summary>
        /// <param name="sub"></param>
        public void push_back_subcondition(OPCSubcondition sub)
        {
            m_Subconditions.AddUnique(sub);
        }


        #region Private Members
        private byte[] m_EventId;
        private short m_wChangeMask;
        private short m_wNewState;
        private DateTime m_ftTime;
        private DateTime m_ftLastAckTime;
        private string m_wsMessage;
        private int m_dwCookie;
        private int m_dwEventType;
        private int m_dwEventCategory;
        private int m_dwSeverity;
        private string m_wsSubconditionName;
        private short m_wQuality;
        private bool m_bAckRequired;
        private DateTime m_ftActiveTime;
        private DateTime m_ftSubconditionTime;
        private DateTime m_ftRTNTime;
        private string m_wsActorID;
        private string m_wsAckComment;
        private bool m_bEnabled;
        private bool m_bInitialEnableSet;
        private bool m_bInitialQualitySet;
        private bool m_bInitialAckSet;
        private bool m_bInitialActiveSet;
        private bool m_bInitialActiveTimeSet;
        private NodeId m_ConditionId;
        private NodeId m_AcknowledgeMethod;
        private bool m_bAckPending;
        private DateTime m_ftAckPendingStartTime;

        /// <summary>
        /// Simplistics means to generate a unique cookie
        /// </summary>
        static public int m_dwCookieCounter = 0;

        private Dictionary<int, object> m_EventAttributes = new Dictionary<int, object>();
        private UniqueList<string> m_Areas = new UniqueList<string>();
        private UniqueList<OPCSubcondition> m_Subconditions = new UniqueList<OPCSubcondition>();
        #endregion

    }

    /// <summary>
    /// Holds the contents of one ONEVENTSTRUCT.  Instances are pushed into each subscription's
    /// pending events queue where the subscription filter applies to the event.
    /// </summary>
    public class OnEventClass
    {
        /// <summary>
        /// Constructor -- initializes all members
        /// </summary>
        /// <param name="wszSource"></param>
        /// <param name="wszCondition"></param>
        /// <param name="cond"></param>
        public OnEventClass(string wszSource, string wszCondition, OPCCondition cond)
        {
            m_oes.pEventAttributes = IntPtr.Zero;
            m_oes.dwNumEventAttrs = 0;
            m_oes.wChangeMask = cond.ChangeMask;
            m_oes.wNewState = cond.NewState;
            m_oes.szSource = wszSource;
            m_oes.ftTime = ComUtils.GetFILETIME(cond.Time);
            m_oes.szMessage = cond.Message;
            m_oes.dwEventType = cond.EventType;
            m_oes.dwEventCategory = cond.EventCategory;
            m_oes.dwSeverity = cond.Severity;
            m_oes.szConditionName = wszCondition;
            m_oes.szSubconditionName = cond.SubconditionName;
            m_oes.wQuality = cond.Quality;
            m_oes.bAckRequired = cond.AckRequired ?1:0;
            m_oes.ftActiveTime = ComUtils.GetFILETIME(cond.SubconditionTime);
            m_oes.dwCookie = cond.Cookie;
            m_oes.szActorID = cond.ActorID;

            m_EventAttributes = (Dictionary<int, object>) DeepCopy (cond.EventAttributes);
        }

        /// <summary>
        /// Performs a deep copy of the event attributes map/dictionary
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        private object DeepCopy(object obj)
        {
            MemoryStream ms = new MemoryStream();
            BinaryFormatter bf = new BinaryFormatter();
            bf.Serialize(ms, obj);

            object newObj;
            ms.Seek(0, SeekOrigin.Begin);
            newObj = bf.Deserialize(ms);
            ms.Close();
            return newObj;
        } 

        /// <summary>
        /// Returns the event attributes map
        /// </summary>
        public Dictionary<int, object> EventAttributes
        {
            get { return m_EventAttributes; }
        }

        /// <summary>
        /// A COM compatible ONEVENTSTRUCT
        /// </summary>
        public ONEVENTSTRUCT InternalOES
        {
            get { return m_oes; }
        }

        private ONEVENTSTRUCT m_oes;
        private Dictionary<int, object> m_EventAttributes;

    }
}
