using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Opc.Ua;

#pragma warning disable CS1591

namespace Alarms
{
    public class AlarmConditionTypeHolder : AcknowledgeableConditionTypeHolder
    {
        public AlarmConditionTypeHolder(
            Alarms alarms,
            FolderState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            bool optional = true,
            double maxShelveTime = AlarmDefines.NORMAL_MAX_TIME_SHELVED,
            bool create = true) :
            base(alarms, parent, trigger, name, alarmConditionType, controllerType, interval, optional, false)
        {
            if (create)
            {
                Initialize(Opc.Ua.ObjectTypes.AlarmConditionType, name, maxShelveTime);
            }
        }

        public void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
        {
            // Create an alarm and trigger name - Create a base method for creating the trigger, just provide the name

            if (m_alarm == null)
            {
                m_alarm = new AlarmConditionState(m_parent);
            }

            AlarmConditionState alarm = GetAlarm();

            if (Optional)
            {
                if (alarm.SuppressedState == null)
                {
                    alarm.SuppressedState = new TwoStateVariableState(alarm);
                }


                if (alarm.OutOfServiceState == null)
                {
                    alarm.OutOfServiceState = new TwoStateVariableState(alarm);
                }

                if (alarm.ShelvingState == null)
                {
                    alarm.ShelvingState = new ShelvedStateMachineState(alarm);
                    alarm.ShelvingState.Create(SystemContext,
                        null,
                        BrowseNames.ShelvingState,
                        BrowseNames.ShelvingState,
                        false);
                }
                if (alarm.MaxTimeShelved == null)
                {
                    // Off normal does not create MaxTimeShelved.
                    alarm.MaxTimeShelved = new PropertyState<double>(alarm);
                }

            }


            // Call the base class to set parameters
            base.Initialize(alarmTypeIdentifier, name);

            alarm.SetActiveState(SystemContext, active: false);
            alarm.InputNode.Value = new NodeId(m_trigger.NodeId);

            if (Optional)
            {
                alarm.SetSuppressedState(SystemContext, suppressed: false);
                alarm.SetShelvingState(SystemContext, shelved: false, oneShot: false, shelvingTime: Double.MaxValue);
                alarm.ShelvingState.LastTransition.Value = new LocalizedText("");
                alarm.ShelvingState.LastTransition.Id.Value = 0;
                
                alarm.OnShelve = OnShelve;
                alarm.OnTimedUnshelve = OnTimedUnshelve;
                alarm.UnshelveTimeUpdateRate = 2000;

                alarm.MaxTimeShelved.Value = maxTimeShelved;

                alarm.LatchedState.Value = new LocalizedText("");
                alarm.LatchedState.Id.Value = false;


            }
            else
            {
                alarm.ShelvingState = null;
                alarm.LatchedState = null;
            }

            alarm.AudibleSound = null;
            alarm.AudibleEnabled = null;
        }

        public override BaseEventState CreateBranch(BaseEventState branch, NodeId branchId)
        {
            if (branch == null)
            {

                // this is invalid
                branch = new AlarmConditionState(m_parent);
            }

            AlarmConditionState branchEvent = GetAlarm(branch);
            InitializeInternal(branchEvent);

            base.CreateBranch(branch, branchId);

            branchEvent.SetActiveState(SystemContext, active: true);

            AlarmConditionState alarm = GetAlarm();
            alarm.InputNode.Value = new NodeId(m_trigger.NodeId);

            if (Optional)
            {
                branchEvent.SetSuppressedState(SystemContext, alarm.SuppressedState.Id.Value);
                // This needs more work.
                branchEvent.SetShelvingState(SystemContext, shelved: false, oneShot: false, shelvingTime: Double.MaxValue);

                branchEvent.OnShelve = OnShelve;
                branchEvent.OnTimedUnshelve = OnTimedUnshelve;
                branchEvent.UnshelveTimeUpdateRate = 2000;

                branchEvent.MaxTimeShelved.Value = alarm.MaxTimeShelved.Value;
            }
            return branchEvent;
        }

        public void InitializeInternal( AlarmConditionState alarm )
        {
            // Create an alarm and trigger name - Create a base method for creating the trigger, just provide the name

            if (Optional)
            {
                if (alarm.SuppressedState == null)
                {
                    alarm.SuppressedState = new TwoStateVariableState(alarm);
                }


                if (alarm.OutOfServiceState == null)
                {
                    alarm.OutOfServiceState = new TwoStateVariableState(alarm);
                }

                if (alarm.ShelvingState == null)
                {
                    alarm.ShelvingState = new ShelvedStateMachineState(alarm);
                    alarm.ShelvingState.Create(SystemContext,
                        null,
                        BrowseNames.ShelvingState,
                        BrowseNames.ShelvingState,
                        false);
                }
                if (alarm.MaxTimeShelved == null)
                {
                    // Off normal does not create MaxTimeShelved.
                    alarm.MaxTimeShelved = new PropertyState<double>(alarm);
                }
            }
        }


