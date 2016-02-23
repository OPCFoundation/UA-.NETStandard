/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using Opc.Ua;

namespace Boiler
{
    public partial class BoilerState
    {
        #region Initialization
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            this.Simulation.OnAfterTransition = OnControlSimulation;
            m_random = new Random();
        }
        #endregion
        
        #region IDisposeable Methods
        /// <summary>
        /// Cleans up when the object is disposed.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (m_simulationTimer != null)
                {
                    m_simulationTimer.Dispose();
                    m_simulationTimer = null;
                }
            }
        }
        #endregion
                
        #region Private Methods
        /// <summary>
        /// Changes the state of the simulation.
        /// </summary>
        private ServiceResult OnControlSimulation(
            ISystemContext context,
            StateMachineState machine,
            uint transitionId,
            uint causeId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            switch (causeId)
            {
                case Opc.Ua.Methods.ProgramStateMachineType_Start:
                {
                    if (m_simulationTimer != null)
                    {
                        m_simulationTimer.Dispose();
                        m_simulationTimer = null;
                    }

                    uint updateRate = this.Simulation.UpdateRate.Value;

                    if (updateRate < 100)
                    {
                        updateRate = 100;
                        Simulation.UpdateRate.Value = updateRate;
                    }

                    m_simulationContext = context;
                    m_simulationTimer = new Timer(DoSimulation, null, (int)updateRate, (int)updateRate);
                    break;
                }

                case Opc.Ua.Methods.ProgramStateMachineType_Halt:
                case Opc.Ua.Methods.ProgramStateMachineType_Suspend:
                {
                    if (m_simulationTimer != null)
                    {
                        m_simulationTimer.Dispose();
                        m_simulationTimer = null;
                    }
                    
                    m_simulationContext = context;
                    break;
                }

                case Opc.Ua.Methods.ProgramStateMachineType_Reset:
                {
                    if (m_simulationTimer != null)
                    {
                        m_simulationTimer.Dispose();
                        m_simulationTimer = null;
                    }
                    
                    m_simulationContext = context;
                    break;
                }
            }
                
            return ServiceResult.Good;
        }
        
        /// <summary>
        /// Rounds a value to the significate digits specified and adds a random perturbation.
        /// </summary>
        private double RoundAndPerturb(double value, byte significantDigits)
        {
            double offsetToApply = 0;

            if (value != 0)
            {
                // need to move all significate digits above the decimal point.
                double offset = significantDigits - Math.Log10(Math.Abs(value));

                offsetToApply = Math.Floor(offset);

                if (offsetToApply == offset)
                {
                    offsetToApply -= 1;
                }
            }
            
            // round value to significant digits.
            double perturbedValue = Math.Round(value * Math.Pow(10.0, offsetToApply));
                        
            // apply the perturbation.
            perturbedValue += (m_random.NextDouble()-0.5)*5;

            // restore original exponent.
            perturbedValue = Math.Round(perturbedValue)*Math.Pow(10.0, -offsetToApply);

            // return value.
            return perturbedValue;
        }
                
        /// <summary>
        /// Moves the value towards the target.
        /// </summary>
        private double Adjust(double value, double target, double step, Range range)
        {
            // convert percentage step to an absolute step if range is specified.
            if (range != null)
            {
                step = step * range.Magnitude;
            }
            
            double difference = target - value;

            if (difference < 0)
            {
                value -= step;

                if (value < target)
                {
                   return target;
                }
            }
            else
            {
                value += step;

                if (value > target)
                {
                   return target;
                }
            }

            return value;
        }
        
        /// <summary>
        /// Returns the value as a percentage of the range.
        /// </summary>
        private double GetPercentage(AnalogItemState<double> value)
        {
            double percentage = value.Value;
            Range range = value.EURange.Value;

            if (range != null)
            {
                percentage /= Math.Abs(range.High - range.Low);

                if (Math.Abs(percentage) > 1.0)
                {
                    percentage = 1.0;
                }
            }

            return percentage;
        }
        
        /// <summary>
        /// Returns the value as a percentage of the range.
        /// </summary>
        private double GetValue(double value, Range range)
        {
            if (range != null)
            {
                return value * range.Magnitude;
            }

            return value;
        }

        /// <summary>
        /// Updates the values for the simulation. 
        /// </summary>
        private void DoSimulation(object state)
        {
            try
            {
                m_simulationCounter++;

                // adjust level.
                m_drum.LevelIndicator.Output.Value = Adjust(
                    m_drum.LevelIndicator.Output.Value, 
                    m_levelController.SetPoint.Value, 
                    0.1, 
                    m_drum.LevelIndicator.Output.EURange.Value);
                 
                // calculate inputs for custom controller. 
                m_customController.Input1.Value = m_levelController.UpdateMeasurement(m_drum.LevelIndicator.Output);
                m_customController.Input2.Value = GetPercentage(m_inputPipe.FlowTransmitter1.Output);
                m_customController.Input3.Value = GetPercentage(m_outputPipe.FlowTransmitter2.Output);
                                
                // calculate output for custom controller. 
                m_customController.ControlOut.Value = (m_customController.Input1.Value + m_customController.Input3.Value - m_customController.Input2.Value)/2;
                
                // update flow controller set point.
                m_flowController.SetPoint.Value = GetValue((m_customController.ControlOut.Value+1)/2, m_inputPipe.FlowTransmitter1.Output.EURange.Value);
                
                double error = m_flowController.UpdateMeasurement(m_inputPipe.FlowTransmitter1.Output);
                
                // adjust the input valve.
                m_inputPipe.Valve.Input.Value = Adjust(m_inputPipe.Valve.Input.Value, (error>0)?100:0, 10, null);
                
                // adjust the input flow.
                m_inputPipe.FlowTransmitter1.Output.Value = Adjust(
                    m_inputPipe.FlowTransmitter1.Output.Value, 
                    m_flowController.SetPoint.Value, 
                    0.6, 
                    m_inputPipe.FlowTransmitter1.Output.EURange.Value);
                     
                // add pertubations.
                m_drum.LevelIndicator.Output.Value         = RoundAndPerturb(m_drum.LevelIndicator.Output.Value, 3);
                m_inputPipe.FlowTransmitter1.Output.Value  = RoundAndPerturb(m_inputPipe.FlowTransmitter1.Output.Value, 3);
                m_outputPipe.FlowTransmitter2.Output.Value = RoundAndPerturb(m_outputPipe.FlowTransmitter2.Output.Value, 3);

                this.ClearChangeMasks(m_simulationContext, true);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error during boiler simulation.");
            }
        }
        #endregion

        #region Private Fields
        private ISystemContext m_simulationContext;
        private Timer m_simulationTimer;
        private Random m_random;
        private long m_simulationCounter;
        #endregion
    }
}
