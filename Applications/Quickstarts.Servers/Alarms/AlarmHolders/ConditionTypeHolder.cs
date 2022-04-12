/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua;

#pragma warning disable CS1591

namespace Alarms
{
    public class ConditionTypeHolder : BaseEventTypeHolder
    {

        protected ConditionTypeHolder(
            AlarmNodeManager alarmNodeManager,
            FolderState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            bool optional) :
            base(alarmNodeManager, parent, trigger, name, alarmConditionType, controllerType, interval, optional)
        {
            m_alarmConditionType = alarmConditionType;
        }

        protected new void Initialize(
            uint alarmTypeIdentifier,
            string name)
        {
            if (m_alarm == null)
            {
                // this is invalid
                m_alarm = new ConditionState(m_parent);
            }

            ConditionState alarm = GetAlarm();

            // Call the base class to set parameters
            base.Initialize(alarmTypeIdentifier, name);

            // Set all ConditionType Parameters
            alarm.ClientUserId.Value = "Anonymous";
            alarm.ConditionClassId.Value = m_alarmConditionType.Node;
            alarm.ConditionClassName.Value = new LocalizedText("", m_alarmConditionType.ConditionName);
            alarm.ConditionName.Value = m_alarmRootName;
            Utils.LogInfo(Utils.TraceMasks.Information, "Alarm ConditionName = " + alarm.ConditionName.Value);

            alarm.BranchId.Value = new NodeId();
            alarm.Retain.Value = false;

            alarm.SetEnableState(SystemContext, true);
            alarm.Quality.Value = Opc.Ua.StatusCodes.Good;
            alarm.LastSeverity.Value = AlarmDefines.INACTIVE_SEVERITY;
            alarm.Severity.Value = AlarmDefines.INACTIVE_SEVERITY;
            alarm.Comment.Value = new LocalizedText("en", "");

            // Set Method Handlers
            alarm.OnEnableDisable = OnEnableDisableAlarm;
            alarm.OnAddComment = OnAddComment;

            alarm.ConditionSubClassId = null;
            alarm.ConditionSubClassName = null;
        }


        public BaseEventState FindBranch()
        {
            BaseEventState state = null;

            return state;
        }

        protected override void CreateBranch()
        {
            if (SupportsBranching)
            {
                ConditionState alarm = GetAlarm();

                int currentSeverity = alarm.Severity.Value;
                int newSeverity = GetSeverity();
                // A branch is created at the end of an active cycle
                // This could be a transition between alarm states,
                // or a transition to inactive
                // So a branch can only be created when the severity changes
                if (currentSeverity > AlarmDefines.INACTIVE_SEVERITY &&
                    newSeverity != currentSeverity)
                {
                    NodeId branchId = GetNewBranchId();
                    ConditionState branch = alarm.CreateBranch(SystemContext, branchId);

                    string postEventId = Utils.ToHexString(branch.EventId.Value as byte[]);

                    Log("CreateBranch", " Branch " + branchId.ToString() +
                        " EventId " + postEventId + " created, Message " + alarm.Message.Value.Text);

                    m_alarmController.SetBranchCount(alarm.GetBranchCount());
                }
            }
        }

        #region Overrides

        public override void SetValue(string message = "")
        {
            ConditionState alarm = GetAlarm();

            if (ShouldEvent() || message.Length > 0)
            {
                CreateBranch();

                int newSeverity = GetSeverity();

                alarm.SetSeverity(SystemContext, (EventSeverity)newSeverity);
                if (message.Length == 0)
                {
                    message = "Alarm Event Value = " + m_trigger.Value.ToString();
                }

                alarm.Message.Value = new LocalizedText("en", message);

                ReportEvent();
            }
        }

        #endregion

        #region Child Helpers

        public void ReportEvent(ConditionState alarm = null)
        {
            if (alarm == null)
            {
                alarm = GetAlarm();
            }

            if (alarm.EnabledState.Id.Value)
            {
                alarm.EventId.Value = Guid.NewGuid().ToByteArray();
                alarm.Time.Value = DateTime.UtcNow;
                alarm.ReceiveTime.Value = alarm.Time.Value;

                Log("ReportEvent", " Value " + m_alarmController.GetValue().ToString() +
                    " Message " + alarm.Message.Value.Text);

                alarm.ClearChangeMasks(SystemContext, true);

                InstanceStateSnapshot eventSnapshot = new InstanceStateSnapshot();
                eventSnapshot.Initialize(SystemContext, alarm);
                alarm.ReportEvent(SystemContext, eventSnapshot);
            }
        }

