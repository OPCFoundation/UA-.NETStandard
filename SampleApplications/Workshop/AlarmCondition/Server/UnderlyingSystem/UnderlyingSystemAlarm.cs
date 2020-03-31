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
using Opc.Ua;

namespace Quickstarts.AlarmConditionServer
{
    /// <summary>
    /// This class stores the state of a alarm known to the system.
    /// </summary>
    /// <remarks>
    /// This class only stores the information about an alarm that a system has. The
    /// system has no concept of the UA information model and the NodeManager must 
    /// convert the information stored in this class into the UA equivalent.
    /// </remarks>
    public class UnderlyingSystemAlarm
    {
        #region Public Members
        /// <summary>
        /// The source that the alarm belongs to
        /// </summary>
        /// <value>The source.</value>
        public UnderlyingSystemSource Source
        {
            get { return m_source; }
            set { m_source = value; }
        }

        /// <summary>
        /// Gets or sets the name of the alarm.
        /// </summary>
        /// <value>The name of the alarm.</value>
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// Gets or sets the type of the alarm.
        /// </summary>
        /// <value>The type of the alarm.</value>
        public string AlarmType
        {
            get { return m_alarmType; }
            set { m_alarmType = value; }
        }

        /// <summary>
        /// Gets or sets a unique record number assigned to an archived snapshot of the alarm.
        /// </summary>
        /// <value>The record number assigned to an archived snapshot of the alarm.</value>
        /// <remarks>
        /// Past state transitions are assigned a record number when they are archived. This
        /// Record number allows the system to updated archived record. This number is 0 if
        /// the state transition has not been archived. 
        /// </remarks>
        public uint RecordNumber
        {
            get { return m_recordNumber; }
            set { m_recordNumber = value; }
        }

        /// <summary>
        /// Gets or sets the time when the alarm last changed state.
        /// </summary>
        /// <value>The last state change time.</value>
        public DateTime Time
        {
            get { return m_time; }
            set { m_time = value; }
        }

        /// <summary>
        /// Gets or sets the reason for the last state change.
        /// </summary>
        /// <value>The reason for the last state change.</value>
        public string Reason
        {
            get { return m_reason; }
            set { m_reason = value; }
        }

        /// <summary>
        /// Gets or sets the severity of the alarm.
        /// </summary>
        /// <value>The alarm severity.</value>
        public EventSeverity Severity
        {
            get { return m_severity; }
            set { m_severity = value; }
        }

        /// <summary>
        /// Gets or sets the comment associated with the alarm.
        /// </summary>
        /// <value>The comment.</value>
        public string Comment
        {
            get { return m_comment; }
            set { m_comment = value; }
        }

        /// <summary>
        /// Gets or sets the name of the user that provided the comment.
        /// </summary>
        /// <value>The name of the user that provided the comment.</value>
        public string UserName
        {
            get { return m_userName; }
            set { m_userName = value; }
        }

        /// <summary>
        /// Gets or sets the current alarm state.
        /// </summary>
        /// <value>The current alarm state.</value>
        public UnderlyingSystemAlarmStates State
        {
            get { return m_state; }
            set { m_state = value; }
        }

        /// <summary>
        /// Gets or sets the time when the alarm went into the enabled state.
        /// </summary>
        /// <value>When the alarm went into the enabled state.</value>
        public DateTime EnableTime
        {
            get { return m_enableTime; }
            set { m_enableTime = value; }
        }

        /// <summary>
        /// Gets or sets the time when the alarm went into the active state.
        /// </summary>
        /// <value>When the alarm went into the active state.</value>
        public DateTime ActiveTime
        {
            get { return m_activeTime; }
            set { m_activeTime = value; }
        }

        /// <summary>
        /// Gets or sets the limits that apply to the alarm.
        /// </summary>
        /// <value>The limits that apply to the alarm.</value>
        /// <remarks>
        /// 1 limit = High
        /// 2 limits = High, Low
        /// 4 limits = HighHigh, High, Low, LowLow
        /// </remarks>
        public double[] Limits
        {
            get { return m_limits; }
            set { m_limits = value; }
        }

        /// <summary>
        /// Creates a snapshort of the alarm.
        /// </summary>
        /// <returns>The snapshot,</returns>
        public UnderlyingSystemAlarm CreateSnapshot()
        {
            return (UnderlyingSystemAlarm)MemberwiseClone();
        }

        /// <summary>
        /// Sets or clears the bits in the alarm state mask.
        /// </summary>
        /// <param name="bits">The bits.</param>
        /// <param name="isSet">if set to <c>true</c> the bits are set; otherwise they are cleared.</param>
        /// <returns>True if the state changed as a result of setting the bits.</returns>
        public bool SetStateBits(UnderlyingSystemAlarmStates bits, bool isSet)
        {
            if (isSet)
            {
                bool currentlySet = ((m_state & bits) == bits);
                m_state |= bits;
                return !currentlySet;
            }

            bool currentlyCleared = ((m_state & ~bits) == m_state);
            m_state &= ~bits;
            return !currentlyCleared;
        }
        #endregion

        #region Private Fields
        private UnderlyingSystemSource m_source;
        private string m_name;
        private uint m_recordNumber;
        private string m_alarmType;
        private DateTime m_time;
        private string m_reason;
        private EventSeverity m_severity;
        private string m_comment;
        private string m_userName;
        private UnderlyingSystemAlarmStates m_state;
        private DateTime m_enableTime;
        private DateTime m_activeTime;
        private double[] m_limits;
        #endregion
    }
}
