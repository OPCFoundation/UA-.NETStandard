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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;

#pragma warning disable CS1591

namespace Alarms
{
    public class BaseEventTypeHolder : AlarmHolder
    {
        protected BaseEventTypeHolder(
            AlarmNodeManager alarmNodeManager,
            FolderState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            bool optional) :
            base(alarmNodeManager, parent, trigger, controllerType, interval)
        {
            m_optional = optional;
        }

        protected new void Initialize(
            uint alarmTypeIdentifier,
            string name)
        {
            m_alarmTypeIdentifier = alarmTypeIdentifier;

            if (m_alarm != null)
            {
                // Call the base class to set parameters
                base.Initialize(alarmTypeIdentifier, name);

                BaseEventState alarm = GetAlarm();

                alarm.EventId.Value = Guid.NewGuid().ToByteArray();
                alarm.EventType.Value = new NodeId( alarmTypeIdentifier, GetNameSpaceIndex( alarmTypeIdentifier ) );
                alarm.SourceNode.Value = m_trigger.NodeId;
                alarm.SourceName.Value = m_trigger.SymbolicName;
                alarm.Time.Value = DateTime.UtcNow;
                alarm.ReceiveTime.Value = alarm.Time.Value;
                alarm.Message.Value = name + " Initialized";
                alarm.Severity.Value = AlarmDefines.INACTIVE_SEVERITY;

                // TODO Implement for Optionals - Needs to go to all places where Time is set.
                alarm.LocalTime = null;
            }
        }

        #region Overrides

        public override void SetValue(string message = "")
        {
            
        }

        #endregion

        #region Helpers

        private BaseEventState GetAlarm(BaseEventState alarm = null)
        {
            if ( alarm == null )
            {
                alarm = m_alarm;
            }
            return (BaseEventState)alarm;
        }


        #endregion

        #region Child Helpers

        protected bool IsEvent( byte[] eventId )
        {
            bool isEvent = false;
            if (GetAlarm().EventId.Value.SequenceEqual(eventId) )
            {
                isEvent = true;
            }

            return isEvent;
        }


        #endregion

    }
}