        protected virtual ushort GetSeverity()
        {
            ushort severity = AlarmDefines.INACTIVE_SEVERITY;

            int level = m_alarmController.GetValue();

            if (Analog)
            {
                if (level <= AlarmDefines.LOWLOW_ALARM && Analog)
                {
                    severity = AlarmDefines.LOWLOW_SEVERITY;
                }
                // Level is Low
                else if (level <= AlarmDefines.LOW_ALARM)
                {
                    severity = AlarmDefines.LOW_SEVERITY;
                }
                // Level is HighHigh
                else if (level >= AlarmDefines.HIGHHIGH_ALARM && Analog)
                {
                    severity = AlarmDefines.HIGHHIGH_SEVERITY;
                }
                // Level is High
                else if (level >= AlarmDefines.HIGH_ALARM)
                {
                    severity = AlarmDefines.HIGH_SEVERITY;
                }
            }
            else
            {
                if (level <= AlarmDefines.BOOL_LOW_ALARM)
                {
                    severity = AlarmDefines.LOW_SEVERITY;
                }
                // Level is High
                else if (level >= AlarmDefines.BOOL_HIGH_ALARM)
                {
                    severity = AlarmDefines.HIGH_SEVERITY;
                }

            }

            return severity;
        }

        protected bool IsActive()
        {
            bool isActive = false;
            if (GetSeverity() > AlarmDefines.INACTIVE_SEVERITY)
            {
                isActive = true;
            }
            return isActive;
        }

        protected bool WasActive()
        {
            bool wasActive = false;
            ConditionState alarm = GetAlarm();
            if (alarm.Severity.Value > AlarmDefines.INACTIVE_SEVERITY)
            {
                wasActive = true;
            }
            return wasActive;
        }

        protected bool ShouldEvent()
        {
            bool shouldEvent = false;
            ConditionState alarm = GetAlarm();
            ushort newSeverity = GetSeverity();
            if (newSeverity != alarm.Severity.Value)
            {
                shouldEvent = true;
            }

            return shouldEvent;
        }

        #endregion

        #region Helpers

        private ConditionState GetAlarm(BaseEventState alarm = null)
        {
            if (alarm == null)
            {
                alarm = m_alarm;
            }
            return (ConditionState)alarm;
        }



        protected bool IsEvent(string caller, byte[] eventId)
        {
            bool isEvent = IsEvent(eventId);

            if (!isEvent)
            {
                LogError(caller, EventErrorMessage(eventId));
            }

            return isEvent;
        }

        protected string EventErrorMessage(byte[] eventId)
        {
            return " Requested Event " + Utils.ToHexString(eventId);
        }


        #endregion

        #region Method Handlers 
        public ServiceResult OnEnableDisableAlarm(
            ISystemContext context,
            ConditionState condition,
            bool enabling)
        {
            StatusCode status = StatusCodes.Good;

            ConditionState alarm = GetAlarm();

            if (enabling != alarm.EnabledState.Id.Value)
            {
                alarm.SetEnableState(SystemContext, enabling);
                alarm.Message.Value = enabling ? "Enabling" : "Disabling" + " alarm " + MapName;

                // if disabled, it will not fire
                ReportEvent();
            }
            else
            {
                if (enabling)
                {
                    status = StatusCodes.BadConditionAlreadyEnabled;
                }
                else
                {
                    status = StatusCodes.BadConditionAlreadyDisabled;
                }
            }

            return status;
        }

        private ServiceResult OnAddComment(
            ISystemContext context,
            ConditionState condition,
            byte[] eventId,
            LocalizedText comment)
        {
            ConditionState alarm = GetAlarm();

            ConditionState alarmOrBranch = alarm.GetEventByEventId(eventId);
            if (alarmOrBranch == null)
            {
                string errorMessage = "Unknown event id " + Utils.ToHexString(eventId);
                alarm.Message.Value = "OnAddComment " + errorMessage;
                LogError("OnAddComment", errorMessage);
                return StatusCodes.BadEventIdUnknown;
            }

            m_alarmController.OnAddComment();

            // Don't call ReportEvent,  Core will send the event.

            m_delayedMessages.Add("OnAddComment");

            return ServiceResult.Good;
        }

        protected bool CanSetComment(LocalizedText comment)
        {
            bool canSetComment = false;

            if (comment != null)
            {
                canSetComment = true;

                bool emptyComment = comment.Text == null || comment.Text.Length == 0;
                bool emptyLocale = comment.Locale == null || comment.Locale.Length == 0;

                if (emptyComment && emptyLocale)
                {
                    canSetComment = false;
                }
            }

            return canSetComment;
        }

        protected virtual bool GetRetainState()
        {
            return true;
        }

        #endregion
    }



}