        #region Overrides

        public override void SetValue(string message = "")
        {
            bool setValue = false;
            AlarmConditionState alarm = GetAlarm();


            if (ShouldEvent())
            {
                alarm.SetActiveState(SystemContext, IsActive());

                setValue = true;
            }

            if ( UpdateSuppression() )
            {
                if ( message.Length <= 0 )
                {
                    message = "Updating due to Shelving State Update: " + alarm.ShelvingState.CurrentState.Value.ToString(); 
                }
                setValue = true;
            }
            else if ( UpdateSuppression() )
            {
                if (message.Length <= 0)
                {
                    message = "Updating due to Suppression Update: " + alarm.SuppressedState.Value.ToString();
                }
                setValue = true;
            }

            if (setValue)
            {
                base.SetValue(message);
            }
        }

        protected override bool GetRetainState()
        {
            AlarmConditionState alarm = GetAlarm();

            bool retainState = true;

            if (!alarm.ActiveState.Id.Value)
            {
                if (alarm.AckedState.Id.Value)
                {
                    if ((Optional))
                    {
                        if (alarm.ConfirmedState.Id.Value)
                        {
                            retainState = false;
                        }
                    }
                    else
                    {
                        retainState = false;
                    }
                }
            }

            return retainState;
        }

        protected override void SetActive(BaseEventState baseEvent, bool activeState)
        {
            AlarmConditionState alarm = GetAlarm(baseEvent);
            alarm.SetActiveState(SystemContext, activeState);
        }

        protected override bool UpdateShelving()
        {
            // Don't have to worry about changing state to Unshelved, there is an SDK timer to deal with that.
            bool update = false;

            //if (Optional)
            //{
            //    AlarmConditionState alarm = GetAlarm();
            //    if (alarm.ShelvingState.UnshelveTime.Value > 0)
            //    {
            //        bool waiting = true;
            //    }

            //    object unshelveTimeObject = new object();
            //    alarm.OnReadUnshelveTime(SystemContext, null, ref unshelveTimeObject);
            //    double unshelveTime = (double)unshelveTimeObject;

            //    if (alarm.ShelvingState.UnshelveTime.Value > 0)
            //    {
            //        bool waiting = true;
            //    }

            //    if (unshelveTime != alarm.ShelvingState.UnshelveTime.Value)
            //    {
            //        alarm.ShelvingState.UnshelveTime.Value = unshelveTime;
            //        update = true;
            //    }
            //}

            return update;
        }

        protected override bool UpdateSuppression()
        {
            bool update = false;
            if ( Optional )
            {
                AlarmConditionState alarm = GetAlarm();

                if ( m_alarmController.ShouldSuppress() )
                {
                    alarm.SetSuppressedState(SystemContext, true);
                    update = true;
                }

                if (m_alarmController.ShouldUnsuppress())
                {
                    alarm.SetSuppressedState(SystemContext, false);
                    update = true;
                }
            }
            return update;
        }

        #endregion

        #region Helpers

        private AlarmConditionState GetAlarm(BaseEventState alarm = null)
        {
            if (alarm == null)
            {
                alarm = m_alarm;
            }
            return (AlarmConditionState)alarm;
        }


        #endregion

        #region Methods

        private ServiceResult OnShelve(
            ISystemContext context,
            AlarmConditionState alarm,
            bool shelving,
            bool oneShot,
            double shelvingTime)
        {
            string shelved = "Shelved";
            string dueTo = "";

            if ( shelving )
            {
                if ( oneShot )
                {
                    dueTo = " due to OneShotShelve";
                }
                else
                {
                    dueTo = " due to TimedShelve of " + shelvingTime.ToString();
                }
            }
            else
            {
                shelved = "Unshelved";
            }

            alarm.Message.Value = "The alarm is " + shelved + dueTo;
            alarm.SetShelvingState(context, shelving, oneShot, shelvingTime);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the alarm is shelved.
        /// </summary>
        private ServiceResult OnTimedUnshelve(
            ISystemContext context,
            AlarmConditionState alarm)
        {
            // update the alarm state and produce and event.
            alarm.Message.Value = "The timed shelving period expired.";
            alarm.SetShelvingState(context, false, false, 0);

            base.SetValue(alarm.Message.Value.Text );

            return ServiceResult.Good;
        }

        #endregion
    }
}
