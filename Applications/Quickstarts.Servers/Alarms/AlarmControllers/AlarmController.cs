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

#pragma warning disable CS0219
#pragma warning disable CS1591

namespace Alarms
{
    public class AlarmController
    {
        #region Variables

        protected BaseDataVariableState m_variable = null;
        protected int m_value = 0;
        protected bool m_increment = true;
        protected DateTime m_nextTime = DateTime.Now;
        protected DateTime m_stopTime = DateTime.Now;
        protected int m_interval = 0;
        protected bool m_isBoolean = false;
        protected bool m_allowChanges = false;
        protected bool m_reset = false;
        protected DateTime m_lastMaxValue = new DateTime();
        protected bool m_validLastMaxValue = false;
        private int m_branchCount = 0;
        private bool m_supportsBranching = false;
        protected int m_midpoint = AlarmDefines.NORMAL_START_VALUE;


        #endregion


        public AlarmController(BaseDataVariableState variable, int interval, bool isBoolean)
        {
            m_variable = variable;
            m_interval = interval;
            m_isBoolean = isBoolean;
            m_increment = true;

            m_value = m_midpoint;
            m_stopTime = m_stopTime.AddYears(5);

            m_allowChanges = false;
        }

        public virtual void Start()
        {
            Stop();

            m_validLastMaxValue = false;

            m_nextTime = DateTime.Now;
            m_stopTime = DateTime.Now;
            m_stopTime = m_stopTime.AddMinutes(3);


            m_allowChanges = true;
        }

        public virtual void Stop()
        {
            m_value = m_midpoint;
            m_increment = true;
            m_allowChanges = false;

            m_reset = true;
        }

        public virtual bool Update(ISystemContext systemContext)
        {
            bool valueSet = false;
            if (CanSetValue())
            {
                int value = 0;
                bool boolValue = false;
                GetValue(ref value, ref boolValue);

                Utils.LogInfo(Utils.TraceMasks.Information, "AlarmController Update Value = " + value.ToString());

                if (m_isBoolean)
                {
                    m_variable.Value = boolValue;
                }
                else
                {
                    m_variable.Value = value;
                }
                m_variable.Timestamp = DateTime.UtcNow;
                m_variable.ClearChangeMasks(systemContext, false);

                valueSet = true;
            }

            return valueSet;
        }

        protected virtual void SetNextInterval()
        {
            m_nextTime = DateTime.Now;

            m_nextTime = m_nextTime.AddMilliseconds(m_interval);
        }

        public void ManualWrite( object value )
        {
            if ( value.GetType().Name == "Int32" )
            {
                // Don't let anyone write a value out of range
                Int32 potentialWrite = (Int32)value;
                if ( potentialWrite >= AlarmDefines.MIN_VALUE && potentialWrite <= AlarmDefines.MAX_VALUE )
                {
                    m_value = potentialWrite;
                }
                else
                {
                    Utils.LogInfo(Utils.TraceMasks.Error, "AlarmController Received out of range manual write of " + value.ToString());
                }
            }
            else
            {
                if ((bool)value )
                {
                    m_value = 70;
                    m_increment = true;
                }
                else
                {
                    m_value = m_midpoint;
                }
            }
        }

        public virtual bool CanSetValue()
        {
            bool setValue = false;

            if ( DateTime.Now > m_stopTime )
            {
                Stop();
                m_stopTime = DateTime.Now;
                m_stopTime = m_stopTime.AddYears(5);
            }
            else if (m_allowChanges || m_reset)
            {
                m_reset = false;

                if (DateTime.Now > m_nextTime)
                {
                    SetNextInterval();
                    setValue = true;
                }
            }

            return setValue;
        }

        protected virtual void GetValue(ref int intValue, ref bool boolValue)
        {
            int maxValue = 100;
            int minValue = 0;

            TypicalGetValue(minValue, maxValue, ref intValue, ref boolValue);
        }

        public bool SupportsBranching
        {
            get { return m_supportsBranching; }
            set { m_supportsBranching = value; }
        }

        public virtual void SetBranchCount( int count )
        {
            m_branchCount = count;
        }

        protected void TypicalGetValue(int minValue, int maxValue, ref int intValue, ref bool boolValue)
        {
            int incrementValue = 5;
            if (m_isBoolean)
            {
                incrementValue = 10;
            }
            if (m_increment)
            {
                m_value += incrementValue;
                if (m_value >= maxValue)
                {
                    if ( m_validLastMaxValue )
                    {
                        Utils.LogInfo(Utils.TraceMasks.Information,
                            "Cycle Time " + (DateTime.Now - m_lastMaxValue).ToString() + " Interval " + m_interval.ToString());
                    }
                    m_lastMaxValue = DateTime.Now;
                    m_validLastMaxValue = true;

                    m_increment = false;
                }
            }
            else
            {
                m_value -= incrementValue;
                if (m_value <= minValue)
                {
                    m_increment = true;
                }
            }

            intValue = m_value;
            boolValue = IsBooleanActive();
        }

        public bool IsBooleanActive()
        {
            bool isActive = false;
            if (m_value >= AlarmDefines.BOOL_HIGH_ALARM || m_value <= AlarmDefines.BOOL_LOW_ALARM)
            {
                isActive = true;
            }

            return isActive;
        }

        public int GetValue()
        {
            return m_value;
        }

        public int GetSawtooth()
        {
            return m_value;
        }

        public int GetSine(int minValue, int maxValue )
        {
            return CalcSine(minValue, maxValue, m_value);
        }

        public int CalcSine( int minValue, int maxValue, int value )
        {
            // What I want is a sawtooth compared against a sine value.
            // This calculates a simular sine value that will have predictable differences between value and sine

            /*
             * https://www.mathsisfun.com/algebra/amplitude-period-frequency-phase-shift.html
             * Sine with Phase Shift and Vertical Shift!  This is what I want
             * y = A sin(B(x + C)) + D
             * A - Amplitude
             * B - relates to period - This should extend the time period
             * C - Phase Shift
             * D - Vertical Shift
             * 
             */

            double twoPi = Math.PI * 2;

            double normalSpan = maxValue - minValue;
            double amplitude = normalSpan / 2;
            double median = maxValue - amplitude;

            double offsetValue = value - minValue;
            double percentageOfRange = offsetValue / normalSpan;

            double reducedPeriod = percentageOfRange / 2;

            double period = twoPi; // this would relate to the interval.  Ignore for now.
            double phase = -0.25; // phaseShift;
            double verticalShift = median; // amplitude

            double calculated = amplitude * (Math.Sin(period * (reducedPeriod + phase))) + verticalShift;

            Utils.LogInfo(Utils.TraceMasks.Information,
                " Phase " + String.Format("{0:0.00}", phase) +
                " Value " + value.ToString() +
                " Sine " + String.Format("{0:0.00}", calculated) +
                " Offset Value " + String.Format("{0:0.00}", offsetValue) +
                " Span " + String.Format("{0:0.00}", normalSpan) +
                " Percentage of Range " + String.Format("{0:0.00}", percentageOfRange));

            return (int)calculated;
        }

        public virtual bool ShouldSuppress()
        {
            return false;
        }

        public virtual bool ShouldUnsuppress()
        {
            return false;
        }

        public virtual void OnAddComment()
        {

        }

        public virtual void OnAcknowledge()
        {

        }

        public virtual void OnConfirm()
        {

        }
    }
}
