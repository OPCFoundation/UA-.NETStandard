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
    /// This class simulates a source in the system.
    /// </summary>
    public class UnderlyingSystemSource
    {
        #region Public Members
        /// <summary>
        /// Initializes a new instance of the <see cref="UnderlyingSystemSource"/> class.
        /// </summary>
        public UnderlyingSystemSource()
        {
            m_alarms = new List<UnderlyingSystemAlarm>();
            m_archive = new Dictionary<uint, UnderlyingSystemAlarm>();
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Used to receive events when the state of an alarm changed.
        /// </summary>
        public AlarmChangedEventHandler OnAlarmChanged;
        
        /// <summary>
        /// Gets or sets the name of the source.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// Gets or sets the fully qualified name for the source.
        /// </summary>
        /// <value>The fully qualified name for a source.</value>
        public string SourcePath
        {
            get { return m_sourcePath; }
            set { m_sourcePath = value; }
        }

        /// <summary>
        /// Gets or sets the type of the source.
        /// </summary>
        /// <value>The type of the source.</value>
        public string SourceType
        {
            get { return m_sourceType; }
            set { m_sourceType = value; }
        }

        /// <summary>
        /// Creates a new active alarm for the source.
        /// </summary>
        /// <param name="alarmName">Name of the alarm.</param>
        /// <param name="alarmType">Type of the alarm.</param>
        public void CreateAlarm(string alarmName, string alarmType)
        {
            UnderlyingSystemAlarm alarm = new UnderlyingSystemAlarm();

            alarm.Source = this;
            alarm.Name = alarmName;
            alarm.AlarmType = alarmType;
            alarm.RecordNumber = 0;
            alarm.Reason = "Alarm created.";
            alarm.Time = DateTime.UtcNow;
            alarm.Severity = EventSeverity.Low;
            alarm.Comment = null;
            alarm.UserName = null;
            alarm.State = UnderlyingSystemAlarmStates.Active | UnderlyingSystemAlarmStates.Enabled;
            alarm.EnableTime = DateTime.UtcNow;
            alarm.ActiveTime = DateTime.UtcNow;

            switch (alarmType)
            {
                case "HighAlarm":
                {
                    alarm.Limits = new double[] { 80 };
                    alarm.State |= UnderlyingSystemAlarmStates.High;
                    break;
                }

                case "HighLowAlarm":
                {
                    alarm.Limits = new double[] { 90, 70, 30, 10 };
                    alarm.State |= UnderlyingSystemAlarmStates.High;
                    break;
                }
            }

            lock (m_alarms)
            {
                m_alarms.Add(alarm);
            }
        }

        /// <summary>
        /// Enables or disables the alarm.
        /// </summary>
        /// <param name="alarmName">Name of the alarm.</param>
        /// <param name="enabling">if set to <c>true</c> the alarm is enabled.</param>
        public void EnableAlarm(string alarmName, bool enabling)
        {
            List<UnderlyingSystemAlarm> snapshots = new List<UnderlyingSystemAlarm>();

            lock (m_alarms)
            {
                UnderlyingSystemAlarm alarm = FindAlarm(alarmName, 0);

                if (alarm != null)
                {
                    // enable/disable the alarm.
                    if (alarm.SetStateBits(UnderlyingSystemAlarmStates.Enabled, enabling))
                    {
                        alarm.Time = alarm.EnableTime = DateTime.UtcNow;
                        alarm.Reason = "The alarm was " + ((enabling)?"enabled.":"disabled.");
                        snapshots.Add(alarm.CreateSnapshot());
                    }

                    // enable/disable any archived records for the alarm.
                    foreach (UnderlyingSystemAlarm record in m_archive.Values)
                    {
                        if (record.Name != alarmName)
                        {
                            continue;
                        }

                        if (record.SetStateBits(UnderlyingSystemAlarmStates.Enabled, enabling))
                        {
                            record.Time = alarm.EnableTime = DateTime.UtcNow;
                            record.Reason = "The alarm was " + ((enabling)?"enabled.":"disabled.");
                            snapshots.Add(alarm.CreateSnapshot());
                        }
                    }
                }
            }

            // report any alarm changes after releasing the lock.
            for (int ii = 0; ii < snapshots.Count; ii++)
            {
                ReportAlarmChange(snapshots[ii]);
            }
        }

        /// <summary>
        /// Adds a comment to an alarm.
        /// </summary>
        /// <param name="alarmName">Name of the alarm.</param>
        /// <param name="recordNumber">The record number.</param>
        /// <param name="comment">The comment.</param>
        /// <param name="userName">Name of the user.</param>
        public void CommentAlarm(string alarmName, uint recordNumber, LocalizedText comment, string userName)
        {
            UnderlyingSystemAlarm snapshot = null;

            lock (m_alarms)
            {
                UnderlyingSystemAlarm alarm = FindAlarm(alarmName, recordNumber);

                if (alarm != null)
                {

                    alarm.Time = DateTime.UtcNow;
                    alarm.Reason = "A comment was added.";
                    alarm.UserName = userName;

                    // only change the comment if a non-null comment was provided.
                    if (comment != null && (!String.IsNullOrEmpty(comment.Text) || !String.IsNullOrEmpty(comment.Locale)))
                    {
                        alarm.Comment = Utils.Format("{0}", comment);
                    }

                    snapshot = alarm.CreateSnapshot();
                }
            }

            if (snapshot != null)
            {
                ReportAlarmChange(snapshot);
            }
        }

        /// <summary>
        /// Acknowledges an alarm.
        /// </summary>
        /// <param name="alarmName">Name of the alarm.</param>
        /// <param name="recordNumber">The record number.</param>
        /// <param name="comment">The comment.</param>
        /// <param name="userName">Name of the user.</param>
        public void AcknowledgeAlarm(string alarmName, uint recordNumber, LocalizedText comment, string userName)
        {
            UnderlyingSystemAlarm snapshot = null;

            lock (m_alarms)
            {
                UnderlyingSystemAlarm alarm = FindAlarm(alarmName, recordNumber);

                if (alarm != null)
                {
                    if (alarm.SetStateBits(UnderlyingSystemAlarmStates.Acknowledged, true))
                    {
                        alarm.Time = DateTime.UtcNow;
                        alarm.Reason = "The alarm was acknoweledged.";
                        alarm.Comment = Utils.Format("{0}", comment);
                        alarm.UserName = userName;
                        
                        alarm.SetStateBits(UnderlyingSystemAlarmStates.Confirmed, false);
                    }

                    snapshot = alarm.CreateSnapshot();
                }
            }

            if (snapshot != null)
            {
                ReportAlarmChange(snapshot);
            }
        }

        /// <summary>
        /// Confirms an alarm.
        /// </summary>
        /// <param name="alarmName">Name of the alarm.</param>
        /// <param name="recordNumber">The record number.</param>
        /// <param name="comment">The comment.</param>
        /// <param name="userName">Name of the user.</param>
        public void ConfirmAlarm(string alarmName, uint recordNumber, LocalizedText comment, string userName)
        {
            UnderlyingSystemAlarm snapshot = null;

            lock (m_alarms)
            {
                UnderlyingSystemAlarm alarm = FindAlarm(alarmName, recordNumber);

                if (alarm != null)
                {
                    if (alarm.SetStateBits(UnderlyingSystemAlarmStates.Confirmed, true))
                    {
                        alarm.Time = DateTime.UtcNow;
                        alarm.Reason = "The alarm was confirmed.";
                        alarm.Comment = Utils.Format("{0}", comment);
                        alarm.UserName = userName;

                        // remove branch.
                        if (recordNumber != 0)
                        {
                            m_archive.Remove(recordNumber);
                            alarm.SetStateBits(UnderlyingSystemAlarmStates.Deleted, true);
                        }

                        // de-activate alarm.
                        else
                        {
                            alarm.SetStateBits(UnderlyingSystemAlarmStates.Active, false);
                        }
                    }

                    snapshot = alarm.CreateSnapshot();
                }
            }

            if (snapshot != null)
            {
                ReportAlarmChange(snapshot);
            }
        }

        /// <summary>
        /// Reports the current state of all conditions.
        /// </summary>
        public void Refresh()
        {
            List<UnderlyingSystemAlarm> snapshots = new List<UnderlyingSystemAlarm>();

            lock (m_alarms)
            {
                for (int ii = 0; ii < m_alarms.Count; ii++)
                {
                    UnderlyingSystemAlarm alarm = m_alarms[ii];
                    snapshots.Add(alarm.CreateSnapshot());
                }
            }

            // report any alarm changes after releasing the lock.
            for (int ii = 0; ii < snapshots.Count; ii++)
            {
                ReportAlarmChange(snapshots[ii]);
            }
        }

        /// <summary>
        /// Sets the state of the source (surpresses any active alarms).
        /// </summary>
        /// <param name="offline">if set to <c>true</c> the source is offline.</param>
        public void SetOfflineState(bool offline)
        {
            m_isOffline = offline;
            List<UnderlyingSystemAlarm> snapshots = new List<UnderlyingSystemAlarm>();

            lock (m_alarms)
            {
                for (int ii = 0; ii < m_alarms.Count; ii++)
                {
                    UnderlyingSystemAlarm alarm = m_alarms[ii];
                    
                    if (alarm.SetStateBits(UnderlyingSystemAlarmStates.Suppressed, offline))
                    {
                        alarm.Time = alarm.EnableTime = DateTime.UtcNow;
                        alarm.Reason = "The alarm was " + ((offline)?"suppressed.":"unsuppressed.");
                        
                        // check if the alarm change should be reported.
                        if ((alarm.State & UnderlyingSystemAlarmStates.Enabled) != 0)
                        {
                            snapshots.Add(alarm.CreateSnapshot());
                        }
                    }               
                }
            }
            
            // report any alarm changes after releasing the lock.
            for (int ii = 0; ii < snapshots.Count; ii++)
            {
                ReportAlarmChange(snapshots[ii]);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the source is offline.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is offline; otherwise, <c>false</c>.
        /// </value>
        /// <remarks>
        /// All alarms for offline sources are suppressed.
        /// </remarks>
        public bool IsOffline
        {
            get { return m_isOffline; }
        }

        /// <summary>
        /// Simulates a source by updating the state of the alarms belonging to the condition.
        /// </summary>
        /// <param name="counter">The number of simulation cycles that have elapsed.</param>
        /// <param name="index">The index of the source within the system.</param>
        public void DoSimulation(long counter, int index)
        {
            try
            {
                List<UnderlyingSystemAlarm> snapshots = new List<UnderlyingSystemAlarm>();

                // update the alarms.
                lock (m_alarms)
                {
                    for (int ii = 0; ii < m_alarms.Count; ii++)
                    {
                        UpdateAlarm(m_alarms[ii], counter, ii+index, snapshots);
                    }
                }

                // report any alarm changes after releasing the lock.
                for (int ii = 0; ii < snapshots.Count; ii++)
                {
                    ReportAlarmChange(snapshots[ii]);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error running simulation for source {0}", m_sourcePath);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Finds the alarm identified by the name.
        /// </summary>
        /// <param name="alarmName">Name of the alarm.</param>
        /// <param name="recordNumber">The record number associated with the alarm.</param>
        /// <returns>The alarm if null; otherwise null.</returns>
        private UnderlyingSystemAlarm FindAlarm(string alarmName, uint recordNumber)
        {
            lock (m_alarms)
            {
                // look up archived alarm.
                if (recordNumber != 0)
                {
                    UnderlyingSystemAlarm alarm = null;

                    if (!m_archive.TryGetValue(recordNumber, out alarm))
                    {
                        return null;
                    }

                    return alarm;
                }

                // look up alarm.
                for (int ii = 0; ii < m_alarms.Count; ii++)
                {
                    UnderlyingSystemAlarm alarm = m_alarms[ii];

                    if (alarm.Name == alarmName)
                    {
                        return alarm;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Reports a change to an alarm record.
        /// </summary>
        /// <param name="alarm">The alarm.</param>
        private void ReportAlarmChange(UnderlyingSystemAlarm alarm)
        {
            if (OnAlarmChanged != null)
            {
                try
                {
                    OnAlarmChanged(alarm);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error reporting change to an Alarm for Source {0}.", m_sourcePath);
                }
            }
        }

        /// <summary>
        /// Updates the state of an alarm.
        /// </summary>
        private void UpdateAlarm(UnderlyingSystemAlarm alarm, long counter, int index, List<UnderlyingSystemAlarm> snapshots)
        {
            string reason = null;
            
            // ignore disabled alarms.
            if ((alarm.State & UnderlyingSystemAlarmStates.Enabled) == 0)
            {
                return;
            }

            // check if the alarm needs to be updated this cycle.
            if (counter % (8 + (index%4)) == 0)
            {
                // check if it is time to activate.
                if ((alarm.State & UnderlyingSystemAlarmStates.Active) == 0)
                {
                    reason = "The alarm is active.";

                    alarm.SetStateBits(UnderlyingSystemAlarmStates.Active, true);
                    alarm.SetStateBits(UnderlyingSystemAlarmStates.Acknowledged | UnderlyingSystemAlarmStates.Confirmed, false);
                    alarm.Severity = EventSeverity.Low;
                    alarm.ActiveTime = DateTime.UtcNow;

                    switch (alarm.AlarmType)
                    {
                        case "HighAlarm":
                        {
                            alarm.SetStateBits(UnderlyingSystemAlarmStates.Limits, false);
                            alarm.SetStateBits(UnderlyingSystemAlarmStates.High, true);
                            break;
                        }

                        case "HighLowAlarm":
                        {
                            alarm.SetStateBits(UnderlyingSystemAlarmStates.Limits, false);
                            alarm.SetStateBits(UnderlyingSystemAlarmStates.Low, true);
                            break;
                        }
                    }
                }

                // bump the severity.
                else if ((alarm.State & UnderlyingSystemAlarmStates.Acknowledged) == 0)
                {
                    if (alarm.Severity < EventSeverity.High)
                    {
                        reason = "The alarm severity has increased.";

                        Array values = Enum.GetValues(typeof(EventSeverity));

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            EventSeverity severity = (EventSeverity)values.GetValue(ii);

                            if (severity > alarm.Severity)
                            {
                                alarm.Severity = severity;
                                break;
                            }
                        }

                        if (alarm.Severity > EventSeverity.Medium)
                        {
                            switch (alarm.AlarmType)
                            {
                                case "HighLowAlarm":
                                {
                                    alarm.SetStateBits(UnderlyingSystemAlarmStates.Limits, false);
                                    alarm.SetStateBits(UnderlyingSystemAlarmStates.LowLow, true);
                                    break;
                                }
                            }
                        }
                    }

                    // give up on the alarm.
                    else
                    {
                        // create an archived state that needs to be acknowledged.
                        if (alarm.AlarmType == "TripAlarm")
                        {
                            // check the number of archived states.
                            int count = 0;

                            foreach (UnderlyingSystemAlarm record in m_archive.Values)
                            {
                                if (record.Name == alarm.Name)
                                {
                                    count++;
                                }
                            }
                            // limit the number of archived states to avoid filling up the display.
                            if (count < 2)
                            {
                                // archive the current state.
                                UnderlyingSystemAlarm snapshot = alarm.CreateSnapshot();
                                snapshot.RecordNumber = ++m_nextRecordNumber;
                                snapshot.Severity = EventSeverity.Low;
                                m_archive.Add(snapshot.RecordNumber, snapshot);
                                snapshots.Add(snapshot);
                            }
                        }

                        reason = "The alarm was deactivated by the system.";
                        alarm.SetStateBits(UnderlyingSystemAlarmStates.Active, false);
                        //alarm.SetStateBits(UnderlyingSystemAlarmStates.Acknowledged | UnderlyingSystemAlarmStates.Confirmed, true);
                        alarm.Severity = EventSeverity.Low;
                    }
                }
            }

            // update the reason.
            if (reason != null)
            {
                alarm.Time = DateTime.UtcNow;
                alarm.Reason = reason;

                // return a snapshot used to report the state change.
                snapshots.Add(alarm.CreateSnapshot());
            }

            // no change so nothing to report.
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private string m_name;
        private string m_sourcePath;
        private string m_sourceType;
        private List<UnderlyingSystemAlarm> m_alarms;
        private Dictionary<uint,UnderlyingSystemAlarm> m_archive;
        private bool m_isOffline;
        private uint m_nextRecordNumber;
        #endregion
    }

    /// <summary>
    /// Used to receive events when the state of an alarm changes.
    /// </summary>
    public delegate void AlarmChangedEventHandler(UnderlyingSystemAlarm alarm);
}
