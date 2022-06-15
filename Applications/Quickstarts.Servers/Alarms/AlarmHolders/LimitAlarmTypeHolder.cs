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

namespace Alarms
{
    class LimitAlarmTypeHolder : AlarmConditionTypeHolder
    {
        private bool m_isLimit = true;

        public LimitAlarmTypeHolder(
            AlarmNodeManager alarmNodeManager,
            FolderState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            bool optional = true,
            double maxShelveTime = AlarmDefines.NORMAL_MAX_TIME_SHELVED,
            bool create = true) :
            base(alarmNodeManager, parent, trigger, name, alarmConditionType, controllerType, interval, optional, maxShelveTime, false)
        {
            if (create)
            {
                Initialize(Opc.Ua.ObjectTypes.LimitAlarmType, name, maxShelveTime);
            }
        }

        public void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED,
            bool isLimit = true)
        {
            // Create an alarm and trigger name - Create a base method for creating the trigger, just provide the name

            if (m_alarm == null)
            {
                m_alarm = new LimitAlarmState(m_parent);
            }

            m_isLimit = isLimit;

            LimitAlarmState alarm = GetAlarm();

            if (alarm.HighLimit == null)
            {
                alarm.HighLimit = new PropertyState<double>(alarm);
            }
            if (alarm.HighHighLimit == null)
            {
                alarm.HighHighLimit = new PropertyState<double>(alarm);
            }
            if (alarm.LowLimit == null)
            {
                alarm.LowLimit = new PropertyState<double>(alarm);
            }
            if (alarm.LowLowLimit == null)
            {
                alarm.LowLowLimit = new PropertyState<double>(alarm);
            }

            if (Optional)
            {
                alarm.BaseHighLimit = new PropertyState<double>(alarm);
                alarm.BaseHighHighLimit = new PropertyState<double>(alarm);
                alarm.BaseLowLimit = new PropertyState<double>(alarm);
                alarm.BaseLowLowLimit = new PropertyState<double>(alarm);
            }

            // Call the base class to set parameters
            base.Initialize(alarmTypeIdentifier, name, maxTimeShelved);

            alarm.HighLimit.Value = AlarmDefines.HIGH_ALARM;
            alarm.HighHighLimit.Value = AlarmDefines.HIGHHIGH_ALARM;
            alarm.LowLimit.Value = AlarmDefines.LOW_ALARM;
            alarm.LowLowLimit.Value = AlarmDefines.LOWLOW_ALARM;

            if (Optional)
            {
                alarm.BaseHighLimit.Value = AlarmDefines.HIGH_ALARM;
                alarm.BaseHighHighLimit.Value = AlarmDefines.HIGHHIGH_ALARM;
                alarm.BaseLowLimit.Value = AlarmDefines.LOW_ALARM;
                alarm.BaseLowLowLimit.Value = AlarmDefines.LOWLOW_ALARM;
            }
            else
            {
                alarm.BaseHighHighLimit = null;
                alarm.BaseLowLimit = null;
                alarm.BaseLowLowLimit = null;
            }
        }

        #region Helpers

        private LimitAlarmState GetAlarm()
        {
            return (LimitAlarmState)m_alarm;
        }

        #endregion


    }
}
