/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Quickstarts.Servers;

namespace Boiler
{
#pragma warning disable CA1001 // Using timers that are disposed in OnAfterDelete
    public partial class BoilerState
    {
#pragma warning restore CA1001 // Using timers that are disposed in OnAfterDelete
        protected override void Initialize(ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<BoilerState>();
            base.Initialize(telemetry);
        }

        /// <inheritdoc/>
        protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
        {
            base.OnAfterCreate(context, node, ct);

            Simulation!.OnAfterTransition = OnControlSimulation;
        }

        /// <inheritdoc/>
        protected override void OnAfterDelete(ISystemContext context)
        {
            base.OnAfterDelete(context);

            m_simulationTimer?.Dispose();
            m_simulationTimer = null;
        }

        /// <summary>
        /// Changes the state of the simulation.
        /// </summary>
        private ServiceResult OnControlSimulation(
            ISystemContext context,
            StateMachineState machine,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant>? outputArguments)
        {
            switch (causeId)
            {
                case Opc.Ua.Methods.ProgramStateMachineType_Start:
                    m_simulationTimer?.Dispose();
                    m_simulationTimer = null;

                    uint updateRate = Simulation!.UpdateRate!.Value;

                    if (updateRate < 100)
                    {
                        updateRate = 100;
                        Simulation!.UpdateRate!.Value = updateRate;
                    }

                    m_simulationContext = context;
                    TimeProvider timeProvider = ((context as ServerSystemContext)?.Server as
                        ITimeProviderProvider)?.TimeProvider ??
                        TimeProvider.System;
                    m_simulationTimer = timeProvider.CreateTimer(
                        DoSimulation,
                        null,
                        TimeSpan.FromMilliseconds(updateRate),
                        TimeSpan.FromMilliseconds(updateRate));
                    break;
                case Opc.Ua.Methods.ProgramStateMachineType_Halt:
                case Opc.Ua.Methods.ProgramStateMachineType_Suspend:
                    m_simulationTimer?.Dispose();
                    m_simulationTimer = null;

                    m_simulationContext = context;
                    break;
                case Opc.Ua.Methods.ProgramStateMachineType_Reset:
                    m_simulationTimer?.Dispose();
                    m_simulationTimer = null;

                    m_simulationContext = context;
                    break;
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Rounds a value to the significant digits specified and adds a random perturbation.
        /// </summary>
        private static double RoundAndPerturb(double value, byte significantDigits)
        {
            double offsetToApply = 0;

            if (value != 0)
            {
                // need to move all significant digits above the decimal point.
                double offset = significantDigits - Math.Log10(Math.Abs(value));

                offsetToApply = Math.Floor(offset);

                if (offsetToApply == offset)
                {
                    offsetToApply--;
                }
            }

            // round value to significant digits.
            double perturbedValue = Math.Round(value * Math.Pow(10.0, offsetToApply));

            // apply the perturbation.
            perturbedValue += (UnsecureRandom.Shared.NextDouble() - 0.5) * 5;

            // restore original exponent.

            // return value.
            return Math.Round(perturbedValue) * Math.Pow(10.0, -offsetToApply);
        }

        /// <summary>
        /// Moves the value towards the target.
        /// </summary>
        private static double Adjust(double value, double target, double step, Opc.Ua.Range? range)
        {
            // convert percentage step to an absolute step if range is specified.
            if (range != null)
            {
                step *= range.Magnitude;
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
        private static double GetPercentage(AnalogItemState<double> value)
        {
            double percentage = value.Value;
            Opc.Ua.Range? range = value.EURange?.Value;

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
        private static double GetValue(double value, Opc.Ua.Range? range)
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
        private void DoSimulation(object? state)
        {
            try
            {
                AnalogItemState<double> drumLevelOutput = m_drum!.LevelIndicator!.Output!;
                Opc.Ua.Range? drumLevelEURange = drumLevelOutput.EURange?.Value;
                LevelControllerState levelController = m_levelController!;
                PropertyState<double> levelSetPoint = levelController.SetPoint!;
                CustomControllerState customController = m_customController!;
                PropertyState<double> customInput1 = customController.Input1!;
                PropertyState<double> customInput2 = customController.Input2!;
                PropertyState<double> customInput3 = customController.Input3!;
                PropertyState<double> customControlOut = customController.ControlOut!;
                FlowControllerState flowController = m_flowController!;
                PropertyState<double> flowSetPoint = flowController.SetPoint!;
                BoilerInputPipeState inputPipe = m_inputPipe!;
                AnalogItemState<double> inputFlow = inputPipe.FlowTransmitter1!.Output!;
                Opc.Ua.Range? inputFlowEURange = inputFlow.EURange?.Value;
                AnalogItemState<double> inputValve = inputPipe.Valve!.Input!;
                AnalogItemState<double> outputFlow = m_outputPipe!.FlowTransmitter2!.Output!;

                // adjust level.
                drumLevelOutput.Value = Adjust(
                    drumLevelOutput.Value,
                    levelSetPoint.Value,
                    0.1,
                    drumLevelEURange);

                // calculate inputs for custom controller.
                customInput1.Value = levelController.UpdateMeasurement(drumLevelOutput);
                customInput2.Value = GetPercentage(inputFlow);
                customInput3.Value = GetPercentage(outputFlow);

                // calculate output for custom controller.
                customControlOut.Value =
                    (
                        customInput1.Value +
                        customInput3.Value -
                        customInput2.Value
                    ) /
                    2;

                // update flow controller set point.
                flowSetPoint.Value = GetValue(
                    (customControlOut.Value + 1) / 2,
                    inputFlowEURange);

                double error = flowController.UpdateMeasurement(inputFlow);

                // adjust the input valve.
                inputValve.Value
                    = Adjust(inputValve.Value, error > 0 ? 100 : 0, 10, null);

                // adjust the input flow.
                inputFlow.Value = Adjust(
                    inputFlow.Value,
                    flowSetPoint.Value,
                    0.6,
                    inputFlowEURange);

                // add pertubations.
                drumLevelOutput.Value
                    = RoundAndPerturb(drumLevelOutput.Value, 3);
                inputFlow.Value = RoundAndPerturb(
                    inputFlow.Value,
                    3);
                outputFlow.Value = RoundAndPerturb(
                    outputFlow.Value,
                    3);

                ClearChangeMasks(m_simulationContext, true);
            }
            catch (Exception e)
            {
                m_logger.UnexpectedErrorDuringSimulation(e);
            }
        }

        private ILogger m_logger = null!;
        private ISystemContext m_simulationContext = null!;
        private ITimer? m_simulationTimer;
    }

    internal static partial class BoilerStateLog
    {
        [LoggerMessage(
            EventId = QuickstartsServersEventIds.BoilerState + 0, Level = LogLevel.Error,
            Message = "Unexpected error during boiler simulation.")]
        public static partial void UnexpectedErrorDuringSimulation(this ILogger logger, Exception exception);
    }

}
